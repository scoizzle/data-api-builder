// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Overload selection is an exact match of configured names to one overload's input formals,
    /// or an explicit #n. A subset and a lowest-number fallback are not matches.
    /// </summary>
    [TestClass]
    public class OracleOverloadSelectionTests
    {
        [TestMethod]
        public void ExplicitOverloadSuffixSelectsThatSignatureEvenWhenNamesDiffer()
        {
            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(
                    SampleRows(),
                    configuredParameterNames: new[] { "p_other" },
                    configuredOverload: "2",
                    qualifiedName: "SYSTEM.DAB_OVERLOAD_PKG.TOUCH_ITEM");

            Assert.IsTrue(selected.All(row => row.Overload == "2"));
            Assert.AreEqual(2, selected.Count(row => row.Position > 0 && row.InOut != "OUT"));
        }

        [TestMethod]
        public void OverloadSuffixMatchesCatalogNumberWithDifferentPadding()
        {
            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(SampleRows(), configuredParameterNames: null, configuredOverload: "02");

            Assert.IsTrue(selected.All(row => row.Overload == "2"));
        }

        [TestMethod]
        public void MissingOverloadSuffixNamesTheSubprogram()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.SelectOracleOverload(
                    SampleRows(),
                    configuredParameterNames: null,
                    configuredOverload: "9",
                    qualifiedName: "SYSTEM.DAB_OVERLOAD_PKG.TOUCH_ITEM"));

            StringAssert.Contains(ex.Message, "SYSTEM.DAB_OVERLOAD_PKG.TOUCH_ITEM");
            StringAssert.Contains(ex.Message, "#9");
            StringAssert.Contains(ex.Message, "metadata only");
        }

        [TestMethod]
        public void OmittingADefaultedFormalDoesNotMatch()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_name", "VARCHAR2", "IN", 1, "1"),
                new("p_id", "NUMBER", "IN", 1, "2"),
                new("p_flag", "VARCHAR2", "IN", 2, "2", Defaulted: "Y"),
            };

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.SelectOracleOverload(rows, new[] { "p_id" }, qualifiedName: "SYSTEM.PKG.TOUCH_ITEM"));

            StringAssert.Contains(ex.Message, "p_flag DEFAULT");
            StringAssert.Contains(ex.Message, "#2");
        }

        [TestMethod]
        public void IdenticalInputFormalsInSeveralOverloadsFailStartup()
        {
            // Two overloads with the same required formals cannot be told apart from the
            // configured names alone. Picking the lowest number silently could run a different
            // operation (for example a delete instead of an update), so this must fail closed.
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_id", "NUMBER", "IN", 1, "1"),
                new("p_id", "NUMBER", "IN", 1, "2"),
            };

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.SelectOracleOverload(
                    rows,
                    configuredParameterNames: new[] { "p_id" },
                    qualifiedName: "SYSTEM.PKG.DELETE_OR_UPDATE"));

            StringAssert.Contains(ex.Message, "More than one overload");
            StringAssert.Contains(ex.Message, "#1");
            StringAssert.Contains(ex.Message, "#2");
            StringAssert.Contains(ex.Message, "SYSTEM.PKG.DELETE_OR_UPDATE");
        }

        [TestMethod]
        public void ZeroConfiguredNamesDoesNotChooseAnAllDefaultedOverload()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_id", "NUMBER", "IN", 1, "1"),
                new("p_flag", "VARCHAR2", "IN", 1, "2", Defaulted: "Y"),
            };

            Assert.ThrowsException<DataApiBuilderException>(() =>
                OracleMetadataProvider.SelectOracleOverload(rows, configuredParameterNames: null));
        }

        [TestMethod]
        public void ZeroConfiguredNamesMatchesOnlyTheOverloadWithNoInputFormals()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new(null, "NUMBER", "OUT", 0, "1"),
                new("p_cur", "REF CURSOR", "OUT", 1, "1"),
                new("p_id", "NUMBER", "IN", 1, "2"),
            };

            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(rows, configuredParameterNames: System.Array.Empty<string>());

            Assert.IsTrue(selected.All(row => row.Overload == "1"));
        }

        [TestMethod]
        public void OutAndReturnRowsAreNotInputFormals()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_id", "NUMBER", "IN", 1, "1"),
                new("p_cur", "REF CURSOR", "OUT", 2, "1"),
                new("p_id", "NUMBER", "IN", 1, "2"),
                new("p_name", "VARCHAR2", "IN", 2, "2"),
            };

            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(rows, new[] { "p_id" });

            Assert.IsTrue(selected.All(row => row.Overload == "1"));
        }

        [TestMethod]
        public void InOutCountsAsAnInputFormal()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_id", "NUMBER", "IN/OUT", 1, "1"),
                new("p_name", "VARCHAR2", "IN", 1, "2"),
            };

            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(rows, new[] { "p_id" });

            Assert.AreEqual("1", selected[0].Overload);
        }

        [TestMethod]
        public void SingleOverloadIsReturnedUnchanged()
        {
            List<OracleMetadataProvider.OracleArgumentRow> rows = new()
            {
                new("p_id", "NUMBER", "IN", 1, null),
            };

            List<OracleMetadataProvider.OracleArgumentRow> selected =
                OracleMetadataProvider.SelectOracleOverload(rows, configuredParameterNames: new[] { "does_not_match" });

            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual("p_id", selected[0].Name);
        }

        [TestMethod]
        public void HashInsideBracketsIsNotAnOverloadSuffix()
        {
            (string source, string? overload) = OracleMetadataProvider.SplitOracleOverloadSuffix("SYSTEM.[sub#2]");

            Assert.IsNull(overload);
            Assert.AreEqual("SYSTEM.[sub#2]", source);
        }

        [TestMethod]
        public void PackageSourceHashSuffixStripsBeforePackageParse()
        {
            (string source, string? overload) = OracleMetadataProvider.SplitOracleOverloadSuffix("SYSTEM.dab_overload_pkg.touch_item#2");
            Assert.AreEqual("SYSTEM.dab_overload_pkg.touch_item", source);
            Assert.AreEqual("2", overload);

            (OracleMetadataProvider provider, _) = OracleMetadataProviderTestsAccessor.Create();
            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "CreateStoredProcedureDatabaseObject",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            DatabaseStoredProcedure procedure = (DatabaseStoredProcedure)method.Invoke(
                provider,
                new object[] { "SYSTEM.dab_overload_pkg.touch_item#2", EntitySourceType.StoredProcedure })!;

            Assert.AreEqual("SYSTEM", procedure.SchemaName);
            Assert.AreEqual("TOUCH_ITEM", procedure.Name);
            Assert.AreEqual("DAB_OVERLOAD_PKG", OracleObjectFacts.Procedure(procedure).PackageName);
            Assert.AreEqual("2", OracleObjectFacts.Procedure(procedure).Overload);
        }

        private static List<OracleMetadataProvider.OracleArgumentRow> SampleRows()
        {
            return new()
            {
                new("p_id", "NUMBER", "IN", 1, "1"),
                new("p_id", "NUMBER", "IN", 1, "2"),
                new("p_flag", "VARCHAR2", "IN", 2, "2", Defaulted: "Y"),
            };
        }
    }

    /// <summary>
    /// Exposes the provider constructor helper without widening the production test class.
    /// </summary>
    internal static class OracleMetadataProviderTestsAccessor
    {
        public static (OracleMetadataProvider Provider, RuntimeConfigProvider ConfigProvider) Create()
        {
            // The timeout tests construct a provider the same way. Repeat the minimum here so this
            // class does not depend on a private helper.
            return CreateProvider();
        }

        private static (OracleMetadataProvider Provider, RuntimeConfigProvider ConfigProvider) CreateProvider()
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(OracleMetadataProvider));
            System.Reflection.FieldInfo databaseType = typeof(OracleMetadataProvider).BaseType!.GetField(
                "_databaseType",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            databaseType.SetValue(provider, DatabaseType.Oracle);
            return (provider, null!);
        }
    }
}
