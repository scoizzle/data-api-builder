// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Services
{
    public partial class OracleMetadataProvider
    {
        /// <summary>
        /// Distinct base tables a view reads, following nested views and synonyms.
        /// A table referenced only inside a subquery is counted too, so such a view is treated
        /// as multi-table; a self-join counts once.
        /// </summary>
        internal const string VIEW_BASE_TABLE_COUNT_QUERY =
            "SELECT COUNT(DISTINCT CASE WHEN REFERENCED_TYPE = 'TABLE' " +
            "THEN REFERENCED_OWNER || '.' || REFERENCED_NAME END) " +
            "FROM ALL_DEPENDENCIES " +
            "START WITH OWNER = :owner AND NAME = :view_name AND TYPE = 'VIEW' " +
            "CONNECT BY NOCYCLE OWNER = PRIOR REFERENCED_OWNER AND NAME = PRIOR REFERENCED_NAME " +
            "AND TYPE = PRIOR REFERENCED_TYPE AND PRIOR REFERENCED_TYPE IN ('VIEW', 'SYNONYM')";

        /// <summary>
        /// An enabled INSTEAD OF DELETE trigger defines what a DELETE on the view does, so the
        /// view stays deletable whatever it joins.
        /// </summary>
        internal const string VIEW_INSTEAD_OF_DELETE_QUERY =
            "SELECT COUNT(*) FROM ALL_TRIGGERS " +
            "WHERE TABLE_OWNER = :owner AND TABLE_NAME = :view_name " +
            "AND BASE_OBJECT_TYPE = 'VIEW' AND TRIGGER_TYPE = 'INSTEAD OF' " +
            "AND TRIGGERING_EVENT LIKE '%DELETE%' AND STATUS = 'ENABLED'";

        /// <summary>
        /// Decides whether DELETE through a view is rejected. Null means allowed.
        /// Unresolvable dependencies (no base table visible, typically missing privileges) fail
        /// closed: the delete is rejected rather than run against an unknown set of tables.
        /// </summary>
        internal static string? ClassifyViewDelete(int baseTableCount, bool hasInsteadOfDeleteTrigger)
        {
            if (hasInsteadOfDeleteTrigger)
            {
                return null;
            }

            return baseTableCount switch
            {
                1 => null,
                0 => "its base tables could not be resolved from ALL_DEPENDENCIES",
                _ => $"it reads {baseTableCount} tables"
            };
        }

        /// <summary>
        /// Sets <see cref="OracleObjectFacts.ViewFacts.DeleteRejection"/> for every view entity
        /// backed by <paramref name="owner"/>.<paramref name="name"/>. A command timeout fails
        /// startup like the other catalog reads; any other catalog error rejects deletes through
        /// the view and is logged.
        /// </summary>
        private async Task LoadViewDeleteRejectionAsync(
            OracleConnection connection,
            string owner,
            string name,
            CancellationToken cancellationToken)
        {
            List<DatabaseView> views = EntityToDatabaseObject.Values
                .OfType<DatabaseView>()
                .Where(view => string.Equals(view.Name, name, StringComparison.Ordinal)
                    && string.Equals(
                        string.IsNullOrEmpty(view.SchemaName) ? GetDefaultSchemaName() : view.SchemaName,
                        owner,
                        StringComparison.Ordinal))
                .ToList();
            if (views.Count == 0)
            {
                return;
            }

            string? rejection;
            try
            {
                int baseTableCount = await CountAsync(connection, VIEW_BASE_TABLE_COUNT_QUERY, owner, name, cancellationToken);
                bool hasInsteadOfDelete = await CountAsync(connection, VIEW_INSTEAD_OF_DELETE_QUERY, owner, name, cancellationToken) > 0;
                rejection = ClassifyViewDelete(baseTableCount, hasInsteadOfDelete);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OracleException ex) when (ex.Number == 1013)
            {
                throw MetadataTimeoutException($"reading the dependencies of view {owner}.{name}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not read the dependencies of Oracle view {Owner}.{View}; DELETE through it is rejected.",
                    owner,
                    name);
                rejection = "its dependencies could not be read";
            }

            foreach (DatabaseView view in views)
            {
                OracleObjectFacts.View(view).DeleteRejection = rejection;
            }
        }

        private async Task<int> CountAsync(
            OracleConnection connection,
            string query,
            string owner,
            string name,
            CancellationToken cancellationToken)
        {
            using OracleCommand command = CreateMetadataCommand(connection, query);
            command.Parameters.Add(new OracleParameter("owner", owner));
            command.Parameters.Add(new OracleParameter("view_name", name));
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? 0 : Convert.ToInt32(value);
        }
    }
}
