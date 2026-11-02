// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the Oracle metadata provider's provider-local overrides. Autoentity
    /// generation and linking-object generation are implemented in OracleMetadataProvider so the
    /// shared base keeps its hook-only contract.
    /// </summary>
    [TestClass]
    public class OracleMetadataProviderTests
    {
        [TestMethod]
        public async Task GenerateAutoentitiesIntoEntities_CreatesEntitiesFromOracleQueryResult()
        {
            Autoentity autoentity = new(null, null, null);
            (OracleMetadataProvider provider, RuntimeConfigProvider configProvider) =
                CreateProvider(autoentity);
            ConfigureAutoentityQuery(provider, new JsonArray(new JsonObject
            {
                ["entity_name"] = "DAB Test",
                ["object"] = "BOOKS",
                ["schema"] = "SYSTEM"
            }));

            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "GenerateAutoentitiesIntoEntities",
                BindingFlags.Instance | BindingFlags.NonPublic,
                new[] { typeof(IReadOnlyDictionary<string, Autoentity>) })!;
            await (Task)method.Invoke(provider, new object?[]
            {
                new Dictionary<string, Autoentity> { ["all"] = autoentity }
            })!;

            // "DAB Test" normalizes to "DABTest" and counts as one resolved entity.
            RuntimeConfig config = configProvider.GetConfig();
            Assert.AreEqual(1, config.AutoentityResolutionCounts["all"]);
            Assert.IsTrue(config.Entities.ContainsKey("DABTest"));
            Assert.AreEqual("SYSTEM.BOOKS", config.Entities["DABTest"].Source.Object);
        }

        [TestMethod]
        public async Task GenerateAutoentitiesIntoEntities_QuotesMixedCaseCatalogNames()
        {
            Autoentity autoentity = new(null, null, null);
            (OracleMetadataProvider provider, RuntimeConfigProvider configProvider) =
                CreateProvider(autoentity);
            ConfigureAutoentityQuery(provider, new JsonArray(new JsonObject
            {
                ["entity_name"] = "Customers",
                ["object"] = "Customers",
                ["schema"] = "HR"
            }));

            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "GenerateAutoentitiesIntoEntities",
                BindingFlags.Instance | BindingFlags.NonPublic,
                new[] { typeof(IReadOnlyDictionary<string, Autoentity>) })!;
            await (Task)method.Invoke(provider, new object?[]
            {
                new Dictionary<string, Autoentity> { ["all"] = autoentity }
            })!;

            Assert.AreEqual("HR.\"Customers\"", configProvider.GetConfig().Entities["Customers"].Source.Object);
        }

        [TestMethod]
        public void PopulateMetadataForLinkingObject_MultipleCreateDisabledReturnsWithoutChanges()
        {
            (OracleMetadataProvider provider, _) = CreateProvider(autoentity: null);
            Dictionary<string, DatabaseObject> sourceObjects = new();

            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "PopulateMetadataForLinkingObject",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(provider, new object[]
            {
                "Book", "Author", "dbo.book_authors", sourceObjects
            });

            Assert.AreEqual(0, provider.GetLinkingEntities().Count);
            Assert.AreEqual(0, sourceObjects.Count);
        }

        [TestMethod]
        public void CreateMetadataCommand_UsesConfiguredCommandTimeout()
        {
            (OracleMetadataProvider provider, _) = CreateProvider(autoentity: null, commandTimeout: 7);
            using OracleConnection connection = new();
            using OracleCommand command = provider.CreateMetadataCommand(connection, "SELECT 1 FROM DUAL");

            Assert.AreEqual(7, command.CommandTimeout);
            Assert.IsTrue(command.BindByName);
            Assert.AreEqual("SELECT 1 FROM DUAL", command.CommandText);
        }

        [TestMethod]
        public void CreateMetadataCommand_DefaultsTimeoutWhenOptionIsAbsent()
        {
            (OracleMetadataProvider provider, _) = CreateProvider(autoentity: null);
            using OracleConnection connection = new();
            using OracleCommand command = provider.CreateMetadataCommand(connection, "SELECT 1 FROM DUAL");

            Assert.AreEqual(OracleOptions.DEFAULT_COMMAND_TIMEOUT_SECONDS, command.CommandTimeout);
            Assert.IsTrue(command.BindByName);
        }

        [TestMethod]
        public void CreateMetadataCommand_HonorsZeroTimeout()
        {
            (OracleMetadataProvider provider, _) = CreateProvider(autoentity: null, commandTimeout: 0);
            using OracleConnection connection = new();
            using OracleCommand command = provider.CreateMetadataCommand(connection, "SELECT 1 FROM DUAL");

            Assert.AreEqual(0, command.CommandTimeout);
        }

        [TestMethod]
        public void CreateMetadataCommand_UnknownDataSourceFallsBackToDefaultTimeout()
        {
            (OracleMetadataProvider provider, _) = CreateProvider(autoentity: null, commandTimeout: 7);
            SetBaseField(provider, "_dataSourceName", "missing");
            using OracleConnection connection = new();
            using OracleCommand command = provider.CreateMetadataCommand(connection, "SELECT 1 FROM DUAL");

            Assert.AreEqual(OracleOptions.DEFAULT_COMMAND_TIMEOUT_SECONDS, command.CommandTimeout);
        }

        private static (OracleMetadataProvider Provider, RuntimeConfigProvider ConfigProvider) CreateProvider(
            Autoentity? autoentity,
            int? commandTimeout = null)
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)RuntimeHelpers.GetUninitializedObject(
                typeof(OracleMetadataProvider));

            SetBaseField(provider, "_databaseType", DatabaseType.Oracle);
            SetBaseField(provider, "_linkingEntities", new Dictionary<string, Entity>());
            SetBaseField(provider, "_logger", NullLogger<ISqlMetadataProvider>.Instance);
            SetBaseAutoProperty(provider, "EntityBackingColumnsToExposedNames", new Dictionary<string, Dictionary<string, string>>());
            SetBaseAutoProperty(provider, "EntityExposedNamesToBackingColumnNames", new Dictionary<string, Dictionary<string, string>>());

            Dictionary<string, object?>? options = commandTimeout is null
                ? null
                : new Dictionary<string, object?> { ["command-timeout"] = commandTimeout.Value };
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(
                    DatabaseType.Oracle,
                    "User Id=system;Password=x;Data Source=localhost:1521/x",
                    Options: options),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()))
            {
                Autoentities = autoentity is null
                    ? new RuntimeAutoentities(new Dictionary<string, Autoentity>())
                    : new RuntimeAutoentities(new Dictionary<string, Autoentity> { ["all"] = autoentity })
            };
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            SetBaseField(provider, "_runtimeConfigProvider", configProvider);
            typeof(OracleMetadataProvider)
                .GetField("_runtimeConfigProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, configProvider);
            SetBaseField(provider, "_dataSourceName", configProvider.GetConfig().DefaultDataSourceName);
            return (provider, configProvider);
        }

        private static void ConfigureAutoentityQuery(OracleMetadataProvider provider, JsonArray result)
        {
            Mock<IQueryExecutor> queryExecutor = new();
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<System.Data.Common.DbDataReader, List<string>?, Task<JsonArray>>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(),
                    It.IsAny<List<string>>()))
                .ReturnsAsync(result);
            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.BuildGetAutoentitiesQuery()).Returns("SELECT autoentities FROM dual");
            SetBaseAutoProperty(provider, "QueryExecutor", queryExecutor.Object);
            SetBaseAutoProperty(provider, "SqlQueryBuilder", queryBuilder.Object);
        }

        private static void SetBaseField(OracleMetadataProvider provider, string fieldName, object value) =>
            typeof(OracleMetadataProvider).BaseType!.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(provider, value);

        private static void SetBaseAutoProperty(OracleMetadataProvider provider, string propertyName, object value) =>
            typeof(OracleMetadataProvider).BaseType!.GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, value);
    }
}
