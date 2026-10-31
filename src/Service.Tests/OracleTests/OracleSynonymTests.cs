// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleSynonymTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestMethod]
        public void PrivateSynonym_MetadataUsesBaseTable()
        {
            Assert.AreEqual(
                _sqlMetadataProvider.GetSchemaName("Book"),
                _sqlMetadataProvider.GetSchemaName("BookViaPrivateSynonym"),
                ignoreCase: true);
            Assert.AreEqual(
                "BOOKS",
                _sqlMetadataProvider.GetDatabaseObjectName("BookViaPrivateSynonym"),
                ignoreCase: true);
        }

        [TestMethod]
        public void NestedSynonym_MetadataUsesBaseTable()
        {
            Assert.AreEqual(
                "BOOKS",
                _sqlMetadataProvider.GetDatabaseObjectName("BookViaNestedSynonym"),
                ignoreCase: true);
        }

        [TestMethod]
        public void PublicSynonym_MetadataUsesBaseTable()
        {
            Assert.AreEqual(
                _sqlMetadataProvider.GetSchemaName("Publisher"),
                _sqlMetadataProvider.GetSchemaName("PublisherViaPublicSynonym"),
                ignoreCase: true);
            Assert.AreEqual(
                "PUBLISHERS",
                _sqlMetadataProvider.GetDatabaseObjectName("PublisherViaPublicSynonym"),
                ignoreCase: true);
        }

        [TestMethod]
        public async Task PrivateSynonym_RestReadMatchesBooks()
        {
            string oracleQuery =
                "SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data " +
                "FROM (SELECT * FROM books WHERE id = 1 FETCH FIRST 1 ROWS ONLY)";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: "BookViaPrivateSynonym",
                sqlQuery: oracleQuery,
                operationType: EntityActionOperation.Read);
        }

        [TestMethod]
        public async Task PublicSynonym_RestReadMatchesPublishers()
        {
            string oracleQuery =
                "SELECT JSON_OBJECT('id' VALUE id, 'name' VALUE name) AS data " +
                "FROM (SELECT * FROM publishers WHERE id = 1234 FETCH FIRST 1 ROWS ONLY)";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1234",
                queryString: string.Empty,
                entityNameOrPath: "PublisherViaPublicSynonym",
                sqlQuery: oracleQuery,
                operationType: EntityActionOperation.Read);
        }
    }
}
