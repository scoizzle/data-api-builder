// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Oracle catalog type names. ALL_TAB_COLUMNS and ALL_ARGUMENTS qualify precision
    /// (<c>TIMESTAMP(6)</c>, <c>INTERVAL DAY(2) TO SECOND(6)</c>). Comparisons and CLR
    /// mapping use the unqualified form.
    /// </summary>
    internal static class OracleSqlType
    {
        private static readonly Regex PrecisionQualifier = new(
            @"\(\d+\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Whitespace = new(
            @"\s+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Strips precision qualifiers and collapses whitespace. Null and blank become empty.
        /// </summary>
        public static string Normalize(string? sqlType)
        {
            if (string.IsNullOrWhiteSpace(sqlType))
            {
                return string.Empty;
            }

            string stripped = PrecisionQualifier.Replace(sqlType.Trim().ToUpperInvariant(), string.Empty);
            return Whitespace.Replace(stripped, " ").Trim();
        }

        /// <summary>
        /// Subprogram argument and RETURN types DAB cannot bind, rejected at startup (#60).
        /// INTERVAL YEAR TO MONTH has no CLR equivalent (a year-month span is not a
        /// <see cref="TimeSpan"/>). PL/SQL-only types (PL/SQL BOOLEAN, PLS_INTEGER,
        /// BINARY_INTEGER, records and index-by tables) cannot be bound from a SQL client.
        /// </summary>
        public static bool IsUnsupportedSubprogramType(string? sqlType)
        {
            string normalized = Normalize(sqlType);
            return normalized is "INTERVAL YEAR TO MONTH" or "BINARY_INTEGER" or "PLS_INTEGER"
                || normalized.StartsWith("PL/SQL ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Maps a catalog type name to the CLR type DAB exposes. Unknown names stay <see cref="object"/>.
        /// </summary>
        public static Type ToClrType(string? sqlType)
        {
            return Normalize(sqlType) switch
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
                "INTERVAL DAY TO SECOND" => typeof(TimeSpan),
                "INTERVAL YEAR TO MONTH" => typeof(TimeSpan),
                "RAW" => typeof(byte[]),
                "BLOB" => typeof(byte[]),
                "LONG" => typeof(string),
                "LONG RAW" => typeof(byte[]),
                "ROWID" => typeof(string),
                "UROWID" => typeof(string),
                "XMLTYPE" => typeof(string),
                "CURSOR" or "REF CURSOR" => typeof(IDataReader),
                _ => typeof(object)
            };
        }
    }
}
