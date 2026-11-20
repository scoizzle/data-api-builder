// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
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
    /// Unit tests for the Oracle empty-string policy rewrite.
    /// These tests do not require database metadata; the OData nodes are constructed directly.
    /// </summary>
    [TestClass]
    public class ODataASTVisitorPolicyUnitTests
    {
        private static SingleValuePropertyAccessNode CreatePropertyNode(string propertyName)
        {
            EdmModel model = new();
            EdmEntityType entityType = new("Test", "Entity");
            model.AddElement(entityType);
            IEdmStructuralProperty property = entityType.AddStructuralProperty(propertyName, EdmPrimitiveTypeKind.String);

            // The node requires a non-null source; its value is irrelevant to the empty-string rewrite.
            SingleValueNode source = new ConstantNode(
                constantValue: 0,
                literalText: "0",
                new EdmPrimitiveTypeReference(EdmCoreModel.Instance.GetPrimitiveType(EdmPrimitiveTypeKind.Int32), isNullable: false));

            return new SingleValuePropertyAccessNode(source, property);
        }

        private static ConstantNode CreateStringConstantNode(string value)
        {
            return new ConstantNode(
                constantValue: value,
                literalText: $"'{value}'",
                new EdmPrimitiveTypeReference(EdmCoreModel.Instance.GetPrimitiveType(EdmPrimitiveTypeKind.String), isNullable: false));
        }

        [TestMethod]
        public void OracleEmptyStringEquality_RewritesToIsNull()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = new OracleSqlLiteralRewriter().TryGetEmptyStringNullTest(
                BinaryOperatorKind.Equal,
                propertyNode,
                emptyStringNode,
                out SingleValuePropertyAccessNode? resolvedProperty,
                out PredicateOperation nullTestOperation);

            Assert.IsTrue(matched);
            Assert.AreEqual(PredicateOperation.IS, nullTestOperation);
            Assert.AreSame(propertyNode, resolvedProperty);
        }

        [TestMethod]
        public void OracleEmptyStringInequality_RewritesToIsNotNull()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = new OracleSqlLiteralRewriter().TryGetEmptyStringNullTest(
                BinaryOperatorKind.NotEqual,
                propertyNode,
                emptyStringNode,
                out SingleValuePropertyAccessNode? resolvedProperty,
                out PredicateOperation nullTestOperation);

            Assert.IsTrue(matched);
            Assert.AreEqual(PredicateOperation.IS_NOT, nullTestOperation);
            Assert.AreSame(propertyNode, resolvedProperty);
        }

        [TestMethod]
        public void ConstantOnLeftOracleEmptyStringEquality_RewritesToIsNull()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = new OracleSqlLiteralRewriter().TryGetEmptyStringNullTest(
                BinaryOperatorKind.Equal,
                emptyStringNode,
                propertyNode,
                out SingleValuePropertyAccessNode? resolvedProperty,
                out PredicateOperation nullTestOperation);

            Assert.IsTrue(matched);
            Assert.AreEqual(PredicateOperation.IS, nullTestOperation);
            Assert.AreSame(propertyNode, resolvedProperty);
        }

        [TestMethod]
        public void NonEmptyString_IsNotRewritten()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode nonEmptyStringNode = CreateStringConstantNode("SciFi");

            bool matched = new OracleSqlLiteralRewriter().TryGetEmptyStringNullTest(
                BinaryOperatorKind.Equal,
                propertyNode,
                nonEmptyStringNode,
                out _,
                out _);

            Assert.IsFalse(matched);
        }

        [TestMethod]
        public void NonOracleEmptyStringEquality_IsNotRewritten()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = SqlLiteralRewriter.Shared.TryGetEmptyStringNullTest(
                BinaryOperatorKind.Equal,
                propertyNode,
                emptyStringNode,
                out _,
                out _);

            Assert.IsFalse(matched, "The default rewriter must not rewrite empty-string comparisons.");
        }

        [TestMethod]
        public void NonEqualityOperator_IsNotRewritten()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = new OracleSqlLiteralRewriter().TryGetEmptyStringNullTest(
                BinaryOperatorKind.GreaterThan,
                propertyNode,
                emptyStringNode,
                out _,
                out _);

            Assert.IsFalse(matched);
        }

        private delegate void TryGetBackingColumnCallback(string entity, string field, out string? column);

        [TestMethod]
        public void OraclePolicyEmptyStringEquality_ProducesIsNullPredicate()
        {
            BinaryOperatorNode node = new(
                BinaryOperatorKind.Equal,
                CreatePropertyNode("title"),
                CreateStringConstantNode(string.Empty));

            Assert.AreEqual("(\"TITLE\" IS NULL)", CreatePolicyVisitor().Visit(node));
        }

        [TestMethod]
        public void OraclePolicyEmptyStringInequality_ProducesIsNotNullPredicate()
        {
            BinaryOperatorNode node = new(
                BinaryOperatorKind.NotEqual,
                CreatePropertyNode("title"),
                CreateStringConstantNode(string.Empty));

            Assert.AreEqual("(\"TITLE\" IS NOT NULL)", CreatePolicyVisitor().Visit(node));
        }

        private static ODataASTVisitor CreatePolicyVisitor()
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(provider => provider.GetQueryBuilder()).Returns(new OracleQueryBuilder());
            string? backingColumn;
            metadata.Setup(provider => provider.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out backingColumn))
                .Callback(new TryGetBackingColumnCallback((string _, string _, out string? column) => column = "TITLE"))
                .Returns(true);
            metadata.As<ISqlLiteralSource>().Setup(source => source.LiteralRewriter).Returns(new OracleSqlLiteralRewriter());

            Mock<BaseSqlQueryStructure> structure = new(
                metadata.Object,
                Mock.Of<IAuthorizationResolver>(),
                null!,
                null,
                string.Empty,
                null,
                null,
                EntityActionOperation.None,
                false);
            structure.Object.EntityName = "Book";

            return new ODataASTVisitor(structure.Object, metadata.Object);
        }
    }
}
