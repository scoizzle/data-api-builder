// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Represents the query used for an EXISTS clause.
    /// e.g.
    /// EXISTS (
    /// SELECT 1
    /// FROM <sourcename> AS <sourcealias>
    /// WHERE <sourcealias>.[column] = <value>
    /// )
    /// </summary>
    public class SqlExistsQueryStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Constructor for Exists query.
        /// </summary>
        /// <exception cref="DataApiBuilderException">if middleware context doesn't have an httpcontext</exception>
        public SqlExistsQueryStructure(
            HttpContext httpContext,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            List<Predicate> predicates,
            string entityName,
            IncrementingInteger? counter = null)
            : base(
                  metadataProvider,
                  authorizationResolver,
                  gQLFilterParser,
                  predicates,
                  entityName,
                  counter,
                  // The base constructor renders the read policy. SourceAlias is still empty
                  // there, so the visitor would emit an unqualified column. A second visit
                  // would also bind each policy parameter twice.
                  httpContext: null,
                  EntityActionOperation.Read)
        {
            SourceAlias = CreateTableAlias();
            if (httpContext is not null)
            {
                AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                    EntityActionOperation.Read,
                    this,
                    httpContext,
                    authorizationResolver,
                    metadataProvider);
            }
        }
    }
}
