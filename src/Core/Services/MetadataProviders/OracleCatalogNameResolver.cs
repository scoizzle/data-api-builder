// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Resolves an Oracle (schema, name) pair to the local base object, following
    /// private and public synonyms. Used at metadata startup so SQL and catalog
    /// lookups always use physical names.
    /// </summary>
    internal static class OracleCatalogNameResolver
    {
        internal const int MaxSynonymDepth = 10;
        internal const string PublicOwner = "PUBLIC";

        internal readonly record struct SynonymRow(
            string Owner,
            string SynonymName,
            string TableOwner,
            string TableName,
            string? DbLink);

        /// <summary>
        /// Oracle unqualified order: current-schema object, then private synonym,
        /// then public synonym. Schema-qualified names (allowPublicFallback = false)
        /// never consult public synonyms. Following a synonym is always a
        /// schema-qualified lookup of TABLE_OWNER.TABLE_NAME.
        /// </summary>
        internal static async Task<(string Schema, string Name)> ResolveAsync(
            string schema,
            string name,
            bool allowPublicFallback,
            Func<string, string, Task<string?>> getObjectType,
            Func<string, string, Task<SynonymRow?>> getSynonym)
        {
            string currentOwner = schema.ToUpperInvariant();
            string currentName = name.ToUpperInvariant();
            HashSet<(string Owner, string Name)> visited = new();
            bool triedPublicFallback = false;

            for (int depth = 0; depth <= MaxSynonymDepth; depth++)
            {
                if (!visited.Add((currentOwner, currentName)))
                {
                    throw new DataApiBuilderException(
                        message: $"Oracle synonym {currentOwner}.{currentName} has a circular definition.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                string? objectType = await getObjectType(currentOwner, currentName);
                if (objectType is not null &&
                    !objectType.Equals("SYNONYM", StringComparison.OrdinalIgnoreCase))
                {
                    return (currentOwner, currentName);
                }

                SynonymRow? synonym = await getSynonym(currentOwner, currentName);

                if (synonym is SynonymRow row)
                {
                    FollowSynonym(row);
                    currentOwner = row.TableOwner.ToUpperInvariant();
                    currentName = row.TableName.ToUpperInvariant();
                    allowPublicFallback = false;
                    continue;
                }

                if (allowPublicFallback
                    && !triedPublicFallback
                    && !currentOwner.Equals(PublicOwner, StringComparison.OrdinalIgnoreCase))
                {
                    triedPublicFallback = true;
                    currentOwner = PublicOwner;
                    continue;
                }

                throw new DataApiBuilderException(
                    message: $"Could not resolve Oracle object {schema}.{name} to a local table, view, or subprogram.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            throw new DataApiBuilderException(
                message: $"Oracle synonym chain for {schema}.{name} exceeded {MaxSynonymDepth} levels.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        internal static bool IsTableLike(string objectType)
        {
            return objectType.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
                || objectType.Equals("VIEW", StringComparison.OrdinalIgnoreCase)
                || objectType.Equals("MATERIALIZED VIEW", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsSubprogramLike(string objectType)
        {
            return objectType.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)
                || objectType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)
                || objectType.Equals("PACKAGE", StringComparison.OrdinalIgnoreCase);
        }

        private static void FollowSynonym(SynonymRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.DbLink))
            {
                throw new DataApiBuilderException(
                    message: $"Oracle synonym {row.Owner}.{row.SynonymName} points at a remote object ({row.DbLink}). Remote synonyms are not supported.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            if (string.IsNullOrWhiteSpace(row.TableOwner) || string.IsNullOrWhiteSpace(row.TableName))
            {
                throw new DataApiBuilderException(
                    message: $"Oracle synonym {row.Owner}.{row.SynonymName} does not name a local object.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
        }
    }
}
