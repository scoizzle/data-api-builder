// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        [DataRow(STRING_TYPE, "lksa;jdflasdf;alsdflksdfkldj", "\"lksa;jdflasdf;alsdflksdfkldj\"")]
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
        [DataRow(INT_TYPE, "gt", "-2147000000", "-2147000000", ">")]
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
        [DataRow(STRING_TYPE, "gt", "'a'", "\"a\"", ">")]
        [DataRow(STRING_TYPE, "gte", "''", "\"\"", ">=")]
        [DataRow(STRING_TYPE, "lt", "'null'", "\"null\"", "<")]
        [DataRow(STRING_TYPE, "lte", "'null'", "\"null\"", "<=")]
        [DataRow(STRING_TYPE, "neq", "'null'", "\"null\"", "!=")]
        [DataRow(STRING_TYPE, "eq", "''", "\"\"", "=")]
        [DataRow(FLOAT_TYPE, "gt", "-1.8E308", "-1.8E308", ">")]
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
            string formattedSelect = limit.Equals("1") ? 
                "SELECT JSON_OBJECT(" : 
                "SELECT JSON_ARRAYAGG(JSON_OBJECT(";

            string jsonFields = string.Join(", ", 
                queryFields.Select(field => $"'{field.Alias}' VALUE {field.BackingColumnName}"));

            string closingBracket = limit.Equals("1") ? ")" : "))";

            return $@"
                {formattedSelect}
                    {jsonFields}
                {closingBracket} AS data
                FROM (
                    SELECT {string.Join(", ", queryFields.Select(field => field.BackingColumnName))}
                    FROM type_table table0
                    WHERE {filterField} {filterOperator} {filterValue}
                    ORDER BY {orderBy} ASC
                    FETCH FIRST {limit} ROWS ONLY
                ) subq
            ";
        }
    }
}
