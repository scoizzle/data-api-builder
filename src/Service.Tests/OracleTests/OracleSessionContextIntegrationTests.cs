// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Oracle.ManagedDataAccess.Client;

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
