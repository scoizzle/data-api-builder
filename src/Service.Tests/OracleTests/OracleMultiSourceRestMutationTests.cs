// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Oracle.ManagedDataAccess.Client;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.AppServiceAuthentication;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// REST writes for an entity on a second Oracle data source must use that source.
    /// The default source keeps an empty control table with the same name.
    /// Not run in the default job: it needs two Oracle schemas.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleMultiSourceRestMutationTests
    {
        private const string ChildUser = "DAB_MULTISOURCE";
        private const string ChildPassword = "Dab Multi 1";
        private const string ItemTable = "DAB_MS_ITEM";
        private const string EntityName = "MultisourceItem";
        private const string Title = "Ada";

        [TestMethod]
        public async Task RestInsert_WithReadPolicy_WritesAndReadsOnlyEntityDataSource()
        {
            await WithChildSource(async (client, system) =>
            {
                HttpResponseMessage response = await PostItem(client, id: 1);
                string body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);
                StringAssert.Contains(body, Title);
                Assert.AreEqual(1, Count(system, ChildUser + "." + ItemTable));
                Assert.AreEqual(0, Count(system, "SYSTEM." + ItemTable));
            });
        }

        [TestMethod]
        public async Task RestUpdate_WithReadPolicy_WritesAndReadsOnlyEntityDataSource()
        {
            await WithChildSource(async (client, system) =>
            {
                Execute(system, "INSERT INTO " + ChildUser + "." + ItemTable + " (id, title) VALUES (1, '" + Title + "')");

                using HttpRequestMessage request = new(HttpMethod.Put, "api/" + EntityName + "/id/1")
                {
                    Content = new StringContent("{\"title\":\"" + Title + "\"}", Encoding.UTF8, "application/json")
                };
                AddRole(request, "authenticated");
                HttpResponseMessage response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
                StringAssert.Contains(body, Title);
                Assert.AreEqual(1, Count(system, ChildUser + "." + ItemTable));
                Assert.AreEqual(0, Count(system, "SYSTEM." + ItemTable));
            });
        }

        [TestMethod]
        public async Task RestInsert_DoesNotWriteToDefaultSource()
        {
            await WithChildSource(async (client, system) =>
            {
                HttpResponseMessage response = await PostItem(client, id: 2);
                string body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);
                Assert.AreEqual(1, Count(system, ChildUser + "." + ItemTable));
                Assert.AreEqual(0, Count(system, "SYSTEM." + ItemTable));
            });
        }

        private static async Task<HttpResponseMessage> PostItem(HttpClient client, int id)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "api/" + EntityName)
            {
                Content = new StringContent(
                    "{\"id\":" + id + ",\"title\":\"" + Title + "\"}",
                    Encoding.UTF8,
                    "application/json")
            };
            AddRole(request, "authenticated");
            return await client.SendAsync(request);
        }

        private static async Task WithChildSource(Func<HttpClient, OracleConnection, Task> body)
        {
            if (!TryOpen(out OracleConnection system))
            {
                FailOrSkip("Live Oracle is not available.");
                return;
            }

            string parentPath = Path.Combine(Path.GetTempPath(), "dab-oracle-multisource-" + Guid.NewGuid().ToString("N") + ".json");
            string childPath = Path.Combine(Path.GetTempPath(), "oracle-multisource-child-" + Guid.NewGuid().ToString("N") + ".json");
            using (system)
            {
                try
                {
                    PrepareChild(system);
                    RuntimeConfig parent = LoadParentConfig();
                    OracleConnectionStringBuilder childBuilder = new(parent.DataSource!.ConnectionString);
                    childBuilder.UserID = ChildUser;
                    childBuilder.Password = ChildPassword;
                    File.WriteAllText(childPath, ChildConfigJson(childBuilder.ConnectionString));
                    RuntimeConfig withChild = parent with
                    {
                        DataSourceFiles = new DataSourceFiles([childPath])
                    };
                    File.WriteAllText(parentPath, withChild.ToJson());

                    string[] args = ["--ConfigFileName=" + parentPath];
                    using TestServer server = new(Program.CreateWebHostBuilder(args));
                    using HttpClient client = server.CreateClient();
                    await body(client, system);
                }
                finally
                {
                    if (File.Exists(parentPath))
                    {
                        File.Delete(parentPath);
                    }

                    if (File.Exists(childPath))
                    {
                        File.Delete(childPath);
                    }
                }
            }
        }

        private static void PrepareChild(OracleConnection system)
        {
            try
            {
                Execute(system, @"
BEGIN
  BEGIN
    EXECUTE IMMEDIATE 'CREATE USER " + ChildUser + @" IDENTIFIED BY ""Dab Multi 1""';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE = -1920 THEN
        EXECUTE IMMEDIATE 'ALTER USER " + ChildUser + @" IDENTIFIED BY ""Dab Multi 1""';
      ELSE
        RAISE;
      END IF;
  END;
  EXECUTE IMMEDIATE 'GRANT CREATE SESSION TO " + ChildUser + @"';
  EXECUTE IMMEDIATE 'GRANT CREATE TABLE TO " + ChildUser + @"';
  EXECUTE IMMEDIATE 'ALTER USER " + ChildUser + @" QUOTA UNLIMITED ON USERS';
END;");
                Execute(system, DropTable("SYSTEM." + ItemTable));
                Execute(system, DropTable(ChildUser + "." + ItemTable));
                Execute(system, "CREATE TABLE SYSTEM." + ItemTable + " (id NUMBER PRIMARY KEY, title VARCHAR2(100))");
                Execute(system, "CREATE TABLE " + ChildUser + "." + ItemTable + " (id NUMBER PRIMARY KEY, title VARCHAR2(100))");
            }
            catch (OracleException exception) when (exception.Number is 1031 or 65096 or 65048)
            {
                FailOrSkip("Live Oracle rejected the second schema (ORA-" + exception.Number + "): " + exception.Message);
            }
        }

        private static string DropTable(string qualifiedName)
        {
            return @"
BEGIN
  EXECUTE IMMEDIATE 'DROP TABLE " + qualifiedName + @" PURGE';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -942 THEN RAISE; END IF;
END;";
        }

        private static string ChildConfigJson(string connectionString)
        {
            string quotedConnection = JsonSerializer.Serialize(connectionString);
            return @"{
  ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
  ""data-source"": {
    ""database-type"": ""oracle"",
    ""connection-string"": " + quotedConnection + @"
  },
  ""runtime"": {
    ""rest"": { ""enabled"": true, ""path"": ""/api"" },
    ""graphql"": { ""enabled"": false },
    ""host"": { ""authentication"": { ""provider"": ""AppService"" }, ""mode"": ""development"" }
  },
  ""entities"": {
    """ + EntityName + @""": {
      ""source"": { ""object"": """ + ItemTable + @""", ""type"": ""table"" },
      ""rest"": { ""enabled"": true },
      ""graphql"": { ""enabled"": false, ""type"": { ""singular"": """ + EntityName + @""", ""plural"": """ + EntityName + @"s"" } },
      ""permissions"": [{
        ""role"": ""authenticated"",
        ""actions"": [
          { ""action"": ""create"" },
          { ""action"": ""read"", ""policy"": { ""database"": ""@item.title eq '" + Title + @"'"" } },
          { ""action"": ""update"" }
        ]
      }]
    }
  }
}";
        }

        private static RuntimeConfig LoadParentConfig()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.ORACLE);
            return TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader()).GetConfig();
        }

        private static void AddRole(HttpRequestMessage request, string role)
        {
            request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, role);
            request.Headers.Add(
                AuthenticationOptions.CLIENT_PRINCIPAL_HEADER,
                AuthTestHelper.CreateAppServiceEasyAuthToken(
                    roleClaimType: AuthenticationOptions.ROLE_CLAIM_TYPE,
                    additionalClaims:
                    [
                        new AppServiceClaim
                        {
                            Typ = AuthenticationOptions.ROLE_CLAIM_TYPE,
                            Val = role
                        }
                    ]));
        }

        private static int Count(OracleConnection connection, string qualifiedTable)
        {
            using OracleCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM " + qualifiedTable;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static void Execute(OracleConnection connection, string sql)
        {
            using OracleCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static bool TryOpen(out OracleConnection connection)
        {
            string? connectionString = TryGetConnectionString();
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

        private static string? TryGetConnectionString()
        {
            string? env = Environment.GetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING);
            if (!string.IsNullOrEmpty(env))
            {
                return env;
            }

            string configPath = Path.Combine(AppContext.BaseDirectory, "dab-config.Oracle.json");
            if (!File.Exists(configPath))
            {
                configPath = Path.Combine(Directory.GetCurrentDirectory(), "dab-config.Oracle.json");
            }

            if (!File.Exists(configPath))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("data-source", out JsonElement dataSource)
                && dataSource.TryGetProperty("connection-string", out JsonElement connectionString))
            {
                return connectionString.GetString();
            }

            return null;
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
    }
}
