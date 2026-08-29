// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests.MultipleCreateMutationTests
{
    /// <summary>
    /// GraphQL multiple-create mutation tests against Oracle.
    /// Identity sequences in DatabaseSchema-Oracle.sql start at 5001 to match MSSQL fixtures.
    /// Nested dbQuery uses JSON_OBJECT / JSON_ARRAYAGG / LATERAL (not FOR JSON PATH / OUTER APPLY).
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleMultipleCreateMutationTests : MultipleCreateMutationTestBase
    {
        #region Test Fixture Setup

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToOneRelationship()
        {
            string dbQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title,
                    'publisher_id' VALUE table0.publisher_id,
                    'publishers' VALUE table1_subq.data
                )
                FROM books table0
                LEFT OUTER JOIN LATERAL (
                    SELECT JSON_OBJECT(
                        'id' VALUE table1.id,
                        'name' VALUE table1.name
                    ) AS data
                    FROM publishers table1
                    WHERE table0.publisher_id = table1.id
                    ORDER BY table1.id ASC
                    FETCH FIRST 1 ROWS ONLY
                ) table1_subq ON 1 = 1
                WHERE table0.id = 5001 AND table0.title = 'Book #1'
                ORDER BY table0.id ASC
                FETCH FIRST 1 ROWS ONLY";

            await MultipleCreateMutationWithManyToOneRelationship(dbQuery);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToManyRelationship()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 1234,
                                          ""reviews"": {
                                            ""items"": [
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Book #1 - Review #1""
                                              },
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5002,
                                                ""content"": ""Book #1 - Review #2""
                                              }
                                            ]
                                          }
                                        }";

            await MultipleCreateMutationWithOneToManyRelationship(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToManyRelationship()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 1234,
                                          ""authors"": {
                                            ""items"": [
                                              {
                                                ""id"": 5001,
                                                ""name"": ""Author #1"",
                                                ""birthdate"": ""2000-01-01""
                                              },
                                              {
                                                ""id"": 5002,
                                                ""name"": ""Author #2"",
                                                ""birthdate"": ""2000-02-03""
                                              }
                                            ]
                                          }
                                        }";

            string linkingTableDbValidationQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT(
                        'book_id' VALUE book_id,
                        'author_id' VALUE author_id,
                        'royalty_percentage' VALUE royalty_percentage
                    )
                    ORDER BY book_id, author_id ASC
                    RETURNING CLOB
                )
                FROM book_author_link
                WHERE book_id = 5001 AND (author_id = 5001 OR author_id = 5002)";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0}]";

            await MultipleCreateMutationWithManyToManyRelationship(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToOneRelationship()
        {
            string expectedResponse = @" {
                                            ""categoryid"": 101,
                                            ""pieceid"": 101,
                                            ""categoryName"": ""SciFi"",
                                            ""piecesAvailable"": 100,
                                            ""piecesRequired"": 50,
                                            ""stocks_price"": {
                                                ""categoryid"": 101,
                                                ""pieceid"": 101,
                                                ""instant"": ""2024-04-02"",
                                                ""price"": 75,
                                                ""is_wholesale_price"": true
                                            }
                                        }";

            await MultipleCreateMutationWithOneToOneRelationship(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithAllRelationshipTypes()
        {
            string expectedResponse = @"{
                                            ""id"": 5001,
                                            ""title"": ""Book #1"",
                                            ""publishers"": {
                                            ""id"": 5001,
                                            ""name"": ""Publisher #1""
                                            },
                                            ""reviews"": {
                                            ""items"": [
                                                {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Book #1 - Review #1"",
                                                ""website_users"": {
                                                    ""id"": 5001,
                                                    ""username"": ""WebsiteUser #1""
                                                }
                                                },
                                                {
                                                ""book_id"": 5001,
                                                ""id"": 5002,
                                                ""content"": ""Book #1 - Review #2"",
                                                ""website_users"": {
                                                    ""id"": 1,
                                                    ""username"": ""George""
                                                }
                                                }
                                            ]
                                            },
                                            ""authors"": {
                                            ""items"": [
                                                {
                                                ""id"": 5001,
                                                ""name"": ""Author #1"",
                                                ""birthdate"": ""2000-02-01""
                                                },
                                                {
                                                ""id"": 5002,
                                                ""name"": ""Author #2"",
                                                ""birthdate"": ""2000-01-02""
                                                }
                                            ]
                                        }
                                    }";

            string linkingTableDbValidationQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT(
                        'book_id' VALUE book_id,
                        'author_id' VALUE author_id,
                        'royalty_percentage' VALUE royalty_percentage
                    )
                    ORDER BY book_id, author_id ASC
                    RETURNING CLOB
                )
                FROM book_author_link
                WHERE book_id = 5001 AND (author_id = 5001 OR author_id = 5002)";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0}]";

            await MultipleCreateMutationWithAllRelationshipTypes(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task ManyTypeMultipleCreateMutationOperation()
        {
            string expectedResponse = @"{
                                          ""items"": [
                                            {
                                              ""id"": 5001,
                                              ""title"": ""Book #1"",
                                              ""publisher_id"": 5001,
                                              ""publishers"": {
                                                ""id"": 5001,
                                                ""name"": ""Publisher #1""
                                              },
                                              ""reviews"": {
                                                ""items"": [
                                                  {
                                                    ""book_id"": 5001,
                                                    ""id"": 5001,
                                                    ""content"": ""Book #1 - Review #1"",
                                                    ""website_users"": {
                                                      ""id"": 5001,
                                                      ""username"": ""Website user #1""
                                                    }
                                                  },
                                                  {
                                                    ""book_id"": 5001,
                                                    ""id"": 5002,
                                                    ""content"": ""Book #1 - Review #2"",
                                                    ""website_users"": {
                                                      ""id"": 4,
                                                      ""username"": ""book_lover_95""
                                                    }
                                                  }
                                                ]
                                              },
                                              ""authors"": {
                                                ""items"": [
                                                  {
                                                    ""id"": 5001,
                                                    ""name"": ""Author #1"",
                                                    ""birthdate"": ""2000-01-02""
                                                  },
                                                  {
                                                    ""id"": 5002,
                                                    ""name"": ""Author #2"",
                                                    ""birthdate"": ""2001-02-03""
                                                  }
                                                ]
                                              }
                                            },
                                            {
                                              ""id"": 5002,
                                              ""title"": ""Book #2"",
                                              ""publisher_id"": 1234,
                                              ""publishers"": {
                                                ""id"": 1234,
                                                ""name"": ""Big Company""
                                              },
                                              ""reviews"": {
                                                ""items"": []
                                              },
                                              ""authors"": {
                                                ""items"": [
                                                  {
                                                    ""id"": 5003,
                                                    ""name"": ""Author #3"",
                                                    ""birthdate"": ""2000-01-02""
                                                  },
                                                  {
                                                    ""id"": 5004,
                                                    ""name"": ""Author #4"",
                                                    ""birthdate"": ""2001-02-03""
                                                  }
                                                ]
                                              }
                                            }
                                          ]
                                        }";

            string linkingTableDbValidationQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT(
                        'book_id' VALUE book_id,
                        'author_id' VALUE author_id,
                        'royalty_percentage' VALUE royalty_percentage
                    )
                    ORDER BY book_id ASC
                    RETURNING CLOB
                )
                FROM book_author_link
                WHERE (book_id = 5001 AND (author_id = 5001 OR author_id = 5002))
                   OR (book_id = 5002 AND (author_id = 5003 OR author_id = 5004))";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0},{""book_id"":5002,""author_id"":5003,""royalty_percentage"":65.0},{""book_id"":5002,""author_id"":5004,""royalty_percentage"":35.0}]";

            await ManyTypeMultipleCreateMutationOperation(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task PointMultipleCreateFailsDueToCreatePolicyViolationAtTopLevelEntity()
        {
            string expectedErrorMessage = "Could not insert row with given values for entity: Book at nesting level : 0";

            string bookDbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB('[]'))
                FROM books
                WHERE id = 5001";

            string publisherDbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB('[]'))
                FROM publishers
                WHERE id = 5001";

            await PointMultipleCreateFailsDueToCreatePolicyViolationAtTopLevelEntity(expectedErrorMessage, bookDbQuery, publisherDbQuery);
        }

        [TestMethod]
        public async Task PointMultipleCreateFailsDueToCreatePolicyViolationAtRelatedEntity()
        {
            string expectedErrorMessage = "Could not insert row with given values for entity: Publisher at nesting level : 1";

            string bookDbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB('[]'))
                FROM books
                WHERE id = 5001";

            string publisherDbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB('[]'))
                FROM publishers
                WHERE id = 5001";

            await PointMultipleCreateFailsDueToCreatePolicyViolationAtRelatedEntity(expectedErrorMessage, bookDbQuery, publisherDbQuery);
        }

        [TestMethod]
        public async Task ManyTypeMultipleCreateFailsDueToCreatePolicyFailure()
        {
            string expectedErrorMessage = "Could not insert row with given values for entity: Book at nesting level : 0";

            string bookDbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB('[]'))
                FROM books
                WHERE id >= 5001";

            string publisherDbQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(*) RETURNING CLOB), TO_CLOB('[]'))
                FROM publishers
                WHERE id >= 5001";

            await ManyTypeMultipleCreateFailsDueToCreatePolicyFailure(expectedErrorMessage, bookDbQuery, publisherDbQuery);
        }

        [TestMethod]
        public async Task PointMultipleCreateMutationWithReadPolicyViolationAtRelatedEntity()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 2345,
                                          ""reviews"": {
                                            ""items"": [
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Review #1"",
                                                ""websiteuser_id"": 4
                                              }
                                            ]
                                          }
                                        }";

            await PointMultipleCreateMutationWithReadPolicyViolationAtRelatedEntity(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToOneRelationshipDefinedInConfigFile()
        {
            Assert.Inconclusive(
                "Oracle fixtures expose users/user_profiles but not User_NonAutogenRelationshipColumn GraphQL types.");
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToOneRelationshipDefinedInConfigFile()
        {
            Assert.Inconclusive(
                "Oracle test config does not map book_mm / publisher_mm config-defined relationships.");
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToManyRelationshipDefinedInConfigFile()
        {
            Assert.Inconclusive(
                "Oracle test config does not map book_mm / reviews_mm config-defined relationships.");
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToManyRelationshipDefinedInConfigFile()
        {
            Assert.Inconclusive(
                "Oracle test config does not map book_author_link_mm as a GraphQL linking entity.");
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithAllRelationshipTypesDefinedInConfigFile()
        {
            Assert.Inconclusive(
                "Oracle test config does not map *_mm entities used by config-defined multiple-create relationships.");
        }

        [TestMethod]
        public async Task ManyTypeMultipleCreateMutationOperationRelationshipsDefinedInConfig()
        {
            Assert.Inconclusive(
                "Oracle test config does not map *_mm entities used by config-defined multiple-create relationships.");
        }
    }
}
