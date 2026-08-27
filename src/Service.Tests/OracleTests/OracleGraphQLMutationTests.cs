// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Test GraphQL Mutations validating proper resolver/engine operation for Oracle.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLMutationTests : GraphQLMutationTestBase
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
        /// Runs after every test to reset the database state
        /// </summary>
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
                    'id' VALUE id, 'title' VALUE title,
                    'publisher_id' VALUE publisher_id
                ) AS data
                FROM (
                    SELECT id, title, publisher_id FROM books
                    WHERE id = 5001 AND title = 'My New Book' AND publisher_id = 1234
                    ORDER BY id ASC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutation(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title,
                    'publisher_id' VALUE publisher_id
                ) AS data
                FROM (
                    SELECT id, title, publisher_id FROM books
                    WHERE id = 5001 AND title = 'My New Book' AND publisher_id = 1234
                    ORDER BY id ASC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutationWithVariables(oracleQuery);
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
        public async Task DeleteMutation()
        {
            string oracleQueryForResult = @"
                SELECT JSON_OBJECT(
                    'title' VALUE title, 'publisher_id' VALUE publisher_id
                ) AS data
                FROM (
                    SELECT title, publisher_id FROM books WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";

            string oracleQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count FROM books WHERE id = 1";

            await DeleteMutation(oracleQueryForResult, oracleQueryToVerifyDeletion);
        }

        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books
                    WHERE title = 'My New Book' AND publisher_id = 1234
                    ORDER BY id DESC FETCH FIRST 1 ROWS ONLY
                )";
            await NestedQueryingInMutation(oracleQuery);
        }

        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number
                ) AS data
                FROM (
                    SELECT id, title, issue_number FROM magazines
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
                    SELECT id, title, issue_number FROM magazines
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
                    SELECT id, issue_number FROM magazines
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
                    SELECT id, title, issue_number FROM magazines
                    WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";
            await TestMissingColumnNotUpdatedToNull(oracleQuery);
        }

        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books
                    WHERE id = 5001 AND title = 'My New Book'
                    ORDER BY id ASC FETCH FIRST 1 ROWS ONLY
                )";
            await TestAliasSupportForGraphQLMutationQueryFields(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'column1' VALUE column1, 'column2' VALUE column2
                ) AS data
                FROM (
                    SELECT column1, column2 FROM GQLmappings
                    WHERE column1 = 2 FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutationWithVariablesAndMappings(oracleQuery);
        }

        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'column1' VALUE column1, 'column2' VALUE column2
                ) AS data
                FROM (
                    SELECT column1, column2 FROM GQLmappings
                    WHERE column1 = 3 FETCH FIRST 1 ROWS ONLY
                )";
            await UpdateMutationWithVariablesAndMappings(oracleQuery);
        }

        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string oracleQueryForResult = @"
                SELECT JSON_OBJECT(
                    'column1' VALUE column1, 'column2' VALUE column2
                ) AS data
                FROM (
                    SELECT column1, column2 FROM GQLmappings
                    WHERE column1 = 4 FETCH FIRST 1 ROWS ONLY
                )";

            string oracleQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count FROM GQLmappings WHERE column1 = 4";

            await DeleteMutationWithVariablesAndMappings(oracleQueryForResult, oracleQueryToVerifyDeletion);
        }

        [TestMethod]
        public async Task InsertMutationOnTableWithTriggerWithNonAutoGenPK()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'months' VALUE months, 'name' VALUE name, 'salary' VALUE salary
                ) AS data
                FROM (
                    SELECT id, months, name, salary FROM intern_data
                    WHERE id = 4 AND months = 1 FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutationOnTableWithTriggerWithNonAutoGenPK(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationOnTableWithTriggerWithAutoGenPK()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'u_id' VALUE u_id, 'name' VALUE name,
                    'position' VALUE position, 'salary' VALUE salary
                ) AS data
                FROM (
                    SELECT id, u_id, name, position, salary FROM fte_data
                    WHERE name = 'Joel'
                    ORDER BY id DESC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertMutationOnTableWithTriggerWithAutoGenPK(oracleQuery);
        }

        [TestMethod]
        public async Task UpdateMutationOnTableWithTriggerWithNonAutoGenPK()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'months' VALUE months, 'name' VALUE name, 'salary' VALUE salary
                ) AS data
                FROM (
                    SELECT id, months, name, salary FROM intern_data
                    WHERE id = 1 AND months = 3 FETCH FIRST 1 ROWS ONLY
                )";
            await UpdateMutationOnTableWithTriggerWithNonAutoGenPK(oracleQuery);
        }

        [TestMethod]
        public async Task UpdateMutationOnTableWithTriggerWithAutoGenPK()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'u_id' VALUE u_id, 'name' VALUE name,
                    'position' VALUE position, 'salary' VALUE salary
                ) AS data
                FROM (
                    SELECT id, u_id, name, position, salary FROM fte_data
                    WHERE id = 1 AND u_id = 2 FETCH FIRST 1 ROWS ONLY
                )";
            await UpdateMutationOnTableWithTriggerWithAutoGenPK(oracleQuery);
        }

        [TestMethod]
        public async Task InsertIntoSimpleView()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books
                    WHERE title = 'Book View' AND publisher_id = 1234
                    ORDER BY id DESC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertIntoSimpleView(oracleQuery);
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
        public async Task DeleteFromSimpleView()
        {
            string oracleQueryForResult = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title
                ) AS data
                FROM (
                    SELECT id, title FROM books_view_all
                    WHERE id = 1 FETCH FIRST 1 ROWS ONLY
                )";

            string oracleQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count FROM books_view_all WHERE id = 1";

            await DeleteFromSimpleView(oracleQueryForResult, oracleQueryToVerifyDeletion);
        }

        [TestMethod]
        public async Task InsertIntoInsertableComplexView()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id
                ) AS data
                FROM (
                    SELECT id, title, publisher_id FROM books
                    WHERE title = 'Book Complex View' AND publisher_id = 1234
                    ORDER BY id DESC FETCH FIRST 1 ROWS ONLY
                )";
            await InsertIntoInsertableComplexView(oracleQuery);
        }

        [TestMethod]
        public async Task InsertMutationForNonGraphQLTypeTable()
        {
            string oracleQuery = @"
                SELECT COUNT(*) AS count
                FROM book_author_link WHERE author_id = 123 AND book_id = 2";
            await InsertMutationForNonGraphQLTypeTable(oracleQuery);
        }



        #endregion

        #region Negative Tests

        [TestMethod]
        public async Task InsertWithInvalidForeignKey()
        {
            string oracleQuery = @"
                SELECT COUNT(*) AS count FROM books WHERE publisher_id = -1";
            string errorMessage = "The given value for field publisher_id is not valid";
            await InsertWithInvalidForeignKey(oracleQuery, errorMessage);
        }

        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string oracleQuery = @"
                SELECT COUNT(*) AS count FROM books WHERE id = 1 AND publisher_id = -1";
            string errorMessage = "The given value for field publisher_id is not valid";
            await UpdateWithInvalidForeignKey(oracleQuery, errorMessage);
        }

        #endregion
    }
}
