// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Oracle string bind kind carried beside <see cref="DbConnectionParam"/>.
    /// The shared parameter model stays free of Oracle-only fields; the facts live only as
    /// long as the parameter object does.
    /// </summary>
    internal static class OracleStringBindFacts
    {
        internal enum Kind
        {
            None = 0,
            Clob,
            NClob,
            NChar
        }

        private static readonly ConditionalWeakTable<DbConnectionParam, Box> Facts = new();

        private sealed class Box
        {
            public Kind Kind;
        }

        public static void Set(DbConnectionParam parameter, Kind kind)
        {
            if (kind == Kind.None)
            {
                return;
            }

            Facts.GetOrCreateValue(parameter).Kind = kind;
        }

        public static Kind Get(DbConnectionParam parameter)
        {
            return Facts.TryGetValue(parameter, out Box? box) ? box.Kind : Kind.None;
        }
    }
}
