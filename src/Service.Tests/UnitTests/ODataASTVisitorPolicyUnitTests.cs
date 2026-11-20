// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

            bool matched = new OracleSqlLiteralRewriter().TryRewriteEmptyStringComparison(
                BinaryOperatorKind.Equal,
                propertyNode,
                emptyStringNode,
                out SingleValuePropertyAccessNode? resolvedProperty,
                out bool isNullPredicate);

            Assert.IsTrue(matched);
            Assert.IsTrue(isNullPredicate);
            Assert.AreSame(propertyNode, resolvedProperty);
        }

        [TestMethod]
        public void OracleEmptyStringInequality_RewritesToIsNotNull()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = new OracleSqlLiteralRewriter().TryRewriteEmptyStringComparison(
                BinaryOperatorKind.NotEqual,
                propertyNode,
                emptyStringNode,
                out SingleValuePropertyAccessNode? resolvedProperty,
                out bool isNullPredicate);

            Assert.IsTrue(matched);
            Assert.IsFalse(isNullPredicate);
            Assert.AreSame(propertyNode, resolvedProperty);
        }

        [TestMethod]
        public void ConstantOnLeftOracleEmptyStringEquality_RewritesToIsNull()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode emptyStringNode = CreateStringConstantNode(string.Empty);

            bool matched = new OracleSqlLiteralRewriter().TryRewriteEmptyStringComparison(
                BinaryOperatorKind.Equal,
                emptyStringNode,
                propertyNode,
                out SingleValuePropertyAccessNode? resolvedProperty,
                out bool isNullPredicate);

            Assert.IsTrue(matched);
            Assert.IsTrue(isNullPredicate);
            Assert.AreSame(propertyNode, resolvedProperty);
        }

        [TestMethod]
        public void NonEmptyString_IsNotRewritten()
        {
            SingleValuePropertyAccessNode propertyNode = CreatePropertyNode("categoryName");
            ConstantNode nonEmptyStringNode = CreateStringConstantNode("SciFi");

            bool matched = new OracleSqlLiteralRewriter().TryRewriteEmptyStringComparison(
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

            bool matched = SqlLiteralRewriter.Shared.TryRewriteEmptyStringComparison(
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

            bool matched = new OracleSqlLiteralRewriter().TryRewriteEmptyStringComparison(
                BinaryOperatorKind.GreaterThan,
                propertyNode,
                emptyStringNode,
                out _,
                out _);

            Assert.IsFalse(matched);
        }
    }
}
