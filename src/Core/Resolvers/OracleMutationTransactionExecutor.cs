// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Oracle mutations that write and then read use one connection and a local ReadCommitted
    /// transaction. ODP.NET promotes a second Open inside TransactionScope to XA, which .NET
    /// does not support. Writes that do not re-read stay on TransactionScope.
    /// </summary>
    internal sealed class OracleMutationTransactionExecutor : IMutationTransactionExecutor
    {
        private readonly OracleQueryExecutor _queryExecutor;
        private readonly DbExceptionParser _exceptionParser;
        private readonly TransactionScopeMutationExecutor _transactionScopeExecutor;

        public OracleMutationTransactionExecutor(OracleQueryExecutor queryExecutor, DbExceptionParser exceptionParser)
        {
            _queryExecutor = queryExecutor;
            _exceptionParser = exceptionParser;
            _transactionScopeExecutor = new TransactionScopeMutationExecutor(DatabaseType.Oracle);
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(
            string dataSourceName,
            MutationConnectionSharing sharing,
            Func<IMutationWorkSession, Task> work,
            CancellationToken cancellationToken)
        {
            if (sharing is MutationConnectionSharing.None)
            {
                await _transactionScopeExecutor.ExecuteAsync(dataSourceName, sharing, work, cancellationToken);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using OracleConnection connection = _queryExecutor.CreateConnection(dataSourceName);
            await _queryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(connection, dataSourceName, cancellationToken);
            try
            {
                await connection.OpenAsync(cancellationToken);
                using OracleTransaction transaction = _queryExecutor.BeginLocalReadCommittedTransaction(connection, dataSourceName);
                OracleLocalMutationSession session = new(_queryExecutor, connection, transaction, dataSourceName);
                using (MutationWork.Push(session))
                {
                    try
                    {
                        await work(session);
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (DbException dbException)
            {
                throw _exceptionParser.Parse(dbException);
            }
        }

        internal sealed class OracleLocalMutationSession : IMutationWorkSession
        {
            private readonly OracleQueryExecutor _queryExecutor;
            private readonly OracleConnection _connection;
            private readonly OracleTransaction _transaction;
            private readonly string _dataSourceName;

            public OracleLocalMutationSession(
                OracleQueryExecutor queryExecutor,
                OracleConnection connection,
                OracleTransaction transaction,
                string dataSourceName)
            {
                _queryExecutor = queryExecutor;
                _connection = connection;
                _transaction = transaction;
                _dataSourceName = dataSourceName;
            }

            public bool IsInLocalTransaction => true;

            public Task<TResult?> ExecuteQueryAsync<TResult>(
                string sqltext,
                IDictionary<string, DbConnectionParam> parameters,
                Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
                string dataSourceName,
                HttpContext? httpContext,
                List<string>? args)
            {
                EnsureStatementDataSource(_dataSourceName, dataSourceName);
                return _queryExecutor.ExecuteQueryOnConnectionAsync(
                    _connection,
                    sqltext,
                    parameters,
                    dataReaderHandler,
                    dataSourceName,
                    _transaction,
                    httpContext,
                    args);
            }

            public TResult? ExecuteQuery<TResult>(
                string sqltext,
                IDictionary<string, DbConnectionParam> parameters,
                Func<DbDataReader, List<string>?, TResult>? dataReaderHandler,
                HttpContext? httpContext,
                List<string>? args,
                string dataSourceName)
            {
                EnsureStatementDataSource(_dataSourceName, dataSourceName);
                return _queryExecutor.ExecuteQueryOnConnection(
                    _connection,
                    sqltext,
                    parameters,
                    dataReaderHandler,
                    httpContext,
                    args,
                    dataSourceName,
                    _transaction);
            }
        }

        /// <summary>
        /// Rejects a follow-up statement whose data source is not the one this local transaction opened.
        /// An empty name is allowed: executors treat it as the session connection. Comparison is
        /// case-insensitive, matching data-source name lookup. A linking entity on another source
        /// is a mismatch and must not run on this connection.
        /// </summary>
        internal static void EnsureStatementDataSource(string sessionDataSourceName, string statementDataSourceName)
        {
            if (string.IsNullOrEmpty(statementDataSourceName))
            {
                return;
            }

            if (!string.Equals(sessionDataSourceName, statementDataSourceName, StringComparison.OrdinalIgnoreCase))
            {
                throw new DataApiBuilderException(
                    message: $"Mutation statement data source '{statementDataSourceName}' does not match the transaction data source '{sessionDataSourceName}'.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }
        }
    }
}
