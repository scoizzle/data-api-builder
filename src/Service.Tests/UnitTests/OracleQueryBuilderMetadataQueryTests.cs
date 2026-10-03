// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for Oracle metadata-discovery query generation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleQueryBuilderMetadataQueryTests
    {
        [TestMethod]
        public void ForeignKeyInfoQuery_ComparesUppercasedBindsDirectly()
        {
            string query = new OracleQueryBuilder().BuildForeignKeyInfoQuery(numberOfParameters: 2);

            // OracleMetadataProvider.GetForeignKeyQueryParams uppercases the bind values; wrapping
            // the data dictionary columns in UPPER() would make the predicates non-sargable.
            Assert.IsFalse(query.Contains("UPPER("), query);
            StringAssert.Contains(query, "RefCons.OWNER IN (:schemaName0, :schemaName1)");
            StringAssert.Contains(query, "RefCons.TABLE_NAME IN (:tableName0, :tableName1)");
        }
    }
}
