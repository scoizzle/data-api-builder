// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Service.Exceptions;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Oracle connection-string rules shared by the query executor and the metadata provider.
    /// Hosted mode validates transport security and does not rewrite it. Statement-cache bounds
    /// are applied only when the operator left both cache attributes unset.
    /// </summary>
    internal static class OracleConnectionStringPolicy
    {
        /// <summary>
        /// Fixed statement-cache ceiling used when the connection string sets neither
        /// <c>Statement Cache Size</c> nor <c>Self Tuning</c>. ODP.NET's builder reports an
        /// unset size as 0 and leaves self-tuning on, and self-tuning can grow the cache until
        /// the session hits <c>open_cursors</c>. 20 is a fixed cap just above the driver's
        /// commonly cited default of 10. It is overridable from the connection string and was
        /// not chosen from a measured cursor plateau.
        /// </summary>
        public const int DefaultStatementCacheSize = 20;

        /// <summary>
        /// <c>open_cursors</c> below this value logs one startup warning. 1500 is the size the
        /// Oracle integration job sets so a full category run can finish.
        /// </summary>
        public const int OpenCursorsWarningThreshold = 1500;

        private static readonly Regex ProtocolRegex = new(
            @"PROTOCOL\s*=\s*([A-Za-z0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private static readonly Regex DistinguishedNameMatchRegex = new(
            @"SSL_SERVER_DN_MATCH\s*=\s*([A-Za-z0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        public static bool ShouldWarnAboutOpenCursors(int openCursors)
        {
            return openCursors < OpenCursorsWarningThreshold;
        }

        /// <summary>
        /// Validates hosted transport when requested, then returns a connection string whose
        /// statement cache is bounded unless the operator already set a cache attribute.
        /// </summary>
        public static string Prepare(string connectionString, bool requireHostedTransport)
        {
            if (requireHostedTransport)
            {
                RequireHostedTransportSecurity(connectionString);
            }

            return ApplyBoundedStatementCache(connectionString);
        }

        /// <summary>
        /// Appends <c>Statement Cache Size=20;Self Tuning=false</c> when neither attribute is
        /// present. An explicit value, including <c>Statement Cache Size=0</c>, is preserved
        /// and the other attribute is left untouched.
        /// </summary>
        public static string ApplyBoundedStatementCache(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)
                || HasAttribute(connectionString, "Statement Cache Size")
                || HasAttribute(connectionString, "StatementCacheSize")
                || HasAttribute(connectionString, "Self Tuning")
                || HasAttribute(connectionString, "SelfTuning"))
            {
                return connectionString;
            }

            string trimmed = connectionString.TrimEnd();
            if (!trimmed.EndsWith(';'))
            {
                trimmed += ";";
            }

            return trimmed
                + "Statement Cache Size="
                + DefaultStatementCacheSize.ToString(CultureInfo.InvariantCulture)
                + ";Self Tuning=false";
        }

        /// <summary>
        /// Hosted Oracle traffic must already be TCPS with server DN matching turned on.
        /// The string is not rewritten: missing or disabled DN match, cleartext, TNS aliases,
        /// and mixed-protocol descriptors fail startup.
        /// </summary>
        public static void RequireHostedTransportSecurity(string connectionString)
        {
            string dataSource;
            try
            {
                dataSource = new OracleConnectionStringBuilder(connectionString).DataSource ?? string.Empty;
            }
            catch (Exception ex) when (ex is not DataApiBuilderException)
            {
                throw HostedTransportException(
                    "The Oracle connection string could not be parsed, so hosted transport security (PROTOCOL=TCPS and SSL_SERVER_DN_MATCH) could not be verified.",
                    ex);
            }

            if (string.IsNullOrWhiteSpace(dataSource))
            {
                throw HostedTransportException(
                    "Hosted Oracle connections require a Data Source that uses PROTOCOL=TCPS and SSL_SERVER_DN_MATCH=TRUE, or a tcps:// URL with ssl_server_dn_match=yes.");
            }

            if (dataSource.Contains("(DESCRIPTION", StringComparison.OrdinalIgnoreCase))
            {
                RequireDescriptor(dataSource);
                return;
            }

            if (dataSource.Contains("://", StringComparison.Ordinal)
                || dataSource.Contains(':')
                || dataSource.Contains('/'))
            {
                RequireEzConnect(dataSource);
                return;
            }

            throw HostedTransportException(
                "Hosted Oracle connections cannot use a TNS alias because PROTOCOL=TCPS and SSL_SERVER_DN_MATCH cannot be verified. Use a full DESCRIPTION or a tcps:// URL with ssl_server_dn_match=yes.");
        }

        private static void RequireDescriptor(string dataSource)
        {
            List<string> protocols = ReadValues(ProtocolRegex, dataSource);
            if (protocols.Count == 0)
            {
                throw HostedTransportException(
                    "Hosted Oracle DESCRIPTION data sources must include an ADDRESS with PROTOCOL=TCPS and SSL_SERVER_DN_MATCH=TRUE.");
            }

            bool anyTcps = protocols.Exists(protocol => protocol.Equals("TCPS", StringComparison.OrdinalIgnoreCase));
            bool anyOther = protocols.Exists(protocol => !protocol.Equals("TCPS", StringComparison.OrdinalIgnoreCase));
            if (anyTcps && anyOther)
            {
                throw HostedTransportException(
                    "Hosted Oracle descriptors must not mix PROTOCOL=TCPS with other ADDRESS protocols.");
            }

            if (!anyTcps)
            {
                throw HostedTransportException(
                    "Hosted Oracle descriptors must use PROTOCOL=TCPS. Native network encryption is not a substitute for TCPS and server DN matching.");
            }

            RequireAffirmativeDnMatch(ReadValues(DistinguishedNameMatchRegex, dataSource));
        }

        private static void RequireEzConnect(string dataSource)
        {
            string trimmed = dataSource.Trim();
            if (!trimmed.StartsWith("tcps://", StringComparison.OrdinalIgnoreCase))
            {
                throw HostedTransportException(
                    "Hosted Oracle connections that use host:port/service or tcp:// must switch to TCPS. Example: tcps://host:2484/service?ssl_server_dn_match=yes.");
            }

            int query = trimmed.IndexOf('?', StringComparison.Ordinal);
            string? match = null;
            if (query >= 0)
            {
                foreach (string pair in trimmed[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = pair.IndexOf('=', StringComparison.Ordinal);
                    if (separator <= 0)
                    {
                        continue;
                    }

                    if (pair[..separator].Equals("ssl_server_dn_match", StringComparison.OrdinalIgnoreCase))
                    {
                        match = Uri.UnescapeDataString(pair[(separator + 1)..]);
                    }
                }
            }

            if (match is null)
            {
                throw HostedTransportException(
                    "Hosted tcps:// URLs must set ssl_server_dn_match to yes, true, or on.");
            }

            if (!IsAffirmative(match))
            {
                throw HostedTransportException(
                    "Hosted Oracle connections require ssl_server_dn_match=yes (true and on are also accepted). An explicit disable is rejected.");
            }
        }

        private static void RequireAffirmativeDnMatch(List<string> values)
        {
            if (values.Count == 0)
            {
                throw HostedTransportException(
                    "Hosted Oracle TCPS connections require SSL_SERVER_DN_MATCH=TRUE (yes and on are also accepted). DAB does not add the setting.");
            }

            if (values.Exists(value => !IsAffirmative(value)))
            {
                throw HostedTransportException(
                    "Hosted Oracle connections reject SSL_SERVER_DN_MATCH values other than TRUE, yes, or on.");
            }
        }

        private static bool IsAffirmative(string value)
        {
            return value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                || value.Equals("YES", StringComparison.OrdinalIgnoreCase)
                || value.Equals("ON", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> ReadValues(Regex pattern, string text)
        {
            List<string> values = new();
            foreach (Match match in pattern.Matches(text))
            {
                values.Add(match.Groups[1].Value);
            }

            return values;
        }

        internal static bool HasAttribute(string connectionString, string attributeName)
        {
            int index = 0;
            while (index < connectionString.Length)
            {
                while (index < connectionString.Length
                    && (connectionString[index] == ';' || char.IsWhiteSpace(connectionString[index])))
                {
                    index++;
                }

                if (index >= connectionString.Length)
                {
                    break;
                }

                int separator = connectionString.IndexOf('=', index);
                if (separator < 0)
                {
                    break;
                }

                string key = connectionString[index..separator].Trim();
                index = separator + 1;
                if (index < connectionString.Length && connectionString[index] == '"')
                {
                    index++;
                    while (index < connectionString.Length && connectionString[index] != '"')
                    {
                        index++;
                    }

                    if (index < connectionString.Length)
                    {
                        index++;
                    }
                }
                else
                {
                    int depth = 0;
                    while (index < connectionString.Length)
                    {
                        char current = connectionString[index];
                        if (current == '(')
                        {
                            depth++;
                        }
                        else if (current == ')' && depth > 0)
                        {
                            depth--;
                        }
                        else if (current == ';' && depth == 0)
                        {
                            break;
                        }

                        index++;
                    }
                }

                if (key.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static DataApiBuilderException HostedTransportException(string message, Exception? inner = null)
        {
            return new DataApiBuilderException(
                message,
                HttpStatusCode.ServiceUnavailable,
                DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                inner);
        }
    }
}
