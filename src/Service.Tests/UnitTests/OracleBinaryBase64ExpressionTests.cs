// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// BLOB and LONG RAW base64 must not go through one VARCHAR2 conversion.
    /// UTL_RAW.CAST_TO_VARCHAR2 stops at 4000 bytes, so a value of about 3000 raw bytes
    /// used to fail inside JSON_OBJECT and the page came back empty.
    /// </summary>
    [TestClass]
    public class OracleBinaryBase64ExpressionTests
    {
        [TestMethod]
        public void Raw_UsesSingleVarcharConversion()
        {
            string sql = OracleQueryBuilder.BuildBinaryColumnExpression("\"T\".\"RAWCOL\"", isBlob: false, physicalType: "RAW");

            StringAssert.Contains(sql, "UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE(\"T\".\"RAWCOL\"))");
            Assert.IsFalse(sql.Contains("GETCLOBVAL", StringComparison.Ordinal), sql);
            Assert.IsFalse(sql.Contains("DBMS_LOB", StringComparison.Ordinal), sql);
        }

        [TestMethod]
        public void Blob_ChunksIntoClobUnderTheVarcharCap()
        {
            string sql = OracleQueryBuilder.BuildBinaryColumnExpression("\"T\".\"BLOBCOL\"", isBlob: true, physicalType: "BLOB");

            StringAssert.Contains(sql, "DBMS_LOB.SUBSTR(\"T\".\"BLOBCOL\", 1800,");
            StringAssert.Contains(sql, "GETCLOBVAL()");
            StringAssert.Contains(sql, "CONNECT BY LEVEL <= CEIL(DBMS_LOB.GETLENGTH(\"T\".\"BLOBCOL\") / 1800)");
            Assert.AreEqual(0, OracleQueryBuilder.Base64ChunkBytes % 3);
            Assert.IsFalse(
                sql.Contains("UTL_ENCODE.BASE64_ENCODE(\"T\".\"BLOBCOL\")", StringComparison.Ordinal),
                sql);
        }

        [TestMethod]
        public void LongRaw_ChunksThroughUtlRawIntoClob()
        {
            string sql = OracleQueryBuilder.BuildBinaryColumnExpression("\"T\".\"LRCOL\"", isBlob: true, physicalType: "LONG RAW");

            StringAssert.Contains(sql, "UTL_RAW.SUBSTR(\"T\".\"LRCOL\",");
            StringAssert.Contains(sql, "GETCLOBVAL()");
            Assert.IsFalse(sql.Contains("DBMS_LOB", StringComparison.Ordinal), sql);
        }

        [TestMethod]
        public void BlobWithoutPhysicalType_StillUsesTheClobPath()
        {
            string sql = OracleQueryBuilder.BuildBinaryColumnExpression("\"BLOBCOL\"", isBlob: true, physicalType: null);

            StringAssert.Contains(sql, "DBMS_LOB.SUBSTR");
            StringAssert.Contains(sql, "CASE WHEN \"BLOBCOL\" IS NULL THEN NULL");
        }
    }
}
