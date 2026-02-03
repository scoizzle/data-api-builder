// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Oracle specific override for SqlMetadataProvider.
    /// </summary>
    public class OracleMetadataProvider :
        SqlMetadataProvider<OracleConnection, OracleDataAdapter, OracleCommand>
    {

        public OracleMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            RuntimeConfigValidator runtimeConfigValidator,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
        }

        /// <summary>
        /// Gets the default schema name for Oracle.
        /// For Oracle, the default schema is the connected user's schema.
        /// We return an empty string here as the schema will be determined at connection time.
        /// </summary>
        public override string GetDefaultSchemaName()
        {
            // Oracle uses the connected user's schema as default
            // This will be determined at runtime from the connection
            return string.Empty;
        }

        /// <summary>
        /// Takes a string version of an Oracle data type and returns its .NET common language runtime (CLR) counterpart
        /// As per Oracle documentation for type mappings
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            return sqlType.ToUpperInvariant() switch
            {
                "NUMBER" => typeof(decimal),
                "FLOAT" => typeof(double),
                "BINARY_FLOAT" => typeof(float),
                "BINARY_DOUBLE" => typeof(double),
                "VARCHAR2" => typeof(string),
                "NVARCHAR2" => typeof(string),
                "CHAR" => typeof(string),
                "NCHAR" => typeof(string),
                "CLOB" => typeof(string),
                "NCLOB" => typeof(string),
                "DATE" => typeof(DateTime),
                "TIMESTAMP" => typeof(DateTime),
                "TIMESTAMP WITH TIME ZONE" => typeof(DateTimeOffset),
                "TIMESTAMP WITH LOCAL TIME ZONE" => typeof(DateTime),
                "RAW" => typeof(byte[]),
                "BLOB" => typeof(byte[]),
                "LONG" => typeof(string),
                "LONG RAW" => typeof(byte[]),
                "ROWID" => typeof(string),
                "UROWID" => typeof(string),
                _ => typeof(object)
            };
        }
    }
}
