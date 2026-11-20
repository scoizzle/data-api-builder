// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Oracle runs a DELETE through a join view against the key-preserved base table and reports
    /// success. DAB rejects it with 400 DatabaseOperationFailed, the contract the other engines
    /// meet (DeleteApiTestBase.DeleteOneInViewBadRequestTest), unless an INSTEAD OF DELETE
    /// trigger defines the delete (#74). The catalog queries themselves need a live database.
    /// </summary>
    [TestClass]
    public class OracleViewDeleteGuardTests
    {
        [DataTestMethod]
        [DataRow(1, false, null, DisplayName = "Single-table view stays deletable")]
        [DataRow(2, false, "it reads 2 tables", DisplayName = "Join view is rejected")]
        [DataRow(3, false, "it reads 3 tables", DisplayName = "Three-table view is rejected")]
        [DataRow(2, true, null, DisplayName = "INSTEAD OF DELETE trigger keeps a join view deletable")]
        [DataRow(0, false, "its base tables could not be resolved from ALL_DEPENDENCIES", DisplayName = "Unresolved dependencies fail closed")]
        [DataRow(0, true, null, DisplayName = "INSTEAD OF DELETE trigger defines the delete even when dependencies are hidden")]
        public void ClassifyViewDelete(int baseTableCount, bool hasInsteadOfDeleteTrigger, string? expected)
        {
            Assert.AreEqual(expected, OracleMetadataProvider.ClassifyViewDelete(baseTableCount, hasInsteadOfDeleteTrigger));
        }

        [TestMethod]
        public void Build_DeleteThroughRejectedView_ThrowsBadRequest()
        {
            DatabaseView view = new("SYSTEM", "BOOKS_PUBLISHERS_VIEW_COMPOSITE");
            OracleObjectFacts.View(view).DeleteRejection = "it reads 2 tables";

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => new OracleQueryBuilder().Build(CreateDeleteStructure(view)));

            Assert.AreEqual(HttpStatusCode.BadRequest, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed, exception.SubStatusCode);
            Assert.AreEqual(
                "Cannot delete from view SYSTEM.BOOKS_PUBLISHERS_VIEW_COMPOSITE because it reads 2 tables.",
                exception.Message);
        }

        [TestMethod]
        public void Build_DeleteThroughAllowedViewAndTable_EmitsDelete()
        {
            DatabaseView view = new("SYSTEM", "BOOKS_VIEW_ALL");
            DatabaseTable table = new("SYSTEM", "BOOKS");

            Assert.AreEqual("DELETE FROM \"SYSTEM\".\"BOOKS_VIEW_ALL\" WHERE 1 = 1", new OracleQueryBuilder().Build(CreateDeleteStructure(view)));
            Assert.AreEqual("DELETE FROM \"SYSTEM\".\"BOOKS\" WHERE 1 = 1", new OracleQueryBuilder().Build(CreateDeleteStructure(table)));
        }

        private static SqlDeleteStructure CreateDeleteStructure(DatabaseObject databaseObject)
        {
            SqlDeleteStructure structure = (SqlDeleteStructure)RuntimeHelpers.GetUninitializedObject(typeof(SqlDeleteStructure));
            SetField(structure, "DatabaseObject", databaseObject);
            SetField(structure, "Predicates", new List<Predicate>());
            SetField(structure, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            return structure;
        }

        private static void SetField<T>(object target, string name, T value)
        {
            for (System.Type? type = target.GetType(); type is not null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"Could not find backing field for {name}.");
        }
    }
}
