// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Live check that #n selects the matching overload's metadata and that executing
    /// the entity runs that body. The suffix is not part of the PL/SQL call.
    /// Not run in the offline metadata pass.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleOverloadExecutionTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            await ResetDbStateAsync();
        }

        [TestMethod]
        public async Task ExplicitOverloadSuffix_SelectsMetadataAndRunsThatBody()
        {
            AssertOverload("TouchItemOne", "1", "p_id");
            AssertOverload("TouchItemTwo", "2", "p_id", "p_flag");

            await PostAndExpectOverload("TouchItemOne", new { p_id = 7101 }, 7101, 1);
            await PostAndExpectOverload("TouchItemTwo", new { p_id = 7102, p_flag = "X" }, 7102, 2);
        }

        private static void AssertOverload(string entityName, string overload, params string[] parameterNames)
        {
            DatabaseStoredProcedure procedure = (DatabaseStoredProcedure)_sqlMetadataProvider.EntityToDatabaseObject[entityName];
            Assert.AreEqual("touch_item", procedure.Name, ignoreCase: true);
            Assert.IsFalse(procedure.Name.Contains('#'));
            OracleObjectFacts.ProcedureFacts facts = OracleObjectFacts.Procedure(procedure);
            Assert.AreEqual("dab_overload_pkg", facts.PackageName, ignoreCase: true);
            Assert.AreEqual(overload, facts.Overload);

            HashSet<string> actual = new(
                procedure.StoredProcedureDefinition.Parameters.Keys,
                StringComparer.OrdinalIgnoreCase);
            CollectionAssert.AreEquivalent(parameterNames, actual.ToArray());
        }

        private static async Task PostAndExpectOverload(string entityName, object body, int id, int whichOverload)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "api/" + entityName)
            {
                Content = JsonContent.Create(body)
            };
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent,
                $"{entityName} returned {(int)response.StatusCode}: {responseBody}");

            string logged = await GetDatabaseResultAsync(
                "SELECT JSON_OBJECT('id' VALUE id, 'which_overload' VALUE which_overload) AS data " +
                "FROM dab_overload_log WHERE id = " + id);
            StringAssert.Contains(logged, "\"which_overload\":" + whichOverload);
        }
    }
}
