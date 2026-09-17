// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
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
        /// <summary>
        /// Maps SourceDefinition instances to their (schemaName, tableName) pairs.
        /// Populated during PopulateTriggerMetadataForTable so that
        /// PopulateColumnDefinitionWithHasDefaultAndDbType can detect trigger-based
        /// identity columns without needing the schema/table name as parameters.
        /// </summary>
        private readonly Dictionary<SourceDefinition, (string SchemaName, string TableName)> _sourceTableMap = new();
        private string? _defaultSchemaName;

        private static readonly Regex SelectIntoIdentityRegex = new(
            @"SELECT\s+(?:\w+\.)?\w+\.NEXTVAL\s+INTO\s+:new\.(""?\w+""?)\s+FROM\s+dual",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AssignmentIdentityRegex = new(
            @":new\.(""?\w+""?)\s*:=\s*(?:\w+\.)?\w+\.NEXTVAL",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        /// Gets the default schema name for Oracle (the connected user's schema).
        /// Prefer User ID from the connection string; if it is missing (wallet / IAM),
        /// query <c>SELECT USER FROM DUAL</c>. Never fall back to SYSTEM.
        /// </summary>
        public override string GetDefaultSchemaName()
        {
            if (_defaultSchemaName is not null)
            {
                return _defaultSchemaName;
            }

            if (TryGetSchemaFromConnectionString(ConnectionString, out string schemaFromUserId))
            {
                _defaultSchemaName = schemaFromUserId;
                return _defaultSchemaName;
            }

            _defaultSchemaName = ResolveSchemaFromSessionUser();
            return _defaultSchemaName;
        }

        /// <summary>
        /// Extracts the Oracle schema from a connection-string User ID.
        /// Returns false when User ID is missing or the string cannot be parsed so callers
        /// can query SESSION USER instead of assuming SYSTEM.
        /// </summary>
        public static bool TryGetSchemaFromConnectionString(string connectionString, out string schema)
        {
            schema = string.Empty;
            try
            {
                OracleConnectionStringBuilder builder = new(connectionString);
                if (string.IsNullOrWhiteSpace(builder.UserID))
                {
                    return false;
                }

                schema = builder.UserID.ToUpperInvariant();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string ResolveSchemaFromSessionUser()
        {
            try
            {
                using OracleConnection connection = new(ConnectionString);
                connection.Open();
                using OracleCommand command = connection.CreateCommand();
                command.CommandText = "SELECT USER FROM DUAL";
                object? result = command.ExecuteScalar();
                string? user = result?.ToString();
                if (!string.IsNullOrWhiteSpace(user))
                {
                    return user.ToUpperInvariant();
                }
            }
            catch (Exception ex)
            {
                throw new DataApiBuilderException(
                    "Unable to determine the Oracle default schema: the connection string has no User ID and SELECT USER FROM DUAL failed.",
                    HttpStatusCode.ServiceUnavailable,
                    DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }

            throw new DataApiBuilderException(
                "Unable to determine the Oracle default schema: the connection string has no User ID and SESSION USER was empty.",
                HttpStatusCode.ServiceUnavailable,
                DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        /// <summary>
        /// Resolve config-authored sources (and FK pair tables) to the local base object
        /// before FillSchema. Private synonyms in the current schema, then public synonyms
        /// for unqualified sources. SQL and catalog lookups then use physical names.
        /// </summary>
        protected override async Task ResolveCatalogObjectNamesAsync()
        {
            Dictionary<(string Owner, string Name), string?> objectTypeCache = new();
            Dictionary<(string Owner, string Name), OracleCatalogNameResolver.SynonymRow?> synonymCache = new();

            HashSet<DatabaseObject> seen = new(ReferenceEqualityComparer.Instance);
            foreach (DatabaseObject databaseObject in EntityToDatabaseObject.Values)
            {
                await ResolveDatabaseObjectAsync(databaseObject, seen, objectTypeCache, synonymCache);
            }
        }

        private async Task ResolveDatabaseObjectAsync(
            DatabaseObject databaseObject,
            HashSet<DatabaseObject> seen,
            Dictionary<(string Owner, string Name), string?> objectTypeCache,
            Dictionary<(string Owner, string Name), OracleCatalogNameResolver.SynonymRow?> synonymCache)
        {
            if (!seen.Add(databaseObject))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(databaseObject.SchemaName)
                || string.IsNullOrWhiteSpace(databaseObject.Name))
            {
                return;
            }

            if (databaseObject is DatabaseStoredProcedure storedProcedure
                && !string.IsNullOrEmpty(storedProcedure.PackageName))
            {
                return;
            }

            bool allowPublicFallback =
                databaseObject.SchemaName.Equals(OracleCatalogNameResolver.PublicOwner, StringComparison.OrdinalIgnoreCase)
                || databaseObject.SchemaName.Equals(GetDefaultSchemaName(), StringComparison.OrdinalIgnoreCase);

            (string resolvedSchema, string resolvedName) = await OracleCatalogNameResolver.ResolveAsync(
                databaseObject.SchemaName,
                databaseObject.Name,
                allowPublicFallback,
                (owner, name) => GetObjectTypeAsync(owner, name, objectTypeCache),
                (owner, name) => GetSynonymAsync(owner, name, synonymCache));

            string? resolvedType = await GetObjectTypeAsync(resolvedSchema, resolvedName, objectTypeCache);
            if (resolvedType is not null)
            {
                bool isTableOrView = databaseObject.SourceType is EntitySourceType.Table or EntitySourceType.View;
                if (isTableOrView && OracleCatalogNameResolver.IsSubprogramLike(resolvedType))
                {
                    throw new DataApiBuilderException(
                        message: $"Oracle source {databaseObject.SchemaName}.{databaseObject.Name} resolved to {resolvedType}, which does not match the configured table/view source type.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                if (databaseObject.SourceType is EntitySourceType.StoredProcedure
                    && OracleCatalogNameResolver.IsTableLike(resolvedType))
                {
                    throw new DataApiBuilderException(
                        message: $"Oracle source {databaseObject.SchemaName}.{databaseObject.Name} resolved to {resolvedType}, which does not match the configured stored-procedure source type.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }

            databaseObject.SchemaName = resolvedSchema;
            databaseObject.Name = resolvedName;

            if (databaseObject.SourceType is EntitySourceType.Table or EntitySourceType.View
                && databaseObject.SourceDefinition is not null
                && databaseObject.SourceDefinition.SourceEntityRelationshipMap.Count > 0)
            {
                foreach (RelationshipMetadata relationshipMetadata in databaseObject.SourceDefinition.SourceEntityRelationshipMap.Values)
                {
                    foreach (List<ForeignKeyDefinition> foreignKeys in relationshipMetadata.TargetEntityToFkDefinitionMap.Values)
                    {
                        foreach (ForeignKeyDefinition foreignKey in foreignKeys)
                        {
                            await ResolveDatabaseObjectAsync(foreignKey.Pair.ReferencingDbTable, seen, objectTypeCache, synonymCache);
                            await ResolveDatabaseObjectAsync(foreignKey.Pair.ReferencedDbTable, seen, objectTypeCache, synonymCache);
                        }
                    }
                }
            }
        }

        private async Task<string?> GetObjectTypeAsync(
            string owner,
            string name,
            Dictionary<(string Owner, string Name), string?> cache)
        {
            (string Owner, string Name) key = (owner, name);
            if (cache.TryGetValue(key, out string? cached))
            {
                return cached;
            }

            string? objectType = null;
            using OracleConnection connection = new(ConnectionString);
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(connection, _dataSourceName);
            await connection.OpenAsync();
            using OracleCommand command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText =
                "SELECT OBJECT_TYPE FROM ALL_OBJECTS " +
                "WHERE OWNER = :owner AND OBJECT_NAME = :object_name " +
                "AND OBJECT_TYPE IN ('TABLE','VIEW','SYNONYM','PROCEDURE','FUNCTION','PACKAGE','MATERIALIZED VIEW') " +
                "AND ROWNUM = 1";
            command.Parameters.Add(new OracleParameter("owner", owner));
            command.Parameters.Add(new OracleParameter("object_name", name));
            object? value = await command.ExecuteScalarAsync();
            if (value is string typeName && !string.IsNullOrWhiteSpace(typeName))
            {
                objectType = typeName;
            }

            cache[key] = objectType;
            return objectType;
        }

        private async Task<OracleCatalogNameResolver.SynonymRow?> GetSynonymAsync(
            string owner,
            string name,
            Dictionary<(string Owner, string Name), OracleCatalogNameResolver.SynonymRow?> cache)
        {
            (string Owner, string Name) key = (owner, name);
            if (cache.TryGetValue(key, out OracleCatalogNameResolver.SynonymRow? cached))
            {
                return cached;
            }

            OracleCatalogNameResolver.SynonymRow? synonym = null;
            using OracleConnection connection = new(ConnectionString);
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(connection, _dataSourceName);
            await connection.OpenAsync();
            using OracleCommand command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText =
                "SELECT OWNER, SYNONYM_NAME, TABLE_OWNER, TABLE_NAME, DB_LINK " +
                "FROM ALL_SYNONYMS " +
                "WHERE OWNER = :owner AND SYNONYM_NAME = :synonym_name";
            command.Parameters.Add(new OracleParameter("owner", owner));
            command.Parameters.Add(new OracleParameter("synonym_name", name));
            using OracleDataReader reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                synonym = new OracleCatalogNameResolver.SynonymRow(
                    Owner: reader.GetString(0),
                    SynonymName: reader.GetString(1),
                    TableOwner: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    TableName: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    DbLink: reader.IsDBNull(4) ? null : reader.GetString(4));
            }

            cache[key] = synonym;
            return synonym;
        }

        /// <summary>
        /// Unquoted catalog objects are stored UPPERCASE and DAB quotes them, so both parts
        /// go through <see cref="OracleQueryBuilder.QuoteRelation"/>.
        /// </summary>
        internal override string GetTableNameWithSchemaPrefix(string schemaName, string tableName)
        {
            return ((OracleQueryBuilder)GetQueryBuilder()).QuoteRelation(schemaName, tableName);
        }

        /// <summary>
        /// Oracle-specific implementation to populate trigger metadata for a table.
        /// Stores the schema/table name mapping for later use by trigger-based
        /// identity column detection.
        /// </summary>
        public override async Task PopulateTriggerMetadataForTable(string entityName, string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            // Store the mapping for use in PopulateColumnDefinitionWithHasDefaultAndDbType
            // where we detect trigger-based identity columns.
            _sourceTableMap[sourceDefinition] = (schemaName, tableName);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Auto-generated linking entities (M:N multiple-create) have no config entity to declare
        /// exposed names, and Oracle folds unquoted identifiers to uppercase. Without defaults such
        /// a linking column (e.g. ROYALTY_PERCENTAGE) would surface uppercase in the generated
        /// multiple-create input type, diverging from the relationship config's authored linking
        /// field names and the other SQL providers. Seed lowercase mappings for every linking column.
        /// </summary>
        protected override void PopulateLinkingEntityExposedNames()
        {
            foreach ((string linkingEntityName, Entity linkingEntity) in _linkingEntities)
            {
                if (!EntityToDatabaseObject.TryGetValue(linkingEntityName, out DatabaseObject? databaseObject))
                {
                    continue;
                }

                Dictionary<string, string> mappings = new(StringComparer.OrdinalIgnoreCase);
                foreach (string columnName in databaseObject.SourceDefinition.Columns.Keys)
                {
                    mappings[columnName] = columnName.ToLowerInvariant();
                }

                _linkingEntities[linkingEntityName] = linkingEntity with { Mappings = mappings };
            }
        }

        /// <summary>
        /// Oracle-specific implementation to get column metadata.
        /// Oracle only supports 3 restrictions (Owner, Table, Column) in GetSchema for Columns,
        /// unlike SQL Server which supports 4 (Database, Schema, Table, Column).
        /// </summary>
        protected override async Task<DataTable> GetColumnsAsync(
            string schemaName,
            string tableName,
            CancellationToken cancellationToken)
        {
            using OracleConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName, cancellationToken);
            await conn.OpenAsync(cancellationToken);

            // Oracle only supports 3 restrictions: Owner (schema), Table Name, Column Name
            // Setting database (index 0) causes ORA-50019 error
            string?[] columnRestrictions = new string?[3];

            // For Oracle: use the schema name if provided, otherwise null (defaults to current user)
            // Oracle stores identifiers in uppercase when unquoted
            columnRestrictions[0] = string.IsNullOrEmpty(schemaName) ? null : schemaName.ToUpperInvariant();
            columnRestrictions[1] = tableName.ToUpperInvariant(); // Table name
            columnRestrictions[2] = null; // Column name (null means all columns)

            DataTable columnsInTable = await conn.GetSchemaAsync("Columns", columnRestrictions, cancellationToken);
            await AddColumnDefaultsAsync(conn, columnsInTable, schemaName, tableName, cancellationToken);
            return columnsInTable;
        }

        private static async Task AddColumnDefaultsAsync(
            OracleConnection connection,
            DataTable columns,
            string schemaName,
            string tableName,
            CancellationToken cancellationToken)
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

            using OracleDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            Dictionary<string, object?> defaults = new(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
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
                // Keys on sourceDefinition.Columns are the catalog spelling (pass-through
                // GetPhysicalDatabaseColumnName). Match that spelling here.
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

            // Detect trigger-based identity columns (e.g., BEFORE INSERT triggers that
            // populate :new.id from a sequence). ODP.NET's FillSchema reports
            // IsAutoIncrement=false for these, so we must detect them explicitly.
            DetectTriggerBasedIdentityColumns(sourceDefinition);
        }

        /// <summary>
        /// Queries ALL_TRIGGERS for BEFORE INSERT triggers on the table and parses their
        /// trigger bodies to detect columns populated from sequences. When found, those
        /// columns are marked as IsAutoGenerated=true and IsReadOnly=true so the
        /// multiple-create validator and insert builders treat them as identity columns.
        /// </summary>
        private void DetectTriggerBasedIdentityColumns(SourceDefinition sourceDefinition)
        {
            try
            {
                // Look up the schema/table name from the mapping populated in
                // PopulateTriggerMetadataForTable.
                if (!_sourceTableMap.TryGetValue(sourceDefinition, out var tableInfo))
                {
                    return;
                }

                string schemaName = tableInfo.SchemaName;
                string tableName = tableInfo.TableName;

                if (string.IsNullOrEmpty(schemaName) || string.IsNullOrEmpty(tableName))
                {
                    return;
                }

                // Find BEFORE INSERT triggers on this table.
                string triggerQuery =
                    "SELECT TRIGGER_BODY FROM ALL_TRIGGERS " +
                    "WHERE OWNER = :owner AND TABLE_NAME = :table_name " +
                    "AND TRIGGERING_EVENT LIKE 'INSERT%' " +
                    "AND STATUS = 'ENABLED'";

                using OracleConnection conn = new();
                conn.ConnectionString = ConnectionString;

                // Managed-identity/wallet connection strings carry no password, so the access
                // token must be applied before opening (as GetColumnsAsync does). This metadata
                // hook is synchronous, so block on the async token acquisition; there is no
                // SynchronizationContext during metadata init.
                QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName).GetAwaiter().GetResult();

                // Synchronous open is acceptable here during metadata init.
                conn.Open();

                using OracleCommand cmd = conn.CreateCommand();
                cmd.BindByName = true;
                cmd.CommandText = triggerQuery;
                cmd.Parameters.Add(new OracleParameter("owner", schemaName.ToUpperInvariant()));
                cmd.Parameters.Add(new OracleParameter("table_name", tableName.ToUpperInvariant()));

                // ODP.NET may need LONG fetch for trigger bodies.
                cmd.InitialLONGFetchSize = -1;

                using OracleDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    string triggerBody = reader.GetString(0);
                    ParseTriggerBodyForIdentityColumns(triggerBody, sourceDefinition);
                }
            }
            catch (Exception ex)
            {
                // If trigger detection fails, fall back to the driver's IsAutoIncrement.
                // This is non-fatal; the worst case is that trigger-based identity columns
                // are not detected and the user must supply the value explicitly.
                _logger.LogWarning(
                    ex,
                    "Failed to detect Oracle trigger-based identity columns; falling back to driver IsAutoIncrement metadata.");
            }
        }

        /// <summary>
        /// Parses a BEFORE INSERT trigger body for columns populated from sequences.
        /// Matches <c>SELECT [schema.]seq.NEXTVAL INTO :new.col FROM dual</c> and
        /// <c>:new.col := [schema.]seq.NEXTVAL</c>.
        /// </summary>
        private static void ParseTriggerBodyForIdentityColumns(string triggerBody, SourceDefinition sourceDefinition)
        {
            foreach (string columnName in FindTriggerAssignedColumnNames(triggerBody))
            {
                string normalizedColumnName = columnName.ToUpperInvariant();

                if (sourceDefinition.Columns.TryGetValue(normalizedColumnName, out ColumnDefinition? columnDef) ||
                    sourceDefinition.Columns.TryGetValue(columnName.ToLowerInvariant(), out columnDef) ||
                    sourceDefinition.Columns.TryGetValue(columnName, out columnDef))
                {
                    columnDef.IsAutoGenerated = true;
                    columnDef.IsReadOnly = true;
                }
            }
        }

        /// <summary>
        /// Returns column names assigned from a sequence in a trigger body (quotes stripped).
        /// </summary>
        internal static IReadOnlyList<string> FindTriggerAssignedColumnNames(string triggerBody)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in SelectIntoIdentityRegex.Matches(triggerBody))
            {
                names.Add(match.Groups[1].Value.Trim('"'));
            }

            foreach (Match match in AssignmentIdentityRegex.Matches(triggerBody))
            {
                names.Add(match.Groups[1].Value.Trim('"'));
            }

            return [.. names];
        }

        /// <summary>
        /// Preserve the physical column name exactly as Oracle reports it (uppercase for unquoted
        /// identifiers, exact case for quoted ones). The backing column name stored on
        /// <see cref="SourceDefinition.Columns"/> and <see cref="SourceDefinition.PrimaryKey"/> is
        /// emitted verbatim by <see cref="OracleQueryBuilder"/>, so preserving casing lets quoted
        /// lowercase/mixed-case columns resolve correctly. The exposed REST/GraphQL name is the
        /// catalog spelling unless the entity config supplies a mapping or field alias; it is never
        /// lowercased.
        /// </summary>
        protected override string GetPhysicalDatabaseColumnName(string columnName)
        {
            return columnName;
        }

        /// <summary>
        /// Config-authored relationship fields (source.fields / linking fields) commonly use a
        /// different casing than the catalog. Join predicates quote those names, so they must be
        /// rewritten to the physical spelling stored on <see cref="SourceDefinition.Columns"/>.
        /// </summary>
        protected override void NormalizeForeignKeyColumnNames(ForeignKeyDefinition fkDefinition)
        {
            SourceDefinition? referencingDefinition = TryGetSourceDefinitionForDatabaseObject(fkDefinition.Pair.ReferencingDbTable);
            SourceDefinition? referencedDefinition = TryGetSourceDefinitionForDatabaseObject(fkDefinition.Pair.ReferencedDbTable);

            if (referencingDefinition is not null)
            {
                fkDefinition.ReferencingColumns =
                    [.. fkDefinition.ReferencingColumns.Select(column => ResolvePhysicalColumnName(referencingDefinition, column))];
            }

            if (referencedDefinition is not null)
            {
                fkDefinition.ReferencedColumns =
                    [.. fkDefinition.ReferencedColumns.Select(column => ResolvePhysicalColumnName(referencedDefinition, column))];
            }
        }

        /// <summary>
        /// The FK pair may hold a distinct <see cref="DatabaseTable"/> instance from the one stored
        /// in EntityToDatabaseObject (especially linking tables). Match by schema/name when the
        /// pair's own TableDefinition is empty.
        /// </summary>
        private SourceDefinition? TryGetSourceDefinitionForDatabaseObject(DatabaseObject dbObject)
        {
            if (dbObject is DatabaseTable table
                && table.TableDefinition is not null
                && table.TableDefinition.Columns.Count > 0)
            {
                return table.TableDefinition;
            }

            foreach (DatabaseObject entityObject in EntityToDatabaseObject.Values)
            {
                if (entityObject.Equals(dbObject)
                    && entityObject is DatabaseTable entityTable
                    && entityTable.TableDefinition is not null
                    && entityTable.TableDefinition.Columns.Count > 0)
                {
                    return entityTable.TableDefinition;
                }
            }

            return null;
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
        /// Oracle stores unquoted identifiers in UPPERCASE, so the schema bind for the read-only
        /// (virtual column) query must be uppercased regardless of the casing authored in the
        /// config source, matching the FK and column-metadata lookups.
        /// </summary>
        protected override string GetSchemaOrDatabaseNameForReadOnlyColumnQuery(string schemaOrDatabaseName)
        {
            return schemaOrDatabaseName.ToUpperInvariant();
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
            StoredProcedureDefinition storedProcedureDefinition,
            CancellationToken cancellationToken)
        {
            // The database object already carries the Oracle package/function metadata parsed from
            // the config source in PopulateDatabaseObjectForEntity. Surface it here so downstream
            // discovery branches on it.
            DatabaseStoredProcedure dbSp = (DatabaseStoredProcedure)EntityToDatabaseObject[entityName];

            using OracleConnection conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName, cancellationToken);
            await conn.OpenAsync(cancellationToken);

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

                using OracleDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                if (reader.HasRows)
                {
                    while (await reader.ReadAsync(cancellationToken))
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
                        string inOut = reader.IsDBNull(2) ? "IN" : reader.GetString(2);
                        Type systemType = SqlToCLRType(dataType);
                        // REF CURSOR is the result path (bound as :dab_result), not a client input.
                        // Pure OUT scalars are not bound as IN parameters.
                        if (systemType == typeof(IDataReader) ||
                            inOut.Equals("OUT", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        storedProcedureDefinition.Parameters.TryAdd(
                            argumentName.TrimStart('@', ':'),
                            new ParameterDefinition
                            {
                                SystemType = systemType,
                                DbType = TypeHelper.GetDbTypeFromSystemType(systemType)
                            });
                    }
                }
                else if (!await OracleSubprogramExistsAsync(conn, schemaFilter, dbSp, objectFilter, cancellationToken))
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
            string objectFilter,
            CancellationToken cancellationToken)
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

            using OracleDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken);
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
            string entityName,
            CancellationToken cancellationToken)
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
                dataSourceName: _dataSourceName,
                cancellationToken: cancellationToken);

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
                "CURSOR" or "REF CURSOR" => typeof(IDataReader),
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
