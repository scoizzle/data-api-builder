// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the Oracle managed identity access token flow: the three-argument
    /// SetManagedIdentityAccessTokenIfAnyAsync overload (the one every execution path calls)
    /// must apply the token to <see cref="OracleConnection.AccessToken"/> without rewriting the
    /// connection string, and passwordless-looking wallet / external-auth descriptors must never
    /// be converted to token authentication.
    /// </summary>
    [TestClass]
    public class OracleQueryExecutorAuthTests
    {
        private const string PASSWORDLESS = "User Id=app;Pooling=false;Data Source=localhost:1521/svc";

        private const string WALLET_DESCRIPTOR =
            "User Id=app;Pooling=false;" +
            "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCPS)(HOST=db.example.com)(PORT=2484))" +
            "(CONNECT_DATA=(SERVICE_NAME=svc))(SECURITY=(MY_WALLET_DIRECTORY=/wallet)(SSL_SERVER_DN_MATCH=TRUE)))";

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ThreeArgOverload_SetsConnectionAccessToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS, configToken: "config-token");
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNotNull(GetAccessToken(conn), "The three-argument overload must apply the token.");
            Assert.IsFalse(conn.ConnectionString.Contains("config-token", StringComparison.Ordinal), "The token must not be written to the connection string.");
            OracleConnectionStringBuilder builder = new(conn.ConnectionString);
            Assert.IsTrue(string.IsNullOrEmpty(builder.Password));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_TwoArgOverload_ChainsToThreeArgAndSetsToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS, configToken: "config-token");
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName);

            Assert.IsNotNull(GetAccessToken(conn));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ConfigTokenWinsOverDefaultCredential()
        {
            Mock<DefaultAzureCredential> credential = new();
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS, configToken: "config-token");
            executor.AzureCredential = credential.Object;
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNotNull(GetAccessToken(conn));
            credential.Verify(
                c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_EmptyPasswordInference_UsesDefaultCredential()
        {
            Mock<DefaultAzureCredential> credential = new();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("default-token", DateTimeOffset.UtcNow.AddHours(1))));
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS);
            executor.AzureCredential = credential.Object;
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNotNull(GetAccessToken(conn));
            Assert.IsFalse(conn.ConnectionString.Contains("default-token", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ExplicitPassword_DoesNotSetToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(
                "User Id=app;Password=secret;Pooling=false;Data Source=localhost:1521/svc");
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNull(GetAccessToken(conn));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ManagedIdentityFalse_DoesNotSetToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(
                PASSWORDLESS,
                configToken: "config-token",
                options: new() { { "managed-identity", false } });
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNull(GetAccessToken(conn));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ManagedIdentityTrueWithPassword_SetsToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(
                "User Id=app;Password=secret;Pooling=false;Data Source=localhost:1521/svc",
                configToken: "config-token",
                options: new() { { "managed-identity", true } });
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNotNull(GetAccessToken(conn));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_WalletDescriptor_DoesNotSetToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(WALLET_DESCRIPTOR, configToken: "config-token");
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNull(GetAccessToken(conn));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_CancelledBeforeAcquisition_ThrowsAndKeepsTokenUseEnabled()
        {
            Mock<DefaultAzureCredential> credential = new();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns<TokenRequestContext, CancellationToken>((_, ct) =>
                    ValueTask.FromException<AccessToken>(new OperationCanceledException(ct)));
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS);
            executor.AzureCredential = credential.Object;
            using OracleConnection conn = executor.CreateConnection(dataSourceName);
            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, cts.Token));

            // A later, uncancelled request must still try to obtain a token.
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1))));
            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);
            Assert.IsNotNull(GetAccessToken(conn));
        }

        [DataTestMethod]
        [DataRow("User Id=app;Data Source=localhost:1521/svc", null, false, true, DisplayName = "empty password infers token")]
        [DataRow("User Id=app;Password=x;Data Source=localhost:1521/svc", null, false, false, DisplayName = "explicit password")]
        [DataRow("User Id=app;Password=x;Data Source=localhost:1521/svc", null, true, true, DisplayName = "controller token enables")]
        [DataRow("User Id=/;Data Source=localhost:1521/svc", null, true, false, DisplayName = "external auth wins over controller token")]
        [DataRow("User Id=/;Data Source=localhost:1521/svc", null, false, false, DisplayName = "external auth")]
        [DataRow("User Id=app;Data Source=(DESCRIPTION=(SECURITY=(MY_WALLET_DIRECTORY=/w)))", null, false, false, DisplayName = "wallet descriptor")]
        [DataRow("User Id=app;Data Source=localhost:1521/svc", false, true, false, DisplayName = "option false wins")]
        [DataRow("User Id=/;Data Source=localhost:1521/svc", true, false, true, DisplayName = "option true wins over external auth")]
        public void ShouldManagedIdentityAccessBeAttempted_Precedence(
            string connectionString,
            bool? option,
            bool controllerToken,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                OracleQueryExecutor.ShouldManagedIdentityAccessBeAttempted(
                    new OracleConnectionStringBuilder(connectionString),
                    option,
                    controllerToken));
        }

        [TestMethod]
        public void ThreeArgTokenApplication_LeavesTlsAndWalletConnectionStringUnchanged()
        {
            // A connection string already in token form (User Id=/) is not rewritten at all, so the
            // Data Source descriptor (PROTOCOL=TCPS, wallet directory, DN matching) is preserved verbatim.
            string connectionString = WALLET_DESCRIPTOR.Replace("User Id=app", "User Id=/", StringComparison.Ordinal);
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(
                connectionString,
                configToken: "config-token",
                options: new() { { "managed-identity", true } });
            using OracleConnection conn = executor.CreateConnection(dataSourceName);
            string before = conn.ConnectionString;

            executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsNotNull(GetAccessToken(conn));
            Assert.AreEqual(before, conn.ConnectionString);
        }

        [TestMethod]
        public void PrepareConnectionStringForTokenAuthentication_NormalizesCredentialsAndPreservesDescriptor()
        {
            string updated = OracleQueryExecutor.PrepareConnectionStringForTokenAuthentication(
                WALLET_DESCRIPTOR + ";Password=secret;Proxy User Id=px;Proxy Password=pxsecret");

            OracleConnectionStringBuilder builder = new(updated);
            Assert.AreEqual("/", builder.UserID);
            Assert.IsTrue(string.IsNullOrEmpty(builder.Password));
            Assert.IsTrue(string.IsNullOrEmpty(builder.ProxyUserId));
            Assert.IsTrue(string.IsNullOrEmpty(builder.ProxyPassword));
            Assert.IsFalse(builder.Pooling);
            StringAssert.Contains(builder.DataSource, "PROTOCOL=TCPS");
            StringAssert.Contains(builder.DataSource, "MY_WALLET_DIRECTORY");
            StringAssert.Contains(builder.DataSource, "SSL_SERVER_DN_MATCH=TRUE");
        }

        [DataTestMethod]
        [DataRow(true, true)]
        [DataRow(false, false)]
        [DataRow(null, null)]
        public void OracleOptions_ManagedIdentity_IsTriState(bool? configured, bool? expected)
        {
            Dictionary<string, object?> options = new();
            if (configured.HasValue)
            {
                options["managed-identity"] = configured.Value;
            }

            DataSource dataSource = new(DatabaseType.Oracle, PASSWORDLESS, options);

            Assert.AreEqual(expected, dataSource.GetTypedOptions<OracleOptions>()!.ManagedIdentity);
        }

        private static (OracleQueryExecutor Executor, string DataSourceName) CreateExecutor(
            string connectionString,
            string? configToken = null,
            Dictionary<string, object?>? options = null,
            TokenCredential? credential = null)
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new(DatabaseType.Oracle, connectionString, options),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>()));

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            string dataSourceName = provider.GetConfig().DefaultDataSourceName;
            if (configToken is not null)
            {
                provider.ManagedIdentityAccessToken[dataSourceName] = configToken;
            }

            OracleQueryExecutor executor = new(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);

            return (executor, dataSourceName);
        }

        private static object? GetAccessToken(OracleConnection conn) =>
            typeof(OracleConnection).GetProperty(nameof(OracleConnection.AccessToken))!.GetValue(conn);
    }
}
