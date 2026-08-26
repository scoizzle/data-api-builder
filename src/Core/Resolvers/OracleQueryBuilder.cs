// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Oracle stores unquoted identifiers in UPPERCASE. A double-quoted lowercase column
        /// reference (e.g. "piecesavailable") resolves to a non-existent lowercase object
        /// (ORA-00904), so physical column references in raw SQL fragments (OData filters,
        /// predicate operands) must be emitted UPPERCASE and quoted.
        /// </summary>
        /// <inheritdoc />
        public override string QuotePhysicalColumn(string columnName)
        {
            return QuoteIdentifier(columnName.ToUpperInvariant());
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            // All Oracle identifiers are case-insensitive by default and are stored in
            // uppercase when created unquoted. DAB builds columns via Build(Column) which
            // emits the table alias uppercased (e.g. "SYSTEM_BOOKS"), so the FROM-clause
            // alias must be uppercased too or the column references will not resolve
            // (ORA-00904: invalid identifier).
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())} " +
                             $"{QuoteIdentifier(structure.SourceAlias.ToUpperInvariant())}{Build(structure.Joins)}";
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) {QuoteIdentifier(x.Key.ToUpperInvariant())} ON (1=1)"));

            string predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));

            string query = $"SELECT {MakeSelectColumns(structure)}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + $" ORDER BY {Build(structure.OrderByColumns)}"
                + $" OFFSET 0 ROWS FETCH NEXT {structure.Limit()} ROWS ONLY";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            StringBuilder result = new();
            if (structure.IsListQuery)
            {
                result.Append($"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*)), JSON_ARRAY()) ");
            }
            else
            {
                result.Append($"SELECT JSON_OBJECT(*) ");
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
                string insertColumns = BuildUppercaseColumns(structure.InsertColumns);
                insertQuery += $"({insertColumns}) ";
                
                // POST-CONDITION: Apply database policy to VALUES clause.
                // Oracle does NOT support RETURNING with INSERT ... SELECT (ORA-03049), so the
                // create policy is enforced by wrapping each value in a guarded scalar subselect
                // (SELECT <value> FROM DUAL WHERE <policy>) inside a VALUES list.
                if (dbPolicyPredicates.Equals(BASE_PREDICATE))
                {
                    insertQuery += $"VALUES ({string.Join(", ", structure.Values)}) ";
                }
                else
                {
                    string guardedValues = string.Join(", ", structure.Values.Select(
                        v => $"(SELECT {v} FROM DUAL WHERE {dbPolicyPredicates})"));
                    insertQuery += $"VALUES ({guardedValues}) ";
                }
            }
            else
            {
                // PRE-CONDITION: Validate DEFAULT VALUES compatibility with policies
                if (!dbPolicyPredicates.Equals(BASE_PREDICATE))
                {
                    // Cannot apply policies to DEFAULT VALUES insert
                    throw new DataApiBuilderException(
                        "INSERT with DEFAULT VALUES cannot be used when row-level security policies are defined",
                        System.Net.HttpStatusCode.BadRequest,
                        DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                }
                insertQuery += "DEFAULT VALUES ";
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
            // when an AS alias appears before INTO), so the output column list is the bare,
            // UPPERCASE physical column name (unquoted-lowercase input resolves case-insensitively).
            string returningColumns = string.Join(", ", structure.OutputColumns.Select(c => QuoteIdentifier(c.ColumnName.ToUpperInvariant())));
            string bindNames = string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label));
            string selectFromBinds = string.Join(", ", structure.OutputColumns.Select(c => $":{c.Label} AS {QuoteIdentifier(c.Label)}"));

            return $"BEGIN {insertQuery} RETURNING {returningColumns} INTO {bindNames}; " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; END;";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string predicates = JoinPredicateStrings(
                                   structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                                   Build(structure.Predicates));

            // Wrap in a PL/SQL block and surface the returned columns as a REF CURSOR result set
            // (see Build(SqlInsertStructure) for why output binds + cursor are required in Oracle).
            // The RETURNING column list must be bare/UPPERCASE (no aliases - ORA-00925).
            string returningColumns = string.Join(", ", structure.OutputColumns.Select(c => QuoteIdentifier(c.ColumnName.ToUpperInvariant())));
            string bindNames = string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label));
            string updateQuery = $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {predicates} " +
                    $"RETURNING {returningColumns} INTO {bindNames}";

            string selectFromBinds = string.Join(", ", structure.OutputColumns.Select(c => $":{c.Label} AS {QuoteIdentifier(c.Label)}"));

            return $"BEGIN {updateQuery}; OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; END;";
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
        /// TODO; tracked here: https://github.com/Azure/hawaii-engine/issues/630
        /// </summary>
        public string Build(SqlExecuteStructure structure)
        {
            throw new NotImplementedException();
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
            // RETURNING column list must be bare/UPPERCASE (no aliases - ORA-00925).
            string returningColumns = string.Join(", ", structure.OutputColumns.Select(c => QuoteIdentifier(c.ColumnName.ToUpperInvariant())));
            string bindNames = string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label));
            string updateQuery = $"UPDATE {tableName} " +
                $"SET {Build(structure.UpdateOperations, ", ")} " +
                $"WHERE {updatePredicates} " +
                $"RETURNING {returningColumns} " +
                $"INTO {bindNames}";

            // The REF CURSOR SELECT reads the output binds (populated by whichever branch ran).
            string selectFromBinds = string.Join(", ", structure.OutputColumns.Select(c => $":{c.Label} AS {QuoteIdentifier(c.Label)}"));

            if (structure.IsFallbackToUpdate)
            {
                // Update-only flow (e.g. autogenerated PK): no INSERT branch. When no row matched the
                // primary key + update policy, no branch ran - open an EMPTY cursor so the executor
                // reports 404 (or a policy failure) exactly like the other database engines.
                string fallbackIndicator = $"{selectFromBinds}, '{UPDATE_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                return $"BEGIN {updateQuery}; " +
                    $"IF SQL%ROWCOUNT > 0 THEN " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {fallbackIndicator} FROM DUAL; " +
                    $"ELSE " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {fallbackIndicator} FROM DUAL WHERE 1 = 0; " +
                    $"END IF; " +
                    $"END;";
            }
            else
            {
                // INSERT only runs when the UPDATE matched no row (row absent). The create database
                // policy, when defined, is applied by selecting the VALUES through a DUAL subquery
                // that filters on the policy. NOTE: Oracle does NOT support RETURNING with
                // INSERT ... SELECT (ORA-03049), and the create-policy predicate must live in the
                // SELECT ... FROM DUAL WHERE clause, so the value list is emitted as a
                // scalar subquery per column when a policy is present.
                //
                // Race safety: concurrent upserts for the same missing PK serialize on the primary
                // key; the loser hits ORA-00001 (unique constraint) which the exception parser maps
                // to HTTP 409, matching the behavior of the other engines' non-atomic upserts.
                List<string> insertWhere = new();
                string? createPolicy = structure.GetDbPolicyForOperation(EntityActionOperation.Create);
                if (!string.IsNullOrEmpty(createPolicy) && !createPolicy.Equals(BASE_PREDICATE))
                {
                    insertWhere.Add(createPolicy);
                }

                // Build INSERT ... VALUES form (RETURNING is only valid with VALUES, not SELECT).
                string insertColumns = BuildUppercaseColumns(structure.InsertColumns);
                string insertQuery;
                if (insertWhere.Count == 0)
                {
                    insertQuery = $"INSERT INTO {tableName} ({insertColumns}) " +
                        $"VALUES ({string.Join(", ", structure.Values)}) " +
                        $"RETURNING {returningColumns} " +
                        $"INTO {bindNames}";
                }
                else
                {
                    // With a create policy, wrap each value in a guarded scalar subselect
                    // (SELECT value FROM DUAL WHERE <policy>) so the policy filters the insert.
                    string whereSql = string.Join(" AND ", insertWhere);
                    string guardedValues = string.Join(", ", structure.Values.Select(
                        v => $"(SELECT {v} FROM DUAL WHERE {whereSql})"));
                    insertQuery = $"INSERT INTO {tableName} ({insertColumns}) " +
                        $"VALUES ({guardedValues}) " +
                        $"RETURNING {returningColumns} " +
                        $"INTO {bindNames}";
                }

                string insertIndicator = $"{selectFromBinds}, '{INSERT_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                string updateIndicator = $"{selectFromBinds}, '{UPDATE_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";

                return $"BEGIN " +
                    $"{updateQuery}; " +
                    $"IF SQL%ROWCOUNT > 0 THEN " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {updateIndicator} FROM DUAL; " +
                    $"ELSE " +
                    $"{insertQuery}; " +
                    $"IF SQL%ROWCOUNT > 0 THEN " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL; " +
                    $"ELSE " +
                    // Neither UPDATE (row existed but policy-blocked) nor INSERT (create-policy
                    // blocked -> the guarded VALUES yielded NULLs and the NOT NULL PK failed, or
                    // the guarded subquery returned no row) ran. Open an EMPTY cursor so the
                    // executor reports 403 rather than fabricating a row of NULLs.
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL WHERE 1 = 0; " +
                    $"END IF; " +
                    $"END IF; " +
                    $"END;";
            }
        }

        /// <summary>
        /// Build column as
        /// "{tableAlias}"."{ColumnName}"
        /// or if SourceAlias is empty, as
        /// "{ColumnName}"
        /// </summary>
        protected override string Build(Column column)
        {
            // Oracle stores unquoted identifiers in uppercase. Column names are exposed
            // lowercase (see OracleMetadataProvider.GetPhysicalDatabaseColumnName) but must
            // be emitted UPPERCASE (and unquoted, or quoted-uppercase) so they resolve against
            // the physical column. If the table alias is not empty, we return [{SourceAlias}].[{Column}]
            if (!string.IsNullOrEmpty(column.TableAlias))
            {
                return $"{QuoteIdentifier(column.TableAlias.ToUpperInvariant())}.{QuoteIdentifier(column.ColumnName.ToUpperInvariant())}";
            }
            // If there is no table alias we return [{Column}]
            else
            {
                return $"{QuoteIdentifier(column.ColumnName.ToUpperInvariant())}";
            }
        }

        /// <summary>
        /// Builds a comma-separated, UPPERCASE, individually-quoted column list for INSERT column
        /// lists. Oracle stores unquoted identifiers uppercase, so a quoted-lowercase column
        /// reference (e.g. "title") resolves to a non-existent object (ORA-00904) unless uppercased.
        /// </summary>
        private string BuildUppercaseColumns(IEnumerable<string> columnNames)
        {
            return string.Join(", ", columnNames.Select(c => QuoteIdentifier(c.ToUpperInvariant())));
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
                    // producing the json result since HotChocolate handles ByteArray as base64
                    builtColumns.Add($"UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE({Build(column as Column)})) AS {QuoteIdentifier(column.Label)}");
                }
                else
                {
                    builtColumns.Add(Build(column as LabelledColumn));
                }
            }

            return string.Join(", ", builtColumns);
        }

        /// <inheritdoc/>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            // Oracle 19c implementation for retrieving stored procedure result set metadata
            // Oracle doesn't have a direct equivalent to SQL Server's dm_exec_describe_first_result_set_for_object
            // Instead, we query ALL_ARGUMENTS to get OUT and IN/OUT parameters that represent the result
            // databaseObjectName format: "schema.procedureName" or "procedureName"
            
            string query = 
                $"SELECT " +
                $"ARGUMENT_NAME AS {QuoteIdentifier(STOREDPROC_COLUMN_NAME)}, " +
                $"DATA_TYPE AS {QuoteIdentifier(STOREDPROC_COLUMN_SYSTEMTYPENAME)}, " +
                $"'false' AS {QuoteIdentifier(STOREDPROC_COLUMN_ISNULLABLE)} " +
                $"FROM ALL_ARGUMENTS " +
                $"WHERE (UPPER(OWNER || '.' || OBJECT_NAME) = UPPER('{databaseObjectName}') " +
                $"OR UPPER(OBJECT_NAME) = UPPER('{databaseObjectName}')) " +
                $"AND IN_OUT IN ('OUT', 'IN/OUT') " +
                $"AND ARGUMENT_NAME IS NOT NULL " +
                $"ORDER BY POSITION";
            
            return query;
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

        public string QuoteTableNameAsDBConnectionParam(string param)
        {
            // Oracle uses same quoting for table name as DB Connection Param
            // as when used directly in SQL text.
            return QuoteIdentifier(param);
        }
    }
}
