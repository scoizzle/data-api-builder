// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Update
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLUpdateMutationPolicyTests : GraphQLUpdateMutationDatabasePolicyTestBase
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
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that allows operation.
        /// Check: Record updated.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation_Success_Policy()
        {
            string dbQuery = @"
                SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                FROM (SELECT id, title FROM books
                      WHERE (title = 'UpdatedBookTitle') AND id = 9
                      FETCH FIRST 1 ROWS ONLY)";

            await UpdateMutation_Success_Policy(dbQuery, roleName: "policy_tester_08", isAuthenticated: true);
        }

        /// <summary>
        /// Do: Test Authenticated GraphQL Update Mutation which triggers
        /// policy processing of policy that prevents operation.
        /// Check: Record not updated.
        /// </summary>
        [DataTestMethod]
        [DataRow("policy_tester_noupdate", true, "Could not find item with", false, DisplayName = "Update Mutation Prohibited by Policy")]
        [DataRow("policy_tester_update_noread", true, "The current user is not authorized to access this resource", true, DisplayName = "Update Mutation Succeeds, Disallowed Post-Update READ")]
        public async Task UpdateMutation_ErrorMessage_Policy(string roleName, bool isAuthenticated, string expectedErrorMessage, bool mutationShouldComplete)
        {
            string dbQuery = @"
                SELECT JSON_OBJECT('id' VALUE id, 'journalname' VALUE journalname) AS data
                FROM (SELECT id, journalname FROM journals
                      WHERE (journalname = 'UpdatedJournalName') AND id = 1
                      FETCH FIRST 1 ROWS ONLY)";

            await UpdateMutation_ErrorMessage_Policy(dbQuery, roleName, isAuthenticated, expectedErrorMessage, mutationShouldComplete);
        }

        /// <summary>
        /// Do: Test Anonymous GraphQL Update Mutation which triggers
        /// policy processing of policy that prevents operation.
        /// Check: Record not updated.
        /// </summary>
        [DataTestMethod]
        [DataRow("anonymous", false, null, true, 1, DisplayName = "Anonymous Update Mutation Succeeds, Disallowed Post-Update READ")]
        [DataRow("anonymous", false, null, true, 2, DisplayName = "Anonymous Update Mutation Succeeds, Allowed Post-Update READ")]
        public async Task UpdateMutation_Anonymous_Policy(string roleName, bool isAuthenticated, string expectedErrorMessage, bool mutationShouldComplete, int id)
        {
            string dbQuery = @"
                SELECT JSON_OBJECT('id' VALUE id, 'notebookname' VALUE notebookname) AS data
                FROM (SELECT id, notebookname FROM notebooks
                      WHERE (notebookname = 'UpdatedNoteBookName') AND id = " + id + @"
                      FETCH FIRST 1 ROWS ONLY)";

            await UpdateMutation_Anonymous_Policy(dbQuery, roleName, isAuthenticated, expectedErrorMessage, mutationShouldComplete, id);
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