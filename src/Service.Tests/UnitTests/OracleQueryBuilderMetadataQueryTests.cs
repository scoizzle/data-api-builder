// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for Oracle metadata-discovery query generation.
    /// </summary>
    [TestClass]
    public class OracleQueryBuilderMetadataQueryTests
    {
        [TestMethod]
        public void ForeignKeyInfoQuery_ComparesUppercasedBindsDirectly()
        {
            string query = new OracleQueryBuilder().BuildForeignKeyInfoQuery(
                numberOfSchemaParameters: 1,
                numberOfTableParameters: 3);

            // OracleMetadataProvider.GetForeignKeyQueryParams uppercases the bind values; wrapping
            // the data dictionary columns in UPPER() would make the predicates non-sargable.
            Assert.IsFalse(query.Contains("UPPER("), query);
            StringAssert.Contains(query, "RefCons.OWNER IN (:schemaName0)");
            StringAssert.Contains(query, "RefCons.TABLE_NAME IN (:tableName0, :tableName1, :tableName2)");
        }

        [TestMethod]
        public void QuoteRelationEmitsStoredSpelling()
        {
            OracleQueryBuilder builder = new();

            Assert.AreEqual("\"Hr\".\"Customers\"", builder.QuoteRelation("Hr", "Customers"));
            Assert.AreEqual("\"TABLE0\"", builder.QuoteTableAlias("table0"));
        }

        [TestMethod]
        public void UnquotedUppercaseRelationUnchanged()
        {
            Assert.AreEqual("\"SYSTEM\".\"BOOKS\"", new OracleQueryBuilder().QuoteRelation("SYSTEM", "BOOKS"));
        }

        [TestMethod]
        public void CatalogBindKeepsStoredSpelling()
        {
            OracleQueryBuilder builder = new();

            Assert.AreEqual("Customers", builder.QuoteTableNameAsDBConnectionParam("Customers"));
            Assert.AreEqual("BOOKS", builder.QuoteTableNameAsDBConnectionParam("BOOKS"));
        }
    }
}
