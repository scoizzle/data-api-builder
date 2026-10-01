// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Executes a relational read plan and rebuilds its JSON document in C#: the page cursor
    /// runs first, its rows seed the per-relationship child cursors, and every child row is
    /// attached to its parent by correlation key. From the caller's perspective this mirrors
    /// the other database engines' reads (the same handler-based execution and the same
    /// single JsonDocument per query), except the JSON is assembled here instead of in SQL.
    /// </summary>
    internal static class RelationalReadExecutor
    {
        /// <summary>
        /// Maximum binds one child cursor statement may carry. Oracle allows 65535 binds per
        /// statement; staying well below keeps the generated SQL and bind arrays sane.
        /// </summary>
        private const int MaxBindsPerChildCursor = 30000;

        /// <summary>
        /// Executes the page cursor and all nested cursors, returning the JSON document the
        /// JSON-based engines would have returned: an array for list queries (empty when there
        /// are no rows) and a single object for point queries (null when there is no row).
        /// When <paramref name="connection"/> is supplied (multiple-create follow-up reads on an
        /// uncommitted transaction) every cursor runs on that connection and transaction.
        /// </summary>
        public static async Task<JsonDocument?> ExecuteAsync(
            IRelationalReadPlanBuilder planBuilder,
            RelationalReadCursor pageCursor,
            SqlQueryStructure structure,
            IQueryExecutor queryExecutor,
            string dataSourceName,
            HttpContext? httpContext,
            DbConnection? connection = null,
            DbTransaction? transaction = null)
        {
            RelationalExecutionContext context = new(
                planBuilder,
                queryExecutor,
                structure.Parameters,
                dataSourceName,
                httpContext,
                connection,
                transaction);

            List<RelationalReadRow> pageRows = await ExecuteCursorAsync(context, pageCursor);

            if (!pageCursor.IsList && pageRows.Count == 0)
            {
                // Mirror the JSON path: a row-less point query returns no document.
                return null;
            }

            List<JsonNode?> rowValues = await AssembleRowsAsync(context, pageCursor, pageRows);

            JsonNode result = pageCursor.IsList
                ? new JsonArray(rowValues.Select(value => value?.DeepClone()).ToArray())
                : rowValues[0]!;

            return JsonDocument.Parse(result.ToJsonString());
        }

        /// <summary>
        /// Returns one assembled JSON object per input row. Child cursors are built and executed
        /// once per level, then each parent row picks its children by correlation key.
        /// </summary>
        private static async Task<List<JsonNode?>> AssembleRowsAsync(
            RelationalExecutionContext context,
            RelationalReadCursor cursor,
            List<RelationalReadRow> rows)
        {
            Dictionary<string, ChildCursorResult> children = await ExecuteChildrenAsync(context, cursor, rows);

            List<JsonNode?> values = new(rows.Count);
            foreach (RelationalReadRow row in rows)
            {
                values.Add(BuildRowObject(cursor, row, children));
            }

            return values;
        }

        /// <summary>
        /// Executes every relationship cursor nested under the cursor's rows and groups the
        /// assembled child values by parent key. Parent keys are fetched in batches sized to stay
        /// within the per-statement bind budget and the batches are merged here, so arbitrarily
        /// large pages stay on the relational path.
        /// </summary>
        private static async Task<Dictionary<string, ChildCursorResult>> ExecuteChildrenAsync(
            RelationalExecutionContext context,
            RelationalReadCursor cursor,
            List<RelationalReadRow> rows)
        {
            Dictionary<string, ChildCursorResult> children = new(StringComparer.Ordinal);
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> keysByAlias = BuildChildKeys(cursor, rows);
            if (keysByAlias.Count == 0)
            {
                return children;
            }

            int maxKeyColumns = keysByAlias.Keys.Max(alias => cursor.CorrelationAliasesByJoinAlias[alias].Count);
            int batchSize = Math.Max(1, MaxBindsPerChildCursor / Math.Max(1, maxKeyColumns));
            Dictionary<string, (RelationalReadCursor Cursor, List<RelationalReadRow> Rows, List<JsonNode?> Values)> accumulated = new(StringComparer.Ordinal);

            for (int start = 0; ; start += batchSize)
            {
                Dictionary<string, IReadOnlyList<object?[]>> batchKeys = new(StringComparer.Ordinal);
                foreach ((string alias, IReadOnlyList<object?[]> keys) in keysByAlias)
                {
                    if (start < keys.Count)
                    {
                        batchKeys[alias] = keys.Skip(start).Take(batchSize).ToArray();
                    }
                }

                if (batchKeys.Count == 0)
                {
                    break;
                }

                if (!context.PlanBuilder.TryBuildRelationalChildCursors(cursor, batchKeys, out IReadOnlyList<RelationalReadCursor>? childCursors))
                {
                    break;
                }

                foreach (RelationalReadCursor child in childCursors!)
                {
                    List<RelationalReadRow> childRows = await ExecuteCursorAsync(context, child);
                    List<JsonNode?> childValues = await AssembleRowsAsync(context, child, childRows);
                    if (!accumulated.TryGetValue(child.JoinAlias!, out var accumulator))
                    {
                        accumulator = (child, new List<RelationalReadRow>(), new List<JsonNode?>());
                    }

                    accumulator.Rows.AddRange(childRows);
                    accumulator.Values.AddRange(childValues);
                    accumulated[child.JoinAlias!] = accumulator;
                }
            }

            foreach ((string alias, var accumulator) in accumulated)
            {
                children[alias] = new ChildCursorResult(accumulator.Cursor, accumulator.Rows, accumulator.Values);
            }

            return children;
        }

        private static JsonObject BuildRowObject(
            RelationalReadCursor cursor,
            RelationalReadRow row,
            Dictionary<string, ChildCursorResult> children)
            => BuildObject(cursor.Fields, row, children);

        /// <summary>
        /// Builds one row's JSON object. Flattened to-one fields recurse into their own children,
        /// which can contain both nested flattened objects and relationship fields served by
        /// child cursors (a to-many relationship under a flattened to-one row).
        /// </summary>
        private static JsonObject BuildObject(
            IReadOnlyList<RelationalReadField> fields,
            RelationalReadRow row,
            Dictionary<string, ChildCursorResult> children)
        {
            JsonObject obj = new();
            foreach (RelationalReadField field in fields)
            {
                if (field.RelationJoinAlias is not null)
                {
                    obj[field.JsonName] = BuildChildValue(row, field, children);
                }
                else if (field.IsObject)
                {
                    bool exists = field.NullGuardAliases.Any(alias => row[alias] is not null);
                    obj[field.JsonName] = exists ? BuildObject(field.Children, row, children) : null;
                }
                else
                {
                    obj[field.JsonName] = ConvertValue(row[field.Alias]);
                }
            }

            return obj;
        }

        private static JsonNode? BuildChildValue(
            RelationalReadRow row,
            RelationalReadField field,
            Dictionary<string, ChildCursorResult> children)
        {
            if (!children.TryGetValue(field.RelationJoinAlias!, out ChildCursorResult? child))
            {
                // No row carried a key for this relationship (or every key was NULL).
                return field.RelationIsList ? new JsonArray() : null;
            }

            object?[] parentKeys = child.CorrelationAliases
                .Select(alias => row[alias])
                .ToArray();
            if (parentKeys.Any(key => key is null))
            {
                return child.Cursor.IsList ? new JsonArray() : null;
            }

            List<JsonNode?> grouped = child.RowsByParentKeys.TryGetValue(parentKeys, out List<JsonNode?>? values)
                ? values
                : new List<JsonNode?>();

            if (!child.Cursor.IsList)
            {
                return grouped.Count > 0 ? grouped[0]?.DeepClone() : null;
            }

            return new JsonArray(grouped.Select(value => value?.DeepClone()).ToArray());
        }

        /// <summary>
        /// Collects the distinct parent key tuples for every relationship nested under the
        /// cursor's rows, in correlation-alias order, skipping rows whose key values are NULL
        /// (a NULL foreign key can never match a child row).
        /// </summary>
        private static IReadOnlyDictionary<string, IReadOnlyList<object?[]>> BuildChildKeys(
            RelationalReadCursor cursor,
            List<RelationalReadRow> rows)
        {
            Dictionary<string, IReadOnlyList<object?[]>> keysByAlias = new(StringComparer.Ordinal);
            foreach ((string joinAlias, IReadOnlyList<string> aliases) in cursor.CorrelationAliasesByJoinAlias)
            {
                List<object?[]> keys = new();
                HashSet<object?[]> seen = new(KeyTupleComparer.Instance);
                foreach (RelationalReadRow row in rows)
                {
                    object?[] key = aliases.Select(alias => row[alias]).ToArray();
                    if (key.Any(value => value is null) || !seen.Add(key))
                    {
                        continue;
                    }

                    keys.Add(key);
                }

                if (keys.Count > 0)
                {
                    keysByAlias[joinAlias] = keys;
                }
            }

            return keysByAlias;
        }

        private static async Task<List<RelationalReadRow>> ExecuteCursorAsync(
            RelationalExecutionContext context,
            RelationalReadCursor cursor)
        {
            IDictionary<string, DbConnectionParam> parameters = context.Parameters;
            if (cursor.Binds.Count > 0)
            {
                Dictionary<string, DbConnectionParam> withBinds = new(context.Parameters);
                foreach (RelationalReadBind bind in cursor.Binds)
                {
                    withBinds[bind.Name] = new DbConnectionParam(bind.Value);
                }

                parameters = withBinds;
            }

            List<RelationalReadRow>? rows = context.Connection is null
                ? await context.QueryExecutor.ExecuteQueryAsync(
                    sqltext: cursor.Sql,
                    parameters: parameters,
                    dataReaderHandler: ReadRowsAsync,
                    dataSourceName: context.DataSourceName,
                    httpContext: context.HttpContext)
                : await context.QueryExecutor.ExecuteQueryOnConnectionAsync(
                    connection: context.Connection,
                    sqltext: cursor.Sql,
                    parameters: parameters,
                    dataReaderHandler: ReadRowsAsync,
                    dataSourceName: context.DataSourceName,
                    transaction: context.Transaction,
                    httpContext: context.HttpContext);

            return rows ?? new List<RelationalReadRow>();
        }

        private static async Task<List<RelationalReadRow>> ReadRowsAsync(DbDataReader reader, List<string>? args)
        {
            List<RelationalReadRow> rows = new();
            while (await reader.ReadAsync())
            {
                Dictionary<string, object?> values = new(StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }

                rows.Add(new RelationalReadRow(values));
            }

            return rows;
        }

        /// <summary>
        /// Formats driver values the way Oracle's JSON functions do, so the assembled document
        /// is interchangeable with the database-side JSON the other engines produce.
        /// </summary>
        private static JsonNode? ConvertValue(object? value)
        {
            return value switch
            {
                null => null,
                string text => JsonValue.Create(text),
                byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
                DateTime dateTime => JsonValue.Create(FormatDateTime(dateTime)),
                DateTimeOffset dateTimeOffset => JsonValue.Create(FormatDateTimeOffset(dateTimeOffset)),
                decimal number => JsonValue.Create(number),
                double number => JsonValue.Create(number),
                float number => JsonValue.Create(number),
                int number => JsonValue.Create(number),
                long number => JsonValue.Create(number),
                short number => JsonValue.Create(number),
                byte number => JsonValue.Create(number),
                bool flag => JsonValue.Create(flag),
                char character => JsonValue.Create(character.ToString()),
                Guid guid => JsonValue.Create(guid.ToString()),
                _ => JsonValue.Create(value.ToString()),
            };
        }

        private static string FormatDateTime(DateTime value)
        {
            string baseValue = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            long fraction = value.Ticks % TimeSpan.TicksPerSecond;
            return fraction == 0
                ? baseValue
                : $"{baseValue}.{value.ToString("fffffff", CultureInfo.InvariantCulture).TrimEnd('0')}";
        }

        private static string FormatDateTimeOffset(DateTimeOffset value)
        {
            string baseValue = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            long fraction = value.Ticks % TimeSpan.TicksPerSecond;
            string fractionText = fraction == 0
                ? string.Empty
                : $".{value.ToString("fffffff", CultureInfo.InvariantCulture).TrimEnd('0')}";
            string offset = value.Offset == TimeSpan.Zero
                ? "Z"
                : value.ToString("zzz", CultureInfo.InvariantCulture);
            return $"{baseValue}{fractionText}{offset}";
        }

        /// <summary>
        /// Everything a cursor execution needs, so the recursive assembly methods do not carry
        /// eight parameters each.
        /// </summary>
        private sealed record RelationalExecutionContext(
            IRelationalReadPlanBuilder PlanBuilder,
            IQueryExecutor QueryExecutor,
            IDictionary<string, DbConnectionParam> Parameters,
            string DataSourceName,
            HttpContext? HttpContext,
            DbConnection? Connection,
            DbTransaction? Transaction);

        private sealed class RelationalReadRow
        {
            public RelationalReadRow(Dictionary<string, object?> values)
            {
                Values = values;
            }

            public Dictionary<string, object?> Values { get; }

            public object? this[string alias] => Values.TryGetValue(alias, out object? value) ? value : null;
        }

        private sealed class ChildCursorResult
        {
            public ChildCursorResult(
                RelationalReadCursor cursor,
                List<RelationalReadRow> childRows,
                List<JsonNode?> childValues)
            {
                Cursor = cursor;
                RowsByParentKeys = new Dictionary<object?[], List<JsonNode?>>(KeyTupleComparer.Instance);
                for (int i = 0; i < childRows.Count; i++)
                {
                    object?[] key = cursor.Keys.Select(k => childRows[i][k.Alias]).ToArray();
                    if (!RowsByParentKeys.TryGetValue(key, out List<JsonNode?>? values))
                    {
                        values = new List<JsonNode?>();
                        RowsByParentKeys[key] = values;
                    }

                    values.Add(childValues[i]);
                }

                // The parent row matches child rows by the value the parent projects under the
                // correlation key's parent alias.
                CorrelationAliases = cursor.Keys.Select(k => k.ParentAlias ?? string.Empty).ToArray();
            }

            public RelationalReadCursor Cursor { get; }

            public Dictionary<object?[], List<JsonNode?>> RowsByParentKeys { get; }

            public string[] CorrelationAliases { get; }
        }

        private sealed class KeyTupleComparer : IEqualityComparer<object?[]>
        {
            public static readonly KeyTupleComparer Instance = new();

            public bool Equals(object?[]? left, object?[]? right)
            {
                if (ReferenceEquals(left, right))
                {
                    return true;
                }

                if (left is null || right is null || left.Length != right.Length)
                {
                    return false;
                }

                for (int i = 0; i < left.Length; i++)
                {
                    if (!Equals(left[i], right[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(object?[] tuple)
            {
                HashCode hash = new();
                foreach (object? value in tuple)
                {
                    hash.Add(value);
                }

                return hash.ToHashCode();
            }
        }
    }
}
