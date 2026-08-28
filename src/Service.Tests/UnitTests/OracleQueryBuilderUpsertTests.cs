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
using Oracle.ManagedDataAccess.Client;

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
        /// Verifies that the Oracle upsert builder produces a single PL/SQL block: a BEGIN-wrapped
        /// UPDATE (scoped by the primary key and the update database policy) whose output binds feed a
        /// REF CURSOR result set carrying the ___upsert_op___ indicator.
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
            Assert.IsTrue(query.Contains("BEGIN UPDATE", StringComparison.Ordinal), $"Expected a PL/SQL block. Query: {query}");
            Assert.IsTrue(query.EndsWith("END;", StringComparison.Ordinal), $"Expected the block to close with END;. Query: {query}");
            Assert.IsTrue(query.Contains("UPDATE", StringComparison.Ordinal), $"Expected an UPDATE statement. Query: {query}");

            // Isolate the UPDATE statement so the assertion targets the UPDATE, not the INSERT.
            int updateIndex = query.IndexOf("UPDATE", StringComparison.Ordinal);
            int ifIndex = query.IndexOf("IF SQL%ROWCOUNT", StringComparison.Ordinal);
            int endIndex = ifIndex > updateIndex ? ifIndex : query.Length;
            string updateBranch = query.Substring(updateIndex, endIndex - updateIndex);

            Assert.IsTrue(
                updateBranch.Contains("WHERE", StringComparison.Ordinal),
                $"The Oracle upsert UPDATE MUST include a WHERE clause to scope the update. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains(updateDbPolicy, StringComparison.Ordinal),
                $"The Oracle upsert UPDATE WHERE clause MUST include the update database policy. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains("RETURNING", StringComparison.OrdinalIgnoreCase)
                && updateBranch.Contains("INTO", StringComparison.OrdinalIgnoreCase),
                $"The Oracle upsert UPDATE MUST use RETURNING ... INTO to fetch the updated row. Query: {query}");

            Assert.IsTrue(
                query.Contains($"OPEN :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME} FOR SELECT", StringComparison.Ordinal),
                $"The upsert MUST surface the result through the REF CURSOR. Query: {query}");
            Assert.IsTrue(
                query.Contains("DAB_ORACLE_OUTPUT_TYPES:", StringComparison.Ordinal),
                $"The upsert MUST emit output-bind type hints for ODP.NET. Query: {query}");
            Assert.IsTrue(
                query.Contains(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME, StringComparison.Ordinal),
                $"The upsert MUST carry the {OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME} indicator. Query: {query}");
        }

        /// <summary>
        /// Verifies that the Oracle upsert builder includes an INSERT ... VALUES branch (the only
        /// form Oracle supports with RETURNING - ORA-03049 forbids RETURNING with INSERT ... SELECT)
        /// and that a create database policy guards the VALUES via a DUAL filter.
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
            Assert.IsTrue(query.Contains("INSERT INTO", StringComparison.Ordinal), $"Expected an INSERT statement. Query: {query}");
            Assert.IsTrue(
                query.Contains("VALUES", StringComparison.Ordinal),
                $"The Oracle upsert INSERT MUST use the VALUES form (RETURNING is not valid with INSERT ... SELECT). Query: {query}");
            Assert.IsTrue(
                query.Contains(createDbPolicy, StringComparison.Ordinal),
                $"The Oracle upsert INSERT MUST include the create database policy. Query: {query}");
            Assert.IsTrue(
                query.Contains("SELECT COUNT(*) FROM (SELECT", StringComparison.Ordinal),
                $"The create-policy INSERT MUST be gated by an IF (SELECT COUNT(*) FROM (SELECT <named values> FROM DUAL) WHERE ...) pre-check so column-referencing policies resolve. Query: {query}");
            Assert.IsTrue(
                query.Contains($"SELECT COUNT(*) FROM \"SYSTEM\".\"BOOKS\" WHERE \"id\" =", StringComparison.Ordinal),
                $"The upsert MUST distinguish a policy-blocked existing row from a missing row via a PK existence check. Query: {query}");
            Assert.IsTrue(
                query.Contains("WHERE 1 = 0", StringComparison.Ordinal),
                $"The policy-blocked INSERT path MUST open an empty cursor (WHERE 1 = 0) to surface 403. Query: {query}");
            Assert.IsTrue(
                query.Contains(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME, StringComparison.Ordinal),
                $"The Oracle upsert MUST carry the {OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME} indicator. Query: {query}");
            Assert.IsTrue(
                query.Contains("'inserted'", StringComparison.Ordinal),
                $"The INSERT branch MUST emit the 'inserted' indicator. Query: {query}");
        }

        /// <summary>
        /// Verifies that a standalone INSERT with a create database policy gates the INSERT on
        /// the policy via IF (SELECT COUNT(*) FROM (SELECT <named values> FROM DUAL) WHERE policy), and opens an empty cursor
        /// when the policy blocks — surfacing 403 rather than ORA-01400 (400).
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleInsertWithCreatePolicyGatesOnPolicyAndOpensEmptyCursorWhenBlocked()
        {
            // Arrange
            const string createDbPolicy = "(publisher_id != 0)";

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
                { "title", "New Book" }
            };

            SqlInsertStructure structure = new(
                entityName: ENTITY_NAME,
                sqlMetadataProvider: metadataProvider.Object,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: gQLFilterParser,
                mutationParams: mutationParams,
                httpContext: httpContext);

            // Set the create policy directly on the structure (bypasses the resolver/AST pipeline
            // which requires a full GraphQL context). This mirrors how the upsert test sets its
            // DbPolicyPredicatesForOperations.
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Create] = createDbPolicy;

            OracleQueryBuilder builder = new();

            // Act
            string query = builder.Build(structure);

            // Assert
            Assert.IsTrue(query.Contains("BEGIN", StringComparison.Ordinal), $"Expected a PL/SQL block. Query: {query}");
            Assert.IsTrue(query.Contains("INSERT INTO", StringComparison.Ordinal), $"Expected an INSERT statement. Query: {query}");
            Assert.IsTrue(
                query.Contains("SELECT COUNT(*) FROM (SELECT", StringComparison.Ordinal),
                $"The INSERT MUST be gated by an IF (SELECT COUNT(*) FROM (SELECT <named values> FROM DUAL) WHERE ...) pre-check so column-referencing policies resolve. Query: {query}");
            Assert.IsTrue(
                query.Contains("WHERE 1 = 0", StringComparison.Ordinal),
                $"The policy-blocked path MUST open an empty cursor (WHERE 1 = 0) to surface 403. Query: {query}");
            Assert.IsTrue(
                query.Contains(createDbPolicy, StringComparison.Ordinal),
                $"The policy MUST appear in the IF-guard condition. Query: {query}");
        }

        /// <summary>
        /// Verifies that fallback-to-update (e.g. autogenerated primary key) produces no INSERT
        /// branch and always emits the 'updated' indicator.
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
                query.Contains("'updated'", StringComparison.Ordinal),
                $"Fallback-to-update MUST emit the 'updated' indicator. Query: {query}");
            Assert.IsTrue(
                query.Contains("WHERE 1 = 0", StringComparison.Ordinal),
                $"Fallback-to-update MUST open an EMPTY cursor when no row matched. Query: {query}");
        }

        /// <summary>
        /// Verifies that the plain UPDATE builder (GraphQL update mutation) opens an EMPTY REF
        /// CURSOR when the UPDATE matches no row. If the cursor were opened unconditionally, a
        /// no-match update (record absent, or the update database policy blocks it) would surface
        /// a fabricated row of all-NULL columns instead of "item not found", deviating from the
        /// behavior of the other database engines.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleUpdateOpensEmptyCursorWhenNoRowMatched()
        {
            // Arrange
            OracleQueryBuilder builder = new();
            SqlUpdateStructure structure = CreateUpdateStructure();

            // Act
            string query = builder.Build(structure);

            // Assert
            Assert.IsTrue(query.Contains("BEGIN UPDATE", StringComparison.Ordinal), $"Expected a PL/SQL block. Query: {query}");
            Assert.IsTrue(query.EndsWith("END;", StringComparison.Ordinal), $"Expected the block to close with END;. Query: {query}");
            Assert.IsTrue(
                query.Contains("IF SQL%ROWCOUNT > 0", StringComparison.Ordinal),
                $"The UPDATE MUST gate the cursor on SQL%ROWCOUNT so a no-match update returns no row. Query: {query}");
            Assert.IsTrue(
                query.Contains("WHERE 1 = 0", StringComparison.Ordinal),
                $"The no-match branch MUST open an EMPTY cursor (WHERE 1 = 0). Query: {query}");
            Assert.IsTrue(
                query.Contains($"OPEN :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME} FOR SELECT", StringComparison.Ordinal),
                $"The UPDATE MUST surface the result through the REF CURSOR. Query: {query}");
        }

        /// <summary>
        /// Builds a minimal <see cref="SqlUpdateStructure"/> for the test entity using the
        /// non-GraphQL constructor so no live database or GraphQL context is required.
        /// </summary>
        private static SqlUpdateStructure CreateUpdateStructure()
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
                { "title", "The Hobbit Returns to The Shire" }
            };

            return new SqlUpdateStructure(
                entityName: ENTITY_NAME,
                sqlMetadataProvider: metadataProvider.Object,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: gQLFilterParser,
                mutationParams: mutationParams,
                httpContext: httpContext,
                isIncrementalUpdate: true);
        }

        /// <summary>
        /// Verifies that PL/SQL RETURNING binds use the type hints emitted by the builder and
        /// that the REF CURSOR result bind is registered with the correct Oracle type.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleOutputBindRegistrarUsesTypedOutputParameters()
        {
            OracleCommand command = new();
            const string sql = "/* DAB_ORACLE_OUTPUT_TYPES:id=Decimal,title=Varchar2 */ " +
                "BEGIN UPDATE \"SYSTEM\".\"BOOKS\" SET \"TITLE\" = :param0 " +
                "RETURNING \"ID\", \"TITLE\" INTO :id, :title; " +
                "OPEN :dab_result FOR SELECT :id AS \"id\", :title AS \"title\" FROM DUAL; END;";

            OracleBindRegistrar.RegisterPlSqlOutputBinds(command, sql);

            Assert.AreEqual(OracleDbType.RefCursor, command.Parameters["dab_result"].OracleDbType);
            Assert.AreEqual(ParameterDirection.Output, command.Parameters["dab_result"].Direction);
            Assert.AreEqual(OracleDbType.Decimal, command.Parameters["id"].OracleDbType);
            Assert.AreEqual(OracleDbType.Varchar2, command.Parameters["title"].OracleDbType);
            Assert.AreEqual(4000, command.Parameters["title"].Size);
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