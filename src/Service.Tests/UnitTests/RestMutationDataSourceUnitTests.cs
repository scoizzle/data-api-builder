// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// REST mutations must open metadata and the transaction against the entity's data source.
    /// The executor delegate is not run: a sentinel records the routed name and sharing.
    /// </summary>
    [TestClass]
    public class RestMutationDataSourceUnitTests
    {
        private const string DefaultSource = "default";
        private const string EntitySource = "other";
        private const string EntityName = "Item";

        /// <summary>
        /// <paramref name="isUpsert"/> is true only for <see cref="EntityActionOperation.Upsert"/>.
        /// That flag selects the upsert branch (primary key already on the URL). It is not the
        /// keyless upsert-to-insert degeneration, which has its own test.
        /// <paramref name="readPolicy"/> is true when the role's read action has a database policy,
        /// so the engine must open the transaction with <see cref="MutationConnectionSharing.RestFollowUpRead"/>.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntityActionOperation.Insert, false, false)]
        [DataRow(EntityActionOperation.Update, false, false)]
        [DataRow(EntityActionOperation.Upsert, true, false)]
        [DataRow(EntityActionOperation.Insert, false, true)]
        public async Task RestMutation_UsesEntityDataSourceForTransactionAndMetadataProvider(
            EntityActionOperation operation,
            bool isUpsert,
            bool readPolicy)
        {
            Assert.AreEqual(operation is EntityActionOperation.Upsert, isUpsert);

            CapturedRoute route = new();
            SqlMutationEngine engine = CreateEngine(
                route,
                defaultType: DatabaseType.Oracle,
                entityType: DatabaseType.Oracle,
                readPolicy: readPolicy);

            await AssertSentinelAsync(engine, CreateContext(operation, primaryKeyInUrl: operation is not EntityActionOperation.Insert));

            Assert.AreEqual(EntitySource, route.DataSourceName);
            Assert.IsFalse(route.MetadataProviders.Contains(DefaultSource), "The default metadata provider must not be used.");
            Assert.IsTrue(route.MetadataProviders.Contains(EntitySource));
            Assert.AreEqual(DatabaseType.Oracle, route.ExecutorDatabaseType);
            Assert.AreEqual(
                readPolicy ? MutationConnectionSharing.RestFollowUpRead : MutationConnectionSharing.None,
                route.Sharing);
        }

        /// <summary>
        /// DELETE never calls <see cref="IMutationTransactionExecutor"/>. It must still resolve the
        /// entity's metadata provider and query executor, and the TransactionScope isolation must
        /// follow the entity's engine when that engine differs from the default source.
        /// </summary>
        [TestMethod]
        public async Task RestDelete_UsesEntityMetadataProviderAndEngineIsolation()
        {
            CapturedRoute route = new();
            SqlMutationEngine engine = CreateEngine(
                route,
                defaultType: DatabaseType.MSSQL,
                entityType: DatabaseType.MySQL,
                readPolicy: false,
                captureDelete: true);

            await AssertSentinelAsync(engine, CreateContext(EntityActionOperation.Delete, primaryKeyInUrl: true));

            Assert.IsFalse(route.MetadataProviders.Contains(DefaultSource));
            Assert.IsTrue(route.MetadataProviders.Contains(EntitySource));
            Assert.AreEqual(DatabaseType.MySQL, route.QueryExecutorDatabaseType);
            Assert.AreEqual(IsolationLevel.RepeatableRead, route.Isolation);
            Assert.IsNull(route.DataSourceName, "DELETE does not open IMutationTransactionExecutor.");
        }

        [TestMethod]
        public async Task RestMutation_TwoMsSqlSources_UsesEntityDataSource()
        {
            CapturedRoute route = new();
            SqlMutationEngine engine = CreateEngine(
                route,
                defaultType: DatabaseType.MSSQL,
                entityType: DatabaseType.MSSQL,
                readPolicy: false);

            await AssertSentinelAsync(engine, CreateContext(EntityActionOperation.Insert, primaryKeyInUrl: false));

            Assert.AreEqual(EntitySource, route.DataSourceName);
            Assert.AreEqual(DatabaseType.MSSQL, route.ExecutorDatabaseType);
            Assert.IsFalse(route.MetadataProviders.Contains(DefaultSource));
        }

        [TestMethod]
        public async Task RestMutation_DifferentEngines_ResolvesEntityExecutor()
        {
            CapturedRoute route = new();
            SqlMutationEngine engine = CreateEngine(
                route,
                defaultType: DatabaseType.Oracle,
                entityType: DatabaseType.MSSQL,
                readPolicy: false);

            await AssertSentinelAsync(engine, CreateContext(EntityActionOperation.Insert, primaryKeyInUrl: false));

            Assert.AreEqual(EntitySource, route.DataSourceName);
            Assert.AreEqual(DatabaseType.MSSQL, route.ExecutorDatabaseType);
        }

        /// <summary>
        /// An upsert whose URL has no primary key promotes every primary-key value found in the body
        /// before the transaction opens. The promotion and the transaction both use the entity source.
        /// </summary>
        [TestMethod]
        public async Task RestUpsert_BodyPromotedPrimaryKey_UsesEntityDataSource()
        {
            CapturedRoute route = new();
            SqlMutationEngine engine = CreateEngine(
                route,
                defaultType: DatabaseType.Oracle,
                entityType: DatabaseType.Oracle,
                readPolicy: false);

            RestRequestContext context = CreateContext(EntityActionOperation.Upsert, primaryKeyInUrl: false, body: """{"id":7,"title":"Ada"}""");
            await AssertSentinelAsync(engine, context);

            Assert.AreEqual(EntitySource, route.DataSourceName);
            Assert.IsTrue(context.PrimaryKeyValuePairs.ContainsKey("id"));
        }

        /// <summary>
        /// An upsert that still has no primary key after the body is inspected degenerates to INSERT
        /// on the entity's data source.
        /// </summary>
        [TestMethod]
        public async Task RestUpsert_Keyless_DegeneratesToInsertOnEntityDataSource()
        {
            CapturedRoute route = new();
            SqlMutationEngine engine = CreateEngine(
                route,
                defaultType: DatabaseType.MSSQL,
                entityType: DatabaseType.MSSQL,
                readPolicy: false,
                runWork: true);

            RestRequestContext context = CreateContext(EntityActionOperation.Upsert, primaryKeyInUrl: false, body: """{"title":"Ada"}""");
            await AssertSentinelAsync(engine, context);

            Assert.AreEqual(EntitySource, route.DataSourceName);
            Assert.AreEqual(0, context.PrimaryKeyValuePairs.Count);
            Assert.AreEqual("insert", route.BuiltStructure);
        }

        private static async Task AssertSentinelAsync(SqlMutationEngine engine, RestRequestContext context)
        {
            try
            {
                await engine.ExecuteAsync(context);
                Assert.Fail("The mutation route must stop at the sentinel.");
            }
            catch (InvalidOperationException exception) when (exception.Message == SentinelMessage)
            {
            }
        }

        private static RestRequestContext CreateContext(EntityActionOperation operation, bool primaryKeyInUrl, string body = """{"title":"Ada"}""")
        {
            DatabaseTable table = new("dbo", "items");
            using JsonDocument document = JsonDocument.Parse(body);
            RestRequestContext context = operation switch
            {
                EntityActionOperation.Insert => new InsertRequestContext(EntityName, table, document.RootElement, operation),
                EntityActionOperation.Delete => new DeleteRequestContext(EntityName, table, isList: false),
                _ => new UpsertRequestContext(EntityName, table, document.RootElement, operation)
            };

            if (primaryKeyInUrl)
            {
                context.PrimaryKeyValuePairs["id"] = 7;
            }

            return context;
        }

        private static SqlMutationEngine CreateEngine(
            CapturedRoute route,
            DatabaseType defaultType,
            DatabaseType entityType,
            bool readPolicy,
            bool captureDelete = false,
            bool runWork = false)
        {
            RuntimeConfig config = CreateConfig(defaultType, entityType);
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem(), isCliLoader: true);
            loader.RuntimeConfig = config;
            RuntimeConfigProvider provider = new(loader);

            SourceDefinition source = new() { PrimaryKey = new() { "id" } };
            source.Columns["id"] = new ColumnDefinition(typeof(int));
            source.Columns["title"] = new ColumnDefinition(typeof(string));
            DatabaseTable table = new("dbo", "items") { TableDefinition = source };

            Mock<ISqlMetadataProvider> entityMetadata = CreateMetadata(entityType, source, table);
            Mock<ISqlMetadataProvider> defaultMetadata = new();
            defaultMetadata.Setup(x => x.GetDatabaseType()).Returns(defaultType);

            Mock<IMetadataProviderFactory> metadataFactory = new();
            metadataFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>()))
                .Returns((string name) =>
                {
                    route.MetadataProviders.Add(name);
                    return string.Equals(name, EntitySource, StringComparison.OrdinalIgnoreCase)
                        ? entityMetadata.Object
                        : defaultMetadata.Object;
                });

            Mock<IMutationTransactionExecutor> transactionExecutor = new();
            transactionExecutor.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<MutationConnectionSharing>(),
                    It.IsAny<Func<IMutationWorkSession, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (string name, MutationConnectionSharing sharing, Func<IMutationWorkSession, Task> work, CancellationToken _) =>
                {
                    route.DataSourceName = name;
                    route.Sharing = sharing;
                    if (runWork)
                    {
                        await work(new Mock<IMutationWorkSession>().Object);
                    }

                    throw new InvalidOperationException(SentinelMessage);
                });

            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlInsertStructure>()))
                .Callback<SqlInsertStructure>(_ => route.BuiltStructure = "insert")
                .Throws(new InvalidOperationException(SentinelMessage));
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlUpdateStructure>()))
                .Callback<SqlUpdateStructure>(_ => route.BuiltStructure = "update")
                .Throws(new InvalidOperationException(SentinelMessage));
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlUpsertQueryStructure>()))
                .Callback<SqlUpsertQueryStructure>(_ => route.BuiltStructure = "upsert")
                .Throws(new InvalidOperationException(SentinelMessage));

            Mock<IAbstractQueryManagerFactory> queryManager = new();
            queryManager.Setup(x => x.GetMutationTransactionExecutor(It.IsAny<DatabaseType>()))
                .Returns((DatabaseType databaseType) =>
                {
                    route.ExecutorDatabaseType = databaseType;
                    return transactionExecutor.Object;
                });
            queryManager.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(queryBuilder.Object);
            queryManager.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>()))
                .Returns((DatabaseType databaseType) =>
                {
                    route.QueryExecutorDatabaseType = databaseType;
                    route.Isolation = Transaction.Current?.IsolationLevel;
                    if (captureDelete)
                    {
                        throw new InvalidOperationException(SentinelMessage);
                    }

                    return new Mock<IQueryExecutor>().Object;
                });

            Mock<IAuthorizationResolver> authorization = new();
            authorization.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns(readPolicy);
            authorization.Setup(x => x.GetDBPolicyForRequest(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns(readPolicy ? "id gt 0" : null);
            authorization.Setup(x => x.ResolveDBPolicy(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>(), It.IsAny<HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "authenticated";
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

            return new SqlMutationEngine(
                queryManager.Object,
                metadataFactory.Object,
                new Mock<IQueryEngineFactory>().Object,
                authorization.Object,
                new GQLFilterParser(provider, metadataFactory.Object),
                httpContextAccessor.Object,
                provider);
        }

        private static Mock<ISqlMetadataProvider> CreateMetadata(DatabaseType databaseType, SourceDefinition source, DatabaseTable table)
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetDatabaseType()).Returns(databaseType);
            metadata.Setup(x => x.GetSourceDefinition(EntityName)).Returns(source);
            metadata.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject> { [EntityName] = table });
            metadata.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>());
            metadata.Setup(x => x.GetEntityNamesAndDbObjects()).Returns(new Dictionary<string, DatabaseObject> { [EntityName] = table });
            metadata.Setup(x => x.TryGetBackingColumn(EntityName, It.IsAny<string>(), out It.Ref<string?>.IsAny))
                .Returns((string _, string exposed, out string? backing) =>
                {
                    backing = exposed;
                    return source.Columns.ContainsKey(exposed);
                });
            metadata.Setup(x => x.TryGetExposedColumnName(EntityName, It.IsAny<string>(), out It.Ref<string?>.IsAny))
                .Returns((string _, string backing, out string? exposed) =>
                {
                    exposed = backing;
                    return source.Columns.ContainsKey(backing);
                });
            return metadata;
        }

        private static RuntimeConfig CreateConfig(DatabaseType defaultType, DatabaseType entityType)
        {
            Entity entity = new(
                Source: new EntitySource(Object: "items", Type: EntitySourceType.Table, Parameters: null, KeyFields: new[] { "id" }),
                GraphQL: new EntityGraphQLOptions("Item", "Items"),
                Fields: null,
                Rest: new EntityRestOptions(),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);

            return new RuntimeConfig(
                Schema: "",
                DataSource: new DataSource(defaultType, "Server=default", new Dictionary<string, object?>()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(Cors: null, Authentication: null, HostMode.Production)),
                Entities: new(new Dictionary<string, Entity> { [EntityName] = entity }),
                DefaultDataSourceName: DefaultSource,
                DataSourceNameToDataSource: new Dictionary<string, DataSource>
                {
                    [DefaultSource] = new DataSource(defaultType, "Server=default", new Dictionary<string, object?>()),
                    [EntitySource] = new DataSource(entityType, "Server=other", new Dictionary<string, object?>())
                },
                EntityNameToDataSourceName: new Dictionary<string, string> { [EntityName] = EntitySource });
        }

        private const string SentinelMessage = "REST mutation route sentinel";

        private sealed class CapturedRoute
        {
            public List<string> MetadataProviders { get; } = new();

            public string? DataSourceName { get; set; }

            public MutationConnectionSharing? Sharing { get; set; }

            public DatabaseType? ExecutorDatabaseType { get; set; }

            public DatabaseType? QueryExecutorDatabaseType { get; set; }

            public IsolationLevel? Isolation { get; set; }

            public string? BuiltStructure { get; set; }
        }
    }
}
