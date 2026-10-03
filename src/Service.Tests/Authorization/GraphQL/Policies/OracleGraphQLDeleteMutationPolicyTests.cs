// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Delete
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Delete Mutations for Oracle,
    /// mirroring the MSSQL/PostgreSQL/MySQL coverage in this folder.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLDeleteMutationPolicyTests : GraphQLDeleteMutationDatabasePolicyTestBase
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
        /// Tests Authenticated GraphQL Delete Mutation which triggers
        /// policy processing. Tests deleteBook with policy that
        /// allows/prevents operation.
        /// - Operation allowed: confirm record deleted.
        /// - Operation forbidden: confirm record not deleted.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation_Policy()
        {
            string dbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)), JSON_ARRAY()) AS data
                FROM (SELECT id, title FROM books
                      WHERE id = 9 AND title = 'Policy-Test-01'
                      FETCH FIRST 100 ROWS ONLY)";

            await DeleteMutation_Policy(dbQuery);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }
    }
}