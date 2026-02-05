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
            return columnsInTable;
        }

        /// <summary>
        /// Oracle-specific implementation to populate column definition with HasDefault and DbType.
        /// Oracle's schema metadata may not include default value information via GetSchema,
        /// so we skip the default value population for now.
        /// </summary>
        protected override void PopulateColumnDefinitionWithHasDefaultAndDbType(
            SourceDefinition sourceDefinition,
            DataTable allColumnsInTable)
        {
            foreach (DataRow columnInfo in allColumnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                
                if (sourceDefinition.Columns.TryGetValue(columnName, out ColumnDefinition? columnDefinition))
                {
                    // For Oracle, we'll assume no defaults for now since GetSchema doesn't provide
                    // default value information reliably. This can be enhanced later with direct
                    // queries to ALL_TAB_COLUMNS if needed.
                    columnDefinition.HasDefault = false;
                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);
                }
            }
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
