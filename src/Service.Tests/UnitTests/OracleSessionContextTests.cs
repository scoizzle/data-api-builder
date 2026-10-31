// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for Oracle session-context (claims forwarding) command generation and gating.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleSessionContextTests
    {
        [TestMethod]
        public void BuildSessionContextBlock_NoClaims_ClearsOnly()
        {
            (string commandText, List<(string Name, string Value)> parameters) =
                OracleQueryExecutor.BuildSessionContextBlock(new Dictionary<string, string>());

            Assert.AreEqual("BEGIN DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS; END;", commandText);
            Assert.AreEqual(0, parameters.Count);
        }

        [TestMethod]
        public void BuildSessionContextBlock_BindsEveryClaimByNameAndValue()
        {
            KeyValuePair<string, string>[] claims =
            [
                new("roles", "authenticated"),
                new("groups", "[\"group1\",\"group2\"]"),
            ];

            (string commandText, List<(string Name, string Value)> parameters) =
                OracleQueryExecutor.BuildSessionContextBlock(claims);

            StringAssert.Contains(commandText, "DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS;");
            StringAssert.Contains(commandText, "DAB_SESSION_CONTEXT_PKG.SET_CLAIM(:dab_claim_key0, :dab_claim_value0);");
            StringAssert.Contains(commandText, "DAB_SESSION_CONTEXT_PKG.SET_CLAIM(:dab_claim_key1, :dab_claim_value1);");
            Assert.AreEqual(4, parameters.Count);
            Assert.AreEqual("dab_claim_key0", parameters[0].Name);
            Assert.AreEqual("roles", parameters[0].Value);
            Assert.AreEqual("dab_claim_value0", parameters[1].Name);
            Assert.AreEqual("authenticated", parameters[1].Value);
            Assert.AreEqual("dab_claim_key1", parameters[2].Name);
            Assert.AreEqual("groups", parameters[2].Value);
            Assert.AreEqual("dab_claim_value1", parameters[3].Name);
            Assert.AreEqual("[\"group1\",\"group2\"]", parameters[3].Value);
        }

        [TestMethod]
        public void BuildSessionContextBlock_InjectionLookingValues_StayOutOfCommandText()
        {
            const string maliciousValue = "x'; DELETE FROM BOOKS;--";

            KeyValuePair<string, string>[] claims = [new("groups", maliciousValue)];

            (string commandText, List<(string Name, string Value)> parameters) =
                OracleQueryExecutor.BuildSessionContextBlock(claims);

            Assert.IsFalse(
                commandText.Contains(maliciousValue),
                "Claim values must travel only as bind parameters, never in the command text.");
            Assert.AreEqual(maliciousValue, parameters[1].Value);
        }

        [TestMethod]
        public void PrepareDbCommand_SessionContextDisabled_DoesNotConnectOrExecute()
        {
            RuntimeConfigProvider provider = CreateOracleProvider(new Dictionary<string, object?>());
            OracleQueryExecutor executor = new(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);

            using OracleConnection conn = new("User Id=x;Password=y;Data Source=localhost:1521/x");
            DefaultHttpContext httpContext = new()
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("groups", "group1")], "TestAuth"))
            };

            using DbCommand cmd = executor.PrepareDbCommand(
                conn,
                "SELECT 1 FROM DUAL",
                new Dictionary<string, DbConnectionParam>(),
                httpContext,
                provider.GetConfig().DefaultDataSourceName);

            Assert.IsNotNull(cmd);
            Assert.AreEqual(ConnectionState.Closed, conn.State);
        }

        [TestMethod]
        public void OracleOptions_SetSessionContext_DefaultsToDisabled()
        {
            OracleOptions defaults = new();
            Assert.IsFalse(defaults.SetSessionContext);

            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", new() { { "set-session-context", true } }),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>()));

            Assert.IsTrue(config.DataSource!.GetTypedOptions<OracleOptions>()!.SetSessionContext);
        }

        private static RuntimeConfigProvider CreateOracleProvider(Dictionary<string, object?> options)
        {
            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", options),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>()));

            return TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        }
    }
}
