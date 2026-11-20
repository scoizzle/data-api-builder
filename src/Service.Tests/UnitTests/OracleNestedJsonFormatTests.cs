// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Issue #29. The builder now marks nested JSON values FORMAT JSON because the root
    /// JSON_ARRAYAGG already needed that marker to keep an inline object from being quoted
    /// as text (see the comment on the root wrapper in OracleQueryBuilder). Child
    /// JSON_ARRAYAGG(json_doc) and VALUE clauses over relationship CLOB columns are the same
    /// shape. JSON_OBJECT(*) cannot mark one column, so a select list that contains a nested
    /// relationship column uses an explicit JSON_OBJECT instead.
    ///
    /// These probes are the live statements that would confirm the wire shape on 19c and 23c.
    /// They are not executed here: this workspace has no Oracle database. GraphQL
    /// ResolveObject/ResolveList reparses a stringified object or array, so a passing GraphQL
    /// test does not prove the driver returned JsonValueKind.Object/Array. Capturing the raw
    /// JsonDocument requires running the generated SQL through the Oracle executor.
    ///
    /// Not a test class: the probes are reference SQL for a live run. The shapes the builder
    /// emits are pinned by OracleQueryBuilderNestedReadTests (keyed CTE, FORMAT JSON, COALESCE,
    /// flattened CASE and the depth-1 golden).
    /// </summary>
    public static class OracleNestedJsonFormatTests
    {
        /// <summary>
        /// SQL to run on 19c and 23c. A probe that returns a JSON string (leading quote) instead
        /// of an object or array reproduces the double-encoding. The FORMAT JSON variants are the
        /// shape the builder now emits.
        /// </summary>
        public static readonly IReadOnlyList<string> LiveProbes = new string[]
        {
            "SELECT JSON_OBJECT('o' VALUE JSON_OBJECT('b' VALUE 1)) FROM dual",
            "WITH c AS (SELECT JSON_OBJECT('b' VALUE 1) d FROM dual) SELECT JSON_OBJECT('o' VALUE c.d) FROM c",
            "WITH c AS (SELECT /*+ MATERIALIZE */ JSON_OBJECT('b' VALUE 1) d FROM dual) SELECT JSON_OBJECT('o' VALUE c.d) FROM c",
            "WITH c AS (SELECT 1 k, JSON_OBJECT('b' VALUE 1) d FROM dual) SELECT JSON_ARRAYAGG(d ORDER BY 1) FROM c GROUP BY k",
            "WITH c AS (SELECT 1 k, JSON_OBJECT('b' VALUE 1) d FROM dual) SELECT JSON_ARRAYAGG(d FORMAT JSON ORDER BY 1 RETURNING CLOB) FROM c GROUP BY k",
            "SELECT JSON_OBJECT(*) FROM (SELECT 1 a, JSON_OBJECT('b' VALUE 1) d FROM dual) x",
            "SELECT JSON_OBJECT('o' VALUE CASE WHEN 1 = 1 THEN JSON_OBJECT('b' VALUE 1) ELSE NULL END FORMAT JSON) FROM dual",
            "SELECT JSON_OBJECT('o' VALUE COALESCE(JSON_OBJECT('b' VALUE 1), TO_CLOB(JSON_ARRAY())) FORMAT JSON) FROM dual"
        };
    }
}
