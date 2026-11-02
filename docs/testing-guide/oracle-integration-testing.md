# Oracle integration testing

Oracle tests are split by what they need, and the `Oracle` test category marks only the ones that need a live server.

| Kind | Examples | Category | Runs in |
| --- | --- | --- | --- |
| Pure unit tests (no database) | `UnitTests/OracleQueryBuilder*`, `OracleQueryExecutor*`, `OracleMetadataProviderTests`, `OracleSessionContextTests` | none | the default "Build and Unit Tests" job (`.pipelines/unittest-pipelines.yml`) |
| Live-database tests | `OracleTests/*`, `SqlTests/**/Oracle*ApiTests`, `Mcp/*OracleIntegrationTests` | `TestCategory("Oracle")` | `.pipelines/oracle-pipelines.yml` |

The default job runs with `TestCategory!=Oracle`, so a pure unit test tagged `Oracle` is silently dropped from the required job and a live-database test without the tag turns it red. `UnitTests/OracleTestCategoryContractTests.cs` enforces the split: add a new live-database class to its allowlist and tag it; do not tag unit tests.

## Running locally

1. Start Oracle Free with a PDB named `test` (the name in `dab-config.Oracle.json`):

   ```bash
   docker run --name oracle-db -p 1521:1521 \
     -e ORACLE_PASSWORD=awesome -e ORACLE_DATABASE=test -e ORACLE_CHARACTERSET=AL32UTF8 \
     -d gvenzl/oracle-free:23.9-slim
   ```

   Wait for `DATABASE IS READY TO USE!` in `docker logs oracle-db`. The image is amd64-only (about 2 GB).
2. Raise the cursor limit. The default `open_cursors=300` is too low for the suite (see [oracle-support.md](../design/oracle-support.md)):

   ```bash
   docker exec oracle-db bash -c "printf 'ALTER SYSTEM SET open_cursors=1500 SCOPE=BOTH;\nexit;\n' | sqlplus -s system/awesome@//localhost:1521/test"
   ```

3. Run the live tests; the connection string in `src/Service.Tests/dab-config.Oracle.json` already matches the container above:

   ```bash
   dotnet test src/Service.Tests/Azure.DataApiBuilder.Service.Tests.csproj --filter "TestCategory=Oracle"
   ```

Pure unit tests need no database: `dotnet test --filter "TestCategory!=Oracle&FullyQualifiedName~Oracle"`.

## CI

`.pipelines/oracle-pipelines.yml` starts the same image (pinned by digest) with a generated password, waits for the PDB to accept SQL (retrying the container once and dumping its log on failure), sets and asserts `open_cursors=1500`, writes the connection string into the built `dab-config.Oracle.json` with `FileTransform`, and runs `TestCategory=Oracle`.

- Live classes that cannot reach Oracle in CI fail instead of skipping (`TF_BUILD`/`CI` set).
- Ignored Oracle tests are capped offline by `OracleSkipPolicyTests` against `tests/oracle-skip-baseline.json`. The ceiling only shrinks.
- `scripts/check-test-budget.ps1` can compare a measured TRX with `tests/oracle-test-budget.json`. It is not a pipeline step. The checked-in counts have not been measured, and wiring them in would fail the first real run. After a green TRX, replace the counts with the measured ones and add the step.
- Coverage is published as `coverage-oracle`. The total-coverage job downloads a database pipeline only by its Azure DevOps definition ID. Add Oracle there after that ID exists. Do not reserve one.
- Set the pipeline parameter `oracleEnabled` to `false` to confirm the readiness step fails with the container log when no database is running.

### Required check (Azure DevOps configuration)

The pipeline is an Azure DevOps build, not a GitHub check. Creating it from `.pipelines/oracle-pipelines.yml` and marking it required are organization settings, done after the pipeline has run green. Oracle Free startup is the main flake risk.
