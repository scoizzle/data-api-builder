// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedDateTimeTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{

    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Oracle Single Type Test.
        /// Oracle requires conversion of a float value, ex: 0.33 to appropriate type otherwise precision may be lost.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: gt, lt, gte, lte, etc.</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SINGLE_TYPE, "gt", "CAST('-9.3' AS BINARY_FLOAT)", "-9.3", ">")]
        [DataRow(SINGLE_TYPE, "gte", "CAST('-9.2' AS BINARY_FLOAT)", "-9.2", ">=")]
        [DataRow(SINGLE_TYPE, "lt", "CAST('.33' AS BINARY_FLOAT)", "0.33", "<")]
        [DataRow(SINGLE_TYPE, "lte", "CAST('.33' AS BINARY_FLOAT)", "0.33", "<=")]
        [DataRow(SINGLE_TYPE, "neq", "CAST('9.2' AS BINARY_FLOAT)", "9.2", "!=")]
        [DataRow(SINGLE_TYPE, "eq", "CAST('0.33' AS BINARY_FLOAT)", "0.33", "=")]
        [DataTestMethod]
        public async Task Oracle_real_graphql_single_filter_expectedValues(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Oracle filter Type with IN operator Tests.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: IN</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SHORT_TYPE, "-1", "-1")]
        [DataRow(INT_TYPE, "-1", "-1")]
        [DataRow(LONG_TYPE, "-1", "-1")]
        [DataRow(FLOAT_TYPE, "-9.2", "-9.2")]
        [DataRow(DECIMAL_TYPE, "-9.292929", "-9.292929")]
        [DataRow(BOOLEAN_TYPE, "0", "false")]
        [DataTestMethod]
        public async Task Oracle_real_graphql_in_filter_expectedValues(
            string type,
            string sqlValue,
            string gqlValue)
        {
            if (type == STRING_TYPE)
            {
                sqlValue = $"('{sqlValue}')";
                gqlValue = $"[{gqlValue}]";
            }
            else
            {
                sqlValue = $"({sqlValue})";
                gqlValue = $"[{gqlValue}]";
            }

            await QueryTypeColumnFilterAndOrderBy(type, "in", sqlValue, gqlValue, "IN");
        }

        /// <summary>
        /// All the GQL supported types excluding UUID and Byte types which are tested separately.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: gt, lt, gte, lte, etc.</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SHORT_TYPE, "gt", "-33000", "-33000", ">")]
        [DataRow(SHORT_TYPE, "gte", "-1", "-1", ">=")]
        [DataRow(SHORT_TYPE, "lt", "1", "1", "<")]
        [DataRow(SHORT_TYPE, "lte", "1", "1", "<=")]
        [DataRow(SHORT_TYPE, "neq", "-32768", "-32768", "!=")]
        [DataRow(SHORT_TYPE, "eq", "-1", "-1", "=")]
        [DataRow(INT_TYPE, "gte", "-1", "-1", ">=")]
        [DataRow(INT_TYPE, "lt", "1", "1", "<")]
        [DataRow(INT_TYPE, "lte", "1", "1", "<=")]
        [DataRow(INT_TYPE, "neq", "-2147483648", "-2147483648", "!=")]
        [DataRow(INT_TYPE, "eq", "-1", "-1", "=")]
        [DataRow(LONG_TYPE, "gt", "-9223372036800000000", "-9223372036800000000", ">")]
        [DataRow(LONG_TYPE, "gte", "-1", "-1", ">=")]
        [DataRow(LONG_TYPE, "lt", "1", "1", "<")]
        [DataRow(LONG_TYPE, "lte", "1", "1", "<=")]
        [DataRow(LONG_TYPE, "neq", "-9223372036854775808", "-9223372036854775808", "!=")]
        [DataRow(LONG_TYPE, "eq", "-1", "-1", "=")]
        [DataRow(STRING_TYPE, "neq", "'null'", "\"null\"", "!=")]
        [DataRow(STRING_TYPE, "eq", "''", "\"\"", "=")]
        [DataRow(FLOAT_TYPE, "gte", "-9.2", "-9.2", ">=")]
        [DataRow(FLOAT_TYPE, "lt", ".33", "0.33", "<")]
        [DataRow(FLOAT_TYPE, "lte", ".33", "0.33", "<=")]
        [DataRow(FLOAT_TYPE, "neq", "9.2", "9.2", "!=")]
        [DataRow(FLOAT_TYPE, "eq", "-.33", "-0.33", "=")]
        [DataRow(DECIMAL_TYPE, "gt", "-10", "-10", ">")]
        [DataRow(DECIMAL_TYPE, "gte", "-9.292929", "-9.292929", ">=")]
        [DataRow(DECIMAL_TYPE, "lt", ".334", "0.334", "<")]
        [DataRow(DECIMAL_TYPE, "lte", ".333333", "0.333333", "<=")]
        [DataRow(DECIMAL_TYPE, "neq", "9.29292", "9.29292", "!=")]
        [DataRow(DECIMAL_TYPE, "eq", ".333333", "0.333333", "=")]
        [DataRow(BOOLEAN_TYPE, "neq", "0", "false", "!=")]
        [DataRow(BOOLEAN_TYPE, "eq", "1", "true", "=")]
        [DataRow(DATETIME_TYPE, "gt", "TO_TIMESTAMP('1753-01-01 00:00:00', 'YYYY-MM-DD HH24:MI:SS')", "\"1753-01-01 00:00:00.000\"", ">")]
        [DataRow(DATETIME_TYPE, "gte", "TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS')", "\"1999-01-08 10:23:00\"", ">=")]
        [DataRow(DATETIME_TYPE, "lt", "TO_TIMESTAMP('9999-12-31 23:59:59', 'YYYY-MM-DD HH24:MI:SS')", "\"9999-12-31 23:59:59\"", "<")]
        [DataRow(DATETIME_TYPE, "lte", "TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS')", "\"1999-01-08 10:23:54\"", "<=")]
        [DataRow(DATETIME_TYPE, "neq", "TO_TIMESTAMP('1900-01-01 00:00:00', 'YYYY-MM-DD HH24:MI:SS')", "\"1900-01-01 00:00:00\"", "!=")]
        [DataRow(DATETIME_TYPE, "eq", "TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS')", "\"1999-01-08 10:23:54\"", "=")]
        [DataTestMethod]
        public async Task Oracle_graphql_filter_tests(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Regression test: GraphQL filter values for Oracle DATE/TIMESTAMP columns must be
        /// bound as date/time values, not as VARCHAR2. A string bind is converted by Oracle
        /// using the session NLS date format (default DD-MON-RR), which accepted the
        /// Oracle-specific spelling but raised ORA-01861 for ISO 8601 filter values.
        /// The shared supported-types tests skip these types for Oracle, so they are
        /// exercised here against the type_table columns.
        /// </summary>
        [DataTestMethod]
        [DataRow(DATETIME_TYPE, "gte", "TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS')", "\"1999-01-08T10:23:00\"", " >= ")]
        [DataRow(DATETIME_TYPE, "eq", "TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS')", "\"1999-01-08T10:23:54\"", " = ")]
        [DataRow(DATETIME_TYPE, "lt", "TO_TIMESTAMP('9999-12-31 23:59:59', 'YYYY-MM-DD HH24:MI:SS')", "\"9999-12-31T23:59:59\"", " < ")]
        [DataRow(DATE_TYPE, "gte", "TO_DATE('1999-01-08', 'YYYY-MM-DD')", "\"1999-01-08\"", " >= ")]
        // The Oracle NLS spelling that worked before the fix must keep working.
        [DataRow(DATETIME_TYPE, "gte", "TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS')", "\"08-JAN-99 10:23:00\"", " >= ")]
        public async Task Oracle_graphql_datetime_filters_accept_iso_values(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            string field = GetTestFieldName(type);
            string gqlQuery = @"{
                supportedTypes(first: 100 orderBy: { typeid: ASC } filter: { " + field + ": {" + filterOperator + ": " + gqlValue + @"} }) {
                    items {
                        typeid, " + field + @"
                    }
                }
            }";

            string dbQuery = MakeQueryOnTypeTable(
                queryFields: new List<DabField> { new(alias: "typeid", backingColumnName: "id"), new(backingColumnName: field) },
                filterValue: sqlValue,
                filterOperator: queryOperator,
                filterField: field,
                orderBy: "id",
                limit: "100");

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "supportedTypes", isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Oracle stores '' as NULL, so an IN list containing "" must match NULL rows through
        /// IS NULL rather than binding the empty string into the IN list.
        /// </summary>
        [DataTestMethod]
        [DataRow("[\"\"]", "string_types IS NULL", DisplayName = "in [''] is a NULL test")]
        [DataRow("[\"\", \"lksa;jdflasdf;alsdflksdfkldj\"]", "(string_types IN ('lksa;jdflasdf;alsdflksdfkldj') OR string_types IS NULL)", DisplayName = "in ['', value] is IN or NULL")]
        public async Task Oracle_graphql_in_filter_treats_empty_string_as_null(string gqlList, string sqlPredicate)
        {
            string gqlQuery = @"{
                supportedTypes(first: 100 orderBy: { typeid: ASC } filter: { string_types: { in: " + gqlList + @" } }) {
                    items {
                        typeid, string_types
                    }
                }
            }";

            string dbQuery = $@"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('typeid' VALUE typeid, 'string_types' VALUE string_types) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT id AS typeid, string_types FROM type_table WHERE {sqlPredicate} ORDER BY id ASC)";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "supportedTypes", isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        protected override string MakeQueryOnTypeTable(List<DabField> queryFields, int id)
        {
            return MakeQueryOnTypeTable(queryFields, filterValue: id.ToString(), filterField: "id");
        }

        protected override string MakeQueryOnTypeTable(
            List<DabField> queryFields,
            string filterValue = "1",
            string filterOperator = "=",
            string filterField = "1",
            string orderBy = "id",
            string limit = "1")
        {
            string jsonFields = string.Join(", ",
                queryFields.Select(field => $"'{field.Alias}' VALUE {FormatColumnValue(field.BackingColumnName)}"));

            string selectList = string.Join(", ", queryFields.Select(field => field.BackingColumnName));

            if (limit.Equals("1"))
            {
                return $@"
                    SELECT JSON_OBJECT(
                        {jsonFields}
                    ) AS data
                    FROM (
                        SELECT {selectList}
                        FROM type_table table0
                        WHERE {filterField} {filterOperator} {filterValue}
                        ORDER BY {orderBy} ASC
                        FETCH FIRST {limit} ROWS ONLY
                    ) subq
                ";
            }

            return $@"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    {jsonFields}
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT {selectList}
                    FROM type_table table0
                    WHERE {filterField} {filterOperator} {filterValue}
                    ORDER BY {orderBy} ASC
                    FETCH FIRST {limit} ROWS ONLY
                ) subq
            ";
        }

        protected override bool IsSupportedType(string type)
        {
            return IsQueryTypeSupported(type);
        }

        /// <summary>
        /// Query rows for DATE/TIMESTAMP stay inconclusive. Typed writes use
        /// <see cref="IsTypedWriteSupported"/> and run those rows.
        /// </summary>
        private static bool IsQueryTypeSupported(string type)
        {
            return type switch
            {
                BYTE_TYPE => false,
                TIME_TYPE => false,
                LOCALTIME_TYPE => false,
                UUID_TYPE => false,
                BOOLEAN_TYPE => false,
                DATE_TYPE => false,
                DATETIME2_TYPE => false,
                DATETIMEOFFSET_TYPE => false,
                SMALLDATETIME_TYPE => false,
                DATETIME_TYPE => false,
                _ => true
            };
        }

        /// <summary>
        /// Keeps the shared typed-write rows. BINARY_FLOAT/BINARY_DOUBLE overflow on 2E35/2E150.
        /// TIMESTAMP WITH TIME ZONE is covered for the seeded non-UTC offset and for null;
        /// SQL Server's year 1/9999 literals stay out. RAW(2000) accepts the base64 rows.
        /// DATE, DATETIME, DATETIME2, and SMALLDATETIME are DATE or TIMESTAMP columns and are
        /// written as DateTime, so those rows run. A live mismatch stays a failure.
        /// Query rows still use <see cref="IsSupportedType"/> and stay inconclusive.
        /// </summary>
        protected override bool IsSupportedValue(string type, string value)
        {
            return IsTypedWriteSupported(type, value);
        }

        /// <summary>
        /// Offline gate for the typed-write matrices. DATE/DATETIME/DATETIME2/SMALLDATETIME
        /// return true for every seeded value, including null.
        /// </summary>
        internal static bool IsTypedWriteSupported(string type, string value)
        {
            if (type is DATE_TYPE or DATETIME_TYPE or DATETIME2_TYPE or SMALLDATETIME_TYPE)
            {
                return true;
            }

            if (IsExtremeMagnitude(type, value))
            {
                return false;
            }

            if (type == DATETIMEOFFSET_TYPE)
            {
                string trimmed = (value ?? string.Empty).Trim().Trim('"');
                return trimmed.Equals("null", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Contains("10:23:54+8:00", StringComparison.Ordinal);
            }

            return IsQueryTypeSupported(type);
        }

        /// <summary>
        /// Reads the seeded TIMESTAMP WITH TIME ZONE and RAW values, including the null row.
        /// Row 1 stores a +08:00 offset. Row 5 stores NULL in both columns.
        /// </summary>
        [DataTestMethod]
        [DataRow(DATETIMEOFFSET_TYPE, 1, DisplayName = "TSTZ round-trip of the +08:00 seed")]
        [DataRow(DATETIMEOFFSET_TYPE, 5, DisplayName = "TSTZ round-trip of the null seed")]
        [DataRow(BYTEARRAY_TYPE, 1, DisplayName = "RAW round-trip of the seeded payload")]
        [DataRow(BYTEARRAY_TYPE, 5, DisplayName = "RAW round-trip of the null seed")]
        public async Task Oracle_raw_and_timestamp_with_time_zone_round_trip(string type, int id)
        {
            string field = GetTestFieldName(type);
            string gqlQuery = "{ supportedType_by_pk(typeid: " + id + ") { typeid, " + field + " } }";
            string dbQuery = MakeQueryOnTypeTable(
                queryFields: new List<DabField> { new(alias: "typeid", backingColumnName: "id"), new(backingColumnName: field) },
                id: id);

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "supportedType_by_pk", isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            PerformTestEqualsForExtendedTypes(type, expected, actual.ToString());
        }

        [TestMethod]
        public Task Oracle_timestamp_with_time_zone_null_write()
        {
            return UpdateTypeColumn(DATETIMEOFFSET_TYPE, "null");
        }

        [TestMethod]
        public async Task Oracle_blob_round_trip_includes_null()
        {
            await QueryBlob(1);
            await QueryBlob(2);
        }

        private async Task QueryBlob(int id)
        {
            string gqlQuery = "{ blobType_by_pk(id: " + id + ") { id, payload } }";
            string dbQuery = $@"
                SELECT JSON_OBJECT(
                    'id' VALUE id,
                    'payload' VALUE {FormatColumnValue("payload")}
                ) AS data
                FROM (
                    SELECT id, payload
                    FROM blob_type_table
                    WHERE id = {id}
                    FETCH FIRST 1 ROWS ONLY
                ) subq";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "blobType_by_pk", isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// RAW and BLOB are base64-encoded in the engine's JSON_OBJECT projection.
        /// UTL_ENCODE.BASE64_ENCODE(NULL) raises ORA-29261, so null stays null.
        /// </summary>
        private static string FormatColumnValue(string columnName)
        {
            if (columnName is "bytearray_types" or "payload")
            {
                return $"CASE WHEN {columnName} IS NULL THEN NULL ELSE UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE({columnName})) END";
            }

            return columnName;
        }

        private static bool IsExtremeMagnitude(string type, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string normalized = value.Replace("+", string.Empty, StringComparison.Ordinal).Replace("\"", string.Empty, StringComparison.Ordinal);
            if (type == SINGLE_TYPE && normalized.Contains("2E35", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return type == FLOAT_TYPE && normalized.Contains("2E150", StringComparison.OrdinalIgnoreCase);
        }
    }
}
