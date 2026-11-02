// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Offline checks for the Oracle fixture. Live round-trips still need a database;
    /// these assertions lock the schema text the live tests depend on.
    /// </summary>
    [TestClass]
    public class OracleSchemaContractTests
    {
        [TestMethod]
        public void TypeTable_UsesTimeZoneRawAndBlobSeeds()
        {
            string schema = File.ReadAllText(FindRepoFile(Path.Combine("src", "Service.Tests", "DatabaseSchema-Oracle.sql")));

            StringAssert.Contains(schema, "datetimeoffset_types TIMESTAMP(6) WITH TIME ZONE");
            StringAssert.Contains(schema, "bytearray_types RAW(2000)");
            StringAssert.Contains(schema, "CREATE TABLE blob_type_table(");
            StringAssert.Contains(schema, "TO_TIMESTAMP_TZ('1999-01-08 10:23:54.999999 +08:00', 'YYYY-MM-DD HH24:MI:SS.FF6 TZH:TZM')");
            StringAssert.Contains(schema, "INSERT INTO blob_type_table(id, payload) VALUES (1, HEXTORAW('DEADBEEF'))");
            StringAssert.Contains(schema, "INSERT INTO blob_type_table(id, payload) VALUES (2, NULL)");
            StringAssert.Contains(schema, "INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,\n        single_types, float_types, decimal_types, boolean_types,\n        date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,\n        bytearray_types)\n    VALUES (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL )");
            StringAssert.Contains(schema, "WHERE books.id = get_book_by_id.id");
            StringAssert.Contains(schema, "WHERE publishers.id = get_publisher_by_id.id");
            Assert.AreEqual("3q2+7w==", Convert.ToBase64String(Convert.FromHexString("DEADBEEF")));
        }

        private static string FindRepoFile(string relativePath)
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail($"Could not find {relativePath} above {AppContext.BaseDirectory}.");
            return relativePath;
        }
    }
}
