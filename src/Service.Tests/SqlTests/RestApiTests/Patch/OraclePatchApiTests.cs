// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OraclePatchApiTests : PatchApiTestBase
    {
        // The magazine entity's physical table lives in the FOO schema in Oracle; the base constant
        // is unqualified and would resolve to the test connection's SYSTEM schema (ORA-00942).
        private const string _oracleNonAutoGenPKTable = "foo.magazines";

        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "PatchOne_Insert_KeylessWithAutoGenPK_Test",
                @"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'My New Book' AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "PatchOne_Update_KeylessWithPKInBody_ExistingRow_Test",
                @"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id = 1
                        AND title = 'Updated Vogue' AND issue_number = 1234
                    ) subq
                "
            },
            {
                "PatchOne_Insert_KeylessWithPKInBody_NewRow_Test",
                @"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'Brand New Magazine'
                    ) subq
                "
            },
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                @"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id = 2 AND title = 'Batman Begins'
                        AND issue_number = 1234
                    ) subq
                "
            },
            {
                "PatchOne_Insert_UniqueCharacters_Test",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('┬─┬ノ( º _ ºノ)' VALUE NoteNum, '始計' VALUE DetailAssessmentAndPlanning, '作戰' VALUE WagingWar, '謀攻' VALUE StrategicAttack)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationUniqueCharactersTable + @"
                      WHERE NoteNum = 2
                  ) subq
                "
            },
            {
                "PatchOne_Insert_Mapping_Test",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'Scientific Name' VALUE species, 'United State''s Region' VALUE region, 'height' VALUE height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 4
                    ) subq
                "
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName ='Tales' AND piecesAvailable = 5
                        AND piecesRequired = 4
                    ) subq
                "
            },
            {
                "PatchOneInsertWithDatabasePolicy",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName, piecesAvailable, piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 0 AND pieceid = 7 AND categoryName = 'SciFi'
                        AND piecesAvailable = 4 AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "PatchOneUpdateWithDatabasePolicy",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 100 AND pieceid = 99 AND categoryName ='Historical' AND piecesAvailable = 4
                        AND piecesRequired = 0 AND pieceid != 1
                    ) subq
                "
            },
            {
                "PatchOne_Insert_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 5
                        AND piecesRequired = 4
                    ) subq
                "
            },
            {
                "PatchOne_Insert_Default_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 7 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 0
                        AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "PatchOne_Insert_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 4
                    ) subq
                "
            },
            {
                "PatchOne_Update_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 8 AND title = 'Heart of Darkness'
                        AND publisher_id = 2324
                    ) subq
                "
            },
            {
                "PatchOne_Update_IfMatchHeaders_Test",
                @"
                  SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1 and title = 'The Hobbit Returns to The Shire' and publisher_id = 1234
                      ORDER BY id asc
                      FETCH FIRST 1 ROWS ONLY
                  ) subq"
            },
            {
                "PatchOne_Update_Default_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'content' VALUE content, 'book_id' VALUE book_id, 'websiteuser_id' VALUE websiteuser_id) AS data
                    FROM (
                        SELECT id, content, book_id, websiteuser_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 567 AND book_id = 1 AND content = 'That''s a great book'
                    ) subq
                "
            },
            {
                "PatchOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 10
                        AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "PatchOne_Update_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 10
                        AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "PatchOneUpdateWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'book_name' VALUE book_name, 'row_version' VALUE row_version, 'copies_sold' VALUE copies_sold, 'last_sold_on' VALUE last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, row_version, copies_sold, TO_CHAR(last_sold_on, 'YYYY-MM-DD HH24:MI:SS') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 1 AND book_name = 'New book' AND copies_sold = 50
                    ) subq
                "
            },
            {
                "PatchOneInsertWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'book_name' VALUE book_name, 'row_version' VALUE row_version, 'copies_sold' VALUE copies_sold, 'last_sold_on' VALUE last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, row_version, copies_sold, TO_CHAR(last_sold_on, 'YYYY-MM-DD HH24:MI:SS') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'New book' AND copies_sold = 50
                    ) subq
                "
            },
            {
                "PatchOne_Update_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 1000 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "PatchOne_Update_NoReadTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE 0 = 1
                    ) subq
                "
            },
            {
                "Patch_Update_WithExcludeFieldsTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 8 AND title = 'Heart of Darkness'
                        AND publisher_id = 2324
                    ) subq
                "
            },
            {
                "PatchInsert_NoReadTest",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE 0 = 1
                    ) subq
                "
            },
            {
                "Patch_Insert_WithExcludeFieldsTest",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName, piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 0 AND pieceid = 7 AND categoryName ='SciFi' AND piecesAvailable = 4
                        AND piecesRequired = 4
                    ) subq
                "
            },
            {
                "PatchOneInsertInStocksViewSelected",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName, piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 4 AND pieceid = 1
                    ) subq
                "
            },
            {
                "PatchOneUpdateStocksViewSelected",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName, piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 2 AND pieceid = 1
                    ) subq
                "
            }
        };

        #region overridden tests

        [TestMethod]
        public async Task PatchOneViewBadRequestTest()
        {
            // PATCH update trying to modify an underlying column of the composite (join) view.
            // Oracle rejects DML on the non key-preserved table with ORA-01779, surfaced as 500.
            string requestBody = @"
            {
                ""name"": ""new publisher"",
                ""title"": ""new Book""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1/pub_id/1234",
                queryString: string.Empty,
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "ORA-",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }
        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        [TestMethod]
        public new async Task PatchWithUncastablePKValue()
        {
            string requestBody = @"
            {
                ""publisher_id"": ""StringFailsToCastToInt""
            }";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                operationType: EntityActionOperation.UpsertIncremental,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"StringFailsToCastToInt\" cannot be resolved as column \"PUBLISHER_ID\" with type \"Decimal\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Oracle treats the empty string '' as NULL, so the inherited test's final step
        /// (PATCH categoryName to '') violates the NOT NULL constraint (ORA-01407). The
        /// empty-string round-trip cannot be represented in Oracle, so the first three
        /// steps are exercised instead.
        /// </summary>
        [TestMethod]
        public override async Task PatchOne_Update_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Heart of Darkness""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/8",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Update_Test)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""content"": ""That's a great book""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/567/book_id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithCompositePrimaryKey,
                    sqlQuery: GetQuery("PatchOne_Update_Default_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""piecesAvailable"": ""10""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/1/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Update_CompositeNonAutoGenPK_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Oracle's books_sold table has no computed 'last_sold_on_date' column, so the field is
        /// unknown to the entity and surfaces as a generic invalid-body error instead of the
        /// computed-field message MSSQL/PostgreSQL emit.
        /// </summary>
        [TestMethod]
        public override async Task PatchOneWithComputedFieldInRequestBody()
        {
            string requestBody = @"
            {
                ""last_sold_on_date"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Oracle treats the empty string '' as NULL, so the inherited test's third step
        /// (PATCH-insert categoryName as '') violates the NOT NULL constraint (ORA-01400) and
        /// its verification SQL (categoryName = '') could never match. The remaining non-empty
        /// steps are exercised instead.
        /// </summary>
        [TestMethod]
        public override async Task PatchOne_Insert_NonAutoGenPK_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Batman Begins"",
                ""issue_number"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/2",
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: GetQuery(nameof(PatchOne_Insert_NonAutoGenPK_Test)),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
                ""categoryName"": ""Tales"",
                ""piecesAvailable"":""5"",
                ""piecesRequired"":""4""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/4/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_CompositeNonAutoGenPK_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
                ""categoryName"": ""SciFi""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"categoryid/7/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    sqlQuery: GetQuery("PatchOne_Insert_Default_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            // Entity with mapping defined for columns
            requestBody = @"
            {
                ""Scientific Name"": ""Quercus"",
                ""United State's Region"": ""South West""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"treeid/4",
                    queryString: null,
                    entityNameOrPath: _integrationMappingEntity,
                    sqlQuery: GetQuery("PatchOne_Insert_Mapping_Test"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );
        }
    }
}
