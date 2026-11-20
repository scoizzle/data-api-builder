// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// The Oracle upsert block returns one REF CURSOR. Its ___upsert_op___ indicator decides the
    /// HTTP outcome: empty cursor is a policy failure (403), 'missing' is 404, 'updated' is 200
    /// and 'inserted' is 201. The indicator never reaches the response.
    /// </summary>
    [TestClass]
    public class OracleQueryExecutorUpsertResultTests
    {
        [TestMethod]
        public async Task EmptyCursor_IsDatabasePolicyFailure()
        {
            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => CreateExecutor().GetMultipleResultSetsIfAnyAsync(Cursor(), new List<string> { "id: 1", "Book" }));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, exception.SubStatusCode);
            Assert.AreEqual(DataApiBuilderException.AUTHORIZATION_FAILURE, exception.Message);
        }

        [TestMethod]
        public async Task MissingIndicator_IsItemNotFoundNamingTheKey()
        {
            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => CreateExecutor().GetMultipleResultSetsIfAnyAsync(
                    Cursor(OracleQueryBuilder.MISSING_UPSERT), new List<string> { "id: 1", "Book" }));

            Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ItemNotFound, exception.SubStatusCode);
            Assert.AreEqual(
                "Cannot perform INSERT and could not find Book with primary key id: 1 to perform UPDATE on.",
                exception.Message);
        }

        [TestMethod]
        public async Task UpdatedIndicator_MarksUpdateResultAndStripsIndicator()
        {
            DbResultSet result = await CreateExecutor().GetMultipleResultSetsIfAnyAsync(Cursor("updated"));

            Assert.IsTrue(result.ResultProperties.TryGetValue(SqlMutationEngine.IS_UPDATE_RESULT_SET, out object? isUpdate));
            Assert.AreEqual(true, isUpdate);
            AssertRowWithoutIndicator(result);
        }

        [TestMethod]
        public async Task InsertedIndicator_IsNotAnUpdateResultAndStripsIndicator()
        {
            DbResultSet result = await CreateExecutor().GetMultipleResultSetsIfAnyAsync(Cursor("inserted"));

            Assert.IsFalse(result.ResultProperties.ContainsKey(SqlMutationEngine.IS_UPDATE_RESULT_SET));
            AssertRowWithoutIndicator(result);
        }

        private static void AssertRowWithoutIndicator(DbResultSet result)
        {
            Assert.AreEqual(1, result.Rows.Count);
            Dictionary<string, object?> columns = result.Rows[0].Columns;
            Assert.IsFalse(columns.ContainsKey(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME));
            Assert.AreEqual(1, columns["id"]);
            Assert.AreEqual("Dune", columns["title"]);
        }

        /// <summary>
        /// The upsert REF CURSOR: one row with the result columns and the indicator, or no row.
        /// </summary>
        private static DbDataReader Cursor(string? indicator = null)
        {
            DataTable table = new();
            table.Columns.Add("id", typeof(int));
            table.Columns.Add("title", typeof(string));
            table.Columns.Add(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME, typeof(string));
            if (indicator is not null)
            {
                table.Rows.Add(1, "Dune", indicator);
            }

            return table.CreateDataReader();
        }

        private static OracleQueryExecutor CreateExecutor()
        {
            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", new()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>()));
            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            return new OracleQueryExecutor(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }
    }
}
