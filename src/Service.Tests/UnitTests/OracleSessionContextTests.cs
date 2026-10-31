// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for Oracle session-context (claims forwarding) command generation and gating.
    /// </summary>
    [TestClass]
    public class OracleSessionContextTests
    {
        [TestMethod]
        public void BuildSessionContextBlock_NoClaims_ClearsOnly()
        {
            (string commandText, List<(string Name, string Value)> parameters) =
                OracleQueryExecutor.BuildSessionContextBlock(new Dictionary<string, string>());

            Assert.AreEqual("BEGIN DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS; END;", commandText);
            Assert.AreEqual(0, parameters.Count);
        }

        [TestMethod]
        public void BuildSessionContextBlock_BindsEveryClaimByNameAndValue()
        {
            KeyValuePair<string, string>[] claims =
            [
                new("roles", "authenticated"),
                new("groups", "[\"group1\",\"group2\"]"),
            ];

            (string commandText, List<(string Name, string Value)> parameters) =
                OracleQueryExecutor.BuildSessionContextBlock(claims);

            StringAssert.Contains(commandText, "DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS;");
            StringAssert.Contains(commandText, "DAB_SESSION_CONTEXT_PKG.SET_CLAIM(:dab_claim_key0, :dab_claim_value0);");
            StringAssert.Contains(commandText, "DAB_SESSION_CONTEXT_PKG.SET_CLAIM(:dab_claim_key1, :dab_claim_value1);");
            Assert.AreEqual(4, parameters.Count);
            Assert.AreEqual("dab_claim_key0", parameters[0].Name);
            Assert.AreEqual("roles", parameters[0].Value);
            Assert.AreEqual("dab_claim_value0", parameters[1].Name);
            Assert.AreEqual("authenticated", parameters[1].Value);
            Assert.AreEqual("dab_claim_key1", parameters[2].Name);
            Assert.AreEqual("groups", parameters[2].Value);
            Assert.AreEqual("dab_claim_value1", parameters[3].Name);
            Assert.AreEqual("[\"group1\",\"group2\"]", parameters[3].Value);
        }

        [TestMethod]
        public void BuildSessionContextBlock_InjectionLookingValues_StayOutOfCommandText()
        {
            const string maliciousValue = "x'; DELETE FROM BOOKS;--";

            KeyValuePair<string, string>[] claims = [new("groups", maliciousValue)];

            (string commandText, List<(string Name, string Value)> parameters) =
                OracleQueryExecutor.BuildSessionContextBlock(claims);

            Assert.IsFalse(
                commandText.Contains(maliciousValue),
                "Claim values must travel only as bind parameters, never in the command text.");
            Assert.AreEqual(maliciousValue, parameters[1].Value);
        }

        [TestMethod]
        public void PrepareDbCommand_SessionContextDisabled_DoesNotConnectOrExecute()
        {
            RuntimeConfigProvider provider = CreateOracleProvider(new Dictionary<string, object?>());
            OracleQueryExecutor executor = new(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);

            using OracleConnection conn = new("User Id=x;Password=y;Data Source=localhost:1521/x");
            DefaultHttpContext httpContext = new()
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("groups", "group1")], "TestAuth"))
            };

            using DbCommand cmd = executor.PrepareDbCommand(
                conn,
                "SELECT 1 FROM DUAL",
                new Dictionary<string, DbConnectionParam>(),
                httpContext,
                provider.GetConfig().DefaultDataSourceName);

            Assert.IsNotNull(cmd);
            Assert.AreEqual(ConnectionState.Closed, conn.State);
        }

        [TestMethod]
        public void OracleOptions_SetSessionContext_DefaultsToDisabled()
        {
            OracleOptions defaults = new();
            Assert.IsFalse(defaults.SetSessionContext);

            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", new() { { "set-session-context", true } }),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>()));

            Assert.IsTrue(config.DataSource!.GetTypedOptions<OracleOptions>()!.SetSessionContext);
        }

        [TestMethod]
        public void ApplySessionContext_NullHttpContext_ClearsOnly()
        {
            RecordingOracleQueryExecutor executor = CreateRecordingExecutor(sessionContext: true);
            using OracleConnection connection = ClosedConnection();

            executor.Apply(connection, httpContext: null, dataSourceName: string.Empty, transaction: null);

            Assert.AreEqual(1, executor.CommandTexts.Count);
            Assert.AreEqual("BEGIN DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS; END;", executor.CommandTexts[0]);
            Assert.IsFalse(executor.CommandTexts[0].Contains("SET_CLAIM", StringComparison.Ordinal));
            Assert.AreEqual(0, executor.ParameterCount);
            Assert.AreEqual(ConnectionState.Closed, connection.State);
            Assert.IsFalse(executor.Enlisted[0]);
        }

        [TestMethod]
        public void ApplySessionContext_OptionOff_DoesNotExecute()
        {
            RecordingOracleQueryExecutor executor = CreateRecordingExecutor(sessionContext: false);
            using OracleConnection connection = ClosedConnection();
            DefaultHttpContext httpContext = ClaimsContext("sub", "user-1");

            executor.Apply(connection, httpContext, dataSourceName: string.Empty, transaction: null);

            Assert.AreEqual(0, executor.CommandTexts.Count);
            Assert.AreEqual(ConnectionState.Closed, connection.State);
        }

        [TestMethod]
        public async Task ApplySessionContextAsync_ForwardsAbortToken()
        {
            RecordingOracleQueryExecutor executor = CreateRecordingExecutor(sessionContext: true);
            using OracleConnection connection = ClosedConnection();
            using CancellationTokenSource source = new();

            await executor.ApplyAsync(connection, httpContext: null, dataSourceName: string.Empty, transaction: null, source.Token);

            Assert.AreEqual(1, executor.AsyncTokens.Count);
            Assert.AreEqual(source.Token, executor.AsyncTokens[0]);
            Assert.AreEqual("BEGIN DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS; END;", executor.CommandTexts[0]);
        }

        [TestMethod]
        public async Task ApplySessionContextAsync_CancelledToken_ThrowsBeforeCommand()
        {
            RecordingOracleQueryExecutor executor = CreateRecordingExecutor(sessionContext: true);
            using OracleConnection connection = ClosedConnection();
            using CancellationTokenSource source = new();
            source.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => executor.ApplyAsync(connection, httpContext: null, dataSourceName: string.Empty, transaction: null, source.Token));

            Assert.AreEqual(0, executor.CommandTexts.Count);
            Assert.AreEqual(ConnectionState.Closed, connection.State);
        }

        [TestMethod]
        public void ApplySessionContext_WithClaims_BindsClaimAndClears()
        {
            RecordingOracleQueryExecutor executor = CreateRecordingExecutor(sessionContext: true);
            using OracleConnection connection = ClosedConnection();

            executor.Apply(connection, ClaimsContext("sub", "user-1"), dataSourceName: string.Empty, transaction: null);

            StringAssert.Contains(executor.CommandTexts[0], "DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS;");
            StringAssert.Contains(executor.CommandTexts[0], "DAB_SESSION_CONTEXT_PKG.SET_CLAIM(");
            Assert.IsTrue(executor.ParameterValues.Contains("sub"));
            Assert.IsTrue(executor.ParameterValues.Contains("user-1"));
            Assert.AreEqual(ConnectionState.Closed, connection.State);
        }

        [TestMethod]
        public void ApplySessionContext_NonOracleTransaction_LeavesCommandUnenlisted()
        {
            RecordingOracleQueryExecutor executor = CreateRecordingExecutor(sessionContext: true);
            using OracleConnection connection = ClosedConnection();

            executor.Apply(connection, httpContext: null, dataSourceName: string.Empty, transaction: new NonOracleTransaction());

            Assert.AreEqual(1, executor.CommandTexts.Count);
            Assert.IsFalse(executor.Enlisted[0]);
        }

        [TestMethod]
        public void PrepareDbCommand_SessionContextEnabled_DoesNotConnectOrExecute()
        {
            OracleQueryExecutor executor = CreateDirectExecutor(sessionContext: true);
            Assert.IsTrue(executor.IsSessionContextEnabled(string.Empty));
            using OracleConnection connection = ClosedConnection();
            DefaultHttpContext httpContext = ClaimsContext("groups", "group1");

            using DbCommand command = executor.PrepareDbCommand(
                connection,
                "SELECT 1 FROM DUAL",
                new Dictionary<string, DbConnectionParam>(),
                httpContext,
                string.Empty);

            Assert.AreEqual("SELECT 1 FROM DUAL", command.CommandText);
            Assert.AreEqual(ConnectionState.Closed, connection.State);
        }

        [TestMethod]
        public void HotReload_ReplacesExecutor_WithUpdatedSessionContextFlag()
        {
            const string sourceName = "oracle";
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem())
            {
                RuntimeConfig = CreateNamedOracleConfig(sourceName, sessionContext: false)
            };
            RuntimeConfigProvider provider = new(loader);
            QueryManagerFactory factory = new(
                provider,
                Mock.Of<ILogger<IQueryExecutor>>(),
                new HttpContextAccessor(),
                handler: null);

            OracleQueryExecutor before = (OracleQueryExecutor)factory.GetQueryExecutor(DatabaseType.Oracle);
            Assert.IsFalse(before.IsSessionContextEnabled(sourceName));

            loader.RuntimeConfig = CreateNamedOracleConfig(sourceName, sessionContext: true);
            factory.OnConfigChanged(sender: null, new HotReloadEventArgs("test", "reload"));

            OracleQueryExecutor after = (OracleQueryExecutor)factory.GetQueryExecutor(DatabaseType.Oracle);
            Assert.AreNotSame(before, after);
            Assert.IsFalse(before.IsSessionContextEnabled(sourceName));
            Assert.IsTrue(after.IsSessionContextEnabled(sourceName));
        }

        private static RuntimeConfigProvider CreateOracleProvider(Dictionary<string, object?> options)
        {
            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", options),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>()));

            return TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        }

        private static RecordingOracleQueryExecutor CreateRecordingExecutor(bool sessionContext)
        {
            RuntimeConfigProvider provider = CreateDirectProvider(sessionContext);
            return new RecordingOracleQueryExecutor(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }

        private static OracleQueryExecutor CreateDirectExecutor(bool sessionContext)
        {
            RuntimeConfigProvider provider = CreateDirectProvider(sessionContext);
            return new OracleQueryExecutor(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }

        private static RuntimeConfigProvider CreateDirectProvider(bool sessionContext)
        {
            Dictionary<string, object?> options = sessionContext
                ? new() { ["set-session-context"] = true }
                : new();
            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", options),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>()));
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            return new RuntimeConfigProvider(loader);
        }

        private static RuntimeConfig CreateNamedOracleConfig(string sourceName, bool sessionContext)
        {
            Dictionary<string, object?> options = sessionContext
                ? new() { ["set-session-context"] = true }
                : new();
            DataSource source = new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", options);
            return new RuntimeConfig(
                Schema: string.Empty,
                DataSource: source,
                Runtime: new RuntimeOptions(null, null, null, null),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                DefaultDataSourceName: sourceName,
                DataSourceNameToDataSource: new Dictionary<string, DataSource> { [sourceName] = source },
                EntityNameToDataSourceName: new Dictionary<string, string>());
        }

        private static OracleConnection ClosedConnection()
        {
            return new OracleConnection("User Id=x;Password=y;Data Source=localhost:1521/x");
        }

        private static DefaultHttpContext ClaimsContext(string claimType, string claimValue)
        {
            return new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, claimValue)], "TestAuth"))
            };
        }

        /// <summary>
        /// Records the session-context command instead of opening a database connection.
        /// </summary>
        private sealed class RecordingOracleQueryExecutor : OracleQueryExecutor
        {
            public List<string> CommandTexts { get; } = new();

            public List<string> ParameterValues { get; } = new();

            public List<bool> Enlisted { get; } = new();

            public List<CancellationToken> AsyncTokens { get; } = new();

            public int ParameterCount => ParameterValues.Count;

            public RecordingOracleQueryExecutor(
                RuntimeConfigProvider runtimeConfigProvider,
                DbExceptionParser dbExceptionParser,
                ILogger<IQueryExecutor> logger,
                IHttpContextAccessor httpContextAccessor)
                : base(runtimeConfigProvider, dbExceptionParser, logger, httpContextAccessor)
            {
            }

            public void Apply(
                DbConnection connection,
                HttpContext? httpContext,
                string dataSourceName,
                DbTransaction? transaction)
            {
                ApplySessionContext(connection, httpContext, dataSourceName, transaction);
            }

            public Task ApplyAsync(
                DbConnection connection,
                HttpContext? httpContext,
                string dataSourceName,
                DbTransaction? transaction,
                CancellationToken cancellationToken)
            {
                return ApplySessionContextAsync(connection, httpContext, dataSourceName, transaction, cancellationToken);
            }

            protected override void ExecuteSessionContextCommand(OracleCommand command)
            {
                Record(command, cancellationToken: null);
            }

            protected override Task ExecuteSessionContextCommandAsync(OracleCommand command, CancellationToken cancellationToken)
            {
                Record(command, cancellationToken);
                return Task.CompletedTask;
            }

            private void Record(OracleCommand command, CancellationToken? cancellationToken)
            {
                CommandTexts.Add(command.CommandText);
                Enlisted.Add(command.Transaction is not null);
                foreach (OracleParameter parameter in command.Parameters)
                {
                    ParameterValues.Add(parameter.Value?.ToString() ?? string.Empty);
                }

                if (cancellationToken is not null)
                {
                    AsyncTokens.Add(cancellationToken.Value);
                }
            }
        }

        private sealed class NonOracleTransaction : DbTransaction
        {
            public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;

            protected override DbConnection? DbConnection => null;

            public override void Commit()
            {
            }

            public override void Rollback()
            {
            }
        }
    }
}
