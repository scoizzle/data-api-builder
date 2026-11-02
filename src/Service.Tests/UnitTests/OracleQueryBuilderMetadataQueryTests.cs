// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

        /// <summary>
        /// Result-metadata predicates compare the stored spelling. A quoted "GetBook" bind
        /// must not be folded onto unquoted GETBOOK the way UPPER(OBJECT_NAME) = UPPER(:name) would.
        /// </summary>
        [TestMethod]
        public void ResultDetailsQuery_QuotedGetBook_DoesNotFoldToUppercase()
        {
            OracleQueryBuilder builder = new();
            string standalone = builder.BuildStoredProcedureResultDetailsQuery("\"GetBook\"");
            string packaged = builder.BuildStoredProcedureResultDetailsQuery(
                schemaName: "\"Hr\"",
                packageName: "\"BooksApi\"",
                subprogramName: "GetBook",
                isFunction: false);

            AssertExactIdentifierMatch(standalone);
            AssertExactIdentifierMatch(packaged);
            StringAssert.Contains(standalone, "WHERE OWNER = @param0 ");
            StringAssert.Contains(standalone, "AND OBJECT_NAME = @param1 ");
            StringAssert.Contains(packaged, "WHERE OWNER = @param0 ");
            StringAssert.Contains(packaged, "AND PACKAGE_NAME = @param1 ");
            StringAssert.Contains(packaged, "AND OBJECT_NAME = @param2 ");
            Assert.IsFalse(standalone.Contains("GETBOOK", StringComparison.Ordinal), standalone);
            Assert.IsFalse(packaged.Contains("GETBOOK", StringComparison.Ordinal), packaged);
        }

        private static void AssertExactIdentifierMatch(string query)
        {
            Assert.IsFalse(query.Contains("UPPER(OWNER)", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("UPPER(OBJECT_NAME)", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("UPPER(PACKAGE_NAME)", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("UPPER(", StringComparison.Ordinal), query);
        }
    }
}
