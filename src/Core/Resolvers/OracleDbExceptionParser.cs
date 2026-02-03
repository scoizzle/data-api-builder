// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;

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
            BadRequestExceptionCodes.UnionWith(new List<string>
            {
                // ORA-00001: unique constraint violated
                "1",

                // ORA-02290: check constraint violated
                "2290",

                // ORA-02291: integrity constraint (foreign key) violated - parent key not found
                "2291",

                // ORA-02292: integrity constraint violated - child record found
                "2292",

                // ORA-01400: cannot insert NULL into column
                "1400",

                // ORA-01407: cannot update NULL into column
                "1407",

                // ORA-12899: value too large for column
                "12899",

                // ORA-01438: value larger than specified precision allowed for this column
                "1438"
            });

            TransientExceptionCodes.UnionWith(new List<string>
            {
                // ORA-00054: resource busy and acquire with NOWAIT specified
                "54",

                // ORA-00060: deadlock detected while waiting for resource
                "60",

                // ORA-01089: immediate shutdown in progress
                "1089",

                // ORA-01090: shutdown in progress
                "1090",

                // ORA-03113: end-of-file on communication channel
                "3113",

                // ORA-03114: not connected to ORACLE
                "3114",

                // ORA-03135: connection lost contact
                "3135",

                // ORA-12170: TNS:connect timeout occurred
                "12170",

                // ORA-12541: TNS:no listener
                "12541",

                // ORA-12170: TNS:connect timeout occurred
                "12170",

                // ORA-01012: not logged on
                "1012",

                // ORA-25408: can not safely replay call
                "25408"
            });

            ConflictExceptionCodes.UnionWith(new List<string>
            {
                // ORA-08177: can't serialize access for this transaction
                "8177"
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

            if (string.IsNullOrEmpty(errorCode))
            {
                return HttpStatusCode.InternalServerError;
            }

            if (BadRequestExceptionCodes.Contains(errorCode))
            {
                return HttpStatusCode.BadRequest;
            }

            if (ConflictExceptionCodes.Contains(errorCode))
            {
                return HttpStatusCode.Conflict;
            }

            return HttpStatusCode.InternalServerError;
        }

        /// <summary>
        /// Extracts Oracle error code from exception.
        /// </summary>
        private static string GetOracleErrorCode(DbException e)
        {
            // Oracle.ManagedDataAccess.Client.OracleException has a Number property
            // We need to use reflection since we don't have a direct reference to the Oracle library here
            var oracleException = e;
            var numberProperty = oracleException.GetType().GetProperty("Number");
            if (numberProperty != null)
            {
                var errorNumber = numberProperty.GetValue(oracleException);
                if (errorNumber != null)
                {
                    return errorNumber.ToString()!;
                }
            }

            return string.Empty;
        }
    }
}
