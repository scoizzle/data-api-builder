// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class OracleTriggerIdentityTests
    {
        [TestMethod]
        public void GuardedNullCheckIsGeneratedByDefault()
        {
            (string name, bool isReadOnly) = One(null, null, """
                BEGIN
                    IF :new.id IS NULL THEN
                        :new.id := books_seq.NEXTVAL;
                    END IF;
                END;
                """);

            Assert.AreEqual("id", name);
            Assert.IsFalse(isReadOnly);
        }

        [TestMethod]
        public void UnguardedAssignmentIsReadOnly()
        {
            (string _, bool isReadOnly) = One(null, null, ":new.id := books_seq.NEXTVAL;");
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void QuotedSelectIntoIsReadOnly()
        {
            (string name, bool isReadOnly) = One(
                "BEFORE EACH ROW",
                "INSERT",
                "SELECT books_seq.NEXTVAL INTO :new.\"ID\" FROM dual;");

            Assert.AreEqual("ID", name);
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void QuotedMixedCaseAssignmentKeepsSpelling()
        {
            (string name, bool isReadOnly) = One(null, null, ":new.\"Id\" := books_seq.NEXTVAL;");

            Assert.AreEqual("Id", name);
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void QuotedNewAssignmentInsideGuardIsWritable()
        {
            (string name, bool isReadOnly) = One(null, null, """
                IF :NEW."ID" IS NULL THEN
                    :NEW."ID" := books_seq.NEXTVAL;
                END IF;
                """);

            Assert.AreEqual("ID", name);
            Assert.IsFalse(isReadOnly);
        }

        [TestMethod]
        public void AssignmentAfterTheGuardIsReadOnly()
        {
            (string _, bool isReadOnly) = One(null, null, """
                IF :new.id IS NULL THEN
                    NULL;
                END IF;
                :new.id := books_seq.NEXTVAL;
                """);
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void NestedGuardDoesNotCoverAnAssignmentOutsideIt()
        {
            (string _, bool isReadOnly) = One(null, null, """
                IF :new.id IS NULL THEN
                    IF :new.title IS NULL THEN
                        NULL;
                    END IF;
                END IF;
                :new.id := books_seq.NEXTVAL;
                """);
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void DifferentColumnInsideTheGuardIsReadOnly()
        {
            (string name, bool isReadOnly) = One(null, null, """
                IF :new.id IS NULL THEN
                    :new.title := title_seq.NEXTVAL;
                END IF;
                """);

            Assert.AreEqual("title", name);
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void InsertingAndUpdatingGuardsAreWritable()
        {
            (string _, bool inserting) = One(null, null, """
                IF INSERTING AND :new.id IS NULL THEN
                    :new.id := books_seq.NEXTVAL;
                END IF;
                """);
            (string _, bool updating) = One(null, null, """
                IF UPDATING AND :new.id IS NULL THEN
                    :new.id := books_seq.NEXTVAL;
                END IF;
                """);
            (string _, bool parenthesized) = One(null, null, """
                IF ((INSERTING AND :new.id IS NULL)) THEN
                    :new.id := books_seq.NEXTVAL;
                END IF;
                """);

            Assert.IsFalse(inserting);
            Assert.IsFalse(updating);
            Assert.IsFalse(parenthesized);
        }

        [TestMethod]
        public void OrConditionIsNotAGuard()
        {
            (string _, bool isReadOnly) = One(null, null, """
                IF INSERTING OR :new.id IS NULL THEN
                    :new.id := books_seq.NEXTVAL;
                END IF;
                """);
            Assert.IsTrue(isReadOnly);
        }

        [TestMethod]
        public void ElseAndElsifClearThePreviousGuard()
        {
            (string _, bool elseReadOnly) = One(null, null, """
                IF :new.id IS NULL THEN
                    NULL;
                ELSE
                    :new.id := books_seq.NEXTVAL;
                END IF;
                """);
            List<(string Name, bool IsReadOnly)> elsif = OracleMetadataProvider.ClassifyTrigger(
                null,
                null,
                """
                IF :new.id IS NULL THEN
                    :new.id := books_seq.NEXTVAL;
                ELSIF :new.id = 0 THEN
                    :new.id := other_seq.NEXTVAL;
                END IF;
                """).ToList();

            Assert.IsTrue(elseReadOnly);
            Assert.AreEqual(1, elsif.Count);
            Assert.IsTrue(elsif[0].IsReadOnly, "the ELSIF assignment is not under the IS NULL guard");
        }

        [TestMethod]
        public void CommentedAndLiteralAssignmentsAreIgnored()
        {
            IReadOnlyList<string> commented = OracleMetadataProvider.FindTriggerAssignedColumnNames("""
                -- :new.id := books_seq.NEXTVAL;
                /* :new.title := title_seq.NEXTVAL; */
                """);
            IReadOnlyList<string> literal = OracleMetadataProvider.FindTriggerAssignedColumnNames("""
                :new.note := ':new.id := books_seq.NEXTVAL';
                :new.title := title_seq.NEXTVAL;
                """);

            Assert.AreEqual(0, commented.Count);
            CollectionAssert.AreEqual(new[] { "title" }, literal.ToArray());
        }

        [TestMethod]
        public void AfterStatementAndCompoundTriggersAreSkipped()
        {
            string body = ":new.id := books_seq.NEXTVAL;";
            Assert.AreEqual(0, OracleMetadataProvider.ClassifyTrigger("AFTER EACH ROW", "INSERT", body).Count);
            Assert.AreEqual(0, OracleMetadataProvider.ClassifyTrigger("BEFORE STATEMENT", "INSERT", body).Count);
            Assert.AreEqual(0, OracleMetadataProvider.ClassifyTrigger("COMPOUND", "INSERT", body).Count);
            Assert.AreEqual(0, OracleMetadataProvider.ClassifyTrigger("BEFORE EACH ROW", "INSERT", """
                COMPOUND TRIGGER
                BEFORE EACH ROW IS BEGIN
                    :new.id := books_seq.NEXTVAL;
                END BEFORE EACH ROW;
                END;
                """).Count);
        }

        [TestMethod]
        public void UpdateOrInsertIsIncludedAndDeleteIsSkipped()
        {
            string body = ":new.id := books_seq.NEXTVAL;";
            Assert.AreEqual(1, OracleMetadataProvider.ClassifyTrigger("BEFORE EACH ROW", "UPDATE OR INSERT", body).Count);
            Assert.AreEqual(0, OracleMetadataProvider.ClassifyTrigger("BEFORE EACH ROW", "DELETE", body).Count);
            Assert.AreEqual(0, OracleMetadataProvider.ClassifyTrigger("BEFORE EACH ROW", "INSERT", null).Count);
        }

        [TestMethod]
        public void UnguardedWinsWhenTheSameColumnIsAssignedBothWays()
        {
            Dictionary<string, bool> merged = new(System.StringComparer.OrdinalIgnoreCase);
            OracleMetadataProvider.MergeTriggerIdentity(merged, [("id", false)]);
            OracleMetadataProvider.MergeTriggerIdentity(merged, [("ID", true)]);

            Assert.IsTrue(merged["id"]);
        }

        /// <summary>
        /// #58: Oracle stores a header guard <c>WHEN (new.id IS NULL)</c> in ALL_TRIGGERS.WHEN_CLAUSE,
        /// not in TRIGGER_BODY, so the body alone looks like an unguarded assignment.
        /// </summary>
        [DataTestMethod]
        [DataRow("new.id IS NULL", DisplayName = "stored form without parentheses")]
        [DataRow("(new.id IS NULL)", DisplayName = "parenthesized")]
        [DataRow("NEW.ID is null", DisplayName = "case-insensitive")]
        [DataRow(":new.id IS NULL", DisplayName = "colon form")]
        public void WhenClauseNullGuardIsGeneratedByDefault(string whenClause)
        {
            IReadOnlyList<(string Name, bool IsReadOnly)> columns = OracleMetadataProvider.ClassifyTrigger(
                "BEFORE EACH ROW",
                "INSERT",
                "BEGIN :new.id := emp_seq.NEXTVAL; END;",
                whenClause);

            Assert.AreEqual(1, columns.Count);
            Assert.AreEqual("id", columns[0].Name);
            Assert.IsFalse(columns[0].IsReadOnly);
        }

        [TestMethod]
        public void QuotedWhenClauseGuardIsGeneratedByDefault()
        {
            (string name, bool isReadOnly) = OracleMetadataProvider.ClassifyTrigger(
                "BEFORE EACH ROW",
                "INSERT",
                ":new.\"Id\" := emp_seq.NEXTVAL;",
                "new.\"Id\" IS NULL").Single();

            Assert.AreEqual("Id", name);
            Assert.IsFalse(isReadOnly);
        }

        [DataTestMethod]
        [DataRow(null, DisplayName = "no WHEN clause")]
        [DataRow("new.other IS NULL", DisplayName = "guard on another column")]
        [DataRow("new.id IS NULL OR new.flag = 1", DisplayName = "OR condition")]
        [DataRow("new.id = 0", DisplayName = "not a null test")]
        [DataRow("new.id IS NOT NULL", DisplayName = "IS NOT NULL")]
        public void WhenClauseThatIsNotANullGuardForTheColumnStaysReadOnly(string? whenClause)
        {
            IReadOnlyList<(string Name, bool IsReadOnly)> columns = OracleMetadataProvider.ClassifyTrigger(
                "BEFORE EACH ROW",
                "INSERT",
                ":new.id := emp_seq.NEXTVAL;",
                whenClause);

            Assert.AreEqual(1, columns.Count);
            Assert.IsTrue(columns[0].IsReadOnly);
        }

        /// <summary>
        /// #57: ALL_TRIGGERS.OWNER is the trigger's owner; the table's schema is TABLE_OWNER.
        /// #58: the query must also read WHEN_CLAUSE.
        /// </summary>
        [TestMethod]
        public void TriggerQueryFiltersOnTableOwnerAndReadsWhenClause()
        {
            string query = OracleMetadataProvider.TRIGGER_IDENTITY_QUERY;

            StringAssert.Contains(query, "WHERE TABLE_OWNER = :owner AND TABLE_NAME = :table_name AND BASE_OBJECT_TYPE = 'TABLE'");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(query, @"(?<!TABLE_)OWNER\s*="), query);
            StringAssert.Contains(query, "SELECT TRIGGER_NAME, TRIGGERING_EVENT, TRIGGER_TYPE, TRIGGER_BODY, WHEN_CLAUSE FROM ALL_TRIGGERS");
        }

        private static (string Name, bool IsReadOnly) One(string? triggerType, string? triggeringEvent, string body)
        {
            IReadOnlyList<(string Name, bool IsReadOnly)> columns =
                OracleMetadataProvider.ClassifyTrigger(triggerType, triggeringEvent, body);
            Assert.AreEqual(1, columns.Count, string.Join(",", columns.Select(column => column.Name)));
            return columns[0];
        }
    }
}
