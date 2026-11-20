// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Stored procedures that need a scalar OUT or IN OUT bind fail at startup. A single
    /// REF CURSOR OUT, IN parameters, and a function RETURN stay valid.
    /// </summary>
    [TestClass]
    public class OracleProcedureSignatureTests
    {
        [TestMethod]
        public void RefCursorOutAndInParameters_AreSupported()
        {
            OracleMetadataProvider.EnsureSupportedOracleSignature(
                new[]
                {
                    Row("p_id", "NUMBER", "IN", 1),
                    Row("p_cur", "REF CURSOR", "OUT", 2),
                },
                "SYSTEM.GET_BOOK");
        }

        [TestMethod]
        public void ScalarFunctionReturn_IsSupported()
        {
            OracleMetadataProvider.EnsureSupportedOracleSignature(
                new[]
                {
                    Row(null, "NUMBER", "OUT", 0),
                    Row("p_id", "NUMBER", "IN", 1),
                },
                "SYSTEM.BOOK_COUNT");
        }

        [TestMethod]
        public void ScalarOut_IsRejectedAtStartup()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.EnsureSupportedOracleSignature(
                    new[]
                    {
                        Row("p_id", "NUMBER", "IN", 1),
                        Row("p_count", "NUMBER", "OUT", 2),
                    },
                    "SYSTEM.COUNT_BOOKS"));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "SYSTEM.COUNT_BOOKS");
            StringAssert.Contains(ex.Message, "p_count OUT NUMBER");
            StringAssert.Contains(ex.Message, "rejected at startup");
        }

        [TestMethod]
        public void InOut_IsRejectedEvenWhenARefCursorIsPresent()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.EnsureSupportedOracleSignature(
                    new[]
                    {
                        Row("p_title", "VARCHAR2", "IN/OUT", 1),
                        Row("p_cur", "REF CURSOR", "OUT", 2),
                    },
                    "SYSTEM.TOUCH_BOOK"));

            StringAssert.Contains(ex.Message, "p_title IN OUT VARCHAR2");
            StringAssert.Contains(ex.Message, "REF CURSOR OUT parameter");
        }

        /// <summary>
        /// INTERVAL YEAR TO MONTH has no CLR equivalent (it is not a TimeSpan), and PL/SQL-only
        /// types cannot be bound from SQL. Both fail at startup, for arguments and RETURN values (#60).
        /// </summary>
        [DataTestMethod]
        [DataRow("INTERVAL YEAR(2) TO MONTH", "IN", 1, "p_age IN INTERVAL YEAR(2) TO MONTH")]
        [DataRow("PL/SQL BOOLEAN", "IN", 1, "p_age IN PL/SQL BOOLEAN")]
        [DataRow("PL/SQL PLS INTEGER", "IN", 1, "p_age IN PL/SQL PLS INTEGER")]
        [DataRow("PL/SQL BINARY INTEGER", "IN", 1, "p_age IN PL/SQL BINARY INTEGER")]
        [DataRow("BINARY_INTEGER", "IN", 1, "p_age IN BINARY_INTEGER")]
        [DataRow("PLS_INTEGER", "IN", 1, "p_age IN PLS_INTEGER")]
        [DataRow("PL/SQL RECORD", "IN", 1, "p_age IN PL/SQL RECORD")]
        [DataRow("INTERVAL YEAR(2) TO MONTH", "OUT", 0, "RETURN INTERVAL YEAR(2) TO MONTH")]
        public void UnsupportedArgumentTypes_AreRejectedAtStartup(string dataType, string direction, int position, string expectedItem)
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.EnsureSupportedOracleSignature(
                    new[]
                    {
                        Row(position == 0 ? null : "p_age", dataType, direction, position),
                        Row("p_cur", "REF CURSOR", "OUT", 2),
                    },
                    "SYSTEM.GET_AGE"));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "SYSTEM.GET_AGE");
            StringAssert.Contains(ex.Message, expectedItem);
            StringAssert.Contains(ex.Message, "rejected at startup");
        }

        [DataTestMethod]
        [DataRow("INTERVAL DAY(2) TO SECOND(6)")]
        [DataRow("BOOLEAN")]
        [DataRow("TIMESTAMP(6) WITH TIME ZONE")]
        public void SupportedSqlTypes_AreNotRejected(string dataType)
        {
            OracleMetadataProvider.EnsureSupportedOracleSignature(
                new[]
                {
                    Row("p_value", dataType, "IN", 1),
                    Row("p_cur", "REF CURSOR", "OUT", 2),
                },
                "SYSTEM.GET_VALUE");
        }

        [TestMethod]
        public void MultipleRefCursors_AreRejected()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.EnsureSupportedOracleSignature(
                    new[]
                    {
                        Row("p_one", "REF CURSOR", "OUT", 1),
                        Row("p_two", "SYS_REFCURSOR", "OUT", 2),
                    },
                    "SYSTEM.TWO_CURSORS"));

            StringAssert.Contains(ex.Message, "2 REF CURSOR OUT parameters");
        }

        [TestMethod]
        public void InParametersOnly_AreSupported()
        {
            OracleMetadataProvider.EnsureSupportedOracleSignature(
                new[] { Row("p_id", "NUMBER", "IN", 1) },
                "SYSTEM.TOUCH_BOOK");
        }

        private static OracleMetadataProvider.OracleArgumentRow Row(
            string? name,
            string dataType,
            string inOut,
            int position)
        {
            return new OracleMetadataProvider.OracleArgumentRow(name, dataType, inOut, position, Overload: null);
        }
    }
}
