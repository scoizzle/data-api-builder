// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the Oracle stored procedure execution query builder
    /// (<see cref="OracleQueryBuilder.Build(SqlExecuteStructure)"/>).
    /// These validate that Oracle procedures are invoked from a PL/SQL anonymous block
    /// (never the SQL*Plus-only "EXEC" keyword), that bind references use the
    /// engine-generated "@paramN" values (not the SP argument names), and that a trailing
    /// REF CURSOR OUT bind is appended only when the procedure returns a result set.
    /// </summary>
    [TestClass]
    public class OracleQueryBuilderExecuteTests
    {
        private const string ENTITY_NAME = "Book";
        private const string SCHEMA_NAME = "SYSTEM";

        /// <summary>
        /// A procedure with an IN parameter and a SYS_REFCURSOR OUT parameter must be invoked
        /// with named association: <c>BEGIN "SYSTEM"."GET_BOOK_BY_ID"("id" => :param0, "cursor" => :dab_result); END;</c>.
        /// The IN bind references the engine parameter value, and the cursor is bound to its argument name.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteWithInputParamAndCursorUsesBlockAndRefCursor()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });
            spDef.Columns.Add("cursor", new ColumnDefinition { SystemType = typeof(IDataReader) });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "id", 1 } });

            string query = new OracleQueryBuilder().Build(structure);

            Assert.IsTrue(query.StartsWith("BEGIN ", StringComparison.Ordinal), $"Expected a PL/SQL block. Query: {query}");
            Assert.IsTrue(query.EndsWith("END;", StringComparison.Ordinal), $"Expected block to close with END;. Query: {query}");
            Assert.IsFalse(query.Contains("EXEC ", StringComparison.Ordinal), $"EXEC is SQL*Plus syntax. Query: {query}");
            Assert.IsTrue(
                query.Contains("\"SYSTEM\".\"GET_BOOK_BY_ID\"", StringComparison.Ordinal),
                $"Expected the procedure to be qualified by schema. Query: {query}");
            Assert.IsTrue(
                query.Contains(":param0", StringComparison.Ordinal),
                $"The IN bind MUST reference the engine parameter value. Query: {query}");
            Assert.IsFalse(
                query.Contains(":id", StringComparison.Ordinal),
                $"The IN bind MUST NOT reference the SP argument name. Query: {query}");
            Assert.AreEqual(
                $"BEGIN \"SYSTEM\".\"GET_BOOK_BY_ID\"(\"id\" => :param0, \"cursor\" => :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}); END;",
                query);
        }

        /// <summary>
        /// A procedure with only a SYS_REFCURSOR OUT parameter (no IN parameters) must be invoked
        /// as <c>BEGIN "SYSTEM"."GET_BOOKS"("cursor" => :dab_result); END;</c>.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteCursorOnlyProcedureUsesOnlyRefCursorBind()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Columns.Add("cursor", new ColumnDefinition { SystemType = typeof(IDataReader) });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?>());

            string query = new OracleQueryBuilder().Build(structure);

            Assert.AreEqual(
                $"BEGIN \"SYSTEM\".\"GET_BOOKS\"(\"cursor\" => :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}); END;",
                query);
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteScalarOutputDoesNotAppendRefCursorBind()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });
            spDef.Columns.Add("result", new ColumnDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "id", 1 } });

            string query = new OracleQueryBuilder().Build(structure);

            Assert.IsTrue(query.Contains("BEGIN", StringComparison.Ordinal), $"Expected a PL/SQL block. Query: {query}");
            Assert.IsFalse(
                query.Contains($":{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}", StringComparison.Ordinal),
                $"A scalar OUT parameter must not receive the REF CURSOR bind. Query: {query}");
        }

        /// <summary>
        /// A procedure that performs work but returns no result set and declares no parameters
        /// must be invoked as <c>BEGIN "SYSTEM"."DELETE_LAST_INSERTED_BOOK"; END;</c> (no
        /// parentheses, no REF CURSOR bind).
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteNoParamsNoResultSetOmitsParentheses()
        {
            StoredProcedureDefinition spDef = new();

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?>());

            string query = new OracleQueryBuilder().Build(structure);

            Assert.IsFalse(query.Contains($":{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}", StringComparison.Ordinal), $"Query: {query}");
            Assert.IsFalse(query.Contains("()", StringComparison.Ordinal), $"No-parentheses invocation expected. Query: {query}");
            Assert.IsTrue(query.Contains("; END;", StringComparison.Ordinal), $"Query: {query}");
        }

        /// <summary>
        /// A procedure with IN parameters but no result set must NOT append the REF CURSOR bind.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteInputParamsOnlyOmitsRefCursor()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("title", new ParameterDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String
            });
            spDef.Parameters.Add("publisher_id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "title", "A New Book" }, { "publisher_id", 1234 } });

            string query = new OracleQueryBuilder().Build(structure);

            Assert.AreEqual(
                "BEGIN \"SYSTEM\".\"INSERT_BOOK\"(\"title\" => :param0, \"publisher_id\" => :param1); END;",
                query);
        }

        /// <summary>
        /// A procedure inside a package (source "schema.package.subprogram") must be invoked with the
        /// package qualifier and named arguments:
        /// <c>BEGIN "SYSTEM"."PKG_TEST"."GET_BOOK_BY_ID"("id" => :param0, "cursor" => :dab_result); END;</c>.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecutePackagedProcedureQualifiesPackageAndAppendsRefCursor()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });
            spDef.Columns.Add("cursor", new ColumnDefinition { SystemType = typeof(IDataReader) });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "id", 1 } },
                packageName: "pkg_test",
                name: "get_book_by_id");

            string query = new OracleQueryBuilder().Build(structure);

            Assert.AreEqual(
                $"BEGIN \"SYSTEM\".\"PKG_TEST\".\"GET_BOOK_BY_ID\"(\"id\" => :param0, \"cursor\" => :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}); END;",
                query);
        }

        /// <summary>
        /// A cursor that is not the last argument must be bound by name. Supplying only a later
        /// optional argument must not shift that value onto the cursor.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteBindsRefCursorByNameWhenItIsNotLast()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("p_id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });
            spDef.Columns.Add("p_cur", new ColumnDefinition { SystemType = typeof(IDataReader) });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "p_id", 7 } },
                name: "get_by_cursor_first");

            string query = new OracleQueryBuilder().Build(structure);

            Assert.AreEqual(
                $"BEGIN \"SYSTEM\".\"GET_BY_CURSOR_FIRST\"(\"p_id\" => :param0, \"p_cur\" => :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}); END;",
                query);
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleOverloadSelectionKeepsOnlyTheMatchingSignature()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_id", "NUMBER", "IN", 1, "1"),
                new("p_cur", "REF CURSOR", "OUT", 2, "1"),
                new("p_name", "VARCHAR2", "IN", 1, "2"),
                new("p_id", "NUMBER", "IN", 2, "2"),
            };

            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(rows, configuredParameterNames: new[] { "p_name" });

            Assert.AreEqual(2, selected.Count);
            Assert.IsTrue(selected.All(row => row.Overload == "2"));
        }

        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleOverloadSelectionUsesLowestNumberWhenConfigDoesNotMatch()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_b", "NUMBER", "IN", 1, "2"),
                new("p_a", "NUMBER", "IN", 1, "1"),
            };

            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(rows, configuredParameterNames: null);

            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual("p_a", selected[0].Name);
            Assert.AreEqual("1", selected[0].Overload);
        }

        /// <summary>
        /// A function inside a package whose RETURN type is a REF CURSOR must be assigned into the
        /// shared cursor bind: <c>BEGIN :dab_result := "SYSTEM"."PKG_TEST"."GET_BOOKS"(:param0); END;</c>.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecutePackagedFunctionReturningCursorAssignsRefCursor()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });
            spDef.Columns.Add("get_books", new ColumnDefinition { SystemType = typeof(IDataReader) });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "id", 1 } },
                packageName: "pkg_test",
                isFunction: true,
                name: "get_books");

            string query = new OracleQueryBuilder().Build(structure);

            Assert.AreEqual(
                $"BEGIN :{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME} := \"SYSTEM\".\"PKG_TEST\".\"GET_BOOKS\"(\"id\" => :param0); END;",
                query);
        }

        /// <summary>
        /// A standalone scalar function must be invoked as <c>SELECT "SYSTEM"."FN_GET_COUNT"() AS
        /// "value" FROM DUAL;</c> so its value surfaces as a single row in the reader.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecuteScalarFunctionSelectsFromDual()
        {
            StoredProcedureDefinition spDef = new();

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?>(),
                isFunction: true,
                name: "fn_get_count");

            string query = new OracleQueryBuilder().Build(structure);

            Assert.IsTrue(
                query.Contains("SELECT \"SYSTEM\".\"FN_GET_COUNT\"() AS \"value\" FROM DUAL", StringComparison.Ordinal),
                $"Query: {query}");
            Assert.IsFalse(
                query.Contains($":{OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME}", StringComparison.Ordinal),
                $"A scalar function MUST NOT use the REF CURSOR bind. Query: {query}");
        }

        /// <summary>
        /// A packaged scalar function must be invoked as
        /// <c>SELECT "SYSTEM"."PKG_TEST"."GET_COUNT"(:param0) AS "value" FROM DUAL;</c>.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.ORACLE)]
        public void OracleExecutePackagedScalarFunctionSelectsFromDual()
        {
            StoredProcedureDefinition spDef = new();
            spDef.Parameters.Add("id", new ParameterDefinition
            {
                SystemType = typeof(decimal),
                DbType = DbType.Decimal
            });

            SqlExecuteStructure structure = CreateExecuteStructure(
                spDef,
                requestParams: new Dictionary<string, object?> { { "id", 1 } },
                packageName: "pkg_test",
                isFunction: true,
                name: "get_count");

            string query = new OracleQueryBuilder().Build(structure);

            Assert.IsTrue(
                query.Contains("SELECT \"SYSTEM\".\"PKG_TEST\".\"GET_COUNT\"(\"id\" => :param0) AS \"value\" FROM DUAL", StringComparison.Ordinal),
                $"Query: {query}");
        }

        /// <summary>
        /// Builds a minimal <see cref="SqlExecuteStructure"/> backed by a mocked metadata provider
        /// so no live database is required.
        /// </summary>
        private static SqlExecuteStructure CreateExecuteStructure(
            StoredProcedureDefinition spDefinition,
            IDictionary<string, object?> requestParams,
            string? packageName = null,
            bool isFunction = false,
            string? name = null)
        {
            DatabaseStoredProcedure databaseObject = new(SCHEMA_NAME, name ?? spDefinitionName(spDefinition))
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = spDefinition,
                PackageName = packageName,
                IsFunction = isFunction
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { ENTITY_NAME, databaseObject } });
            metadataProvider.Setup(x => x.GetStoredProcedureDefinition(ENTITY_NAME)).Returns(spDefinition);
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(spDefinition);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.Oracle);

            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver
                .Setup(x => x.ResolveDBPolicy(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<EntityActionOperation>(),
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            GQLFilterParser gQLFilterParser = new(runtimeConfigProvider, metadataProviderFactory.Object);

            return new SqlExecuteStructure(
                entityName: ENTITY_NAME,
                sqlMetadataProvider: metadataProvider.Object,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: gQLFilterParser,
                requestParams: requestParams);
        }

        /// <summary>
        /// Derives a deterministic stored-procedure name from the parameter/result-set shape so the
        /// assertions can reference a known object name.
        /// </summary>
        private static string spDefinitionName(StoredProcedureDefinition spDefinition)
        {
            if (spDefinition.Parameters.Count == 0 && spDefinition.Columns.Count == 0)
            {
                return "DELETE_LAST_INSERTED_BOOK";
            }

            if (spDefinition.Columns.Count > 0)
            {
                return spDefinition.Parameters.Count > 0 ? "GET_BOOK_BY_ID" : "GET_BOOKS";
            }

            return "INSERT_BOOK";
        }
    }
}
