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

        public static ProcedureFacts Procedure(DatabaseStoredProcedure procedure)
        {
            return Procedures.GetOrCreateValue(procedure);
        }

        public static LobFacts Column(ColumnDefinition column)
        {
            return Columns.GetOrCreateValue(column);
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
    }
}
