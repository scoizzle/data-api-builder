// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
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
        public async Task SetManagedIdentityAccessTokenAsync_CancelledDuringAcquisition_ThrowsAndKeepsTokenUseEnabled()
        {
            using CancellationTokenSource cts = new();
            Mock<DefaultAzureCredential> credential = new();
            // Cancel while the credential is acquiring the token so the catch in GetAccessTokenAsync runs.
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns<TokenRequestContext, CancellationToken>((_, ct) =>
                {
                    cts.Cancel();
                    return ValueTask.FromException<AccessToken>(new OperationCanceledException(ct));
                });
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS);
            executor.AzureCredential = credential.Object;
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, cts.Token));
            Assert.IsNull(GetAccessToken(conn));

            // A later, uncancelled request must still request a token: the cancellation must not
            // have cleared the per-data-source token flag.
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1))));
            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNotNull(GetAccessToken(conn));
            credential.Verify(
                c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ControllerTokenWithExplicitPassword_DoesNotSetToken()
        {
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(
                "User Id=app;Password=secret;Pooling=false;Data Source=localhost:1521/svc",
                configToken: "config-token");
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNull(GetAccessToken(conn));
            Assert.AreEqual("app", new OracleConnectionStringBuilder(conn.ConnectionString).UserID, "The configured credentials must not be rewritten.");
            Assert.AreEqual("secret", new OracleConnectionStringBuilder(conn.ConnectionString).Password);
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_ForcedAndCredentialUnavailable_Throws503AndKeepsTokenUseEnabled()
        {
            Mock<DefaultAzureCredential> credential = new();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromException<AccessToken>(new CredentialUnavailableException("no credential")));
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(
                "User Id=app;Password=secret;Pooling=false;Data Source=localhost:1521/svc",
                options: new() { { "managed-identity", true } });
            executor.AzureCredential = credential.Object;
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "managed-identity");
            Assert.IsNull(GetAccessToken(conn));

            // The flag is not cleared: the next request retries, and a second failure throws again
            // instead of silently connecting without a token.
            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None));

            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1))));
            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);
            Assert.IsNotNull(GetAccessToken(conn));
        }

        [TestMethod]
        public async Task SetManagedIdentityAccessTokenAsync_InferredAndCredentialUnavailable_DisablesTokenUseWithoutThrowing()
        {
            Mock<DefaultAzureCredential> credential = new();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromException<AccessToken>(new CredentialUnavailableException("no credential")));
            (OracleQueryExecutor executor, string dataSourceName) = CreateExecutor(PASSWORDLESS);
            executor.AzureCredential = credential.Object;
            using OracleConnection conn = executor.CreateConnection(dataSourceName);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);
            await executor.SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName, CancellationToken.None);

            Assert.IsNull(GetAccessToken(conn));
            credential.Verify(
                c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "Inferred token use is best effort and is disabled after the first failure.");
        }

        [DataTestMethod]
        [DataRow("User Id=app;Data Source=localhost:1521/svc", null, true, DisplayName = "empty password infers token")]
        [DataRow("User Id=app;Password=x;Data Source=localhost:1521/svc", null, false, DisplayName = "explicit password")]
        [DataRow("User Id=/;Data Source=localhost:1521/svc", null, false, DisplayName = "external auth")]
        [DataRow("User Id=app;Data Source=(DESCRIPTION=(SECURITY=(MY_WALLET_DIRECTORY=/w)))", null, false, DisplayName = "wallet directory descriptor")]
        [DataRow("User Id=app;Data Source=(DESCRIPTION=(SECURITY=(my_wallet_directory = /w)))", null, false, DisplayName = "wallet directory is case and space insensitive")]
        [DataRow("User Id=app;Data Source=(DESCRIPTION=(ADDRESS=(HOST=h))(WALLET_LOCATION=(SOURCE=(METHOD=FILE)(METHOD_DATA=(DIRECTORY=/w)))))", null, false, DisplayName = "wallet location descriptor")]
        [DataRow("User Id=app;Data Source=wallet-db.corp:1521/svc", null, true, DisplayName = "host named wallet-db is not a wallet")]
        [DataRow("User Id=app;Data Source=(DESCRIPTION=(ADDRESS=(HOST=wallet.corp)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=wallet_svc)))", null, true, DisplayName = "wallet in host and service name is not a wallet")]
        [DataRow("User Id=app;Data Source=localhost:1521/svc", false, false, DisplayName = "option false")]
        [DataRow("User Id=app;Password=x;Data Source=localhost:1521/svc", true, true, DisplayName = "option true wins over password")]
        [DataRow("User Id=/;Data Source=localhost:1521/svc", true, true, DisplayName = "option true wins over external auth")]
        [DataRow("User Id=app;Data Source=(DESCRIPTION=(SECURITY=(MY_WALLET_DIRECTORY=/w)))", true, true, DisplayName = "option true wins over wallet")]
        public void ShouldManagedIdentityAccessBeAttempted_Precedence(
            string connectionString,
            bool? option,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                OracleQueryExecutor.ShouldManagedIdentityAccessBeAttempted(
                    new OracleConnectionStringBuilder(connectionString),
                    option));
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
        [DataRow("1", true)]
        [DataRow("0", false)]
        [DataRow("true", true)]
        [DataRow("false", false)]
        public void OracleOptions_ManagedIdentity_IsTriState(object? configured, bool? expected)
        {
            Dictionary<string, object?> options = new();
            if (configured is not null)
            {
                options["managed-identity"] = configured;
            }

            DataSource dataSource = new(DatabaseType.Oracle, PASSWORDLESS, options);

            Assert.AreEqual(expected, dataSource.GetTypedOptions<OracleOptions>()!.ManagedIdentity);
        }

        [DataTestMethod]
        [DataRow("\"maybe\"")]
        [DataRow("\"2\"")]
        [DataRow("\"\"")]
        [DataRow("1")]
        public void OracleOptions_ManagedIdentity_InvalidValueIsRejected(string jsonValue)
        {
            string json = "{\"database-type\":\"oracle\",\"connection-string\":\"User Id=app;Data Source=localhost:1521/svc\",\"options\":{\"managed-identity\":" + jsonValue + "}}";
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();

            // Strings (and numbers) outside true/false/"1"/"0" are rejected, either while the options
            // are read at config load or when they are first materialized.
            Assert.ThrowsException<JsonException>(() =>
                JsonSerializer.Deserialize<DataSource>(json, serializerOptions)!.GetTypedOptions<OracleOptions>());
        }

        [DataTestMethod]
        [DataRow("\"1\"", true)]
        [DataRow("\"0\"", false)]
        [DataRow("\"true\"", true)]
        [DataRow("true", true)]
        [DataRow("false", false)]
        public void OracleOptions_ManagedIdentity_ParsedFromConfigJson(string jsonValue, bool expected)
        {
            string json = "{\"database-type\":\"oracle\",\"connection-string\":\"User Id=app;Data Source=localhost:1521/svc\",\"options\":{\"managed-identity\":" + jsonValue + "}}";

            DataSource dataSource = JsonSerializer.Deserialize<DataSource>(json, RuntimeConfigLoader.GetSerializationOptions())!;

            Assert.AreEqual(expected, dataSource.GetTypedOptions<OracleOptions>()!.ManagedIdentity);
        }

        /// <summary>
        /// The schema's option description and the documented behavior must stay in step with the
        /// implemented precedence and value forms.
        /// </summary>
        [TestMethod]
        public void ManagedIdentityOption_SchemaAndDocsAreConsistent()
        {
            string root = FindRepositoryRoot();
            string schema = File.ReadAllText(Path.Combine(root, "schemas", "dab.draft.schema.json"));
            using JsonDocument schemaDocument = JsonDocument.Parse(schema);
            JsonElement managedIdentity = default;
            foreach (JsonElement branch in schemaDocument.RootElement
                .GetProperty("properties").GetProperty("data-source").GetProperty("allOf").EnumerateArray())
            {
                if (branch.TryGetProperty("then", out JsonElement then) &&
                    then.GetProperty("properties").TryGetProperty("options", out JsonElement options) &&
                    options.GetProperty("properties").TryGetProperty(OracleOptions.MANAGED_IDENTITY_OPTION, out JsonElement found))
                {
                    managedIdentity = found;
                }
            }

            Assert.AreNotEqual(JsonValueKind.Undefined, managedIdentity.ValueKind, "managed-identity is missing from the Oracle options schema.");
            Assert.AreEqual("#/$defs/boolean-or-string", managedIdentity.GetProperty("$ref").GetString());

            // The shared boolean-or-string definition admits exactly the forms the parser accepts.
            string pattern = schemaDocument.RootElement.GetProperty("$defs").GetProperty("boolean-or-string")
                .GetProperty("oneOf")[0].GetProperty("pattern").GetString()!;
            StringAssert.Contains(pattern, "true|false|1|0");

            string docs = File.ReadAllText(Path.Combine(root, "docs", "design", "oracle-support.md"));
            StringAssert.Contains(docs, "`managed-identity`");
            StringAssert.Contains(docs, "OracleConnection.AccessToken");
            StringAssert.Contains(docs, "User Id=/");
            StringAssert.Contains(docs, "MY_WALLET_DIRECTORY");
            StringAssert.Contains(docs, "wallet-db.corp");
            StringAssert.Contains(docs, "503");
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "schemas", "dab.draft.schema.json")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "Could not locate the repository root.");
            return directory.FullName;
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
