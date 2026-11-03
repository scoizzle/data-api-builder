// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// The empty-string-as-NULL rule belongs to the predicate producers, not to the executor.
    /// These tests assert the predicate shape each engine's rewriter produces, so a change that
    /// moves the rule back into SQL text rewriting fails here.
    /// </summary>
    [TestClass]
    public class OracleEmptyStringPredicateTests
    {
        private static readonly Column TitleColumn = new("dbo", "books", "title");

        private static SqlLiteralRewriter RewriterFor(DatabaseType engine)
        {
            return engine is DatabaseType.Oracle
                ? new OracleSqlLiteralRewriter()
                : SqlLiteralRewriter.Shared;
        }

        private static Predicate Parse(
            DatabaseType engine,
            string operation,
            IValueNode value,
            out object? literal)
        {
            return Parse(engine, operation, value, out literal, out _, nullVariableName: null);
        }

        private static Predicate Parse(
            DatabaseType engine,
            string operation,
            IValueNode value,
            out object? literal,
            out List<object?> literals,
            string? nullVariableName)
        {
            List<object?> captured = new();
            Predicate predicate = FieldFilterParser.Parse(
                CreateMiddlewareContext(nullVariableName),
                CreateFilterArgumentSchema(),
                TitleColumn,
                new List<ObjectFieldNode> { new(operation, value) },
                (literalValue, _, _) =>
                {
                    captured.Add(literalValue);
                    return "?";
                },
                RewriterFor(engine));

            literals = captured;
            literal = captured.Count == 0 ? null : captured[^1];
            return predicate;
        }

        [DataTestMethod]
        [DataRow(DatabaseType.Oracle, PredicateOperation.IS, DisplayName = "Oracle eq '' is a NULL test")]
        [DataRow(DatabaseType.MSSQL, PredicateOperation.Equal, DisplayName = "MSSQL eq '' stays equality")]
        [DataRow(DatabaseType.PostgreSQL, PredicateOperation.Equal, DisplayName = "PostgreSQL eq '' stays equality")]
        [DataRow(DatabaseType.MySQL, PredicateOperation.Equal, DisplayName = "MySQL eq '' stays equality")]
        public void EqEmptyString_MatchesEngineRule(DatabaseType engine, PredicateOperation expectedOperation)
        {
            Predicate predicate = Parse(engine, "eq", new StringValueNode(string.Empty), out object? literal);

            Assert.AreEqual(expectedOperation, predicate.Op);
            if (expectedOperation is PredicateOperation.IS)
            {
                Assert.AreEqual(GQLFilterParser.NullStringValue, predicate.Right.AsString());
                Assert.IsNull(literal, "An IS NULL predicate must not bind the empty string.");
            }
            else
            {
                Assert.AreEqual(string.Empty, literal);
                Assert.AreEqual("?", predicate.Right.AsString());
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.Oracle, PredicateOperation.IS_NOT, DisplayName = "Oracle neq '' is a NOT NULL test")]
        [DataRow(DatabaseType.MSSQL, PredicateOperation.NotEqual, DisplayName = "MSSQL neq '' stays inequality")]
        [DataRow(DatabaseType.PostgreSQL, PredicateOperation.NotEqual, DisplayName = "PostgreSQL neq '' stays inequality")]
        [DataRow(DatabaseType.MySQL, PredicateOperation.NotEqual, DisplayName = "MySQL neq '' stays inequality")]
        public void NeqEmptyString_MatchesEngineRule(DatabaseType engine, PredicateOperation expectedOperation)
        {
            Predicate predicate = Parse(engine, "neq", new StringValueNode(string.Empty), out object? literal);

            Assert.AreEqual(expectedOperation, predicate.Op);
            if (expectedOperation is PredicateOperation.IS_NOT)
            {
                Assert.AreEqual(GQLFilterParser.NullStringValue, predicate.Right.AsString());
                Assert.IsNull(literal);
            }
            else
            {
                Assert.AreEqual(string.Empty, literal);
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.Oracle, PredicateOperation.IS, DisplayName = "Oracle in [''] is a NULL test")]
        [DataRow(DatabaseType.MSSQL, PredicateOperation.IN, DisplayName = "MSSQL in [''] stays IN")]
        [DataRow(DatabaseType.PostgreSQL, PredicateOperation.IN, DisplayName = "PostgreSQL in [''] stays IN")]
        [DataRow(DatabaseType.MySQL, PredicateOperation.IN, DisplayName = "MySQL in [''] stays IN")]
        public void InEmptyStringOnly_MatchesEngineRule(DatabaseType engine, PredicateOperation expectedOperation)
        {
            ListValueNode values = new(new List<IValueNode> { new StringValueNode(string.Empty) });

            Predicate predicate = Parse(engine, "in", values, out object? literal);

            Assert.AreEqual(expectedOperation, predicate.Op);
            if (expectedOperation is PredicateOperation.IS)
            {
                Assert.AreEqual(GQLFilterParser.NullStringValue, predicate.Right.AsString());
                Assert.IsNull(literal, "The empty IN element must not bind.");
            }
            else
            {
                Assert.AreEqual(string.Empty, literal);
                Assert.AreEqual("(?)", predicate.Right.AsString());
            }
        }

        [TestMethod]
        public void InEmptyStringAndValue_OnOracle_IsInOrNull()
        {
            ListValueNode values = new(new List<IValueNode>
            {
                new StringValueNode(string.Empty),
                new StringValueNode("a"),
            });

            Predicate predicate = Parse(DatabaseType.Oracle, "in", values, out object? literal);

            Assert.AreEqual(PredicateOperation.OR, predicate.Op);
            Assert.IsTrue(predicate.AddParenthesis, "The OR must be parenthesized before it is ANDed with the rest of the filter.");

            Predicate inPredicate = predicate.Left!.AsPredicate()!;
            Assert.AreEqual(PredicateOperation.IN, inPredicate.Op);
            Assert.AreEqual("(?)", inPredicate.Right.AsString());
            Assert.AreEqual("a", literal, "Only the non-empty element is bound.");

            Predicate nullPredicate = predicate.Right!.AsPredicate()!;
            Assert.AreEqual(PredicateOperation.IS, nullPredicate.Op);
            Assert.AreEqual(GQLFilterParser.NullStringValue, nullPredicate.Right.AsString());
        }

        [TestMethod]
        public void InEmptyStringAndValue_OnNonOracle_StaysIn()
        {
            ListValueNode values = new(new List<IValueNode>
            {
                new StringValueNode(string.Empty),
                new StringValueNode("a"),
            });

            Predicate predicate = Parse(DatabaseType.MSSQL, "in", values, out _, out List<object?> literals, nullVariableName: null);

            Assert.AreEqual(PredicateOperation.IN, predicate.Op);
            Assert.AreEqual("(?, ?)", predicate.Right.AsString());
            CollectionAssert.AreEqual(
                new object?[] { string.Empty, "a" },
                literals,
                "Both elements are bound on an engine that keeps '' distinct from NULL.");
        }

        [TestMethod]
        public void InEmptyStringAndNullVariable_OnOracle_IsNull()
        {
            ListValueNode values = new(new List<IValueNode>
            {
                new StringValueNode(string.Empty),
                new VariableNode("v"),
            });

            Predicate predicate = Parse(DatabaseType.Oracle, "in", values, out object? literal, out _, nullVariableName: "v");

            Assert.AreEqual(PredicateOperation.IS, predicate.Op);
            Assert.AreEqual(GQLFilterParser.NullStringValue, predicate.Right.AsString());
            Assert.IsNull(literal, "The null variable and the empty element must not bind.");
        }

        [TestMethod]
        public void InNonEmptyValue_StaysIn()
        {
            ListValueNode values = new(new List<IValueNode> { new StringValueNode("a") });

            Predicate predicate = Parse(DatabaseType.Oracle, "in", values, out object? literal);

            Assert.AreEqual(PredicateOperation.IN, predicate.Op);
            Assert.AreEqual("a", literal);
        }

        [TestMethod]
        public void EqNonEmptyString_StaysEqual()
        {
            Predicate predicate = Parse(DatabaseType.Oracle, "eq", new StringValueNode("a"), out object? literal);

            Assert.AreEqual(PredicateOperation.Equal, predicate.Op);
            Assert.AreEqual("a", literal);
        }

        [TestMethod]
        public void NonEqualityOperator_IsNotRewritten()
        {
            Predicate predicate = Parse(DatabaseType.Oracle, "gte", new StringValueNode(string.Empty), out object? literal);

            Assert.AreEqual(PredicateOperation.GreaterThanOrEqual, predicate.Op);
            Assert.AreEqual(string.Empty, literal);
        }

        private static IMiddlewareContext CreateMiddlewareContext(string? nullVariableName = null)
        {
            Mock<IVariableValueCollection> variables = new();
            if (nullVariableName is not null)
            {
                variables.Setup(v => v.GetValue<IValueNode>(nullVariableName)).Returns(NullValueNode.Default);
            }

            Mock<IMiddlewareContext> context = new();
            context.SetupGet(x => x.Variables).Returns(variables.Object);
            return context.Object;
        }

        private static IInputValueDefinition CreateFilterArgumentSchema()
        {
            return SchemaBuilder.New()
                .AddDocumentFromString("""
                    type Query {
                      test(filter: TestFilterInput): String
                    }

                    input TestFilterInput {
                      eq: String
                      neq: String
                      gte: String
                      in: [String]
                    }
                    """)
                .AddResolver("Query", "test", _ => string.Empty)
                .Create()
                .QueryType.Fields["test"].Arguments["filter"];
        }
    }
}
