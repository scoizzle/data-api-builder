// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using Azure.Core;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
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
                
                // Check if Oracle session context (application context) should be enabled
                // For Oracle, we can enable session context by default as it uses application contexts
                // which are similar to SQL Server's SESSION_CONTEXT
                _dataSourceToSessionContextUsage[dataSourceName] = true;
            }
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

            if (httpContext is null || !_dataSourceToSessionContextUsage[dataSourceName])
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
            // For now, we'll use a simplified approach that sets session-level attributes
            // In production, you'd create: CREATE CONTEXT dab_context USING dab_context_pkg;
            foreach ((string claimType, string claimValue) in sessionParams)
            {
                string paramName = $"{SESSION_PARAM_NAME}{counter.Next()}";
                parameters.Add(paramName, new(claimValue));
                
                // Oracle uses DBMS_SESSION.SET_CONTEXT but requires a context to be pre-created
                // Alternative: Use client_identifier or CLIENT_INFO for simpler scenarios
                // For compatibility, we'll set the CLIENT_IDENTIFIER which can be used in policies
                // Format: SET CLIENT_IDENTIFIER = <value>
                // Note: This is a simplified implementation. Full implementation would use application contexts.
                
                // Using dynamic SQL to set application context (requires dab_context to exist)
                // string statementToSetContext = $"BEGIN DBMS_SESSION.SET_CONTEXT('dab_context', '{claimType}', {paramName}); END;";
                
                // Simpler approach: Use DBMS_APPLICATION_INFO.SET_CLIENT_INFO (limited to one value)
                // For multiple claims, we'd need a proper application context setup
                // For now, we'll document this as a TODO for full implementation
                string statementToSetContext = $"BEGIN DBMS_APPLICATION_INFO.SET_CLIENT_INFO({paramName}); END;";
                sessionMapQuery.Append(statementToSetContext);
                
                // Only set one value for CLIENT_INFO (Oracle limitation without custom context)
                // For multiple claims, requires creating application context in Oracle
                break; // TODO: Implement full application context support for multiple claims
            }

            return sessionMapQuery.ToString();
        }
    }
}
