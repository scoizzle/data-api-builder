// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class BaseSqlQueryStructureHelperTests
    {
        [DataTestMethod]
        [DataRow("text", typeof(string), "text")]
        [DataRow("255", typeof(byte), (byte)255)]
        [DataRow("-12", typeof(short), (short)-12)]
        [DataRow("123", typeof(int), 123)]
        [DataRow("123456789", typeof(long), 123456789L)]
        [DataRow("true", typeof(bool), true)]
        [DataRow("7d4ee078-a85c-4a95-82b6-4bf6c3f3cfe8", typeof(Guid), "7d4ee078-a85c-4a95-82b6-4bf6c3f3cfe8")]
        public void ParseParamAsSystemType_ParsesSupportedScalarTypes(string value, Type targetType, object expected)
        {
            object result = InvokeParse(value, targetType);

            if (targetType == typeof(Guid))
            {
                Assert.AreEqual(Guid.Parse((string)expected), result);
            }
            else
            {
                Assert.AreEqual(expected, result);
            }
        }

        [TestMethod]
        public void ParseParamAsSystemType_ParsesBinaryDateAndFloatingPointTypes()
        {
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])InvokeParse("AQID", typeof(byte[])));
            Assert.AreEqual(1.25f, InvokeParse("1.25", typeof(float)));
            Assert.AreEqual(2.5d, InvokeParse("2.5", typeof(double)));
            Assert.AreEqual(3.75m, InvokeParse("3.75", typeof(decimal)));
            Assert.AreEqual(new TimeOnly(12, 34, 56), InvokeParse("12:34:56", typeof(TimeOnly)));
        }

        [TestMethod]
        public void ParseParamAsSystemType_ParsesDatesAndArrays()
        {
            DateTime dateTime = (DateTime)InvokeParse("2025-01-02T12:00:00+03:00", typeof(DateTime));
            Assert.AreEqual(DateTimeKind.Utc, dateTime.Kind);
            Assert.AreEqual(9, dateTime.Hour);

            DateTimeOffset offset = (DateTimeOffset)InvokeParse("2025-01-02T12:00:00+03:00", typeof(DateTimeOffset));
            Assert.AreEqual(TimeSpan.FromHours(3), offset.Offset);

            Assert.AreEqual(new TimeSpan(12, 34, 56), InvokeParse("12:34:56", typeof(TimeSpan)));

            object[] values = (object[])InvokeParse("[1.5,2.25]", typeof(float[]));
            CollectionAssert.AreEqual(new object[] { 1.5f, 2.25f }, values);
        }

        [TestMethod]
        public void ParseParamAsSystemType_UnsupportedOrMalformedArray_Throws()
        {
            TargetInvocationException unsupported = Assert.ThrowsException<TargetInvocationException>(
                () => InvokeParse("value", typeof(Uri)));
            Assert.IsInstanceOfType<NotSupportedException>(unsupported.InnerException);

            TargetInvocationException malformed = Assert.ThrowsException<TargetInvocationException>(
                () => InvokeParse("not-json", typeof(float[])));
            Assert.IsInstanceOfType<FormatException>(malformed.InnerException);
        }

        [TestMethod]
        public void GetSubArgumentNamesFromGQLMutArguments_ReturnsObjectFieldNames()
        {
            Dictionary<string, object?> parameters = new()
            {
                ["item"] = new List<ObjectFieldNode>
                {
                    new("id", new IntValueNode(1)),
                    new("title", new StringValueNode("book"))
                }
            };

            List<string> names = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments("item", parameters);

            CollectionAssert.AreEqual(new[] { "id", "title" }, names);
        }

        /// <summary>
        /// Verifies malformed and missing GraphQL mutation arguments produce their distinct contextual errors.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "Argument exists with an unexpected shape")]
        [DataRow(false, DisplayName = "Expected argument is missing")]
        public void GetSubArgumentNamesFromGQLMutArguments_InvalidArguments_Throw(bool includeWrongFormat)
        {
            Dictionary<string, object?> parameters = new();
            if (includeWrongFormat)
            {
                parameters["item"] = "unexpected";
            }

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments("item", parameters));
            StringAssert.Contains(exception.Message, includeWrongFormat ? "Unexpected" : "Expected");
        }

        [TestMethod]
        public void GetColumnSystemType_ReturnsKnownTypeAndRejectsUnknownColumn()
        {
            (TestSqlQueryStructure structure, _) = CreateStructure(EntitySourceType.Table, isDevelopment: false);

            Assert.AreEqual(typeof(int), structure.GetColumnSystemType("id"));
            Assert.ThrowsException<DataApiBuilderException>(() => structure.GetColumnSystemType("missing"));
        }

        [TestMethod]
        public void AddJoinPredicatesForRelationship_MissingSelfRelationshipThrows()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            metadata.SetupGet(x => x.RelationshipToFkDefinition).Returns(new Dictionary<EntityRelationshipKey, ForeignKeyDefinition>());

            Assert.ThrowsException<DataApiBuilderException>(() => structure.AddJoinPredicatesForRelationship(
                new EntityRelationshipKey("Book", "related"), "Book", "table1", structure));
        }

        [TestMethod]
        public void AddJoinPredicatesForRelatedEntity_MissingRelationshipThrows()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable relatedTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = structure.DatabaseObject,
                ["Author"] = relatedTable
            });
            metadata.Setup(x => x.GetSourceDefinition("Author")).Returns(relatedTable.TableDefinition);

            Assert.ThrowsException<DataApiBuilderException>(() =>
                structure.AddJoinPredicatesForRelatedEntity("Author", "table1", structure));
        }

        /// <summary>
        /// When multiple relationships connect the same pair of entities through distinct foreign
        /// keys, selecting one relationship must emit only the predicates of the foreign key that
        /// matches that relationship. Previously every foreign key definition between the pair was
        /// iterated, combining unrelated predicates (e.g. component/customer/print/book product
        /// columns) into one join and dropping valid nested results.
        /// </summary>
        [TestMethod]
        public void AddJoinPredicatesForRelationship_MultipleForeignKeysBetweenSameEntityPair_SelectsOnlyMatchingFk()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable authorTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            DatabaseTable bookTable = (DatabaseTable)structure.DatabaseObject;
            SetupRelationshipMetadata(metadata, structure, authorTable, new List<ForeignKeyDefinition>
            {
                CreateForeignKey("to_author", bookTable, authorTable, new[] { "author_id" }, new[] { "id" }),
                CreateForeignKey("to_editor", bookTable, authorTable, new[] { "editor_id" }, new[] { "id" })
            });

            structure.AddJoinPredicatesForRelationship(
                fkLookupKey: new EntityRelationshipKey("Book", "to_author"),
                targetEntityName: "Author",
                subqueryTargetTableAlias: "table1",
                subQuery: structure);

            Assert.AreEqual(1, structure.Predicates.Count, "Only the selected relationship's foreign key predicate should be emitted.");
            AssertJoinPredicate(structure.Predicates[0], leftColumn: "author_id", rightColumn: "id", rightAlias: "table1");
        }

        /// <summary>
        /// A composite (multi-column) foreign key must keep every referencing/referenced column
        /// pair in order, and must not absorb predicates from other relationships to the same entity.
        /// </summary>
        [TestMethod]
        public void AddJoinPredicatesForRelationship_CompositeForeignKey_PreservesAllColumnMappings()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable authorTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            DatabaseTable bookTable = (DatabaseTable)structure.DatabaseObject;
            SetupRelationshipMetadata(metadata, structure, authorTable, new List<ForeignKeyDefinition>
            {
                CreateForeignKey("to_author", bookTable, authorTable, new[] { "author_first", "author_last" }, new[] { "first", "last" }),
                CreateForeignKey("to_editor", bookTable, authorTable, new[] { "editor_id" }, new[] { "id" })
            });

            structure.AddJoinPredicatesForRelationship(
                fkLookupKey: new EntityRelationshipKey("Book", "to_author"),
                targetEntityName: "Author",
                subqueryTargetTableAlias: "table1",
                subQuery: structure);

            Assert.AreEqual(2, structure.Predicates.Count, "A composite foreign key must emit one predicate per column pair.");
            AssertJoinPredicate(structure.Predicates[0], leftColumn: "author_first", rightColumn: "first", rightAlias: "table1");
            AssertJoinPredicate(structure.Predicates[1], leftColumn: "author_last", rightColumn: "last", rightAlias: "table1");
        }

        /// <summary>
        /// A many-to-many relationship is represented by two foreign key definitions (source ->
        /// linking and linking -> target) that share the relationship name. Selectively matching
        /// by relationship name must retain both so the linking object predicate and join survive.
        /// </summary>
        [TestMethod]
        public void AddJoinPredicatesForRelationship_LinkingRelationship_RetainsBothLinkingForeignKeys()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable authorTable = new("dbo", "authors") { TableDefinition = new SourceDefinition() };
            DatabaseTable bookTable = (DatabaseTable)structure.DatabaseObject;
            DatabaseTable bookAuthorsTable = new("dbo", "book_authors") { TableDefinition = new SourceDefinition() };
            DatabaseTable bookEditorsTable = new("dbo", "book_editors") { TableDefinition = new SourceDefinition() };
            SetupRelationshipMetadata(metadata, structure, authorTable, new List<ForeignKeyDefinition>
            {
                CreateForeignKey(
                    "to_authors", bookAuthorsTable, bookTable, new[] { "book_id" }, new[] { "id" },
                    RelationshipRole.Linking, RelationshipRole.Source),
                CreateForeignKey(
                    "to_authors", bookAuthorsTable, authorTable, new[] { "author_id" }, new[] { "id" },
                    RelationshipRole.Linking, RelationshipRole.Target),
                CreateForeignKey(
                    "to_editors", bookEditorsTable, bookTable, new[] { "book_id" }, new[] { "id" },
                    RelationshipRole.Linking, RelationshipRole.Source),
                CreateForeignKey(
                    "to_editors", bookEditorsTable, authorTable, new[] { "editor_id" }, new[] { "id" },
                    RelationshipRole.Linking, RelationshipRole.Target)
            });

            structure.AddJoinPredicatesForRelationship(
                fkLookupKey: new EntityRelationshipKey("Book", "to_authors"),
                targetEntityName: "Author",
                subqueryTargetTableAlias: "table1",
                subQuery: structure);

            Assert.AreEqual(1, structure.Predicates.Count, "Only the selected linking relationship's predicate should be emitted.");
            AssertJoinPredicate(structure.Predicates[0], leftColumn: "book_id", rightColumn: "id", rightAlias: structure.SourceAlias);

            Assert.AreEqual(1, structure.Joins.Count, "Only the selected linking relationship's join should be emitted.");
            SqlJoinStructure join = structure.Joins[0];
            Assert.AreEqual("dbo.book_authors", join.DbObject.FullName);
            Assert.AreEqual(structure.Predicates[0].Left!.AsColumn()!.TableAlias, join.TableAlias, "The linking predicate and join must share the linking table alias.");
            Assert.AreEqual(1, join.Predicates.Count);
            AssertJoinPredicate(join.Predicates[0], leftColumn: "author_id", rightColumn: "id", rightAlias: "table1");
        }

        /// <summary>
        /// Self-joined entities already resolve their foreign key through
        /// RelationshipToFkDefinition. This guards that existing behavior while the related-entity
        /// path is scoped by relationship name.
        /// </summary>
        [TestMethod]
        public void AddJoinPredicatesForRelationship_SelfJoinedEntity_UsesRelationshipSpecificForeignKey()
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(EntitySourceType.Table, false);
            DatabaseTable bookTable = (DatabaseTable)structure.DatabaseObject;
            ForeignKeyDefinition parentForeignKey = CreateForeignKey("parent_book", bookTable, bookTable, new[] { "parent_id" }, new[] { "id" });
            RelationshipMetadata relationshipMetadata = new();
            relationshipMetadata.TargetEntityToFkDefinitionMap["Book"] = new() { parentForeignKey };
            bookTable.SourceDefinition.SourceEntityRelationshipMap["Book"] = relationshipMetadata;
            metadata.SetupGet(x => x.RelationshipToFkDefinition).Returns(new Dictionary<EntityRelationshipKey, ForeignKeyDefinition>
            {
                [new EntityRelationshipKey("Book", "parent_book")] = parentForeignKey
            });

            structure.AddJoinPredicatesForRelationship(
                fkLookupKey: new EntityRelationshipKey("Book", "parent_book"),
                targetEntityName: "Book",
                subqueryTargetTableAlias: "tableSelf",
                subQuery: structure);

            Assert.AreEqual(1, structure.Predicates.Count);
            AssertJoinPredicate(structure.Predicates[0], leftColumn: "parent_id", rightColumn: "id", rightAlias: "tableSelf");
        }

        [TestMethod]
        public void ProcessOdataClause_NullPolicyStoresNullAndMissingOperationReturnsNull()
        {
            (TestSqlQueryStructure structure, _) = CreateStructure(EntitySourceType.Table, false);

            structure.ProcessOdataClause(null, EntityActionOperation.Read);

            Assert.IsTrue(structure.DbPolicyPredicatesForOperations.ContainsKey(EntityActionOperation.Read));
            Assert.IsNull(structure.GetDbPolicyForOperation(EntityActionOperation.Read));
            Assert.IsNull(structure.GetDbPolicyForOperation(EntityActionOperation.Create));
        }

        /// <summary>
        /// Verifies conversion errors expose source-kind context in development and safe field names in production.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntitySourceType.StoredProcedure, true, "stored procedure parameter", DisplayName = "Development stored-procedure error describes the parameter kind")]
        [DataRow(EntitySourceType.Table, true, "column", DisplayName = "Development table error describes the column kind")]
        [DataRow(EntitySourceType.Table, false, "publicId", DisplayName = "Production table error uses the exposed field name")]
        [DataRow(EntitySourceType.StoredProcedure, false, "id", DisplayName = "Production stored-procedure error uses the parameter name")]
        public void GetParamAsSystemType_InvalidValueUsesSafeContextualMessage(
            EntitySourceType sourceType,
            bool isDevelopment,
            string expectedMessagePart)
        {
            (TestSqlQueryStructure structure, Mock<ISqlMetadataProvider> metadata) = CreateStructure(sourceType, isDevelopment);
            metadata.Setup(x => x.TryGetExposedColumnName("Book", "id", out It.Ref<string?>.IsAny))
                .Returns((string _, string _, out string? name) =>
                {
                    name = "publicId";
                    return true;
                });

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                structure.ParseWithContext("not-an-int", "id", typeof(int)));

            StringAssert.Contains(exception.Message, expectedMessagePart);
        }

        /// <summary>
        /// Wires up a related entity and the foreign key definitions defined for the source
        /// entity's relationships targeting it.
        /// </summary>
        private static void SetupRelationshipMetadata(
            Mock<ISqlMetadataProvider> metadata,
            TestSqlQueryStructure structure,
            DatabaseTable relatedTable,
            List<ForeignKeyDefinition> foreignKeys)
        {
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = structure.DatabaseObject,
                ["Author"] = relatedTable
            });
            metadata.Setup(x => x.GetSourceDefinition("Author")).Returns(relatedTable.SourceDefinition);

            RelationshipMetadata relationshipMetadata = new();
            relationshipMetadata.TargetEntityToFkDefinitionMap["Author"] = foreignKeys;
            structure.DatabaseObject.SourceDefinition.SourceEntityRelationshipMap["Book"] = relationshipMetadata;
        }

        private static ForeignKeyDefinition CreateForeignKey(
            string relationshipName,
            DatabaseTable referencingTable,
            DatabaseTable referencedTable,
            string[] referencingColumns,
            string[] referencedColumns,
            RelationshipRole referencingRole = RelationshipRole.Source,
            RelationshipRole referencedRole = RelationshipRole.Target)
        {
            ForeignKeyDefinition foreignKey = new()
            {
                SourceEntityName = "Book",
                RelationshipName = relationshipName,
                ReferencingEntityRole = referencingRole,
                ReferencedEntityRole = referencedRole,
                Pair = new RelationShipPair(referencingTable, referencedTable) { RelationshipName = relationshipName }
            };
            foreignKey.ReferencingColumns.AddRange(referencingColumns);
            foreignKey.ReferencedColumns.AddRange(referencedColumns);
            return foreignKey;
        }

        private static void AssertJoinPredicate(Predicate predicate, string leftColumn, string rightColumn, string? rightAlias)
        {
            Assert.AreEqual(PredicateOperation.Equal, predicate.Op);
            Column left = predicate.Left!.AsColumn()!;
            Column right = predicate.Right.AsColumn()!;
            Assert.AreEqual(leftColumn, left.ColumnName);
            Assert.AreEqual(rightColumn, right.ColumnName);
            Assert.AreEqual(rightAlias, right.TableAlias);
        }

        private static object InvokeParse(string value, Type targetType)
        {
            MethodInfo method = typeof(BaseSqlQueryStructure).GetMethod(
                "ParseParamAsSystemType",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, new object[] { value, targetType })!;
        }

        private static (TestSqlQueryStructure Structure, Mock<ISqlMetadataProvider> Metadata) CreateStructure(
            EntitySourceType sourceType,
            bool isDevelopment)
        {
            SourceDefinition sourceDefinition = new();
            sourceDefinition.Columns["id"] = new ColumnDefinition(typeof(int));
            StoredProcedureDefinition storedProcedureDefinition = new();
            storedProcedureDefinition.Columns["id"] = sourceDefinition.Columns["id"];
            DatabaseObject databaseObject = sourceType is EntitySourceType.StoredProcedure
                ? new DatabaseStoredProcedure("dbo", "books") { StoredProcedureDefinition = storedProcedureDefinition }
                : new DatabaseTable("dbo", "books") { TableDefinition = sourceDefinition };
            databaseObject.SourceType = sourceType;

            Mock<ISqlMetadataProvider> metadata = new();
            metadata.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                ["Book"] = databaseObject
            });
            metadata.Setup(x => x.IsDevelopmentMode()).Returns(isDevelopment);
            metadata.Setup(x => x.GetSourceDefinition("Book")).Returns(databaseObject.SourceDefinition);

            return (new TestSqlQueryStructure(metadata.Object), metadata);
        }

        private sealed class TestSqlQueryStructure : BaseSqlQueryStructure
        {
            public TestSqlQueryStructure(ISqlMetadataProvider metadataProvider)
                : base(metadataProvider, new Mock<IAuthorizationResolver>().Object, null!, entityName: "Book")
            {
            }

            public object ParseWithContext(string value, string fieldName, Type type) =>
                GetParamAsSystemType(value, fieldName, type);
        }
    }
}
