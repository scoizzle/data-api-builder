// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the relational read plan used to assemble Oracle nested reads in C#.
    /// The integration suite verifies result equivalence; these tests pin the generated cursor
    /// SQL and the metadata the executor and assembler rely on: no JSON functions, correlation
    /// aliases, flattened to-one null guards and page-key binds.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleQueryBuilderRelationalReadTests
    {
        [TestMethod]
        public void PageCursor_ListChild_ProjectsFlatRowsWithoutJson()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.Equal);

            bool built = new OracleQueryBuilder().TryBuildRelationalPageCursor(parent, out RelationalReadCursor? cursor);

            Assert.IsTrue(built);
            Assert.IsNotNull(cursor);
            Assert.IsNull(cursor.JoinAlias, "The page cursor does not render a relationship.");
            Assert.IsTrue(cursor.IsList);
            Assert.AreEqual(100, cursor.Limit);
            Assert.IsFalse(cursor.Sql.Contains("JSON_"), "Relational cursors must not use Oracle JSON functions.");
            StringAssert.Contains(cursor.Sql, "\"TABLE0\".\"ID\" AS \"id\"");
            StringAssert.Contains(cursor.Sql, "FROM \"DBO\".\"PARENTS\" \"TABLE0\"");
            StringAssert.Contains(cursor.Sql, "ORDER BY \"TABLE0\".\"ID\" ASC OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY");

            // The child's parent-side correlation column is exposed so child rows can be attached;
            // the alias is reused from the already-selected column instead of duplicated.
            Assert.AreEqual("id", cursor.CorrelationAliasesByJoinAlias["table1_subq"].Single());
            Assert.AreEqual("id", cursor.AliasByExpression["\"TABLE0\".\"ID\""]);
            Assert.AreEqual(2, cursor.Fields.Count);
            Assert.AreEqual("id", cursor.Fields[0].JsonName);
            Assert.AreEqual("id", cursor.Fields[0].Alias);
            Assert.IsFalse(cursor.Fields[0].IsObject);

            // The relationship is a placeholder filled from its own cursor's result.
            RelationalReadField relation = cursor.Fields[1];
            Assert.AreEqual("children", relation.JsonName);
            Assert.AreEqual("table1_subq", relation.RelationJoinAlias);
            Assert.IsTrue(relation.RelationIsList);
        }

        [TestMethod]
        public void ChildCursor_ListChild_RanksPerParentAndScopesToPageKeys()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.Equal);
            OracleQueryBuilder builder = new();
            Assert.IsTrue(builder.TryBuildRelationalPageCursor(parent, out RelationalReadCursor? pageCursor));

            bool built = builder.TryBuildRelationalChildCursors(
                pageCursor!,
                new Dictionary<string, IReadOnlyList<object?[]>> { ["table1_subq"] = new object?[][] { new object?[] { 1 }, new object?[] { 2 } } },
                out IReadOnlyList<RelationalReadCursor>? children);

            Assert.IsTrue(built);
            RelationalReadCursor cursor = children!.Single();
            Assert.AreEqual("table1_subq", cursor.JoinAlias);
            Assert.IsTrue(cursor.IsList);
            Assert.AreEqual(100, cursor.Limit);
            Assert.IsFalse(cursor.Sql.Contains("JSON_"));

            // Ranking happens per parent key inside the cursor; the outer query applies the
            // per-parent top-N and orders rows so the assembler can stream buckets.
            StringAssert.Contains(cursor.Sql, "ROW_NUMBER() OVER (PARTITION BY \"TABLE1\".\"parent_id\" ORDER BY \"TABLE1\".\"id\" ASC) AS \"rn\"");
            StringAssert.Contains(cursor.Sql, "\"TABLE1\".\"rn\" <= 100");
            StringAssert.Contains(cursor.Sql, "ORDER BY \"TABLE1\".\"k0\", \"TABLE1\".\"rn\"");

            // The page restriction uses one bind per parent key value instead of re-running the
            // page computation, so the rank never scans children of non-page parents.
            Assert.AreEqual(2, cursor.Binds.Count);
            Assert.AreEqual(1, cursor.Binds[0].Value);
            Assert.AreEqual(2, cursor.Binds[1].Value);
            Assert.AreEqual("ID", cursor.Binds[0].SourceColumn);
            foreach (RelationalReadBind bind in cursor.Binds)
            {
                StringAssert.Contains(cursor.Sql, bind.Name);
            }

            Assert.AreEqual(1, cursor.Keys.Count);
            Assert.AreEqual("k0", cursor.Keys[0].Alias);
            Assert.AreEqual("id", cursor.Keys[0].ParentAlias);
        }

        [TestMethod]
        public void PageCursor_ToOneChild_FlattensWithNullGuard()
        {
            SqlQueryStructure parent = CreateParentWithToOneChild();

            bool built = new OracleQueryBuilder().TryBuildRelationalPageCursor(parent, out RelationalReadCursor? cursor);

            Assert.IsTrue(built);
            Assert.IsNotNull(cursor);
            Assert.IsFalse(cursor.Sql.Contains("JSON_"));
            StringAssert.Contains(cursor.Sql, "LEFT OUTER JOIN \"DBO\".\"CHILDREN\" \"TABLE1\" ON (\"TABLE1\".\"ID\" = \"TABLE0\".\"ID\")");

            // The flattened child becomes a nested field whose guard aliases are its primary key:
            // an outer-join miss leaves them NULL, so the assembler emits JSON null.
            Assert.AreEqual(2, cursor.Fields.Count);
            RelationalReadField objectField = cursor.Fields.Single(field => field.IsObject);
            Assert.AreEqual("child", objectField.JsonName);
            Assert.AreEqual(1, objectField.NullGuardAliases.Count);
            Assert.AreEqual("ID", objectField.NullGuardAliases[0]);
            RelationalReadField childId = objectField.Children.Single();
            Assert.AreEqual("ID", childId.JsonName);
            Assert.AreEqual("ID", childId.Alias);
            Assert.IsFalse(cursor.CorrelationAliasesByJoinAlias.ContainsKey("table1_subq"),
                "A flattened to-one row does not need its own cursor.");
        }

        [TestMethod]
        public void ChildCursor_NestedList_ResolvesGrandchildCorrelationAlias()
        {
            (SqlQueryStructure parent, SqlQueryStructure child) = CreateParentListChildAndGrandchild();
            OracleQueryBuilder builder = new();
            Assert.IsTrue(builder.TryBuildRelationalPageCursor(parent, out RelationalReadCursor? pageCursor));
            Assert.IsTrue(builder.TryBuildRelationalChildCursors(
                pageCursor!,
                new Dictionary<string, IReadOnlyList<object?[]>> { ["table1_subq"] = new object?[][] { new object?[] { 1 } } },
                out IReadOnlyList<RelationalReadCursor>? children));

            RelationalReadCursor childCursor = children!.Single();
            // Child rows carry the aliases the grandchild cursor binds to.
            Assert.AreEqual("id", childCursor.CorrelationAliasesByJoinAlias["table2_subq"].Single());

            Assert.IsTrue(builder.TryBuildRelationalChildCursors(
                childCursor,
                new Dictionary<string, IReadOnlyList<object?[]>> { ["table2_subq"] = new object?[][] { new object?[] { 42 } } },
                out IReadOnlyList<RelationalReadCursor>? grandChildren));

            RelationalReadCursor grandChildCursor = grandChildren!.Single();
            Assert.AreEqual(1, grandChildCursor.Keys.Count);
            Assert.AreEqual("k0", grandChildCursor.Keys[0].Alias);
            Assert.AreEqual("id", grandChildCursor.Keys[0].ParentAlias);
            StringAssert.Contains(grandChildCursor.Sql, "\"TABLE2\".\"child_id\" AS \"k0\"");
            // The bind's source column is the parent-side key it was read from (the child's id).
            Assert.AreEqual("id", grandChildCursor.Binds[0].SourceColumn);
            Assert.AreEqual(42, grandChildCursor.Binds[0].Value);
        }

        [TestMethod]
        public void PageCursor_NonEqualityCorrelation_FallsBackToJsonPlan()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.GreaterThan);

            bool built = new OracleQueryBuilder().TryBuildRelationalPageCursor(parent, out RelationalReadCursor? cursor);

            Assert.IsFalse(built, "A child that is not correlated by column equality cannot be keyed relationally.");
            Assert.IsNull(cursor);
        }

        private static (SqlQueryStructure Parent, SqlQueryStructure Child) CreateParentWithListChild(
            PredicateOperation correlationOperation)
        {
            SourceDefinition childSource = new();
            childSource.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            childSource.Columns.Add("parent_id", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable childTable = new("dbo", "children") { TableDefinition = childSource };
            Mock<ISqlMetadataProvider> childMetadata = new();
            childMetadata.Setup(x => x.GetSourceDefinition("Child")).Returns(childSource);

            SqlQueryStructure child = CreateStructure(isList: true);
            SetField(child, "EntityName", "Child");
            SetField(child, "MetadataProvider", childMetadata.Object);
            SetField(child, "DatabaseObject", childTable);
            SetField(child, "SourceAlias", "table1");
            SetBaseProperty(child, "Columns", new List<LabelledColumn>
            {
                new("dbo", "children", "id", "id", "table1")
            });
            SetField(child, "Predicates", new List<Predicate>
            {
                new(
                    new PredicateOperand(new Column(string.Empty, string.Empty, "parent_id", "table1")),
                    correlationOperation,
                    new PredicateOperand(new Column(string.Empty, string.Empty, "ID", "table0")))
            });
            SetField(child, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(child, "Joins", new List<SqlJoinStructure>());
            SetField(child, "FilterPredicates", string.Empty);
            SetField(child, "OrderByColumns", new List<OrderByColumn>
            {
                new("dbo", "children", "id", "table1")
            });
            SetField(child, "PaginationMetadata", new PaginationMetadata(child));
            SetField(child, "GroupByMetadata", new GroupByMetadata());
            SetField(child, "Counter", new IncrementingInteger());
            SetLimit(child, 100);

            SourceDefinition parentSource = new();
            parentSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable parentTable = new("dbo", "parents") { TableDefinition = parentSource };
            Mock<ISqlMetadataProvider> parentMetadata = new();
            parentMetadata.Setup(x => x.GetSourceDefinition("Parent")).Returns(parentSource);

            SqlQueryStructure parent = CreateStructure(isList: true);
            SetField(parent, "EntityName", "Parent");
            SetField(parent, "MetadataProvider", parentMetadata.Object);
            SetField(parent, "DatabaseObject", parentTable);
            SetField(parent, "SourceAlias", "table0");
            SetBaseProperty(parent, "Columns", new List<LabelledColumn>
            {
                new("dbo", "parents", "ID", "id", "table0"),
                new("dbo", "children", SqlQueryStructure.DATA_IDENT, "children", "table1_subq")
            });
            SetJoinQueries(parent, ("table1_subq", child));
            SetField(parent, "Predicates", new List<Predicate>());
            SetField(parent, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(parent, "Joins", new List<SqlJoinStructure>());
            SetField(parent, "FilterPredicates", string.Empty);
            SetField(parent, "OrderByColumns", new List<OrderByColumn>
            {
                new("dbo", "parents", "ID", "table0")
            });
            SetField(parent, "PaginationMetadata", new PaginationMetadata(parent));
            SetField(parent, "GroupByMetadata", new GroupByMetadata());
            SetField(parent, "Counter", new IncrementingInteger());
            SetLimit(parent, 100);

            return (parent, child);
        }

        private static SqlQueryStructure CreateParentWithToOneChild()
        {
            SourceDefinition childSource = new();
            childSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            childSource.PrimaryKey.Add("ID");
            DatabaseTable childTable = new("dbo", "children") { TableDefinition = childSource };
            Mock<ISqlMetadataProvider> childMetadata = new();
            childMetadata.Setup(x => x.GetSourceDefinition("Child")).Returns(childSource);

            SqlQueryStructure child = CreateStructure(isList: false);
            SetField(child, "EntityName", "Child");
            SetField(child, "MetadataProvider", childMetadata.Object);
            SetField(child, "DatabaseObject", childTable);
            SetField(child, "SourceAlias", "table1");
            SetBaseProperty(child, "Columns", new List<LabelledColumn>
            {
                new("dbo", "children", "ID", "ID", "table1")
            });
            SetField(child, "Predicates", new List<Predicate>
            {
                new(
                    new PredicateOperand(new Column(string.Empty, string.Empty, "ID", "table1")),
                    PredicateOperation.Equal,
                    new PredicateOperand(new Column(string.Empty, string.Empty, "ID", "table0")))
            });
            SetField(child, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(child, "Joins", new List<SqlJoinStructure>());
            SetField(child, "FilterPredicates", string.Empty);
            SetField(child, "OrderByColumns", new List<OrderByColumn>
            {
                new("dbo", "children", "ID", "table1")
            });
            SetField(child, "PaginationMetadata", new PaginationMetadata(child));
            SetField(child, "GroupByMetadata", new GroupByMetadata());
            SetField(child, "Counter", new IncrementingInteger());
            SetLimit(child, 1);

            SourceDefinition parentSource = new();
            parentSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable parentTable = new("dbo", "parents") { TableDefinition = parentSource };
            Mock<ISqlMetadataProvider> parentMetadata = new();
            parentMetadata.Setup(x => x.GetSourceDefinition("Parent")).Returns(parentSource);

            SqlQueryStructure parent = CreateStructure(isList: true);
            SetField(parent, "EntityName", "Parent");
            SetField(parent, "MetadataProvider", parentMetadata.Object);
            SetField(parent, "DatabaseObject", parentTable);
            SetField(parent, "SourceAlias", "table0");
            SetBaseProperty(parent, "Columns", new List<LabelledColumn>
            {
                new("dbo", "parents", "ID", "id", "table0"),
                new("dbo", "children", SqlQueryStructure.DATA_IDENT, "child", "table1_subq")
            });
            SetJoinQueries(parent, ("table1_subq", child));
            SetField(parent, "Predicates", new List<Predicate>());
            SetField(parent, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(parent, "Joins", new List<SqlJoinStructure>());
            SetField(parent, "FilterPredicates", string.Empty);
            SetField(parent, "OrderByColumns", new List<OrderByColumn>
            {
                new("dbo", "parents", "ID", "table0")
            });
            SetField(parent, "PaginationMetadata", new PaginationMetadata(parent));
            SetField(parent, "GroupByMetadata", new GroupByMetadata());
            SetField(parent, "Counter", new IncrementingInteger());
            SetLimit(parent, 100);

            return parent;
        }

        private static (SqlQueryStructure Parent, SqlQueryStructure Child) CreateParentListChildAndGrandchild()
        {
            SourceDefinition grandChildSource = new();
            grandChildSource.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            grandChildSource.Columns.Add("child_id", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable grandChildTable = new("dbo", "grandchildren") { TableDefinition = grandChildSource };
            Mock<ISqlMetadataProvider> grandChildMetadata = new();
            grandChildMetadata.Setup(x => x.GetSourceDefinition("GrandChild")).Returns(grandChildSource);

            SqlQueryStructure grandChild = CreateStructure(isList: true);
            SetField(grandChild, "EntityName", "GrandChild");
            SetField(grandChild, "MetadataProvider", grandChildMetadata.Object);
            SetField(grandChild, "DatabaseObject", grandChildTable);
            SetField(grandChild, "SourceAlias", "table2");
            SetBaseProperty(grandChild, "Columns", new List<LabelledColumn>
            {
                new("dbo", "grandchildren", "id", "id", "table2")
            });
            SetField(grandChild, "Predicates", new List<Predicate>
            {
                new(
                    new PredicateOperand(new Column(string.Empty, string.Empty, "child_id", "table2")),
                    PredicateOperation.Equal,
                    new PredicateOperand(new Column(string.Empty, string.Empty, "id", "table1")))
            });
            SetField(grandChild, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(grandChild, "Joins", new List<SqlJoinStructure>());
            SetField(grandChild, "FilterPredicates", string.Empty);
            SetField(grandChild, "OrderByColumns", new List<OrderByColumn>
            {
                new("dbo", "grandchildren", "id", "table2")
            });
            SetField(grandChild, "PaginationMetadata", new PaginationMetadata(grandChild));
            SetField(grandChild, "GroupByMetadata", new GroupByMetadata());
            SetField(grandChild, "Counter", new IncrementingInteger());
            SetLimit(grandChild, 100);

            (SqlQueryStructure parent, SqlQueryStructure child) = CreateParentWithListChild(PredicateOperation.Equal);
            SetJoinQueries(child, ("table2_subq", grandChild));
            SetBaseProperty(child, "Columns", new List<LabelledColumn>
            {
                new("dbo", "children", "id", "id", "table1"),
                new("dbo", "grandchildren", SqlQueryStructure.DATA_IDENT, "grandchildren", "table2_subq")
            });
            return (parent, child);
        }

        private static SqlQueryStructure CreateStructure(bool isList)
        {
            SqlQueryStructure structure = (SqlQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(SqlQueryStructure));
            structure.IsListQuery = isList;
            SetJoinQueries(structure);
            return structure;
        }

        private static void SetJoinQueries(SqlQueryStructure structure, params (string Alias, SqlQueryStructure Query)[] joins)
        {
            Dictionary<string, SqlQueryStructure> joinQueries = new();
            foreach ((string alias, SqlQueryStructure query) in joins)
            {
                joinQueries.Add(alias, query);
            }

            typeof(SqlQueryStructure).GetField("<JoinQueries>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, joinQueries);
        }

        private static void SetLimit(SqlQueryStructure structure, uint? limit)
        {
            typeof(SqlQueryStructure).GetField("_limit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, limit);
        }

        private static void SetField<T>(SqlQueryStructure structure, string name, T value)
        {
            for (System.Type? type = typeof(SqlQueryStructure); type is not null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(structure, value);
                    return;
                }
            }

            Assert.Fail($"Could not find backing field for {name}.");
        }

        private static void SetBaseProperty<T>(SqlQueryStructure structure, string name, T value)
        {
            typeof(BaseQueryStructure).GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, value);
        }
    }
}
