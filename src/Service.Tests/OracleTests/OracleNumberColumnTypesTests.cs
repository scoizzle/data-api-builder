// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Live catalog check for NUMBER precision. Not run in the offline metadata pass:
    /// it needs the Oracle fixture (number_shapes plus books.id).
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleNumberColumnTypesTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestMethod]
        public async Task NumberPrecision_MetadataAndGraphQLTypes()
        {
            SourceDefinition shapes = _sqlMetadataProvider.GetSourceDefinition("NumberShape");
            Assert.AreEqual(typeof(int), Column(shapes, "small_id").SystemType);
            Assert.AreEqual(typeof(long), Column(shapes, "big_id").SystemType);
            Assert.AreEqual(typeof(decimal), Column(shapes, "free_num").SystemType);
            Assert.AreEqual(typeof(decimal), Column(shapes, "money").SystemType);

            SourceDefinition book = _sqlMetadataProvider.GetSourceDefinition("Book");
            Assert.AreEqual(typeof(int), Column(book, "id").SystemType);

            string introspection = @"
                query {
                    __type(name: ""NumberShape"") {
                        fields {
                            name
                            type { name kind ofType { name kind ofType { name kind } } }
                        }
                    }
                }";
            JsonElement numberShape = await ExecuteGraphQLRequestAsync(introspection, "__type", isAuthenticated: true);
            Dictionary<string, string> graphQlTypes = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement field in numberShape.GetProperty("fields").EnumerateArray())
            {
                graphQlTypes[field.GetProperty("name").GetString()] = Unwrap(field.GetProperty("type"));
            }

            Assert.AreEqual("Int", graphQlTypes["small_id"]);
            Assert.AreEqual("Long", graphQlTypes["big_id"]);
            Assert.AreEqual("Decimal", graphQlTypes["free_num"]);
            Assert.AreEqual("Decimal", graphQlTypes["money"]);
        }

        private static ColumnDefinition Column(SourceDefinition source, string columnName)
        {
            foreach (KeyValuePair<string, ColumnDefinition> entry in source.Columns)
            {
                if (string.Equals(entry.Key, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            Assert.Fail($"Column {columnName} was not loaded.");
            return null;
        }

        private static string Unwrap(JsonElement type)
        {
            if (type.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString();
            }

            if (type.TryGetProperty("ofType", out JsonElement ofType) && ofType.ValueKind == JsonValueKind.Object)
            {
                return Unwrap(ofType);
            }

            return string.Empty;
        }
    }
}
