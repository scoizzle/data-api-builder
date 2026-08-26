// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Text;
using Azure.Core;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Specialized QueryExecutor for Oracle SQL mainly providing methods to
    /// handle connecting to the database with a managed identity.
    /// </summary>
    public class OracleQueryExecutor : QueryExecutor<OracleConnection>
    {
        // Scope for Oracle Cloud Infrastructure authentication
        // This may need to be configured based on the specific Oracle Cloud setup
        public const string DATABASE_SCOPE = @"https://database.oracle.com/.default";

        /// <summary>
        /// The managed identity Access Token string obtained
        /// from the configuration controller.
        /// Key: datasource name, Value: access token for this datasource.
        /// </summary>
        private Dictionary<string, string?> _accessTokensFromConfiguration;

        public DefaultAzureCredential AzureCredential { get; set; } = new(); // CodeQL [SM05137]: DefaultAzureCredential will use Managed Identity if available or fallback to default.

        /// <summary>
        /// The Oracle specific connection string builders.
        /// Key: datasource name, Value: connection string builder for this datasource.
        /// </summary>
        public override IDictionary<string, DbConnectionStringBuilder> ConnectionStringBuilders
            => base.ConnectionStringBuilders;

        /// <summary>
        /// The saved cached access token obtained from DefaultAzureCredentials
        /// representing a managed identity.
        /// </summary>
        private AccessToken? _defaultAccessToken;

        /// <summary>
        /// DatasourceName to boolean value indicating if access token should be set for db.
        /// </summary>
        private Dictionary<string, bool> _dataSourceAccessTokenUsage;

        /// <summary>
        /// DatasourceName to boolean value indicating if session context should be set for db using Oracle application contexts.
        /// </summary>
        private Dictionary<string, bool> _dataSourceToSessionContextUsage;

        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public OracleQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor httpContextAccessor,
            HotReloadEventHandler<HotReloadEventArgs>? handler = null)
            : base(dbExceptionParser,
                  logger,
                  runtimeConfigProvider,
                  httpContextAccessor,
                  handler)
        {
            _dataSourceAccessTokenUsage = new Dictionary<string, bool>();
            _dataSourceToSessionContextUsage = new Dictionary<string, bool>();
            _accessTokensFromConfiguration = runtimeConfigProvider.ManagedIdentityAccessToken;
            _runtimeConfigProvider = runtimeConfigProvider;
            ConfigureOracleQueryExecutor();
        }

        /// <summary>
        /// Configure during construction or a hot-reload scenario.
        /// </summary>
        private void ConfigureOracleQueryExecutor()
        {
            IEnumerable<KeyValuePair<string, DataSource>> oracledbs = _runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType == DatabaseType.Oracle);

            foreach ((string dataSourceName, DataSource dataSource) in oracledbs)
            {
                OracleConnectionStringBuilder builder = new(dataSource.ConnectionString);

                // PRE-CONDITION: Check if late-configured (security-sensitive scenario)
                if (_runtimeConfigProvider.IsLateConfigured)
                {
                    // POST-CONDITION: Enforce encryption for late-configured connections
                    // Oracle managed driver uses connection string encryption parameter
                    // Ensure encryption is enabled for production deployments
                    if (!builder.ConnectionString.Contains("Encryption", StringComparison.OrdinalIgnoreCase))
                    {
                        string newConnectionString = builder.ConnectionString + 
                            (builder.ConnectionString.EndsWith(";") ? "" : ";") + "Encryption=true;";
                        builder = new OracleConnectionStringBuilder(newConnectionString);
                    }
                }

                ConnectionStringBuilders.TryAdd(dataSourceName, builder);
                _dataSourceAccessTokenUsage[dataSourceName] = ShouldManagedIdentityAccessBeAttempted(builder);

                // Session context (application context) forwarding is not enabled by default for Oracle.
                //
                // DAB forwards the caller's claims to the database by prepending the session-context
                // SQL to the query text in QueryExecutor.PrepareDbCommand. For MSSQL this works because
                // the generated text is a batch of EXEC statements. Oracle, however, does not accept a
                // PL/SQL anonymous block (BEGIN ... END;) followed by a separate statement in a single
                // CommandText - prepending one would generate invalid SQL (ORA-00900) for every request.
                // Until a proper application-context setup (CREATE CONTEXT + package) is implemented
                // (see GetSessionParamsQuery), keep this disabled so ordinary queries are not broken.
                _dataSourceToSessionContextUsage[dataSourceName] = false;
            }
        }

        /// <summary>
        /// Prepares an OracleCommand for execution.
        /// Oracle named bind variables use the ':' prefix (e.g. :param1), whereas DAB's shared
        /// query structures generate '@'-prefixed parameter names (BaseQueryStructure.PARAM_NAME_PREFIX).
        /// This override translates both the command text and parameter names to the Oracle syntax so
        /// that predicates/filters built by <see cref="OracleQueryBuilder"/> execute correctly
        /// (without this, Oracle raises ORA-00936 "missing expression").
        /// </summary>
        /// <inheritdoc />
        public override DbCommand PrepareDbCommand(
            OracleConnection conn,
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            HttpContext? httpContext,
            string dataSourceName)
        {
            OracleCommand cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;

            // Add query to send user data from DAB to the underlying database to enable additional
            // security the user might have configured at the database level.
            string sessionParamsQuery = GetSessionParamsQuery(httpContext, parameters, dataSourceName);
            string translatedSql = sessionParamsQuery + TranslateBindParameters(sqltext);
            cmd.CommandText = translatedSql;

            if (parameters is not null)
            {
                // Oracle (unlike SqlClient/Npgsql/MySqlConnector) raises ORA-01006 when a parameter
                // is bound that does not have a matching bind variable in the statement text. DAB's
                // shared SqlQueryStructure unconditionally adds "column label" parameters
                // (ParametrizeColumns) that only MySQL's JSON_OBJECT builder consumes. Skip any
                // parameter that does not appear as :name in the (translated) command text.
                foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in parameters)
                {
                    string oracleName = parameterEntry.Key.TrimStart('@') ?? string.Empty;
                    if (string.IsNullOrEmpty(oracleName)
                        || !translatedSql.Contains($":{oracleName}", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    OracleParameter parameter = cmd.CreateParameter();
                    // Oracle bind names: strip the '@' DAB prefix and use ':' (i.e. the parameter
                    // collection name must match the ':name' referenced in the command text).
                    parameter.ParameterName = oracleName;
                    parameter.Value = parameterEntry.Value.Value ?? DBNull.Value;

                    PopulateDbTypeForParameter(parameterEntry, parameter);
                    cmd.Parameters.Add(parameter);
                }
            }

            return cmd;
        }

        /// <summary>
        /// Rewrites DAB's '@'-prefixed bind references to Oracle's ':'-prefixed syntax.
        /// Handles both @param0-style names and names embedded in strings; safe because it
        /// only rewrites tokens that begin with '@' followed by a letter.
        /// </summary>
        private static string TranslateBindParameters(string sqltext)
        {
            if (string.IsNullOrEmpty(sqltext) || !sqltext.Contains('@'))
            {
                return sqltext;
            }

            return System.Text.RegularExpressions.Regex.Replace(
                sqltext,
                "@([A-Za-z_][A-Za-z0-9_]*)",
                ":$1");
        }

        /// <summary>
        /// Modifies the properties of the supplied connection string to support managed identity access.
        /// In the case of Oracle, if a default managed identity needs to be used, the password in the
        /// connection needs to be replaced with the default access token.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if null</param>
        public override async Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn, string dataSourceName)
        {
            // using default datasource name for first db - maintaining backward compatibility for single db scenario.
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            _dataSourceAccessTokenUsage.TryGetValue(dataSourceName, out bool setAccessToken);

            // Only attempt to get the access token if the connection string is in the appropriate format
            if (setAccessToken)
            {
                OracleConnection sqlConn = (OracleConnection)conn;

                // If the configuration controller provided a managed identity access token use that,
                // else use the default saved access token if still valid.
                // Get a new token only if the saved token is null or expired.
                _accessTokensFromConfiguration.TryGetValue(dataSourceName, out string? accessTokenFromController);
                string? accessToken = accessTokenFromController ??
                    (IsDefaultAccessTokenValid() ?
                        ((AccessToken)_defaultAccessToken!).Token :
                        await GetAccessTokenAsync(dataSourceName));

                if (accessToken is not null)
                {
                    OracleConnectionStringBuilder newConnectionString = new(sqlConn.ConnectionString)
                    {
                        Password = accessToken
                    };
                    sqlConn.ConnectionString = newConnectionString.ToString();
                }
            }
        }

        /// <summary>
        /// Determines if managed identity access should be attempted or not.
        /// It should only be attempted if the password is not provided
        /// </summary>
        private static bool ShouldManagedIdentityAccessBeAttempted(OracleConnectionStringBuilder builder)
        {
            return string.IsNullOrEmpty(builder.Password);
        }

        /// <summary>
        /// Determines if the saved default azure credential's access token is valid and not expired.
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        private bool IsDefaultAccessTokenValid()
        {
            return _defaultAccessToken is not null &&
                ((AccessToken)_defaultAccessToken).ExpiresOn.CompareTo(DateTimeOffset.Now) > 0;
        }

        /// <summary>
        /// Tries to get an access token using DefaultAzureCredentials.
        /// Catches any CredentialUnavailableException and logs only a warning
        /// since this is best effort.
        /// </summary>
        /// <returns>The string representation of the access token if found,
        /// null otherwise.</returns>
        private async Task<string?> GetAccessTokenAsync(string dataSourceName)
        {
            bool firstAttemptAtDefaultAccessToken = _defaultAccessToken is null;

            try
            {
                _defaultAccessToken =
                    await AzureCredential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }));
            }
            // because there can be scenarios where password is not specified but
            // default managed identity is not the intended method of authentication
            // so a bunch of different exceptions could occur in that scenario
            catch (Exception ex)
            {
                string messagePrefix = "{correlationId} No password detected in the connection string. Attempt to retrieve a managed identity access token using DefaultAzureCredential failed due to:\n{errorMessage}";
                string messageSuffix = (firstAttemptAtDefaultAccessToken ? $"If authentication with DefaultAzureCrendential is not intended, this warning can be safely ignored." : string.Empty);
                string message = messagePrefix + messageSuffix;
                QueryExecutorLogger.LogWarning(
                    exception: ex,
                    message: message,
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext),
                    ex.Message);

                // the config doesn't contain an identity token
                // and a default identity token cannot be obtained
                // so the application should not attempt to set the token
                // for future conntions
                // note though that if a default access token has been previously
                // obtained successfully (firstAttemptAtDefaultAccessToken == false)
                // this might be a transitory failure don't disable attempts to set
                // the token
                //
                // disabling the attempts is useful in scenarios where the user
                // has a valid connection string without a password in it
                if (firstAttemptAtDefaultAccessToken)
                {
                    _dataSourceAccessTokenUsage[dataSourceName] = false;
                }
            }

            return _defaultAccessToken?.Token;
        }

        /// <summary>
        /// Method to generate the query to send user data to the underlying Oracle database via application contexts
        /// which can be used for additional security (e.g., using Virtual Private Database policies) at the database level.
        /// Oracle application contexts are similar to SQL Server's SESSION_CONTEXT.
        /// </summary>
        /// <param name="httpContext">Current user httpContext.</param>
        /// <param name="parameters">Dictionary of parameters/value required to execute the query.</param>
        /// <param name="dataSourceName">Name of datasource. Default dbName taken from config if null</param>
        /// <returns>empty string / query to set session parameters for the connection.</returns>
        /// <seealso cref="https://docs.oracle.com/en/database/oracle/oracle-database/19/dbseg/using-application-contexts-to-retrieve-user-information.html"/>
        public override string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, DbConnectionParam> parameters, string dataSourceName = "")
        {
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            // Session-context forwarding is disabled by default for Oracle (see ConfigureOracleQueryExecutor)
            // because a PL/SQL anonymous block cannot be prepended to a separate statement in a single
            // OracleCommand. When support is enabled via a future config option, the application context
            // must be created in the database first (CREATE CONTEXT dab_context USING dab_context_pkg;)
            // and this method should set each claim via DBMS_SESSION.SET_CONTEXT.
            if (httpContext is null
                || !_dataSourceToSessionContextUsage.TryGetValue(dataSourceName, out bool isSessionContextEnabled)
                || !isSessionContextEnabled)
            {
                return string.Empty;
            }

            // Dictionary containing all the claims belonging to the user, to be used as session parameters.
            Dictionary<string, string> sessionParams = AuthorizationResolver.GetProcessedUserClaims(httpContext);

            // Counter to generate different param name for each of the sessionParam.
            IncrementingInteger counter = new();
            const string SESSION_PARAM_NAME = $"{BaseQueryStructure.PARAM_NAME_PREFIX}session_param";
            StringBuilder sessionMapQuery = new();

            // Oracle uses DBMS_SESSION.SET_CONTEXT to set application context values
            // Note: This requires creating an application context and a procedure to set values
            // In production, you'd create: CREATE CONTEXT dab_context USING dab_context_pkg;
            foreach ((string claimType, string claimValue) in sessionParams)
            {
                string paramName = $"{SESSION_PARAM_NAME}{counter.Next()}";
                parameters.Add(paramName, new(claimValue));

                // Oracle's native approach is DBMS_SESSION.SET_CONTEXT, which requires a pre-created
                // application context and an associated PL/SQL package:
                //   CREATE CONTEXT dab_context USING dab_context_pkg;
                //   CREATE OR REPLACE PACKAGE dab_context_pkg AS PROCEDURE set_ctx(key VARCHAR2, val VARCHAR2); END;
                //   CREATE OR REPLACE PACKAGE BODY dab_context_pkg AS
                //     PROCEDURE set_ctx(key VARCHAR2, val VARCHAR2) AS
                //     BEGIN DBMS_SESSION.SET_CONTEXT('dab_context', key, val); END;
                //   END;
                //
                // NB: the block emitted below is only valid as the ENTIRE command text. Prepending it in
                // PrepareDbCommand currently generates invalid SQL (ORA-00900), which is why session
                // context is disabled until a batch-capable mechanism (e.g. a single anonymous block that
                // wraps both the session setup and the main statement) is implemented.
                string statementToSetContext = $"BEGIN DBMS_APPLICATION_INFO.SET_CLIENT_INFO({paramName}); END;";
                sessionMapQuery.Append(statementToSetContext);

                // Only set one value for CLIENT_INFO (Oracle limitation without custom context)
                // For multiple claims, requires creating application context in Oracle
                break; // TODO: Implement full application context support for multiple claims
            }

            return sessionMapQuery.ToString();
        }

        /// <summary>
        /// Interprets the result sets produced by an upsert (PUT/PATCH) query built by
        /// <see cref="OracleQueryBuilder.Build(SqlUpsertQueryStructure)"/> to determine whether the
        /// operation resulted in an update or an insert, and to surface database policy failures.
        /// The upsert query returns:
        ///   result set #1: the count of rows matching the primary key plus the fallback-to-update flag.
        ///   result set #2: the output of the UPDATE (non-empty when a row matched the primary key and the update policy).
        ///   result set #3 (non-fallback only): the output of the INSERT (non-empty only when a record was inserted).
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">The arguments to this handler - args[0] = primary key in pretty format, args[1] = entity name.</param>
        /// <inheritdoc />
        public override async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            // RS1: COUNT of rows matching PK (no policy) — used to distinguish
            // "row doesn't exist" from "row exists but policy blocked".
            DbResultSet resultSetWithCountOfRowsWithGivenPk = await ExtractResultSetFromDbDataReaderAsync(dbDataReader);
            DbResultSetRow? resultSetRowWithCountOfRowsWithGivenPk = resultSetWithCountOfRowsWithGivenPk.Rows.FirstOrDefault();
            int numOfRecordsWithGivenPK;
            bool isFallbackToUpdate;

            if (resultSetRowWithCountOfRowsWithGivenPk is not null &&
                resultSetRowWithCountOfRowsWithGivenPk.Columns.TryGetValue(OracleQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK,
                    out object? rowsWithGivenPK) &&
                resultSetRowWithCountOfRowsWithGivenPk.Columns.TryGetValue(OracleQueryBuilder.IS_FALLBACK_TO_UPDATE,
                    out object? fallbackToUpdate))
            {
                // Oracle COUNT(*) returns a NUMBER which ODP.NET surfaces as decimal/int depending on driver config.
                numOfRecordsWithGivenPK = Convert.ToInt32(rowsWithGivenPK!);
                isFallbackToUpdate = Convert.ToInt32(fallbackToUpdate!) == 1;
            }
            else
            {
                throw new DataApiBuilderException(
                    message: "Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            // RS2: UPDATE result, or UPDATE+INSERT results.
            DbResultSet dbResultSet = await dbDataReader.NextResultAsync()
                ? await ExtractResultSetFromDbDataReaderAsync(dbDataReader)
                : throw new DataApiBuilderException(
                    message: "Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);

            if (numOfRecordsWithGivenPK == 1) // Row existed — we attempted an UPDATE.
            {
                if (dbResultSet.Rows.Count == 0)
                {
                    // Row exists but UPDATE returned no rows — update policy blocked it.
                    throw new DataApiBuilderException(
                        message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                }

                RemoveUpsertIndicator(dbResultSet);

                // Identifies this as the result set of an update operation (used to return HTTP 200
                // instead of 201 and to omit the location header).
                dbResultSet.ResultProperties.Add(SqlMutationEngine.IS_UPDATE_RESULT_SET, true);
                return dbResultSet;
            }

            // No record existed for the given primary key, so an insert was attempted. The insert output
            // is in result set #3. For the update-only (fallback) path there is no insert result set.
            DbResultSet? insertResultSet = await dbDataReader.NextResultAsync()
                ? await ExtractResultSetFromDbDataReaderAsync(dbDataReader)
                : null;

            if (insertResultSet is null)
            {
                // Update-only path (e.g. autogenerated primary key) and no record was found to update.
                if (args is not null && args.Count > 1)
                {
                    string prettyPrintPk = args[0];
                    string entityName = args[1];

                    throw new DataApiBuilderException(
                        message: $"Cannot perform INSERT and could not find {entityName} " +
                            $"with primary key {prettyPrintPk} to perform UPDATE on.",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound);
                }

                throw new DataApiBuilderException(
                    message: "Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            if (insertResultSet.Rows.Count == 0)
            {
                // Row didn't exist but the INSERT returned no rows — the create policy blocked it.
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
            }

            RemoveUpsertIndicator(insertResultSet);
            return insertResultSet;
        }

        /// <summary>
        /// Removes the internal <c>___upsert_op___</c> indicator column produced by
        /// <see cref="OracleQueryBuilder.Build(SqlUpsertQueryStructure)"/> from a result set.
        /// The mutation engine does not consume this indicator for Oracle (unlike PostgreSQL,
        /// where the engine calls <see cref="OracleQueryBuilder.IsInsert"/> on the returned row),
        /// so it must be stripped before the row is returned to the caller to avoid leaking the
        /// internal marker into the API response.
        /// </summary>
        private static void RemoveUpsertIndicator(DbResultSet resultSet)
        {
            foreach (DbResultSetRow row in resultSet.Rows)
            {
                row.Columns.Remove(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME);
            }
        }
    }
}
