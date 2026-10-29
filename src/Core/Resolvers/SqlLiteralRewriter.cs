// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Engine-specific literal adjustments. The shared visitor and query structure call this
    /// instead of branching on a database type.
    /// </summary>
    internal class SqlLiteralRewriter
    {
        public static readonly SqlLiteralRewriter Shared = new();

        /// <summary>
        /// Rewrites a property-vs-empty-string comparison when the engine stores '' as NULL.
        /// </summary>
        public virtual bool TryRewriteEmptyStringComparison(
            BinaryOperatorKind operatorKind,
            SingleValueNode left,
            SingleValueNode right,
            out SingleValuePropertyAccessNode? propertyNode,
            out bool isNullPredicate)
        {
            propertyNode = null;
            isNullPredicate = false;
            return false;
        }

        /// <summary>
        /// Adjusts a bound value before it is sent to the driver. The default returns the value unchanged.
        /// </summary>
        public virtual object? CoerceParameterValue(
            object? value,
            string? paramName,
            Type? columnSystemType,
            Func<string, string, Type, object?> parse)
        {
            return value;
        }

        protected static SingleValueNode UnwrapConvertNode(SingleValueNode node)
        {
            return node is ConvertNode convert ? convert.Source : node;
        }

        protected static bool IsDateTimeSystemType(Type systemType)
        {
            Type underlyingType = Nullable.GetUnderlyingType(systemType) ?? systemType;
            return underlyingType == typeof(DateTime)
                || underlyingType == typeof(DateTimeOffset)
                || underlyingType == typeof(DateOnly);
        }
    }

    /// <summary>
    /// Oracle stores the empty string as NULL, and compares string binds to DATE/TIMESTAMP
    /// columns with the session NLS format. Rewrite both before the SQL is sent.
    /// </summary>
    internal sealed class OracleSqlLiteralRewriter : SqlLiteralRewriter
    {
        public override bool TryRewriteEmptyStringComparison(
            BinaryOperatorKind operatorKind,
            SingleValueNode left,
            SingleValueNode right,
            out SingleValuePropertyAccessNode? propertyNode,
            out bool isNullPredicate)
        {
            propertyNode = null;
            isNullPredicate = false;

            if (operatorKind is not (BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual))
            {
                return false;
            }

            SingleValueNode unwrappedLeft = UnwrapConvertNode(left);
            SingleValueNode unwrappedRight = UnwrapConvertNode(right);

            SingleValueNode? constantSide = unwrappedRight;
            SingleValuePropertyAccessNode? property = unwrappedLeft as SingleValuePropertyAccessNode;
            if (property is null)
            {
                property = unwrappedRight as SingleValuePropertyAccessNode;
                constantSide = unwrappedLeft;
            }

            if (property is null
                || constantSide is not ConstantNode constant
                || constant.Value is not string constantString
                || constantString.Length != 0)
            {
                return false;
            }

            propertyNode = property;
            isNullPredicate = operatorKind is BinaryOperatorKind.Equal;
            return true;
        }

        public override object? CoerceParameterValue(
            object? value,
            string? paramName,
            Type? columnSystemType,
            Func<string, string, Type, object?> parse)
        {
            if (!string.IsNullOrEmpty(paramName)
                && value is string text
                && columnSystemType is not null
                && IsDateTimeSystemType(columnSystemType))
            {
                return parse(text, paramName, columnSystemType);
            }

            return value;
        }
    }

    /// <summary>
    /// Exposes the literal rewriter without naming an engine on the public metadata interface.
    /// </summary>
    internal interface ISqlLiteralSource
    {
        SqlLiteralRewriter LiteralRewriter { get; }
    }
}
