// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services
{
    public partial class OracleMetadataProvider
    {
        internal override SqlLiteralRewriter LiteralRewriter { get; } = new OracleSqlLiteralRewriter();

        /// <summary>
        /// Oracle unquoted identifiers are stored uppercase. Match an authored name to that
        /// spelling once, while metadata is loaded. Later lookups stay ordinal-exact.
        /// </summary>
        protected override string ResolvePhysicalColumnName(SourceDefinition sourceDefinition, string configColumnName)
        {
            foreach (string physicalName in sourceDefinition.Columns.Keys)
            {
                if (string.Equals(physicalName, configColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return physicalName;
                }
            }

            return configColumnName;
        }

        protected override DatabaseObject CreateStoredProcedureDatabaseObject(string source, EntitySourceType sourceType)
        {
            (string? packageName, string subprogramSource) = SplitOraclePackageQualifier(source);
            (string schemaName, string dbObjectName) = ParseSchemaAndDbTableName(subprogramSource);
            DatabaseStoredProcedure procedure = new(schemaName, dbObjectName)
            {
                SourceType = sourceType,
                StoredProcedureDefinition = new()
            };
            if (!string.IsNullOrEmpty(packageName))
            {
                OracleObjectFacts.Procedure(procedure).PackageName = packageName;
            }

            return procedure;
        }

        /// <summary>
        /// Rewrites authored field, mapping, and permission names to the spelling metadata will
        /// compare later. Include and exclude sets use the exposed name. Mapping keys and field
        /// names use the physical column name so exact lookups find them.
        /// </summary>
        protected override void NormalizeConfiguredIdentifiers(
            string entityName,
            Entity entity,
            IReadOnlyDictionary<string, string>? exposedToBacking)
        {
            if (EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                RewriteFieldAndMappingNames(entity, databaseObject.SourceDefinition);
            }

            if (exposedToBacking is not null)
            {
                RewritePermissionNames(entity, exposedToBacking);
            }
        }

        private void RewriteFieldAndMappingNames(Entity entity, SourceDefinition sourceDefinition)
        {
            if (entity.Fields is not null)
            {
                foreach (FieldMetadata field in entity.Fields)
                {
                    field.Name = ResolvePhysicalColumnName(sourceDefinition, field.Name);
                }
            }

            if (entity.Mappings is null || entity.Mappings.Count == 0)
            {
                return;
            }

            Dictionary<string, string> rewritten = new(entity.Mappings.Comparer);
            foreach ((string key, string value) in entity.Mappings)
            {
                rewritten[ResolvePhysicalColumnName(sourceDefinition, key)] = value;
            }

            entity.Mappings.Clear();
            foreach ((string key, string value) in rewritten)
            {
                entity.Mappings[key] = value;
            }
        }

        private static void RewritePermissionNames(Entity entity, IReadOnlyDictionary<string, string> exposedToBacking)
        {
            if (entity.Permissions is null)
            {
                return;
            }

            foreach (EntityPermission permission in entity.Permissions)
            {
                if (permission.Actions is null)
                {
                    continue;
                }

                for (int actionIndex = 0; actionIndex < permission.Actions.Length; actionIndex++)
                {
                    EntityAction action = permission.Actions[actionIndex];
                    EntityActionFields? fields = action.Fields;
                    if (fields is not null)
                    {
                        fields = fields with
                        {
                            Exclude = CanonicalizeFieldSet(fields.Exclude, exposedToBacking),
                            Include = fields.Include is null ? null : CanonicalizeFieldSet(fields.Include, exposedToBacking)
                        };
                    }

                    EntityActionPolicy? policy = action.Policy;
                    if (policy?.Request is not null || policy?.Database is not null)
                    {
                        policy = policy with
                        {
                            Request = policy.Request is null ? null : RewritePolicyFieldTokens(policy.Request, exposedToBacking),
                            Database = policy.Database is null ? null : RewritePolicyFieldTokens(policy.Database, exposedToBacking)
                        };
                    }

                    permission.Actions[actionIndex] = action with { Fields = fields, Policy = policy };
                }
            }
        }

        private static HashSet<string> CanonicalizeFieldSet(HashSet<string> fields, IReadOnlyDictionary<string, string> exposedToBacking)
        {
            HashSet<string> rewritten = new(fields.Comparer);
            foreach (string field in fields)
            {
                rewritten.Add(CanonicalExposedName(field, exposedToBacking));
            }

            return rewritten;
        }

        private static string CanonicalExposedName(string fieldName, IReadOnlyDictionary<string, string> exposedToBacking)
        {
            foreach (string exposedName in exposedToBacking.Keys)
            {
                if (string.Equals(exposedName, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return exposedName;
                }
            }

            return fieldName;
        }

        private static string RewritePolicyFieldTokens(string policy, IReadOnlyDictionary<string, string> exposedToBacking)
        {
            return Regex.Replace(
                policy,
                Regex.Escape(AuthorizationResolver.FIELD_PREFIX) + @"([A-Za-z_][A-Za-z0-9_]*)",
                match => AuthorizationResolver.FIELD_PREFIX + CanonicalExposedName(match.Groups[1].Value, exposedToBacking));
        }

        /// <summary>
        /// Splits <c>schema.package.subprogram</c> into the package name and the
        /// <c>schema.subprogram</c> remainder the shared two-token parser accepts.
        /// Standalone sources are returned unchanged.
        /// </summary>
        private static (string? packageName, string subprogramSource) SplitOraclePackageQualifier(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return (null, source);
            }

            int firstDot = -1;
            int secondDot = -1;
            bool inBracket = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '[')
                {
                    inBracket = true;
                }
                else if (c == ']')
                {
                    inBracket = false;
                }
                else if (c == '.' && !inBracket)
                {
                    if (firstDot == -1)
                    {
                        firstDot = i;
                    }
                    else if (secondDot == -1)
                    {
                        secondDot = i;
                        break;
                    }
                }
            }

            if (secondDot == -1)
            {
                return (null, source);
            }

            bool hasFourthToken = false;
            bool nestedBracket = false;
            for (int i = secondDot + 1; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '[')
                {
                    nestedBracket = true;
                }
                else if (c == ']')
                {
                    nestedBracket = false;
                }
                else if (c == '.' && !nestedBracket)
                {
                    hasFourthToken = true;
                    break;
                }
            }

            if (hasFourthToken)
            {
                throw new DataApiBuilderException(
                    message: $"Invalid Oracle stored procedure source: \"{source}\". Expected " +
                             "\"schema.subprogram\" (standalone) or \"schema.package.subprogram\" (packaged).",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            string packagePart = source[(firstDot + 1)..secondDot].Replace("[[", "[").Replace("]]", "]");
            if (packagePart.StartsWith('[') && packagePart.EndsWith(']') && packagePart.Length >= 2)
            {
                packagePart = packagePart[1..^1];
            }

            string subprogramPart = source[..firstDot] + "." + source[(secondDot + 1)..];
            return (packagePart, subprogramPart);
        }
    }
}
