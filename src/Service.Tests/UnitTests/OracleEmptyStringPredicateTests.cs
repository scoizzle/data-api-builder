// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
