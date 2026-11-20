// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// CHAR/NCHAR columns are fixed-width. The bind size must be the character length
    /// (CHAR_LENGTH), not the byte DATA_LENGTH, or a multi-byte charset over-pads the comparison.
    /// </summary>
    [TestClass]
    public class OracleFixedCharacterMetadataTests
    {
        [TestMethod]
        public void PopulateColumnDefinition_Char_UsesCharacterLengthAndFixedFlag()
        {
            ColumnDefinition column = Populate("CODE", "CHAR", dataLength: 16, charLength: 4);

            Assert.IsTrue(OracleObjectFacts.Column(column).IsFixedChar);
            Assert.IsFalse(OracleObjectFacts.Column(column).IsNChar);
            Assert.AreEqual(4, column.Length);
            Assert.AreEqual("CHAR", OracleObjectFacts.Column(column).PhysicalType);
        }

        [TestMethod]
        public void PopulateColumnDefinition_NChar_IsFixedAndNational()
        {
            ColumnDefinition column = Populate("CODE", "NCHAR", dataLength: 8, charLength: 4);

            Assert.IsTrue(OracleObjectFacts.Column(column).IsFixedChar);
            Assert.IsTrue(OracleObjectFacts.Column(column).IsNChar);
            Assert.AreEqual(4, column.Length);
        }

        [TestMethod]
        public void PopulateColumnDefinition_NVarchar2_IsNationalAndNotFixed()
        {
            ColumnDefinition column = Populate("TITLE", "NVARCHAR2", dataLength: 200, charLength: 50);

            Assert.IsFalse(OracleObjectFacts.Column(column).IsFixedChar);
            Assert.IsTrue(OracleObjectFacts.Column(column).IsNChar);
            Assert.AreEqual(200, column.Length);
        }

        [TestMethod]
        public void PopulateColumnDefinition_CharWithoutCharacterLength_FallsBackToDataLength()
        {
            ColumnDefinition column = Populate("CODE", "CHAR", dataLength: 4, charLength: null);

            Assert.IsTrue(OracleObjectFacts.Column(column).IsFixedChar);
            Assert.AreEqual(4, column.Length);
        }

        private static ColumnDefinition Populate(string name, string dataType, int dataLength, int? charLength)
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)RuntimeHelpers.GetUninitializedObject(
                typeof(OracleMetadataProvider));
            SourceDefinition source = new();
            source.Columns.Add(name, new ColumnDefinition { SystemType = typeof(string) });

            DataTable table = new();
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("DATA_TYPE", typeof(string));
            table.Columns.Add("DATA_LENGTH", typeof(int));
            table.Columns.Add("CHAR_LENGTH", typeof(object));
            DataRow row = table.NewRow();
            row["COLUMN_NAME"] = name;
            row["DATA_TYPE"] = dataType;
            row["DATA_LENGTH"] = dataLength;
            row["CHAR_LENGTH"] = charLength.HasValue ? charLength.Value : DBNull.Value;
            table.Rows.Add(row);

            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "PopulateColumnDefinitionWithHasDefaultAndDbType",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(provider, new object[] { source, table });
            return source.Columns[name];
        }
    }
}
