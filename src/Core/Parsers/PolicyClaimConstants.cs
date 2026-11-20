// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Remembers which <see cref="ConstantNode"/>s in a database policy hold a claim value rather
    /// than a literal the policy author wrote. <see cref="ConstantNode"/> is sealed, so the mark is
    /// carried beside the node (as <see cref="Resolvers.OracleStringBindFacts"/> does for parameters)
    /// and lives only as long as the node does.
    /// </summary>
    internal static class PolicyClaimConstants
    {
        private static readonly ConditionalWeakTable<ConstantNode, object> _claimConstants = new();

        private static readonly object _mark = new();

        /// <summary>
        /// Creates the constant that stands for a claim value in a policy expression.
        /// </summary>
        public static ConstantNode Create(object? claimValue)
        {
            ConstantNode node = new(claimValue);
            _claimConstants.AddOrUpdate(node, _mark);
            return node;
        }

        /// <summary>
        /// Whether <paramref name="node"/> holds a claim value.
        /// </summary>
        public static bool IsClaim(ConstantNode node)
        {
            return _claimConstants.TryGetValue(node, out _);
        }

        /// <summary>
        /// Keeps the claim mark when a claim constant is replaced by a converted copy.
        /// </summary>
        public static SingleValueNode CopyMark(ConstantNode source, SingleValueNode replacement)
        {
            if (replacement is ConstantNode replacementConstant
                && !ReferenceEquals(source, replacementConstant)
                && IsClaim(source))
            {
                _claimConstants.AddOrUpdate(replacementConstant, _mark);
            }

            return replacement;
        }
    }
}
