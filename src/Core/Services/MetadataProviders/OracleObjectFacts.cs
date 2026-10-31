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
