// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class OracleCatalogNameResolverTests
    {
        [TestMethod]
        public async Task CurrentSchemaTableWinsOverPublicSynonym()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("SYSTEM", "BOOKS")] = "TABLE",
                [("PUBLIC", "BOOKS")] = "SYNONYM"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("PUBLIC", "BOOKS")] = new("PUBLIC", "BOOKS", "HR", "BOOKS", DbLink: null)
            };

            (string schema, string name) = await Resolve("SYSTEM", "BOOKS", allowPublicFallback: true, types, synonyms);

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("BOOKS", name);
        }

        [TestMethod]
        public async Task PrivateSynonymResolvesToBaseTable()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("SYSTEM", "BOOKS_SYN")] = "SYNONYM",
                [("SYSTEM", "BOOKS")] = "TABLE"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("SYSTEM", "BOOKS_SYN")] = new("SYSTEM", "BOOKS_SYN", "SYSTEM", "BOOKS", DbLink: null)
            };

            (string schema, string name) = await Resolve("SYSTEM", "BOOKS_SYN", allowPublicFallback: true, types, synonyms);

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("BOOKS", name);
        }

        [TestMethod]
        public async Task NestedPrivateSynonymResolvesToBaseTable()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("SYSTEM", "BOOKS_SYN_NESTED")] = "SYNONYM",
                [("SYSTEM", "BOOKS_SYN")] = "SYNONYM",
                [("SYSTEM", "BOOKS")] = "TABLE"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("SYSTEM", "BOOKS_SYN_NESTED")] = new("SYSTEM", "BOOKS_SYN_NESTED", "SYSTEM", "BOOKS_SYN", DbLink: null),
                [("SYSTEM", "BOOKS_SYN")] = new("SYSTEM", "BOOKS_SYN", "SYSTEM", "BOOKS", DbLink: null)
            };

            (string schema, string name) = await Resolve("SYSTEM", "BOOKS_SYN_NESTED", allowPublicFallback: true, types, synonyms);

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("BOOKS", name);
        }

        [TestMethod]
        public async Task PublicFallbackWhenUnqualifiedNameMissingInCurrentSchema()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("PUBLIC", "DAB_PUB_PUBLISHERS")] = "SYNONYM",
                [("SYSTEM", "PUBLISHERS")] = "TABLE"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("PUBLIC", "DAB_PUB_PUBLISHERS")] = new("PUBLIC", "DAB_PUB_PUBLISHERS", "SYSTEM", "PUBLISHERS", DbLink: null)
            };

            (string schema, string name) = await Resolve("SYSTEM", "DAB_PUB_PUBLISHERS", allowPublicFallback: true, types, synonyms);

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("PUBLISHERS", name);
        }

        [TestMethod]
        public async Task QualifiedNameDoesNotUsePublicFallback()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("PUBLIC", "BOOKS")] = "SYNONYM",
                [("HR", "BOOKS")] = "TABLE"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("PUBLIC", "BOOKS")] = new("PUBLIC", "BOOKS", "HR", "BOOKS", DbLink: null)
            };

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => Resolve("OTHER", "BOOKS", allowPublicFallback: false, types, synonyms));

            StringAssert.Contains(exception.Message, "Could not resolve Oracle object");
        }

        [TestMethod]
        public async Task PublicSchemaLooksUpPublicSynonym()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("PUBLIC", "DAB_PUB_PUBLISHERS")] = "SYNONYM",
                [("SYSTEM", "PUBLISHERS")] = "TABLE"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("PUBLIC", "DAB_PUB_PUBLISHERS")] = new("PUBLIC", "DAB_PUB_PUBLISHERS", "SYSTEM", "PUBLISHERS", DbLink: null)
            };

            (string schema, string name) = await Resolve("PUBLIC", "DAB_PUB_PUBLISHERS", allowPublicFallback: true, types, synonyms);

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("PUBLISHERS", name);
        }

        [TestMethod]
        public async Task RemoteSynonymThrows()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("SYSTEM", "REMOTE_BOOKS")] = "SYNONYM"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("SYSTEM", "REMOTE_BOOKS")] = new("SYSTEM", "REMOTE_BOOKS", "HR", "BOOKS", DbLink: "OTHERDB")
            };

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => Resolve("SYSTEM", "REMOTE_BOOKS", allowPublicFallback: true, types, synonyms));

            StringAssert.Contains(exception.Message, "remote object");
        }

        [TestMethod]
        public async Task CircularSynonymThrows()
        {
            Dictionary<(string, string), string> types = new()
            {
                [("SYSTEM", "A")] = "SYNONYM",
                [("SYSTEM", "B")] = "SYNONYM"
            };
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms = new()
            {
                [("SYSTEM", "A")] = new("SYSTEM", "A", "SYSTEM", "B", DbLink: null),
                [("SYSTEM", "B")] = new("SYSTEM", "B", "SYSTEM", "A", DbLink: null)
            };

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => Resolve("SYSTEM", "A", allowPublicFallback: true, types, synonyms));

            StringAssert.Contains(exception.Message, "circular");
        }

        private static Task<(string Schema, string Name)> Resolve(
            string schema,
            string name,
            bool allowPublicFallback,
            Dictionary<(string, string), string> types,
            Dictionary<(string, string), OracleCatalogNameResolver.SynonymRow> synonyms)
        {
            return OracleCatalogNameResolver.ResolveAsync(
                schema,
                name,
                allowPublicFallback,
                (owner, objectName) =>
                {
                    types.TryGetValue((owner, objectName), out string? objectType);
                    return Task.FromResult(objectType);
                },
                (owner, objectName) =>
                {
                    if (synonyms.TryGetValue((owner, objectName), out OracleCatalogNameResolver.SynonymRow row))
                    {
                        return Task.FromResult<OracleCatalogNameResolver.SynonymRow?>(row);
                    }

                    return Task.FromResult<OracleCatalogNameResolver.SynonymRow?>(null);
                });
        }
    }
}
