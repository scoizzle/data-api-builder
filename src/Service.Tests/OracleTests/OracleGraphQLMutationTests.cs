// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLMutationTests : GraphQLMutationTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #region Positive Tests

        [TestMethod]
        public async Task InsertMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books
                    WHERE id = 5001 AND title = 'My New Book' AND publisher_id = 1234
                    ORDER BY id ASC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutation(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationForComputedColumns()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'item_name' VALUE item_name,
                    'subtotal' VALUE subtotal, 'tax' VALUE tax, 'total' VALUE total
                ) AS data
                FROM (
                    SELECT id, item_name, subtotal, tax, total FROM sales
                    WHERE id = 5001 AND item_name = 'headphones'
                    ORDER BY id ASC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutationForComputedColumns(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationForConstantdefaultValue()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'content' VALUE content
                ) AS data
                FROM (
                    SELECT id, content FROM reviews
                    WHERE book_id = 1 AND content = 'Its a classic'
                    ORDER BY id DESC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutationForConstantdefaultValue(oracleQuery);
        }

        [TestMethod]
        public async Task UpdateMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'title' VALUE title, 'publisher_id' VALUE publisher_id
                ) AS data
                FROM (
                    SELECT title, publisher_id FROM books WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";
            await UpdateMutation(oracleQuery);
        }

        [TestMethod]
        public async Task UpdateMutationForComputedColumns()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'item_name' VALUE item_name,
                    'subtotal' VALUE subtotal, 'tax' VALUE tax, 'total' VALUE total
                ) AS data
                FROM (
                    SELECT id, item_name, subtotal, tax, total FROM sales
                    WHERE id = 2 FETCH FIRST 1 ROWS ONLY
                )";
            await UpdateMutationForComputedColumns(oracleQuery);
        }

        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number
                ) AS data
                FROM (
                    SELECT id, title, issue_number FROM foo.magazines
                    WHERE id = 800 FETCH FIRST 1 ROWS ONLY
                )";
            await TestExplicitNullInsert(oracleQuery);
        }

        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number
                ) AS data
                FROM (
                    SELECT id, title, issue_number FROM foo.magazines
                    WHERE id = 801 FETCH FIRST 1 ROWS ONLY
                )";
            await TestImplicitNullInsert(oracleQuery);
        }

        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'issue_number' VALUE issue_number
                ) AS data
                FROM (
                    SELECT id, issue_number FROM foo.magazines
                    WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";
            await TestUpdateColumnToNull(oracleQuery);
        }

        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number
                ) AS data
                FROM (
                    SELECT id, title, issue_number FROM foo.magazines
                    WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";
            await TestMissingColumnNotUpdatedToNull(oracleQuery);
        }

        [TestMethod]
        public async Task InsertIntoSimpleView()
        {
            // Oracle views do not expose the underlying identity attribute, so the generated
            // GraphQL input type marks books_view_all.id as required. Supply the PK explicitly
            // (Oracle still auto-generates when REST omits it; GraphQL requires the field).
            string graphQLMutationName = "createbooks_view_all";
            string graphQLMutation = @"
                mutation {
                    createbooks_view_all(item: { id: 5001, title: ""Book View"", publisher_id: 1234 }) {
                        id
                        title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books_view_all
                    WHERE id = 5001 AND title = 'Book View' AND publisher_id = 1234
                    ORDER BY id
                    FETCH FIRST 1 ROWS ONLY
                )";
            string expected = await GetDatabaseResultAsync(oracleQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task UpdateSimpleView()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books_view_all
                    WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";
            await UpdateSimpleView(oracleQuery);
        }

        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title,
                    'publishers' VALUE table1_subq.data
                ) AS data
                FROM books table0
                LEFT OUTER JOIN LATERAL (
                    SELECT JSON_OBJECT('name' VALUE table1.name) AS data
                    FROM publishers table1
                    WHERE table1.id = table0.publisher_id
                    FETCH FIRST 1 ROWS ONLY
                ) table1_subq ON (1=1)
                WHERE table0.title = 'My New Book' AND table0.publisher_id = 1234
                FETCH FIRST 1 ROWS ONLY";
            await NestedQueryingInMutation(oracleQuery);
        }

        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('book_id' VALUE id, 'book_title' VALUE title) AS data
                FROM (
                    SELECT id, title FROM books
                    WHERE title = 'My New Book' AND publisher_id = 1234
                    FETCH FIRST 1 ROWS ONLY
                )";
            await TestAliasSupportForGraphQLMutationQueryFields(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                FROM (
                    SELECT id, title FROM books
                    WHERE title = 'My New Book' AND publisher_id = 1234
                    FETCH FIRST 1 ROWS ONLY
                )";
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation($title: String!, $publisher_id: Decimal!) {
                    createbook(item: { title: $title, publisher_id: $publisher_id }) {
                        id
                        title
                    }
                }
            ";
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                new() { { "title", "My New Book" }, { "publisher_id", 1234 } });
            string expected = await GetDatabaseResultAsync(oracleQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('column1' VALUE ""__column1"", 'column2' VALUE ""__column2"") AS data
                FROM (
                    SELECT ""__column1"", ""__column2"" FROM GQLmappings
                    WHERE ""__column1"" = 2
                    FETCH FIRST 1 ROWS ONLY
                )";
            string graphQLMutationName = "createGQLmappings";
            string graphQLMutation = @"
                mutation($id: Decimal!, $col2Value: String) {
                    createGQLmappings(item: { column1: $id, column2: $col2Value }) {
                        column1
                        column2
                    }
                }
            ";
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                new() { { "id", 2 }, { "col2Value", "My New Value" } });
            string expected = await GetDatabaseResultAsync(oracleQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('column1' VALUE ""__column1"", 'column2' VALUE ""__column2"") AS data
                FROM (
                    SELECT ""__column1"", ""__column2"" FROM GQLmappings
                    WHERE ""__column1"" = 3 AND ""__column2"" = 'Updated Value of Mapped Column'
                    FETCH FIRST 1 ROWS ONLY
                )";
            string graphQLMutationName = "updateGQLmappings";
            string graphQLMutation = @"
                mutation($id: Decimal!, $col2Value: String) {
                    updateGQLmappings(column1: $id, item: { column2: $col2Value }) {
                        column1
                        column2
                    }
                }
            ";
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                new() { { "id", 3 }, { "col2Value", "Updated Value of Mapped Column" } });
            string expected = await GetDatabaseResultAsync(oracleQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        [TestMethod]
        public async Task InsertMutationFailingDatabasePolicy()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data
                FROM publishers WHERE name = 'New publisher'";
            await InsertMutationFailingDatabasePolicy(
                dbQuery: oracleQuery,
                errorMessage: "Could not insert row with given values for entity: Publisher",
                roleName: "database_policy_tester",
                graphQLMutationName: "createPublisher",
                graphQLMutationPayload: @"
                mutation {
                    createPublisher(item: { name: ""New publisher"" }) {
                        id
                        name
                    }
                }");
        }

        [TestMethod]
        public async Task InsertMutationWithDatabasePolicy()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data
                FROM publishers WHERE name = 'Not New publisher'";
            await InsertMutationWithDatabasePolicy(
                dbQuery: oracleQuery,
                roleName: "database_policy_tester",
                graphQLMutationName: "createPublisher",
                graphQLMutationPayload: @"
                mutation {
                    createPublisher(item: { name: ""Not New publisher"" }) {
                        id
                        name
                    }
                }");
        }

        [TestMethod]
        public async Task InsertWithInvalidForeignKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data
                FROM books WHERE title = 'My New Book' AND publisher_id = -1";
            await InsertWithInvalidForeignKey(oracleQuery, "ORA-02291");
        }

        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data
                FROM books WHERE id = 1 AND publisher_id = -1";
            await UpdateWithInvalidForeignKey(oracleQuery, "ORA-02291");
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        public async Task DeleteMutation()
        {
            string oracleQueryForResult = @"
                SELECT JSON_OBJECT('title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                FROM (SELECT title, publisher_id FROM books WHERE id = 1 FETCH FIRST 1 ROWS ONLY)";
            string oracleQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data FROM books WHERE id = 1";
            await DeleteMutation(oracleQueryForResult, oracleQueryToVerifyDeletion);
        }

        [TestMethod]
        public async Task DeleteFromSimpleView()
        {
            string oracleQueryForResult = @"
                SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                FROM (SELECT id, title FROM books_view_all WHERE id = 1 FETCH FIRST 1 ROWS ONLY)";
            string oracleQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data FROM books_view_all WHERE id = 1";
            await DeleteFromSimpleView(oracleQueryForResult, oracleQueryToVerifyDeletion);
        }

        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('column1' VALUE ""__column1"", 'column2' VALUE ""__column2"") AS data
                FROM (
                    SELECT ""__column1"", ""__column2"" FROM GQLmappings
                    WHERE ""__column1"" = 4
                    FETCH FIRST 1 ROWS ONLY
                )";
            string oracleQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count' VALUE COUNT(*)) AS data
                FROM GQLmappings WHERE ""__column1"" = 4";
            string graphQLMutationName = "deleteGQLmappings";
            string graphQLMutation = @"
                mutation($id: Decimal!) {
                    deleteGQLmappings(column1: $id) {
                        column1
                        column2
                    }
                }
            ";
            string expected = await GetDatabaseResultAsync(oracleQuery);
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                new() { { "id", 4 } });
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            string dbResponse = await GetDatabaseResultAsync(oracleQueryToVerifyDeletion);
            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(0, result.RootElement.GetProperty("count").GetInt64());
        }

        [TestMethod]
        public override async Task TestTryInsertMutationForVariableNotNullDefault()
        {
            string graphQLMutationName = "createSupportedType";
            string graphQLMutation = @"
                mutation {
                    createstocks_price(item: { categoryid: 100 pieceid: 99 instant: null } ) {
                    categoryid
                    pieceid
                    instant
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(
                actual.ToString(),
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseInputError}");
        }

        #endregion
    }
}
