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
    /// Unit tests for Oracle SELECT SQL generated for multiple-create follow-up queries
    /// and table-alias quoting (AS omitted, quoted UPPERCASE).
    /// </summary>
    [TestClass]
    public class OracleQueryBuilderMultipleCreateTests
    {
        private const string ENTITY_NAME = "Book";
        private const string SCHEMA_NAME = "SYSTEM";
        private const string TABLE_NAME = "books";
        private const string READ_POLICY = "(publisher_id != 0)";

        private delegate void TryGetColumnCallback(string entity, string field, out string? column);

        private static readonly Dictionary<string, string> _columnMapping = new()
        {
            { "id", "ID" },
            { "title", "TITLE" },
            { "publisher_id", "PUBLISHER_ID" }
        };

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void MultipleCreateSelectJoinsPkPredicatesWithOrInsideParentheses()
        {
            SqlQueryStructure structure = CreateSelectStructure();
            structure.IsMultipleCreateOperation = true;
            structure.Predicates.Add(PkEquality("id", "@param0", addParenthesis: true));
            structure.Predicates.Add(PkEquality("id", "@param1", addParenthesis: true));

            string query = new OracleQueryBuilder().Build(structure);

            StringAssert.Contains(query, "((\"SYSTEM_BOOKS\".\"ID\" = @param0) OR (\"SYSTEM_BOOKS\".\"ID\" = @param1))", StringComparison.Ordinal);
            Assert.IsFalse(query.Contains("(\"SYSTEM_BOOKS\".\"ID\" = @param0) AND (\"SYSTEM_BOOKS\".\"ID\" = @param1)", StringComparison.Ordinal), query);
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void NonMultipleCreateSelectJoinsPkPredicatesWithAnd()
        {
            SqlQueryStructure structure = CreateSelectStructure();
            structure.IsMultipleCreateOperation = false;
            structure.Predicates.Add(PkEquality("id", "@param0", addParenthesis: false));
            structure.Predicates.Add(PkEquality("id", "@param1", addParenthesis: false));

            string query = new OracleQueryBuilder().Build(structure);

            StringAssert.Contains(query, "\"SYSTEM_BOOKS\".\"ID\" = @param0 AND \"SYSTEM_BOOKS\".\"ID\" = @param1", StringComparison.Ordinal);
            Assert.IsFalse(query.Contains(" OR ", StringComparison.Ordinal), query);
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void MultipleCreateSelectKeepsReadPolicyOutsideOrGroup()
        {
            SqlQueryStructure structure = CreateSelectStructure();
            structure.IsMultipleCreateOperation = true;
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Read] = READ_POLICY;
            structure.Predicates.Add(PkEquality("id", "@param0", addParenthesis: true));
            structure.Predicates.Add(PkEquality("id", "@param1", addParenthesis: true));

            string query = new OracleQueryBuilder().Build(structure);

            StringAssert.Contains(query, READ_POLICY, StringComparison.Ordinal);
            int policyIndex = query.IndexOf(READ_POLICY, StringComparison.Ordinal);
            int orGroupIndex = query.IndexOf("((\"SYSTEM_BOOKS\".\"ID\" = @param0) OR (\"SYSTEM_BOOKS\".\"ID\" = @param1))", StringComparison.Ordinal);
            Assert.IsTrue(orGroupIndex > policyIndex, $"Read policy must sit outside the OR group. Query: {query}");
            StringAssert.Contains(query, $"{READ_POLICY} AND ((\"SYSTEM_BOOKS\".\"ID\" = @param0) OR (\"SYSTEM_BOOKS\".\"ID\" = @param1))", StringComparison.Ordinal);
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void ExistsSubqueryJoinsPredicatesWithAndEvenWhenMultipleCreate()
        {
            SqlQueryStructure structure = CreateSelectStructure();
            structure.IsMultipleCreateOperation = true;
            structure.Predicates.Add(PkEquality("id", "@param0", addParenthesis: true));
            structure.Predicates.Add(PkEquality("id", "@param1", addParenthesis: true));

            string query = new OracleQueryBuilder().Build((BaseSqlQueryStructure)structure);

            StringAssert.Contains(query, "SELECT 1 ", StringComparison.Ordinal);
            StringAssert.Contains(query, "(\"SYSTEM_BOOKS\".\"ID\" = @param0) AND (\"SYSTEM_BOOKS\".\"ID\" = @param1)", StringComparison.Ordinal);
            Assert.IsFalse(query.Contains(" OR ", StringComparison.Ordinal), query);
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void JoinSqlOmitsAsBeforeOnAndQuotesUppercaseAlias()
        {
            SqlQueryStructure structure = CreateSelectStructure();
            DatabaseTable joinTable = new(SCHEMA_NAME, "publishers")
            {
                TableDefinition = new SourceDefinition(),
                SourceType = EntitySourceType.Table
            };

            structure.Joins.Add(new SqlJoinStructure(
                joinTable,
                "table1",
                new List<Predicate>
                {
                    new Predicate(
                        new PredicateOperand(new Column(SCHEMA_NAME, TABLE_NAME, "publisher_id", "SYSTEM_BOOKS")),
                        PredicateOperation.Equal,
                        new PredicateOperand(new Column(SCHEMA_NAME, "publishers", "id", "table1")))
                }));

            string query = new OracleQueryBuilder().Build(structure);

            StringAssert.Contains(query, " INNER JOIN \"SYSTEM\".\"PUBLISHERS\" \"TABLE1\" ON ", StringComparison.Ordinal);
            int joinIndex = query.IndexOf(" INNER JOIN ", StringComparison.Ordinal);
            int onIndex = query.IndexOf(" ON ", joinIndex, StringComparison.Ordinal);
            string joinClause = query.Substring(joinIndex, onIndex - joinIndex);
            Assert.IsFalse(
                joinClause.Contains(" AS \"", StringComparison.Ordinal),
                $"Join output must not emit AS immediately before ON. Join: {joinClause}. Query: {query}");
            StringAssert.Contains(query, " AS \"id\"", StringComparison.Ordinal);
        }

        private static Predicate PkEquality(string columnName, string param, bool addParenthesis)
        {
            return new Predicate(
                new PredicateOperand(new Column(SCHEMA_NAME, TABLE_NAME, columnName.ToUpperInvariant(), "SYSTEM_BOOKS")),
                PredicateOperation.Equal,
                new PredicateOperand(param),
                addParenthesis: addParenthesis);
        }

        private static SqlQueryStructure CreateSelectStructure()
        {
            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new() { "ID" }
            };
            sourceDefinition.Columns.Add("ID", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });
            sourceDefinition.Columns.Add("TITLE", new ColumnDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String,
                IsNullable = true
            });
            sourceDefinition.Columns.Add("PUBLISHER_ID", new ColumnDefinition
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

            FindRequestContext findContext = new(ENTITY_NAME, dbTable, isList: false)
            {
                FieldsToBeReturned = new List<string> { "id" }
            };

            return new SqlQueryStructure(
                findContext,
                metadataProvider.Object,
                authorizationResolver.Object,
                runtimeConfigProvider,
                gQLFilterParser,
                httpContext);
        }
    }
}
