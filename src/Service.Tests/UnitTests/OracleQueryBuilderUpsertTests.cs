// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Regression tests for the Oracle upsert query builder.
    /// These validate the generated upsert follows the count-first + UPDATE/INSERT
    /// pattern (modeled after the PostgreSQL builder) so the mutation engine can
    /// distinguish an insert from an update and surface database policy failures.
    /// A missing WHERE clause on the UPDATE branch would cause a single PUT upsert
    /// to overwrite every row in the target table (CWE-862).
    /// </summary>
    [TestClass]
    public class OracleQueryBuilderUpsertTests
    {
        private const string ENTITY_NAME = "Book";
        private const string SCHEMA_NAME = "SYSTEM";
        private const string TABLE_NAME = "books";

        private delegate void TryGetColumnCallback(string entity, string field, out string? column);

        /// <summary>
        /// Maps exposed field names to backing column names (identity mapping for the test entity).
        /// </summary>
        private static readonly Dictionary<string, string> _columnMapping = new()
        {
            { "id", "id" },
            { "title", "title" },
            { "publisher_id", "publisher_id" }
        };

        /// <summary>
        /// Verifies that the Oracle upsert builder produces the count-first structure with
        /// an UPDATE branch scoped by the primary key and the update database policy.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleUpsertUpdateBranchContainsWhereClauseScopedByPrimaryKeyAndPolicy()
        {
            // Arrange
            const string updateDbPolicy = "(publisher_id != 0)";

            SqlUpsertQueryStructure structure = CreateUpsertStructure();

            // Simulate an update database policy being defined for the operation.
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = updateDbPolicy;

            OracleQueryBuilder builder = new();

            // Act
            string query = builder.Build(structure);

            // Assert
            Assert.IsTrue(query.Contains("UPDATE", StringComparison.Ordinal), $"Expected an UPDATE statement. Query: {query}");
            Assert.IsTrue(
                query.Contains(OracleQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, StringComparison.Ordinal),
                $"Expected a COUNT query with {OracleQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK}. Query: {query}");
            Assert.IsTrue(
                query.Contains(OracleQueryBuilder.IS_FALLBACK_TO_UPDATE, StringComparison.Ordinal),
                $"Expected the fallback-to-update flag in the COUNT query. Query: {query}");

            // Isolate the UPDATE statement so the assertion targets the UPDATE, not the INSERT.
            int updateIndex = query.IndexOf("UPDATE", StringComparison.Ordinal);
            int insertIndex = query.IndexOf("INSERT", StringComparison.Ordinal);
            int endIndex = insertIndex > updateIndex ? insertIndex : query.Length;
            string updateBranch = query.Substring(updateIndex, endIndex - updateIndex);

            Assert.IsTrue(
                updateBranch.Contains("WHERE", StringComparison.Ordinal),
                $"The Oracle upsert UPDATE MUST include a WHERE clause to scope the update. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains("\"id\"", StringComparison.Ordinal),
                $"The Oracle upsert UPDATE WHERE clause MUST scope by the primary key column. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains(updateDbPolicy, StringComparison.Ordinal),
                $"The Oracle upsert UPDATE WHERE clause MUST include the update database policy. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains("RETURNING", StringComparison.OrdinalIgnoreCase),
                $"The Oracle upsert UPDATE MUST use RETURNING to fetch the updated row. Query: {query}");
        }

        /// <summary>
        /// Verifies that the Oracle upsert builder includes an INSERT branch (with a NOT EXISTS
        /// guard and the create database policy) when the upsert is not fallback-to-update.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleUpsertContainsInsertBranchWithCreatePolicyGuard()
        {
            // Arrange
            const string createDbPolicy = "(publisher_id != 0)";

            SqlUpsertQueryStructure structure = CreateUpsertStructure();

            // Simulate a create database policy being defined for the operation.
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Create] = createDbPolicy;

            OracleQueryBuilder builder = new();

            // Act
            string query = builder.Build(structure);

            // Assert
            Assert.IsFalse(structure.IsFallbackToUpdate, "The test entity should allow INSERT (non-fallback).");
            Assert.IsTrue(query.Contains("INSERT", StringComparison.Ordinal), $"Expected an INSERT statement. Query: {query}");
            Assert.IsTrue(
                query.Contains("NOT EXISTS", StringComparison.Ordinal),
                $"The Oracle upsert INSERT MUST be guarded by NOT EXISTS to avoid overwriting. Query: {query}");
            Assert.IsTrue(
                query.Contains(createDbPolicy, StringComparison.Ordinal),
                $"The Oracle upsert INSERT MUST include the create database policy. Query: {query}");
            Assert.IsTrue(
                query.Contains(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME, StringComparison.Ordinal),
                $"The Oracle upsert MUST carry the {OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME} indicator. Query: {query}");
        }

        /// <summary>
        /// Verifies that fallback-to-update (e.g. autogenerated primary key) produces no INSERT
        /// branch and no COUNT flag mismatch.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleUpsertFallbackToUpdateHasNoInsertBranch()
        {
            // Arrange
            OracleQueryBuilder builder = new();
            (SqlUpsertQueryStructure structure, Mock<ISqlMetadataProvider> metadataProvider) = CreateFallbackUpsertStructure();

            _ = metadataProvider; // retained for symmetry with CreateFallbackUpsertStructure

            // Act
            string query = builder.Build(structure);

            // Assert
            Assert.IsTrue(structure.IsFallbackToUpdate, "Expected the structure to be fallback-to-update.");
            Assert.IsFalse(query.Contains("INSERT", StringComparison.Ordinal), $"Fallback-to-update MUST not contain an INSERT. Query: {query}");
            Assert.IsTrue(
                query.Contains("1 AS " + OracleQueryBuilder.IS_FALLBACK_TO_UPDATE, StringComparison.Ordinal),
                $"Fallback-to-update MUST flag {OracleQueryBuilder.IS_FALLBACK_TO_UPDATE}=1. Query: {query}");
        }

        /// <summary>
        /// Builds a minimal <see cref="SqlUpsertQueryStructure"/> for the test entity using a mocked
        /// metadata provider so no live database is required.
        /// </summary>
        private static SqlUpsertQueryStructure CreateUpsertStructure()
        {
            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new() { "id" }
            };
            sourceDefinition.Columns.Add("id", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });
            sourceDefinition.Columns.Add("title", new ColumnDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String,
                IsNullable = true
            });
            sourceDefinition.Columns.Add("publisher_id", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });

            DatabaseTable dbTable = new(SCHEMA_NAME, TABLE_NAME)
            {
                TableDefinition = sourceDefinition,
                SourceType = EntitySourceType.Table
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { ENTITY_NAME, dbTable } });
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(sourceDefinition);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.Oracle);

            string? outColumn;
            metadataProvider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out outColumn))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => _columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string? column) => _columnMapping.ContainsKey(field));

            string? outExposed;
            metadataProvider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out outExposed))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => _columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string? column) => _columnMapping.ContainsKey(field));

            // The update/create policies are injected directly onto the structure after construction.
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver
                .Setup(x => x.ResolveDBPolicy(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<EntityActionOperation>(),
                    It.IsAny<HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            GQLFilterParser gQLFilterParser = new(runtimeConfigProvider, metadataProviderFactory.Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "authenticated";

            Dictionary<string, object?> mutationParams = new()
            {
                { "id", 1 },
                { "title", "The Hobbit Returns to The Shire" },
                { "publisher_id", 1234 }
            };

            return new SqlUpsertQueryStructure(
                entityName: ENTITY_NAME,
                sqlMetadataProvider: metadataProvider.Object,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: gQLFilterParser,
                mutationParams: mutationParams,
                incrementalUpdate: false,
                httpContext: httpContext);
        }

        /// <summary>
        /// Builds a fallback-to-update upsert structure (autogenerated primary key) so the
        /// resulting upsert is an UPDATE-only flow with no INSERT branch.
        /// </summary>
        private static (SqlUpsertQueryStructure, Mock<ISqlMetadataProvider>) CreateFallbackUpsertStructure()
        {
            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new() { "id" }
            };
            sourceDefinition.Columns.Add("id", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32,
                IsAutoGenerated = true
            });
            sourceDefinition.Columns.Add("title", new ColumnDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String,
                IsNullable = true
            });
            sourceDefinition.Columns.Add("publisher_id", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });

            DatabaseTable dbTable = new(SCHEMA_NAME, TABLE_NAME)
            {
                TableDefinition = sourceDefinition,
                SourceType = EntitySourceType.Table
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { ENTITY_NAME, dbTable } });
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(sourceDefinition);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.Oracle);

            string? outColumn;
            metadataProvider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out outColumn))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => _columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string? column) => _columnMapping.ContainsKey(field));

            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver
                .Setup(x => x.ResolveDBPolicy(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<EntityActionOperation>(),
                    It.IsAny<HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            GQLFilterParser gQLFilterParser = new(runtimeConfigProvider, metadataProviderFactory.Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "authenticated";

            Dictionary<string, object?> mutationParams = new()
            {
                { "id", 1 },
                { "title", "The Hobbit Returns to The Shire" },
                { "publisher_id", 1234 }
            };

            SqlUpsertQueryStructure structure = new(
                entityName: ENTITY_NAME,
                sqlMetadataProvider: metadataProvider.Object,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: gQLFilterParser,
                mutationParams: mutationParams,
                incrementalUpdate: false,
                httpContext: httpContext);

            return (structure, metadataProvider);
        }
    }
}