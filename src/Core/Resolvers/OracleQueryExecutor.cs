// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
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
        /// DatasourceName to the explicit "managed-identity" option (null = infer from the connection string).
        /// </summary>
        private readonly Dictionary<string, bool?> _dataSourceManagedIdentityOptions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// DatasourceName to boolean value indicating if session context should be set for db using Oracle application contexts.
        /// </summary>
        private Dictionary<string, bool> _dataSourceToSessionContextUsage;

        /// <summary>
        /// DatasourceName to the maximum time, in seconds, one command may run (0 disables).
        /// Applied per command in <see cref="PrepareDbCommand(OracleConnection, string, IDictionary{string, DbConnectionParam}, HttpContext?, string)"/>;
        /// ODP.NET's process-wide OracleConfiguration.CommandTimeout cannot be used because it
        /// rejects assignment once any connection has been opened (ORA-50099), which hot reload
        /// and long-lived processes would hit.
        /// </summary>
        private readonly Dictionary<string, int> _dataSourceCommandTimeouts = new(StringComparer.OrdinalIgnoreCase);

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
        /// A rollback that itself fails must not replace the mutation error the caller sees.
        /// </summary>
        internal void LogPreservedRollbackFailure(Exception rollbackException, Exception original)
        {
            QueryExecutorLogger.LogWarning(
                rollbackException,
                "Oracle mutation rollback failed. The original mutation error is unchanged: {OriginalError}",
                original.Message);
        }

        /// <summary>
        /// Starts a local <see cref="OracleTransaction"/> at ReadCommitted.
        /// ODP.NET Core does not expose BeginTransactionAsync; ambient TransactionScope is not used
        /// because a second Open inside the same scope promotes to XA/MSDTC, which is unsupported on .NET Core.
        /// </summary>
        public OracleTransaction BeginLocalReadCommittedTransaction(OracleConnection conn, string dataSourceName)
        {
            QueryExecutorLogger.LogDebug(
                "{correlationId} Using local OracleTransaction for mutation on data source {dataSourceName} ({oracleDataSource}); skipping ambient TransactionScope.",
                HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext),
                dataSourceName,
                conn.DataSource);
            return conn.BeginTransaction(IsolationLevel.ReadCommitted);
        }

        /// <summary>
        /// Configure during construction or a hot-reload scenario.
        /// </summary>
        private void ConfigureOracleQueryExecutor()
        {
            IEnumerable<KeyValuePair<string, DataSource>> oracledbs = _runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType == DatabaseType.Oracle);

            // ODP.NET's command timeout defaults to 0 (wait forever), unlike the other ADO.NET
            // providers whose default is 30 seconds. Cap each data source's commands so a runaway
            // query fails with ORA-01013 instead of pinning the request; 0 disables the cap.
            foreach ((string dataSourceName, DataSource dataSource) in oracledbs)
            {
                OracleOptions oracleOptions = dataSource.GetTypedOptions<OracleOptions>() ?? new OracleOptions();
                _dataSourceCommandTimeouts[dataSourceName] = oracleOptions.CommandTimeoutSeconds;

                // Hosted mode validates TCPS + SSL_SERVER_DN_MATCH and does not mutate
                // OracleConfiguration (that static is rejected with ORA-50099 after any
                // connection opens, which breaks hot reload). Statement-cache bounds apply
                // whenever the operator left both cache attributes unset.
                string preparedConnectionString = OracleConnectionStringPolicy.Prepare(
                    dataSource.ConnectionString,
                    _runtimeConfigProvider.IsLateConfigured);
                OracleConnectionStringBuilder builder = new(preparedConnectionString);
                ConnectionStringBuilders.TryAdd(dataSourceName, builder);
                bool? managedIdentityOption = oracleOptions.ManagedIdentity;
                _dataSourceManagedIdentityOptions[dataSourceName] = managedIdentityOption;
                _dataSourceAccessTokenUsage[dataSourceName] = ShouldManagedIdentityAccessBeAttempted(builder, managedIdentityOption);

                // Session context (application context) forwarding is opt-in via "set-session-context".
                // A DBA must first create the context namespace and the package that sets it; DAB then
                // executes a parameterized anonymous block as a separate command on the same connection
                // before each statement (see ApplySessionContext and docs/design/oracle-support.md).
                _dataSourceToSessionContextUsage[dataSourceName] = oracleOptions.SetSessionContext;
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

            // ODP.NET waits forever by default; cap the command so a runaway query fails with
            // ORA-01013 instead of pinning the request (see _dataSourceCommandTimeouts).
            cmd.CommandTimeout = _dataSourceCommandTimeouts.TryGetValue(dataSourceName, out int commandTimeout)
                ? commandTimeout
                : OracleOptions.DEFAULT_COMMAND_TIMEOUT_SECONDS;

            // ODP.NET binds by POSITION by default, which mismatches DAB's named binds and causes
            // PL/SQL blocks that mix input binds, RETURNING output binds, and a REF CURSOR output
            // to fail (e.g. 'item DAB_RESULT is not a cursor', wrong values for the WRONG bind).
            // Named binding matches each :name in the command text to the parameter of the same name.
            cmd.BindByName = true;

            string translatedSql = TranslateBindParameters(sqltext);
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

                    // PopulateDbTypeForParameter also translates date/time values ODP.NET cannot
                    // bind (e.g. Microsoft.OData.Edm.Date) and must therefore run before Value is
                    // assigned - ODP.NET throws from the Value setter for those types.
                    PopulateDbTypeForParameter(parameterEntry, parameter);
                    if (parameter.Value is null)
                    {
                        parameter.Value = parameterEntry.Value.Value ?? DBNull.Value;
                    }

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
        /// Clears and sets Oracle application-context claims after the statement command is enlisted
        /// and before it executes. A null <paramref name="httpContext"/> still clears, so a pooled
        /// session cannot keep a previous caller's claims. The context command is one extra round trip.
        /// </summary>
        protected override void ApplySessionContext(
            DbConnection conn,
            HttpContext? httpContext,
            string dataSourceName,
            DbTransaction? transaction)
        {
            if (conn is not OracleConnection oracleConnection
                || !TryCreateSessionContextCommand(oracleConnection, httpContext, dataSourceName, transaction, out OracleCommand? contextCommand)
                || contextCommand is null)
            {
                return;
            }

            using (contextCommand)
            {
                try
                {
                    ExecuteSessionContextCommand(contextCommand);
                }
                catch (DbException e)
                {
                    throw DbExceptionParser.Parse(e);
                }
            }
        }

        /// <inheritdoc />
        protected override async Task ApplySessionContextAsync(
            DbConnection conn,
            HttpContext? httpContext,
            string dataSourceName,
            DbTransaction? transaction,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (conn is not OracleConnection oracleConnection
                || !TryCreateSessionContextCommand(oracleConnection, httpContext, dataSourceName, transaction, out OracleCommand? contextCommand)
                || contextCommand is null)
            {
                return;
            }

            using (contextCommand)
            {
                try
                {
                    await ExecuteSessionContextCommandAsync(contextCommand, cancellationToken);
                }
                catch (DbException e)
                {
                    throw DbExceptionParser.Parse(e);
                }
            }
        }

        /// <summary>
        /// Runs the session-context block. Tests override this to record the command without a database.
        /// </summary>
        protected virtual void ExecuteSessionContextCommand(OracleCommand command)
        {
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Runs the session-context block and forwards <paramref name="cancellationToken"/> to ODP.NET.
        /// </summary>
        protected virtual Task ExecuteSessionContextCommandAsync(OracleCommand command, CancellationToken cancellationToken)
        {
            return command.ExecuteNonQueryAsync(cancellationToken);
        }

        private bool TryCreateSessionContextCommand(
            OracleConnection conn,
            HttpContext? httpContext,
            string dataSourceName,
            DbTransaction? transaction,
            out OracleCommand? contextCommand)
        {
            // Only the option gates this. A null HttpContext still clears; GetProcessedUserClaims
            // returns an empty dictionary, so the block is CLEAR_CLAIMS and no SET_CLAIM.
            if (!IsSessionContextEnabled(dataSourceName))
            {
                contextCommand = null;
                return false;
            }

            contextCommand = BuildSessionContextCommand(
                conn,
                AuthorizationResolver.GetProcessedUserClaims(httpContext),
                dataSourceName);
            if (transaction is OracleTransaction oracleTransaction)
            {
                contextCommand.Transaction = oracleTransaction;
            }

            return true;
        }

        /// <summary>
        /// Translates DAB's date/time parameter values into Oracle-bindable values and types.
        /// ODP.NET infers the bind type from the CLR value alone, which is wrong for the shapes
        /// DAB produces:
        ///  - Microsoft.OData.Edm.Date (the value of an OData date literal) and DateOnly are not
        ///    CLR types ODP.NET can bind, so a date filter would fail with an argument error.
        ///  - A System.DateTimeOffset (ISO 8601 UTC filters such as "col ge 2025-01-01T00:00:00Z")
        ///    would bind as TIMESTAMP WITH TIME ZONE. Comparing that bind against a plain
        ///    DATE/TIMESTAMP column makes Oracle convert the column to an instant at the session
        ///    time zone, so equality filters silently miss rows and range filters can raise
        ///    ORA-01878 around DST transitions. Those values are bound as a plain TIMESTAMP using
        ///    the UTC wall clock, matching DAB's contract that date/time values are UTC.
        ///  - TIMESTAMP WITH TIME ZONE columns carry DbType.DateTimeOffset, so their parameters
        ///    keep the offset (Oracle compares instants for those columns).
        ///  - A plain <see cref="DateTime"/> has no DbType mapping in TypeHelper.
        ///    Left untyped, the parameter stays VARCHAR2 until the value setter, so TIMESTAMP
        ///    writes depend on driver inference. The default branch binds TIMESTAMP explicitly.
        /// Parameters without a DbType hint (date literals the OData parser wrapped in a
        /// ConvertNode do not inherit the column type) fall back to the plain TIMESTAMP form.
        /// String parameters use column facts copied onto the parameter
        /// (<see cref="OracleStringBindFacts"/>). CLOB/NCLOB bind as LOBs. CHAR and NCHAR
        /// bind as fixed types sized to the column's character length so Oracle blank-pads
        /// the comparison. NVARCHAR2 stays NVarchar2. A string whose UTF-8 length exceeds
        /// the standard VARCHAR2 byte limit binds as CLOB so a multi-byte VARCHAR2(4000 CHAR)
        /// value is not sent as VARCHAR2.
        /// </summary>
        /// <inheritdoc />
        public override void PopulateDbTypeForParameter(
            KeyValuePair<string, DbConnectionParam> parameterEntry,
            DbParameter parameter)
        {
            if (parameter is not OracleParameter oracleParameter
                || parameterEntry.Value is null)
            {
                return;
            }

            object? value = parameterEntry.Value.Value;
            if (value is null)
            {
                ApplyOracleStringBind(parameterEntry.Value, null, oracleParameter);
                return;
            }

            switch (parameterEntry.Value.DbType)
            {
                case DbType.DateTimeOffset:
                    oracleParameter.OracleDbType = OracleDbType.TimeStampTZ;
                    oracleParameter.Value = ToDateTimeOffset(value);
                    break;
                case DbType.Date:
                case DbType.DateTime:
                case DbType.DateTime2:
                    oracleParameter.OracleDbType = OracleDbType.TimeStamp;
                    oracleParameter.Value = ToUtcWallClock(value);
                    break;
                default:
                    // Oracle DATE/TIMESTAMP columns surface as System.DateTime, for which
                    // TypeHelper has no DbType mapping, so most date filters have no hint.
                    if (value is Microsoft.OData.Edm.Date
                        or DateTimeOffset
                        or DateOnly
                        or DateTime)
                    {
                        oracleParameter.OracleDbType = OracleDbType.TimeStamp;
                        oracleParameter.Value = ToUtcWallClock(value);
                    }
                    else
                    {
                        ApplyOracleStringBind(parameterEntry.Value, value, oracleParameter);
                    }

                    break;
            }
        }

        /// <summary>
        /// Standard VARCHAR2 byte limit (MAX_STRING_SIZE=STANDARD). A bind longer than this
        /// is sent as a LOB so ODP.NET does not leave it as VARCHAR2.
        /// </summary>
        private const int Varchar2StandardByteLimit = 4000;

        /// <summary>
        /// Selects CLOB, NCLOB, or NVARCHAR2 from column facts. Facts win over the value length.
        /// A national column whose value exceeds the VARCHAR2 byte limit binds as NCLOB; any
        /// other wide string binds as CLOB. Nulls still receive the column's type.
        /// </summary>
        private static void ApplyOracleStringBind(DbConnectionParam parameter, object? value, OracleParameter oracleParameter)
        {
            OracleStringBindFacts.Kind kind = OracleStringBindFacts.Get(parameter);
            if (kind == OracleStringBindFacts.Kind.NClob)
            {
                oracleParameter.OracleDbType = OracleDbType.NClob;
                return;
            }

            if (kind == OracleStringBindFacts.Kind.Clob)
            {
                oracleParameter.OracleDbType = OracleDbType.Clob;
                return;
            }

            // CHAR/NCHAR compare with blank-padding only when the bind is that fixed type
            // and its size is the column width. VARCHAR2 'AB' does not equal CHAR(4) 'AB  '.
            if (kind == OracleStringBindFacts.Kind.Char)
            {
                oracleParameter.OracleDbType = OracleDbType.Char;
                ApplyFixedCharacterSize(parameter, oracleParameter);
                return;
            }

            if (kind == OracleStringBindFacts.Kind.FixedNChar)
            {
                oracleParameter.OracleDbType = OracleDbType.NChar;
                ApplyFixedCharacterSize(parameter, oracleParameter);
                return;
            }

            if (value is not string text)
            {
                if (kind == OracleStringBindFacts.Kind.NChar)
                {
                    oracleParameter.OracleDbType = OracleDbType.NVarchar2;
                }

                return;
            }

            bool exceedsVarchar2 = Encoding.UTF8.GetByteCount(text) > Varchar2StandardByteLimit;
            if (kind == OracleStringBindFacts.Kind.NChar)
            {
                oracleParameter.OracleDbType = exceedsVarchar2 ? OracleDbType.NClob : OracleDbType.NVarchar2;
                return;
            }

            if (exceedsVarchar2)
            {
                oracleParameter.OracleDbType = OracleDbType.Clob;
            }
        }

        /// <summary>
        /// Sets the fixed CHAR/NCHAR bind width from the column length already stored on the
        /// parameter. A missing or non-positive length leaves Size unset; ODP.NET then does not
        /// blank-pad, so catalog reads must supply CHAR_LENGTH (characters) rather than the
        /// byte DATA_LENGTH of a character-semantics column.
        /// </summary>
        private static void ApplyFixedCharacterSize(DbConnectionParam parameter, OracleParameter oracleParameter)
        {
            if (parameter.Length is int length && length > 0)
            {
                oracleParameter.Size = length;
            }
        }

        /// <summary>
        /// Converts a date/time parameter value to the plain (offset-less) UTC DateTime that a
        /// DATE/TIMESTAMP column comparison expects. DAB stores date/time values as UTC, so an
        /// offset is discarded by shifting to UTC rather than dropping it.
        /// </summary>
        private static object ToUtcWallClock(object value)
        {
            return value switch
            {
                Microsoft.OData.Edm.Date date => new DateTime(date.Year, date.Month, date.Day),
                DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
                DateTime dateTime when dateTime.Kind == DateTimeKind.Local => dateTime.ToUniversalTime(),
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                _ => value,
            };
        }

        /// <summary>
        /// Converts a date/time parameter value to a DateTimeOffset so it can be bound to a
        /// TIMESTAMP WITH TIME ZONE column without losing the offset.
        /// </summary>
        private static object ToDateTimeOffset(object value)
        {
            return value switch
            {
                DateTimeOffset => value,
                Microsoft.OData.Edm.Date date => new DateTimeOffset(
                    new DateTime(date.Year, date.Month, date.Day),
                    TimeSpan.Zero),
                DateTime dateTime => new DateTimeOffset(
                    dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime,
                    TimeSpan.Zero),
                DateOnly date => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                _ => value,
            };
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
                else if (current == '"')
                {
                    // Copy a complete quoted identifier. Apostrophes inside a double-quoted
                    // identifier (e.g. the exposed label "United State's Region") are not string
                    // delimiters, so failing to skip them would make the scanner swallow following
                    // '@paramN' tokens and leave them untranslated (ORA-00936 / ORA-01008).
                    while (++i < sqltext.Length)
                    {
                        translated.Append(sqltext[i]);
                        if (sqltext[i] == '"')
                        {
                            if (i + 1 < sqltext.Length && sqltext[i + 1] == '"')
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
        /// Applies a managed identity access token to the supplied connection via
        /// <see cref="OracleConnection.AccessToken"/>. The token never appears in a connection string,
        /// and the Data Source descriptor (TLS/wallet options) is preserved; only User Id, Password and
        /// proxy credentials are normalized as ODP.NET requires for token authentication. This is the three-argument overload because every
        /// core execution and metadata path calls it; the two-argument base overload chains here.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if null</param>
        /// <param name="cancellationToken">Cancels token acquisition.</param>
        public override async Task SetManagedIdentityAccessTokenIfAnyAsync(
            DbConnection conn,
            string dataSourceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // using default datasource name for first db - maintaining backward compatibility for single db scenario.
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            // A token supplied by the configuration controller is applied whenever the connection
            // string/option allow token authentication; it can arrive after construction. Otherwise
            // fall back to the flag computed at configuration time (which is cleared when the default
            // credential turns out to be unavailable and token use was only inferred).
            _accessTokensFromConfiguration.TryGetValue(dataSourceName, out string? accessTokenFromController);
            bool setAccessToken;
            if (accessTokenFromController is not null)
            {
                _dataSourceManagedIdentityOptions.TryGetValue(dataSourceName, out bool? managedIdentityOption);
                setAccessToken = ConnectionStringBuilders.TryGetValue(dataSourceName, out DbConnectionStringBuilder? existing) &&
                    existing is OracleConnectionStringBuilder existingBuilder &&
                    ShouldManagedIdentityAccessBeAttempted(existingBuilder, managedIdentityOption);
            }
            else
            {
                _dataSourceAccessTokenUsage.TryGetValue(dataSourceName, out setAccessToken);
            }

            // Only attempt to get the access token if the connection string is in the appropriate format
            if (setAccessToken)
            {
                // If the configuration controller provided a managed identity access token use that,
                // else use the default saved access token if still valid.
                // Get a new token only if the saved token is null or expired.
                string? accessToken = accessTokenFromController ??
                    (IsDefaultAccessTokenValid() ?
                        ((AccessToken)_defaultAccessToken!).Token :
                        await GetAccessTokenAsync(dataSourceName, cancellationToken));

                if (accessToken is not null)
                {
                    OracleConnection oracleConnection = (OracleConnection)conn;
                    oracleConnection.ConnectionString = PrepareConnectionStringForTokenAuthentication(oracleConnection.ConnectionString);
                    oracleConnection.AccessToken = new OracleAccessToken(accessToken.ToCharArray());
                }
            }
        }

        /// <summary>
        /// ODP.NET rejects <see cref="OracleConnection.AccessToken"/> (ORA-50108) unless the connection
        /// string has <c>User Id=/</c> and no password or proxy credentials. Normalizes those keywords
        /// and leaves every other attribute, including the Data Source descriptor with its TLS/wallet
        /// options, untouched. The access token itself is never written to the connection string.
        /// Returns the input unchanged when it is already in the required form.
        /// </summary>
        internal static string PrepareConnectionStringForTokenAuthentication(string connectionString)
        {
            OracleConnectionStringBuilder builder = new(connectionString);
            if (string.Equals(builder.UserID, "/", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(builder.Password) &&
                string.IsNullOrEmpty(builder.ProxyUserId) &&
                string.IsNullOrEmpty(builder.ProxyPassword))
            {
                return connectionString;
            }

            builder.UserID = "/";
            builder.Remove("Password");
            builder.Remove("Proxy User Id");
            builder.Remove("Proxy Password");
            return builder.ConnectionString;
        }

        /// <summary>
        /// Determines if managed identity access should be attempted or not.
        /// Precedence: an explicit <c>managed-identity</c> option wins; otherwise wallet or external
        /// authentication markers disable it; otherwise it is inferred from an empty password. A
        /// controller-supplied token does not override a configured password (MSSQL gates the same
        /// way); set <c>managed-identity: true</c> explicitly to use a token with a TCPS+wallet
        /// descriptor or alongside a password.
        /// </summary>
        /// <param name="builder">The data source's connection string builder.</param>
        /// <param name="managedIdentityOption">Explicit option value, or null to infer.</param>
        internal static bool ShouldManagedIdentityAccessBeAttempted(
            OracleConnectionStringBuilder builder,
            bool? managedIdentityOption)
        {
            if (managedIdentityOption.HasValue)
            {
                return managedIdentityOption.Value;
            }

            if (HasWalletOrExternalAuthMarkers(builder))
            {
                return false;
            }

            return string.IsNullOrEmpty(builder.Password);
        }

        /// <summary>
        /// Matches a wallet directory attribute inside the Data Source descriptor, e.g.
        /// <c>(MY_WALLET_DIRECTORY=/wallet)</c> or <c>WALLET_LOCATION = (SOURCE=...)</c>. Anchored on the
        /// attribute name so a host such as <c>wallet-db.corp</c> is not treated as a wallet.
        /// </summary>
        private static readonly Regex _walletAttributeRegex = new(
            @"(?<![A-Za-z0-9_])(WALLET_LOCATION|MY_WALLET_DIRECTORY)\s*=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// True when the connection string requests wallet-based or external (OS/wallet) authentication,
        /// in which case the credential must not be replaced by a managed identity token.
        /// </summary>
        private static bool HasWalletOrExternalAuthMarkers(OracleConnectionStringBuilder builder)
        {
            return string.Equals(builder.UserID?.Trim(), "/", StringComparison.Ordinal) ||
                _walletAttributeRegex.IsMatch(builder.DataSource ?? string.Empty);
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
        /// When token use was only inferred, any credential failure is logged as a warning (best
        /// effort). When <c>managed-identity: true</c> is set explicitly, a failure is surfaced as a
        /// 503 so the data source never silently falls back to an unauthenticated connection.
        /// </summary>
        /// <returns>The string representation of the access token if found,
        /// null otherwise.</returns>
        private async Task<string?> GetAccessTokenAsync(string dataSourceName, CancellationToken cancellationToken)
        {
            bool firstAttemptAtDefaultAccessToken = _defaultAccessToken is null;
            _dataSourceManagedIdentityOptions.TryGetValue(dataSourceName, out bool? managedIdentityOption);
            bool managedIdentityForced = managedIdentityOption == true;

            try
            {
                _defaultAccessToken =
                    await AzureCredential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }),
                        cancellationToken);
            }
            // A cancelled request is not evidence that the credential is unavailable; do not
            // permanently disable token use for the data source.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // because there can be scenarios where password is not specified but
            // default managed identity is not the intended method of authentication
            // so a bunch of different exceptions could occur in that scenario
            catch (Exception ex)
            {
                string messagePrefix = managedIdentityForced
                    ? "{correlationId} Attempt to retrieve a managed identity access token using DefaultAzureCredential failed although 'managed-identity' is set to true for the data source:\n{errorMessage}"
                    : "{correlationId} No password detected in the connection string. Attempt to retrieve a managed identity access token using DefaultAzureCredential failed due to:\n{errorMessage}";
                string messageSuffix = (firstAttemptAtDefaultAccessToken && !managedIdentityForced ? $"If authentication with DefaultAzureCredential is not intended, this warning can be safely ignored." : string.Empty);
                string message = messagePrefix + messageSuffix;
                QueryExecutorLogger.LogWarning(
                    exception: ex,
                    message: message,
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext),
                    ex.Message);

                // An explicit managed-identity: true must never degrade to an unauthenticated
                // connection attempt, and a stale default token must not be reused: fail the request
                // and leave the flag set so the next request retries token acquisition.
                if (managedIdentityForced)
                {
                    throw new DataApiBuilderException(
                        message: $"Managed identity authentication is enabled for data source '{dataSourceName}' ('managed-identity': true) " +
                            "but an access token could not be obtained with DefaultAzureCredential. " +
                            "Verify that a managed identity or Azure credential is available to the service, or remove the option.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                        innerException: ex);
                }

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
        /// Name of the Oracle application context namespace whose attributes receive the caller's
        /// claims. A DBA creates it once with
        /// <c>CREATE CONTEXT DAB_SESSION_CONTEXT USING DAB_SESSION_CONTEXT_PKG;</c>.
        /// </summary>
        public const string SESSION_CONTEXT_NAMESPACE = "DAB_SESSION_CONTEXT";

        /// <summary>
        /// Name of the PL/SQL package associated with <see cref="SESSION_CONTEXT_NAMESPACE"/>. Oracle
        /// only permits a namespace's context to be set from its associated package, so the package
        /// (owned by the DBA) exposes the CLEAR_CLAIMS and SET_CLAIM procedures that wrap
        /// DBMS_SESSION.CLEAR_ALL_CONTEXT / DBMS_SESSION.SET_CONTEXT. See docs/design/oracle-support.md.
        /// </summary>
        public const string SESSION_CONTEXT_PACKAGE = "DAB_SESSION_CONTEXT_PKG";

        /// <summary>
        /// Whether claim forwarding is enabled for the data source. Data sources that are unknown to
        /// this executor (e.g. during startup metadata discovery) are treated as disabled.
        /// An empty name uses the default data source, matching the other executors.
        /// </summary>
        internal bool IsSessionContextEnabled(string dataSourceName)
        {
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            return _dataSourceToSessionContextUsage.TryGetValue(dataSourceName, out bool isEnabled) && isEnabled;
        }

        /// <summary>
        /// Builds the PL/SQL block that clears any claims left on the session and sets the current
        /// request's claims. Exposed as internal for unit testing.
        /// </summary>
        /// <param name="claims">Claim name/value pairs to forward.</param>
        /// <returns>The anonymous block text and the bind parameters it references.</returns>
        internal static (string CommandText, List<(string Name, string Value)> Parameters) BuildSessionContextBlock(
            IEnumerable<KeyValuePair<string, string>> claims)
        {
            StringBuilder block = new();
            block.Append("BEGIN ").Append(SESSION_CONTEXT_PACKAGE).Append(".CLEAR_CLAIMS; ");

            List<(string Name, string Value)> parameters = new();
            int index = 0;
            foreach ((string claimType, string claimValue) in claims)
            {
                string keyParameter = $"dab_claim_key{index}";
                string valueParameter = $"dab_claim_value{index}";
                parameters.Add((keyParameter, claimType));
                parameters.Add((valueParameter, claimValue));

                block.Append(SESSION_CONTEXT_PACKAGE)
                    .Append(".SET_CLAIM(:").Append(keyParameter)
                    .Append(", :").Append(valueParameter)
                    .Append("); ");

                index++;
            }

            block.Append("END;");
            return (block.ToString(), parameters);
        }

        /// <summary>
        /// Creates the command that forwards the given claims. Claim names and values are always
        /// bound parameters, never concatenated into the command text.
        /// </summary>
        private OracleCommand BuildSessionContextCommand(
            OracleConnection conn,
            Dictionary<string, string> claims,
            string dataSourceName)
        {
            (string commandText, List<(string Name, string Value)> claimParameters) = BuildSessionContextBlock(claims);

            OracleCommand cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;
            // ODP.NET binds by position by default; named binding matches each :name occurrence.
            cmd.BindByName = true;
            cmd.CommandText = commandText;
            cmd.CommandTimeout = _dataSourceCommandTimeouts.TryGetValue(dataSourceName, out int commandTimeout)
                ? commandTimeout
                : OracleOptions.DEFAULT_COMMAND_TIMEOUT_SECONDS;

            foreach ((string parameterName, string parameterValue) in claimParameters)
            {
                OracleParameter parameter = new()
                {
                    ParameterName = parameterName,
                    OracleDbType = OracleDbType.Varchar2,
                    Direction = ParameterDirection.Input,
                    Value = parameterValue,
                };
                cmd.Parameters.Add(parameter);
            }

            return cmd;
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
            Dictionary<string, OutputTypeHint> outputTypes = ExtractOutputTypeHints(translatedSql);
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

                OutputTypeHint hint = outputTypes.TryGetValue(bindName, out OutputTypeHint hinted)
                    ? hinted
                    : new OutputTypeHint(OracleDbType.Varchar2, Size: null);
                OracleDbType outputType = hint.Type;
                OracleParameter output = new(bindName, outputType)
                {
                    Direction = ParameterDirection.Output,
                };

                // Variable-length outputs need an explicit size or ODP.NET returns empty
                // or raises. RAW left at Size 0 fails every mutation that returns the column.
                // Character sizes come from the builder hint (char length × 4, already clamped
                // to 32767). An unknown hint keeps the historical 4000-byte default.
                if (outputType is OracleDbType.Varchar2 or OracleDbType.NVarchar2 or OracleDbType.Char or OracleDbType.NChar)
                {
                    output.Size = ClampCharacterOutputSize(hint.Size ?? 4000);
                }
                else if (outputType == OracleDbType.Raw)
                {
                    output.Size = hint.Size is > 0 ? hint.Size.Value : 2000;
                }

                cmd.Parameters.Add(output);
                existing.Add(bindName);
            }
        }

        /// <summary>
        /// PL/SQL VARCHAR2/NVARCHAR2 variables cannot exceed 32767 bytes. A non-positive hint
        /// is raised to 1 so ODP.NET does not treat the bind as unsized.
        /// </summary>
        private static int ClampCharacterOutputSize(int size)
        {
            if (size < 1)
            {
                return 1;
            }

            if (size > 32767)
            {
                return 32767;
            }

            return size;
        }

        [GeneratedRegex(@"RETURNING\s+.*?\s+INTO\s+([^;]*?)(?:;|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ReturnIntoRegex();

        private readonly record struct OutputTypeHint(OracleDbType Type, int? Size);

        private static Dictionary<string, OutputTypeHint> ExtractOutputTypeHints(string sqlText)
        {
            Dictionary<string, OutputTypeHint> result = new(StringComparer.OrdinalIgnoreCase);
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
                if (parts.Length != 2)
                {
                    continue;
                }

                string typeToken = parts[1];
                int? size = null;
                int sizeSeparator = typeToken.IndexOf(':');
                if (sizeSeparator > 0
                    && int.TryParse(typeToken[(sizeSeparator + 1)..], out int parsedSize))
                {
                    size = parsedSize;
                    typeToken = typeToken[..sizeSeparator];
                }

                if (Enum.TryParse(typeToken, ignoreCase: true, out OracleDbType oracleType))
                {
                    result[parts[0]] = new OutputTypeHint(oracleType, size);
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
