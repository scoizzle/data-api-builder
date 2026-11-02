// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Every <see cref="IgnoreAttribute"/> reachable from an Oracle-category test must be listed in
    /// tests/oracle-skip-baseline.json with an issue number that also appears in the ignore message.
    /// The ceiling only shrinks. A new ignore that is not in the baseline fails this test.
    /// </summary>
    [TestClass]
    public class OracleSkipPolicyTests
    {
        /// <summary>
        /// Ceiling after issue #27. Lower this when an ignore is removed. Do not raise it.
        /// </summary>
        private const int MaxIgnored = 21;

        [TestMethod]
        public void OracleIgnores_MatchTheBaseline()
        {
            string path = FindRepoFile(Path.Combine("tests", "oracle-skip-baseline.json"));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            int maxIgnored = root.GetProperty("maxIgnored").GetInt32();
            Dictionary<string, int> baseline = new(StringComparer.Ordinal);
            foreach (JsonProperty entry in root.GetProperty("ignores").EnumerateObject())
            {
                baseline[entry.Name] = entry.Value.GetProperty("issue").GetInt32();
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Value.GetProperty("owner").GetString()));
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Value.GetProperty("reason").GetString()));
            }

            Dictionary<string, string> actual = CollectOracleIgnores();
            List<string> problems = Compare(actual, baseline, maxIgnored);
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
            Assert.IsTrue(maxIgnored <= MaxIgnored, $"oracle-skip-baseline maxIgnored only shrinks. Ceiling is {MaxIgnored}, file says {maxIgnored}.");
            Assert.AreEqual(maxIgnored, actual.Count);
        }

        [TestMethod]
        public void NewIgnoreWithoutBaseline_IsReported()
        {
            Dictionary<string, string> actual = new(StringComparer.Ordinal)
            {
                ["Sample.Existing"] = "kept for a documented gap. #27"
            };
            Dictionary<string, int> baseline = new(StringComparer.Ordinal)
            {
                ["Sample.Existing"] = 27
            };

            actual["Sample.BrandNew"] = "someone added a blanket ignore";
            List<string> problems = Compare(actual, baseline, maxIgnored: 1);

            Assert.IsTrue(problems.Any(problem => problem.Contains("Sample.BrandNew", StringComparison.Ordinal)));
            Assert.IsTrue(problems.Any(problem => problem.Contains("missing issue number", StringComparison.Ordinal)));
        }

        private static List<string> Compare(Dictionary<string, string> actual, Dictionary<string, int> baseline, int maxIgnored)
        {
            List<string> problems = new();
            foreach ((string id, string message) in actual.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!baseline.TryGetValue(id, out int issue))
                {
                    problems.Add($"{id}: ignore is not in tests/oracle-skip-baseline.json");
                    if (!ContainsIssueNumber(message))
                    {
                        problems.Add($"{id}: ignore message is missing issue number");
                    }

                    continue;
                }

                if (!message.Contains("#" + issue, StringComparison.Ordinal))
                {
                    problems.Add($"{id}: ignore message is missing issue number #{issue}");
                }
            }

            foreach (string id in baseline.Keys.Except(actual.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                problems.Add($"{id}: baseline entry has no matching [Ignore]");
            }

            if (actual.Count > maxIgnored)
            {
                problems.Add($"Ignored {actual.Count} Oracle tests, baseline allows {maxIgnored}.");
            }

            return problems;
        }

        private static bool ContainsIssueNumber(string message)
        {
            for (int index = 0; index < message.Length - 1; index++)
            {
                if (message[index] == '#' && char.IsDigit(message[index + 1]))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, string> CollectOracleIgnores()
        {
            Dictionary<string, string> ignores = new(StringComparer.Ordinal);
            foreach (Type type in typeof(TestCategory).Assembly.GetTypes().Where(type => type.GetCustomAttribute<TestClassAttribute>() is not null))
            {
                bool oracleType = HasOracleCategory(type) || type.GetMethods().Any(HasOracleCategory);
                if (!oracleType)
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    IgnoreAttribute ignore = method.GetCustomAttribute<IgnoreAttribute>();
                    if (ignore is null)
                    {
                        continue;
                    }

                    ignores[$"{type.FullName}.{method.Name}"] = ignore.IgnoreMessage ?? string.Empty;
                }
            }

            return ignores;
        }

        private static bool HasOracleCategory(MemberInfo member) =>
            member.GetCustomAttributes<TestCategoryAttribute>().Any(attribute => attribute.TestCategories.Contains(TestCategory.ORACLE));

        private static string FindRepoFile(string relativePath)
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail($"Could not find {relativePath} above {AppContext.BaseDirectory}.");
            return relativePath;
        }
    }
}
