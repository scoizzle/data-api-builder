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
    /// A create policy on an insert that supplies no column values cannot be evaluated.
    /// The insert fails closed instead of becoming a DEFAULT VALUES statement.
    /// </summary>
    [TestClass]
    public class SqlInsertStructurePolicyTests
    {
        [TestMethod]
        public void ZeroColumnInsert_WithCreatePolicy_ReturnsDatabasePolicyFailure()
        {
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => CreateInsert(createPolicy: "true"));

            Assert.AreEqual(HttpStatusCode.BadRequest, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, exception.SubStatusCode);
        }

        [TestMethod]
        public void ZeroColumnInsert_WithoutCreatePolicy_IsAllowed()
        {
            SqlInsertStructure structure = CreateInsert(createPolicy: null);

            Assert.AreEqual(0, structure.InsertColumns.Count);
        }

        private static SqlInsertStructure CreateInsert(string? createPolicy)
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
            metadata.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            string? exposedName = "id";
            metadata.Setup(x => x.TryGetExposedColumnName(entityName, "id", out exposedName)).Returns(true);

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
                new Dictionary<string, object?>(),
                httpContext);
        }
    }
}
