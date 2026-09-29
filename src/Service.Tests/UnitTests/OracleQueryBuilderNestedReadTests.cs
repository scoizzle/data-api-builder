// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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

            StringAssert.StartsWith(query, "WITH ");
            StringAssert.Contains(query, "\"table1_subq_cte\" AS ( SELECT \"k0\"");
            StringAssert.Contains(query, "ROW_NUMBER() OVER (PARTITION BY \"TABLE1\".\"parent_id\"");
            StringAssert.Contains(query, "GROUP BY \"k0\"");
            StringAssert.Contains(query, "LEFT OUTER JOIN \"table1_subq_cte\" ON \"table1_subq_cte\".\"k0\" = \"TABLE0\".\"ID\"");
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
    }
}
