// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{

    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLCollationTests : GraphQLCollationTestBase
    {
        //Collation setting for database
        private const string DEFAULT_COLLATION = "USING_NLS_COMP";
        private const string CASE_INSENSITIVE_COLLATION = "BINARY_CI";

        /// <summary>
        /// Set the database engine for tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        #region Tests
        /// <summary>
        /// Oracle Collation Tests to ensure that GraphQL is working properly when there is a change in case sensitivity on the database
        /// </summary>
        [DataTestMethod]
        [DataRow("comics", "title", @"SELECT JSON_ARRAYAGG(JSON_OBJECT(*)) FROM (SELECT title FROM comics ORDER BY title asc)")]
        [DataRow("authors", "name", @"SELECT JSON_ARRAYAGG(JSON_OBJECT(*)) FROM (SELECT name FROM authors ORDER BY name asc)")]
        [DataRow("fungi", "habitat", @"SELECT JSON_ARRAYAGG(JSON_OBJECT(*)) FROM (SELECT habitat FROM fungi ORDER BY habitat asc)")]
        public async Task OracleCaseSensitiveResultQuery(string objectType, string fieldName, string dbQuery)
        {
            string defaultCollationQuery = OracleCollationQuery(objectType, fieldName, DEFAULT_COLLATION);
            string newCollationQuery = OracleCollationQuery(objectType, fieldName, CASE_INSENSITIVE_COLLATION);
            await TestQueryingWithCaseSensitiveCollation(objectType, fieldName, dbQuery, defaultCollationQuery, newCollationQuery);
        }

        /// <summary>
        /// Creates collation query for a specific column on a table in the database for Oracle
        /// </summary>
        private static string OracleCollationQuery(string table, string column, string newCollation)
        {
            string dbQuery = @"
                ALTER TABLE " + table + @"
                MODIFY " + column + @" VARCHAR2(4000)
                COLLATE " + newCollation;

            return dbQuery;
        }
        #endregion
    }
}
