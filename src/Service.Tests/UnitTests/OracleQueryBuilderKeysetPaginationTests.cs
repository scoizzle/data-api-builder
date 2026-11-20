// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Pins the keyset pagination predicate Oracle emits. Oracle sorts NULLs last on ASC and
    /// stores '' as NULL, so a bare comparison never reaches the NULL/blank tail of the sort.
    /// These tests fail if the NULL group is dropped or emitted twice.
    /// </summary>
    [TestClass]
    public class OracleQueryBuilderKeysetPaginationTests
    {
        private sealed class TestableOracleQueryBuilder : OracleQueryBuilder
        {
            public string BuildPagination(KeysetPaginationPredicate? predicate)
            {
                return Build(predicate);
            }
        }

        private static PaginationColumn Column(
            string name,
            object? value,
            string? paramName = null,
            OrderBy direction = OrderBy.ASC)
        {
            return new PaginationColumn(
                tableSchema: "DBO",
                tableName: "TYPE_TABLE",
                columnName: name,
                value: value,
                tableAlias: "T1",
                direction: direction,
                paramName: paramName);
        }

        [TestMethod]
        public void AscendingSingleColumnPrimaryKey_KeepsBaseShape()
        {
            // A primary-key-terminated cursor is unique, so the last column cannot be NULL and
            // needs no extra group.
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("id", 5, "@param0")
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual("\"T1\".\"id\" > @param0", sql);
        }

        [TestMethod]
        public void AscendingGroupedTerminatingColumn_AddsNullTail()
        {
            // A grouped cursor carries no primary-key tie-break, so its last column can repeat
            // and can be NULL: the NULL tail must be reachable there too.
            KeysetPaginationPredicate predicate = new(
                new List<PaginationColumn>
                {
                    Column("int_types", 5, "@param0")
                },
                terminatingColumnCanBeNull: true);

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" > @param0) OR (\"T1\".\"int_types\" IS NULL))",
                sql);
        }

        [TestMethod]
        public void DescendingGroupedTerminatingColumn_WithNullCursorReachesNonNulls()
        {
            KeysetPaginationPredicate predicate = new(
                new List<PaginationColumn>
                {
                    Column("int_types", null, "@param0", OrderBy.DESC)
                },
                terminatingColumnCanBeNull: true);

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" < @param0) OR (\"T1\".\"int_types\" IS NOT NULL))",
                sql);
        }

        [TestMethod]
        public void DescendingWithNullCursorValue_ReachesNonNullSegment()
        {
            // Oracle sorts NULLs first on DESC, so a NULL cursor means the page ended inside the
            // NULL block; the whole non-NULL segment sorts after it and must stay reachable.
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("int_types", null, "@param0", OrderBy.DESC),
                Column("id", 10, "@param1", OrderBy.DESC)
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" < @param0) OR (\"T1\".\"int_types\" IS NOT NULL) " +
                "OR (\"T1\".\"int_types\" is NULL AND \"T1\".\"id\" < @param1))",
                sql);
        }

        [TestMethod]
        public void AscendingComposite_AddsNullTailForEachStep()
        {
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("int_types", 5, "@param0"),
                Column("id", 10, "@param1")
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" > @param0) OR (\"T1\".\"int_types\" IS NULL) " +
                "OR (\"T1\".\"int_types\" = @param0 AND \"T1\".\"id\" > @param1))",
                sql);
        }

        [TestMethod]
        public void AscendingThreeColumns_AddsNullTailForEachNullableStep()
        {
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("int_types", 5, "@param0"),
                Column("book_id", 3, "@param1"),
                Column("id", 10, "@param2")
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" > @param0) OR (\"T1\".\"int_types\" IS NULL) " +
                "OR (\"T1\".\"int_types\" = @param0 AND \"T1\".\"book_id\" > @param1) " +
                "OR (\"T1\".\"int_types\" = @param0 AND \"T1\".\"book_id\" IS NULL) " +
                "OR (\"T1\".\"int_types\" = @param0 AND \"T1\".\"book_id\" = @param1 AND \"T1\".\"id\" > @param2))",
                sql);
        }

        [TestMethod]
        public void EmptyStringCursor_OnOracle_BecomesNull()
        {
            // Oracle stores '' as NULL, so an empty-string cursor is a NULL cursor.
            PaginationColumn normalized = SqlQueryStructure.NormalizePaginationColumn(
                Column("string_types", string.Empty, "@param0"),
                new OracleSqlLiteralRewriter());

            Assert.IsNull(normalized.Value);
            Assert.AreEqual("string_types", normalized.ColumnName);
        }

        [TestMethod]
        public void EmptyStringCursor_OnNonOracle_KeepsValue()
        {
            PaginationColumn normalized = SqlQueryStructure.NormalizePaginationColumn(
                Column("string_types", string.Empty, "@param0"),
                SqlLiteralRewriter.Shared);

            Assert.AreEqual(string.Empty, normalized.Value);
        }

        [TestMethod]
        public void AscendingEmptyStringCursor_DoesNotRepeatNullRows()
        {
            // Normalizing '' to NULL must reuse the NULL-cursor shape, not add the extra
            // IS NULL tail that a non-null cursor needs.
            PaginationColumn normalized = SqlQueryStructure.NormalizePaginationColumn(
                Column("string_types", string.Empty, "@param0"),
                new OracleSqlLiteralRewriter());
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                normalized,
                Column("id", 10, "@param1")
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"string_types\" > @param0) " +
                "OR (\"T1\".\"string_types\" is NULL AND \"T1\".\"id\" > @param1))",
                sql);
        }

        [TestMethod]
        public void AscendingWithNullCursorValue_DoesNotRepeatNullRows()
        {
            // The cursor itself is NULL: the rows after it are in the same NULL block, which the
            // tie-break equality already matches. Adding the NULL group here would re-emit rows.
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("int_types", null, "@param0"),
                Column("id", 10, "@param1")
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" > @param0) " +
                "OR (\"T1\".\"int_types\" is NULL AND \"T1\".\"id\" > @param1))",
                sql);
        }

        [TestMethod]
        public void DescendingSingleColumn_KeepsBaseShape()
        {
            // NULLs sort first on DESC, so they are already on the first page.
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("int_types", 5, "@param0", OrderBy.DESC)
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual("\"T1\".\"int_types\" < @param0", sql);
        }

        [TestMethod]
        public void DescendingComposite_KeepsBaseShape()
        {
            KeysetPaginationPredicate predicate = new(new List<PaginationColumn>
            {
                Column("int_types", 5, "@param0", OrderBy.DESC),
                Column("id", 10, "@param1", OrderBy.DESC)
            });

            string sql = new TestableOracleQueryBuilder().BuildPagination(predicate);

            Assert.AreEqual(
                "((\"T1\".\"int_types\" < @param0) " +
                "OR (\"T1\".\"int_types\" = @param0 AND \"T1\".\"id\" < @param1))",
                sql);
        }

        [TestMethod]
        public void AddPaginationPredicate_NormalizesEmptyStringCursorOnOracle()
        {
            SqlQueryStructure structure = CreateStructure(new OracleSqlLiteralRewriter());

            structure.AddPaginationPredicate(new List<PaginationColumn>
            {
                Column("STRING_TYPES", string.Empty)
            });

            Assert.IsNull(structure.PaginationMetadata.PaginationPredicate!.Columns[0].Value);
        }

        [TestMethod]
        public void AddPaginationPredicate_KeepsEmptyStringCursorOnNonOracle()
        {
            SqlQueryStructure structure = CreateStructure(SqlLiteralRewriter.Shared);

            structure.AddPaginationPredicate(new List<PaginationColumn>
            {
                Column("STRING_TYPES", string.Empty)
            });

            Assert.AreEqual(string.Empty, structure.PaginationMetadata.PaginationPredicate!.Columns[0].Value);
        }

        private static SqlQueryStructure CreateStructure(SqlLiteralRewriter literalRewriter)
        {
            const string entityName = "SupportedType";
            SourceDefinition sourceDefinition = new() { PrimaryKey = new() { "ID" } };
            sourceDefinition.Columns.Add("ID", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });
            sourceDefinition.Columns.Add("STRING_TYPES", new ColumnDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String,
                IsNullable = true
            });

            DatabaseTable table = new("DBO", "TYPE_TABLE")
            {
                TableDefinition = sourceDefinition,
                SourceType = EntitySourceType.Table
            };

            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { entityName, table } });
            metadata.Setup(x => x.GetSourceDefinition(entityName)).Returns(sourceDefinition);
            metadata.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.Oracle);
            metadata.Setup(x => x.GetQueryBuilder()).Returns(new OracleQueryBuilder());

            string? backingColumn = "ID";
            metadata.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out backingColumn))
                .Returns(true);
            metadata.As<ISqlLiteralSource>().Setup(x => x.LiteralRewriter).Returns(literalRewriter);

            RuntimeConfigProvider runtimeConfigProvider =
                TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            GQLFilterParser filterParser = new(
                runtimeConfigProvider,
                new Mock<IMetadataProviderFactory>().Object);
            FindRequestContext context = new(entityName, table, isList: true)
            {
                FieldsToBeReturned = new List<string> { "ID" }
            };

            return new SqlQueryStructure(
                context,
                metadata.Object,
                new Mock<IAuthorizationResolver>().Object,
                runtimeConfigProvider,
                filterParser,
                httpContext: null!);
        }
    }
}
