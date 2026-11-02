// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Integration tests for AggregateRecordsTool against a real Oracle database.
    /// The books table is seeded with 21 rows. publisher_id 1234 has 11 books and
    /// publisher_id 2324 has 3, so both survive a count having gt 2.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class AggregateRecordsToolOracleIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        /// <summary>
        /// COUNT(*) with no groupby is a scalar aggregate. Oracle returns the count as a
        /// JSON number that may be an integer or a decimal.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_CountWithoutGroupBy_ReturnsSeedCount()
        {
            CallToolResult result = await ExecuteAggregateAsync("Book", "count", "*");

            AssertSuccess(result, "COUNT(*) should succeed.");

            JsonElement root = ParseResultRoot(result);
            Assert.AreEqual("Book", root.GetProperty("entity").GetString());
            JsonElement rows = root.GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, rows.ValueKind);
            Assert.AreEqual(1, rows.GetArrayLength());

            decimal count = ReadNumber(rows[0].GetProperty("count"));
            Assert.IsTrue(count >= 21m, $"Expected at least 21 books, got {count}.");
        }

        /// <summary>
        /// Grouped COUNT with HAVING keeps only groups whose count is greater than 2
        /// and returns those counts as numbers.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_GroupedCountWithHaving_FiltersGroups()
        {
            CallToolResult result = await ExecuteAggregateAsync(
                "Book",
                "count",
                "*",
                groupby: new[] { "publisher_id" },
                having: new Dictionary<string, object?> { ["gt"] = 2 },
                orderby: "desc");

            AssertSuccess(result, "Grouped COUNT with HAVING should succeed.");

            JsonElement rows = ParseResultRoot(result).GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, rows.ValueKind);
            Assert.IsTrue(rows.GetArrayLength() >= 2, $"Expected at least the two seed groups, got {rows}.");

            decimal largest = ReadNumber(FindGroup(rows, 1234m).GetProperty("count"));
            decimal next = ReadNumber(FindGroup(rows, 2324m).GetProperty("count"));
            Assert.IsTrue(largest >= 11m, $"publisher_id 1234 should have at least 11 books, got {largest}.");
            Assert.IsTrue(next >= 3m, $"publisher_id 2324 should have at least 3 books, got {next}.");
            Assert.IsTrue(largest >= next, "Descending order should put the larger count first.");

            decimal? previous = null;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                decimal count = ReadNumber(row.GetProperty("count"));
                Assert.IsTrue(count > 2m, $"HAVING gt 2 returned a group with count {count}. Row: {row}.");
                if (previous.HasValue)
                {
                    Assert.IsTrue(previous.Value >= count, $"Counts should be descending. Rows: {rows}.");
                }

                previous = count;
            }
        }

        /// <summary>
        /// first and after page the grouped aggregate. The cursor comes from the
        /// previous response, and each page is ordered by the aggregate.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_GroupedCount_FirstAndAfter_PagesGroups()
        {
            Dictionary<string, object?> having = new() { ["gt"] = 2 };

            CallToolResult firstPage = await ExecuteAggregateAsync(
                "Book",
                "count",
                "*",
                groupby: new[] { "publisher_id" },
                having: having,
                orderby: "desc",
                first: 1);

            AssertSuccess(firstPage, "First page of grouped COUNT should succeed.");

            JsonElement firstResult = ParseResultRoot(firstPage).GetProperty("result");
            Assert.AreEqual(JsonValueKind.Object, firstResult.ValueKind);
            JsonElement firstItems = firstResult.GetProperty("items");
            Assert.AreEqual(1, firstItems.GetArrayLength());
            Assert.IsTrue(firstResult.GetProperty("hasNextPage").GetBoolean());

            decimal firstCount = ReadNumber(firstItems[0].GetProperty("count"));
            decimal firstPublisher = ReadNumber(firstItems[0].GetProperty("publisher_id"));
            string? cursor = firstResult.GetProperty("endCursor").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(cursor));

            CallToolResult secondPage = await ExecuteAggregateAsync(
                "Book",
                "count",
                "*",
                groupby: new[] { "publisher_id" },
                having: having,
                orderby: "desc",
                first: 1,
                after: cursor);

            AssertSuccess(secondPage, "Second page of grouped COUNT should succeed.");

            JsonElement secondItems = ParseResultRoot(secondPage).GetProperty("result").GetProperty("items");
            Assert.AreEqual(1, secondItems.GetArrayLength());
            decimal secondCount = ReadNumber(secondItems[0].GetProperty("count"));
            decimal secondPublisher = ReadNumber(secondItems[0].GetProperty("publisher_id"));
            Assert.AreNotEqual(firstPublisher, secondPublisher);
            Assert.IsTrue(firstCount >= secondCount, $"Page order should be descending. First {firstCount}, second {secondCount}.");
            Assert.IsTrue(secondCount > 2m, $"Second page should still satisfy HAVING. Count was {secondCount}.");
        }

        private static decimal ReadNumber(JsonElement element)
        {
            Assert.AreEqual(JsonValueKind.Number, element.ValueKind, $"Expected a JSON number but found {element}.");
            return element.GetDecimal();
        }

        private static JsonElement FindGroup(JsonElement rows, decimal publisherId)
        {
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (ReadNumber(row.GetProperty("publisher_id")) == publisherId)
                {
                    return row;
                }
            }

            Assert.Fail($"Expected a group for publisher_id {publisherId}. Rows: {rows}");
            return default;
        }

        private static async Task<CallToolResult> ExecuteAggregateAsync(
            string entity,
            string function,
            string field,
            string[]? groupby = null,
            Dictionary<string, object?>? having = null,
            string? orderby = null,
            int? first = null,
            string? after = null)
        {
            var args = new Dictionary<string, object?>
            {
                { "entity", entity },
                { "function", function },
                { "field", field }
            };

            if (groupby != null)
            {
                args["groupby"] = groupby;
            }

            if (having != null)
            {
                args["having"] = having;
            }

            if (orderby != null)
            {
                args["orderby"] = orderby;
            }

            if (first != null)
            {
                args["first"] = first;
            }

            if (after != null)
            {
                args["after"] = after;
            }

            return await ExecuteToolAsync(new AggregateRecordsTool(), BuildQueryServiceProvider(), args);
        }
    }
}
