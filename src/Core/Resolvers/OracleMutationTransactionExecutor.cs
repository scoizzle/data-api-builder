// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
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
            // The two-argument override is the one Oracle implements. The three-argument
            // member on the base executor does not set the access token.
            await _queryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(connection, dataSourceName);
            try
            {
                await connection.OpenAsync(cancellationToken);
                using OracleTransaction transaction = _queryExecutor.BeginLocalReadCommittedTransaction(connection, dataSourceName);
                OracleLocalMutationSession session = new(_queryExecutor, connection, transaction);
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

        private sealed class OracleLocalMutationSession : IMutationWorkSession
        {
            private readonly OracleQueryExecutor _queryExecutor;
            private readonly OracleConnection _connection;
            private readonly OracleTransaction _transaction;

            public OracleLocalMutationSession(
                OracleQueryExecutor queryExecutor,
                OracleConnection connection,
                OracleTransaction transaction)
            {
                _queryExecutor = queryExecutor;
                _connection = connection;
                _transaction = transaction;
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
    }
}
