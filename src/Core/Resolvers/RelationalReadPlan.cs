// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// A nested read rendered as flat relational row sets instead of database JSON functions:
    /// one cursor for the request page and one cursor per relationship, assembled into the JSON
    /// document in C#. This is the engine-agnostic counterpart of the single-JSON-column shape
    /// the other database engines produce in SQL (MSSQL FOR JSON, PostgreSQL/MySQL json
    /// functions); an engine opts in by implementing <see cref="IRelationalReadPlanBuilder"/>
    /// and executing the plan through its query executor.
    /// </summary>
    internal sealed record RelationalReadPlan(
        RelationalReadCursor PageCursor,
        IReadOnlyList<RelationalReadCursor> ChildCursors);

    /// <summary>
    /// One flat row set of a relational plan. All payload columns are projected with explicit
    /// aliases; correlation keys are projected as additional k0.. aliases so the executor can
    /// pass their values to the next level and the assembler can attach rows to their parents.
    /// </summary>
    internal sealed record RelationalReadCursor(
        SqlQueryStructure Structure,
        string Sql,
        /// <summary>JoinQueries alias of the relationship this cursor renders; null for the page cursor.</summary>
        string? JoinAlias,
        bool IsList,
        /// <summary>Per-parent row cap applied in SQL, including the extra row used for pagination.</summary>
        int Limit,
        /// <summary>This cursor's own correlation to its parent; empty for the page cursor.</summary>
        IReadOnlyList<RelationalReadKey> Keys,
        /// <summary>Projected JSON fields, in select order, with flattened to-one objects as children.</summary>
        IReadOnlyList<RelationalReadField> Fields,
        /// <summary>Page-key binds that restrict this cursor to the parent rows already read.</summary>
        IReadOnlyList<RelationalReadBind> Binds,
        /// <summary>
        /// For every relationship nested under this cursor (including relationships reached
        /// through flattened to-one rows), the projected aliases of its parent-side correlation
        /// columns, in correlation-key order. The executor reads those aliases from each row to
        /// build the next level's <see cref="RelationalReadBind"/> values.
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyList<string>> CorrelationAliasesByJoinAlias,
        /// <summary>Built SQL expression to projected alias, used to resolve nested correlation columns.</summary>
        IReadOnlyDictionary<string, string> AliasByExpression);

    /// <summary>
    /// One correlation column of a relationship. <see cref="Alias"/> is the column this cursor
    /// projects the child-side value under; <see cref="ParentAlias"/> is the alias of the matching
    /// parent-side value in the parent cursor (null for the page cursor).
    /// </summary>
    internal sealed record RelationalReadKey(string Alias, string? ParentAlias);

    /// <summary>
    /// A bind whose value is one parent-side key value for one parent row. The executor adds it
    /// to the child cursor's command; <see cref="SourceColumn"/> is the physical parent column
    /// so the executor can apply the column's bind type.
    /// </summary>
    internal sealed record RelationalReadBind(string Name, object? Value, string SourceColumn);

    /// <summary>
    /// One projected JSON field. Scalar fields map a projected alias to a JSON property;
    /// a flattened to-one object has no direct alias and instead exposes its column aliases
    /// as children. <see cref="NullGuardAliases"/> are the child's primary-key aliases: when
    /// every one of them is NULL the outer join found no row and the field must be JSON null.
    /// </summary>
    internal sealed class RelationalReadField
    {
        public RelationalReadField(
            string jsonName,
            string alias = "",
            bool isObject = false,
            IReadOnlyList<string>? nullGuardAliases = null,
            IReadOnlyList<RelationalReadField>? children = null)
        {
            JsonName = jsonName;
            Alias = alias;
            IsObject = isObject;
            NullGuardAliases = nullGuardAliases ?? System.Array.Empty<string>();
            Children = children ?? System.Array.Empty<RelationalReadField>();
        }

        public string JsonName { get; }

        public string Alias { get; }

        public bool IsObject { get; }

        public IReadOnlyList<string> NullGuardAliases { get; }

        public IReadOnlyList<RelationalReadField> Children { get; }
    }
}
