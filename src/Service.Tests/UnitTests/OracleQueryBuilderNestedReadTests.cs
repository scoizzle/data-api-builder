// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the keyed-CTE strategy used by OracleQueryBuilder for nested reads.
    /// The integration suite verifies result equivalence; these tests pin the generated SQL
    /// shape so a silent fallback back to correlated LATERALs is caught.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleQueryBuilderNestedReadTests
    {
        [TestMethod]
        public void Build_NestedListRelationship_UsesKeyedAggregateCte()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.Equal);

            string query = new OracleQueryBuilder().Build(parent);

            // The request page is materialized first (with the columns the final query needs) and
            // bounds the relationship aggregate.
            // MATERIALIZE keeps the page from being inlined into its multiple readers; inlining
            // a query name whose definition references another one raises ORA-32036 on some
            // Oracle versions.
            StringAssert.StartsWith(query, "WITH \"dab_page_cte\" AS ( SELECT /*+ MATERIALIZE */ \"TABLE0\".\"ID\" AS \"ID\", \"TABLE0\".\"ID\" AS \"c0\"");
            StringAssert.Contains(query, "FROM \"DBO\".\"PARENTS\" \"TABLE0\"");
            StringAssert.Contains(query, "ORDER BY \"TABLE0\".\"ID\" ASC OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY");
            // The child aggregate only ranks rows reachable from the page.
            StringAssert.Contains(query, "INNER JOIN (SELECT DISTINCT \"c0\" FROM \"dab_page_cte\") \"dab_page\" ON (\"dab_page\".\"c0\" = \"TABLE1\".\"parent_id\")");
            StringAssert.Contains(query, "ROW_NUMBER() OVER (PARTITION BY \"TABLE1\".\"parent_id\" ORDER BY \"TABLE1\".\"id\" ASC)");
            StringAssert.Contains(query, "GROUP BY \"k0\"");
            // The final query reads the page instead of re-applying the root predicates.
            StringAssert.Contains(query, "FROM \"dab_page_cte\" \"TABLE0\" LEFT OUTER JOIN \"table1_subq_cte\" ON \"table1_subq_cte\".\"k0\" = \"TABLE0\".\"ID\"");
            StringAssert.Contains(query, "WHERE 1 = 1");
            // JSON is built after the per-parent top-N filter, not for every ranked row.
            // The child's rows are ranked first; descendant joins and JSON build only for the
            // surviving top-N rows.
            StringAssert.Contains(query, "SELECT \"TABLE1\".\"id\" AS \"id\", \"TABLE1\".\"parent_id\" AS \"k0\", ROW_NUMBER() OVER (PARTITION BY \"TABLE1\".\"parent_id\" ORDER BY \"TABLE1\".\"id\" ASC) AS \"rn\"");
            StringAssert.Contains(query, "JSON_ARRAYAGG(json_doc ORDER BY \"rn\" RETURNING CLOB)");
            StringAssert.Contains(query, "JSON_OBJECT('id' VALUE \"TABLE1\".\"id\" NULL ON NULL RETURNING VARCHAR2(4000)) AS json_doc");
            StringAssert.Contains(query, "WHERE \"TABLE1\".\"rn\" <= 100 ) ranked_rows GROUP BY \"k0\"");
            // Lists must still deserialize as [] when a parent has no children.
            StringAssert.Contains(query, "COALESCE(\"table1_subq_cte\".\"data\", TO_CLOB(JSON_ARRAY()))");
            Assert.IsFalse(query.Contains("LATERAL"), "Nested relationship should not fall back to LATERAL.");
        }

        [TestMethod]
        public void Build_NestedRelationshipWithNonEqualityCorrelation_FallsBackToLateral()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.GreaterThan);

            string query = new OracleQueryBuilder().Build(parent);

            Assert.IsFalse(query.StartsWith("WITH "), "Non-equality correlation must not use the keyed CTE form.");
            StringAssert.Contains(query, "LEFT OUTER JOIN LATERAL");
        }

        [TestMethod]
        public void Build_ToOneRelationshipWithPrimaryKeyCorrelation_IsFlattened()
        {
            (SqlQueryStructure parent, _) = CreateParentWithToOneChild();

            string query = new OracleQueryBuilder().Build(parent);

            // No CTE, no page CTE (the only child is flattened), no ranking and no lateral.
            Assert.IsFalse(query.StartsWith("WITH "), "A single flattenable to-one child needs no CTE.");
            Assert.IsFalse(query.Contains("_cte"), "To-one relationship should not become a CTE.");
            Assert.IsFalse(query.Contains("ROW_NUMBER"), "Unique to-one relationship must not be ranked.");
            Assert.IsFalse(query.Contains("LATERAL"), "To-one relationship should be flattened, not lateral.");
            StringAssert.Contains(query, "LEFT OUTER JOIN \"DBO\".\"CHILDREN\" \"TABLE1\" ON (\"TABLE1\".\"ID\" = \"TABLE0\".\"ID\")");
            // The child's JSON object is built inline over the flattened join, guarded so a
            // missing child stays JSON null instead of becoming an all-null object.
            StringAssert.Contains(query, "CASE WHEN \"TABLE1\".\"ID\" IS NOT NULL THEN JSON_OBJECT('ID' VALUE \"TABLE1\".\"ID\" NULL ON NULL RETURNING VARCHAR2(4000)) ELSE NULL END AS \"child\"");
        }

        [TestMethod]
        public void Build_AuthorsChildWithLinkIdAndReadPolicy_QualifiesPolicyColumn()
        {
            SqlQueryStructure parent = CreateAuthorsChildWithLinkId();

            string query = new OracleQueryBuilder().Build(parent);

            // @item.id eq 1 is visited after SourceAlias is table1, so Oracle quotes the alias.
            StringAssert.Contains(query, "(\"TABLE1\".\"ID\" = @param0)", query);
            StringAssert.Contains(query, "INNER JOIN \"SYSTEM\".\"BOOK_AUTHOR_LINK\" \"TABLE3\"", query);
            Assert.IsFalse(query.Contains("(\"ID\""), query);
            Assert.IsFalse(Regex.IsMatch(query, "(?<!\\.)\"ID\" ="), query);
        }

        [TestMethod]
        public void Build_ToOneWithReadPolicy_IsNotFlattened()
        {
            (SqlQueryStructure parent, SqlQueryStructure child) = CreateParentWithToOneChild();
            child.DbPolicyPredicatesForOperations[EntityActionOperation.Read] = VisitItemIdEqualsOne(child);

            string query = new OracleQueryBuilder().Build(parent);

            // A qualified policy is still a policy string. Flattening stays off.
            Assert.IsTrue(query.StartsWith("WITH "), query);
            Assert.IsFalse(query.Contains("LEFT OUTER JOIN \"DBO\".\"CHILDREN\""), query);
            StringAssert.Contains(query, "(\"TABLE1\".\"ID\" = @param0)", query);
            Assert.IsFalse(query.Contains("(\"ID\""), query);
        }

        [TestMethod]
        public void Build_NestedListRelationship_AliasesEveryRankedKey()
        {
            (SqlQueryStructure parent, SqlQueryStructure child) = CreateParentWithListChild(PredicateOperation.Equal);
            child.Predicates.Add(new Predicate(
                new PredicateOperand(new Column(string.Empty, string.Empty, "tenant_id", "table1")),
                PredicateOperation.Equal,
                new PredicateOperand(new Column(string.Empty, string.Empty, "TENANT", "table0"))));

            string query = new OracleQueryBuilder().Build(parent);

            StringAssert.Contains(query, "\"TABLE1\".\"k0\" AS \"k0\", \"TABLE1\".\"k1\" AS \"k1\"", query);
            Assert.IsFalse(query.Contains("\"TABLE1\".\"k0\", \"TABLE1\".\"k1\" AS"), query);
            StringAssert.Contains(query, "GROUP BY \"k0\", \"k1\"", query);
            // The one-key shape stays a single aliased key. Covered by Build_NestedListRelationship_UsesKeyedAggregateCte.
            StringAssert.Contains(query, "\"TABLE1\".\"k0\" AS \"k0\"");
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
            SetJoinQuery(parent, "table1_subq", child);
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

        private static (SqlQueryStructure Parent, SqlQueryStructure Child) CreateParentWithToOneChild()
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
            SetJoinQuery(parent, "table1_subq", child);
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

        private static SqlQueryStructure CreateStructure(bool isList)
        {
            SqlQueryStructure structure = (SqlQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(SqlQueryStructure));
            structure.IsListQuery = isList;
            SetJoinQuery(structure, alias: null, query: null);
            return structure;
        }

        /// <summary>
        /// Sets the get-only JoinQueries dictionary. When alias is null an empty dictionary is set.
        /// </summary>
        private static void SetJoinQuery(SqlQueryStructure structure, string? alias, SqlQueryStructure? query)
        {
            Dictionary<string, SqlQueryStructure> joinQueries = new();
            if (alias is not null && query is not null)
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

        /// <summary>
        /// Authors nested under books through book_author_link. The link table has its own ID,
        /// so an unqualified policy column "ID" is ambiguous (ORA-00918).
        /// </summary>
        private static SqlQueryStructure CreateAuthorsChildWithLinkId()
        {
            (SqlQueryStructure parent, SqlQueryStructure child) = CreateParentWithListChild(PredicateOperation.Equal);

            SourceDefinition authorSource = new();
            authorSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            authorSource.Columns.Add("NAME", new ColumnDefinition { SystemType = typeof(string) });
            authorSource.PrimaryKey.Add("ID");
            DatabaseTable authorTable = new("SYSTEM", "AUTHORS") { TableDefinition = authorSource };

            SourceDefinition linkSource = new();
            linkSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            linkSource.Columns.Add("BOOK_ID", new ColumnDefinition { SystemType = typeof(int) });
            linkSource.Columns.Add("AUTHOR_ID", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable linkTable = new("SYSTEM", "BOOK_AUTHOR_LINK") { TableDefinition = linkSource };

            Mock.Get(child.MetadataProvider).Setup(provider => provider.GetSourceDefinition("Author")).Returns(authorSource);
            SetField(child, "EntityName", "Author");
            SetField(child, "DatabaseObject", authorTable);
            SetBaseProperty(child, "Columns", new List<LabelledColumn>
            {
                new("SYSTEM", "AUTHORS", "ID", "id", "table1"),
                new("SYSTEM", "AUTHORS", "NAME", "name", "table1"),
                new("SYSTEM", "BOOK_AUTHOR_LINK", "ID", "linkId", "table3")
            });
            child.Joins.Add(new SqlJoinStructure(
                linkTable,
                "table3",
                new List<Predicate>
                {
                    new(
                        new PredicateOperand(new Column("SYSTEM", "BOOK_AUTHOR_LINK", "AUTHOR_ID", "table3")),
                        PredicateOperation.Equal,
                        new PredicateOperand(new Column("SYSTEM", "AUTHORS", "ID", "table1")))
                }));
            child.Predicates.Clear();
            child.Predicates.Add(new Predicate(
                new PredicateOperand(new Column("SYSTEM", "BOOK_AUTHOR_LINK", "BOOK_ID", "table3")),
                PredicateOperation.Equal,
                new PredicateOperand(new Column("SYSTEM", "BOOKS", "ID", "table0"))));
            child.DbPolicyPredicatesForOperations[EntityActionOperation.Read] = VisitItemIdEqualsOne(child);

            SourceDefinition parentSource = new();
            parentSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            Mock.Get(parent.MetadataProvider).Setup(provider => provider.GetSourceDefinition("Parent")).Returns(parentSource);
            SetField(parent, "DatabaseObject", new DatabaseTable("SYSTEM", "BOOKS") { TableDefinition = parentSource });
            return parent;
        }

        private delegate void TryGetBackingColumnCallback(string entity, string field, out string? column);

        /// <summary>
        /// Runs ODataASTVisitor on the property access left by stripping @item. from @item.id eq 1.
        /// </summary>
        private static string VisitItemIdEqualsOne(SqlQueryStructure structure)
        {
            Mock<ISqlMetadataProvider> metadata = Mock.Get(structure.MetadataProvider);
            metadata.Setup(provider => provider.GetQueryBuilder()).Returns(new OracleQueryBuilder());
            metadata.Setup(provider => provider.GetDatabaseType()).Returns(DatabaseType.Oracle);

            string? backingColumn;
            metadata.Setup(provider => provider.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out backingColumn))
                .Callback(new TryGetBackingColumnCallback((string _, string _, out string? column) => column = "ID"))
                .Returns(true);

            SetField(structure, "Parameters", new Dictionary<string, DbConnectionParam>());

            EdmEntityType entityType = new("Test", "Entity");
            IEdmStructuralProperty property = entityType.AddStructuralProperty("id", EdmPrimitiveTypeKind.Int32);
            EdmPrimitiveTypeReference intType = new(
                EdmCoreModel.Instance.GetPrimitiveType(EdmPrimitiveTypeKind.Int32), isNullable: false);
            SingleValuePropertyAccessNode propertyNode = new(new ConstantNode(0, "0", intType), property);
            ConstantNode one = new(1, "1", intType);
            BinaryOperatorNode comparison = new(BinaryOperatorKind.Equal, propertyNode, one);
            return new ODataASTVisitor(structure, metadata.Object, EntityActionOperation.Read).Visit(comparison);
        }
    }
}
