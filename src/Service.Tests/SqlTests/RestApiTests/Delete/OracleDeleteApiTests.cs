// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Delete
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleDeleteApiTests : DeleteApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "DeleteOneTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5
                    ) subq
                "
            }
        };

        [TestMethod]
        [Ignore("Negative test asserting a delete against a multi-table view is rejected. Oracle permits DELETE on books_publishers_view_composite (the key-preserved base-table row is deleted and the response is 204), so the expected bad-request never occurs; MySQL also skips this test (its multi-table views are read-only).")]
        public new async Task DeleteOneInViewBadRequestTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/pub_id/1234",
                queryString: null,
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Delete,
                exceptionExpected: true,
                expectedErrorMessage: "ORA-",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        [TestMethod]
        public new async Task DeleteOneInViewTest()
        {
            // Delete one from view based on books table.
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _simple_all_books,
                    sqlQuery: null,
                    operationType: EntityActionOperation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );

            // Delete one from view based on stocks table. Oracle enforces the
            // stocks_price -> stocks foreign key (ORA-02292), so target the seeded
            // stock row that has no child price row (categoryid/0/pieceid/1).
            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _simple_subset_stocks,
                    sqlQuery: null,
                    operationType: EntityActionOperation.Delete,
                    requestBody: null,
                    expectedStatusCode: HttpStatusCode.NoContent
                );
        }

        [TestMethod]
        public new async Task DeleteWithInvalidPrimaryKeyTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "title/7",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Delete,
                requestBody: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "The request is invalid since the primary keys: TITLE requested were not found in the entity definition.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField.ToString()
            );
        }

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
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

        #endregion

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        [TestMethod]
        public new async Task DeleteWithUncastablePKValue()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/{}",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Delete,
                requestBody: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"{}\" cannot be resolved as column \"ID\" with type \"Decimal\".",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
            );
        }
    }
}
