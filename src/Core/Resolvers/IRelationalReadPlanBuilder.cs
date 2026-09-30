// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Implemented by query builders that can render a nested read as flat relational row sets
    /// (<see cref="RelationalReadPlan"/>) instead of a single SQL JSON document. The query engine
    /// asks for a plan first and falls back to <see cref="IQueryBuilder.Build(SqlQueryStructure)"/>
    /// when the builder does not support the shape; engines without an implementation keep the
    /// database-side JSON path unchanged.
    /// </summary>
    internal interface IRelationalReadPlanBuilder
    {
        /// <summary>
        /// Builds the page cursor plus one cursor per relationship. <paramref name="pageKeysByJoinAlias"/>
        /// maps a relationship alias to one array of parent key values per parent row, in
        /// correlation-key order.
        /// </summary>
        bool TryBuildRelationalReadPlan(
            SqlQueryStructure root,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            out RelationalReadPlan? plan);

        /// <summary>Builds only the request-page cursor (the first execution phase).</summary>
        bool TryBuildRelationalPageCursor(SqlQueryStructure root, out RelationalReadCursor? pageCursor);

        /// <summary>
        /// Builds the cursors for the relationships nested under <paramref name="parentCursor"/>'s
        /// rows, restricted to the parent key values already read.
        /// </summary>
        bool TryBuildRelationalChildCursors(
            RelationalReadCursor parentCursor,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            out IReadOnlyList<RelationalReadCursor>? childCursors);
    }
}
