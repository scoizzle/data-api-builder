// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Every <see cref="IgnoreAttribute"/> reachable from an Oracle-category test must be listed in
    /// tests/oracle-skip-baseline.json with an issue number that also appears in the ignore message.
    /// The ceiling only shrinks. A new ignore that is not in the baseline fails this test.
    /// A class-level ignore counts once per test method. A test whose body only calls
    /// <see cref="Assert.Inconclusive()"/> is a hidden skip and fails the gate: it must be an
    /// <see cref="IgnoreAttribute"/> with an issue number so the baseline counts it.
    /// </summary>
    [TestClass]
    public class OracleSkipPolicyTests
    {
        /// <summary>
        /// Ceiling after issue #27. Lower this when an ignore is removed. Do not raise it.
        /// #70 raised it from 21 to 27 once: six Assert.Inconclusive-only stubs were already
        /// skipped but invisible to the gate, and are now counted as ignores.
        /// #74 raised it to 33 once: six shared mutation scenarios that never ran on Oracle
        /// (no wrapper at all) are now present as ignores.
        /// </summary>
        private const int MaxIgnored = 33;

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
            problems.AddRange(CollectOracleInconclusiveOnlyTests().Select(
                id => $"{id}: body only calls Assert.Inconclusive; use [Ignore(\"<reason> #<issue>\")] and a baseline entry"));
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

        [TestMethod]
        public void InconclusiveOnlyBody_IsDetected()
        {
            Assert.IsTrue(IsInconclusiveOnly(Sample(nameof(InconclusiveSamples.SyncStub))));
            Assert.IsTrue(IsInconclusiveOnly(Sample(nameof(InconclusiveSamples.AsyncStub))));
        }

        [TestMethod]
        public void TestThatAssertsBehaviour_IsNotInconclusiveOnly()
        {
            Assert.IsFalse(IsInconclusiveOnly(Sample(nameof(InconclusiveSamples.GuardedAsync))));
            Assert.IsFalse(IsInconclusiveOnly(Sample(nameof(InconclusiveSamples.NoInconclusive))));
        }

        private static MethodInfo Sample(string name) =>
            typeof(InconclusiveSamples).GetMethod(name)!;

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
            foreach (Type type in OracleTestClasses())
            {
                IgnoreAttribute classIgnore = type.GetCustomAttribute<IgnoreAttribute>();
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    IgnoreAttribute ignore = method.GetCustomAttribute<IgnoreAttribute>();
                    if (ignore is null && classIgnore is not null && IsTestMethod(method))
                    {
                        ignore = classIgnore;
                    }

                    if (ignore is null)
                    {
                        continue;
                    }

                    ignores[$"{type.FullName}.{method.Name}"] = ignore.IgnoreMessage ?? string.Empty;
                }
            }

            return ignores;
        }

        /// <summary>
        /// Test methods in Oracle classes that are not ignored but whose body calls nothing except
        /// <see cref="Assert.Inconclusive()"/> (and compiler-generated async plumbing).
        /// </summary>
        private static List<string> CollectOracleInconclusiveOnlyTests()
        {
            List<string> stubs = new();
            foreach (Type type in OracleTestClasses())
            {
                if (type.GetCustomAttribute<IgnoreAttribute>() is not null)
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (IsTestMethod(method)
                        && method.GetCustomAttribute<IgnoreAttribute>() is null
                        && IsInconclusiveOnly(method))
                    {
                        stubs.Add($"{type.FullName}.{method.Name}");
                    }
                }
            }

            stubs.Sort(StringComparer.Ordinal);
            return stubs;
        }

        private static IEnumerable<Type> OracleTestClasses() =>
            typeof(TestCategory).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<TestClassAttribute>() is not null)
                .Where(type => HasOracleCategory(type) || type.GetMethods().Any(HasOracleCategory));

        private static bool IsTestMethod(MethodInfo method) =>
            method.GetCustomAttribute<TestMethodAttribute>() is not null;

        /// <summary>
        /// True when the method's IL (or its async state machine's MoveNext) calls
        /// <see cref="Assert.Inconclusive()"/> and nothing else apart from compiler-generated
        /// async plumbing and string building for the message.
        /// </summary>
        internal static bool IsInconclusiveOnly(MethodInfo method)
        {
            MethodInfo body = method;
            AsyncStateMachineAttribute asyncStateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
            if (asyncStateMachine is not null)
            {
                body = asyncStateMachine.StateMachineType.GetMethod(
                    nameof(IAsyncStateMachine.MoveNext),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            }

            bool inconclusive = false;
            foreach (MethodBase callee in CalledMethods(body))
            {
                if (callee is null)
                {
                    return false;
                }

                if (callee.DeclaringType == typeof(Assert) && callee.Name == nameof(Assert.Inconclusive))
                {
                    inconclusive = true;
                }
                else if (callee.DeclaringType?.Namespace != typeof(AsyncTaskMethodBuilder).Namespace
                    && callee.DeclaringType != typeof(string))
                {
                    return false;
                }
            }

            return inconclusive;
        }

        private static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

        /// <summary>
        /// Methods referenced by call, callvirt and newobj in the method body. A token that does
        /// not resolve is returned as null so the caller treats it as a real call.
        /// </summary>
        private static List<MethodBase> CalledMethods(MethodInfo method)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
            Type[] typeArguments = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
            Type[] methodArguments = method.GetGenericArguments();
            List<MethodBase> calls = new();
            int offset = 0;
            while (offset < il.Length)
            {
                short value = il[offset] == 0xFE ? unchecked((short)(0xFE00 | il[offset + 1])) : il[offset];
                offset += il[offset] == 0xFE ? 2 : 1;
                OpCode opCode = _opCodes[value];
                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    MethodBase callee;
                    try
                    {
                        callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset), typeArguments, methodArguments);
                    }
                    catch (ArgumentException)
                    {
                        callee = null;
                    }

                    calls.Add(callee);
                }

                offset += opCode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
                    _ => 4
                };
            }

            return calls;
        }

        /// <summary>
        /// Shapes for <see cref="IsInconclusiveOnly"/>. Not a test class, so these never run.
        /// </summary>
        public static class InconclusiveSamples
        {
            public static void SyncStub()
            {
                Assert.Inconclusive("Oracle test config does not map this entity.");
            }

            public static async Task AsyncStub()
            {
                Assert.Inconclusive("Oracle test config does not map this entity.");
            }

            public static async Task GuardedAsync(bool live)
            {
                if (!live)
                {
                    Assert.Inconclusive("Live Oracle is not available.");
                }

                await Task.Yield();
                Assert.IsTrue(live);
            }

            public static void NoInconclusive()
            {
                Assert.AreEqual(2, 1 + 1);
            }
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
