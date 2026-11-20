// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using HealthCheckUtilities = Azure.DataApiBuilder.Service.HealthCheck.Utilities;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// The Oracle data-source health probe must use the query executor's connection: the prepared
    /// connection string (hosted TLS check, bounded statement cache) and the managed identity token
    /// path, not a raw connection built from the configured string (#68).
    /// </summary>
    [TestClass]
    public class OracleHealthCheckTests
    {
        private const string PASSWORDLESS = "User Id=app;Pooling=false;Data Source=localhost:1521/svc";

        [TestMethod]
        public async Task ExecuteDbQueryAsync_Oracle_UsesExecutorConnectionAndTokenPath()
        {
            (RuntimeConfigProvider provider, string dataSourceName) = CreateProvider(new() { ["managed-identity"] = true });
            RecordingOracleQueryExecutor executor = new(provider);
            HttpUtilities utilities = CreateUtilities(provider, executor);

            string? error = await utilities.ExecuteDbQueryAsync(
                HealthCheckUtilities.GetDatSourceQuery(DatabaseType.Oracle),
                PASSWORDLESS,
                HealthCheckUtilities.GetDbProviderFactory(DatabaseType.Oracle),
                DatabaseType.Oracle);

            Assert.AreEqual(RecordingOracleQueryExecutor.STOP_MESSAGE, error, "The probe must reach the executor's token step.");
            Assert.AreEqual(1, executor.TokenRequests.Count);
            Assert.AreEqual(dataSourceName, executor.TokenRequests[0].DataSourceName);
            Assert.AreEqual(executor.ConnectionStringBuilders[dataSourceName].ConnectionString, executor.TokenRequests[0].ConnectionString);
            Assert.IsTrue(
                executor.TokenRequests[0].ConnectionString.Contains("Statement Cache Size=20", StringComparison.OrdinalIgnoreCase),
                "The probe must use the prepared connection string: " + executor.TokenRequests[0].ConnectionString);
        }

        [TestMethod]
        public async Task ExecuteDbQueryAsync_OracleTokenFailure_ReportsTheError()
        {
            (RuntimeConfigProvider provider, _) = CreateProvider(new() { ["managed-identity"] = true });
            OracleQueryExecutor executor = new(
                provider,
                new OracleDbExceptionParser(provider),
                Mock.Of<ILogger<IQueryExecutor>>(),
                Mock.Of<IHttpContextAccessor>());
            Mock<Azure.Identity.DefaultAzureCredential> credential = new();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<Azure.Core.TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromException<Azure.Core.AccessToken>(new Azure.Identity.CredentialUnavailableException("no credential")));
            executor.AzureCredential = credential.Object;
            HttpUtilities utilities = CreateUtilities(provider, executor);

            string? error = await utilities.ExecuteDbQueryAsync(
                HealthCheckUtilities.GetDatSourceQuery(DatabaseType.Oracle),
                PASSWORDLESS,
                HealthCheckUtilities.GetDbProviderFactory(DatabaseType.Oracle),
                DatabaseType.Oracle);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "managed-identity", "A forced token that cannot be obtained must make the probe fail, not connect without it.");
        }

        private static HttpUtilities CreateUtilities(RuntimeConfigProvider provider, OracleQueryExecutor executor)
        {
            Mock<IHttpClientFactory> httpClientFactory = new();
            httpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient { BaseAddress = new Uri("http://localhost:5000") });
            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(f => f.GetQueryExecutor(DatabaseType.Oracle)).Returns(executor);

            return new HttpUtilities(
                Mock.Of<ILogger<HttpUtilities>>(),
                Mock.Of<IMetadataProviderFactory>(),
                provider,
                httpClientFactory.Object,
                queryManagerFactory.Object);
        }

        private static (RuntimeConfigProvider Provider, string DataSourceName) CreateProvider(Dictionary<string, object?> options)
        {
            const string dataSourceName = "oracle";
            DataSource source = new(DatabaseType.Oracle, PASSWORDLESS, options);
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: source,
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>()),
                DefaultDataSourceName: dataSourceName,
                DataSourceNameToDataSource: new Dictionary<string, DataSource> { [dataSourceName] = source },
                EntityNameToDataSourceName: new Dictionary<string, string>());
            return (new RuntimeConfigProvider(new FileSystemRuntimeConfigLoader(new MockFileSystem()) { RuntimeConfig = config }), dataSourceName);
        }

        /// <summary>
        /// Records the token step and stops before a database connection is opened.
        /// </summary>
        private sealed class RecordingOracleQueryExecutor : OracleQueryExecutor
        {
            public const string STOP_MESSAGE = "stopped before opening a connection";

            public List<(string DataSourceName, string ConnectionString)> TokenRequests { get; } = new();

            public RecordingOracleQueryExecutor(RuntimeConfigProvider provider)
                : base(provider, new OracleDbExceptionParser(provider), Mock.Of<ILogger<IQueryExecutor>>(), Mock.Of<IHttpContextAccessor>())
            {
            }

            public override Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn, string dataSourceName, CancellationToken cancellationToken)
            {
                TokenRequests.Add((dataSourceName, conn.ConnectionString));
                throw new InvalidOperationException(STOP_MESSAGE);
            }
        }
    }
}
