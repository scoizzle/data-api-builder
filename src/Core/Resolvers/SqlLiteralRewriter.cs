// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Models;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Engine-specific literal decisions. The predicate producers and the query structure call
    /// this instead of branching on a database type. It answers with an operation; it never
    /// returns SQL text.
    /// </summary>
    internal class SqlLiteralRewriter
    {
        public static readonly SqlLiteralRewriter Shared = new();

        /// <summary>
        /// True when the engine stores the given value as NULL (Oracle: the empty string).
        /// </summary>
        public virtual bool StoresEmptyStringAsNull(object? value) => false;

        /// <summary>
        /// Maps an equality comparison against a value stored as NULL to the matching null test.
        /// Equal becomes IS and NotEqual becomes IS_NOT; other operations are not rewritten.
        /// </summary>
        public virtual bool TryGetEmptyStringNullTest(
            PredicateOperation comparison,
            object? value,
            out PredicateOperation nullTestOperation)
        {
            nullTestOperation = PredicateOperation.None;
            if (!StoresEmptyStringAsNull(value))
            {
                return false;
            }

            switch (comparison)
            {
                case PredicateOperation.Equal:
                    nullTestOperation = PredicateOperation.IS;
                    return true;
                case PredicateOperation.NotEqual:
                    nullTestOperation = PredicateOperation.IS_NOT;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// OData form of the null test: a property compared to a constant on either side.
        /// </summary>
        public bool TryGetEmptyStringNullTest(
            BinaryOperatorKind operatorKind,
            SingleValueNode left,
            SingleValueNode right,
            out SingleValuePropertyAccessNode? propertyNode,
            out PredicateOperation nullTestOperation)
        {
            propertyNode = null;
            nullTestOperation = PredicateOperation.None;

            PredicateOperation comparison;
            switch (operatorKind)
            {
                case BinaryOperatorKind.Equal:
                    comparison = PredicateOperation.Equal;
                    break;
                case BinaryOperatorKind.NotEqual:
                    comparison = PredicateOperation.NotEqual;
                    break;
                default:
                    return false;
            }

            SingleValueNode unwrappedLeft = UnwrapConvertNode(left);
            SingleValueNode unwrappedRight = UnwrapConvertNode(right);

            SingleValueNode constantSide = unwrappedRight;
            SingleValuePropertyAccessNode? property = unwrappedLeft as SingleValuePropertyAccessNode;
            if (property is null)
            {
                property = unwrappedRight as SingleValuePropertyAccessNode;
                constantSide = unwrappedLeft;
            }

            if (property is null
                || constantSide is not ConstantNode constant
                || !TryGetEmptyStringNullTest(comparison, constant.Value, out nullTestOperation))
            {
                return false;
            }

            propertyNode = property;
            return true;
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
    /// Oracle stores the empty string as NULL and compares string binds to DATE/TIMESTAMP
    /// columns with the session NLS format. The first rule is answered when the predicate is
    /// generated; the second coerces a bind before it is sent.
    /// </summary>
    internal sealed class OracleSqlLiteralRewriter : SqlLiteralRewriter
    {
        public override bool StoresEmptyStringAsNull(object? value) => value is string text && text.Length == 0;

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
