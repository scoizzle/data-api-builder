// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// How a mutation shares its connection with a follow-up read.
    /// The shared engine names the scenario. The database executor decides whether that
    /// requires one local transaction.
    /// </summary>
    public enum MutationConnectionSharing
    {
        /// <summary>
        /// REST write that does not re-read on the same connection.
        /// </summary>
        None,

        /// <summary>
        /// GraphQL mutation. The write and the selection-set read must see one transaction
        /// when the database cannot enlist a second connection.
        /// </summary>
        GraphQL,

        /// <summary>
        /// REST write whose read policy must observe the uncommitted write.
        /// </summary>
        RestFollowUpRead
    }

    /// <summary>
    /// Runs a follow-up statement on the mutation's connection and transaction
    /// without naming a provider type.
    /// </summary>
    public interface IMutationWorkSession
    {
        /// <summary>
        /// True when this session holds an uncommitted local transaction.
        /// A TransactionScope session returns false so cache behavior is unchanged.
        /// </summary>
        bool IsInLocalTransaction { get; }

        Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
            string dataSourceName,
            HttpContext? httpContext,
            List<string>? args);

        TResult? ExecuteQuery<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, TResult>? dataReaderHandler,
            HttpContext? httpContext,
            List<string>? args,
            string dataSourceName);
    }

    /// <summary>
    /// Opens the transaction a mutation needs and runs the write plus any follow-up read inside it.
    /// </summary>
    public interface IMutationTransactionExecutor
    {
        Task ExecuteAsync(
            string dataSourceName,
            MutationConnectionSharing sharing,
            Func<IMutationWorkSession, Task> work,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Ambient mutation session. Follow-up reads consult this instead of threading a connection
    /// through the query engine.
    /// </summary>
    internal static class MutationWork
    {
        private static readonly AsyncLocal<IMutationWorkSession?> CurrentSession = new();

        public static IMutationWorkSession? Current => CurrentSession.Value;

        public static IDisposable Push(IMutationWorkSession session)
        {
            IMutationWorkSession? previous = CurrentSession.Value;
            CurrentSession.Value = session;
            return new Pop(previous);
        }

        private sealed class Pop : IDisposable
        {
            private readonly IMutationWorkSession? _previous;
            private bool _disposed;

            public Pop(IMutationWorkSession? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                CurrentSession.Value = _previous;
                _disposed = true;
            }
        }
    }
}
