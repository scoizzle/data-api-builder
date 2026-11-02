// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
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
    [TestClass]
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
            StringAssert.Contains(query, "JSON_ARRAYAGG(json_doc FORMAT JSON ORDER BY \"rn\" RETURNING CLOB)");
            StringAssert.Contains(query, "'children' VALUE \"children\" FORMAT JSON");
            Assert.IsFalse(query.Contains("JSON_OBJECT(*)", StringComparison.Ordinal), query);
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
            StringAssert.Contains(query, "'child' VALUE \"child\" FORMAT JSON");
        }

        [TestMethod]
        public void Build_AuthorsChildWithLinkIdAndReadPolicy_QualifiesPolicyColumn()
        {
            SqlQueryStructure parent = CreateAuthorsChildWithLinkId();

            string query = new OracleQueryBuilder().Build(parent);

            // @item.id eq 1 is stored as a bare "ID". OracleQueryBuilder qualifies it with the child alias.
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

        /// <summary>
        /// The EXISTS body is escaped when it is built, and the enclosing predicate string is
        /// escaped again. Multi-character parameter names must still receive one ESCAPE clause.
        /// </summary>
        [TestMethod]
        public void Build_NestedExistsLike_EscapesExactlyOnce()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.Equal);
            SqlQueryStructure exists = CreateLikeExistsStructure();
            parent.Predicates.Add(new Predicate(
                new PredicateOperand(new Predicate(
                    new PredicateOperand("1"),
                    PredicateOperation.Equal,
                    new PredicateOperand("1"))),
                PredicateOperation.OR,
                new PredicateOperand(new Predicate(
                    null,
                    PredicateOperation.EXISTS,
                    new PredicateOperand(exists)))));

            string query = new OracleQueryBuilder().Build(parent);

            StringAssert.Contains(query, "LIKE @param10 ESCAPE '\\'", query);
            StringAssert.Contains(query, "NOT LIKE @param2 ESCAPE '\\'", query);
            StringAssert.Contains(query, "LIKE @p ESCAPE '\\'", query);
            Assert.IsFalse(query.Contains("LIKE @param1 ESCAPE", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("ESCAPE '\\' ESCAPE", StringComparison.Ordinal), query);
            Assert.AreEqual(
                Regex.Matches(query, @"LIKE\s+@[\w\d]+(?![\w\d])", RegexOptions.IgnoreCase).Count,
                Regex.Matches(query, @"ESCAPE '\\'").Count,
                query);
        }

        [TestMethod]
        public void Build_DepthOneNestedList_MatchesGoldenSql()
        {
            (SqlQueryStructure parent, _) = CreateParentWithListChild(PredicateOperation.Equal);

            string query = new OracleQueryBuilder().Build(parent);

            Assert.AreEqual(ReadGolden("oracle-nested-depth1.sql"), query, query);
            Assert.IsFalse(query.Contains("_rows_cte", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("dab_restrict_", StringComparison.Ordinal), query);
        }

        [TestMethod]
        public void Build_ThreeLevelNestedRead_RestrictsEveryLevelToPageKeys()
        {
            SqlQueryStructure root = CreateChainedList(levels: 3, rootOrdered: true);

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(query, "INNER JOIN (SELECT DISTINCT \"c0\" FROM \"dab_page_cte\") \"dab_page\" ON (\"dab_page\".\"c0\" = \"TABLE1\".\"parent_id\")", query);
            StringAssert.Contains(query, "\"table1_rows_cte\" AS ( SELECT /*+ MATERIALIZE */", query);
            StringAssert.Contains(query, "INNER JOIN (SELECT DISTINCT \"id\" FROM \"table1_rows_cte\" WHERE \"rn\" <= 50) \"dab_restrict_0\" ON (\"dab_restrict_0\".\"id\" = \"TABLE2\".\"parent_id\")", query);
            Assert.IsFalse(query.Contains("table2_rows_cte", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("FROM \"DBO\".\"LEVEL2\" \"TABLE2\" WHERE 1 = 1 )", StringComparison.Ordinal), query);
            Assert.IsTrue(query.IndexOf("\"table1_rows_cte\" AS (", StringComparison.Ordinal) < query.IndexOf("\"table2_subq_cte\" AS (", StringComparison.Ordinal), query);
        }

        [TestMethod]
        public void Build_FourLevelNestedRead_RestrictsEveryRankedLevel()
        {
            SqlQueryStructure root = CreateChainedList(levels: 4, rootOrdered: true);

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(query, "FROM \"table1_rows_cte\" WHERE \"rn\" <= 50", query);
            StringAssert.Contains(query, "FROM \"table2_rows_cte\" WHERE \"rn\" <= 25", query);
            StringAssert.Contains(query, "\"dab_restrict_0\"", query);
            StringAssert.Contains(query, "\"dab_restrict_1\"", query);
            Assert.IsFalse(query.Contains("table3_rows_cte", StringComparison.Ordinal), query);
            Assert.IsTrue(
                query.IndexOf("\"table1_rows_cte\" AS (", StringComparison.Ordinal)
                < query.IndexOf("\"table2_rows_cte\" AS (", StringComparison.Ordinal),
                query);
        }

        [TestMethod]
        public void Build_ThreeLevelNestedRead_CompositeCorrelationKeys()
        {
            SqlQueryStructure root = CreateChainedList(levels: 3, rootOrdered: true);
            SqlQueryStructure level2 = root.JoinQueries["table1_subq"].JoinQueries["table2_subq"];
            level2.Predicates.Add(new Predicate(
                new PredicateOperand(new Column(string.Empty, string.Empty, "tenant_id", "table2")),
                PredicateOperation.Equal,
                new PredicateOperand(new Column(string.Empty, string.Empty, "tenant", "table1"))));

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(
                query,
                "INNER JOIN (SELECT DISTINCT \"id\", \"tenant\" FROM \"table1_rows_cte\" WHERE \"rn\" <= 50) \"dab_restrict_0\" ON (\"dab_restrict_0\".\"id\" = \"TABLE2\".\"parent_id\" AND \"dab_restrict_0\".\"tenant\" = \"TABLE2\".\"tenant_id\")",
                query);
        }

        [TestMethod]
        public void Build_ThreeLevelNestedReadWithNonEqualityCorrelation_FallsBackToLateral()
        {
            SqlQueryStructure root = CreateChainedList(levels: 3, rootOrdered: true);
            SqlQueryStructure level2 = root.JoinQueries["table1_subq"].JoinQueries["table2_subq"];
            level2.Predicates.Clear();
            level2.Predicates.Add(new Predicate(
                new PredicateOperand(new Column(string.Empty, string.Empty, "parent_id", "table2")),
                PredicateOperation.GreaterThan,
                new PredicateOperand(new Column(string.Empty, string.Empty, "id", "table1"))));

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(query, "\"table1_rows_cte\"", query);
            StringAssert.Contains(query, "LEFT OUTER JOIN LATERAL", query);
            Assert.IsFalse(query.Contains("dab_restrict_", StringComparison.Ordinal), query);
        }

        [TestMethod]
        public void Build_NoPageCte_ThreeLevelSqlIsUnchanged()
        {
            SqlQueryStructure root = CreateChainedList(levels: 3, rootOrdered: false);

            string query = new OracleQueryBuilder().Build(root);

            Assert.AreEqual(ReadGolden("oracle-nested-nopage.sql"), query, query);
            Assert.IsFalse(query.Contains("dab_page_cte", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("_rows_cte", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("dab_restrict_", StringComparison.Ordinal), query);
        }

        [TestMethod]
        public void Build_AssociativeChildAtDepthTwo_KeepsSingleBlockShape()
        {
            SqlQueryStructure root = CreateChainedList(levels: 4, rootOrdered: true);
            SqlQueryStructure level2 = root.JoinQueries["table1_subq"].JoinQueries["table2_subq"];
            SourceDefinition linkSource = new();
            linkSource.Columns.Add("CHILD_ID", new ColumnDefinition { SystemType = typeof(int) });
            level2.Joins.Add(new SqlJoinStructure(
                new DatabaseTable("DBO", "LINK") { TableDefinition = linkSource },
                "table9",
                new List<Predicate>
                {
                    new(
                        new PredicateOperand(new Column("DBO", "LINK", "CHILD_ID", "table9")),
                        PredicateOperation.Equal,
                        new PredicateOperand(new Column("DBO", "LEVEL2", "id", "table2")))
                }));

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(query, "\"table1_rows_cte\"", query);
            Assert.IsFalse(query.Contains("table2_rows_cte", StringComparison.Ordinal), query);
            StringAssert.Contains(query, "INNER JOIN \"DBO\".\"LINK\" \"TABLE9\"", query);
            string level3 = CteBody(query, "table3_subq_cte");
            Assert.IsFalse(level3.Contains("dab_restrict_", StringComparison.Ordinal), level3);
            Assert.IsFalse(level3.Contains("dab_page", StringComparison.Ordinal), level3);
            string level2Sql = CteBody(query, "table2_subq_cte");
            StringAssert.Contains(level2Sql, "\"dab_restrict_0\"", level2Sql);
            StringAssert.Contains(level2Sql, "ROW_NUMBER()", level2Sql);
            StringAssert.Contains(level2Sql, "INNER JOIN \"DBO\".\"LINK\" \"TABLE9\"", level2Sql);
        }

        [TestMethod]
        public void Build_FlattenedToOneWithDescendant_LeavesDescendantUnrestricted()
        {
            SqlQueryStructure root = CreateToOneWithListDescendant(withReadPolicy: false);

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(query, "LEFT OUTER JOIN \"DBO\".\"CHILDREN\" \"TABLE1\" ON ", query);
            Assert.IsFalse(query.Contains("_rows_cte", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("dab_page_cte", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("dab_restrict_", StringComparison.Ordinal), query);
            StringAssert.Contains(query, "FROM \"DBO\".\"GRANDCHILDREN\" \"TABLE2\" WHERE 1 = 1", query);
        }

        [TestMethod]
        public void Build_ToOneWithDescendantsAndPolicy_HoistsRowsCteWithoutRank()
        {
            SqlQueryStructure root = CreateToOneWithListDescendant(withReadPolicy: true);

            string query = new OracleQueryBuilder().Build(root);

            StringAssert.Contains(query, "\"table1_rows_cte\" AS ( SELECT /*+ MATERIALIZE */", query);
            string rows = CteBody(query, "table1_rows_cte");
            Assert.IsFalse(rows.Contains("ROW_NUMBER()", StringComparison.Ordinal), rows);
            Assert.IsFalse(rows.Contains("\"rn\"", StringComparison.Ordinal), rows);
            StringAssert.Contains(
                query,
                "INNER JOIN (SELECT DISTINCT \"ID\" FROM \"table1_rows_cte\") \"dab_restrict_0\" ON (\"dab_restrict_0\".\"ID\" = \"TABLE2\".\"parent_id\")",
                query);
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

        /// <summary>
        /// Chain of list relationships. Level 0 is the root. Each deeper level correlates its
        /// <c>parent_id</c> to the previous level's <c>id</c>.
        /// </summary>
        internal static SqlQueryStructure CreateChainedList(int levels, bool rootOrdered, uint rootLimit = 100)
        {
            SqlQueryStructure? child = null;
            for (int level = levels - 1; level >= 0; level--)
            {
                bool isRoot = level == 0;
                string alias = $"table{level}";
                string schema = "DBO";
                string table = isRoot ? "PARENTS" : $"LEVEL{level}";
                string idColumn = isRoot ? "ID" : "id";
                SqlQueryStructure node = CreateStructure(isList: true);
                SourceDefinition source = new();
                source.Columns.Add(idColumn, new ColumnDefinition { SystemType = typeof(int) });
                if (!isRoot)
                {
                    source.Columns.Add("parent_id", new ColumnDefinition { SystemType = typeof(int) });
                }

                DatabaseTable databaseTable = new(schema, table) { TableDefinition = source };
                string entityName = $"Entity{level}";
                Mock<ISqlMetadataProvider> metadata = new();
                metadata.Setup(provider => provider.GetSourceDefinition(entityName)).Returns(source);
                SetField(node, "EntityName", entityName);
                SetField(node, "MetadataProvider", metadata.Object);
                SetField(node, "DatabaseObject", databaseTable);
                SetField(node, "SourceAlias", alias);

                List<LabelledColumn> columns = new()
                {
                    new(schema, table, idColumn, "id", alias)
                };
                if (child is not null)
                {
                    columns.Add(new(schema, table, SqlQueryStructure.DATA_IDENT, $"level{level + 1}", $"table{level + 1}_subq"));
                    SetJoinQuery(node, $"table{level + 1}_subq", child);
                }
                else
                {
                    SetJoinQuery(node, alias: null, query: null);
                }

                SetBaseProperty(node, "Columns", columns);
                List<Predicate> predicates = new();
                if (!isRoot)
                {
                    string parentIdColumn = level == 1 ? "ID" : "id";
                    predicates.Add(new Predicate(
                        new PredicateOperand(new Column(string.Empty, string.Empty, "parent_id", alias)),
                        PredicateOperation.Equal,
                        new PredicateOperand(new Column(string.Empty, string.Empty, parentIdColumn, $"table{level - 1}"))));
                }

                SetField(node, "Predicates", predicates);
                SetField(node, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
                SetField(node, "Joins", new List<SqlJoinStructure>());
                SetField(node, "FilterPredicates", string.Empty);
                List<OrderByColumn> order = new();
                if (!isRoot || rootOrdered)
                {
                    order.Add(new(schema, table, idColumn, alias));
                }

                SetField(node, "OrderByColumns", order);
                SetField(node, "PaginationMetadata", new PaginationMetadata(node));
                SetField(node, "GroupByMetadata", new GroupByMetadata());
                SetField(node, "Counter", new IncrementingInteger());
                SetLimit(node, isRoot ? rootLimit : (uint)(50 / level));
                child = node;
            }

            return child!;
        }

        private static SqlQueryStructure CreateToOneWithListDescendant(bool withReadPolicy)
        {
            SourceDefinition grandSource = new();
            grandSource.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            grandSource.Columns.Add("parent_id", new ColumnDefinition { SystemType = typeof(int) });
            Mock<ISqlMetadataProvider> grandMetadata = new();
            grandMetadata.Setup(provider => provider.GetSourceDefinition("Grandchild")).Returns(grandSource);
            SqlQueryStructure grandchild = CreateStructure(isList: true);
            SetField(grandchild, "EntityName", "Grandchild");
            SetField(grandchild, "MetadataProvider", grandMetadata.Object);
            SetField(grandchild, "DatabaseObject", new DatabaseTable("DBO", "GRANDCHILDREN") { TableDefinition = grandSource });
            SetField(grandchild, "SourceAlias", "table2");
            SetBaseProperty(grandchild, "Columns", new List<LabelledColumn>
            {
                new("DBO", "GRANDCHILDREN", "id", "id", "table2")
            });
            SetField(grandchild, "Predicates", new List<Predicate>
            {
                new(
                    new PredicateOperand(new Column(string.Empty, string.Empty, "parent_id", "table2")),
                    PredicateOperation.Equal,
                    new PredicateOperand(new Column(string.Empty, string.Empty, "ID", "table1")))
            });
            SetField(grandchild, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(grandchild, "Joins", new List<SqlJoinStructure>());
            SetField(grandchild, "FilterPredicates", string.Empty);
            SetField(grandchild, "OrderByColumns", new List<OrderByColumn> { new("DBO", "GRANDCHILDREN", "id", "table2") });
            SetField(grandchild, "PaginationMetadata", new PaginationMetadata(grandchild));
            SetField(grandchild, "GroupByMetadata", new GroupByMetadata());
            SetField(grandchild, "Counter", new IncrementingInteger());
            SetLimit(grandchild, 10);

            SourceDefinition childSource = new();
            childSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            childSource.PrimaryKey.Add("ID");
            Mock<ISqlMetadataProvider> childMetadata = new();
            childMetadata.Setup(provider => provider.GetSourceDefinition("Child")).Returns(childSource);
            SqlQueryStructure child = CreateStructure(isList: false);
            SetField(child, "EntityName", "Child");
            SetField(child, "MetadataProvider", childMetadata.Object);
            SetField(child, "DatabaseObject", new DatabaseTable("DBO", "CHILDREN") { TableDefinition = childSource });
            SetField(child, "SourceAlias", "table1");
            SetBaseProperty(child, "Columns", new List<LabelledColumn>
            {
                new("DBO", "CHILDREN", "ID", "ID", "table1"),
                new("DBO", "CHILDREN", SqlQueryStructure.DATA_IDENT, "grandchildren", "table2_subq")
            });
            SetJoinQuery(child, "table2_subq", grandchild);
            SetField(child, "Predicates", new List<Predicate>
            {
                new(
                    new PredicateOperand(new Column(string.Empty, string.Empty, "ID", "table1")),
                    PredicateOperation.Equal,
                    new PredicateOperand(new Column(string.Empty, string.Empty, "ID", "table0")))
            });
            Dictionary<EntityActionOperation, string?> policies = new();
            if (withReadPolicy)
            {
                policies[EntityActionOperation.Read] = "(\"ID\" = @param0)";
            }

            SetField(child, "DbPolicyPredicatesForOperations", policies);
            SetField(child, "Joins", new List<SqlJoinStructure>());
            SetField(child, "FilterPredicates", string.Empty);
            SetField(child, "OrderByColumns", new List<OrderByColumn> { new("DBO", "CHILDREN", "ID", "table1") });
            SetField(child, "PaginationMetadata", new PaginationMetadata(child));
            SetField(child, "GroupByMetadata", new GroupByMetadata());
            SetField(child, "Counter", new IncrementingInteger());
            SetLimit(child, 1);

            SourceDefinition parentSource = new();
            parentSource.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(int) });
            Mock<ISqlMetadataProvider> parentMetadata = new();
            parentMetadata.Setup(provider => provider.GetSourceDefinition("Parent")).Returns(parentSource);
            SqlQueryStructure parent = CreateStructure(isList: true);
            SetField(parent, "EntityName", "Parent");
            SetField(parent, "MetadataProvider", parentMetadata.Object);
            SetField(parent, "DatabaseObject", new DatabaseTable("DBO", "PARENTS") { TableDefinition = parentSource });
            SetField(parent, "SourceAlias", "table0");
            SetBaseProperty(parent, "Columns", new List<LabelledColumn>
            {
                new("DBO", "PARENTS", "ID", "id", "table0"),
                new("DBO", "PARENTS", SqlQueryStructure.DATA_IDENT, "child", "table1_subq")
            });
            SetJoinQuery(parent, "table1_subq", child);
            SetField(parent, "Predicates", new List<Predicate>());
            SetField(parent, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(parent, "Joins", new List<SqlJoinStructure>());
            SetField(parent, "FilterPredicates", string.Empty);
            SetField(parent, "OrderByColumns", new List<OrderByColumn> { new("DBO", "PARENTS", "ID", "table0") });
            SetField(parent, "PaginationMetadata", new PaginationMetadata(parent));
            SetField(parent, "GroupByMetadata", new GroupByMetadata());
            SetField(parent, "Counter", new IncrementingInteger());
            SetLimit(parent, 100);
            return parent;
        }

        private static string CteBody(string query, string cteName)
        {
            string marker = $"\"{cteName}\" AS (";
            int start = query.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, query);
            int depth = 0;
            for (int i = start + marker.Length - 1; i < query.Length; i++)
            {
                if (query[i] == '(')
                {
                    depth++;
                }
                else if (query[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return query.Substring(start, i - start + 1);
                    }
                }
            }

            Assert.Fail(query);
            return string.Empty;
        }

        private static string ReadGolden(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "UnitTests", "Goldens", name);
            Assert.IsTrue(File.Exists(path), path);
            return File.ReadAllText(path);
        }

        private static SqlQueryStructure CreateLikeExistsStructure()
        {
            SourceDefinition source = new();
            source.Columns.Add("TITLE", new ColumnDefinition { SystemType = typeof(string) });
            DatabaseTable table = new("DBO", "NOTES") { TableDefinition = source };
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(provider => provider.GetSourceDefinition("Note")).Returns(source);

            SqlQueryStructure exists = CreateStructure(isList: false);
            SetField(exists, "EntityName", "Note");
            SetField(exists, "MetadataProvider", metadata.Object);
            SetField(exists, "DatabaseObject", table);
            SetField(exists, "SourceAlias", "table9");
            SetBaseProperty(exists, "Columns", new List<LabelledColumn>());
            SetField(exists, "Predicates", new List<Predicate>
            {
                new(
                    new PredicateOperand(new Column("DBO", "NOTES", "TITLE", "table9")),
                    PredicateOperation.LIKE,
                    new PredicateOperand("@param10")),
                new(
                    new PredicateOperand(new Column("DBO", "NOTES", "TITLE", "table9")),
                    PredicateOperation.NOT_LIKE,
                    new PredicateOperand("@param2"))
            });
            SetField(exists, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>
            {
                [EntityActionOperation.Read] = "(\"TITLE\" LIKE @p ESCAPE '\\')"
            });
            SetField(exists, "Joins", new List<SqlJoinStructure>());
            SetField(exists, "FilterPredicates", string.Empty);
            SetField(exists, "OrderByColumns", new List<OrderByColumn>());
            SetField(exists, "PaginationMetadata", new PaginationMetadata(exists));
            SetField(exists, "GroupByMetadata", new GroupByMetadata());
            SetField(exists, "Counter", new IncrementingInteger());
            return exists;
        }

        private static (SqlQueryStructure Parent, SqlQueryStructure Child) CreateParentWithListChild(
            PredicateOperation correlationOperation)
        {
            SourceDefinition childSource = new();
            childSource.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            childSource.Columns.Add("parent_id", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable childTable = new("DBO", "CHILDREN") { TableDefinition = childSource };
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
            DatabaseTable parentTable = new("DBO", "PARENTS") { TableDefinition = parentSource };
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
            DatabaseTable childTable = new("DBO", "CHILDREN") { TableDefinition = childSource };
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
            DatabaseTable parentTable = new("DBO", "PARENTS") { TableDefinition = parentSource };
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
