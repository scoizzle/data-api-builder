// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Live checks for a quoted table (<c>"Customers"</c>) and a quoted synonym.
    /// Not run in the default job (class is TestCategory.Oracle; no Oracle instance here).
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleQuotedIdentifierTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestMethod]
        public void Metadata_KeepsQuotedTableAndColumnSpelling()
        {
            Assert.AreEqual("Customers", _sqlMetadataProvider.GetDatabaseObjectName("QuotedCustomer"));
            SourceDefinition source = _sqlMetadataProvider.GetSourceDefinition("QuotedCustomer");
            CollectionAssert.AreEquivalent(new[] { "Id", "Name" }, source.Columns.Keys.ToArray());
            Assert.IsTrue(source.Columns.ContainsKey("Id"));
            Assert.IsTrue(source.PrimaryKey.Contains("Id"));
        }

        [TestMethod]
        public void Synonym_ResolvesToQuotedBaseSpelling()
        {
            Assert.AreEqual(
                _sqlMetadataProvider.GetSchemaName("QuotedCustomer"),
                _sqlMetadataProvider.GetSchemaName("QuotedCustomerViaSynonym"));
            Assert.AreEqual("Customers", _sqlMetadataProvider.GetDatabaseObjectName("QuotedCustomerViaSynonym"));
        }

        [TestMethod]
        public async Task Read_ReturnsQuotedRow()
        {
            string oracleQuery =
                "SELECT JSON_OBJECT('id' VALUE \"Id\", 'name' VALUE \"Name\") AS data " +
                "FROM (SELECT * FROM \"Customers\" WHERE \"Id\" = 1 FETCH FIRST 1 ROWS ONLY)";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: "QuotedCustomer",
                sqlQuery: oracleQuery,
                operationType: EntityActionOperation.Read);
        }

        [TestMethod]
        public async Task GraphQL_ReturnsQuotedRow()
        {
            string graphQLQuery = @"
            {
                quotedCustomers(filter: { id: { eq: 1 } }) {
                    items {
                        id
                        name
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, "quotedCustomers", isAuthenticated: false);
            string id = actual.GetProperty("items")[0].GetProperty("id").ToString();
            string? name = actual.GetProperty("items")[0].GetProperty("name").GetString();
            Assert.AreEqual("1", id);
            Assert.AreEqual("Ada", name);
        }
    }
}
