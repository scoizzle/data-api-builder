#!/usr/bin/env python3
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

"""Oracle DAB stress/demo battery — GraphQL + REST.

Starts a DAB engine against an Oracle-backed config, runs a battery of
GraphQL and REST requests (filters, pagination, nested relationships,
M:N traversals, aggregations, mutations, error codes), cleans up the rows
it created, and stops the engine. Exits non-zero on any failure.

Usage:
    python3 scripts/oracle-stress-test.py [--engine PATH] [--config PATH] [--port N]

Defaults:
    --engine  Uses the locally built CLI binary if present, else `dab` on PATH.
    --config  Uses the star-trek demo config if present, else `dab-config.json` in CWD.
    --port    5001 (port 5000 can be claimed by macOS Control Center / AirPlay).

Expected data (demo schema): 7 series, 36 actors, 36 characters, 12 species
with series_character / character_species M:N links.

Known engine-wide limitations asserted as current behavior (not Oracle-specific):
- OData $filter string functions (contains/startswith) are unsupported -> 400
- REST $expand is not implemented -> 400
- by-PK / delete of a missing item returns null data; REST by-PK of a missing
  item returns 200 with an empty value array.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request

BUILT_ENGINE = ("/Users/scoizzle/Projects/data-api-builder/src/out/cli/net10.0/"
                "Microsoft.DataApiBuilder")
STAR_TREK_CONFIG = "/Users/scoizzle/Projects/star-trek-api/dab-config.json"

results = []  # (category, name, ok, detail)
created_ids = []  # characters created by the battery, cleaned up at the end


def gql(query, base):
    body = json.dumps({"query": query}).encode()
    req = urllib.request.Request(f"{base}/graphql/", data=body,
                                 headers={"Content-Type": "application/json"})
    try:
        return json.loads(urllib.request.urlopen(req).read())
    except urllib.error.HTTPError as e:
        try:
            return {"http_error": e.code, "body": json.loads(e.read())}
        except Exception:
            return {"http_error": e.code, "body": {}}
    except Exception as e:
        return {"http_error": str(e)}


def rest(method, path, base, body=None):
    data = json.dumps(body).encode() if body is not None else None
    url = path if path.startswith("http") else f"{base}{path}"
    req = urllib.request.Request(url, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    try:
        resp = urllib.request.urlopen(req)
        return resp.status, json.loads(resp.read()) if resp.status != 204 else None
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, {}
    except Exception as e:
        return "?", {}


def ok(category, name, cond, detail=""):
    results.append((category, name, bool(cond), detail))
    return bool(cond)


def gql_ok(r):
    return isinstance(r, dict) and "data" in r and not r.get("errors") and "http_error" not in r


def gql_err(r):
    return isinstance(r, dict) and ("errors" in r or "http_error" in r)


def run(base, engine, config):
    subprocess.run(["pkill", "-f", "Microsoft.DataApiBuilder"], capture_output=True)
    time.sleep(1)
    env = {**os.environ, "ASPNETCORE_URLS": base}
    log = open("/tmp/dab_stress.log", "w")
    engine_proc = subprocess.Popen([engine, "start", "--config", config],
                                   stdout=log, stderr=subprocess.STDOUT, env=env)
    ready = False
    for _ in range(60):
        try:
            urllib.request.urlopen(f"{base}/graphql/", timeout=2)
            ready = True
            break
        except urllib.error.HTTPError:
            ready = True
            break
        except Exception:
            time.sleep(1)
    if not ready:
        print("ENGINE DID NOT START (see /tmp/dab_stress.log)")
        engine_proc.kill()
        return False

    # ---------- GraphQL queries ----------
    r = gql("{ series { items { id name } } }", base)
    ok("GQL", "list series", gql_ok(r) and len(r["data"]["series"]["items"]) == 7,
       json.dumps(r)[:120])

    r = gql("{ characters { items { id name } } }", base)
    ok("GQL", "list characters", gql_ok(r) and len(r["data"]["characters"]["items"]) == 36,
       json.dumps(r)[:120])

    r = gql("{ character_by_pk(id: 1) { id name } }", base)
    ok("GQL", "by_pk", gql_ok(r) and r["data"]["character_by_pk"]["name"] == "James T. Kirk",
       json.dumps(r)[:150])

    r = gql("{ character_by_pk(id: 999999) { id } }", base)
    ok("GQL", "by_pk missing -> null data", gql_ok(r) and r["data"]["character_by_pk"] is None,
       json.dumps(r)[:150])

    r = gql('{ characters(first: 3) { items { id } endCursor hasNextPage } }', base)
    ok("GQL", "pagination first:3", gql_ok(r) and len(r["data"]["characters"]["items"]) == 3
       and r["data"]["characters"]["hasNextPage"] is True, json.dumps(r)[:200])
    cursor = r["data"]["characters"]["endCursor"] if gql_ok(r) else None
    if cursor:
        r2 = gql(f'{{ characters(first: 3, after: "{cursor}") {{ items {{ id }} }} }}', base)
        ids2 = [i["id"] for i in r2["data"]["characters"]["items"]] if gql_ok(r2) else []
        ok("GQL", "pagination after cursor", gql_ok(r2) and ids2 == [4, 5, 6],
           json.dumps(r2)[:200])

    r = gql('{ characters(orderBy: { name: DESC }) { items { name } } }', base)
    names = [i["name"] for i in r["data"]["characters"]["items"]] if gql_ok(r) else []
    ok("GQL", "orderBy DESC", gql_ok(r) and names == sorted(names, reverse=True),
       json.dumps(r)[:150])

    r = gql('{ characters(orderBy: { name: ASC, id: DESC }) { items { id name } } }', base)
    items = r["data"]["characters"]["items"] if gql_ok(r) else []
    ok("GQL", "orderBy multi-column", gql_ok(r) and items == sorted(
        items, key=lambda i: (i["name"], -i["id"])), json.dumps(r)[:150])

    r = gql('{ characters(filter: { name: { eq: "Spock" } }) { items { id name } } }', base)
    ok("GQL", "filter eq", gql_ok(r) and r["data"]["characters"]["items"][0]["name"] == "Spock",
       json.dumps(r)[:150])

    r = gql("{ characters(filter: { id: { gt: 30 } }) { items { id } } }", base)
    ids = [i["id"] for i in r["data"]["characters"]["items"]] if gql_ok(r) else []
    ok("GQL", "filter gt", gql_ok(r) and len(ids) == 7 and all(i > 30 for i in ids),
       json.dumps(r)[:150])

    r = gql("{ characters(filter: { id: { gte: 30 } }) { items { id } } }", base)
    ids = [i["id"] for i in r["data"]["characters"]["items"]] if gql_ok(r) else []
    ok("GQL", "filter gte", gql_ok(r) and len(ids) == 8 and all(i >= 30 for i in ids),
       json.dumps(r)[:150])

    r = gql("{ characters(filter: { id: { lte: 4 } }) { items { id } } }", base)
    ids = [i["id"] for i in r["data"]["characters"]["items"]] if gql_ok(r) else []
    ok("GQL", "filter lte", gql_ok(r) and ids == [1, 2, 3, 4], json.dumps(r)[:150])

    r = gql('{ characters(filter: { id: { in: [1, 2, 3] } }) { items { id } } }', base)
    ids = [i["id"] for i in r["data"]["characters"]["items"]] if gql_ok(r) else []
    ok("GQL", "filter in", gql_ok(r) and ids == [1, 2, 3], json.dumps(r)[:150])

    r = gql('{ characters(filter: { name: { contains: "Kirk" } }) { items { id } } }', base)
    ok("GQL", "filter contains", gql_ok(r) and len(r["data"]["characters"]["items"]) == 1,
       json.dumps(r)[:150])

    r = gql('{ characters(filter: { name: { startsWith: "J" } }) { items { id } } }', base)
    ok("GQL", "filter startsWith", gql_ok(r) and len(r["data"]["characters"]["items"]) >= 3,
       json.dumps(r)[:150])

    r = gql('{ characters(filter: { stardate: { isNull: false } }) { items { id } } }', base)
    ok("GQL", "filter isNull:false", gql_ok(r) and len(r["data"]["characters"]["items"]) == 36,
       json.dumps(r)[:150])

    r = gql('{ characters(filter: { and: [{ name: { eq: "Spock" } }, { actorid: { eq: 2 } }] }) { items { id } } }', base)
    ok("GQL", "filter and", gql_ok(r) and len(r["data"]["characters"]["items"]) == 1,
       json.dumps(r)[:150])

    r = gql('{ characters(filter: { or: [{ name: { eq: "Spock" } }, { name: { eq: "James T. Kirk" } }] }) { items { id } } }', base)
    ok("GQL", "filter or", gql_ok(r) and len(r["data"]["characters"]["items"]) == 2,
       json.dumps(r)[:150])

    r = gql('{ characters(filter: { name: { eq: "Spock" } }) { items { id actor { id name } } } }', base)
    ok("GQL", "nested 1:1 character->actor", gql_ok(r)
       and r["data"]["characters"]["items"][0]["actor"]["name"] == "Leonard Nimoy",
       json.dumps(r)[:150])

    r = gql('{ characters(filter: { name: { eq: "Spock" } }) { items { name species { items { name } } } } }', base)
    sp = [s["name"] for s in r["data"]["characters"]["items"][0]["species"]["items"]] if gql_ok(r) else []
    ok("GQL", "nested M:N characters->species", gql_ok(r) and sorted(sp) == ["Human", "Vulcan"],
       json.dumps(r)[:150])

    r = gql('{ series(filter: { id: { eq: 1 } }) { items { id characters { items { id } } } } }', base)
    ok("GQL", "nested M:N series->characters", gql_ok(r)
       and len(r["data"]["series"]["items"][0]["characters"]["items"]) == 7, json.dumps(r)[:150])

    r = gql('{ series { items { id name characters { items { id name actor { id name } species { items { id name } } } } } } }', base)
    ok("GQL", "3-level series->characters->actor+species", gql_ok(r)
       and r["data"]["series"]["items"][0]["characters"]["items"][0]["actor"]["name"] == "William Shatner",
       json.dumps(r)[:150])

    r = gql('{ species(filter: { name: { eq: "Vulcan" } }) { items { name characters { items { name } } } } }', base)
    ok("GQL", "reverse M:N species->characters", gql_ok(r)
       and len(r["data"]["species"]["items"][0]["characters"]["items"]) >= 2, json.dumps(r)[:150])

    r = gql('{ characters(filter: { actor: { name: { eq: "Leonard Nimoy" } } }) { items { name } } }', base)
    ok("GQL", "nested filter character.actor.name", gql_ok(r)
       and r["data"]["characters"]["items"][0]["name"] == "Spock", json.dumps(r)[:150])

    r = gql('{ series(filter: { characters: { actor: { name: { eq: "Patrick Stewart" } } } }) { items { name } } }', base)
    ok("GQL", "2-level nested filter series.characters.actor", gql_ok(r)
       and r["data"]["series"]["items"][0]["name"] == "Star Trek: The Next Generation", json.dumps(r)[:150])

    r = gql('{ characters(filter: { species: { name: { eq: "Vulcan" } } }) { items { name } } }', base)
    ok("GQL", "nested filter characters.species.name", gql_ok(r)
       and len(r["data"]["characters"]["items"]) >= 2, json.dumps(r)[:150])

    r = gql('{ series { groupBy { aggregations { count(field: id) max(field: id) min(field: id) sum(field: id) } } } }', base)
    agg = r["data"]["series"]["groupBy"][0]["aggregations"] if gql_ok(r) else {}
    ok("GQL", "aggregation-only (count/max/min/sum)", gql_ok(r)
       and agg.get("count") == 7 and agg.get("max") == 3002 and agg.get("min") == 1,
       json.dumps(r)[:200])

    r = gql('{ species { groupBy(fields: [id]) { fields { id } aggregations { count(field: id) } } } }', base)
    rows = r["data"]["species"]["groupBy"] if gql_ok(r) else []
    ok("GQL", "groupBy with fields", gql_ok(r) and len(rows) == 12 and rows[0]["fields"]["id"] == 1,
       json.dumps(r)[:200])

    # ---------- GraphQL mutations ----------
    r = gql('mutation { createcharacter(item: { id: 95001, name: "Stress One", actorid: 1 }) { id name } }', base)
    ok("GQL", "create character", gql_ok(r) and r["data"]["createcharacter"]["id"] == 95001,
       json.dumps(r)[:150])
    created_ids.append(95001)

    r = gql('mutation { createcharacter(item: { name: "No Id", actorid: 1 }) { id } }', base)
    ok("GQL", "create without PK -> error", gql_err(r), json.dumps(r)[:150])

    r = gql('mutation { createcharacter(item: { id: 95001, name: "Dup", actorid: 1 }) { id } }', base)
    ok("GQL", "create duplicate PK -> error", gql_err(r), json.dumps(r)[:150])

    r = gql('mutation { updatecharacter(id: 95001, item: { name: "Stress One Renamed" }) { id name } }', base)
    ok("GQL", "update character", gql_ok(r) and r["data"]["updatecharacter"]["name"] == "Stress One Renamed",
       json.dumps(r)[:150])

    r = gql('mutation { updatecharacter(id: 999999, item: { name: "x" }) { id } }', base)
    ok("GQL", "update missing -> error", gql_err(r), json.dumps(r)[:150])

    r = gql('mutation { deletecharacter(id: 95001) { id } }', base)
    ok("GQL", "delete character", gql_ok(r) and r["data"]["deletecharacter"]["id"] == 95001,
       json.dumps(r)[:150])
    created_ids.remove(95001)

    r = gql('mutation { deletecharacter(id: 999999) { id } }', base)
    ok("GQL", "delete missing -> null data", gql_ok(r) and r["data"]["deletecharacter"] is None,
       json.dumps(r)[:150])

    r = gql('mutation { createseries(item: { id: 95010, name: "Stress Series" }) { id name } }', base)
    ok("GQL", "create series", gql_ok(r), json.dumps(r)[:150])
    created_ids.append(95010)
    r = gql('mutation { deleteseries(id: 95010) { id } }', base)
    ok("GQL", "delete series", gql_ok(r), json.dumps(r)[:150])
    created_ids.remove(95010)

    r = gql('mutation { createactor(item: { id: 95020, name: "Stress Actor", birthyear: 2000 }) { id name } }', base)
    ok("GQL", "create actor (with nullable col)", gql_ok(r), json.dumps(r)[:150])
    created_ids.append(95020)
    r = gql('mutation { updateactor(id: 95020, item: { birthyear: 2001 }) { id birthyear } }', base)
    ok("GQL", "update actor nullable col", gql_ok(r)
       and r["data"]["updateactor"]["birthyear"] == 2001, json.dumps(r)[:150])
    r = gql('mutation { deleteactor(id: 95020) { id } }', base)
    ok("GQL", "delete actor", gql_ok(r), json.dumps(r)[:150])
    created_ids.remove(95020)

    r = gql('mutation { createspecy(item: { id: 95030, name: "Stress Species" }) { id name } }', base)
    ok("GQL", "create species", gql_ok(r), json.dumps(r)[:150])
    created_ids.append(95030)
    r = gql('mutation { deletespecy(id: 95030) { id } }', base)
    ok("GQL", "delete species", gql_ok(r), json.dumps(r)[:150])
    created_ids.remove(95030)

    # ---------- REST ----------
    s, r = rest("GET", "/api/characters", base)
    ok("REST", "GET list", s == 200 and len(r.get("value", [])) == 36, f"{s} {json.dumps(r)[:120]}")

    s, r = rest("GET", "/api/characters/id/1", base)
    ok("REST", "GET by PK", s == 200 and r["value"][0]["name"] == "James T. Kirk",
       f"{s} {json.dumps(r)[:150]}")

    s, r = rest("GET", "/api/characters/id/999999", base)
    ok("REST", "GET missing PK -> 200 empty value", s == 200 and r.get("value") == [],
       f"{s} {json.dumps(r)[:120]}")

    s, r = rest("POST", "/api/characters", base, {"id": 95002, "name": "REST Stress", "actorid": 1})
    ok("REST", "POST create -> 201", s == 201 and r["value"][0]["id"] == 95002,
       f"{s} {json.dumps(r)[:150]}")
    created_ids.append(95002)

    s, r = rest("POST", "/api/characters", base, {"id": 95002, "name": "Dup", "actorid": 1})
    ok("REST", "POST duplicate -> 409", s == 409, f"{s} {json.dumps(r)[:120]}")

    s, r = rest("POST", "/api/characters", base, {"name": "No Id", "actorid": 1})
    ok("REST", "POST missing PK -> 400", s == 400, f"{s} {json.dumps(r)[:120]}")

    s, r = rest("POST", "/api/characters", base,
                {"id": 95004, "name": "With Date", "actorid": 1, "stardate": 2500.5})
    ok("REST", "POST with nullable numeric", s == 201 and r["value"][0]["stardate"] == 2500.5,
       f"{s} {json.dumps(r)[:150]}")
    created_ids.append(95004)

    s, r = rest("PATCH", "/api/characters/id/95002", base, {"name": "REST Stress 2"})
    ok("REST", "PATCH update -> 200", s == 200 and r["value"][0]["name"] == "REST Stress 2",
       f"{s} {json.dumps(r)[:150]}")

    s, r = rest("PATCH", "/api/characters/id/999998", base, {"name": "x"})
    ok("REST", "PATCH missing -> 404", s == 404, f"{s} {json.dumps(r)[:120]}")

    s, r = rest("PUT", "/api/characters/id/95003", base,
                {"id": 95003, "name": "PUT Created", "actorid": 1})
    ok("REST", "PUT upsert (insert) -> 201", s == 201, f"{s} {json.dumps(r)[:150]}")
    created_ids.append(95003)

    s, r = rest("PUT", "/api/characters/id/95003", base,
                {"id": 95003, "name": "PUT Updated", "actorid": 1})
    ok("REST", "PUT upsert (update) -> 200", s == 200 and r["value"][0]["name"] == "PUT Updated",
       f"{s} {json.dumps(r)[:150]}")

    s, r = rest("DELETE", "/api/characters/id/95002", base)
    ok("REST", "DELETE -> 204", s == 204, f"{s}")
    created_ids.remove(95002)

    s, r = rest("DELETE", "/api/characters/id/999997", base)
    ok("REST", "DELETE missing -> 404", s == 404, f"{s} {json.dumps(r)[:120]}")

    q = urllib.parse.quote("name eq 'Spock'")
    s, r = rest("GET", f"/api/characters?$filter={q}", base)
    ok("REST", "$filter eq", s == 200 and r["value"][0]["name"] == "Spock",
       f"{s} {json.dumps(r)[:150]}")

    q = urllib.parse.quote("id gt 30 and id lt 100")
    s, r = rest("GET", f"/api/characters?$filter={q}", base)
    ok("REST", "$filter gt (bounded)", s == 200 and len(r["value"]) == 7,
       f"{s} {json.dumps(r)[:150]}")

    q = urllib.parse.quote("name eq 'Spock' and actorid eq 2")
    s, r = rest("GET", f"/api/characters?$filter={q}", base)
    ok("REST", "$filter and", s == 200 and len(r["value"]) == 1, f"{s} {json.dumps(r)[:150]}")

    q = urllib.parse.quote("name eq 'Spock' or name eq 'Uhura'")
    s, r = rest("GET", f"/api/characters?$filter={q}", base)
    ok("REST", "$filter or", s == 200 and len(r["value"]) == 2, f"{s} {json.dumps(r)[:150]}")

    q = urllib.parse.quote("name desc")
    s, r = rest("GET", f"/api/characters?$orderby={q}", base)
    names = [v["name"] for v in r.get("value", [])]
    ok("REST", "$orderby desc", s == 200 and names == sorted(names, reverse=True),
       f"{s} {json.dumps(r)[:150]}")

    q = urllib.parse.quote("name asc, id desc")
    s, r = rest("GET", f"/api/characters?$orderby={q}", base)
    ok("REST", "$orderby multi-column", s == 200, f"{s} {json.dumps(r)[:150]}")

    s, r = rest("GET", "/api/characters?$select=id,name", base)
    ok("REST", "$select", s == 200 and set(r["value"][0].keys()) == {"id", "name"},
       f"{s} {json.dumps(r)[:150]}")

    s, r = rest("GET", "/api/characters?$first=3", base)
    ok("REST", "$first=3 + nextLink", s == 200 and len(r["value"]) == 3 and bool(r.get("nextLink")),
       f"{s} {json.dumps(r)[:150]}")
    if s == 200 and r.get("nextLink"):
        s2, r2 = rest("GET", r["nextLink"], base)
        ok("REST", "cursor page 2", s2 == 200 and len(r2["value"]) == 3 and r2["value"][0]["id"] == 4,
           f"{s2} {json.dumps(r2)[:150]}")

    # Known engine-wide limitations (not Oracle-specific): assert current behavior
    q = urllib.parse.quote("contains(name, 'Kirk')")
    s, _ = rest("GET", f"/api/characters?$filter={q}", base)
    ok("REST", "$filter string fn unsupported -> 400 (fork-wide)", s == 400, f"{s}")
    s, _ = rest("GET", "/api/series?$expand=characters", base)
    ok("REST", "$expand unsupported -> 400 (fork-wide)", s == 400, f"{s}")

    # ---------- summary ----------
    passed = sum(1 for _, _, ok_, _ in results if ok_)
    total = len(results)
    print(f"\n{'=' * 70}")
    print(f"Oracle stress battery: {passed}/{total} passed")
    print(f"{'=' * 70}")
    for cat, name, ok_, detail in results:
        mark = "PASS" if ok_ else "FAIL"
        line = f"[{mark}] {cat:6s} {name}"
        if not ok_ and detail:
            line += f"  -> {detail}"
        print(line)
    return passed == total


def main():
    parser = argparse.ArgumentParser(description="Oracle DAB stress/demo battery")
    parser.add_argument("--engine", default=BUILT_ENGINE if os.path.exists(BUILT_ENGINE) else "dab",
                        help="DAB CLI binary (default: built CLI or `dab` on PATH)")
    parser.add_argument("--config",
                        default=STAR_TREK_CONFIG if os.path.exists(STAR_TREK_CONFIG) else "dab-config.json",
                        help="DAB runtime config (default: star-trek demo config)")
    parser.add_argument("--port", type=int, default=5001,
                        help="Engine port (default 5001; 5000 can be claimed by macOS Control Center)")
    args = parser.parse_args()

    base = f"http://localhost:{args.port}"
    print(f"Engine : {args.engine}")
    print(f"Config : {args.config}")
    print(f"Base   : {base}")

    try:
        success = run(base, args.engine, args.config)
    finally:
        for cid in created_ids:
            try:
                rest("DELETE", f"/api/characters/id/{cid}", base)
            except Exception:
                pass
        subprocess.run(["pkill", "-f", "Microsoft.DataApiBuilder"], capture_output=True)
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()