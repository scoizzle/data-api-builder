// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;

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
        /// For Oracle, the default schema is the connected user's schema (USER ID from connection string).
        /// </summary>
        public override string GetDefaultSchemaName()
        {
            // Oracle uses the connected user's schema as default
            // Extract the User ID from the connection string
            try
            {
                var builder = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder(ConnectionString);
                // User ID is the schema name in Oracle, and Oracle stores it in uppercase
                return builder.UserID?.ToUpperInvariant() ?? "SYSTEM";
            }
            catch
            {
                // If we can't parse the connection string, default to SYSTEM
                return "SYSTEM";
            }
        }

        /// <summary>
        /// Oracle-specific implementation to populate trigger metadata for a table.
        /// For initial implementation, we skip trigger metadata population.
        /// </summary>
        public override async Task PopulateTriggerMetadataForTable(string entityName, string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            // For now, Oracle trigger metadata population is not implemented
            // This is acceptable for basic functionality - triggers will be detected at runtime
            await Task.CompletedTask;
        }

        /// <summary>
        /// Oracle-specific implementation to get column metadata.
        /// Oracle only supports 3 restrictions (Owner, Table, Column) in GetSchema for Columns,
        /// unlike SQL Server which supports 4 (Database, Schema, Table, Column).
        /// </summary>
        protected override async Task<DataTable> GetColumnsAsync(string schemaName, string tableName)
        {
            using OracleConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            await conn.OpenAsync();

            // Oracle only supports 3 restrictions: Owner (schema), Table Name, Column Name
            // Setting database (index 0) causes ORA-50019 error
            string?[] columnRestrictions = new string?[3];
            
            // For Oracle: use the schema name if provided, otherwise null (defaults to current user)
            // Oracle stores identifiers in uppercase when unquoted
            columnRestrictions[0] = string.IsNullOrEmpty(schemaName) ? null : schemaName.ToUpperInvariant();
            columnRestrictions[1] = tableName.ToUpperInvariant(); // Table name
            columnRestrictions[2] = null; // Column name (null means all columns)

            DataTable columnsInTable = await conn.GetSchemaAsync("Columns", columnRestrictions);
            await AddColumnDefaultsAsync(conn, columnsInTable, schemaName, tableName);
            return columnsInTable;
        }

        private static async Task AddColumnDefaultsAsync(
            OracleConnection connection,
            DataTable columns,
            string schemaName,
            string tableName)
        {
            if (!columns.Columns.Contains("DATA_DEFAULT"))
            {
                columns.Columns.Add("DATA_DEFAULT", typeof(string));
            }

            using OracleCommand command = connection.CreateCommand();
            command.BindByName = true;
            // ALL_TAB_COLUMNS.DATA_DEFAULT is an Oracle LONG. ODP.NET returns an empty string
            // unless the LONG fetch size is explicitly enabled.
            command.InitialLONGFetchSize = -1;
            command.CommandText = "SELECT COLUMN_NAME, DATA_DEFAULT " +
                "FROM ALL_TAB_COLUMNS " +
                "WHERE OWNER = :owner AND TABLE_NAME = :table_name";
            command.Parameters.Add(new OracleParameter("owner", schemaName.ToUpperInvariant()));
            command.Parameters.Add(new OracleParameter("table_name", tableName.ToUpperInvariant()));

            using OracleDataReader reader = await command.ExecuteReaderAsync();
            Dictionary<string, object?> defaults = new(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                defaults[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetValue(1);
            }

            foreach (DataRow column in columns.Rows)
            {
                string columnName = column["COLUMN_NAME"].ToString()!;
                if (defaults.TryGetValue(columnName, out object? value))
                {
                    column["DATA_DEFAULT"] = value is null ? DBNull.Value : value.ToString();
                }
            }
        }

        /// <summary>
        /// Oracle-specific implementation to populate column definition with HasDefault and DbType.
        /// ODP.NET's GetSchema("Columns") exposes the default expression in the DATA_DEFAULT
        /// column (matching ALL_TAB_COLUMNS.DATA_DEFAULT) and the physical type in DATA_TYPE.
        ///
        /// RAW/BLOB columns are also refined here: ODP.NET's FillSchema/GetSchemaTable path reports
        /// them as System.String (hex-encoded) with ProviderType=DBNull, but DAB's REST/GraphQL
        /// contract treats byte columns as byte[] (base64-serialized via HotChocolate's ByteArray
        /// scalar). Setting SystemType to byte[] here makes the Oracle query builder emit its
        /// base64 conversion (UTL_ENCODE.BASE64_ENCODE) so values round-trip like varbinary on MsSql.
        /// </summary>
        protected override void PopulateColumnDefinitionWithHasDefaultAndDbType(
            SourceDefinition sourceDefinition,
            DataTable allColumnsInTable)
        {
            foreach (DataRow columnInfo in allColumnsInTable.Rows)
            {
                // Normalize to the same casing used for sourceDefinition.Columns keys
                // (lowercase via GetPhysicalDatabaseColumnName).
                string columnName = GetPhysicalDatabaseColumnName((string)columnInfo["COLUMN_NAME"]);

                if (sourceDefinition.Columns.TryGetValue(columnName, out ColumnDefinition? columnDefinition))
                {
                    bool hasDefault =
                        allColumnsInTable.Columns.Contains("DATA_DEFAULT")
                        && columnInfo["DATA_DEFAULT"] is not DBNull
                        && !string.IsNullOrEmpty(columnInfo["DATA_DEFAULT"] as string);

                    columnDefinition.HasDefault = hasDefault;

                    if (hasDefault)
                    {
                        columnDefinition.DefaultValue = columnInfo["DATA_DEFAULT"];
                    }

                    // Refine RAW/BLOB to byte[] (base64 contract). ODP.NET's Columns schema reports the
                    // physical type either as "DATA_TYPE" (some drivers) or "DATATYPE" (ODP.NET
                    // managed), with values "RAW"/"BLOB" - compare ordinal-insensitively and
                    // handle both column spellings so detection does not depend on driver version.
                    string? physicalType = null;
                    if (allColumnsInTable.Columns.Contains("DATA_TYPE"))
                    {
                        physicalType = columnInfo["DATA_TYPE"] as string;
                    }
                    else if (allColumnsInTable.Columns.Contains("DATATYPE"))
                    {
                        physicalType = columnInfo["DATATYPE"] as string;
                    }

                    if (physicalType is not null
                        && (physicalType.Equals("RAW", StringComparison.OrdinalIgnoreCase)
                            || physicalType.Equals("BLOB", StringComparison.OrdinalIgnoreCase)))
                    {
                        columnDefinition.SystemType = typeof(byte[]);
                        columnDefinition.DbType = DbType.Binary;
                        continue;
                    }

                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);
                }
            }
        }

        /// <summary>
        /// Oracle stores unquoted identifiers in uppercase. DAB exposes these as-is which makes
        /// REST/GraphQL field names UPPERCASE, inconsistent with the other SQL providers
        /// (MsSql/PostgreSQL/MySQL surface lowercase field names). Lowercase the physical column
        /// name so the exposed schema matches the other providers.
        /// </summary>
        protected override string GetPhysicalDatabaseColumnName(string columnName)
        {
            return columnName.ToLowerInvariant();
        }

        /// <summary>
        /// ODP.NET reports Oracle RAW/BLOB columns as hex-encoded System.String in the schema table,
        /// but DAB's REST/GraphQL contract treats byte columns as byte[] (base64-serialized via
        /// HotChocolate's ByteArray scalar). Refine those columns to typeof(byte[]) so the Oracle
        /// query builder applies its base64 conversion (UTL_ENCODE.BASE64_ENCODE) and the exposed
        /// values round-trip like varbinary on MsSql.
        /// ProviderType values: 126 = RAW, 113 = BLOB.
        /// NB: the DAB base metadata path (FillSchema + DataTableReader) reports ProviderType as
        /// DBNull, so this is a safety net for paths that surface the int; the authoritative
        /// RAW/BLOB detection for DAB runs in PopulateColumnDefinitionWithHasDefaultAndDbType
        /// via the ODP.NET Columns DATATYPE column.
        /// </summary>
        protected override Type GetSystemTypeFromSchemaTable(DataRow columnInfoFromAdapter, Type driverType)
        {
            if (columnInfoFromAdapter.Table.Columns.Contains("ProviderType")
                && columnInfoFromAdapter["ProviderType"] is int providerType
                && (providerType == 126 || providerType == 113))
            {
                return typeof(byte[]);
            }

            return base.GetSystemTypeFromSchemaTable(columnInfoFromAdapter, driverType);
        }

        /// <summary>
        /// Oracle-specific implementation to populate stored procedure schema information.
        /// Oracle only supports 2 restrictions (Owner, Name) for the Procedures collection,
        /// unlike SQL Server which supports 4 (Database, Schema, Table, Column).
        /// </summary>
        protected override async Task FillSchemaForStoredProcedureAsync(
            Azure.DataApiBuilder.Config.ObjectModel.Entity procedureEntity,
            string entityName,
            string schemaName,
            string storedProcedureSourceName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            using OracleConnection conn = new();
            conn.ConnectionString = ConnectionString;
            DataTable procedureMetadata;
            string?[] procedureRestrictions = new string?[2];

            try
            {
                await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
                await conn.OpenAsync();

                // Oracle only supports 2 restrictions for Procedures schema:
                // [0] = Owner (schema) - Oracle stores identifiers in uppercase when unquoted
                // [1] = Name (procedure name)
                procedureRestrictions[0] = string.IsNullOrEmpty(schemaName) ? null : schemaName.ToUpperInvariant();
                procedureRestrictions[1] = storedProcedureSourceName.ToUpperInvariant();

                procedureMetadata = await conn.GetSchemaAsync(collectionName: "Procedures", restrictionValues: procedureRestrictions);
            }
            catch (Exception ex)
            {
                string message = $"Cannot obtain Schema for entity {entityName} " +
                            $"with underlying database object source: {schemaName}.{storedProcedureSourceName} " +
                            $"due to: {ex.Message}";

                var exception = new DataApiBuilderException(
                    message: message,
                    innerException: ex,
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

                if (_isValidateOnly)
                {
                    SqlMetadataExceptions.Add(exception);
                    return;
                }
                else
                {
                    throw exception;
                }
            }

            // Stored procedure does not exist in DB schema
            if (procedureMetadata.Rows.Count == 0)
            {
                var exception = new DataApiBuilderException(
                    message: $"No stored procedure definition found for the given database object {storedProcedureSourceName}",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

                if (_isValidateOnly)
                {
                    SqlMetadataExceptions.Add(exception);
                    return;
                }
                else
                {
                    throw exception;
                }
            }

            // Each row in the procedureParams DataTable corresponds to a single parameter
            DataTable parameterMetadata = await conn.GetSchemaAsync(collectionName: "ProcedureParameters", restrictionValues: procedureRestrictions);

            // For each row/parameter, add an entry to StoredProcedureDefinition.Parameters dictionary
            foreach (DataRow row in parameterMetadata.Rows)
            {
                // row["DATA_TYPE"] has value type string so a direct cast to System.Type is not supported.
                string sqlType = (string)row["DATA_TYPE"];
                Type systemType = SqlToCLRType(sqlType);
                ParameterDefinition paramDefinition = new()
                {
                    SystemType = systemType,
                    DbType = TypeHelper.GetDbTypeFromSystemType(systemType)
                };

                // Add to parameters dictionary without the leading @ or : sign
                string paramName = ((string)row["ARGUMENT_NAME"]).TrimStart('@', ':');
                storedProcedureDefinition.Parameters.TryAdd(paramName, paramDefinition);
            }

            // Loop through parameters specified in config, throw error if not found in schema
            // else set runtime config defined default values.
            // Note: we defer type checking of parameters specified in config until request time
            List<ParameterMetadata>? configParameters = procedureEntity.Source.Parameters;
            if (configParameters is not null)
            {
                foreach (ParameterMetadata paramMeta in configParameters)
                {
                    string configParamKey = paramMeta.Name;
                    if (!storedProcedureDefinition.Parameters.TryGetValue(configParamKey, out ParameterDefinition? parameterDefinition))
                    {
                        var exception = new DataApiBuilderException(
                            message: $"Could not find parameter \"{configParamKey}\" specified in config for procedure \"{schemaName}.{storedProcedureSourceName}\"",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

                        if (_isValidateOnly)
                        {
                            SqlMetadataExceptions.Add(exception);
                        }
                        else
                        {
                            throw exception;
                        }
                    }
                    else
                    {
                        // Map all metadata from config
                        parameterDefinition.Description = paramMeta.Description;
                        parameterDefinition.Required = paramMeta.Required;
                        parameterDefinition.Default = paramMeta.Default;
                        parameterDefinition.HasConfigDefault = paramMeta.Default is not null;
                        parameterDefinition.ConfigDefaultValue = paramMeta.Default?.ToString();
                    }
                }
            }

            // Generating exposed stored-procedure query/mutation name and adding to the dictionary mapping it to its entity name.
            GraphQLStoredProcedureExposedNameToEntityNameMap.TryAdd(GenerateStoredProcedureGraphQLFieldName(entityName, procedureEntity), entityName);
        }

        /// <summary>
        /// Takes a string version of an Oracle data type and returns its .NET common language runtime (CLR) counterpart
        /// As per Oracle documentation for type mappings
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            return sqlType.ToUpperInvariant() switch
            {
                "BOOLEAN" => typeof(bool),
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
                "CURSOR" or "REF CURSOR" => typeof(IDataReader), // Oracle cursors can be mapped to object or a specific data reader type
                _ => typeof(object)
            };
        }

        /// <summary>
        /// Oracle-specific implementation to build foreign key query parameters.
        /// Ensures that schema names and table names are converted to uppercase
        /// as Oracle stores identifiers in uppercase when unquoted.
        /// </summary>
        protected override Dictionary<string, DbConnectionParam> GetForeignKeyQueryParams(
            string[] schemaNames,
            string[] tableNames)
        {
            // Build the parameters dictionary using the base implementation
            Dictionary<string, DbConnectionParam> parameters = new();
            string[] schemaNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.SCHEMA_NAME_PARAM,
                    schemaNames.Count());
            string[] tableNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.TABLE_NAME_PARAM,
                    tableNames.Count());

            // Add schema name parameters with uppercase values
            for (int i = 0; i < schemaNames.Count(); ++i)
            {
                parameters.Add(schemaNameParams[i], new(schemaNames[i].ToUpperInvariant(), DbType.String));
            }

            // Add table name parameters with uppercase values
            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], new(tableNames[i].ToUpperInvariant(), DbType.String));
            }

            return parameters;
        }
    }
}
