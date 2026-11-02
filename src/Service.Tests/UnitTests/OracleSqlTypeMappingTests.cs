// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Catalog type names include precision. TIMESTAMP(6) is the Oracle default and must not
    /// fall through to object. INTERVAL maps to TimeSpan and XMLTYPE to string.
    /// </summary>
    [TestClass]
    public class OracleSqlTypeMappingTests
    {
        [DataTestMethod]
        [DataRow("TIMESTAMP(6)", typeof(DateTime))]
        [DataRow("timestamp(6)", typeof(DateTime))]
        [DataRow("TIMESTAMP", typeof(DateTime))]
        [DataRow("TIMESTAMP(6) WITH TIME ZONE", typeof(DateTimeOffset))]
        [DataRow("TIMESTAMP(9) WITH LOCAL TIME ZONE", typeof(DateTime))]
        [DataRow("INTERVAL DAY(2) TO SECOND(6)", typeof(TimeSpan))]
        [DataRow("INTERVAL YEAR(2) TO MONTH", typeof(TimeSpan))]
        [DataRow("XMLTYPE", typeof(string))]
        [DataRow("DATE", typeof(DateTime))]
        [DataRow("REF CURSOR", typeof(IDataReader))]
        [DataRow("NOT_A_TYPE", typeof(object))]
        public void SqlToCLRType_NormalizesPrecisionQualifiedNames(string sqlType, Type expected)
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)RuntimeHelpers.GetUninitializedObject(
                typeof(OracleMetadataProvider));

            Assert.AreEqual(expected, provider.SqlToCLRType(sqlType));
            Assert.AreEqual(expected, OracleSqlType.ToClrType(sqlType));
        }

        [TestMethod]
        public void Normalize_StripsQualifiersWithoutCollapsingWords()
        {
            Assert.AreEqual("TIMESTAMP WITH TIME ZONE", OracleSqlType.Normalize("TIMESTAMP(6) WITH TIME ZONE"));
            Assert.AreEqual("INTERVAL DAY TO SECOND", OracleSqlType.Normalize("  interval day(2) to second(6) "));
            Assert.AreEqual(string.Empty, OracleSqlType.Normalize(null));
        }
    }
}
