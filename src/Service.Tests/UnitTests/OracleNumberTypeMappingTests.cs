// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the Oracle NUMBER precision/scale -> CLR type mapping.
    /// Oracle's unconstrained NUMBER reports no precision/scale from the catalog; a declared
    /// NUMBER(p, s) reports p and s. The provider narrows integral NUMBER(p, 0) columns to int or
    /// long where the values fit, and keeps everything else as decimal.
    /// </summary>
    [TestClass]
    public class OracleNumberTypeMappingTests
    {
        [DataTestMethod]
        [DataRow(null, null, typeof(decimal), DisplayName = "Unconstrained NUMBER (null precision) -> decimal")]
        [DataRow(9, 0, typeof(int), DisplayName = "NUMBER(9,0) -> int")]
        [DataRow(10, 0, typeof(long), DisplayName = "NUMBER(10,0) -> long")]
        [DataRow(18, 0, typeof(long), DisplayName = "NUMBER(18,0) -> long")]
        [DataRow(19, 0, typeof(decimal), DisplayName = "NUMBER(19,0) -> decimal")]
        [DataRow(8, 2, typeof(decimal), DisplayName = "NUMBER(8,2) -> decimal")]
        [DataRow(18, 2, typeof(decimal), DisplayName = "NUMBER(18,2) -> decimal")]
        [DataRow(9, null, typeof(int), DisplayName = "NUMBER(9) without scale -> int")]
        [DataRow(10, null, typeof(long), DisplayName = "NUMBER(10) without scale -> long")]
        public void MapNumberColumnType_MapsPrecisionAndScaleToExpectedType(object precision, object scale, Type expected)
        {
            // Act
            Type actual = OracleMetadataProvider.MapNumberColumnType(precision, scale);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void MapNumberColumnType_DBNullPrecision_MapsToDecimal()
        {
            // ODP.NET reports a NULL DATA_PRECISION for unconstrained NUMBER as DBNull in the
            // Columns schema table.
            Assert.AreEqual(typeof(decimal), OracleMetadataProvider.MapNumberColumnType(DBNull.Value, DBNull.Value));
            Assert.AreEqual(typeof(decimal), OracleMetadataProvider.MapNumberColumnType(DBNull.Value, 0));
        }

        [TestMethod]
        public void PopulateColumnDefinition_DataPrecisionSpelling_NarrowsNumberToInt()
        {
            ColumnDefinition column = PopulateNumberColumn(
                precisionColumn: "DATA_PRECISION",
                scaleColumn: "DATA_SCALE",
                precision: 9,
                scale: 0);

            Assert.AreEqual(typeof(int), column.SystemType);
        }

        [TestMethod]
        public void PopulateColumnDefinition_PrecisionSpelling_NarrowsNumberToInt()
        {
            ColumnDefinition column = PopulateNumberColumn(
                precisionColumn: "PRECISION",
                scaleColumn: "SCALE",
                precision: 9,
                scale: 0);

            Assert.AreEqual(typeof(int), column.SystemType);
        }

        [TestMethod]
        public void PopulateColumnDefinition_DataPrecision18_NarrowsNumberToLong()
        {
            ColumnDefinition column = PopulateNumberColumn("DATA_PRECISION", "DATA_SCALE", 18, 0);
            Assert.AreEqual(typeof(long), column.SystemType);
        }

        [TestMethod]
        public void PopulateColumnDefinition_DataPrecision19_StaysDecimal()
        {
            ColumnDefinition column = PopulateNumberColumn("DATA_PRECISION", "DATA_SCALE", 19, 0);
            Assert.AreEqual(typeof(decimal), column.SystemType);
        }

        [TestMethod]
        public void PopulateColumnDefinition_MissingPrecision_StaysDecimal()
        {
            ColumnDefinition column = PopulateNumberColumn("DATA_PRECISION", "DATA_SCALE", precision: null, scale: null);
            Assert.AreEqual(typeof(decimal), column.SystemType);
        }

        [TestMethod]
        public void PopulateColumnDefinition_FractionalScale_StaysDecimal()
        {
            ColumnDefinition column = PopulateNumberColumn("DATA_PRECISION", "DATA_SCALE", 18, 2);
            Assert.AreEqual(typeof(decimal), column.SystemType);
        }

        [TestMethod]
        public void ReadNumberPrecisionAndScale_PrefersDataPrecisionWhenBothArePresent()
        {
            DataTable table = new();
            table.Columns.Add("DATA_PRECISION", typeof(int));
            table.Columns.Add("PRECISION", typeof(int));
            table.Columns.Add("DATA_SCALE", typeof(int));
            table.Columns.Add("SCALE", typeof(int));
            DataRow row = table.NewRow();
            row["DATA_PRECISION"] = 9;
            row["PRECISION"] = 18;
            row["DATA_SCALE"] = 0;
            row["SCALE"] = 2;
            table.Rows.Add(row);

            (object? precision, object? scale) = OracleMetadataProvider.ReadNumberPrecisionAndScale(row, table);

            Assert.AreEqual(9, Convert.ToInt32(precision));
            Assert.AreEqual(0, Convert.ToInt32(scale));
            Assert.AreEqual(JsonDataType.Integer, TypeHelper.GetJsonDataTypeFromSystemType(typeof(int)));
            Assert.AreEqual(JsonDataType.Integer, TypeHelper.GetJsonDataTypeFromSystemType(typeof(long)));
            Assert.AreEqual(JsonDataType.Number, TypeHelper.GetJsonDataTypeFromSystemType(typeof(decimal)));
        }

        private static ColumnDefinition PopulateNumberColumn(
            string precisionColumn,
            string scaleColumn,
            int? precision,
            int? scale)
        {
            OracleMetadataProvider provider = (OracleMetadataProvider)RuntimeHelpers.GetUninitializedObject(
                typeof(OracleMetadataProvider));
            SourceDefinition source = new();
            source.Columns.Add("ID", new ColumnDefinition { SystemType = typeof(decimal) });

            DataTable table = new();
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("DATA_TYPE", typeof(string));
            table.Columns.Add(precisionColumn, typeof(int));
            table.Columns.Add(scaleColumn, typeof(int));
            DataRow row = table.NewRow();
            row["COLUMN_NAME"] = "ID";
            row["DATA_TYPE"] = "NUMBER";
            row[precisionColumn] = precision.HasValue ? precision.Value : DBNull.Value;
            row[scaleColumn] = scale.HasValue ? scale.Value : DBNull.Value;
            table.Rows.Add(row);

            MethodInfo method = typeof(OracleMetadataProvider).GetMethod(
                "PopulateColumnDefinitionWithHasDefaultAndDbType",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(provider, new object[] { source, table });
            return source.Columns["ID"];
        }
    }
}
