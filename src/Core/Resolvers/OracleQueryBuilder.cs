// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Text;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Oracle
    /// </summary>
    public class OracleQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        public const string UPSERT_IDENTIFIER_COLUMN_NAME = "___upsert_op___";
        private const string INSERT_UPSERT = "inserted";
        private const string UPDATE_UPSERT = "updated";
        public const string COUNT_ROWS_WITH_GIVEN_PK = "cnt_rows_to_update";
        public const string IS_FALLBACK_TO_UPDATE = "is_fallback_to_update";

        // Oracle DML statements (INSERT/UPDATE/DELETE + RETURNING) deliver their result via output
        // bind variables, NOT via the DbDataReader. ODP.NET surfaces RETURNING INTO values only in
        // the command's output parameters and the reader is empty. To keep DAB's DbDataReader-based
        // result contract intact, data-modifying statements are wrapped in a single PL/SQL anonymous
        // block:
        //   BEGIN
        //     <DML> RETURNING <cols> INTO :o1, :o2, ...;
        //     OPEN :dab_result FOR SELECT <cols> FROM DUAL;
        //   END;
        // The :dab_result REF CURSOR then yields one row that ExtractResultSetFromDbDataReaderAsync
        // can consume. (A literal can NOT appear in Oracle's RETURNING list - only column expressions
        // are allowed - which is why the upsert branch indicator is emitted via the SELECT clause of
        // the REF CURSOR instead.)
        internal const string RESULT_CURSOR_PARAM_NAME = "dab_result";

        private static DbCommandBuilder _builder = new OracleCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Quotes a physical column reference for use in raw SQL fragments (OData filters,
        /// predicate operands). Callers pass names already resolved to the PHYSICAL backing
        /// casing preserved from Oracle metadata, so no case transformation is applied - a
        /// quoted lowercase/mixed-case column resolves exactly as Oracle stores it.
        /// </summary>
        /// <inheritdoc />
        public override string QuotePhysicalColumn(string columnName)
        {
            return QuoteIdentifier(columnName);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            // The FROM-clause table alias is DAB-generated ("table{N}", lowercase) and emitted
            // UPPERCASE here, matching the UPPERCASE alias emitted by Build(Column) for column
            // references. The table/schema names come from the config and resolve against Oracle
            // case-insensitively; they are emitted UPPERCASE (the physical casing for unquoted
            // objects). Column names are physical backing names preserved from metadata and are
            // emitted verbatim by Build(Column).
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())} " +
                             $"{QuoteIdentifier(structure.SourceAlias.ToUpperInvariant())}{Build(structure.Joins)}";
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) {QuoteIdentifier(x.Key.ToUpperInvariant())} ON (1=1)"));

            string predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));

            string aggregations = BuildAggregationColumns(structure);

            string query = $"SELECT {MakeSelectColumns(structure)}{aggregations}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + BuildGroupBy(structure)
                + BuildHaving(structure)
                + $" ORDER BY {Build(structure.OrderByColumns)}"
                + $" OFFSET 0 ROWS FETCH NEXT {structure.Limit()} ROWS ONLY";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            StringBuilder result = new();
            if (structure.IsListQuery)
            {
                // JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB) avoids ORA-40478 ("output value too
                // large, maximum: 4000") when nested/aggregated JSON exceeds 4000 bytes. The empty
                // fallback JSON_ARRAY() is TO_CLOB-wrapped so COALESCE operands share the CLOB type.
                result.Append($"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB(JSON_ARRAY())) ");
            }
            else
            {
                // Oracle rejects RETURNING CLOB directly on the wildcard scalar form
                // JSON_OBJECT(*) (ORA-00923), so wrap it in TO_CLOB instead.
                result.Append($"SELECT TO_CLOB(JSON_OBJECT(*)) ");
            }

            result.Append($"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} FROM ( ");
            result.Append(query);
            result.Append($" ) {subqueryName}");

            return result.ToString();
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            // PRE-CONDITION: Get database policy for CREATE action (required for row-level security)
            string dbPolicyPredicates = JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Create));
            
            // PRE-CONDITION: Get source definition for DML trigger detection
            SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();
            bool isInsertDMLTriggerEnabled = sourceDefinition.IsInsertDMLTriggerEnabled;

            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())}";
            string insertQuery = $"INSERT INTO {tableName} ";
            
            if (structure.InsertColumns.Any())
            {
                string insertColumns = BuildColumnList(structure.InsertColumns);
                insertQuery += $"({insertColumns}) ";
                insertQuery += $"VALUES ({string.Join(", ", structure.Values)}) ";
            }
            else
            {
                // Oracle does not support the SQL Server/PostgreSQL DEFAULT VALUES syntax.
                // Insert one defaulted or identity column explicitly instead.
                if (!dbPolicyPredicates.Equals(BASE_PREDICATE))
                {
                    throw new DataApiBuilderException(
                        "INSERT with defaulted values cannot be used when row-level security policies are defined",
                        System.Net.HttpStatusCode.BadRequest,
                        DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                }

                string? defaultColumn = sourceDefinition.Columns
                    .Where(pair => pair.Value.HasDefault || pair.Value.IsAutoGenerated)
                    .Select(pair => pair.Key)
                    .FirstOrDefault();

                if (defaultColumn is null)
                {
                    throw new DataApiBuilderException(
                        "INSERT requires at least one defaulted or identity column when no values are provided",
                        System.Net.HttpStatusCode.BadRequest,
                        DataApiBuilderException.SubStatusCodes.DatabaseInputError);
                }

                insertQuery += $"({QuoteIdentifier(defaultColumn)}) VALUES (DEFAULT) ";
            }

            // POST-CONDITION: Handle DML trigger scenario (Oracle-specific, not yet fully implemented)
            if (isInsertDMLTriggerEnabled)
            {
                // Note: Oracle supports DML triggers but the full trigger-aware logic
                // (similar to MSSQL with temp tables) is not yet implemented.
                // For now, we log a warning and continue with the standard INSERT.
                // TODO: Implement full DML trigger-aware INSERT logic for Oracle
            }

            // POST-CONDITION: Return inserted data. Oracle's RETURNING INTO delivers values through
            // output bind variables (the DbDataReader is empty), so wrap the statement in a PL/SQL
            // block and expose the returned columns as a REF CURSOR result set that DAB's reader can
            // consume:
            //   BEGIN
            //     INSERT INTO ... RETURNING "ID", "TITLE" INTO :id, :title;
            //     OPEN :dab_result FOR SELECT :id AS "id", :title AS "title" FROM DUAL;
            //   END;
            //
            // NOTE: Oracle's RETURNING clause rejects aliases (ORA-00925 "missing INTO keyword"
            // when an AS alias appears before INTO), so the output column list is the bare
            // PHYSICAL column name (exact case preserved from Oracle metadata).
            string returningColumns = string.Join(", ", structure.OutputColumns.Select(c => QuoteIdentifier(c.ColumnName)));
            string bindNames = string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label));
            string selectFromBinds = string.Join(", ", structure.OutputColumns.Select(c => $":{c.Label} AS {QuoteIdentifier(c.Label)}"));
            string outputTypeHints = BuildOutputTypeHints(structure.OutputColumns, sourceDefinition);

            bool hasCreatePolicy = !dbPolicyPredicates.Equals(BASE_PREDICATE);
            if (hasCreatePolicy)
            {
                // Gate the INSERT on the create policy: when blocked, open an empty cursor
                // (WHERE 1 = 0) so the executor surfaces 403 rather than ORA-01400 (400).
                // The policy may reference column names (e.g. "OWNERID" = :paramN via
                // QuotePhysicalColumn), so alias each value with its physical column name in a
                // DUAL subquery to make those columns resolvable - otherwise ORA-00904.
                string namedValues = string.Join(", ", structure.InsertColumns.Zip(structure.Values,
                    (col, val) => $"{val} AS {QuoteIdentifier(col)}"));
                return $"{outputTypeHints}BEGIN " +
                    $"IF (SELECT COUNT(*) FROM (SELECT {namedValues} FROM DUAL) WHERE {dbPolicyPredicates}) > 0 THEN " +
                    $"{insertQuery} RETURNING {returningColumns} INTO {bindNames}; " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; " +
                    $"ELSE " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL WHERE 1 = 0; " +
                    $"END IF; " +
                    $"END;";
            }

            return $"{outputTypeHints}BEGIN {insertQuery} RETURNING {returningColumns} INTO {bindNames}; " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; END;";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string predicates = JoinPredicateStrings(
                                   structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                                   Build(structure.Predicates));

            // The RETURNING column list must be bare (no aliases - ORA-00925); column names carry the
            // exact physical casing preserved from Oracle metadata.
            string returningColumns = string.Join(", ", structure.OutputColumns.Select(c => QuoteIdentifier(c.ColumnName)));
            string bindNames = string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label));
            string updateQuery = $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {predicates} " +
                    $"RETURNING {returningColumns} INTO {bindNames}";

            string selectFromBinds = string.Join(", ", structure.OutputColumns.Select(c => $":{c.Label} AS {QuoteIdentifier(c.Label)}"));
            string outputTypeHints = BuildOutputTypeHints(structure.OutputColumns, structure.GetUnderlyingSourceDefinition());

            // When the UPDATE matches no row (record absent, or the update database policy blocks it),
            // RETURNING INTO never fires and the output binds stay NULL. Opening the cursor
            // unconditionally would fabricate a row of all-NULL columns, which the mutation engine
            // treats as a successful update. Mirror the upsert builder: open an EMPTY cursor when
            // SQL%ROWCOUNT is 0 so a no-match update surfaces as "item not found" (or a policy
            // failure), matching the behavior of the other database engines.
            return $"{outputTypeHints}BEGIN {updateQuery}; " +
                $"IF SQL%ROWCOUNT > 0 THEN " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; " +
                $"ELSE " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL WHERE 1 = 0; " +
                $"END IF; " +
                $"END;";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            string predicates = JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Delete),
                       Build(structure.Predicates));

            return $"DELETE FROM {QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())} " +
                    $"WHERE {predicates}";
        }

        /// <summary>
        /// Builds an Oracle-compatible stored procedure execution query.
        /// "EXEC" is SQL*Plus client syntax and is invalid for ODP.NET CommandType.Text
        /// (ORA-00900), so the subprogram is invoked from within a PL/SQL anonymous block.
        /// Oracle subprograms return result sets through a SYS_REFCURSOR (a procedure's OUT
        /// parameter, or a function's RETURN value), which ODP.NET does NOT surface in the
        /// DbDataReader on its own. We therefore pass a trailing ":dab_result" OUT bind
        /// (registered as a RefCursor by <see cref="OracleBindRegistrar"/>) whose rows ODP.NET
        /// exposes as the reader result set - the same mechanism the DML paths rely on.
        ///
        /// This also supports subprograms that live inside a package (source "schema.package.sub")
        /// and standalone or packaged FUNCTIONS:
        ///  - Package procedure:  BEGIN "S"."P"."SUB"(:p0, :dab_result); END;
        ///  - Package function returning a cursor:
        ///      BEGIN :dab_result := "S"."P"."SUB"(:p0); END;
        ///  - Scalar function (no cursor): invoked through SELECT ... FROM DUAL and read as a row.
        /// </summary>
        public string Build(SqlExecuteStructure structure)
        {
            DatabaseStoredProcedure sp = (DatabaseStoredProcedure)structure.DatabaseObject;

            // NOTE: sp.IsFunction is NOT parsed from config; it is populated during metadata
            // discovery (OracleMetadataProvider.FillSchemaForStoredProcedureAsync detects the
            // ALL_ARGUMENTS POSITION 0 row that marks a function's RETURN value). This Build method
            // therefore relies on that discovery having completed first - an ordering invariant of
            // the startup pipeline. If it were ever skipped, IsFunction would remain false and a
            // function would be emitted as a bare `BEGIN schema.func(:p0); END;` statement, which
            // is invalid PL/SQL for a function.
            string schemaName = QuoteIdentifier(sp.SchemaName.ToUpperInvariant());
            string subprogramName = QuoteIdentifier(sp.Name.ToUpperInvariant());
            string qualifiedName = string.IsNullOrEmpty(sp.PackageName)
                ? $"{schemaName}.{subprogramName}"
                : $"{schemaName}.{QuoteIdentifier(sp.PackageName.ToUpperInvariant())}.{subprogramName}";

            // ProcedureParameters maps each subprogram argument NAME (no prefix, e.g. "id") to the
            // engine-generated bind reference (e.g. "@param0"). The actual bindable values live in
            // structure.Parameters keyed by those "@paramN" names, so the call must reference the
            // VALUES (not the keys). Emitting the keys (":id") would produce binds with no matching
            // DbConnectionParam, causing PrepareDbCommand to silently drop the real parameters and
            // Oracle to raise ORA-01008 (not all variables bound).
            List<string> callArgs = structure.ProcedureParameters.Values
                .Select(v => $":{v.ToString()!.TrimStart('@')}")
                .ToList();

            // StoredProcedureDefinition.Columns holds the result-set definition, populated from the
            // subprogram's OUT/IN_OUT arguments / RETURN value (see BuildStoredProcedureResultDetailsQuery).
            // A non-empty Columns dictionary signals that the subprogram yields a REF CURSOR result
            // set we must capture by appending/assigning the shared RefCursor OUT bind.
            bool returnsCursor = structure.GetUnderlyingSourceDefinition().Columns.Values
                .Any(column => column.SystemType == typeof(IDataReader));

            if (sp.IsFunction)
            {
                // A function's result (even if it is a REF CURSOR) is expressed as its RETURN
                // value, so it cannot be called as a bare statement. Assign the RETURN into either
                // the REF CURSOR output bind (result-set function) or a scalar bind (scalar function).
                if (returnsCursor)
                {
                    // BEGIN :dab_result := "S"."P"."SUB"(:p0); END;
                    return $"BEGIN :{RESULT_CURSOR_PARAM_NAME} := {qualifiedName}({JoinArgs(callArgs)}); END;";
                }

                // Scalar function - select its value from DUAL so the reader yields one row:
                // SELECT "S"."P"."SUB"(:p0) AS VALUE FROM DUAL;
                string select = $"SELECT {qualifiedName}({JoinArgs(callArgs)}) AS {QuoteIdentifier("value")} FROM DUAL";
                return select;
            }

            // Stored procedure (standalone or packaged).
            if (returnsCursor)
            {
                callArgs.Add($":{RESULT_CURSOR_PARAM_NAME}");
            }

            // Procedures with no arguments are invoked without parentheses:
            //   BEGIN schema.proc; END;
            string args = callArgs.Count > 0 ? $"({string.Join(", ", callArgs)})" : string.Empty;

            return $"BEGIN {qualifiedName}{args}; END;";
        }

        private static string JoinArgs(List<string> callArgs)
        {
            return callArgs.Count > 0 ? string.Join(", ", callArgs) : string.Empty;
        }

        public string Build(SqlUpsertQueryStructure structure)
        {
            // Oracle upserts are built as a SINGLE PL/SQL anonymous block because:
            //  1. ODP.NET does not accept multiple ';'-separated statements in one command
            //     (ORA-03405), so the Postgres-style "COUNT; UPDATE; INSERT;" batch is impossible.
            //  2. MERGE cannot return per-branch data (RETURNING only supports a single column
            //     expression list and cannot include literals).
            //  3. UPDATE/INSERT ... RETURNING delivers values through output binds, not the reader.
            //
            // The block performs the UPDATE first; if it matched no rows (SQL%ROWCOUNT = 0) it
            // performs a guarded INSERT. Each branch emits the resulting columns - plus an
            // ___upsert_op___ indicator literal - through a REF CURSOR result set that the executor
            // reads to distinguish update (200) from insert (201) and to surface policy failures.
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())}";
            string pkPredicates = Build(structure.Predicates);

            string updatePredicates = JoinPredicateStrings(pkPredicates, structure.GetDbPolicyForOperation(EntityActionOperation.Update));
            // RETURNING column list must be bare (no aliases - ORA-00925); column names carry the
            // exact physical casing preserved from Oracle metadata.
            string returningColumns = string.Join(", ", structure.OutputColumns.Select(c => QuoteIdentifier(c.ColumnName)));
            string bindNames = string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label));
            string updateQuery = $"UPDATE {tableName} " +
                $"SET {Build(structure.UpdateOperations, ", ")} " +
                $"WHERE {updatePredicates} " +
                $"RETURNING {returningColumns} " +
                $"INTO {bindNames}";

            // The REF CURSOR SELECT reads the output binds (populated by whichever branch ran).
            string selectFromBinds = string.Join(", ", structure.OutputColumns.Select(c => $":{c.Label} AS {QuoteIdentifier(c.Label)}"));
            string outputTypeHints = BuildOutputTypeHints(structure.OutputColumns, structure.GetUnderlyingSourceDefinition());

            if (structure.IsFallbackToUpdate)
            {
                // Update-only flow (e.g. autogenerated PK): no INSERT branch. When no row matched the
                // primary key + update policy, no branch ran - open an EMPTY cursor so the executor
                // reports 404 (or a policy failure) exactly like the other database engines.
                string fallbackIndicator = $"{selectFromBinds}, '{UPDATE_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                return $"{outputTypeHints}BEGIN {updateQuery}; " +
                    $"IF SQL%ROWCOUNT > 0 THEN " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {fallbackIndicator} FROM DUAL; " +
                    $"ELSE " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {fallbackIndicator} FROM DUAL WHERE 1 = 0; " +
                    $"END IF; " +
                    $"END;";
            }
            else
            {
                // INSERT only runs when the UPDATE matched no row. Two cases:
                //   1. The row EXISTS but the update policy blocked it → return 403 via an empty
                //      cursor, without leaking whether the row exists (matches Postgres/MSSQL).
                //   2. The row is ABSENT → attempt INSERT, gated by the create policy.
                // When the create policy blocks the insert, an empty cursor surfaces 403.
                //
                // Race safety: concurrent upserts for the same missing PK serialize on the primary
                // key; the loser hits ORA-00001 (unique constraint) which the exception parser maps
                // to HTTP 409, matching the behavior of the other engines' non-atomic upserts.
                string? createPolicy = structure.GetDbPolicyForOperation(EntityActionOperation.Create);
                bool hasCreatePolicy = !string.IsNullOrEmpty(createPolicy) && !createPolicy.Equals(BASE_PREDICATE);

                string insertColumns = BuildColumnList(structure.InsertColumns);
                string insertQuery = $"INSERT INTO {tableName} ({insertColumns}) " +
                    $"VALUES ({string.Join(", ", structure.Values)}) " +
                    $"RETURNING {returningColumns} " +
                    $"INTO {bindNames}";

                // Alias each value with its physical column name in a DUAL subquery so a create
                // policy that references column names (e.g. "OWNERID" = :paramN, emitted via
                // QuotePhysicalColumn) can resolve them - otherwise ORA-00904 invalid identifier.
                string namedValues = string.Join(", ", structure.InsertColumns.Zip(structure.Values,
                    (col, val) => $"{val} AS {QuoteIdentifier(col)}"));

                string insertIndicator = $"{selectFromBinds}, '{INSERT_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                string updateIndicator = $"{selectFromBinds}, '{UPDATE_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";

                StringBuilder block = new();
                block.Append($"{outputTypeHints}BEGIN ");
                block.Append($"{updateQuery}; ");
                block.Append($"IF SQL%ROWCOUNT > 0 THEN ");
                block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {updateIndicator} FROM DUAL; ");
                block.Append($"ELSE ");
                // Distinguish "row exists but update policy blocked" (403) from "row absent" (insert).
                block.Append($"IF (SELECT COUNT(*) FROM {tableName} WHERE {pkPredicates}) > 0 THEN ");
                block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {updateIndicator} FROM DUAL WHERE 1 = 0; ");
                block.Append($"ELSE ");
                if (hasCreatePolicy)
                {
                    // Gate the INSERT on the create policy: when blocked, open an empty cursor
                    // so the executor surfaces 403 rather than ORA-01400 (400).
                    block.Append($"IF (SELECT COUNT(*) FROM (SELECT {namedValues} FROM DUAL) WHERE {createPolicy}) > 0 THEN ");
                    block.Append($"{insertQuery}; ");
                    block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL; ");
                    block.Append($"ELSE ");
                    block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL WHERE 1 = 0; ");
                    block.Append($"END IF; ");
                }
                else
                {
                    block.Append($"{insertQuery}; ");
                    block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL; ");
                }
                block.Append($"END IF; ");
                block.Append($"END IF; ");
                block.Append($"END;");
                return block.ToString();
            }
        }

        /// <summary>
        /// Build column as
        /// "{tableAlias}"."{ColumnName}"
        /// or if SourceAlias is empty, as
        /// "{ColumnName}"
        /// </summary>
        private static string BuildOutputTypeHints(
            IEnumerable<LabelledColumn> outputColumns,
            SourceDefinition sourceDefinition)
        {
            IEnumerable<string> hints = outputColumns
                .Select(column =>
                {
                    sourceDefinition.Columns.TryGetValue(column.ColumnName, out ColumnDefinition? definition);
                    return $"{column.Label}={GetOracleOutputType(definition?.SystemType, definition?.DbType)}";
                });

            return $"/* DAB_ORACLE_OUTPUT_TYPES:{string.Join(",", hints)} */ ";
        }

        private static OracleDbType GetOracleOutputType(Type? systemType, DbType? dbType)
        {
            if (systemType == typeof(byte[]))
            {
                return OracleDbType.Raw;
            }

            if (systemType == typeof(DateTimeOffset))
            {
                return OracleDbType.TimeStampTZ;
            }

            if (systemType == typeof(DateTime))
            {
                return OracleDbType.TimeStamp;
            }

            return dbType switch
            {
                DbType.Boolean => OracleDbType.Boolean,
                DbType.Byte => OracleDbType.Byte,
                DbType.Int16 => OracleDbType.Int16,
                DbType.Int32 => OracleDbType.Int32,
                DbType.Int64 => OracleDbType.Int64,
                DbType.Single => OracleDbType.Single,
                DbType.Double => OracleDbType.Double,
                DbType.Decimal => OracleDbType.Decimal,
                _ => OracleDbType.Varchar2,
            };
        }

        protected override string Build(Column column)
        {
            // Oracle stores unquoted identifiers in uppercase. The table alias is DAB-generated
            // ("table{N}", lowercase) and emitted UPPERCASE in the FROM/JOIN clauses, so it must be
            // uppercased here to resolve. The column name is the PHYSICAL backing name preserved
            // from Oracle metadata (uppercase for unquoted identifiers, exact case for quoted ones),
            // so it is emitted verbatim - no case transformation.
            if (!string.IsNullOrEmpty(column.TableAlias))
            {
                return $"{QuoteIdentifier(column.TableAlias.ToUpperInvariant())}.{QuoteIdentifier(column.ColumnName)}";
            }
            // If there is no table alias we return [{Column}]
            else
            {
                return $"{QuoteIdentifier(column.ColumnName)}";
            }
        }

        /// <summary>
        /// Override to emit the INNER JOIN alias WITHOUT the AS keyword - Oracle does not accept
        /// "AS" for table aliases in the FROM/JOIN clause (unlike column aliases) and rejects it
        /// with ORA-02000 "missing ON or USING keyword". The alias is uppercased to match the
        /// uppercase alias emitted by <see cref="Build(Column)"/> (Oracle is case-sensitive for
        /// quoted identifiers).
        /// </summary>
        protected override string Build(SqlJoinStructure join)
        {
            if (join is null)
            {
                throw new ArgumentNullException(nameof(join));
            }

            if (!string.IsNullOrWhiteSpace(join.DbObject.SchemaName))
            {
                return $" INNER JOIN {QuoteIdentifier(join.DbObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(join.DbObject.Name.ToUpperInvariant())} " +
                       $"{QuoteIdentifier(join.TableAlias.ToUpperInvariant())} " +
                       $"ON {Build(join.Predicates)}";
            }
            else
            {
                return $" INNER JOIN {QuoteIdentifier(join.DbObject.Name.ToUpperInvariant())} " +
                       $"{QuoteIdentifier(join.TableAlias.ToUpperInvariant())} " +
                       $"ON {Build(join.Predicates)}";
            }
        }

        /// <summary>
        /// Builds an aggregation column (e.g. MAX([SourceAlias].[Column])) for the SELECT list and
        /// HAVING clauses. Oracle stores unquoted identifiers uppercase, so the table alias and
        /// column name must be emitted UPPERCASE just like <see cref="Build(Column)"/> to avoid
        /// ORA-00904. Aggregation functions (COUNT/SUM/AVG/MIN/MAX) are case-insensitive.
        /// </summary>
        protected override string Build(AggregationColumn column, bool useAlias = false)
        {
            string columnName = Build(column as Column);
            columnName = column.IsDistinct ? $"DISTINCT ({columnName})" : columnName;
            string appendAlias = useAlias ? $" AS {QuoteIdentifier(column.OperationAlias)}" : string.Empty;
            return $"{column.Type.ToString().ToUpperInvariant()}({columnName}) {appendAlias}";
        }

        /// <summary>
        /// Builds a comma-separated, individually-quoted column list for INSERT column lists.
        /// Column names carry the exact physical casing preserved from Oracle metadata (uppercase
        /// for unquoted identifiers, exact case for quoted ones), so they are emitted verbatim.
        /// </summary>
        private string BuildColumnList(IEnumerable<string> columnNames)
        {
            return string.Join(", ", columnNames.Select(c => QuoteIdentifier(c)));
        }

        /// <summary>
        /// Looks into the upsert result returned by Oracle and returns
        /// whether the upsert was executed as an insert.
        /// This function also removes the metadata column that Oracle
        /// returns to indicate how UPSERT is executed.
        /// </summary>
        public static bool IsInsert(IDictionary<string, object?> upsertResult)
        {
            if (!upsertResult.ContainsKey(UPSERT_IDENTIFIER_COLUMN_NAME))
            {
                throw new ArgumentException($"Upsert result must have a {UPSERT_IDENTIFIER_COLUMN_NAME} column.");
            }

            object? opType = upsertResult[UPSERT_IDENTIFIER_COLUMN_NAME];
            upsertResult.Remove(UPSERT_IDENTIFIER_COLUMN_NAME);

            if (opType != null && opType is string opTypeStr)
            {
                switch (opTypeStr)
                {
                    case INSERT_UPSERT:
                        return true;
                    case UPDATE_UPSERT:
                        return false;
                }
            }

            throw new ArgumentException($"Invalid {UPSERT_IDENTIFIER_COLUMN_NAME} column value.");
        }

        /// <summary>
        /// Encode byte array columns to base64 strings instead of hex strings
        /// when parsing the results into json
        /// </summary>
        private string MakeSelectColumns(SqlQueryStructure structure)
        {
            List<string> builtColumns = new();

            // go through columns to find columns with type byte[]
            foreach (LabelledColumn column in structure.Columns)
            {
                // columns which contain the json of a nested type are called SqlQueryStructure.DATA_IDENT
                // and they are not actual columns of the underlying table so don't check for column type
                // in that scenario
                if (column.ColumnName != SqlQueryStructure.DATA_IDENT &&
                    structure.GetColumnSystemType(column.ColumnName) == typeof(byte[]))
                {
                    // Oracle RAW/BLOB is not stored as base64 so a conversion is made before
                    // producing the json result since HotChocolate handles ByteArray as base64.
                    // UTL_ENCODE.BASE64_ENCODE(NULL) throws ORA-29261 "bad argument", so NULL byte
                    // columns must pass through unmodified (CASE WHEN ... IS NULL THEN NULL).
                    string refColumn = Build(column as Column);
                    builtColumns.Add(
                        $"CASE WHEN {refColumn} IS NULL THEN NULL " +
                        $"ELSE UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE({refColumn})) END " +
                        $"AS {QuoteIdentifier(column.Label)}");
                }
                else
                {
                    builtColumns.Add(Build(column as LabelledColumn));
                }
            }

            return string.Join(", ", builtColumns);
        }

        /// <summary>
        /// Builds the metadata query for a STANDALONE Oracle subprogram only. The name must be at
        /// most two parts ("schema.subprogram" or "subprogram"). A three-part name
        /// ("schema.package.subprogram") must route through the package-aware overload instead;
        /// <see cref="SchemaNameFrom"/> / <see cref="NameFrom"/> would otherwise silently drop the
        /// middle package token and resolve against the wrong object.
        /// </summary>
        /// <inheritdoc/>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            // Oracle 19c implementation for retrieving stored procedure result set metadata.
            // Oracle doesn't have a direct equivalent to SQL Server's
            // dm_exec_describe_first_result_set_for_object. Instead we query ALL_ARGUMENTS to get
            // OUT / IN OUT arguments that represent the result.
            // databaseObjectName format: "schema.procedureName" or "procedureName".
            string query =
                $"SELECT " +
                $"ARGUMENT_NAME AS {QuoteIdentifier(STOREDPROC_COLUMN_NAME)}, " +
                $"DATA_TYPE AS {QuoteIdentifier(STOREDPROC_COLUMN_SYSTEMTYPENAME)}, " +
                $"'false' AS {QuoteIdentifier(STOREDPROC_COLUMN_ISNULLABLE)} " +
                $"FROM ALL_ARGUMENTS " +
                $"WHERE UPPER(OWNER) = UPPER('{SchemaNameFrom(databaseObjectName).Replace("'", "''")}') " +
                $"AND UPPER(OBJECT_NAME) = UPPER('{NameFrom(databaseObjectName).Replace("'", "''")}') " +
                $"AND IN_OUT IN ('OUT', 'IN/OUT') " +
                $"AND ARGUMENT_NAME IS NOT NULL " +
                $"ORDER BY POSITION";

            return query;
        }

        /// <summary>
        /// Builds an Oracle query that retrieves result set metadata for a subprogram that lives
        /// inside a package. Unlike standalone subprograms (whose arguments appear in ALL_ARGUMENTS
        /// with an empty PACKAGE_NAME column), packaged subprogram arguments are keyed by the
        /// PACKAGE_NAME column and the bare subprogram name in OBJECT_NAME.
        /// </summary>
        /// <param name="schemaName">Owning schema, e.g. "SYSTEM".</param>
        /// <param name="packageName">Package name, e.g. "PKG_TEST".</param>
        /// <param name="subprogramName">Bare subprogram name within the package, e.g. "GET_BOOKS".</param>
        /// <param name="isFunction">True when the subprogram is a function. A function's result set
        /// is its RETURN value, which appears in ALL_ARGUMENTS as an OUT argument at POSITION 0 with
        /// a NULL ARGUMENT_NAME.</param>
        public string BuildStoredProcedureResultDetailsQuery(
            string schemaName,
            string? packageName,
            string subprogramName,
            bool isFunction)
        {
            // ALL_ARGUMENTS keys a standalone subprogram by PACKAGE_NAME IS NULL and a packaged one
            // by PACKAGE_NAME = <package>. (In Oracle an empty string IS NULL, so a plain equality
            // against an empty package name would match nothing.)
            string packageClause = string.IsNullOrEmpty(packageName)
                ? "PACKAGE_NAME IS NULL"
                : $"UPPER(PACKAGE_NAME) = UPPER('{packageName.Replace("'", "''")}')";

            string query =
                $"SELECT " +
                $"ARGUMENT_NAME AS {QuoteIdentifier(STOREDPROC_COLUMN_NAME)}, " +
                $"DATA_TYPE AS {QuoteIdentifier(STOREDPROC_COLUMN_SYSTEMTYPENAME)}, " +
                $"'false' AS {QuoteIdentifier(STOREDPROC_COLUMN_ISNULLABLE)} " +
                $"FROM ALL_ARGUMENTS " +
                $"WHERE UPPER(OWNER) = UPPER('{schemaName.Replace("'", "''")}') " +
                $"AND {packageClause} " +
                $"AND UPPER(OBJECT_NAME) = UPPER('{subprogramName.Replace("'", "''")}') " +
                (isFunction
                    // A function's result set is its RETURN value (POSITION 0, ARGUMENT_NAME null).
                    ? $"AND POSITION = 0 "
                    // A procedure's result set is its cursor OUT parameter(s).
                    : $"AND IN_OUT IN ('OUT', 'IN/OUT') AND ARGUMENT_NAME IS NOT NULL ") +
                $"ORDER BY POSITION";

            return query;
        }

        /// <summary>
        /// Extracts the schema (the first token) from a standalone subprogram name. Callers must
        /// ensure the name has at most two dot-separated tokens: for "a.b.c" this returns "a" and
        /// silently ignores the middle token, which is only correct when the caller has already
        /// routed package-qualified names elsewhere.
        /// </summary>
        private static string SchemaNameFrom(string databaseObjectName)
        {
            int dot = databaseObjectName.IndexOf('.');
            return dot < 0 ? databaseObjectName : databaseObjectName[..dot];
        }

        /// <summary>
        /// Extracts the object name (the last token) from a standalone subprogram name. Callers must
        /// ensure the name has at most two dot-separated tokens: for "a.b.c" this returns "c" and
        /// silently ignores the middle token.
        /// </summary>
        private static string NameFrom(string databaseObjectName)
        {
            int dot = databaseObjectName.LastIndexOf('.');
            return dot < 0 ? databaseObjectName : databaseObjectName[(dot + 1)..];
        }

        /// <inheritdoc/>
        public override string BuildForeignKeyInfoQuery(int numberOfParameters)
        {
            string[] schemaNameParams = CreateParams(kindOfParam: SCHEMA_NAME_PARAM, numberOfParameters);
            string[] tableNameParams = CreateParams(kindOfParam: TABLE_NAME_PARAM, numberOfParameters);
            
            // Oracle uses :param syntax instead of @param
            string tableSchemaParamsForInClause = string.Join(", :", schemaNameParams);
            string tableNameParamsForInClause = string.Join(", :", tableNameParams);

            // Oracle uses its data dictionary views instead of INFORMATION_SCHEMA
            // ALL_CONSTRAINTS contains constraint information (CONSTRAINT_TYPE = 'R' for foreign keys)
            // ALL_CONS_COLUMNS contains column mappings for constraints
            // R_OWNER and R_CONSTRAINT_NAME reference the parent (unique/primary key) constraint
            string foreignKeyQuery = $@"
                SELECT 
                    RefCons.CONSTRAINT_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition))},
                    RefCons.OWNER {QuoteIdentifier($"Referencing{nameof(DatabaseObject.SchemaName)}")},
                    RefCons.TABLE_NAME {QuoteIdentifier($"Referencing{nameof(SourceDefinition)}")},
                    RefConsCol.COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencingColumns))},
                    RefConsPk.OWNER {QuoteIdentifier($"Referenced{nameof(DatabaseObject.SchemaName)}")},
                    RefConsPk.TABLE_NAME {QuoteIdentifier($"Referenced{nameof(SourceDefinition)}")},
                    RefConsPkCol.COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedColumns))}
                FROM 
                    ALL_CONSTRAINTS RefCons
                    INNER JOIN 
                    ALL_CONS_COLUMNS RefConsCol
                        ON RefCons.OWNER = RefConsCol.OWNER
                        AND RefCons.CONSTRAINT_NAME = RefConsCol.CONSTRAINT_NAME
                    INNER JOIN
                    ALL_CONSTRAINTS RefConsPk
                        ON RefCons.R_OWNER = RefConsPk.OWNER
                        AND RefCons.R_CONSTRAINT_NAME = RefConsPk.CONSTRAINT_NAME
                    INNER JOIN
                    ALL_CONS_COLUMNS RefConsPkCol
                        ON RefConsPk.OWNER = RefConsPkCol.OWNER
                        AND RefConsPk.CONSTRAINT_NAME = RefConsPkCol.CONSTRAINT_NAME
                        AND RefConsCol.POSITION = RefConsPkCol.POSITION
                WHERE
                    RefCons.CONSTRAINT_TYPE = 'R'
                    AND UPPER(RefCons.OWNER) IN (:{tableSchemaParamsForInClause})
                    AND UPPER(RefCons.TABLE_NAME) IN (:{tableNameParamsForInClause})";

            return foreignKeyQuery;
        }

        /// <inheritdoc/>
        public string BuildQueryToGetReadOnlyColumns(string schemaParamName, string tableParamName)
        {
            // Oracle uses :param instead of @param for bind parameters
            string query = $"SELECT COLUMN_NAME FROM ALL_TAB_COLS " +
                $"WHERE OWNER = :{schemaParamName.TrimStart('@')} AND TABLE_NAME = :{tableParamName.TrimStart('@')} AND VIRTUAL_COLUMN = 'YES'";
            return query;
        }

        /// <summary>
        /// Builds an Oracle query that discovers autoentity tables: user-accessible tables that
        /// have a primary key, filtered by the include/exclude patterns and named per the name
        /// pattern (both using {schema}/{object} placeholders). Include/exclude patterns are comma-
        /// separated SQL LIKE patterns (with ESCAPE '\') matched against "schema.object".
        /// Oracle-maintained system schemas (SYS dictionary base tables and friends) are excluded
        /// so a privileged connection does not materialize hundreds of system entities, mirroring
        /// the system-object filtering the MSSQL builder performs.
        /// Entity names are lowercased so generated REST/GraphQL names are consistent with the
        /// lowercase exposed names Oracle uses for columns; the returned "schema"/"object" values
        /// keep physical casing for source resolution.
        /// Returns rows aliased as "schema", "object", and "entity_name" (the JSON property names
        /// the metadata provider reads when materializing generated entities).
        /// </summary>
        public string BuildGetAutoentitiesQuery()
        {
            return @"
WITH exclude_patterns AS (
    SELECT TRIM(REGEXP_SUBSTR(:exclude_pattern, '[^,]+', 1, LEVEL)) AS pattern
    FROM dual
    CONNECT BY LEVEL <= REGEXP_COUNT(:exclude_pattern, ',') + 1
        AND TRIM(REGEXP_SUBSTR(:exclude_pattern, '[^,]+', 1, LEVEL)) IS NOT NULL
),
include_patterns AS (
    SELECT TRIM(REGEXP_SUBSTR(:include_pattern, '[^,]+', 1, LEVEL)) AS pattern
    FROM dual
    CONNECT BY LEVEL <= REGEXP_COUNT(:include_pattern, ',') + 1
        AND TRIM(REGEXP_SUBSTR(:include_pattern, '[^,]+', 1, LEVEL)) IS NOT NULL
),
candidate_tables AS (
    SELECT
        t.owner AS schema_name,
        t.table_name AS object_name,
        t.owner || '.' || t.table_name AS full_name
    FROM all_tables t
    WHERE t.owner NOT IN (
              'SYS', 'XDB', 'ORDSYS', 'ORDDATA', 'MDSYS', 'OLAPSYS', 'LBACSYS',
              'DVSYS', 'AUDSYS', 'OJVMSYS', 'CTXSYS', 'WMSYS', 'EXFSYS', 'OUTLN',
              'GSMADMIN_INTERNAL', 'DBSNMP', 'APPQOSSYS', 'FLOWS_FILES')
      AND NOT REGEXP_LIKE(t.owner, '^APEX_[0-9]+')
      AND EXISTS (
        SELECT 1
        FROM all_constraints c
        WHERE c.owner = t.owner
          AND c.table_name = t.table_name
          AND c.constraint_type = 'P'
    )
)
SELECT
    a.schema_name AS ""schema"",
    a.object_name AS ""object"",
    CASE
        WHEN NVL(LENGTH(TRIM(:name_pattern)), 0) = 0 THEN LOWER(a.object_name)
        ELSE REPLACE(REPLACE(:name_pattern, '{schema}', LOWER(a.schema_name)), '{object}', LOWER(a.object_name))
    END AS ""entity_name""
FROM candidate_tables a
WHERE
    (NOT EXISTS (SELECT 1 FROM exclude_patterns)
     OR NOT EXISTS (SELECT 1 FROM exclude_patterns WHERE a.full_name LIKE exclude_patterns.pattern ESCAPE '\'))
    AND
    (NOT EXISTS (SELECT 1 FROM include_patterns)
     OR EXISTS (SELECT 1 FROM include_patterns WHERE a.full_name LIKE include_patterns.pattern ESCAPE '\'))
ORDER BY a.schema_name, a.object_name";
        }

        public string QuoteTableNameAsDBConnectionParam(string param)
        {
            // This value is bound as a DbConnectionParam (e.g. against ALL_TAB_COLS.TABLE_NAME),
            // NOT embedded directly in SQL text. Oracle stores unquoted identifiers in uppercase
            // and ALL_TAB_COLS.TABLE_NAME stores the bare (unquoted, uppercased) name, so the
            // bind value must be bare and uppercased - quoting it here would never match.
            return param.ToUpperInvariant();
        }
    }
}
