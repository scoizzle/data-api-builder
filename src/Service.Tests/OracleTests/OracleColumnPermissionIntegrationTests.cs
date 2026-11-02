// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Live-Oracle checks for column-level read permissions on the Book entity (table BOOKS,
    /// role test_role_with_excluded_fields excludes publisher_id on read). Offline coverage of the
    /// permission normalization lives in UnitTests.OracleColumnPermissionTests.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleColumnPermissionIntegrationTests : SqlTestBase
    {
        private const string EXCLUDING_ROLE = "test_role_with_excluded_fields";

        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestMethod]
        public async Task Read_ExcludesConfiguredColumn_KeepsOthers()
        {
            string oracleQuery =
                "SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data " +
                "FROM (SELECT * FROM books WHERE id = 1 FETCH FIRST 1 ROWS ONLY)";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: "Book",
                sqlQuery: oracleQuery,
                operationType: EntityActionOperation.Read,
                clientRoleHeader: EXCLUDING_ROLE);
        }

        [TestMethod]
        public async Task Read_SelectExcludedColumn_ReturnsForbidden()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?$select=publisher_id",
                entityNameOrPath: "Book",
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Read,
                exceptionExpected: true,
                expectedErrorMessage: DataApiBuilderException.AUTHORIZATION_FAILURE,
                expectedStatusCode: HttpStatusCode.Forbidden,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                clientRoleHeader: EXCLUDING_ROLE);
        }
    }
}
