// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Transactions;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Runs a mutation inside <see cref="TransactionScope"/>. Follow-up reads open their own
    /// enlisted connection. Used for MSSQL, PostgreSQL, MySQL, and DWSQL, and for Oracle
    /// writes that do not re-read on the same connection.
    /// </summary>
    internal sealed class TransactionScopeMutationExecutor : IMutationTransactionExecutor
    {
        private readonly DatabaseType _databaseType;

        public TransactionScopeMutationExecutor(DatabaseType databaseType)
        {
            _databaseType = databaseType;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(
            string dataSourceName,
            MutationConnectionSharing sharing,
            Func<IMutationWorkSession, Task> work,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using TransactionScope transactionScope = MutationTransactionScope.Create(_databaseType);
            await work(PassthroughMutationWorkSession.Instance);
            transactionScope.Complete();
        }
    }

    /// <summary>
    /// Isolation levels that match <see cref="SqlMutationEngine"/>'s existing TransactionScope helper.
    /// MySQL defaults to Repeatable Read. The other engines default to Read Committed.
    /// </summary>
    internal static class MutationTransactionScope
    {
        public static TransactionScope Create(DatabaseType databaseType)
        {
            System.Transactions.IsolationLevel isolationLevel = databaseType is DatabaseType.MySQL
                ? System.Transactions.IsolationLevel.RepeatableRead
                : System.Transactions.IsolationLevel.ReadCommitted;
            return new(
                TransactionScopeOption.Required,
                new TransactionOptions
                {
                    IsolationLevel = isolationLevel
                },
                TransactionScopeAsyncFlowOption.Enabled);
        }
    }

    /// <summary>
    /// Session for engines that do not hold a local transaction. Callers keep using the
    /// normal executor because <see cref="IsInLocalTransaction"/> is false.
    /// </summary>
    internal sealed class PassthroughMutationWorkSession : IMutationWorkSession
    {
        public static readonly PassthroughMutationWorkSession Instance = new();

        private PassthroughMutationWorkSession()
        {
        }

        /// <inheritdoc />
        public bool IsInLocalTransaction => false;

        /// <inheritdoc />
        public Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
            string dataSourceName,
            HttpContext? httpContext,
            List<string>? args)
        {
            throw new InvalidOperationException("A TransactionScope mutation does not execute follow-up reads on a shared connection.");
        }

        /// <inheritdoc />
        public TResult? ExecuteQuery<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, TResult>? dataReaderHandler,
            HttpContext? httpContext,
            List<string>? args,
            string dataSourceName)
        {
            throw new InvalidOperationException("A TransactionScope mutation does not execute follow-up reads on a shared connection.");
        }
    }
}
