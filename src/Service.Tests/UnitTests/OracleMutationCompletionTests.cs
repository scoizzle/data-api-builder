// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Rollback must not hide the mutation error, and a failed commit must not be replaced
    /// by a later rollback.
    /// </summary>
    [TestClass]
    public class OracleMutationCompletionTests
    {
        [TestMethod]
        public async Task WorkFailure_RollbackFailure_PreservesTheOriginalException()
        {
            InvalidOperationException original = new("mutation failed");
            int rollbacks = 0;
            int commits = 0;
            Exception? loggedRollback = null;
            Exception? loggedOriginal = null;

            InvalidOperationException thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                OracleMutationCompletion.CompleteAsync(
                    () => throw original,
                    () =>
                    {
                        rollbacks++;
                        throw new InvalidOperationException("rollback failed");
                    },
                    () => commits++,
                    (rollbackException, workException) =>
                    {
                        loggedRollback = rollbackException;
                        loggedOriginal = workException;
                    }));

            Assert.AreSame(original, thrown);
            Assert.AreEqual(1, rollbacks);
            Assert.AreEqual(0, commits);
            Assert.AreEqual("rollback failed", loggedRollback?.Message);
            Assert.AreSame(original, loggedOriginal);
        }

        [TestMethod]
        public async Task WorkFailure_SuccessfulRollback_StillThrowsTheOriginal()
        {
            InvalidOperationException original = new("mutation failed");
            int rollbacks = 0;

            InvalidOperationException thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                OracleMutationCompletion.CompleteAsync(
                    () => throw original,
                    () => rollbacks++,
                    () => throw new InvalidOperationException("commit should not run")));

            Assert.AreSame(original, thrown);
            Assert.AreEqual(1, rollbacks);
        }

        [TestMethod]
        public async Task CommitFailure_PropagatesAndDoesNotRollBack()
        {
            int rollbacks = 0;
            InvalidOperationException commitFailure = new("commit failed");

            InvalidOperationException thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                OracleMutationCompletion.CompleteAsync(
                    () => Task.CompletedTask,
                    () => rollbacks++,
                    () => throw commitFailure));

            Assert.AreSame(commitFailure, thrown);
            Assert.AreEqual(0, rollbacks);
        }

        [TestMethod]
        public async Task Success_CommitsOnce()
        {
            int commits = 0;
            int rollbacks = 0;

            await OracleMutationCompletion.CompleteAsync(
                () => Task.CompletedTask,
                () => rollbacks++,
                () => commits++);

            Assert.AreEqual(1, commits);
            Assert.AreEqual(0, rollbacks);
        }
    }
}
