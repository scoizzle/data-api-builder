// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.AppServiceAuthentication;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Verifies that enabling "set-session-context" forwards the caller's claims to Oracle
    /// application contexts: single-valued claims as scalars, multi-valued claims as JSON arrays
    /// (so database-side policies can expand them with JSON_TABLE and IN).
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleSessionContextIntegrationTests
    {
        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.ORACLE);
        }

        [TestMethod]
        public async Task RestRequest_ForwardsClaimsToSessionContext()
        {
            const string SESSION_CONFIG = $"session-context-config.{TestCategory.ORACLE}.json";
            RuntimeConfigProvider configProvider =
                TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            RuntimeConfig config = configProvider.GetConfig();

            // Ensure the context namespace, package, and claims view exist before serving requests.
            using (OracleConnection bootstrap = new(config.DataSource!.ConnectionString))
            {
                bootstrap.Open();
                using OracleCommand command = bootstrap.CreateCommand();
                command.CommandText = File.ReadAllText("DatabaseSchema-Oracle.sql");
                command.ExecuteNonQuery();
            }

            RuntimeConfig updatedConfig = config with
            {
                DataSource = config.DataSource! with
                {
                    Options = new System.Collections.Generic.Dictionary<string, object?>
                    {
                        // Matches OracleOptions.SetSessionContext (hyphenated naming policy).
                        { "set-session-context", true }
                    }
                }
            };

            File.WriteAllText(SESSION_CONFIG, updatedConfig.ToJson());

            try
            {
                string[] args = [$"--ConfigFileName={SESSION_CONFIG}"];
                using TestServer server = new(Program.CreateWebHostBuilder(args));
                using HttpClient client = server.CreateClient();

                const string PRINCIPAL = """
                    {
                        "auth_typ": "aad",
                        "claims": [
                            { "typ": "sub", "val": "user-1" },
                            { "typ": "groups", "val": "group1" },
                            { "typ": "groups", "val": "group2" }
                        ]
                    }
                    """;
                string encodedPrincipal = Convert.ToBase64String(Encoding.UTF8.GetBytes(PRINCIPAL));

                HttpRequestMessage request = new(HttpMethod.Get, "api/session_claims");
                request.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, encodedPrincipal);
                request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, "authenticated");

                HttpResponseMessage response = await client.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, responseBody);

                using JsonDocument document = JsonDocument.Parse(responseBody);
                JsonElement row = document.RootElement.GetProperty("value")[0];

                Assert.AreEqual("user-1", GetStringProperty(row, "sub"));
                Assert.AreEqual("[\"group1\",\"group2\"]", GetStringProperty(row, "claim_groups"));
                Assert.AreEqual("authenticated", GetStringProperty(row, "roles"));
            }
            finally
            {
                if (File.Exists(SESSION_CONFIG))
                {
                    File.Delete(SESSION_CONFIG);
                }
            }
        }

        /// <summary>
        /// A later statement on the same pooled connection, with no HttpContext, must clear the
        /// claims the previous statement stored. The async and sync executor cores both clear.
        /// </summary>
        [TestMethod]
        public async Task ContextlessStatement_ClearsPreviousClaims()
        {
            await AssertContextlessStatementClears(useSyncCore: false);
        }

        [TestMethod]
        public async Task ContextlessStatement_SyncCore_ClearsPreviousClaims()
        {
            await AssertContextlessStatementClears(useSyncCore: true);
        }

        /// <summary>
        /// Probe: a Book insert whose read policy forces a follow-up read, with session context on.
        /// Likely to succeed on the integration branch; a failure is evidence, not a known red.
        /// </summary>
        [TestMethod]
        public async Task MutationWithSessionContext_Succeeds()
        {
            if (!TryOpen(out OracleConnection bootstrap))
            {
                FailOrSkip("Live Oracle is not available.");
                return;
            }

            const string sessionConfig = "session-context-mutation.Oracle.json";
            using (bootstrap)
            {
                Execute(bootstrap, File.ReadAllText(SchemaPath()));
                EnsureSessionContextPackage(bootstrap);
            }

            RuntimeConfig config = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader()).GetConfig();
            RuntimeConfig updated = config with
            {
                DataSource = config.DataSource! with
                {
                    Options = new Dictionary<string, object?> { ["set-session-context"] = true }
                }
            };
            File.WriteAllText(sessionConfig, updated.ToJson());
            int id = 880_000_000 + Random.Shared.Next(1_000_000);
            try
            {
                string[] args = ["--ConfigFileName=" + sessionConfig];
                using TestServer server = new(Program.CreateWebHostBuilder(args));
                using HttpClient client = server.CreateClient();
                using HttpRequestMessage request = new(HttpMethod.Post, "api/Book")
                {
                    Content = new StringContent(
                        "{\"id\":" + id + ",\"title\":\"Policy-Test-01\",\"publisher_id\":1234}",
                        Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, "policy_tester_01");
                request.Headers.Add(
                    AuthenticationOptions.CLIENT_PRINCIPAL_HEADER,
                    AuthTestHelper.CreateAppServiceEasyAuthToken(
                        roleClaimType: AuthenticationOptions.ROLE_CLAIM_TYPE,
                        additionalClaims:
                        [
                            new AppServiceClaim
                            {
                                Typ = AuthenticationOptions.ROLE_CLAIM_TYPE,
                                Val = "policy_tester_01"
                            }
                        ]));

                HttpResponseMessage response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);
                StringAssert.Contains(body, "Policy-Test-01");
            }
            finally
            {
                if (File.Exists(sessionConfig))
                {
                    File.Delete(sessionConfig);
                }

                if (TryOpen(out OracleConnection cleanup))
                {
                    using (cleanup)
                    {
                        Execute(cleanup, "DELETE FROM books WHERE id = " + id);
                    }
                }
            }
        }

        /// <summary>
        /// Probe: ODP.NET does not require OracleCommand.Transaction when the connection already has
        /// a local transaction. Production still assigns the transaction. A throw here is the evidence.
        /// </summary>
        [TestMethod]
        public void LocalTransaction_UnassignedCommand_Behavior()
        {
            if (!TryOpen(out OracleConnection connection))
            {
                FailOrSkip("Live Oracle is not available.");
                return;
            }

            using (connection)
            {
                EnsureSessionContextPackage(connection);
                using OracleTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                using OracleCommand command = connection.CreateCommand();
                command.CommandText = "BEGIN DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS; END;";
                try
                {
                    command.ExecuteNonQuery();
                }
                catch (OracleException exception)
                {
                    Assert.Fail(
                        "ODP.NET rejected CLEAR_CLAIMS on a connection with an active local transaction when OracleCommand.Transaction was left unset: "
                        + exception.Message);
                }

                transaction.Rollback();
            }
        }

        private static async Task AssertContextlessStatementClears(bool useSyncCore)
        {
            if (!TryOpen(out OracleConnection connection))
            {
                FailOrSkip("Live Oracle is not available.");
                return;
            }

            using (connection)
            {
                EnsureSessionContextPackage(connection);
                OracleQueryExecutor executor = CreateSessionContextExecutor(connection.ConnectionString);
                DefaultHttpContext seeded = new()
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "TestAuth"))
                };
                Dictionary<string, DbConnectionParam> parameters = new();

                await executor.ExecuteQueryOnConnectionAsync<object>(
                    connection,
                    "SELECT 1 FROM DUAL",
                    parameters,
                    dataReaderHandler: null,
                    dataSourceName: string.Empty,
                    transaction: null,
                    httpContext: seeded);

                string? sub = useSyncCore
                    ? executor.ExecuteQueryOnConnection(
                        connection,
                        "SELECT SYS_CONTEXT('DAB_SESSION_CONTEXT', 'sub') FROM DUAL",
                        parameters,
                        ReadContext,
                        httpContext: null,
                        args: null,
                        dataSourceName: string.Empty,
                        transaction: null)
                    : await executor.ExecuteQueryOnConnectionAsync(
                        connection,
                        "SELECT SYS_CONTEXT('DAB_SESSION_CONTEXT', 'sub') FROM DUAL",
                        parameters,
                        ReadContextAsync,
                        dataSourceName: string.Empty,
                        transaction: null,
                        httpContext: null);

                Assert.IsNull(sub, "A context-less statement left claim 'sub' = '" + sub + "' on the connection.");
            }
        }

        private static OracleQueryExecutor CreateSessionContextExecutor(string connectionString)
        {
            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, connectionString, new Dictionary<string, object?> { ["set-session-context"] = true }),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>()));
            FileSystemRuntimeConfigLoader loader = new(new System.IO.Abstractions.FileSystem()) { RuntimeConfig = config };
            RuntimeConfigProvider provider = new(loader);
            return new OracleQueryExecutor(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }

        private static void EnsureSessionContextPackage(OracleConnection connection)
        {
            Execute(connection, "CREATE OR REPLACE CONTEXT DAB_SESSION_CONTEXT USING DAB_SESSION_CONTEXT_PKG");
            Execute(connection, @"
CREATE OR REPLACE PACKAGE DAB_SESSION_CONTEXT_PKG AS
    PROCEDURE CLEAR_CLAIMS;
    PROCEDURE SET_CLAIM(p_name IN VARCHAR2, p_value IN VARCHAR2);
END DAB_SESSION_CONTEXT_PKG;");
            Execute(connection, @"
CREATE OR REPLACE PACKAGE BODY DAB_SESSION_CONTEXT_PKG AS
    PROCEDURE CLEAR_CLAIMS IS
    BEGIN
        DBMS_SESSION.CLEAR_ALL_CONTEXT('DAB_SESSION_CONTEXT');
    END CLEAR_CLAIMS;

    PROCEDURE SET_CLAIM(p_name IN VARCHAR2, p_value IN VARCHAR2) IS
    BEGIN
        DBMS_SESSION.SET_CONTEXT('DAB_SESSION_CONTEXT', p_name, p_value);
    END SET_CLAIM;
END DAB_SESSION_CONTEXT_PKG;");
        }

        private static string? ReadContext(DbDataReader reader, List<string>? args)
        {
            _ = args;
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return null;
            }

            return reader.GetString(0);
        }

        private static async Task<string?> ReadContextAsync(DbDataReader reader, List<string>? args)
        {
            _ = args;
            if (!await reader.ReadAsync() || reader.IsDBNull(0))
            {
                return null;
            }

            return reader.GetString(0);
        }

        private static string SchemaPath()
        {
            string nextToAssembly = Path.Combine(AppContext.BaseDirectory, "DatabaseSchema-Oracle.sql");
            if (File.Exists(nextToAssembly))
            {
                return nextToAssembly;
            }

            return "DatabaseSchema-Oracle.sql";
        }

        private static void Execute(OracleConnection connection, string sql)
        {
            using OracleCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static bool TryOpen(out OracleConnection connection)
        {
            string? connectionString = Environment.GetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING);
            if (string.IsNullOrEmpty(connectionString))
            {
                string configPath = Path.Combine(AppContext.BaseDirectory, "dab-config.Oracle.json");
                if (!File.Exists(configPath))
                {
                    configPath = "dab-config.Oracle.json";
                }

                if (File.Exists(configPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (document.RootElement.TryGetProperty("data-source", out JsonElement dataSource)
                        && dataSource.TryGetProperty("connection-string", out JsonElement value))
                    {
                        connectionString = value.GetString();
                    }
                }
            }

            if (string.IsNullOrEmpty(connectionString))
            {
                connection = null!;
                return false;
            }

            connection = new OracleConnection(connectionString);
            try
            {
                connection.Open();
                return true;
            }
            catch (Exception)
            {
                connection.Dispose();
                connection = null!;
                return false;
            }
        }

        private static void FailOrSkip(string message)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            {
                Assert.Fail(message + " The Oracle CI pipeline must provide a reachable database.");
            }

            Assert.Inconclusive(message);
        }

        /// <summary>
        /// Reads a property by name, ignoring casing (Oracle may surface JSON keys in catalog casing).
        /// </summary>
        private static string? GetStringProperty(JsonElement row, string name)
        {
            foreach (JsonProperty property in row.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.GetString();
                }
            }

            return null;
        }
    }
}
