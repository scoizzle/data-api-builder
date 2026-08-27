// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation for Oracle.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLQueryTests : GraphQLQueryTestBase
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

        #region Tests

        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (
                    SELECT id, title FROM books ORDER BY id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQuery(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryContainingComputedColumns()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'item_name' VALUE item_name,
                        'subtotal' VALUE subtotal, 'tax' VALUE tax, 'total' VALUE total)
                ) AS data
                FROM (
                    SELECT id, item_name, subtotal, tax, total FROM sales ORDER BY id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQueryContainingComputedColumns(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (
                    SELECT id, title FROM books ORDER BY id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQueryWithVariables(oracleQuery);
        }

        [TestMethod]
        public async Task InQueryWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (
                    SELECT id, title FROM books WHERE id IN (1, 2) ORDER BY id ASC
                )";
            await InQueryWithVariables(oracleQuery);
        }

        [TestMethod]
        public async Task InQueryWithNullAndEmptyvalues()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('string_types' VALUE string_types)
                ) AS data
                FROM (
                    SELECT string_types FROM type_table
                    WHERE string_types IN ('lksa;jdflasdf;alsdflksdfkldj', ' ', NULL)
                )";
            await InQueryWithNullAndEmptyvalues(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithMappings()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('column1' VALUE ""__column1"", 'column2' VALUE ""__column2"")
                ) AS data
                FROM (
                    SELECT ""__column1"", ""__column2"" FROM GQLmappings ORDER BY ""__column1"" ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQueryWithMappings(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('title' VALUE title) AS data
                FROM (SELECT title FROM books WHERE id = 2 FETCH FIRST 1 ROWS ONLY)";
            await QueryWithSingleColumnPrimaryKey(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('column1' VALUE column1) AS data
                FROM (SELECT column1 FROM GQLmappings WHERE column1 = 1 FETCH FIRST 1 ROWS ONLY)";
            await QueryWithSingleColumnPrimaryKeyAndMappings(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('content' VALUE content) AS data
                FROM (SELECT content FROM reviews WHERE id = 568 AND book_id = 1 FETCH FIRST 1 ROWS ONLY)";
            await QueryWithMultipleColumnPrimaryKey(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithNullableForeignKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'title' VALUE title,
                    'myseries' VALUE (
                        SELECT JSON_OBJECT('name' VALUE name)
                        FROM series WHERE id = comics.series_id
                    )
                ) AS data
                FROM comics WHERE id = 1";
            await QueryWithNullableForeignKey(oracleQuery);
        }

        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY title DESC FETCH FIRST 100 ROWS ONLY)";
            await TestOrderByInListQuery(oracleQuery);
        }

        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'content' VALUE content)
                ) AS data
                FROM (SELECT id, content FROM reviews ORDER BY content ASC, id DESC FETCH FIRST 100 ROWS ONLY)";
            await TestOrderByInListQueryOnCompPkType(oracleQuery);
        }

        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 2 ROWS ONLY)";
            await TestAliasSupportForGraphQLQueryFields(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 2 ROWS ONLY)";
            await TestSupportForMixOfRawDbFieldFieldAndAlias(oracleQuery);
        }

        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number)
                ) AS data
                FROM (SELECT id, title, issue_number FROM magazines FETCH FIRST 100 ROWS ONLY)";
            await TestQueryingTypeWithNullableIntFields(oracleQuery);
        }

        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'username' VALUE username)
                ) AS data
                FROM (SELECT id, username FROM website_users FETCH FIRST 100 ROWS ONLY)";
            await TestQueryingTypeWithNullableStringFields(oracleQuery);
        }

        [TestMethod]
        public async Task TestQueryingTypeWithNullableDateTimeFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('datetime_types' VALUE datetime_types)
                ) AS data
                FROM (SELECT datetime_types FROM type_table ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestQueryingTypeWithNullableDateTimeFields(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryWithExplicitlyNullArguments()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestQueryWithExplicitlyNullArguments(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryOnBasicView()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books_view_all ORDER BY id ASC FETCH FIRST 5 ROWS ONLY)";
            await TestQueryOnBasicView(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryOnCompositeView()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'name' VALUE name)
                ) AS data
                FROM (SELECT id, name FROM books_publishers_view_composite ORDER BY id ASC FETCH FIRST 5 ROWS ONLY)";
            await TestQueryOnCompositeView(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryWithNullResult()
        {
            await TestQueryWithNullResult();
        }

        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY title DESC, id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestNullFieldsInOrderByAreIgnored(oracleQuery);
        }

        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(oracleQuery);
        }



        #endregion
    }
}
