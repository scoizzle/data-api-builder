// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the Oracle NUMBER precision/scale -> CLR type mapping.
    /// Oracle's unconstrained NUMBER reports no precision/scale from the catalog; a declared
    /// NUMBER(p, s) reports p and s. The provider narrows integral NUMBER(p, 0) columns to int or
    /// long where the values fit, and keeps everything else as decimal.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleNumberTypeMappingTests
    {
        [DataTestMethod]
        [DataRow(null, null, typeof(decimal), DisplayName = "Unconstrained NUMBER (null precision) -> decimal")]
        [DataRow(9, 0, typeof(int), DisplayName = "NUMBER(9,0) -> int")]
        [DataRow(10, 0, typeof(long), DisplayName = "NUMBER(10,0) -> long")]
        [DataRow(18, 0, typeof(long), DisplayName = "NUMBER(18,0) -> long")]
        [DataRow(19, 0, typeof(decimal), DisplayName = "NUMBER(19,0) -> decimal")]
        [DataRow(8, 2, typeof(decimal), DisplayName = "NUMBER(8,2) -> decimal")]
        [DataRow(18, 2, typeof(decimal), DisplayName = "NUMBER(18,2) -> decimal")]
        [DataRow(9, null, typeof(int), DisplayName = "NUMBER(9) without scale -> int")]
        [DataRow(10, null, typeof(long), DisplayName = "NUMBER(10) without scale -> long")]
        public void MapNumberColumnType_MapsPrecisionAndScaleToExpectedType(object precision, object scale, Type expected)
        {
            // Act
            Type actual = OracleMetadataProvider.MapNumberColumnType(precision, scale);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void MapNumberColumnType_DBNullPrecision_MapsToDecimal()
        {
            // ODP.NET reports a NULL DATA_PRECISION for unconstrained NUMBER as DBNull in the
            // Columns schema table.
            Assert.AreEqual(typeof(decimal), OracleMetadataProvider.MapNumberColumnType(DBNull.Value, DBNull.Value));
            Assert.AreEqual(typeof(decimal), OracleMetadataProvider.MapNumberColumnType(DBNull.Value, 0));
        }
    }
}
