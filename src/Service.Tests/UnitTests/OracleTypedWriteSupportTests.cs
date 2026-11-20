// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Tests.OracleTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedDateTimeTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Offline expectations for Oracle typed-write rows. DATE, DATETIME, DATETIME2, and
    /// SMALLDATETIME are DATE or TIMESTAMP columns, so the live matrix runs them.
    /// </summary>
    [TestClass]
    public class OracleTypedWriteSupportTests
    {
        [DataTestMethod]
        [DataRow(DATE_TYPE, "\"0001-01-01\"")]
        [DataRow(DATE_TYPE, "\"9999-12-31\"")]
        [DataRow(DATETIME_TYPE, "\"1753-01-01 00:00:00.000\"")]
        [DataRow(DATETIME_TYPE, "\"9999-12-31 23:59:59.499\"")]
        [DataRow(DATETIME_TYPE, "\"9999-12-31T23:59:59.499\"")]
        [DataRow(DATETIME_TYPE, "\"1999-01-08 10:23:54\"")]
        [DataRow(DATETIME_TYPE, "\"1999-01-08\"")]
        [DataRow(DATETIME_TYPE, "null")]
        [DataRow(SMALLDATETIME_TYPE, "\"1900-01-01\"")]
        [DataRow(SMALLDATETIME_TYPE, "\"2079-06-06\"")]
        [DataRow(DATETIME2_TYPE, "\"0001-01-01 00:00:00.0000000\"")]
        [DataRow(DATETIME2_TYPE, "\"9999-12-31 23:59:59.9999999\"")]
        [DataRow(DATETIME2_TYPE, "\"9999-12-31T23:59:59.9999999\"")]
        public void DateAndTimestampWrites_AreSupported(string type, string value)
        {
            Assert.IsTrue(OracleGQLSupportedTypesTests.IsTypedWriteSupported(type, value));
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, "255")]
        [DataRow(BOOLEAN_TYPE, "true")]
        [DataRow(SINGLE_TYPE, "2E35")]
        [DataRow(FLOAT_TYPE, "2E150")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"0001-01-01 00:00:00.0000000\"")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"9999-12-31 23:59:59.9999999\"")]
        public void UnsupportedTypedWrites_StayOutOfTheMatrix(string type, string value)
        {
            Assert.IsFalse(OracleGQLSupportedTypesTests.IsTypedWriteSupported(type, value));
        }

        [TestMethod]
        public void TimestampWithTimeZone_SeededOffsetAndNull_AreSupported()
        {
            Assert.IsTrue(OracleGQLSupportedTypesTests.IsTypedWriteSupported(DATETIMEOFFSET_TYPE, "\"1999-01-08 10:23:54+8:00\""));
            Assert.IsTrue(OracleGQLSupportedTypesTests.IsTypedWriteSupported(DATETIMEOFFSET_TYPE, "null"));
        }
    }
}
