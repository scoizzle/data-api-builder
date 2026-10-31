// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using System.Text;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Input bind types for Oracle strings. ODP.NET infers an untyped string as VARCHAR2, which
    /// cannot carry more than 4000 bytes (ORA-01461). Column facts select CLOB/NCLOB/NVARCHAR2;
    /// a value longer than 4000 UTF-8 bytes is a CLOB even when the column is unknown.
    /// </summary>
    [TestClass]
    public class OracleQueryExecutorParameterBindingTests
    {
        [TestMethod]
        public void PopulateDbTypeForParameter_UnhintedWideString_BindsClob()
        {
            OracleParameter parameter = Apply(new string('A', 4001));

            Assert.AreEqual(OracleDbType.Clob, parameter.OracleDbType);
            Assert.AreEqual(4001, ((string)parameter.Value).Length);
        }

        [TestMethod]
        public void PopulateDbTypeForParameter_ClobColumnInput_BindsClob()
        {
            OracleParameter parameter = Apply("short clob", OracleStringBindFacts.Kind.Clob);

            Assert.AreEqual(OracleDbType.Clob, parameter.OracleDbType);
        }

        [TestMethod]
        public void PopulateDbTypeForParameter_NClobColumnInput_BindsNClob()
        {
            OracleParameter parameter = Apply("short nclob", OracleStringBindFacts.Kind.NClob);

            Assert.AreEqual(OracleDbType.NClob, parameter.OracleDbType);
        }

        [TestMethod]
        public void PopulateDbTypeForParameter_NullClobColumn_BindsClob()
        {
            OracleParameter parameter = new();
            DbConnectionParam entry = new(value: null);
            OracleStringBindFacts.Set(entry, OracleStringBindFacts.Kind.Clob);

            CreateExecutor().PopulateDbTypeForParameter(
                new KeyValuePair<string, DbConnectionParam>("@param0", entry),
                parameter);

            Assert.AreEqual(OracleDbType.Clob, parameter.OracleDbType);
            Assert.IsNull(parameter.Value);
        }

        [TestMethod]
        public void PopulateDbTypeForParameter_NCharColumn_BindsNVarchar2UntilWide()
        {
            OracleParameter shortValue = Apply("n", OracleStringBindFacts.Kind.NChar);
            OracleParameter wideValue = Apply(new string('A', 4001), OracleStringBindFacts.Kind.NChar);
            OracleParameter nullValue = new();
            DbConnectionParam nullEntry = new(value: null);
            OracleStringBindFacts.Set(nullEntry, OracleStringBindFacts.Kind.NChar);
            CreateExecutor().PopulateDbTypeForParameter(
                new KeyValuePair<string, DbConnectionParam>("@param0", nullEntry),
                nullValue);

            Assert.AreEqual(OracleDbType.NVarchar2, shortValue.OracleDbType);
            Assert.AreEqual(OracleDbType.NClob, wideValue.OracleDbType);
            Assert.AreEqual(OracleDbType.NVarchar2, nullValue.OracleDbType);
        }

        [TestMethod]
        public void PopulateDbTypeForParameter_BoundedShortString_RemainsVarchar2()
        {
            OracleParameter parameter = Apply(new string('A', 4000));

            Assert.AreNotEqual(OracleDbType.Clob, parameter.OracleDbType);
            Assert.AreNotEqual(OracleDbType.NClob, parameter.OracleDbType);
            Assert.AreEqual(4000, ((string)parameter.Value).Length);
        }

        [TestMethod]
        public void PopulateDbTypeForParameter_MultiByteAtUtf8Boundary_BindsByByteLength()
        {
            // '測' is 3 UTF-8 bytes. 1333 characters are 3999 bytes; 1334 are 4002.
            OracleParameter under = Apply(new string('測', 1333));
            OracleParameter over = Apply(new string('測', 1334));

            Assert.AreNotEqual(OracleDbType.Clob, under.OracleDbType);
            Assert.AreEqual(OracleDbType.Clob, over.OracleDbType);
            Assert.IsTrue(Encoding.UTF8.GetByteCount((string)over.Value) > 4000);
        }

        private static OracleParameter Apply(object value, OracleStringBindFacts.Kind kind = OracleStringBindFacts.Kind.None)
        {
            OracleParameter parameter = new();
            DbConnectionParam entry = new(value);
            OracleStringBindFacts.Set(entry, kind);
            CreateExecutor().PopulateDbTypeForParameter(
                new KeyValuePair<string, DbConnectionParam>("@param0", entry),
                parameter);
            if (parameter.Value is null)
            {
                parameter.Value = value;
            }

            return parameter;
        }

        private static OracleQueryExecutor CreateExecutor()
        {
            RuntimeConfig mockConfig = new(
                Schema: "",
                DataSource: new(DatabaseType.Oracle, "User Id=x;Password=y;Data Source=localhost:1521/x", new()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(mockConfig);
            return new OracleQueryExecutor(
                provider,
                new OracleDbExceptionParser(provider),
                new Mock<ILogger<IQueryExecutor>>().Object,
                new Mock<IHttpContextAccessor>().Object);
        }
    }
}
