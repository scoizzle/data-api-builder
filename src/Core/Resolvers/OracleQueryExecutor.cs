// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
        /// Starts a local <see cref="OracleTransaction"/> at ReadCommitted.
        /// ODP.NET Core does not expose BeginTransactionAsync; ambient TransactionScope is not used
        /// because a second Open inside the same scope promotes to XA/MSDTC, which is unsupported on .NET Core.
        /// </summary>
        public OracleTransaction BeginLocalReadCommittedTransaction(OracleConnection conn)
        {
            QueryExecutorLogger.LogDebug(
                "{correlationId} Using local OracleTransaction for multiple-create; skipping ambient TransactionScope.",
                HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext));
            return conn.BeginTransaction(IsolationLevel.ReadCommitted);
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
        /// For data-modifying statements the builder produces a single PL/SQL block of the form
        ///   BEGIN <DML> RETURNING <cols> INTO :o1, :o2, ...; OPEN :dab_result FOR SELECT ...; END;
        /// whose RETURNING output binds and REF CURSOR result must be registered as OUTPUT parameters.
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

            // ODP.NET binds by POSITION by default, which mismatches DAB's named binds and causes
            // PL/SQL blocks that mix input binds, RETURNING output binds, and a REF CURSOR output
            // to fail (e.g. 'item DAB_RESULT is not a cursor', wrong values for the WRONG bind).
            // Named binding matches each :name in the command text to the parameter of the same name.
            cmd.BindByName = true;

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
                // parameter that does not appear as a distinct :name in the (translated) command
                // text. A whole-word match is required so that :param1 does not match inside
                // :param10/:param11, which would bind an unused parameter and raise ORA-01006.
                foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in parameters)
                {
                    string oracleName = parameterEntry.Key.TrimStart('@') ?? string.Empty;
                    if (string.IsNullOrEmpty(oracleName)
                        || !Regex.IsMatch(translatedSql, $":{Regex.Escape(oracleName)}(?![A-Za-z0-9_])"))
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

            // Register the PL/SQL-block output binds: every ':name' appearing in the RETURNING INTO
            // list must be an OUTPUT parameter (ODP.NET requires Size to be set for variable-length
            // string outputs), and the ':dab_result' REF CURSOR must be an OUTPUT RefCursor parameter.
            OracleBindRegistrar.RegisterPlSqlOutputBinds(cmd, translatedSql);

            return cmd;
        }

        /// <summary>
        /// Rewrites DAB's '@'-prefixed bind references to Oracle's ':'-prefixed syntax.
        /// Only tokens that match DAB's parameter naming convention (@param{N}, see
        /// <see cref="BaseQueryStructure.GetEncodedParamName"/>) are rewritten. This avoids
        /// corrupting '@' characters that appear inside string literals authored in database
        /// policies (e.g. 'admin@contoso.com') which must pass through unmodified.
        /// </summary>
        private static string TranslateBindParameters(string sqltext)
        {
            if (string.IsNullOrEmpty(sqltext) || !sqltext.Contains('@'))
            {
                return sqltext;
            }

            // Rewrite only outside SQL string literals. Policies may contain values such as
            // 'user@param1.example', which must not be changed into 'user:param1.example'.
            StringBuilder translated = new(sqltext.Length);
            for (int i = 0; i < sqltext.Length; i++)
            {
                char current = sqltext[i];
                translated.Append(current);

                if (current == '\'')
                {
                    // Copy a complete Oracle string literal, including escaped single quotes.
                    while (++i < sqltext.Length)
                    {
                        translated.Append(sqltext[i]);
                        if (sqltext[i] == '\'')
                        {
                            if (i + 1 < sqltext.Length && sqltext[i + 1] == '\'')
                            {
                                translated.Append(sqltext[++i]);
                                continue;
                            }

                            break;
                        }
                    }
                }
                else if (current == '@'
                    && sqltext.AsSpan(i).StartsWith("@param", StringComparison.Ordinal)
                    && i + "@param".Length < sqltext.Length
                    && char.IsAsciiDigit(sqltext[i + "@param".Length]))
                {
                    translated[translated.Length - 1] = ':';
                }
            }

            return translated.ToString();
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
                string messageSuffix = (firstAttemptAtDefaultAccessToken ? $"If authentication with DefaultAzureCredential is not intended, this warning can be safely ignored." : string.Empty);
                string message = messagePrefix + messageSuffix;
                QueryExecutorLogger.LogWarning(
                    exception: ex,
                    message: message,
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext),
                    ex.Message);

                // the config doesn't contain an identity token
                // and a default identity token cannot be obtained
                // so the application should not attempt to set the token
                // for future connections
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
        /// The upsert query is a single PL/SQL block that produces exactly ONE REF CURSOR result
        /// set (:dab_result) carrying the resulting columns plus the ___upsert_op___ indicator:
        ///   - 'updated': the UPDATE branch ran (row matched the primary key and the update policy).
        ///   - 'inserted': the INSERT branch ran (row was absent and the create policy allowed it).
        ///   - 'missing' (fallback-to-update only): the target row does not exist -> 404.
        ///   - empty: neither branch produced a row (a policy blocked the operation) -> 403.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">The arguments to this handler - args[0] = primary key in pretty format, args[1] = entity name.</param>
        /// <inheritdoc />
        public override async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            // The upsert is a single PL/SQL block: BEGIN UPDATE ... RETURNING INTO :o...;
            // IF SQL%ROWCOUNT > 0 THEN OPEN :dab_result FOR SELECT <cols>, 'updated' AS ___upsert_op___
            // FROM DUAL; ELSE INSERT ... RETURNING INTO :o...; OPEN :dab_result FOR SELECT <cols>,
            // 'inserted' AS ___upsert_op___ FROM DUAL; END IF; END;
            //
            // ODP.NET executes the block and surfaces ONLY the REF CURSOR (:dab_result) as a reader
            // result set (RETURNING INTO values go to output parameters, not the reader). So there is
            // exactly one result set whose rows carry the resulting columns plus the ___upsert_op___
            // indicator that tells us whether the branch that ran was an UPDATE or an INSERT.
            DbResultSet upsertResultSet = await ExtractResultSetFromDbDataReaderAsync(dbDataReader);
            DbResultSetRow? upsertResultSetRow = upsertResultSet.Rows.FirstOrDefault();

            if (upsertResultSetRow is null || upsertResultSetRow.Columns.Count == 0)
            {
                // Neither UPDATE nor INSERT produced a row:
                //  - non-fallback: the row existed but the update policy blocked it, OR the row was
                //    absent but the create policy blocked the insert -> 403 policy failure (and we
                //    deliberately avoid distinguishing "row existed" vs "didn't" so row existence is
                //    not leaked to unauthorized callers).
                //  - fallback (autogen PK): the row exists but the update policy blocked it (the
                //    existence probe distinguishes this from a missing row, which yields a 'missing'
                //    indicator row instead) -> 403 is the safe, non-leaky response.
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
            }

            // The fallback-to-update branch fabricates a 'missing' indicator row when the target
            // row does not exist, so the caller gets a 404 (item not found) matching PostgreSQL and
            // MSSQL instead of an ambiguous policy failure.
            bool isMissing =
                upsertResultSetRow.Columns.TryGetValue(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME, out object? missingOp)
                && string.Equals(missingOp?.ToString(), OracleQueryBuilder.MISSING_UPSERT, StringComparison.OrdinalIgnoreCase);

            if (isMissing)
            {
                string message = args is not null && args.Count > 1
                    ? $"Cannot perform INSERT and could not find {args[1]} with primary key {args[0]} to perform UPDATE on."
                    : "Neither insert nor update could be performed.";
                throw new DataApiBuilderException(
                    message: message,
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound);
            }

            // The UPDATE branch yields 'updated', the INSERT branch 'inserted'. Fallback-to-update
            // always yields 'updated' (its cursor SELECT emits the UPDATE_UPSERT literal).
            bool isUpdate =
                upsertResultSetRow.Columns.TryGetValue(OracleQueryBuilder.UPSERT_IDENTIFIER_COLUMN_NAME, out object? op)
                && string.Equals(op?.ToString(), "updated", StringComparison.OrdinalIgnoreCase);

            // Strip the internal indicator from ALL rows before returning the result set (the
            // mutation engine does not consume it for Oracle - unlike PostgreSQL where the engine
            // calls PostgresQueryBuilder.IsInsert on the returned row - so leaving it would leak
            // the marker into the API response).
            RemoveUpsertIndicator(upsertResultSet);

            if (isUpdate)
            {
                // Identifies this as the result set of an update operation (used to return HTTP 200
                // instead of 201 and to omit the location header).
                upsertResultSet.ResultProperties.Add(SqlMutationEngine.IS_UPDATE_RESULT_SET, true);
            }

            return upsertResultSet;
        }

        /// <summary>
        /// Removes the internal <c>___upsert_op___</c> indicator column produced by
        /// <see cref="OracleQueryBuilder.Build(SqlUpsertQueryStructure)"/> from a result set.
        /// The mutation engine does not consume this indicator for Oracle (unlike PostgreSQL,
        /// where the engine calls <see cref="PostgresQueryBuilder.IsInsert"/> on the returned row),
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

    /// <summary>
    /// Registers the output bind variables that OracleQueryBuilder's PL/SQL-block DML statements
    /// rely on:
    ///   BEGIN &lt;DML&gt; RETURNING &lt;cols&gt; INTO :o1, :o2, ...; OPEN :dab_result FOR SELECT ...; END;
    /// ODP.NET delivers RETURNING INTO values through OUTPUT parameters and the DbDataReader is
    /// empty, so the executor must (a) turn each ':name' in the RETURNING INTO list into an OUTPUT
    /// parameter (with a size large enough for variable-length string outputs) and (b) turn the
    /// ':dab_result' REF CURSOR into an OUTPUT RefCursor parameter.
    /// </summary>
    internal static partial class OracleBindRegistrar
    {
        internal const string RESULT_CURSOR_PARAM_NAME = OracleQueryBuilder.RESULT_CURSOR_PARAM_NAME;

        /// <summary>
        /// Scans the (already ':'-translated) command text for a "RETURNING ... INTO :a, :b;"
        /// clause and for the ":dab_result" REF CURSOR, and registers matching output parameters
        /// that the input-parameter loop above did not already add.
        /// </summary>
        public static void RegisterPlSqlOutputBinds(OracleCommand cmd, string translatedSql)
        {
            HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, OracleDbType> outputTypes = ExtractOutputTypeHints(translatedSql);
            foreach (OracleParameter p in cmd.Parameters)
            {
                existing.Add(p.ParameterName);
            }

            // 1. The REF CURSOR result parameter (present in "OPEN :dab_result FOR ...").
            if (translatedSql.Contains($":{RESULT_CURSOR_PARAM_NAME}", StringComparison.Ordinal)
                && !existing.Contains(RESULT_CURSOR_PARAM_NAME))
            {
                cmd.Parameters.Add(new OracleParameter(RESULT_CURSOR_PARAM_NAME, OracleDbType.RefCursor)
                {
                    Direction = ParameterDirection.Output
                });
                existing.Add(RESULT_CURSOR_PARAM_NAME);
            }

            // 2. Every bind name in the "RETURNING ... INTO :name1, :name2, ...;" list.
            foreach (string bindName in ExtractReturningIntoBindNames(translatedSql))
            {
                if (existing.Contains(bindName))
                {
                    continue;
                }

                OracleDbType outputType = outputTypes.TryGetValue(bindName, out OracleDbType hintedType)
                    ? hintedType
                    : OracleDbType.Varchar2;
                OracleParameter output = new(bindName, outputType)
                {
                    Direction = ParameterDirection.Output,
                };

                // Variable-length outputs need an explicit size or ODP.NET can return an empty value.
                if (outputType is OracleDbType.Varchar2 or OracleDbType.NVarchar2 or OracleDbType.Char)
                {
                    output.Size = 4000;
                }

                cmd.Parameters.Add(output);
                existing.Add(bindName);
            }
        }

        [GeneratedRegex(@"RETURNING\s+.*?\s+INTO\s+([^;]*?)(?:;|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ReturnIntoRegex();

        private static Dictionary<string, OracleDbType> ExtractOutputTypeHints(string sqlText)
        {
            Dictionary<string, OracleDbType> result = new(StringComparer.OrdinalIgnoreCase);
            const string marker = "DAB_ORACLE_OUTPUT_TYPES:";
            int start = sqlText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return result;
            }

            start += marker.Length;
            int end = sqlText.IndexOf("*/", start, StringComparison.Ordinal);
            if (end < 0)
            {
                return result;
            }

            foreach (string hint in sqlText[start..end].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = hint.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && Enum.TryParse(parts[1], ignoreCase: true, out OracleDbType oracleType))
                {
                    result[parts[0]] = oracleType;
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts bind variable names from the RETURNING INTO clause of a PL/SQL block/statement,
        /// e.g. "RETURNING "ID", "TITLE" INTO :id, :title" -> [id, title].
        /// </summary>
        public static IEnumerable<string> ExtractReturningIntoBindNames(string sqlText)
        {
            // "RETURNING <expr list> INTO :name1, :name2, :name3;" - capture up to the trailing ';'
            // (or end of string) so the match is not confounded by a later OPEN ... FOR SELECT.
            Match match = ReturnIntoRegex().Match(sqlText);

            if (!match.Success)
            {
                return Enumerable.Empty<string>();
            }

            // Split the comma-separated ":name1, :name2" list without regex.
            List<string> names = new();
            foreach (string token in match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string trimmed = token.Trim();
                if (trimmed.Length > 1 && trimmed[0] == ':')
                {
                    names.Add(trimmed[1..]);
                }
            }

            return names;
        }
    }
}
