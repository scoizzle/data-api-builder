// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Hosted Oracle transport is validate-only: TCPS plus an affirmative server DN match.
    /// The connection string is not rewritten, and the process-wide Oracle encryption
    /// setting is left alone. Metadata connections use the same cache bound.
    /// </summary>
    [TestClass]
    public class OracleTransportSecurityTests
    {
        private const string TcpsDescriptor =
            "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCPS)(HOST=db.example)(PORT=2484))(CONNECT_DATA=(SERVICE_NAME=orcl))(SECURITY=(SSL_SERVER_DN_MATCH=TRUE)))";

        private const string TcpsAddressList =
            "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCPS)(HOST=a.example)(PORT=2484))(ADDRESS=(PROTOCOL=TCPS)(HOST=b.example)(PORT=2484)))(CONNECT_DATA=(SERVICE_NAME=orcl))(SECURITY=(SSL_SERVER_DN_MATCH=TRUE)))";

        [TestMethod]
        public void HostedDescriptorWithTcpsAndDnMatch_IsAccepted()
        {
            AssertAccepted(TcpsDescriptor, "PROTOCOL=TCPS");
            AssertAccepted(TcpsAddressList, "PROTOCOL=TCPS");
        }

        [TestMethod]
        public void HostedDescriptorWithDnMatchYesOrOn_IsAccepted()
        {
            AssertAccepted(TcpsDescriptor.Replace("=TRUE", "=yes", StringComparison.Ordinal), "SSL_SERVER_DN_MATCH=yes");
            AssertAccepted(TcpsDescriptor.Replace("=TRUE", "=on", StringComparison.Ordinal), "SSL_SERVER_DN_MATCH=on");
        }

        [TestMethod]
        public void HostedDescriptorWithoutDnMatch_Throws()
        {
            string descriptor = TcpsDescriptor.Replace("(SECURITY=(SSL_SERVER_DN_MATCH=TRUE))", string.Empty, StringComparison.Ordinal);
            AssertRejected(descriptor);
        }

        [TestMethod]
        public void HostedDescriptorWithDnMatchDisabled_Throws()
        {
            AssertRejected(TcpsDescriptor.Replace("=TRUE", "=FALSE", StringComparison.Ordinal));
            AssertRejected(TcpsDescriptor.Replace("=TRUE", "=no", StringComparison.Ordinal));
            AssertRejected(TcpsDescriptor.Replace("=TRUE", "=off", StringComparison.Ordinal));
        }

        [TestMethod]
        public void HostedDescriptorWithoutTcps_Throws()
        {
            AssertRejected(TcpsDescriptor.Replace("PROTOCOL=TCPS", "PROTOCOL=TCP", StringComparison.Ordinal));
        }

        [TestMethod]
        public void HostedMixedProtocolDescriptor_Throws()
        {
            const string mixed =
                "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=a.example)(PORT=1521))(ADDRESS=(PROTOCOL=TCPS)(HOST=b.example)(PORT=2484)))(CONNECT_DATA=(SERVICE_NAME=orcl))(SECURITY=(SSL_SERVER_DN_MATCH=TRUE)))";
            AssertRejected(mixed);
        }

        [TestMethod]
        public void HostedTnsAlias_Throws()
        {
            AssertRejected("ORCL");
        }

        [TestMethod]
        public void HostedPlainHostPort_Throws()
        {
            AssertRejected("db.example:1521/orcl");
            AssertRejected("//db.example:1521/orcl");
        }

        [TestMethod]
        public void HostedEzConnectWithDnMatch_IsAccepted()
        {
            AssertAccepted("tcps://db.example:2484/orcl?ssl_server_dn_match=yes", "ssl_server_dn_match=yes");
            AssertAccepted("tcps://db.example:2484/orcl?ssl_server_dn_match=TRUE", "ssl_server_dn_match=TRUE");
            AssertAccepted("tcps://db.example:2484/orcl?ssl_server_dn_match=on", "ssl_server_dn_match=on");
        }

        [TestMethod]
        public void HostedEzConnectWithoutTls_ThrowsActionable()
        {
            AssertRejected("tcp://db.example:1521/orcl");
            AssertRejected("tcps://db.example:2484/orcl");
            AssertRejected("tcps://db.example:2484/orcl?ssl_server_dn_match=no");
        }

        [TestMethod]
        public void HostedExecutor_DoesNotMutateProcessWideOracleConfiguration()
        {
            const string connectionString =
                "User Id=x;Password=y;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCPS)(HOST=db.example)(PORT=2484))(CONNECT_DATA=(SERVICE_NAME=orcl))(SECURITY=(SSL_SERVER_DN_MATCH=TRUE)))";
            string encryptionBefore = OracleConfiguration.SqlNetEncryptionClient;

            CreateExecutor(connectionString, lateConfigured: true);
            CreateExecutor(connectionString, lateConfigured: true);

            Assert.AreEqual(encryptionBefore, OracleConfiguration.SqlNetEncryptionClient);
        }

        [TestMethod]
        public void ShouldWarnAboutOpenCursors_DefaultIsLow_RecommendedIsNot()
        {
            Assert.IsTrue(OracleConnectionStringPolicy.ShouldWarnAboutOpenCursors(300));
            Assert.IsTrue(OracleConnectionStringPolicy.ShouldWarnAboutOpenCursors(1499));
            Assert.IsFalse(OracleConnectionStringPolicy.ShouldWarnAboutOpenCursors(1500));
            Assert.IsFalse(OracleConnectionStringPolicy.ShouldWarnAboutOpenCursors(2000));
        }

        [TestMethod]
        public void MetadataConnectionString_UsesTheSameCacheBound()
        {
            PreparedConnectionOracleMetadataProvider metadata = CreateMetadata(
                "User Id=x;Password=y;Data Source=localhost:1521/x",
                lateConfigured: false);

            OracleConnectionStringBuilder builder = new(metadata.PreparedConnectionString);
            Assert.AreEqual(OracleConnectionStringPolicy.DefaultStatementCacheSize, builder.StatementCacheSize);
            Assert.IsFalse(builder.SelfTuning);
            Assert.AreEqual("localhost:1521/x", builder.DataSource);
        }

        [TestMethod]
        public void MetadataConnectionString_HostedTcps_KeepsDescriptorAndBoundsCache()
        {
            string connectionString = "User Id=x;Password=y;Data Source=" + TcpsDescriptor;
            PreparedConnectionOracleMetadataProvider metadata = CreateMetadata(connectionString, lateConfigured: true);

            OracleConnectionStringBuilder builder = new(metadata.PreparedConnectionString);
            StringAssert.Contains(builder.DataSource, "PROTOCOL=TCPS");
            StringAssert.Contains(builder.DataSource, "SSL_SERVER_DN_MATCH=TRUE");
            Assert.AreEqual(20, builder.StatementCacheSize);
            Assert.IsFalse(builder.SelfTuning);
            Assert.IsFalse(builder.ConnectionString.Contains("ENCRYPTION_CLIENT", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void MetadataConnectionString_HostedPlainHost_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => CreateMetadata("User Id=x;Password=secret-value;Data Source=localhost:1521/x", lateConfigured: true));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.IsFalse(ex.Message.Contains("secret-value", StringComparison.Ordinal));
        }

        [TestMethod]
        public void HostedConnection_PreservesExplicitStatementCacheSizeZero()
        {
            string connectionString = "User Id=x;Password=y;Data Source=" + TcpsDescriptor + ";Statement Cache Size=0";
            string prepared = OracleConnectionStringPolicy.Prepare(connectionString, requireHostedTransport: true);
            OracleConnectionStringBuilder builder = new(prepared);
            Assert.AreEqual(0, builder.StatementCacheSize);
            Assert.IsTrue(builder.SelfTuning);
            StringAssert.Contains(builder.DataSource, "PROTOCOL=TCPS");
        }

        private static void AssertAccepted(string dataSource, string expectedFragment)
        {
            string prepared = OracleConnectionStringPolicy.Prepare(Connection(dataSource), requireHostedTransport: true);
            OracleConnectionStringBuilder builder = new(prepared);
            StringAssert.Contains(builder.DataSource, expectedFragment);
            Assert.IsFalse(builder.ConnectionString.Contains("ENCRYPTION_CLIENT", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(builder.ConnectionString.Contains("Encryption=", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(OracleConnectionStringPolicy.DefaultStatementCacheSize, builder.StatementCacheSize);
            Assert.IsFalse(builder.SelfTuning);
        }

        private static void AssertRejected(string dataSource)
        {
            string connectionString = Connection(dataSource).Replace("Password=y", "Password=secret-value", StringComparison.Ordinal);
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => OracleConnectionStringPolicy.Prepare(connectionString, requireHostedTransport: true));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            Assert.IsFalse(ex.Message.Contains("secret-value", StringComparison.Ordinal));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }

        private static string Connection(string dataSource)
        {
            return "User Id=x;Password=y;Data Source=" + dataSource;
        }

        private static OracleQueryExecutor CreateExecutor(string connectionString, bool lateConfigured)
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.Oracle, connectionString, new Dictionary<string, object?>()),
                Runtime: new RuntimeOptions(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            provider.IsLateConfigured = lateConfigured;
            return new OracleQueryExecutor(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }

        private static PreparedConnectionOracleMetadataProvider CreateMetadata(string connectionString, bool lateConfigured)
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.Oracle, connectionString, new Dictionary<string, object?>()),
                Runtime: new RuntimeOptions(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            provider.IsLateConfigured = lateConfigured;
            Mock<IAbstractQueryManagerFactory> factory = new();
            factory.Setup(candidate => candidate.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(Mock.Of<IQueryBuilder>());
            factory.Setup(candidate => candidate.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(Mock.Of<IQueryExecutor>());
            RuntimeConfigValidator validator = new(
                provider,
                new System.IO.Abstractions.TestingHelpers.MockFileSystem(),
                Mock.Of<ILogger<RuntimeConfigValidator>>());
            return new PreparedConnectionOracleMetadataProvider(
                provider,
                validator,
                factory.Object,
                Mock.Of<ILogger<ISqlMetadataProvider>>(),
                provider.GetConfig().DefaultDataSourceName);
        }

        /// <summary>
        /// Exposes the connection string the metadata provider opens. The base property is
        /// protected and init-only, and the Oracle constructor is what applies the policy.
        /// </summary>
        private sealed class PreparedConnectionOracleMetadataProvider : OracleMetadataProvider
        {
            public PreparedConnectionOracleMetadataProvider(
                RuntimeConfigProvider runtimeConfigProvider,
                RuntimeConfigValidator runtimeConfigValidator,
                IAbstractQueryManagerFactory queryManagerFactory,
                ILogger<ISqlMetadataProvider> logger,
                string dataSourceName)
                : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName)
            {
            }

            public string PreparedConnectionString => ConnectionString;
        }
    }
}
