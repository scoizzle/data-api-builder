// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
    /// Unit tests for the Oracle-specific date/time parameter conversion performed by
    /// <see cref="OracleQueryExecutor.PopulateDbTypeForParameter"/>.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleQueryExecutorDateParameterTests
    {
        /// <summary>
        /// OData date literals produce Microsoft.OData.Edm.Date, which ODP.NET cannot bind.
        /// The executor must translate it to a plain DateTime bound as TIMESTAMP.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_EdmDateWithoutHint_BindsPlainTimestamp()
        {
            OracleParameter parameter = Apply(value: new Microsoft.OData.Edm.Date(2023, 1, 24));

            Assert.AreEqual(OracleDbType.TimeStamp, parameter.OracleDbType);
            Assert.AreEqual(new DateTime(2023, 1, 24), parameter.Value);
        }

        /// <summary>
        /// DateOnly is not bindable by ODP.NET either; it is translated to a plain DateTime.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_DateOnlyWithoutHint_BindsPlainTimestamp()
        {
            OracleParameter parameter = Apply(value: new DateOnly(2023, 1, 24));

            Assert.AreEqual(OracleDbType.TimeStamp, parameter.OracleDbType);
            Assert.AreEqual(new DateTime(2023, 1, 24), parameter.Value);
        }

        /// <summary>
        /// ISO 8601 filters produce a DateTimeOffset. Against an Oracle DATE/TIMESTAMP column
        /// (no DbType hint) the UTC wall clock must be bound as a plain TIMESTAMP instead of a
        /// TIMESTAMP WITH TIME ZONE, which Oracle would compare using the session time zone.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_DateTimeOffsetWithoutHint_BindsUtcWallClock()
        {
            OracleParameter parameter = Apply(value: new DateTimeOffset(2023, 1, 24, 10, 0, 0, TimeSpan.FromHours(-5)));

            Assert.AreEqual(OracleDbType.TimeStamp, parameter.OracleDbType);
            Assert.AreEqual(new DateTime(2023, 1, 24, 15, 0, 0), parameter.Value);
        }

        /// <summary>
        /// When the column is a TIMESTAMP WITH TIME ZONE (DbType.DateTimeOffset) the offset is
        /// preserved and the parameter is bound as TIMESTAMP WITH TIME ZONE.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_DateTimeOffsetHint_BindsTimeStampTzWithOffset()
        {
            DateTimeOffset value = new(2023, 1, 24, 10, 0, 0, TimeSpan.FromHours(-5));
            OracleParameter parameter = Apply(value: value, dbType: DbType.DateTimeOffset);

            Assert.AreEqual(OracleDbType.TimeStampTZ, parameter.OracleDbType);
            Assert.AreEqual(value, parameter.Value);
        }

        /// <summary>
        /// A date literal compared against a TIMESTAMP WITH TIME ZONE column is midnight UTC.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_EdmDateWithDateTimeOffsetHint_BindsMidnightUtc()
        {
            OracleParameter parameter = Apply(
                value: new Microsoft.OData.Edm.Date(2023, 1, 24),
                dbType: DbType.DateTimeOffset);

            Assert.AreEqual(OracleDbType.TimeStampTZ, parameter.OracleDbType);
            Assert.AreEqual(new DateTimeOffset(2023, 1, 24, 0, 0, 0, TimeSpan.Zero), parameter.Value);
        }

        /// <summary>
        /// A DateTime bound to a TIMESTAMP WITH TIME ZONE column is treated as UTC.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_DateTimeWithDateTimeOffsetHint_BindsAsUtc()
        {
            OracleParameter parameter = Apply(
                value: new DateTime(2023, 1, 24, 15, 0, 0, DateTimeKind.Utc),
                dbType: DbType.DateTimeOffset);

            Assert.AreEqual(OracleDbType.TimeStampTZ, parameter.OracleDbType);
            Assert.AreEqual(new DateTimeOffset(2023, 1, 24, 15, 0, 0, TimeSpan.Zero), parameter.Value);
        }

        /// <summary>
        /// DATE/TIMESTAMP columns with an explicit hint (DbType.DateTime) also bind the UTC clock
        /// as a plain TIMESTAMP.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_DateTimeHint_BindsPlainTimestamp()
        {
            OracleParameter parameter = Apply(
                value: new DateTimeOffset(2023, 1, 24, 10, 0, 0, TimeSpan.FromHours(-5)),
                dbType: DbType.DateTime);

            Assert.AreEqual(OracleDbType.TimeStamp, parameter.OracleDbType);
            Assert.AreEqual(new DateTime(2023, 1, 24, 15, 0, 0), parameter.Value);
        }

        /// <summary>
        /// Non-date parameters must pass through untouched.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_NonDateParameter_IsUnchanged()
        {
            OracleParameter parameter = Apply(value: "2023-01-24");

            Assert.AreNotEqual(OracleDbType.TimeStamp, parameter.OracleDbType);
            Assert.AreEqual("2023-01-24", parameter.Value);
        }

        /// <summary>
        /// Null values must pass through untouched (they are bound as DBNull by the caller).
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_NullValue_IsUnchanged()
        {
            OracleParameter parameter = new() { Value = DBNull.Value };
            DbConnectionParam entry = new(value: null);

            CreateExecutor().PopulateDbTypeForParameter(
                new KeyValuePair<string, DbConnectionParam>("@param0", entry),
                parameter);

            Assert.AreEqual(DBNull.Value, parameter.Value);
        }

        private static OracleParameter Apply(object value, DbType? dbType = null)
        {
            OracleParameter parameter = new();
            DbConnectionParam entry = new(value, dbType);

            // Mirrors OracleQueryExecutor.PrepareDbCommand: the conversion runs before Value is
            // assigned because ODP.NET rejects some CLR types (e.g. Edm.Date) from the setter.
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
