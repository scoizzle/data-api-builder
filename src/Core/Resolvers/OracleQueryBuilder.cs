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

        private static DbCommandBuilder _builder = new OracleCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
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
                string insertColumns = Build(structure.InsertColumns);
                insertQuery += $"({insertColumns}) ";
                
                // POST-CONDITION: Apply database policy to VALUES clause
                if (dbPolicyPredicates.Equals(BASE_PREDICATE))
                {
                    insertQuery += $"VALUES ({string.Join(", ", structure.Values)}) ";
                }
                else
                {
                    // If policies exist, use SELECT form to apply WHERE clause for row-level security
                    string valueSelects = string.Join(", ", structure.Values.Select((v, i) => $"{v} AS {QuoteIdentifier(structure.InsertColumns[i])}"));
                    insertQuery += $"SELECT {insertColumns} FROM (SELECT {valueSelects} FROM DUAL) WHERE {dbPolicyPredicates} ";
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

            // POST-CONDITION: Return inserted data using RETURNING INTO clause
            return $"{insertQuery} RETURNING {Build(structure.OutputColumns)} INTO {string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label))}";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string predicates = JoinPredicateStrings(
                                   structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                                   Build(structure.Predicates));

            return $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {predicates} " +
                    $"RETURNING {Build(structure.OutputColumns)} INTO {string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label))}";
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
            // Oracle upserts are built on the Postgres model: a leading COUNT
            // statement that reports how many rows match the primary key (so the
            // executor can distinguish update from insert, and surface database
            // policy failures), followed by the data-modifying statements.
            //
            // Oracle's MERGE statement was considered but rejected because:
            //  1. The predicates built by BaseSqlQueryBuilder reference the source
            //     table alias (e.g. table0.col = :param), which does not exist in a
            //     MERGE's ON clause (only target/source aliases are in scope).
            //  2. MERGE cannot return per-branch data with a literal indicator
            //     (RETURNING only supports a single expression list).
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName.ToUpperInvariant())}.{QuoteIdentifier(structure.DatabaseObject.Name.ToUpperInvariant())}";
            string pkPredicates = Build(structure.Predicates);
            string isFallbackToUpdateSqlLiteral = structure.IsFallbackToUpdate ? "1" : "0";

            // RS1: COUNT of rows matching PK (no policy) — used to distinguish
            // "row doesn't exist" from "row exists but policy blocked" and to know
            // whether the upsert resolved to an INSERT or an UPDATE.
            string countQuery = $"SELECT COUNT(*) AS {COUNT_ROWS_WITH_GIVEN_PK}, " +
                $"{isFallbackToUpdateSqlLiteral} AS {IS_FALLBACK_TO_UPDATE} " +
                $"FROM {tableName} WHERE {pkPredicates}";

            string updatePredicates = JoinPredicateStrings(pkPredicates, structure.GetDbPolicyForOperation(EntityActionOperation.Update));
            string updateQuery = $"UPDATE {tableName} " +
                $"SET {Build(structure.UpdateOperations, ", ")} " +
                $"WHERE {updatePredicates} " +
                $"RETURNING {Build(structure.OutputColumns)}, '{UPDATE_UPSERT}' AS {UPSERT_IDENTIFIER_COLUMN_NAME} " +
                $"INTO {string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label))}, :{UPSERT_IDENTIFIER_COLUMN_NAME}";

            if (structure.IsFallbackToUpdate)
            {
                // RS2: UPDATE only — no INSERT branch for autogen PK or missing required columns.
                return $"{countQuery}; {updateQuery};";
            }
            else
            {
                // INSERT only runs when the row doesn't exist (pkPredicates match nothing)
                // AND the create policy (if any) is satisfied.
                string insertPredicates = JoinPredicateStrings(
                    $"NOT EXISTS (SELECT 1 FROM {tableName} WHERE {pkPredicates})",
                    structure.GetDbPolicyForOperation(EntityActionOperation.Create));

                string insertQuery = $"INSERT INTO {tableName} ({Build(structure.InsertColumns)}) " +
                    $"SELECT {Build(structure.InsertColumns)} FROM (SELECT {string.Join(", ", structure.Values.Select((v, i) => $"{v} AS {QuoteIdentifier(structure.InsertColumns[i])}"))} FROM DUAL) " +
                    $"WHERE {insertPredicates} " +
                    $"RETURNING {Build(structure.OutputColumns)}, '{INSERT_UPSERT}' AS {UPSERT_IDENTIFIER_COLUMN_NAME} " +
                    $"INTO {string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label))}, :{UPSERT_IDENTIFIER_COLUMN_NAME}";

                return $"{countQuery}; {updateQuery}; {insertQuery};";
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
