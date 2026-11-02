// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Oracle package, function, overload, and LOB facts that must not be serialized on the
    /// shared database model. Entries live only as long as the database object does.
    /// </summary>
    internal static class OracleObjectFacts
    {
        private static readonly ConditionalWeakTable<DatabaseStoredProcedure, ProcedureFacts> Procedures = new();
        private static readonly ConditionalWeakTable<ColumnDefinition, LobFacts> Columns = new();
        private static readonly ConditionalWeakTable<ParameterDefinition, ArgumentFacts> Arguments = new();

        public static ProcedureFacts Procedure(DatabaseStoredProcedure procedure)
        {
            return Procedures.GetOrCreateValue(procedure);
        }

        public static LobFacts Column(ColumnDefinition column)
        {
            return Columns.GetOrCreateValue(column);
        }

        public static ArgumentFacts Argument(ParameterDefinition parameter)
        {
            return Arguments.GetOrCreateValue(parameter);
        }

        internal sealed class ProcedureFacts
        {
            public string? PackageName { get; set; }

            public bool IsFunction { get; set; }

            public string? Overload { get; set; }
        }

        internal sealed class LobFacts
        {
            public bool IsClob { get; set; }

            public bool IsNClob { get; set; }

            /// <summary>
            /// NVARCHAR2 or NCHAR. National character data uses <c>NVarchar2</c> rather than <c>Varchar2</c>.
            /// Fixed-length <c>NCHAR</c> also sets <see cref="IsFixedChar"/> so the bind is blank-padded.
            /// </summary>
            public bool IsNChar { get; set; }

            /// <summary>
            /// CHAR or NCHAR. Oracle blank-pads comparisons only when the bind is the same fixed type
            /// and width. VARCHAR2 and NVARCHAR2 leave this false.
            /// </summary>
            public bool IsFixedChar { get; set; }

            /// <summary>
            /// Catalog <c>DATA_TYPE</c> (for example <c>DATE</c> or <c>TIMESTAMP(6)</c>).
            /// RETURNING binds use it when <see cref="ColumnDefinition.SystemType"/> cannot tell
            /// DATE from TIMESTAMP. Null when the column was not loaded from the catalog.
            /// </summary>
            public string? PhysicalType { get; set; }

            public bool IsBlob { get; set; }
        }

        /// <summary>
        /// Formal argument name from ALL_ARGUMENTS. The exposed dictionary key may be the
        /// configured spelling; the quoted call must use this catalog name.
        /// </summary>
        internal sealed class ArgumentFacts
        {
            public string? CatalogName { get; set; }
        }
    }
}
