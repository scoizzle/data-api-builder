// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        /// Oracle-specific table-name prefix formatting. Oracle stores unquoted identifiers in
        /// uppercase, so the schema and table names are uppercased before quoting so the generated
        /// identifier matches the physical object. The base implementation must NOT uppercase for
        /// every provider - PostgreSQL and MySQL (case-sensitive identifiers) rely on the base
        /// pass-through behavior.
        /// </summary>
        internal override string GetTableNameWithSchemaPrefix(string schemaName, string tableName)
        {
            IQueryBuilder queryBuilder = GetQueryBuilder();
            StringBuilder tablePrefix = new();

            if (!string.IsNullOrEmpty(schemaName))
            {
                schemaName = queryBuilder.QuoteIdentifier(schemaName.ToUpperInvariant());
                tablePrefix.Append(schemaName);
            }

            string queryPrefix = string.IsNullOrEmpty(tablePrefix.ToString()) ? string.Empty : $"{tablePrefix}.";
            return $"{queryPrefix}{SqlQueryBuilder.QuoteIdentifier(tableName.ToUpperInvariant())}";
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

                    // CLOB/NCLOB columns are surfaced as System.String by the driver, but their
                    // bind/output types must not be limited to VARCHAR2(4000) - the Oracle query
                    // builder maps these to OracleDbType.Clob so RETURNING ... INTO binds hold
                    // values larger than 4000 characters.
                    if (physicalType is not null
                        && (physicalType.Equals("CLOB", StringComparison.OrdinalIgnoreCase)
                            || physicalType.Equals("NCLOB", StringComparison.OrdinalIgnoreCase)))
                    {
                        columnDefinition.IsClob = true;
                    }

                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);
                }
            }
        }

        /// <summary>
        /// Preserve the physical column name exactly as Oracle reports it (uppercase for unquoted
        /// identifiers, exact case for quoted ones). The backing column name stored on
        /// <see cref="SourceDefinition.Columns"/> and <see cref="SourceDefinition.PrimaryKey"/> is
        /// emitted verbatim by <see cref="OracleQueryBuilder"/>, so preserving casing lets quoted
        /// lowercase/mixed-case columns resolve correctly. Exposed REST/GraphQL field names are
        /// kept lowercase separately via <see cref="GetExposedColumnName"/>.
        /// </summary>
        protected override string GetPhysicalDatabaseColumnName(string columnName)
        {
            return columnName;
        }

        /// <summary>
        /// Oracle stores unquoted identifiers in uppercase, so an unaliased column would otherwise
        /// surface as an UPPERCASE REST/GraphQL field name, inconsistent with the other SQL
        /// providers (MsSql/PostgreSQL/MySQL). Lowercase the exposed name so the API schema matches
        /// the other providers while the backing (physical) name is emitted unchanged in SQL.
        /// </summary>
        protected override string GetExposedColumnName(string backingColumnName)
        {
            return backingColumnName.ToLowerInvariant();
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
        /// For a subprogram inside a package (source "schema.package.subprogram"), the underlying
        /// <see cref="DatabaseStoredProcedure"/> carries the PackageName and whether the subprogram
        /// is a function, which the ODP.NET schema collections have no direct representation for,
        /// so those are resolved against ALL_ARGUMENTS/ALL_PROCEDURES below.
        /// </summary>
        protected override async Task FillSchemaForStoredProcedureAsync(
            Azure.DataApiBuilder.Config.ObjectModel.Entity procedureEntity,
            string entityName,
            string schemaName,
            string storedProcedureSourceName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            // The database object already carries the Oracle package/function metadata parsed from
            // the config source in PopulateDatabaseObjectForEntity. Surface it here so downstream
            // discovery branches on it.
            DatabaseStoredProcedure dbSp = (DatabaseStoredProcedure)EntityToDatabaseObject[entityName];

            using OracleConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            await conn.OpenAsync();

            string schemaFilter = string.IsNullOrEmpty(schemaName) ? GetDefaultSchemaName() : schemaName.ToUpperInvariant();
            string objectFilter = storedProcedureSourceName.ToUpperInvariant();

            // ALL_ARGUMENTS keys a standalone subprogram by (OWNER, OBJECT_NAME) with PACKAGE_NAME IS
            // NULL, and a packaged subprogram by (OWNER, PACKAGE_NAME, OBJECT_NAME). A function
            // additionally exposes its RETURN value as a POSITION 0 row with a NULL ARGUMENT_NAME.
            string packageClause = dbSp.PackageName is null
                ? "PACKAGE_NAME IS NULL"
                : "PACKAGE_NAME = :package_name";
            string argumentsQuery =
                "SELECT ARGUMENT_NAME, DATA_TYPE, IN_OUT, POSITION " +
                "FROM ALL_ARGUMENTS " +
                "WHERE OWNER = :owner " +
                "AND OBJECT_NAME = :object_name " +
                $"AND {packageClause} " +
                "ORDER BY POSITION";

            bool isFunction = false;
            using (OracleCommand command = conn.CreateCommand())
            {
                command.BindByName = true;
                command.CommandText = argumentsQuery;
                command.Parameters.Add(new OracleParameter("owner", schemaFilter));
                command.Parameters.Add(new OracleParameter("object_name", objectFilter));
                if (dbSp.PackageName is not null)
                {
                    command.Parameters.Add(new OracleParameter("package_name", dbSp.PackageName.ToUpperInvariant()));
                }

                using OracleDataReader reader = await command.ExecuteReaderAsync();
                if (reader.HasRows)
                {
                    while (await reader.ReadAsync())
                    {
                        int position = Convert.ToInt32(reader.GetValue(3));
                        if (position == 0)
                        {
                            // POSITION 0 is a function's RETURN value, not an input argument.
                            isFunction = true;
                            continue;
                        }

                        if (reader.IsDBNull(0))
                        {
                            continue;
                        }

                        string argumentName = reader.GetString(0);
                        string dataType = reader.GetString(1);
                        Type systemType = SqlToCLRType(dataType);
                        storedProcedureDefinition.Parameters.TryAdd(
                            argumentName.TrimStart('@', ':'),
                            new ParameterDefinition
                            {
                                SystemType = systemType,
                                DbType = TypeHelper.GetDbTypeFromSystemType(systemType)
                            });
                    }
                }
                else if (!await OracleSubprogramExistsAsync(conn, schemaFilter, dbSp, objectFilter))
                {
                    // No ALL_ARGUMENTS rows: either a parameterless subprogram (valid) or a
                    // non-existent object (error). Distinguish via ALL_PROCEDURES.
                    var notFoundException = new DataApiBuilderException(
                        message: $"No stored procedure definition found for the given database object {schemaFilter}.{objectFilter}",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

                    if (_isValidateOnly)
                    {
                        SqlMetadataExceptions.Add(notFoundException);
                        return;
                    }
                    else
                    {
                        throw notFoundException;
                    }
                }
            }

            dbSp.IsFunction = isFunction;

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
        /// Confirms the configured Oracle subprogram exists when ALL_ARGUMENTS returned no rows
        /// (a parameterless subprogram has no argument metadata). Standalone subprograms are keyed by
        /// OBJECT_NAME with a NULL PROCEDURE_NAME; packaged subprograms are keyed by OBJECT_NAME =
        /// package name and PROCEDURE_NAME = subprogram name.
        /// </summary>
        private static async Task<bool> OracleSubprogramExistsAsync(
            OracleConnection conn,
            string schema,
            DatabaseStoredProcedure dbSp,
            string objectFilter)
        {
            string query = dbSp.PackageName is null
                ? "SELECT 1 FROM ALL_PROCEDURES " +
                  "WHERE OWNER = :owner AND OBJECT_NAME = :object_name " +
                  "AND PROCEDURE_NAME IS NULL AND OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION') " +
                  "FETCH FIRST 1 ROWS ONLY"
                : "SELECT 1 FROM ALL_PROCEDURES " +
                  "WHERE OWNER = :owner AND OBJECT_NAME = :package_name AND PROCEDURE_NAME = :object_name " +
                  "FETCH FIRST 1 ROWS ONLY";

            using OracleCommand command = conn.CreateCommand();
            command.BindByName = true;
            command.CommandText = query;
            command.Parameters.Add(new OracleParameter("owner", schema));
            command.Parameters.Add(new OracleParameter("object_name", objectFilter));
            if (dbSp.PackageName is not null)
            {
                command.Parameters.Add(new OracleParameter("package_name", dbSp.PackageName.ToUpperInvariant()));
            }

            using OracleDataReader reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync();
        }

        /// <summary>
        /// Overrides the base stored-procedure result-set discovery to be Oracle package/function
        /// aware. The base implementation builds the ALL_ARGUMENTS query from only the
        /// schema.subprogram name, which is insufficient for a subprogram inside a package (whose
        /// arguments are keyed by PACKAGE_NAME plus the bare subprogram name) or for a function
        /// (whose result set is its RETURN value at POSITION 0). For those, the package-aware
        /// OracleQueryBuilder query is used so the result set definition is populated correctly.
        /// </summary>
        protected override async Task PopulateResultSetDefinitionsForStoredProcedureAsync(
            string schemaName,
            string storedProcedureName,
            SourceDefinition sourceDefinition,
            string entityName)
        {
            StoredProcedureDefinition storedProcedureDefinition = (StoredProcedureDefinition)sourceDefinition;
            DatabaseStoredProcedure dbSp = (DatabaseStoredProcedure)EntityToDatabaseObject[entityName];

            string resultQuery;
            if (dbSp.IsFunction)
            {
                // A function's result set is its RETURN value (ALL_ARGUMENTS POSITION 0), regardless
                // of it being standalone (package name null) or packaged.
                resultQuery = ((OracleQueryBuilder)SqlQueryBuilder).BuildStoredProcedureResultDetailsQuery(
                    schemaName: schemaName,
                    packageName: dbSp.PackageName,
                    subprogramName: storedProcedureName,
                    isFunction: true);
            }
            else if (string.IsNullOrEmpty(dbSp.PackageName))
            {
                // Standalone procedure - use the shared query built from schema.subprogram.
                resultQuery = SqlQueryBuilder.BuildStoredProcedureResultDetailsQuery($"{schemaName}.{storedProcedureName}");
            }
            else
            {
                // Packaged procedure - its OUT cursor parameter(s) define the result set.
                resultQuery = ((OracleQueryBuilder)SqlQueryBuilder).BuildStoredProcedureResultDetailsQuery(
                    schemaName: schemaName,
                    packageName: dbSp.PackageName,
                    subprogramName: storedProcedureName,
                    isFunction: false);
            }

            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: resultQuery,
                parameters: null!,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync,
                dataSourceName: _dataSourceName);

            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                if (!TryGetPropertyByCaseInsensitiveName(element, BaseSqlQueryBuilder.STOREDPROC_COLUMN_NAME, out JsonElement nameElement))
                {
                    throw new DataApiBuilderException(
                        message: $"Unexpected stored procedure metadata shape for '{schemaName}.{storedProcedureName}': " +
                                $"missing column '{BaseSqlQueryBuilder.STOREDPROC_COLUMN_NAME}'.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                if (!TryGetPropertyByCaseInsensitiveName(element, BaseSqlQueryBuilder.STOREDPROC_COLUMN_SYSTEMTYPENAME, out JsonElement typeElement))
                {
                    throw new DataApiBuilderException(
                        message: $"Unexpected stored procedure metadata shape for '{schemaName}.{storedProcedureName}': " +
                                $"missing column '{BaseSqlQueryBuilder.STOREDPROC_COLUMN_SYSTEMTYPENAME}'.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                string resultFieldName = nameElement.ToString();
                Type resultFieldType = SqlToCLRType(typeElement.ToString());

                // A function's RETURN value is reported under a NULL ARGUMENT_NAME. Only a function
                // that returns a REF CURSOR declares a result set (captured through the :dab_result
                // bind); a scalar function has no result-set definition and is invoked by
                // OracleQueryBuilder as SELECT ... FROM DUAL, so skip it here.
                if (dbSp.IsFunction && resultFieldType != typeof(IDataReader))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resultFieldName))
                {
                    // Name a function's cursor result after the subprogram so it is addressable.
                    resultFieldName = dbSp.IsFunction ? storedProcedureName : resultFieldName;
                }

                if (string.IsNullOrWhiteSpace(resultFieldName))
                {
                    throw new DataApiBuilderException(
                        message: $"The stored procedure '{schemaName}.{storedProcedureName}' returns a column without a name. " +
                                "This typically happens when using aggregate functions (like MAX, MIN, COUNT) or expressions " +
                                "without providing an alias. Please add column aliases to your SELECT statement. " +
                                "For example: 'SELECT MAX(id) AS MaxId' instead of 'SELECT MAX(id)'.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                storedProcedureDefinition.Columns.TryAdd(resultFieldName, new(resultFieldType) { IsNullable = true });
            }

            static bool TryGetPropertyByCaseInsensitiveName(JsonElement element, string name, out JsonElement value)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }

                value = default;
                return false;
            }
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
