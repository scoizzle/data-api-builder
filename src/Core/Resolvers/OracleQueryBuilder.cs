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
        private const string UPSERT_IDENTIFIER_COLUMN_NAME = "___upsert_op___";
        private const string INSERT_UPSERT = "inserted";
        private const string UPDATE_UPSERT = "updated";

        private static DbCommandBuilder _builder = new OracleCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"{QuoteIdentifier(structure.SourceAlias)}{Build(structure.Joins)}";
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) {QuoteIdentifier(x.Key)} ON (1=1)"));

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

            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";
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

            return $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
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

            return $"DELETE FROM {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
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
            // Oracle uses MERGE statement for upsert operations
            string updatePredicates = JoinPredicateStrings(Build(structure.Predicates), structure.GetDbPolicyForOperation(EntityActionOperation.Update));
            
            if (structure.IsFallbackToUpdate)
            {
                return $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {updatePredicates} " +
                    $"RETURNING {Build(structure.OutputColumns)}, '{UPDATE_UPSERT}' AS {UPSERT_IDENTIFIER_COLUMN_NAME} INTO {string.Join(", ", structure.OutputColumns.Select(c => ":" + c.Label))}, :{UPSERT_IDENTIFIER_COLUMN_NAME}";
            }
            else
            {
                // Build the MERGE statement  
                string mergeQuery = $"MERGE INTO {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} target " +
                    $"USING (SELECT {string.Join(", ", structure.Values.Select((v, i) => $"{v} AS {QuoteIdentifier(structure.InsertColumns[i])}"))} FROM DUAL) source " +
                    $"ON ({updatePredicates}) " +
                    $"WHEN MATCHED THEN UPDATE SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHEN NOT MATCHED THEN INSERT ({Build(structure.InsertColumns)}) VALUES ({string.Join(", ", structure.Values)})";

                return mergeQuery;
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
            // If the table alias is not empty, we return [{SourceAlias}].[{Column}]
            if (!string.IsNullOrEmpty(column.TableAlias))
            {
                return $"{QuoteIdentifier(column.TableAlias)}.{QuoteIdentifier(column.ColumnName)}";
            }
            // If there is no table alias we return [{Column}]
            else
            {
                return $"{QuoteIdentifier(column.ColumnName)}";
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
                    builtColumns.Add(Build(column));
                }
            }

            return string.Join(", ", builtColumns);
        }

        /// <inheritdoc/>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            throw new NotImplementedException();
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
