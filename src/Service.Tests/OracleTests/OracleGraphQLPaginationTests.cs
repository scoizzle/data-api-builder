// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLPaginationTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for Oracle
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLPaginationTests : GraphQLPaginationTestBase
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

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow("typeid", 1, 3, "", "", false,
            DisplayName = "Test after token for primary key with mapped name.")]
        [DataRow("typeid", 4, 6, "", "", true,
            DisplayName = "Test after token for primary key with mapped name for last page.")]
        [DataRow("short_types", -32768, 1, 3, 1, false, DisplayName = "Test after token for short values.")]
        [DataRow("short_types", 1, "", 1, "", true, DisplayName = "Test after token for short values for last page.")]
        [DataRow("int_types", -2147483648, 1, 3, 1, false,
            DisplayName = "Test after token for int values.")]
        [DataRow("int_types", 1, "", 1, "", true,
            DisplayName = "Test after token for int values for last page.")]
        [DataRow("long_types", -9223372036854775808, 1, 3, 1, false,
            DisplayName = "Test after token for long values.")]
        [DataRow("long_types", 1, "", 1, "", true,
            DisplayName = "Test after token for long values for last page.")]
        [DataRow("string_types", "\"\"", "\"lksa;jdflasdf;alsdflksdfkldj\"", 1, 2, false,
            DisplayName = "Test after token for string values.")]
        [DataRow("string_types", "null", "", 3, "", true,
            DisplayName = "Test after token for string values for last page.")]
        [DataRow("single_types", -3.39E38, .33, 3, 1, false,
            DisplayName = "Test after token for single values.")]
        [DataRow("single_types", .33, "", 1, "", true,
            DisplayName = "Test after token for single values for last page.")]
        [DataRow("float_types", -1.7E308, .33, 3, 1, false,
            DisplayName = "Test after token for float values.")]
        [DataRow("float_types", .33, "", 1, "", true,
            DisplayName = "Test after token for float values for last page.")]
        [DataRow("decimal_types", -9.292929, 0.0000000000000292929, 2, 4, false,
            DisplayName = "Test after token for decimal values.")]
        [DataRow("decimal_types", 0.333333, "", 1, "", true,
            DisplayName = "Test after token for decimal values for last page.")]
        [DataRow("boolean_types", "0", "1", 2, 3, false,
            DisplayName = "Test after token for boolean values.")]
        [DataRow("boolean_types", "1", "", 3, "", true,
            DisplayName = "Test after token for boolean values for last page.")]
        [DataRow("datetime_types", "\"1753-01-01 00:00:00\"", "\"1999-01-08 10:23:00\"", 3, 1, false,
            DisplayName = "Test after token for datetime values.")]
        [DataRow("datetime_types", "\"1999-01-08 10:23:54\"", "", 1, "", true,
            DisplayName = "Test after token for datetime values for last page.")]
        [DataRow("bytearray_types", "\"AAAAAAA=\"", "\"mKt1EaqxIzQ=\"", 3, 2, false,
            DisplayName = "Test after token for bytearray values.")]
        [DataRow("bytearray_types", "\"q83vASM=\"", "", 1, "", true,
            DisplayName = "Test after token for bytearray values for last page.")]
        public override Task TestPaginationOrderBySortingWithAfterTokenPresentInURL(
            string orderByQueryParamValue,
            object afterValueForOrderByColumn,
            object afterValueForPK,
            object expectedFirstResultOrderByCol,
            object expectedFirstResultPK,
            bool expectEmptyResults)
        {
            return base.TestPaginationOrderBySortingWithAfterTokenPresentInURL(
                orderByQueryParamValue,
                afterValueForOrderByColumn,
                afterValueForPK,
                expectedFirstResultOrderByCol,
                expectedFirstResultPK,
                expectEmptyResults);
        }

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow("typeid", "asc", 1, DisplayName = "Test $first with $orderby for primary key with mapped name.")]
        [DataRow("short_types", "asc", -32768,
            DisplayName = "Test $first with $orderby for short type values.")]
        [DataRow("int_types", "asc", -2147483648,
            DisplayName = "Test $first with $orderby for int type values.")]
        [DataRow("long_types", "asc", -9223372036854775808,
            DisplayName = "Test $first with $orderby for long type values.")]
        [DataRow("string_types", "asc", "", DisplayName = "Test $first with $orderby for string type values.")]
        [DataRow("single_types", "asc", -3.39E38,
            DisplayName = "Test $first with $orderby for single type values.")]
        [DataRow("float_types", "asc", -1.7E308,
            DisplayName = "Test $first with $orderby for float type values.")]
        [DataRow("decimal_types", "asc", -9.292929,
            DisplayName = "Test $first with $orderby for decimal type values.")]
        [DataRow("boolean_types", "asc", false,
            DisplayName = "Test $first with $orderby for boolean type values.")]
        [DataRow("datetime_types", "asc", "1753-01-01 00:00:00.000",
            DisplayName = "Test $first with $orderby for datetime type values.")]
        [DataRow("bytearray_types", "asc", "AAAAAAA=",
            DisplayName = "Test $first with $orderby for bytearray type values.")]
        [DataRow("typeid", "desc", 5, DisplayName = "Test $first with $orderby(desc) for primary key with mapped name.")]
        [DataRow("short_types", "desc", 32767, DisplayName = "Test $first with $orderby(desc) for short type values.")]
        [DataRow("int_types", "desc", 2147483647, DisplayName = "Test $first with $orderby(desc) for int type values.")]
        [DataRow("long_types", "desc", 9223372036854775807,
            DisplayName = "Test $first with $orderby(desc) for long type values.")]
        [DataRow("string_types", "desc", "null", DisplayName = "Test $first with $orderby(desc) for string type values.")]
        [DataRow("single_types", "desc", 3.39E38, DisplayName = "Test $first with $orderby(desc) for single type values.")]
        [DataRow("float_types", "desc", 1.7E308, DisplayName = "Test $first with $orderby(desc) for float type values.")]
        [DataRow("decimal_types", "desc", 0.333333,
            DisplayName = "Test $first with $orderby(desc) for decimal type values.")]
        [DataRow("boolean_types", "desc", true, DisplayName = "Test $first with $orderby(desc) for boolean type values.")]
        [DataRow("datetime_types", "desc", "9999-12-31 23:59:59",
            DisplayName = "Test $first with $orderby(desc) for datetime type values.")]
        [DataRow("bytearray_types", "desc", "/////w==",
            DisplayName = "Test $first with $orderby(desc) for bytearray type values.")]
        public override Task TestFirstWithOrderByForSupportedTypes(
            string orderByColumn,
            string order,
            object firstRecordOrderByValue)
        {
            return base.TestFirstWithOrderByForSupportedTypes(orderByColumn, order, firstRecordOrderByValue);
        }
    }
}
