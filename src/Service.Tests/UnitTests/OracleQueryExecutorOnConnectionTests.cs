// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for ExecuteQueryOnConnection: skip Open when already open,
    /// never CloseConnection, assign Transaction, skip Polly when a local tx is attached.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleQueryExecutorOnConnectionTests
    {
        [TestMethod]
        public void ExecuteQueryOnConnection_SkipsOpenWhenAlreadyOpen_DoesNotCloseConnection_AssignsTransaction()
        {
            (QueryExecutor<FakeDbConnection> executor, FakeDbConnection conn) = CreateExecutor();
            conn.SetState(ConnectionState.Open);
            FakeDbTransaction tx = new(conn);

            executor.ExecuteQueryOnConnection<object>(
                conn,
                "SELECT 1 FROM DUAL",
                new Dictionary<string, DbConnectionParam>(),
                dataReaderHandler: null,
                httpContext: null,
                args: null,
                dataSourceName: string.Empty,
                transaction: tx);

            Assert.AreEqual(0, conn.OpenCount, "Already-open connection must not be opened again.");
            Assert.AreEqual(CommandBehavior.Default, conn.LastCommand.LastCommandBehavior,
                "Reused connection must not use CommandBehavior.CloseConnection.");
            Assert.AreSame(tx, conn.LastCommand.Transaction);
        }

        [TestMethod]
        public void ExecuteQueryOnConnection_WithLocalTransaction_DoesNotRetryOnTransientFailure()
        {
            (QueryExecutor<FakeDbConnection> executor, FakeDbConnection conn) = CreateExecutor(transient: true);
            conn.SetState(ConnectionState.Open);
            conn.FailNextExecuteCount = 5;
            FakeDbTransaction tx = new(conn);

            try
            {
                executor.ExecuteQueryOnConnection<object>(
                    conn,
                    "INSERT INTO t VALUES (1)",
                    new Dictionary<string, DbConnectionParam>(),
                    dataReaderHandler: null,
                    httpContext: null,
                    args: null,
                    dataSourceName: string.Empty,
                    transaction: tx);
                Assert.Fail("Expected a database exception.");
            }
            catch (Exception)
            {
                Assert.AreEqual(1, conn.LastCommand.ExecuteReaderCount,
                    "Failed child statement on a local transaction must not Polly-retry.");
            }
        }

        [TestMethod]
        public async Task ExecuteQueryOnConnectionAsync_SkipsOpenWhenAlreadyOpen()
        {
            (QueryExecutor<FakeDbConnection> executor, FakeDbConnection conn) = CreateExecutor();
            conn.SetState(ConnectionState.Open);

            await executor.ExecuteQueryOnConnectionAsync<object>(
                conn,
                "SELECT 1 FROM DUAL",
                new Dictionary<string, DbConnectionParam>(),
                dataReaderHandler: null,
                dataSourceName: string.Empty,
                transaction: new FakeDbTransaction(conn),
                httpContext: null,
                args: null);

            Assert.AreEqual(0, conn.OpenCount);
            Assert.AreEqual(CommandBehavior.Default, conn.LastCommand.LastCommandBehavior);
        }

        private static (QueryExecutor<FakeDbConnection> Executor, FakeDbConnection Connection) CreateExecutor(bool transient = false)
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(mockConfig);
            Mock<DbExceptionParser> parser = new(provider);
            parser.Setup(p => p.IsTransientException(It.IsAny<DbException>())).Returns(transient);
            Mock<ILogger<IQueryExecutor>> logger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            QueryExecutor<FakeDbConnection> executor = new(
                parser.Object,
                logger.Object,
                provider,
                httpContextAccessor.Object,
                handler: null);

            return (executor, new FakeDbConnection());
        }

        private sealed class FakeDbException : DbException
        {
            public FakeDbException()
                : this("transient")
            {
            }

            public FakeDbException(string message)
                : base(message)
            {
            }

            public FakeDbException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        private sealed class FakeDbTransaction : DbTransaction
        {
            public FakeDbTransaction(FakeDbConnection connection)
            {
                DbConnection = connection;
            }

            protected override DbConnection DbConnection { get; }

            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

            public override void Commit()
            {
            }

            public override void Rollback()
            {
            }
        }

        private sealed class FakeDbConnection : DbConnection
        {
            private ConnectionState _state = ConnectionState.Closed;
            private string _connectionString = string.Empty;

            public int OpenCount { get; private set; }

            public FakeDbCommand LastCommand { get; private set; }

            public int FailNextExecuteCount { get; set; }

            public void SetState(ConnectionState state) => _state = state;

            public override string ConnectionString
            {
                get => _connectionString;
                set => _connectionString = value ?? string.Empty;
            }

            public override string Database => "db";

            public override string DataSource => "src";

            public override string ServerVersion => "1";

            public override ConnectionState State => _state;

            public override void ChangeDatabase(string databaseName)
            {
            }

            public override void Close() => _state = ConnectionState.Closed;

            public override void Open()
            {
                OpenCount++;
                _state = ConnectionState.Open;
            }

            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
                => new FakeDbTransaction(this);

            protected override DbCommand CreateDbCommand()
            {
                LastCommand = new FakeDbCommand(this) { FailCount = FailNextExecuteCount };
                return LastCommand;
            }
        }

        private sealed class FakeDbCommand : DbCommand
        {
            public FakeDbCommand(FakeDbConnection connection)
            {
                DbConnection = connection;
            }

            public CommandBehavior LastCommandBehavior { get; private set; }

            public int ExecuteReaderCount { get; private set; }

            public int FailCount { get; set; }

            public override string CommandText { get; set; } = string.Empty;

            public override int CommandTimeout { get; set; }

            public override CommandType CommandType { get; set; } = CommandType.Text;

            public override bool DesignTimeVisible { get; set; }

            public override UpdateRowSource UpdatedRowSource { get; set; }

            protected override DbConnection DbConnection { get; set; }

            protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();

            protected override DbTransaction DbTransaction { get; set; }

            public override void Cancel()
            {
            }

            public override int ExecuteNonQuery() => 0;

            public override object ExecuteScalar() => 1;

            public override void Prepare()
            {
            }

            protected override DbParameter CreateDbParameter() => new FakeDbParameter();

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                LastCommandBehavior = behavior;
                ExecuteReaderCount++;
                if (FailCount > 0)
                {
                    FailCount--;
                    throw new FakeDbException();
                }

                return new FakeDbDataReader();
            }
        }

        private sealed class FakeDbParameter : DbParameter
        {
            public override DbType DbType { get; set; }

            public override ParameterDirection Direction { get; set; }

            public override bool IsNullable { get; set; }

            public override string ParameterName { get; set; } = string.Empty;

            public override int Size { get; set; }

            public override string SourceColumn { get; set; } = string.Empty;

            public override bool SourceColumnNullMapping { get; set; }

            public override object Value { get; set; }

            public override void ResetDbType()
            {
            }
        }

        private sealed class FakeDbParameterCollection : DbParameterCollection
        {
            private readonly List<object> _items = new();

            public override int Count => _items.Count;

            public override object SyncRoot => this;

            public override int Add(object value)
            {
                _items.Add(value);
                return _items.Count - 1;
            }

            public override void AddRange(Array values)
            {
                foreach (object value in values)
                {
                    _items.Add(value);
                }
            }

            public override void Clear() => _items.Clear();

            public override bool Contains(object value) => _items.Contains(value);

            public override bool Contains(string value) => false;

            public override void CopyTo(Array array, int index) => _items.ToArray().CopyTo(array, index);

            public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

            public override int IndexOf(object value) => _items.IndexOf(value);

            public override int IndexOf(string parameterName) => -1;

            public override void Insert(int index, object value) => _items.Insert(index, value);

            public override void Remove(object value) => _items.Remove(value);

            public override void RemoveAt(int index) => _items.RemoveAt(index);

            public override void RemoveAt(string parameterName)
            {
            }

            protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];

            protected override DbParameter GetParameter(string parameterName) => throw new NotImplementedException();

            protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

            protected override void SetParameter(string parameterName, DbParameter value)
            {
            }
        }

        private sealed class FakeDbDataReader : DbDataReader
        {
            public override int FieldCount => 0;

            public override int Depth => 0;

            public override bool HasRows => false;

            public override bool IsClosed => false;

            public override int RecordsAffected => 0;

            public override object this[int ordinal] => throw new InvalidOperationException();

            public override object this[string name] => throw new InvalidOperationException();

            public override bool GetBoolean(int ordinal) => false;

            public override byte GetByte(int ordinal) => 0;

            public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => 0;

            public override char GetChar(int ordinal) => '\0';

            public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => 0;

            public override string GetDataTypeName(int ordinal) => string.Empty;

            public override DateTime GetDateTime(int ordinal) => default;

            public override decimal GetDecimal(int ordinal) => 0;

            public override double GetDouble(int ordinal) => 0;

            public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();

            public override Type GetFieldType(int ordinal) => typeof(object);

            public override float GetFloat(int ordinal) => 0;

            public override Guid GetGuid(int ordinal) => Guid.Empty;

            public override short GetInt16(int ordinal) => 0;

            public override int GetInt32(int ordinal) => 0;

            public override long GetInt64(int ordinal) => 0;

            public override string GetName(int ordinal) => string.Empty;

            public override int GetOrdinal(string name) => -1;

            public override string GetString(int ordinal) => string.Empty;

            public override object GetValue(int ordinal) => null;

            public override int GetValues(object[] values) => 0;

            public override bool IsDBNull(int ordinal) => true;

            public override bool NextResult() => false;

            public override bool Read() => false;

            public override void Close()
            {
            }
        }
    }
}
