// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// The local-transaction executor must hand the transaction's Rollback and Commit to
    /// <see cref="OracleMutationCompletion"/> in the right slots. Swapped slots would commit a
    /// failed mutation and roll back a successful one; <see cref="OracleMutationCompletionTests"/>
    /// covers the completion policy itself.
    /// </summary>
    [TestClass]
    public class OracleMutationTransactionExecutorTests
    {
        [TestMethod]
        public async Task CompleteInTransaction_WorkSucceeds_CommitsOnly()
        {
            RecordingTransaction transaction = new();

            await OracleMutationTransactionExecutor.CompleteInTransactionAsync(
                transaction,
                () => Task.CompletedTask,
                logRollbackFailure: null);

            CollectionAssert.AreEqual(new[] { "commit" }, transaction.Calls);
        }

        [TestMethod]
        public async Task CompleteInTransaction_WorkFails_RollsBackOnlyAndRethrows()
        {
            RecordingTransaction transaction = new();
            InvalidOperationException failure = new("write failed");

            InvalidOperationException thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => OracleMutationTransactionExecutor.CompleteInTransactionAsync(
                    transaction,
                    () => throw failure,
                    logRollbackFailure: null));

            Assert.AreSame(failure, thrown);
            CollectionAssert.AreEqual(new[] { "rollback" }, transaction.Calls);
        }

        private sealed class RecordingTransaction : DbTransaction
        {
            public List<string> Calls { get; } = new();

            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

            protected override DbConnection? DbConnection => null;

            public override void Commit()
            {
                Calls.Add("commit");
            }

            public override void Rollback()
            {
                Calls.Add("rollback");
            }
        }
    }
}
