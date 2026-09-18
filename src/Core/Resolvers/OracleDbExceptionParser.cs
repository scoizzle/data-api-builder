// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for Oracle.
    /// <seealso cref="https://docs.oracle.com/en/database/oracle/oracle-database/19/errmg/"/>
    /// </summary>
    public class OracleDbExceptionParser : DbExceptionParser
    {
        public OracleDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // HashSet of Oracle error codes to be considered as bad requests.
            // NOTE: constraint/duplicate-key violations (ORA-00001, ORA-02290-2294) are NOT listed
            // here - they represent conflicts (the resource already exists / referential integrity)
            // and are mapped to HTTP 409 via ConflictExceptionCodes, matching MsSql's handling of
            // its duplicate-key (2627) and FK (547) errors. Only genuine client-input errors
            // (bad literals, NULL into NOT NULL, value-too-large) are 400.
            // ORA-01031 (insufficient privileges) is authorization, not client input — mapped to 403.
            BadRequestExceptionCodes.UnionWith(new List<string>
            {
                // NULL handling codes
                "1400",     // ORA-01400: cannot insert NULL into column
                "1407",     // ORA-01407: cannot update NULL into column

                // Size and precision codes
                "12899",    // ORA-12899: value too large for column
                "1438",     // ORA-01438: value larger than column precision
                "1461",     // ORA-01461: integer column too large
                "1480",     // ORA-01480: trailing null missing from STR bind

                // Data format codes
                "1858",     // ORA-01858: a non-numeric character was found
                "1861",     // ORA-01861: literal does not match format string
                "2015",     // ORA-02015: cannot use FOR UPDATE with group functions

                // PL/SQL error codes
                "4101",     // ORA-04101: referential integrity - different transaction
                "4102",     // ORA-04102: referential integrity - unknown table

                // Column and table related
                "1430",     // ORA-01430: column is not in select list
                "1717"      // ORA-01717: invalid option for alter session
                // NOTE: ORA-01018/01019 (cursor/memory state), ORA-02003-02005 (USERENV/column
                // specification), ORA-02443 (invalid trigger name) and ORA-01410-01412 (ROWID)
                // are intentionally NOT mapped to 400/409: they describe server-side state or
                // DAB-generated SQL, so they surface as 500 rather than telling clients their
                // request was fixable.
            });

            TransientExceptionCodes.UnionWith(new List<string>
            {
                // Lock and resource conflict codes
                "54",       // ORA-00054: resource busy and acquire with NOWAIT specified
                "60",       // ORA-00060: deadlock detected while waiting for resource
                "2396",     // ORA-02396: exceeded maximum idle time

                // Shutdown and startup codes
                "1089",     // ORA-01089: immediate shutdown in progress
                "1090",     // ORA-01090: shutdown in progress
                "1033",     // ORA-01033: ORACLE initialization or shutdown in progress
                "1034",     // ORA-01034: ORACLE not available
                "1035",     // ORA-01035: ORACLE only available to users with RESTRICTED SESSION

                // Connection loss codes
                "3113",     // ORA-03113: end-of-file on communication channel
                "3114",     // ORA-03114: not connected to ORACLE
                "3135",     // ORA-03135: connection lost contact
                "17002",    // ORA-17002: IO Error
                "17010",    // ORA-17010: Socket read error

                // Privilege and authentication codes
                "1045",     // ORA-01045: user lacks CREATE SESSION privilege
                "1012",     // ORA-01012: not logged on
                "24313",    // ORA-24313: user already authenticated
                "24315",    // ORA-24315: illegal attribute type

                // TNS (listener/network) codes
                "12170",    // ORA-12170: TNS:connect timeout occurred
                "12514",    // ORA-12514: TNS:listener does not currently know of service
                "12516",    // ORA-12516: TNS:listener could not find instance
                "12520",    // ORA-12520: TNS:listener could not find available handler
                "12528",    // ORA-12528: TNS:listener: all appropriate instances are blocking
                "12541",    // ORA-12541: TNS:no listener
                "12545",    // ORA-12545: Connect failed because target host or object does not exist
                "12555",    // ORA-12555: TNS:permission denied

                // File and network codes
                "17404",    // ORA-17404: File not found

                // ORA-25408: can not safely replay call
                "25408"
            });

            ConflictExceptionCodes.UnionWith(new List<string>
            {
                // Constraint and uniqueness conflicts
                "1",        // ORA-00001: unique constraint violated
                "2290",     // ORA-02290: check constraint violated
                "2291",     // ORA-02291: integrity constraint violated
                "2292",     // ORA-02292: integrity constraint violated
                "2293",     // ORA-02293: cannot validate - check constraint violated
                "2294",     // ORA-02294: duplicate key value
                "8177"      // ORA-08177: can't serialize access for this transaction
                // NOTE: ORA-00060 (deadlock) is intentionally NOT here — it is transient and
                // handled by TransientExceptionCodes for retry logic, not mapped to 409.
                // ORA-02443 (invalid trigger name) and ORA-01410-01412 (ROWID) are server/state
                // errors and are left to the 500 fallback rather than reported as conflicts.
            });
        }

        /// <inheritdoc/>
        public override bool IsTransientException(DbException e)
        {
            string errorCode = GetOracleErrorCode(e);
            return !string.IsNullOrEmpty(errorCode) && TransientExceptionCodes.Contains(errorCode);
        }

        /// <inheritdoc/>
        public override HttpStatusCode GetHttpStatusCodeForException(DbException e)
        {
            string errorCode = GetOracleErrorCode(e);
            return MapStatusCode(errorCode);
        }

        /// <summary>
        /// Maps an Oracle error number (without the ORA- prefix) to an HTTP status.
        /// </summary>
        internal HttpStatusCode MapStatusCode(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode))
            {
                return HttpStatusCode.InternalServerError;
            }

            if (BadRequestExceptionCodes.Contains(errorCode))
            {
                return HttpStatusCode.BadRequest;
            }

            if (errorCode == "1031")
            {
                return HttpStatusCode.Forbidden;
            }

            if (ConflictExceptionCodes.Contains(errorCode))
            {
                return HttpStatusCode.Conflict;
            }

            return HttpStatusCode.InternalServerError;
        }

        /// <summary>
        /// Maps Oracle errors that are caused by invalid client-provided input (bad literals,
        /// format mismatches, value-too-large, missing parameters to stored procedures) to the
        /// DatabaseInputError substatus so callers can distinguish client input errors from
        /// generic database operation failures. Mirrors the MSSQL mapping for error 201.
        /// </summary>
        /// <inheritdoc />
        public override DataApiBuilderException.SubStatusCodes GetResultSubStatusCodeForException(DbException e)
        {
            string errorCode = GetOracleErrorCode(e);

            // ORA-01858: a non-numeric character was found where a numeric was expected
            // ORA-01861: literal does not match format string
            // ORA-01843: not a valid month
            // ORA-12899: value too large for column
            // ORA-01400: cannot insert NULL into column
            // ORA-01438: value larger than specified precision
            if (errorCode is "1858" or "1861" or "1843" or "12899" or "1400" or "1438")
            {
                return DataApiBuilderException.SubStatusCodes.DatabaseInputError;
            }

            return DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed;
        }

        /// <summary>
        /// Extracts Oracle error code from exception.
        /// </summary>
        private static string GetOracleErrorCode(DbException e) => e switch
        {
            Oracle.ManagedDataAccess.Client.OracleException oraException => oraException.Number.ToString(),
            _ => string.Empty
        };
    }
}
