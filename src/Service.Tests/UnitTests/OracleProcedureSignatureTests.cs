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
