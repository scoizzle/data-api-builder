// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Guards the meaning of <see cref="TestCategory.ORACLE"/>: it marks tests that need a live Oracle
    /// database and therefore run only in .pipelines/oracle-pipelines.yml. The default unit-test job
    /// filters the category out, so tagging a pure unit test with it silently drops that test from
    /// the required job, and leaving it off a live-database test turns the required job red.
    /// </summary>
    [TestClass]
    public class OracleTestCategoryContractTests
    {
        /// <summary>
        /// Every class allowed to carry TestCategory("Oracle"). Add a class here only when its tests
        /// need a live Oracle server.
        /// </summary>
        private static readonly HashSet<string> _liveOracleTypes = new(StringComparer.Ordinal)
        {
            "Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Delete.OracleGraphQLDeleteMutationPolicyTests",
            "Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.OracleGraphQLQueryPolicyTests",
            "Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Update.OracleGraphQLUpdateMutationPolicyTests",
            "Azure.DataApiBuilder.Service.Tests.Mcp.AggregateRecordsToolOracleIntegrationTests",
            "Azure.DataApiBuilder.Service.Tests.Mcp.ReadRecordsToolOracleIntegrationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleColumnPermissionIntegrationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleFindApiTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleGQLFilterTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleGQLSupportedTypesTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleGraphQLMutationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleGraphQLQueryTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleMultipleCreateTransactionIntegrationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleMultiSourceRestMutationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleNumberColumnTypesTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleOverloadExecutionTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleQuotedIdentifierTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleSessionContextIntegrationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleSynonymTests",
            "Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests.MultipleCreateMutationTests.OracleMultipleCreateMutationTests",
            "Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Delete.OracleDeleteApiTests",
            "Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert.OracleInsertApiTests",
            "Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch.OraclePatchApiTests",
            "Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Put.OraclePutApiTests",
        };

        /// <summary>
        /// Live-database classes that neither derive from SqlTestBase nor declare a [ClassInitialize]
        /// because they open their own connection inside the test body.
        /// </summary>
        private static readonly HashSet<string> _liveWithoutFixture = new(StringComparer.Ordinal)
        {
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleMultipleCreateTransactionIntegrationTests",
            "Azure.DataApiBuilder.Service.Tests.OracleTests.OracleMultiSourceRestMutationTests",
        };

        /// <summary>
        /// Floor on the number of pure-unit Oracle test methods that must keep running in the default
        /// unit-test job, so an accidental re-tag (or deletion) cannot silently shrink that coverage.
        /// </summary>
        private const int MIN_PURE_UNIT_ORACLE_TEST_METHODS = 60;

        private static IEnumerable<Type> TestClasses() =>
            typeof(TestCategory).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() is not null);

        private static bool HasOracleCategory(MemberInfo member) =>
            member.GetCustomAttributes<TestCategoryAttribute>()
                .Any(a => a.TestCategories.Contains(TestCategory.ORACLE));

        private static bool IsLiveDatabaseFixture(Type type) =>
            typeof(SqlTests.SqlTestBase).IsAssignableFrom(type) ||
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Any(m => m.GetCustomAttribute<ClassInitializeAttribute>() is not null);

        private static bool IsOracleType(Type type) =>
            type.Name.Contains("Oracle", StringComparison.Ordinal);

        [TestMethod]
        public void OracleCategory_IsOnlyUsedByLiveDatabaseClasses()
        {
            List<string> unexpected = TestClasses()
                .Where(t => HasOracleCategory(t) || t.GetMethods().Any(HasOracleCategory))
                .Select(t => t.FullName!)
                .Where(name => !_liveOracleTypes.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.AreEqual(
                0,
                unexpected.Count,
                "TestCategory(\"Oracle\") means 'needs a live Oracle server' and is excluded from the default unit-test job. " +
                "Remove it from these pure-unit classes or add them to the live-database allowlist:" +
                Environment.NewLine + string.Join(Environment.NewLine, unexpected));
        }

        [TestMethod]
        public void LiveOracleAllowlist_IsCurrent()
        {
            Assert.IsTrue(_liveOracleTypes.Count > 0);
            Assert.IsTrue(
                _liveOracleTypes.Contains("Azure.DataApiBuilder.Service.Tests.OracleTests.OracleFindApiTests"),
                "The allowlist must keep the main live REST suite.");

            Dictionary<string, Type> byName = TestClasses().ToDictionary(t => t.FullName!, StringComparer.Ordinal);
            List<string> problems = new();
            foreach (string name in _liveOracleTypes)
            {
                if (!byName.TryGetValue(name, out Type? type))
                {
                    problems.Add($"{name}: no such test class");
                }
                else if (!HasOracleCategory(type))
                {
                    problems.Add($"{name}: allowlisted but missing class-level TestCategory(\"Oracle\"); the default job would run it without a database");
                }
                else if (!IsLiveDatabaseFixture(type) && !_liveWithoutFixture.Contains(name))
                {
                    problems.Add($"{name}: allowlisted but is not a live-database class (no SqlTestBase base or [ClassInitialize])");
                }
            }

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        public void LiveOracleFixtures_AreTaggedSoTheDefaultJobExcludesThem()
        {
            List<string> untagged = TestClasses()
                .Where(t => IsOracleType(t) && IsLiveDatabaseFixture(t) && !HasOracleCategory(t))
                .Select(t => t.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.AreEqual(
                0,
                untagged.Count,
                "These Oracle classes need a live database but lack TestCategory(\"Oracle\"), so the default unit-test job would run them:" +
                Environment.NewLine + string.Join(Environment.NewLine, untagged));
        }

        [TestMethod]
        public void PureUnitOracleTests_StayInTheDefaultJob()
        {
            int count = TestClasses()
                .Where(t => IsOracleType(t) && !_liveOracleTypes.Contains(t.FullName!))
                .SelectMany(t => t.GetMethods())
                .Count(m => m.GetCustomAttributes<TestMethodAttribute>().Any());

            Assert.IsTrue(
                count >= MIN_PURE_UNIT_ORACLE_TEST_METHODS,
                $"Expected at least {MIN_PURE_UNIT_ORACLE_TEST_METHODS} pure-unit Oracle test methods in the default job, found {count}.");
        }
    }
}
