// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Live Oracle tests for Option B: two INSERTs + SELECT on one connection/tx.
    /// Skips when Oracle is not reachable (same category as other Oracle tests).
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleMultipleCreateTransactionIntegrationTests
    {
        private static string? TryGetConnectionString()
        {
            string env = Environment.GetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING);
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

            JObject json = JObject.Parse(File.ReadAllText(configPath));
            return json["data-source"]?["connection-string"]?.ToString();
        }

        private static bool TryOpen(string connectionString, out OracleConnection connection)
        {
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

        [TestMethod]
        public void LocalTransaction_CommitMakesBothInsertsVisible_RollbackMakesNeitherVisible()
        {
            string? connectionString = TryGetConnectionString();
            if (string.IsNullOrEmpty(connectionString) || !TryOpen(connectionString, out OracleConnection conn))
            {
                Assert.Inconclusive("Live Oracle is not available.");
                return;
            }

            using (conn)
            {
                using OracleCommand setup = conn.CreateCommand();
                setup.CommandText = @"BEGIN
EXECUTE IMMEDIATE 'DROP TABLE dab_mc_tx_test PURGE';
EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF;
END;";
                setup.ExecuteNonQuery();
                setup.CommandText = "CREATE TABLE dab_mc_tx_test (id NUMBER PRIMARY KEY, name VARCHAR2(50))";
                setup.ExecuteNonQuery();

                using (OracleTransaction tx = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    ExecuteNonQuery(conn, tx, "INSERT INTO dab_mc_tx_test (id, name) VALUES (1, 'a')");
                    ExecuteNonQuery(conn, tx, "INSERT INTO dab_mc_tx_test (id, name) VALUES (2, 'b')");
                    Assert.AreEqual(2, CountRows(conn, tx));
                    tx.Commit();
                }

                Assert.AreEqual(2, CountRows(conn, transaction: null));

                using (OracleTransaction tx = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    ExecuteNonQuery(conn, tx, "INSERT INTO dab_mc_tx_test (id, name) VALUES (3, 'c')");
                    ExecuteNonQuery(conn, tx, "INSERT INTO dab_mc_tx_test (id, name) VALUES (4, 'd')");
                    Assert.AreEqual(4, CountRows(conn, tx));
                    tx.Rollback();
                }

                Assert.AreEqual(2, CountRows(conn, transaction: null));
            }
        }

        [TestMethod]
        public void LocalTransaction_FailedChildDoesNotRetryOnSameTransaction()
        {
            string? connectionString = TryGetConnectionString();
            if (string.IsNullOrEmpty(connectionString) || !TryOpen(connectionString, out OracleConnection conn))
            {
                Assert.Inconclusive("Live Oracle is not available.");
                return;
            }

            using (conn)
            {
                RuntimeConfigProvider provider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
                Mock<ILogger<IQueryExecutor>> logger = new();
                Mock<IHttpContextAccessor> accessor = new();
                OracleQueryExecutor executor;
                try
                {
                    executor = new OracleQueryExecutor(
                        provider,
                        new Mock<DbExceptionParser>(provider).Object,
                        logger.Object,
                        accessor.Object);
                }
                catch (Exception)
                {
                    Assert.Inconclusive("Could not construct OracleQueryExecutor without a loaded Oracle config.");
                    return;
                }

                using OracleTransaction tx = executor.BeginLocalReadCommittedTransaction(conn, dataSourceName: "default");
                logger.Verify(
                    x => x.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("local OracleTransaction", StringComparison.OrdinalIgnoreCase)),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    Times.Once);

                try
                {
                    ExecuteNonQuery(conn, tx, "INSERT INTO definitely_not_a_table_ec643c4f VALUES (1)");
                    Assert.Fail("Expected INSERT to fail.");
                }
                catch (OracleException)
                {
                    // Do not retry on the same doomed transaction.
                }

                tx.Rollback();
            }
        }

        private static void ExecuteNonQuery(OracleConnection conn, OracleTransaction tx, string sql)
        {
            using OracleCommand cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static int CountRows(OracleConnection conn, OracleTransaction? transaction)
        {
            using OracleCommand cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT COUNT(*) FROM dab_mc_tx_test";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}
