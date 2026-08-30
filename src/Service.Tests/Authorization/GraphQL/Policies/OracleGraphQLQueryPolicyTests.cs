// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries for Oracle,
    /// mirroring the MSSQL/PostgreSQL/MySQL coverage in this folder.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLQueryPolicyTests : GraphQLQueryDatabasePolicyTestBase
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
        /// Tests Authenticated GraphQL Queries which trigger
        /// policy processing. Tests QueryByPK with policies that
        /// filter results:
        /// - To 0 records to detect expected null result
        /// - To 1 record to validate result returns as expected.
        /// </summary>
        [TestMethod]
        public async Task QueryByPK_Policy()
        {
            string dbQuery = @"
                SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                FROM (SELECT id, title FROM books
                      WHERE (title = 'Policy-Test-01') AND id = 9
                      FETCH FIRST 1 ROWS ONLY)";

            await QueryByPK_Policy(dbQuery);
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records,
        /// but does not include any nested queries.
        /// When a policy is applied to such top-level query, results are restricted
        /// to the expected records.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_Policy()
        {
            // Tests Book Read Policy: @item.title ne 'Policy-Test-01'
            // Due to restrictive book policy, expects all book records except:
            // id: 9 title: 'Policy-Test-01'
            string dbQuery = @"
                SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) AS data
                FROM (SELECT id, title FROM books
                      WHERE (title != 'Policy-Test-01')
                      ORDER BY id ASC
                      FETCH FIRST 100 ROWS ONLY)";

            string clientRole = "policy_tester_02";
            await QueryMany_Policy(dbQuery, clientRole);

            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Due to restrictive book policy, expects one book result:
            // id: 9 title: 'Policy-Test-01'
            string dbQuery_restrictToOneResult = @"
                SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) AS data
                FROM (SELECT id, title FROM books
                      WHERE (title = 'Policy-Test-01')
                      ORDER BY id ASC
                      FETCH FIRST 100 ROWS ONLY)";

            clientRole = "policy_tester_01";
            await QueryMany_Policy(dbQuery_restrictToOneResult, clientRole);
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records
        /// on a table with a nullable field, but does not include any nested queries.
        /// When a policy is applied to such top-level query, results are restricted
        /// to the expected records.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_Policy_Nullable()
        {
            // Tests Fungi Read Policy: @item.region ne 'northeast'
            // Due to restrictive book policy, expects all fungi records except
            // id: 1 region: 'northeast'
            string dbQuery = @"
                SELECT JSON_ARRAYAGG(JSON_OBJECT('speciesid' VALUE speciesid, 'region' VALUE region)) AS data
                FROM (SELECT speciesid, region FROM fungi
                      WHERE (region != 'northeast')
                      ORDER BY speciesid ASC
                      FETCH FIRST 100 ROWS ONLY)";

            await QueryMany_Policy_Nullable(dbQuery);
        }

        [TestMethod]
        public async Task QueryMany_NestedRequest_Policy()
        {
            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Publisher Read Policy: @item.id ne 1940
            // Expects HotChocolate error since nested query fails to resolve
            // at least one publisher record due to restrictive policy.
            // Due to expecting error, the dbQuery is not run for result validation.
            string dbQuery = @"
                SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publishers' VALUE (
                    SELECT JSON_OBJECT('id' VALUE id, 'name' VALUE name)
                    FROM publishers
                    WHERE books.publisher_id = publishers.id AND id = 1940
                    FETCH FIRST 1 ROWS ONLY))) AS data
                FROM books
                WHERE (title = 'Policy-Test-01')
                ORDER BY id ASC";

            await QueryMany_NestedRequest_Policy(
                dbQuery,
                roleName: "policy_tester_03",
                expectError: true);

            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Publisher Read Policy: @item.id eq 1940
            // Target Record: id: 9, title: 'Policy-Test-01' publisher_id: 1940
            // The top-level book policy restricts this result to one record while
            // the nested query policy resolves at least one result, avoiding
            // resolving null for a non-nullable field.
            // DB Query is used for result validation.
            await QueryMany_NestedRequest_Policy(
                dbQuery,
                roleName: "policy_tester_01",
                expectError: false);
        }
    }
}