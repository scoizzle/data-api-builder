// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Offline checks for Oracle source parsing. Quoting is decided here and does not persist:
    /// an unquoted token folds to uppercase, a double-quoted token keeps its spelling.
    /// Bracket tokens stay the separator escape and still fold. A token starts with <c>"</c> or
    /// <c>[</c>; the two forms do not nest.
    /// </summary>
    [TestClass]
    public class OracleIdentifierParsingTests
    {
        [TestMethod]
        public void QuotedSourceKeepsExactCase()
        {
            (string schema, string name) = Provider().ParseSchemaAndDbTableName("\"Customers\"");

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("Customers", name);
        }

        [TestMethod]
        public void UnquotedSourceFoldsToUppercase()
        {
            (string schema, string name) = Provider().ParseSchemaAndDbTableName("books");

            Assert.AreEqual("SYSTEM", schema);
            Assert.AreEqual("BOOKS", name);
        }

        [TestMethod]
        public void QuotedTokenWithDotIsNotSplit()
        {
            (string schema, string name) = Provider().ParseSchemaAndDbTableName("\"Sch.ema\".\"Tab.le\"");

            Assert.AreEqual("Sch.ema", schema);
            Assert.AreEqual("Tab.le", name);
        }

        [TestMethod]
        public void MixedQuotingIsPerToken()
        {
            (string schema, string name) = Provider().ParseSchemaAndDbTableName("\"Hr\".Books");

            Assert.AreEqual("Hr", schema);
            Assert.AreEqual("BOOKS", name);
        }

        [TestMethod]
        public void UnterminatedQuoteThrows()
        {
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => Provider().ParseSchemaAndDbTableName("\"Customers"));

            StringAssert.Contains(exception.Message, "quote");
        }

        [TestMethod]
        public void PackageTokenFollowsSameRule()
        {
            DatabaseStoredProcedure folded = Procedure("hr.pkg.sub");
            Assert.AreEqual("HR", folded.SchemaName);
            Assert.AreEqual("SUB", folded.Name);
            Assert.AreEqual("PKG", OracleObjectFacts.Procedure(folded).PackageName);

            DatabaseStoredProcedure mixed = Procedure("\"Hr\".\"Pkg.Name\".\"DoIt\"");
            Assert.AreEqual("Hr", mixed.SchemaName);
            Assert.AreEqual("DoIt", mixed.Name);
            Assert.AreEqual("Pkg.Name", OracleObjectFacts.Procedure(mixed).PackageName);
        }

        [TestMethod]
        public void BracketEscapeFoldsAndDoesNotPreserveCase()
        {
            (string schema, string name) = Provider().ParseSchemaAndDbTableName("[sch.ema].[Customers]");

            Assert.AreEqual("SCH.EMA", schema);
            Assert.AreEqual("CUSTOMERS", name);
        }

        [TestMethod]
        public void DoubledQuoteIsOneQuoteCharacter()
        {
            (string _, string name) = Provider().ParseSchemaAndDbTableName("\"Say \"\"Hi\"\"\"");

            Assert.AreEqual("Say \"Hi\"", name);
        }

        [TestMethod]
        public void CatalogSourceRoundTripsQuotedSpelling()
        {
            string formatted = OracleSourceNames.Format("HR", "Customers");

            Assert.AreEqual("HR.\"Customers\"", formatted);
            (string schema, string name) = Provider().ParseSchemaAndDbTableName(formatted);
            Assert.AreEqual("HR", schema);
            Assert.AreEqual("Customers", name);
        }

        [TestMethod]
        public void CaseCollidingColumnsFailInitialization()
        {
            SourceDefinition source = new();
            OracleMetadataProvider.AddCatalogColumn(source, "DATA", new ColumnDefinition());

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => OracleMetadataProvider.AddCatalogColumn(source, "data", new ColumnDefinition()));

            StringAssert.Contains(exception.Message, "DATA");
            StringAssert.Contains(exception.Message, "data");
        }

        [TestMethod]
        public void RepeatedCatalogColumnIsIgnored()
        {
            SourceDefinition source = new();
            OracleMetadataProvider.AddCatalogColumn(source, "ID", new ColumnDefinition());
            OracleMetadataProvider.AddCatalogColumn(source, "ID", new ColumnDefinition());

            Assert.AreEqual(1, source.Columns.Count);
        }

        [TestMethod]
        public void CaseCollidingRelationsFailInitialization()
        {
            DatabaseTable quoted = new("HR", "Customers");
            DatabaseTable folded = new("HR", "CUSTOMERS");

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => OracleMetadataProvider.RejectCaseTwinRelation(
                    "Folded",
                    folded,
                    new[] { new System.Collections.Generic.KeyValuePair<string, DatabaseObject>("Quoted", quoted) }));

            StringAssert.Contains(exception.Message, "Customers");
            StringAssert.Contains(exception.Message, "CUSTOMERS");
        }

        [TestMethod]
        public void IdenticalRelationSpellingIsNotATwin()
        {
            DatabaseTable first = new("HR", "BOOKS");
            DatabaseTable second = new("HR", "BOOKS");

            OracleMetadataProvider.RejectCaseTwinRelation(
                "Second",
                second,
                new[] { new System.Collections.Generic.KeyValuePair<string, DatabaseObject>("First", first) });
        }

        [TestMethod]
        public void QuotedTriggerColumnMatchesExactSpellingOnly()
        {
            SourceDefinition quoted = new();
            quoted.Columns.TryAdd("Id", new ColumnDefinition());
            Assert.IsTrue(OracleMetadataProvider.TryGetTriggerColumn(quoted, "Id", out _));
            Assert.IsFalse(OracleMetadataProvider.TryGetTriggerColumn(quoted, "id", out _));

            SourceDefinition folded = new();
            folded.Columns.TryAdd("ID", new ColumnDefinition());
            Assert.IsTrue(OracleMetadataProvider.TryGetTriggerColumn(folded, "id", out _));
        }

        private static OracleMetadataProvider Provider()
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(OracleMetadataProvider));
            typeof(OracleMetadataProvider).GetField("_defaultSchemaName", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, "SYSTEM");
            typeof(OracleMetadataProvider).BaseType!.GetField("_databaseType", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, DatabaseType.Oracle);
            return provider;
        }

        private static DatabaseStoredProcedure Procedure(string source)
        {
            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "CreateStoredProcedureDatabaseObject",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DatabaseStoredProcedure)method.Invoke(
                Provider(),
                new object[] { source, EntitySourceType.StoredProcedure })!;
        }
    }
}
