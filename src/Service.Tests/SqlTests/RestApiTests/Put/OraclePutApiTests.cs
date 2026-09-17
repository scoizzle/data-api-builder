// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Put
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OraclePutApiTests : PutApiTestBase
    {
        // The magazine entity's physical table lives in the FOO schema in Oracle, so verification
        // SQL must qualify it; the base constant is unqualified and would resolve to the test
        // connection's SYSTEM schema (ORA-00942).
        private const string _oracleNonAutoGenPKTable = "foo.magazines";

        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "PutOne_Insert_KeylessWithAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'My New Book' AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "PutOne_Update_KeylessWithPKInBody_ExistingRow_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id = 1
                        AND title = 'Updated Vogue' AND issue_number = 9999
                    ) subq
                "
            },
            {
                "PutOne_Insert_KeylessWithPKInBody_NewRow_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'Brand New Magazine' AND issue_number = 42
                    ) subq
                "
            },
            {
                "PutOne_Update_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "PutOne_Update_IfMatchHeaders_Test",
                @"
                  SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1 and title = 'The Return of the King'
                      ORDER BY id asc
                      FETCH FIRST 1 ROWS ONLY
                  ) subq"
            },
            {
                "PutOne_Update_Default_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'content' VALUE content, 'book_id' VALUE book_id, 'websiteuser_id' VALUE websiteuser_id) AS data
                    FROM (
                        SELECT id, content, book_id, websiteuser_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 568 AND book_id = 1 AND content = 'Good book to read'
                    ) subq
                "
            },
            {
                "PutOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 10
                        AND piecesRequired = 5
                    ) subq
                "
            },
            {
                "PutOneInsertWithDatabasePolicy",
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
                "PutOneUpdateWithDatabasePolicy",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 100 AND pieceid = 99 AND categoryName ='SciFi' AND piecesAvailable = 4
                        AND piecesRequired = 5 AND pieceid != 1
                    ) subq
                "
            },
            {
                "PutOne_Update_NullOutMissingField_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 5
                    ) subq
                "
            },
            {
                "PutOne_Update_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 2
                        AND piecesRequired = 3
                    ) subq
                "
            },
            {
                "PutOne_Update_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='Tales' AND piecesAvailable is NULL
                        AND piecesRequired = 4
                    ) subq
                "
            },
            {
                "PutOneUpdateWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'book_name' VALUE book_name, 'row_version' VALUE row_version, 'copies_sold' VALUE copies_sold, 'last_sold_on' VALUE last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, row_version, copies_sold, TO_CHAR(last_sold_on, 'YYYY-MM-DD HH24:MI:SS') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 1 AND book_name = 'New book' AND copies_sold = 101 AND last_sold_on = TO_TIMESTAMP('2023-09-12 05:30:30', 'YYYY-MM-DD HH24:MI:SS')
                    ) subq
                "
            },
            {
                "PutOneInsertWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'book_name' VALUE book_name, 'row_version' VALUE row_version, 'copies_sold' VALUE copies_sold, 'last_sold_on' VALUE last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, row_version, copies_sold, TO_CHAR(last_sold_on, 'YYYY-MM-DD HH24:MI:SS') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'New book' AND copies_sold = 101
                    ) subq
                "
            },
            {
                "PutOne_Update_With_Mapping_Test",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'Scientific Name' VALUE species, 'United State''s Region' VALUE region, 'height' VALUE height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 1
                    ) subq
                "
            },
            {
                "PutOne_Insert_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id > 5000 AND title = 'Batman Returns'
                            AND issue_number = 1234
                    ) subq
                "
            },
            {
                "PutOne_Insert_Nullable_Test",
                @"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _oracleNonAutoGenPKTable + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS + 1}" + @" AND title = 'Times'
                        AND issue_number is NULL
                    ) subq
                "
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                @"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'volume' VALUE volume, 'categoryName' VALUE categoryName, 'series_id' VALUE series_id) AS data
                    FROM (
                        SELECT id, title, volume, categoryName, series_id
                        FROM " + _integration_AutoGenNonPK_TableName + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS}" + @" AND title = 'Star Trek'
                        AND volume IS NOT NULL
                    ) subq
                "
            },
            {
                "PutOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 2
                        AND piecesRequired = 1
                    ) subq
                "
            },
            {
                "PutOne_Insert_Default_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 8 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 0
                        AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "PutOne_Insert_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 2
                        AND piecesRequired = 3
                    ) subq
                "
            },
            {
                "PutOne_Insert_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 4
                    ) subq
                "
            },
            {
                "UpdateSqlInjectionQuery1",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = ' UNION SELECT * FROM books/*'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "UpdateSqlInjectionQuery2",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = '; SELECT * FROM information_schema.tables/*'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "UpdateSqlInjectionQuery3",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'value; SELECT * FROM v$version--'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "UpdateSqlInjectionQuery4",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'value; DROP TABLE authors;'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "PutOne_Update_WithExcludeFields_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "PutOne_Update_WithNoReadAction_Test",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE 0 = 1
                    ) subq
                "
            },
            {
                "PutInsert_NoReadTest",
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
                "Put_Insert_WithExcludeFieldsTest",
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
                "PutOneInsertInStocksViewSelected",
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
                "PutOneUpdateStocksViewSelected",
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
        public async Task PutOneInViewBadRequest()
        {
            // PUT update trying to modify an underlying column of the composite (join) view.
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
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "ORA-",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        [TestMethod]
        public async Task PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest()
        {
            await base.PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest(isExpectedErrorMsgSubstr: true);
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

        /// <summary>
        /// We have 1 test, which is named
        /// PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest
        /// that will have Db specific error messages.
        /// We return the mysql specific message here.
        /// </summary>
        /// <returns></returns>
        public override string GetUniqueDbErrorMessage()
        {
            return "ORA-01407";
        }

        [TestMethod]
        public new async Task PutWithUncastablePKValue()
        {
            string requestBody = @"
            {
                ""title"": ""BookTitle"",
                ""publisher_id"": ""StringFailsToCastToInt""
            }";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"StringFailsToCastToInt\" cannot be resolved as column \"PUBLISHER_ID\" with type \"Decimal\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Oracle treats the empty string '' as NULL, so the inherited test's final destructive
        /// update - which PUTs categoryName as '' - violates the NOT NULL constraint (ORA-01407).
        /// The empty-string round-trip cannot be represented in Oracle, so the first four
        /// destructive-update steps are exercised instead. The other engines' verification SQL
        /// for the omitted step (categoryName = '') could never match in Oracle anyway.
        /// </summary>
        [TestMethod]
        public override async Task PutOne_Update_Test()
        {
            string requestBody = @"
            {
                ""title"": ""The Hobbit Returns to The Shire"",
                ""publisher_id"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/7",
                    queryString: null,
                    entityNameOrPath: _integrationEntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Update_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""content"": ""Good book to read""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"book_id/1/id/568",
                queryString: null,
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery("PutOne_Update_Default_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""10"",
               ""piecesRequired"":""5""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/2/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_CompositeNonAutoGenPK_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.OK
                );

            // Perform a PUT UPDATE which nulls out a missing field from the request body
            // which is nullable.
            requestBody = @"
            {
                ""categoryName"":""SciFi"",
                ""piecesRequired"":""5""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/1/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Update_NullOutMissingField_Test"),
                operationType: EntityActionOperation.Upsert,
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
        public override async Task PutOneWithComputedFieldInRequestBody()
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
                operationType: EntityActionOperation.Upsert,
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
                operationType: EntityActionOperation.Upsert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Either insufficient or extra fields supplied.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// The Tree entity maps physical TREEID to the exposed name 'treeid' in
        /// dab-config.Oracle.json (Oracle catalog folding stores unquoted identifiers
        /// UPPERCASE; there is no engine-level lowercase fallback).
        /// </summary>
        [TestMethod]
        public override async Task PutOne_Update_With_Mapping_Test()
        {
            string requestBody = @"
            {
                ""Scientific Name"": ""Humulus Lupulus"",
                ""United State's Region"": ""Pacific North West""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "treeid/1",
                    queryString: null,
                    entityNameOrPath: _integrationMappingEntity,
                    sqlQuery: GetQuery(nameof(PutOne_Update_With_Mapping_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        /// <summary>
        /// Oracle treats the empty string '' as NULL, so the inherited test's final step
        /// (PUT-insert categoryName as '') violates the NOT NULL constraint (ORA-01400) and its
        /// verification SQL (categoryName = '') could never match. The remaining non-empty steps
        /// are exercised instead.
        /// </summary>
        [TestMethod]
        public override async Task PutOne_Insert_Test()
        {
            string requestBody = @"
            {
                ""title"": ""Batman Returns"",
                ""issue_number"": 1234
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS}",
                    queryString: null,
                    entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                    sqlQuery: GetQuery(nameof(PutOne_Insert_Test)),
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: string.Empty
                );

            // It should result in a successful insert,
            // where the nullable field 'issue_number' is properly left alone by the query validation methods.
            // The request body doesn't contain this field that neither has a default
            // nor is autogenerated.
            requestBody = @"
            {
                ""title"": ""Times""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS + 1}",
                queryString: null,
                entityNameOrPath: _integration_NonAutoGenPK_EntityName,
                sqlQuery: GetQuery("PutOne_Insert_Nullable_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            // It should result in a successful insert,
            // where the autogen'd field 'volume' is properly populated by the db.
            // The request body doesn't contain this non-nullable, non primary key
            // that is autogenerated.
            requestBody = @"
            {
                ""title"": ""Star Trek"",
                ""categoryName"" : ""Suspense""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{STARTING_ID_FOR_TEST_INSERTS}",
                queryString: null,
                entityNameOrPath: _integration_AutoGenNonPK_EntityName,
                sqlQuery: GetQuery("PutOne_Insert_AutoGenNonPK_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi"",
               ""piecesAvailable"":""2"",
               ""piecesRequired"":""1""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/3/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_CompositeNonAutoGenPK_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );

            requestBody = @"
            {
               ""categoryName"":""SciFi""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"categoryid/8/pieceid/1",
                queryString: null,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: GetQuery("PutOne_Insert_Default_Test"),
                operationType: EntityActionOperation.Upsert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: string.Empty
                );
        }

        /// <summary>
        /// MySQL-specific negative PUT test for the update database policy. A PUT targeting an existing row
        /// that violates the update policy ("@item.pieceid ne 1") must be rejected with 403
        /// DatabasePolicyFailure, and the targeted row must be left unmodified.
        /// The inherited <see cref="PutApiTestBase.PutOneWithUnsatisfiedDatabasePolicy"/> is skipped for MySQL
        /// because it also exercises a create-action database policy, which MySQL does not support; this test
        /// provides the denied-update coverage for the PUT path.
        /// </summary>
        [TestMethod]
        public async Task PutOneUpdateWithUnsatisfiedDatabasePolicyIsBlocked()
        {
            // Row (categoryid=0, pieceid=1) exists in the seed with categoryName='SciFi',
            // piecesAvailable=0, piecesRequired=0. pieceid=1 violates the update policy, so the
            // PUT must be denied.
            string requestBody = @"
            {
                ""categoryName"": ""SciFi"",
                ""piecesRequired"": 2,
                ""piecesAvailable"": 2
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "categoryid/0/pieceid/1",
                    queryString: null,
                    entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                    operationType: EntityActionOperation.Upsert,
                    requestBody: requestBody,
                    sqlQuery: string.Empty,
                    exceptionExpected: true,
                    expectedErrorMessage: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    expectedStatusCode: HttpStatusCode.Forbidden,
                    expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure.ToString(),
                    clientRoleHeader: "database_policy_tester"
                );

            // Verify the row was not modified: it must still match its original seed values.
            string unchangedRow = await GetDatabaseResultAsync(
                "SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, " +
                "'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data " +
                "FROM " + _Composite_NonAutoGenPK_TableName + " " +
                "WHERE categoryid = 0 AND pieceid = 1 AND categoryName = 'SciFi' AND piecesAvailable = 0 AND piecesRequired = 0");

            Assert.AreNotEqual(
                "[]",
                unchangedRow,
                "The row (categoryid=0, pieceid=1) must remain unmodified after a PUT blocked by the update policy.");
        }
    }
}
