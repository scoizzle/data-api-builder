// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Oracle rejects a create policy on an insert that supplies no column values.
    /// Other engines still build that insert.
    /// </summary>
    [TestClass]
    public class SqlInsertStructurePolicyTests
    {
        /// <summary>
        /// Oracle cannot build this insert. Other engines still accept it, so the shared check
        /// stays on Oracle. A column-free create policy there is 403 DatabasePolicyFailure.
        /// </summary>
        [DataTestMethod]
        [DataRow(DatabaseType.Oracle)]
        public void ZeroColumnInsert_WithColumnFreeCreatePolicy_ReturnsForbidden(DatabaseType databaseType)
        {
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => CreateInsert(createPolicy: "true", databaseType: databaseType));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, exception.SubStatusCode);
            StringAssert.Contains(exception.Message, "An insert with no column values cannot be used when a database policy is defined");
        }

        /// <summary>
        /// A policy that references a column fails the earlier "fields referenced by the policy" check
        /// (403 AuthorizationCheckFailed). Pins the check order so the two substatuses stay distinct.
        /// The message assertion proves the intended check fired rather than the policy-processing catch.
        /// </summary>
        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL)]
        [DataRow(DatabaseType.Oracle)]
        public void ZeroColumnInsert_WithFieldCreatePolicy_ReturnsForbiddenAuthorizationCheckFailed(DatabaseType databaseType)
        {
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => CreateInsert(createPolicy: "id gt 0", databaseType: databaseType));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed, exception.SubStatusCode);
            StringAssert.Contains(exception.Message, "One or more fields referenced by the database policy are not present in the request body.");
        }

        /// <summary>
        /// Negative control: supplying a column with a field-referencing create policy still builds.
        /// </summary>
        [TestMethod]
        public void NonEmptyInsert_WithFieldCreatePolicy_IsAllowed()
        {
            SqlInsertStructure structure = CreateInsert(
                createPolicy: "id gt 0",
                mutationParams: new Dictionary<string, object?> { { "id", 1 } });

            Assert.AreEqual(1, structure.InsertColumns.Count);
        }

        [TestMethod]
        public void ZeroColumnInsert_WithoutCreatePolicy_IsAllowed()
        {
            SqlInsertStructure structure = CreateInsert(createPolicy: null);

            Assert.AreEqual(0, structure.InsertColumns.Count);
        }

        /// <summary>
        /// Defense in depth: if the structure check is bypassed, the Oracle builder must not emit an
        /// insert (or an invalid "SELECT FROM DUAL" policy probe) for a create policy without columns.
        /// The state is only reachable by assigning the predicate directly.
        /// </summary>
        [TestMethod]
        public void OracleBuilder_ZeroColumnInsertWithCreatePolicy_Throws()
        {
            SqlInsertStructure structure = CreateInsert(createPolicy: null, databaseType: DatabaseType.Oracle);
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Create] = "\"ID\" > 0";

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => new OracleQueryBuilder().Build(structure));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        private static SqlInsertStructure CreateInsert(
            string? createPolicy,
            DatabaseType databaseType = DatabaseType.MSSQL,
            Dictionary<string, object?>? mutationParams = null)
        {
            const string entityName = "Book";
            SourceDefinition source = new();
            source.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });

            DatabaseTable table = new("dbo", "books")
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = source
            };
            Dictionary<string, DatabaseObject> entities = new() { [entityName] = table };

            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>());
            metadata.Setup(x => x.GetEntityNamesAndDbObjects()).Returns(entities);
            metadata.Setup(x => x.EntityToDatabaseObject).Returns(entities);
            metadata.Setup(x => x.GetSourceDefinition(entityName)).Returns(source);
            metadata.Setup(x => x.GetDatabaseType()).Returns(databaseType);

            string? exposedName = "id";
            metadata.Setup(x => x.TryGetExposedColumnName(entityName, "id", out exposedName)).Returns(true);

            // The policy visitor resolves backing columns and quotes them while building the predicate.
            string? backingColumn = "id";
            metadata.Setup(x => x.TryGetBackingColumn(entityName, "id", out backingColumn)).Returns(true);
            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.QuoteIdentifier(It.IsAny<string>())).Returns<string>(name => $"\"{name}\"");
            metadata.Setup(x => x.GetQueryBuilder()).Returns(queryBuilder.Object);

            ODataParser parser = new();
            parser.BuildModel(metadata.Object);
            metadata.Setup(x => x.GetODataParser()).Returns(parser);

            Mock<IAuthorizationResolver> authorization = new();
            ResolvedDatabasePolicy policy = createPolicy is null
                ? ResolvedDatabasePolicy.Empty
                : new ResolvedDatabasePolicy(createPolicy, new Dictionary<string, object?>());
            authorization.Setup(x => x.ResolveDBPolicy(
                    entityName,
                    It.IsAny<string>(),
                    EntityActionOperation.Create,
                    It.IsAny<HttpContext>()))
                .Returns(policy);

            RuntimeConfigProvider runtimeConfigProvider =
                TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            GQLFilterParser filterParser = new(runtimeConfigProvider, new Mock<IMetadataProviderFactory>().Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "anonymous";

            return new SqlInsertStructure(
                entityName,
                metadata.Object,
                authorization.Object,
                filterParser,
                mutationParams ?? new Dictionary<string, object?>(),
                httpContext);
        }
    }
}
