// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class OracleEmptyStringPredicateTests
    {
        [TestMethod]
        public void RewriteEmptyStringEqualityToIsNull()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(
                "SELECT * FROM \"BOOKS\" WHERE \"TITLE\" = :param0",
                parameters);

            Assert.AreEqual("SELECT * FROM \"BOOKS\" WHERE \"TITLE\" IS NULL", rewritten);
        }

        [DataTestMethod]
        [DataRow("!=", DisplayName = "!=")]
        [DataRow("<>", DisplayName = "<>")]
        public void RewriteEmptyStringInequalityToIsNotNull(string comparison)
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(
                $"SELECT * FROM \"BOOKS\" WHERE \"TITLE\" {comparison} :param0",
                parameters);

            Assert.AreEqual("SELECT * FROM \"BOOKS\" WHERE \"TITLE\" IS NOT NULL", rewritten);
        }

        [TestMethod]
        public void LeavesNonEmptyStringComparisonAlone()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam("book") }
            };

            const string sql = "SELECT * FROM \"BOOKS\" WHERE \"TITLE\" = :param0";

            Assert.AreEqual(sql, OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters));
        }

        [TestMethod]
        public void DoesNotRewriteGreaterThanOrEqual()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            const string sql = "SELECT * FROM \"BOOKS\" WHERE \"TITLE\" >= :param0";

            Assert.AreEqual(sql, OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters));
        }

        [TestMethod]
        public void RewritesEqualityButKeepsLikeReference()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(
                "SELECT * FROM \"BOOKS\" WHERE \"TITLE\" = :param0 OR \"TITLE\" LIKE :param0",
                parameters);

            Assert.AreEqual(
                "SELECT * FROM \"BOOKS\" WHERE \"TITLE\" IS NULL OR \"TITLE\" LIKE :param0",
                rewritten);
        }

        [TestMethod]
        public void DoesNotRewriteUpdateSetAssignment()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) },
                { "@param1", new DbConnectionParam(string.Empty) }
            };

            // The exact shape OracleQueryBuilder emits for an update: a comment-prefixed
            // anonymous block, not a bare UPDATE statement.
            const string sql = "/* DAB_ORACLE_OUTPUT_TYPES:dab_out_0=Varchar2:80 */ " +
                "BEGIN UPDATE \"DBO\".\"BOOKS\" SET \"TITLE\" = :param0 WHERE \"PUBLISHER_ID\" = :param1 " +
                "RETURNING \"ID\" INTO :dab_out_0; " +
                "IF SQL%ROWCOUNT > 0 THEN OPEN :dab_result FOR SELECT :dab_out_0 FROM DUAL; " +
                "ELSE OPEN :dab_result FOR SELECT :dab_out_0 FROM DUAL WHERE 1 = 0; END IF; END;";

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters);

            // A SET assignment must keep the equality: Oracle rejects `SET "TITLE" IS NULL`
            // with ORA-00936. Only the WHERE predicate may be rewritten.
            StringAssert.Contains(rewritten, "SET \"TITLE\" = :param0");
            StringAssert.Contains(rewritten, "WHERE \"PUBLISHER_ID\" IS NULL");
            StringAssert.Contains(rewritten, "WHERE 1 = 0");
            Assert.IsFalse(rewritten.Contains("SET \"TITLE\" IS NULL", StringComparison.Ordinal), rewritten);
        }

        [TestMethod]
        public void DoesNotRewriteUpsertSetAssignment()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) },
                { "@param1", new DbConnectionParam(string.Empty) },
                { "@param2", new DbConnectionParam(string.Empty) }
            };

            // The exact shape OracleQueryBuilder emits for an upsert: a declaration section
            // followed by an update, an existence probe, and a gated insert.
            const string sql = "/* DAB_ORACLE_OUTPUT_TYPES:dab_out_0=Varchar2:80 */ " +
                "DECLARE v_dab_upsert_count NUMBER; " +
                "BEGIN UPDATE \"DBO\".\"BOOKS\" SET \"TITLE\" = :param0 WHERE \"ID\" = :param1 " +
                "RETURNING \"ID\" INTO :dab_out_0; " +
                "IF SQL%ROWCOUNT > 0 THEN OPEN :dab_result FOR SELECT :dab_out_0 FROM DUAL; " +
                "ELSE SELECT COUNT(*) INTO v_dab_upsert_count FROM \"DBO\".\"BOOKS\" WHERE \"ID\" = :param1; " +
                "IF v_dab_upsert_count > 0 THEN OPEN :dab_result FOR SELECT :dab_out_0 FROM DUAL WHERE 1 = 0; " +
                "ELSE INSERT INTO \"DBO\".\"BOOKS\" (\"TITLE\") VALUES (:param2) RETURNING \"ID\" INTO :dab_out_0; " +
                "END IF; END IF; END;";

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters);

            StringAssert.Contains(rewritten, "SET \"TITLE\" = :param0");
            StringAssert.Contains(rewritten, "WHERE \"ID\" IS NULL");
            StringAssert.Contains(rewritten, "VALUES (:param2)");
        }

        [TestMethod]
        public void DoesNotRewritePlSqlAssignmentOperator()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            const string sql = "DECLARE v_name VARCHAR2(50); BEGIN v_name := :param0; END;";

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters);

            Assert.AreEqual(sql, rewritten);
        }

        [TestMethod]
        public void DoesNotSplitInsideQuotedIdentifier()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) },
                { "@param1", new DbConnectionParam(string.Empty) }
            };

            // A quoted identifier may legally contain ';'. It must not be mistaken for a
            // statement boundary, or the SET assignment would be rewritten again.
            const string sql = "DECLARE v_count NUMBER; " +
                "BEGIN UPDATE \"DBO\".\"BOOKS\" SET \"A;B\" = :param0 WHERE \"ID\" = :param1; END;";

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters);

            StringAssert.Contains(rewritten, "SET \"A;B\" = :param0");
            StringAssert.Contains(rewritten, "WHERE \"ID\" IS NULL");
        }

        [TestMethod]
        public void RewritesCreatePolicyProbeInInsertBlock()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) },
                { "@param1", new DbConnectionParam("title") }
            };

            const string sql = "DECLARE v_dab_insert_count NUMBER; " +
                "BEGIN SELECT COUNT(*) INTO v_dab_insert_count FROM (SELECT :param1 AS \"TITLE\" FROM DUAL) WHERE \"TITLE\" = :param0; " +
                "END;";

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(sql, parameters);

            StringAssert.Contains(rewritten, "WHERE \"TITLE\" IS NULL");
        }

        [TestMethod]
        public void RewritesDeleteWhereClause()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(
                "DELETE FROM \"DBO\".\"BOOKS\" WHERE \"TITLE\" = :param0",
                parameters);

            Assert.AreEqual("DELETE FROM \"DBO\".\"BOOKS\" WHERE \"TITLE\" IS NULL", rewritten);
        }

        [TestMethod]
        public void RewritesSelectSubqueryPredicate()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@param0", new DbConnectionParam(string.Empty) }
            };

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(
                "SELECT * FROM \"DBO\".\"BOOKS\" WHERE EXISTS (SELECT 1 FROM \"DBO\".\"SALES\" WHERE \"NOTE\" = :param0)",
                parameters);

            Assert.AreEqual(
                "SELECT * FROM \"DBO\".\"BOOKS\" WHERE EXISTS (SELECT 1 FROM \"DBO\".\"SALES\" WHERE \"NOTE\" IS NULL)",
                rewritten);
        }

        [TestMethod]
        public void MatchesParameterNameCaseInsensitively()
        {
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@Param0", new DbConnectionParam(string.Empty) }
            };

            string rewritten = OracleQueryExecutor.RewriteEmptyStringEqualityBinds(
                "SELECT * FROM \"BOOKS\" WHERE \"TITLE\" = :param0",
                parameters);

            Assert.AreEqual("SELECT * FROM \"BOOKS\" WHERE \"TITLE\" IS NULL", rewritten);
        }
    }
}
