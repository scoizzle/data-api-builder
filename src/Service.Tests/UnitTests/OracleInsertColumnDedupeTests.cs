// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// INSERT and the create-policy probe collapse column/value pairs that name the same physical
    /// column (ORA-00957 / ORA-00918). The comparison ignores case on purpose: a relationship field
    /// in config case names the same column as the catalog spelling.
    /// </summary>
    [TestClass]
    public class OracleInsertColumnDedupeTests
    {
        [TestMethod]
        public void SameCatalogColumnTwice_LastValueWins()
        {
            (List<string> columns, List<string> values) = OracleQueryBuilder.DedupeInsertColumns(
                new[] { "ID", "PUBLISHER_ID", "PUBLISHER_ID" }, new[] { "@param0", "@derived", "@explicit" });

            CollectionAssert.AreEqual(new[] { "ID", "PUBLISHER_ID" }, columns);
            CollectionAssert.AreEqual(new[] { "@param0", "@explicit" }, values);
        }

        /// <summary>
        /// A relationship field spelled in config case ("publisher_id") and the catalog spelling
        /// ("PUBLISHER_ID") are one column: the later spelling and value win, and the column is
        /// emitted once.
        /// </summary>
        [TestMethod]
        public void ConfigCasedAndCatalogSpellings_CollapseToOneColumn()
        {
            (List<string> columns, List<string> values) = OracleQueryBuilder.DedupeInsertColumns(
                new[] { "publisher_id", "ID", "PUBLISHER_ID" }, new[] { "@derived", "@param0", "@explicit" });

            CollectionAssert.AreEqual(new[] { "PUBLISHER_ID", "ID" }, columns);
            CollectionAssert.AreEqual(new[] { "@explicit", "@param0" }, values);
        }
    }
}
