// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Offline tests for Oracle column-level permissions. Oracle stores unquoted identifiers
    /// uppercase while configs author lowercase field, alias, and mapping names. Permission
    /// include/exclude sets must end up in backing (catalog) spelling because the shared
    /// <see cref="AuthorizationResolver"/> subtracts excluded from included in backing space.
    /// </summary>
    [TestClass]
    public class OracleColumnPermissionTests
    {
        private const string ENTITY = "Book";
        private const string ROLE = "reader";

        [TestMethod]
        public void PermissionExcludeMatchesBackingName_WhenExposedNameDiffersInCase()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "salary" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "salary" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();

            EntityActionFields fields = harness.ReadFields();
            CollectionAssert.AreEquivalent(new[] { "SALARY" }, fields.Exclude.ToArray());
        }

        [TestMethod]
        public void ExcludedExposedColumnIsNotInAllowedExposedColumns()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "salary" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "salary" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();
            AuthorizationResolver resolver = harness.CreateResolver();

            IEnumerable<string> allowed = resolver.GetAllowedExposedColumns(ENTITY, ROLE, EntityActionOperation.Read);
            Assert.IsFalse(allowed.Contains("salary", StringComparer.OrdinalIgnoreCase), "Excluded column must not be exposed.");
            Assert.IsTrue(allowed.Contains("ID", StringComparer.OrdinalIgnoreCase), "Non-excluded column must remain exposed.");
        }

        [TestMethod]
        public void SelectOfExcludedColumnIsDenied()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "salary" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "salary" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();
            AuthorizationResolver resolver = harness.CreateResolver();

            Assert.IsFalse(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "salary" }));
            Assert.IsTrue(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "ID" }));
        }

        [TestMethod]
        public void ExplicitIncludeByExposedNameAllowsOnlyThatColumn()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "salary" } },
                permissions: ReadPermission(include: new() { "salary" }, exclude: new()));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();
            AuthorizationResolver resolver = harness.CreateResolver();

            CollectionAssert.AreEquivalent(
                new[] { "salary" },
                resolver.GetAllowedExposedColumns(ENTITY, ROLE, EntityActionOperation.Read).ToArray());
            Assert.IsTrue(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "salary" }));
            Assert.IsFalse(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "ID" }));
        }

        /// <summary>
        /// The exposed name is a real alias (not just a case difference): the exclude is authored
        /// with the alias and must land on the backing column.
        /// </summary>
        [TestMethod]
        [DataRow(true, DisplayName = "Field alias")]
        [DataRow(false, DisplayName = "Legacy mapping")]
        public void ExcludeByAlias_PayMapsToSalary(bool useFieldAlias)
        {
            Entity entity = CreateEntity(
                fields: useFieldAlias ? new() { new() { Name = "SALARY", Alias = "pay" } } : null,
                mappings: useFieldAlias ? null : new() { { "SALARY", "pay" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "pay" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();

            CollectionAssert.AreEquivalent(new[] { "SALARY" }, harness.ReadFields().Exclude.ToArray());

            AuthorizationResolver resolver = harness.CreateResolver();
            Assert.IsFalse(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "pay" }));
            Assert.IsFalse(resolver.GetAllowedExposedColumns(ENTITY, ROLE, EntityActionOperation.Read).Contains("pay"));
        }

        [TestMethod]
        public void ExcludeByBackingName_WhenAliasExists()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "SALARY", Alias = "pay" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "salary" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();

            CollectionAssert.AreEquivalent(new[] { "SALARY" }, harness.ReadFields().Exclude.ToArray());

            AuthorizationResolver resolver = harness.CreateResolver();
            Assert.IsFalse(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "pay" }));
            Assert.IsFalse(resolver.GetAllowedExposedColumns(ENTITY, ROLE, EntityActionOperation.Read).Contains("pay"));
        }

        /// <summary>
        /// Policy tokens stay in exposed space: the EDM model names properties by exposed name and
        /// the OData filter parser is case-sensitive, so a policy on an aliased column must keep
        /// the alias and must not be rewritten to the backing name.
        /// </summary>
        [TestMethod]
        public void PolicyOnAliasedColumnKeepsExposedToken()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "SALARY", Alias = "pay" } },
                permissions: ReadPermission(
                    include: new() { "*" },
                    exclude: new(),
                    databasePolicy: "@item.PAY gt 0"));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();

            Assert.AreEqual("@item.pay gt 0", harness.ReadAction().Policy!.Database);
        }

        [TestMethod]
        public void WildcardIsPreserved()
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "*" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();

            EntityActionFields fields = harness.ReadFields();
            CollectionAssert.AreEquivalent(new[] { "*" }, fields.Exclude.ToArray());
            CollectionAssert.AreEquivalent(new[] { "*" }, fields.Include!.ToArray());
        }

        [TestMethod]
        public void NormalizationIsIdempotent()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "SALARY", Alias = "pay" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "pay" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();
            harness.NormalizeIdentifiers();

            CollectionAssert.AreEquivalent(new[] { "SALARY" }, harness.ReadFields().Exclude.ToArray());
        }

        [TestMethod]
        [DataRow(true, DisplayName = "Exclude")]
        [DataRow(false, DisplayName = "Include")]
        public void UnknownPermissionColumnFailsInitialization(bool inExclude)
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(
                    include: inExclude ? new() { "*" } : new() { "typo_column" },
                    exclude: inExclude ? new() { "typo_column" } : new()));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(harness.NormalizeIdentifiers);

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "typo_column");
            StringAssert.Contains(ex.Message, ENTITY);
        }

        [TestMethod]
        public void UnknownPermissionColumnIsIgnoredForStoredProcedures()
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(include: new() { "not_a_column" }, exclude: new()),
                sourceType: EntitySourceType.StoredProcedure);

            Harness harness = CreateHarness(entity, EntitySourceType.StoredProcedure);
            harness.NormalizeIdentifiers();
        }

        [TestMethod]
        [DataRow(true, DisplayName = "Exclude")]
        [DataRow(false, DisplayName = "Include")]
        public void UnknownPermissionColumnFailsInitializationForViews(bool inExclude)
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(
                    include: inExclude ? new() { "*" } : new() { "typo_column" },
                    exclude: inExclude ? new() { "typo_column" } : new()),
                sourceType: EntitySourceType.View);

            Harness harness = CreateHarness(entity, EntitySourceType.View, "ID");
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(harness.NormalizeIdentifiers);

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "typo_column");
        }

        [TestMethod]
        public void KnownPermissionColumnIsAcceptedForViews()
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "id" }),
                sourceType: EntitySourceType.View);

            Harness harness = CreateHarness(entity, EntitySourceType.View, "ID");
            harness.NormalizeIdentifiers();

            CollectionAssert.AreEquivalent(new[] { "ID" }, harness.ReadFields().Exclude.ToArray());
        }

        [TestMethod]
        public void WildcardMixedWithUnknownNameStillFailsOnTheName()
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "*", "typo_column" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(harness.NormalizeIdentifiers);
            StringAssert.Contains(ex.Message, "typo_column");
        }

        /// <summary>
        /// Autoentity generation used to hand the same permission array to every generated entity,
        /// so normalizing one entity's permissions would have rewritten the others'.
        /// </summary>
        [TestMethod]
        public async Task AutoentityPermissionsAreNotSharedBetweenGeneratedEntities()
        {
            Autoentity autoentity = new(
                Patterns: null,
                Template: null,
                Permissions: ReadPermission(include: new() { "*" }, exclude: new() { "salary" }));
            Harness harness = CreateHarness(new Dictionary<string, (Entity, string[])>(), autoentity: autoentity);

            await harness.GenerateAutoentitiesAsync(autoentity, new JsonArray(
                new JsonObject { ["entity_name"] = "Emp", ["object"] = "EMP", ["schema"] = "SYSTEM" },
                new JsonObject { ["entity_name"] = "Contractor", ["object"] = "CONTRACTOR", ["schema"] = "SYSTEM" }));

            RuntimeConfig config = harness.Config;
            EntityPermission[] first = config.Entities["Emp"].Permissions;
            EntityPermission[] second = config.Entities["Contractor"].Permissions;

            Assert.AreNotSame(autoentity.Permissions, first);
            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first[0], second[0]);
            Assert.AreNotSame(first[0].Actions, second[0].Actions);
            Assert.AreNotSame(autoentity.Permissions[0].Actions, first[0].Actions);
        }

        /// <summary>
        /// Builds the GraphQL object type the way <c>GraphQLSchemaCreator</c> does (per-column roles
        /// from the authorization resolver) and checks an excluded Oracle column never reaches the
        /// schema, so it cannot be selected or filtered. Offline only: there is no live GraphQL run.
        /// </summary>
        [TestMethod]
        public void ExcludedColumnIsAbsentFromGraphQLObjectType()
        {
            Entity entity = CreateEntity(
                fields: new() { new() { Name = "salary" } },
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "salary" }));

            Harness harness = CreateHarness(entity, "SALARY", "ID");
            harness.NormalizeIdentifiers();
            AuthorizationResolver resolver = harness.CreateResolver();

            DatabaseObject databaseObject = harness.GetDatabaseObject(ENTITY);
            Dictionary<string, IEnumerable<string>> rolesAllowedForFields = new();
            foreach (string column in databaseObject.SourceDefinition.Columns.Keys)
            {
                rolesAllowedForFields.Add(column, resolver.GetRolesForField(ENTITY, column, EntityActionOperation.Read));
            }

            ObjectTypeDefinitionNode node = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                entityName: ENTITY,
                databaseObject: databaseObject,
                configEntity: harness.Config.Entities[ENTITY],
                entities: harness.Config.Entities,
                rolesAllowedForEntity: resolver.GetRolesForEntity(ENTITY),
                rolesAllowedForFields: rolesAllowedForFields,
                databaseType: DatabaseType.Oracle);

            Assert.IsFalse(
                node.Fields.Any(f => string.Equals(f.Name.Value, "SALARY", StringComparison.OrdinalIgnoreCase)),
                "An excluded column must not appear on the GraphQL object type.");
            Assert.IsTrue(node.Fields.Any(f => f.Name.Value == "ID"), "A non-excluded column must remain.");
            Assert.IsFalse(resolver.AreColumnsAllowedForOperation(ENTITY, ROLE, EntityActionOperation.Read, new[] { "SALARY" }));
        }

        /// <summary>
        /// A permission template copied to every autoentity table must not abort startup when only
        /// some tables have the excluded column: the unknown name is dropped with a warning for
        /// the generated entity that lacks it, and still applies to the one that has it.
        /// </summary>
        [TestMethod]
        public async Task AutoentityExcludeOfColumnMissingFromOneTableIsDroppedWithWarning()
        {
            Autoentity autoentity = new(
                Patterns: null,
                Template: null,
                Permissions: ReadPermission(include: new() { "*" }, exclude: new() { "ssn" }));
            Harness harness = CreateHarness(new Dictionary<string, (Entity, string[])>(), autoentity: autoentity);

            await harness.GenerateAutoentitiesAsync(autoentity, new JsonArray(
                new JsonObject { ["entity_name"] = "Emp", ["object"] = "EMP", ["schema"] = "SYSTEM" },
                new JsonObject { ["entity_name"] = "Contractor", ["object"] = "CONTRACTOR", ["schema"] = "SYSTEM" }));
            harness.SetDatabaseObjects(new()
            {
                ["Emp"] = new[] { "ID", "SSN" },
                ["Contractor"] = new[] { "ID" }
            });

            harness.NormalizeIdentifiers();

            CollectionAssert.AreEquivalent(new[] { "SSN" }, harness.ReadFields("Emp").Exclude.ToArray());
            Assert.AreEqual(0, harness.ReadFields("Contractor").Exclude.Count);
            harness.VerifyWarningLogged("ssn", Times.Once());
        }

        [TestMethod]
        public async Task AutoentityIncludeOfUnknownColumnIsDroppedNotWidened()
        {
            Autoentity autoentity = new(
                Patterns: null,
                Template: null,
                Permissions: ReadPermission(include: new() { "id", "ssn" }, exclude: new()));
            Harness harness = CreateHarness(new Dictionary<string, (Entity, string[])>(), autoentity: autoentity);

            await harness.GenerateAutoentitiesAsync(autoentity, new JsonArray(
                new JsonObject { ["entity_name"] = "Contractor", ["object"] = "CONTRACTOR", ["schema"] = "SYSTEM" }));
            harness.SetDatabaseObjects(new() { ["Contractor"] = new[] { "ID", "NAME" } });

            harness.NormalizeIdentifiers();

            CollectionAssert.AreEquivalent(new[] { "ID" }, harness.ReadFields("Contractor").Include!.ToArray());
        }

        /// <summary>
        /// The relaxed handling is only for generated entities: a hand-authored entity with the same
        /// typo still fails startup.
        /// </summary>
        [TestMethod]
        public void HandAuthoredEntityStillFailsOnUnknownColumn()
        {
            Entity entity = CreateEntity(
                fields: null,
                permissions: ReadPermission(include: new() { "*" }, exclude: new() { "ssn" }));
            Assert.IsFalse(entity.IsAutoentity);

            Harness harness = CreateHarness(entity, "ID");
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(harness.NormalizeIdentifiers);
            StringAssert.Contains(ex.Message, "ssn");
        }

        private static EntityPermission[] ReadPermission(HashSet<string>? include, HashSet<string> exclude, string? databasePolicy = null)
        {
            return new[]
            {
                new EntityPermission(
                    Role: ROLE,
                    Actions: new[]
                    {
                        new EntityAction(
                            Action: EntityActionOperation.Read,
                            Fields: new EntityActionFields(Exclude: exclude, Include: include),
                            Policy: new EntityActionPolicy(Request: null, Database: databasePolicy))
                    })
            };
        }

        private static Entity CreateEntity(
            List<FieldMetadata>? fields,
            EntityPermission[] permissions,
            Dictionary<string, string>? mappings = null,
            EntitySourceType sourceType = EntitySourceType.Table)
        {
            return new Entity(
                Source: new EntitySource(Object: "SYSTEM.BOOKS", Type: sourceType, Parameters: null, KeyFields: null),
                Fields: fields,
                Rest: new(Array.Empty<SupportedHttpVerb>()),
                GraphQL: new(ENTITY, ENTITY + "s"),
                Permissions: permissions,
                Relationships: null,
                Mappings: mappings);
        }

        private static Harness CreateHarness(Entity entity, params string[] columns)
        {
            return CreateHarness(entity, EntitySourceType.Table, columns);
        }

        private static Harness CreateHarness(Entity entity, EntitySourceType sourceType, params string[] columns)
        {
            return CreateHarness(
                new Dictionary<string, (Entity Entity, string[] Columns)> { [ENTITY] = (entity, columns) },
                sourceType: sourceType);
        }

        private static Harness CreateHarness(
            Dictionary<string, (Entity Entity, string[] Columns)> entities,
            EntitySourceType sourceType = EntitySourceType.Table,
            Autoentity? autoentity = null)
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(OracleMetadataProvider));
            Type baseType = typeof(OracleMetadataProvider).BaseType!;

            SetField(baseType, provider, "_databaseType", DatabaseType.Oracle);
            SetField(baseType, provider, "_linkingEntities", new Dictionary<string, Entity>());
            Mock<ILogger<ISqlMetadataProvider>> logger = new();
            SetField(baseType, provider, "_logger", logger.Object);
            SetAutoProperty(baseType, provider, "EntityBackingColumnsToExposedNames", new Dictionary<string, Dictionary<string, string>>());
            SetAutoProperty(baseType, provider, "EntityExposedNamesToBackingColumnNames", new Dictionary<string, Dictionary<string, string>>());

            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.Oracle, "User Id=system;Password=x;Data Source=localhost:1521/x"),
                Entities: new RuntimeEntities(entities.ToDictionary(e => e.Key, e => e.Value.Entity)))
            {
                Autoentities = autoentity is null
                    ? new RuntimeAutoentities(new Dictionary<string, Autoentity>())
                    : new RuntimeAutoentities(new Dictionary<string, Autoentity> { ["all"] = autoentity })
            };
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetField(baseType, provider, "_runtimeConfigProvider", configProvider);
            SetField(typeof(OracleMetadataProvider), provider, "_runtimeConfigProvider", configProvider);
            SetField(baseType, provider, "_dataSourceName", configProvider.GetConfig().DefaultDataSourceName);

            Dictionary<string, DatabaseObject> databaseObjects = new(StringComparer.InvariantCulture);
            foreach ((string name, (Entity _, string[] columns)) in entities)
            {
                databaseObjects[name] = CreateDatabaseObject(sourceType, columns);
            }

            SetAutoProperty(baseType, provider, "EntityToDatabaseObject", databaseObjects);
            return new Harness(provider, configProvider, logger);
        }

        private static DatabaseObject CreateDatabaseObject(EntitySourceType sourceType, string[] columns)
        {
            SourceDefinition definition = sourceType switch
            {
                EntitySourceType.StoredProcedure => new StoredProcedureDefinition(),
                EntitySourceType.View => new ViewDefinition(),
                _ => new SourceDefinition()
            };
            foreach (string column in columns)
            {
                definition.Columns.Add(column, new ColumnDefinition { SystemType = typeof(string) });
            }

            return sourceType switch
            {
                EntitySourceType.StoredProcedure => new DatabaseStoredProcedure("SYSTEM", "P")
                {
                    SourceType = sourceType,
                    StoredProcedureDefinition = (StoredProcedureDefinition)definition
                },
                EntitySourceType.View => new DatabaseView("SYSTEM", "V")
                {
                    SourceType = sourceType,
                    ViewDefinition = (ViewDefinition)definition
                },
                _ => new DatabaseTable("SYSTEM", "BOOKS") { SourceType = sourceType, TableDefinition = definition }
            };
        }

        private static void SetField(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

        private static void SetAutoProperty(Type type, object target, string name, object value) =>
            type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        private sealed class Harness
        {
            private readonly OracleMetadataProvider _provider;
            private readonly RuntimeConfigProvider _configProvider;

            private readonly Mock<ILogger<ISqlMetadataProvider>> _logger;

            public Harness(OracleMetadataProvider provider, RuntimeConfigProvider configProvider, Mock<ILogger<ISqlMetadataProvider>> logger)
            {
                _provider = provider;
                _configProvider = configProvider;
                _logger = logger;
            }

            /// <summary>Registers catalog columns for entities generated after the harness was created.</summary>
            public void SetDatabaseObjects(Dictionary<string, string[]> columnsByEntity)
            {
                foreach ((string name, string[] columns) in columnsByEntity)
                {
                    _provider.EntityToDatabaseObject[name] = CreateDatabaseObject(EntitySourceType.Table, columns);
                }
            }

            public void VerifyWarningLogged(string containing, Times times)
            {
                _logger.Verify(
                    l => l.Log(
                        LogLevel.Warning,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(containing, StringComparison.Ordinal)),
                        null,
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    times);
            }

            public RuntimeConfig Config => _configProvider.GetConfig();

            /// <summary>
            /// Runs the same step metadata initialization runs after the database objects exist:
            /// builds the exposed/backing maps, then normalizes configured identifiers.
            /// </summary>
            public void NormalizeIdentifiers()
            {
                MethodInfo method = typeof(OracleMetadataProvider).BaseType!.GetMethod(
                    "GenerateExposedToBackingColumnMapsForEntities",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                try
                {
                    method.Invoke(_provider, null);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
            }

            public async Task GenerateAutoentitiesAsync(Autoentity autoentity, JsonArray catalogRows)
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
                    .ReturnsAsync(catalogRows);
                Mock<IQueryBuilder> queryBuilder = new();
                queryBuilder.Setup(x => x.BuildGetAutoentitiesQuery()).Returns("SELECT autoentities FROM dual");
                SetAutoProperty(typeof(OracleMetadataProvider).BaseType!, _provider, "QueryExecutor", queryExecutor.Object);
                SetAutoProperty(typeof(OracleMetadataProvider).BaseType!, _provider, "SqlQueryBuilder", queryBuilder.Object);

                MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                    "GenerateAutoentitiesIntoEntities",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    new[] { typeof(IReadOnlyDictionary<string, Autoentity>) })!;
                await (Task)method.Invoke(_provider, new object?[]
                {
                    new Dictionary<string, Autoentity> { ["all"] = autoentity }
                })!;
            }

            public DatabaseObject GetDatabaseObject(string entityName) => _provider.EntityToDatabaseObject[entityName];

            public EntityAction ReadAction() =>
                Config.Entities[ENTITY].Permissions.Single(p => p.Role == ROLE).Actions
                    .Single(a => a.Action == EntityActionOperation.Read);

            public EntityActionFields ReadFields() => ReadAction().Fields!;

            public EntityActionFields ReadFields(string entityName) =>
                Config.Entities[entityName].Permissions.Single(p => p.Role == ROLE).Actions
                    .Single(a => a.Action == EntityActionOperation.Read).Fields!;

            public AuthorizationResolver CreateResolver()
            {
                Mock<IMetadataProviderFactory> factory = new();
                factory.Setup(f => f.GetMetadataProvider(It.IsAny<string>())).Returns(_provider);
                return new AuthorizationResolver(_configProvider, factory.Object);
            }
        }
    }
}
