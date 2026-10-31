// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

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
        /// compare later. Mapping keys, field names, and permission include/exclude sets use the
        /// backing (catalog) column name: the authorization resolver subtracts excluded from
        /// included in backing space, so a permission left in exposed spelling would not match.
        /// Policy tokens stay in exposed space because the OData model names properties by
        /// exposed name and the filter parser is case-sensitive.
        /// </summary>
        protected override void NormalizeConfiguredIdentifiers(
            string entityName,
            Entity entity,
            IReadOnlyDictionary<string, string>? exposedToBacking)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                return;
            }

            SourceDefinition? sourceDefinition = databaseObject.SourceDefinition;
            if (sourceDefinition is null)
            {
                return;
            }

            RewriteFieldAndMappingNames(entity, sourceDefinition);

            if (exposedToBacking is not null)
            {
                RewritePermissionNames(entityName, entity, exposedToBacking, databaseObject, sourceDefinition);
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

        private void RewritePermissionNames(
            string entityName,
            Entity entity,
            IReadOnlyDictionary<string, string> exposedToBacking,
            DatabaseObject databaseObject,
            SourceDefinition sourceDefinition)
        {
            if (entity.Permissions is null)
            {
                return;
            }

            // Tables and views both have catalog columns, so a typo in a permission list is caught
            // for either. Stored procedures carry no column permissions to verify.
            bool validateColumns = databaseObject.SourceType is EntitySourceType.Table or EntitySourceType.View;

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
                            Exclude = ResolveBackingFieldSet(fields.Exclude, exposedToBacking, sourceDefinition),
                            Include = fields.Include is null ? null : ResolveBackingFieldSet(fields.Include, exposedToBacking, sourceDefinition)
                        };

                        if (validateColumns)
                        {
                            fields = ValidatePermissionColumns(entityName, entity.IsAutoentity, permission.Role, action.Action, databaseObject, sourceDefinition, fields);
                        }
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

        private HashSet<string> ResolveBackingFieldSet(
            HashSet<string> fields,
            IReadOnlyDictionary<string, string> exposedToBacking,
            SourceDefinition sourceDefinition)
        {
            HashSet<string> rewritten = new(fields.Comparer);
            foreach (string field in fields)
            {
                rewritten.Add(ResolveBackingColumnName(field, exposedToBacking, sourceDefinition));
            }

            return rewritten;
        }

        /// <summary>
        /// Resolves an authored permission name to the backing column spelling. An exposed name
        /// (field alias, field name, or mapping value) wins, matching how a request resolves it;
        /// otherwise the name is matched against the catalog columns. The wildcard is kept as is.
        /// </summary>
        private string ResolveBackingColumnName(
            string name,
            IReadOnlyDictionary<string, string> exposedToBacking,
            SourceDefinition sourceDefinition)
        {
            if (name == AuthorizationResolver.WILDCARD)
            {
                return name;
            }

            return exposedToBacking.TryGetValue(name, out string? backing)
                ? backing
                : ResolvePhysicalColumnName(sourceDefinition, name);
        }

        /// <summary>
        /// Fails initialization when a hand-authored permission names a column the table or view does
        /// not have. Without this, a typo in an exclude list would silently leave the intended column
        /// readable. Autoentities share one permission template across every table the pattern matches,
        /// so a name that is absent from one generated entity is dropped with a warning instead.
        /// </summary>
        /// <returns>The fields to apply: unchanged for hand-authored entities, pruned for autoentities.</returns>
        private EntityActionFields ValidatePermissionColumns(
            string entityName,
            bool isAutoentity,
            string role,
            EntityActionOperation operation,
            DatabaseObject databaseObject,
            SourceDefinition sourceDefinition,
            EntityActionFields fields)
        {
            HashSet<string> exclude = fields.Exclude;
            HashSet<string>? include = fields.Include;

            if (isAutoentity)
            {
                exclude = DropUnknownColumns(entityName, role, operation, databaseObject, sourceDefinition, "exclude", fields.Exclude);
                include = fields.Include is null
                    ? null
                    : DropUnknownColumns(entityName, role, operation, databaseObject, sourceDefinition, "include", fields.Include);
                return fields with { Exclude = exclude, Include = include };
            }

            foreach (string name in fields.Exclude)
            {
                ThrowIfUnknownColumn(entityName, role, operation, databaseObject, sourceDefinition, "exclude", name);
            }

            foreach (string name in fields.Include ?? [])
            {
                ThrowIfUnknownColumn(entityName, role, operation, databaseObject, sourceDefinition, "include", name);
            }

            return fields;
        }

        private HashSet<string> DropUnknownColumns(
            string entityName,
            string role,
            EntityActionOperation operation,
            DatabaseObject databaseObject,
            SourceDefinition sourceDefinition,
            string listName,
            HashSet<string> names)
        {
            HashSet<string> kept = new(names.Comparer);
            foreach (string name in names)
            {
                if (name == AuthorizationResolver.WILDCARD || sourceDefinition.Columns.ContainsKey(name))
                {
                    kept.Add(name);
                    continue;
                }

                _logger.LogWarning(
                    "Autoentity '{entityName}', role '{role}', action '{operation}': permission {listName} field '{name}' is not a column of '{schema}.{object}' and is ignored for this entity.",
                    entityName, role, operation, listName, name, databaseObject.SchemaName, databaseObject.Name);
            }

            return kept;
        }

        private static void ThrowIfUnknownColumn(
            string entityName,
            string role,
            EntityActionOperation operation,
            DatabaseObject databaseObject,
            SourceDefinition sourceDefinition,
            string listName,
            string name)
        {
            if (name == AuthorizationResolver.WILDCARD || sourceDefinition.Columns.ContainsKey(name))
            {
                return;
            }

            throw new DataApiBuilderException(
                message: $"Entity '{entityName}', role '{role}', action '{operation}': permission {listName} field '{name}' " +
                         $"is not a column of '{databaseObject.SchemaName}.{databaseObject.Name}'.",
                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        /// <summary>
        /// Copies the permission template so autoentities do not share (and later rewrite)
        /// the same <see cref="EntityPermission"/> instances.
        /// </summary>
        private static EntityPermission[] ClonePermissions(EntityPermission[] permissions)
        {
            return [.. permissions.Select(permission => permission with
            {
                Actions = permission.Actions is null ? permission.Actions! : [.. permission.Actions.Select(action => action with { })]
            })];
        }

        /// <summary>
        /// Rewrites policy field tokens to the exposed spelling. The first match of an authored
        /// token against the exposed-name map supplies the casing the OData model uses.
        /// </summary>
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
