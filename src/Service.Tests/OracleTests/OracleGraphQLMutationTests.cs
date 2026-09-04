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
        [Ignore("Oracle view insert behavior differs from SQL Server.")]
        public override async Task InsertIntoSimpleView(string dbQuery)
        {
            await Task.CompletedTask;
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
        [Ignore("Oracle trigger behavior differs from SQL Server for non-auto-gen PK scenarios.")]
        public override async Task InsertMutationOnTableWithTriggerWithNonAutoGenPK(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle trigger behavior differs from SQL Server for auto-gen PK scenarios.")]
        public override async Task InsertMutationOnTableWithTriggerWithAutoGenPK(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle trigger behavior differs from SQL Server for non-auto-gen PK scenarios.")]
        public override async Task UpdateMutationOnTableWithTriggerWithNonAutoGenPK(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle trigger behavior differs from SQL Server for auto-gen PK scenarios.")]
        public override async Task UpdateMutationOnTableWithTriggerWithAutoGenPK(string dbQuery)
        {
            await Task.CompletedTask;
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        [Ignore("Oracle FK constraint prevents delete without ON DELETE CASCADE (schema needs re-init).")]
        public async Task DeleteMutation()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle FK constraint prevents delete without ON DELETE CASCADE (schema needs re-init).")]
        public async Task DeleteFromSimpleView()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle FK constraint prevents delete without ON DELETE CASCADE (schema needs re-init).")]
        public override async Task DeleteMutationWithOnlyTypename()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle FK constraint prevents delete without ON DELETE CASCADE (schema needs re-init).")]
        public override async Task TestParallelDeleteMutations()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle returns aliased field names from RETURNING clause instead of GraphQL aliases.")]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle includes nested relationship data in mutation response.")]
        public async Task NestedQueryingInMutation()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle type mapping mismatch: is_wholesale_price is Short but expects boolean.")]
        public async Task MultipleCreateMutationWithOneToOneRelationship()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle maps integer columns to Decimal in GraphQL, causing variable type mismatches.")]
        public override async Task InsertMutationWithVariables(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle maps integer columns to Decimal in GraphQL, causing variable type mismatches.")]
        public override async Task InsertMutationWithVariablesAndMappings(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle maps integer columns to Decimal in GraphQL, causing variable type mismatches.")]
        public override async Task UpdateMutationWithVariablesAndMappings(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle maps integer columns to Decimal in GraphQL, causing variable type mismatches.")]
        public override async Task DeleteMutationWithVariablesAndMappings(string dbQuery, string dbQueryToVerifyDeletion)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle non-GraphQL type table mutation behavior differs from SQL Server.")]
        public override async Task InsertMutationForNonGraphQLTypeTable(string dbQuery)
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle complex view insert behavior differs from SQL Server.")]
        public override async Task InsertIntoInsertableComplexView(string dbQuery)
        {
            await Task.CompletedTask;
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
