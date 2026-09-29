// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Integration tests for MCP read filters against a real Oracle database.
    /// stocks_price seed data contains an instant value of 2023-08-21 15:11:04 (UTC), and
    /// type_table's date_types column contains 1999-01-08.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class ReadRecordsToolOracleIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        /// <summary>
        /// An ISO 8601 UTC filter must match the stored (UTC) TIMESTAMP value. This is the case
        /// that previously failed because ODP.NET bound the DateTimeOffset as TIMESTAMP WITH TIME
        /// ZONE and Oracle compared it using the session time zone.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_IsoUtcDateTimeFilter_ReturnsMatchingRow()
        {
            CallToolResult result = await ExecuteReadAsync(
                entity: "stocks_price",
                select: "categoryid,pieceid,instant",
                filter: "instant eq 2023-08-21T15:11:04Z");

            AssertSuccess(result, "ISO UTC date filter should succeed.");

            JsonElement records = GetRecordsArray(ParseResultRoot(result));
            Assert.AreEqual(1, records.GetArrayLength(), "Expected exactly one matching stocks_price row.");
            Assert.AreEqual(2, records[0].GetProperty("categoryid").GetInt32());
        }

        /// <summary>
        /// A date-only literal (Microsoft.OData.Edm.Date) must bind against an Oracle DATE column
        /// instead of failing with an ODP.NET argument error.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_DateOnlyLiteral_ReturnsMatchingRows()
        {
            CallToolResult result = await ExecuteReadAsync(
                entity: "SupportedType",
                select: "typeid,date_types",
                filter: "date_types eq 1999-01-08");

            AssertSuccess(result, "Date-only filter should succeed.");

            JsonElement records = GetRecordsArray(ParseResultRoot(result));
            Assert.AreEqual(2, records.GetArrayLength(), "Expected two type_table rows with date_types = 1999-01-08.");
        }

        /// <summary>
        /// Invalid filters must still return an error rather than being silently rewritten.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_InvalidDateFilter_ReturnsError()
        {
            CallToolResult result = await ExecuteReadAsync(
                entity: "stocks_price",
                select: "categoryid,pieceid,instant",
                filter: "instant eq 'not-a-date'");

            AssertError(result);
        }

        private static async Task<CallToolResult> ExecuteReadAsync(
            string entity,
            string? select = null,
            string? filter = null)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            ReadRecordsTool tool = new();

            var args = new Dictionary<string, object?> { { "entity", entity } };
            if (select != null)
            {
                args["select"] = select;
            }

            if (filter != null)
            {
                args["filter"] = filter;
            }

            return await ExecuteToolAsync(tool, serviceProvider, args);
        }
    }
}
