// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// A local Oracle mutation transaction may only run statements for the data source it opened.
    /// An empty statement name is the executor's "use this connection" signal and is allowed.
    /// </summary>
    [TestClass]
    public class OracleLocalMutationSessionTests
    {
        [TestMethod]
        public void EnsureStatementDataSource_EmptyName_IsAllowed()
        {
            OracleMutationTransactionExecutor.EnsureStatementDataSource("orders", string.Empty);
            OracleMutationTransactionExecutor.EnsureStatementDataSource("orders", null!);
        }

        [TestMethod]
        public void EnsureStatementDataSource_CaseDifference_IsAllowed()
        {
            OracleMutationTransactionExecutor.EnsureStatementDataSource("Orders", "orders");
        }

        [TestMethod]
        public void EnsureStatementDataSource_Mismatch_NamesBothSources()
        {
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => OracleMutationTransactionExecutor.EnsureStatementDataSource("orders", "linking"));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            StringAssert.Contains(exception.Message, "linking");
            StringAssert.Contains(exception.Message, "orders");
        }
    }
}
