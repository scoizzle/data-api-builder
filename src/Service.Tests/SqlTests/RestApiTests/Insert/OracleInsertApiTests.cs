// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleInsertApiTests : InsertApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5001
                    ) subq
                "
            },
            {
                "InsertOneInSupportedTypes",
                @"
                    SELECT JSON_OBJECT(
                        'typeid' VALUE id, 'byte_types' VALUE byte_types, 'short_types' VALUE short_types,
                        'int_types' VALUE int_types, 'long_types' VALUE long_types, 'string_types' VALUE string_types,
                        'nvarchar_string_types' VALUE nvarchar_string_types, 'single_types' VALUE single_types,
                        'float_types' VALUE float_types, 'decimal_types' VALUE decimal_types, 'boolean_types' VALUE boolean_types,
                        'date_types' VALUE date_types, 'datetime_types' VALUE datetime_types, 'datetime2_types' VALUE datetime2_types,
                        'datetimeoffset_types' VALUE datetimeoffset_types, 'smalldatetime_types' VALUE smalldatetime_types,
                        'bytearray_types' VALUE bytearray_types, 'uuid_types' VALUE uuid_types) AS data
                    FROM (
                        SELECT id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
                            single_types, float_types, decimal_types, boolean_types, date_types, datetime_types,
                            datetime2_types, datetimeoffset_types, smalldatetime_types, bytearray_types, uuid_types
                        FROM " + _integrationTypeTable + @"
                        WHERE id = 5001 AND bytearray_types is NULL 
                    ) subq
                "
            },
            {
                "InsertOneWithComputedFieldMissingInRequestBody",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'book_name' VALUE book_name, 'row_version' VALUE row_version, 'copies_sold' VALUE copies_sold, 'last_sold_on' VALUE last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, row_version, copies_sold, TO_CHAR(last_sold_on, 'YYYY-MM-DD HH24:MI:SS') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'Harry Potter' AND copies_sold = 50
                    ) subq
                "
            },
            {
                "InsertOneUniqueCharactersTest",
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
                "InsertOneWithMappingTest",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'Scientific Name' VALUE species, 'United State''s Region' VALUE region, 'height' VALUE height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 3
                    ) subq
                "
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 2 AND categoryName ='Tales' AND piecesAvailable = 0
                        AND piecesRequired = 0
                    ) subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'content' VALUE content, 'book_id' VALUE book_id, 'websiteuser_id' VALUE websiteuser_id) AS data
                    FROM (
                        SELECT id, content, book_id, websiteuser_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND book_id = 1
                    ) subq
                "
            },
            {
                "InsertOneWithNullFieldValue",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable, 'piecesRequired' VALUE piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 1
                    ) subq
                "
            },
            {
                "InsertOneInDefaultTestTable",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'content' VALUE content, 'book_id' VALUE book_id, 'websiteuser_id' VALUE websiteuser_id) AS data
                    FROM (
                        SELECT id, content, book_id, websiteuser_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS + 1}" + @"
                        AND book_id = 2 AND content = 'Its a classic'
                    ) subq
                "
            },
            {
                "InsertOneWithDefaultValuesAndEmptyRequestBody",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _tableWithDefaultValues + @"
                    ) subq
                "
            },
            {
                "InsertSqlInjectionQuery1",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' UNION SELECT * FROM books/*'
                    ) subq
                "
            },
            {
                "InsertSqlInjectionQuery2",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = '; SELECT * FROM information_schema.tables/*'
                    ) subq
                "
            },
            {
                "InsertSqlInjectionQuery3",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'value; SELECT * FROM v$version--'
                    ) subq
                "
            },
            {
                "InsertSqlInjectionQuery4",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'id; DROP TABLE books;'
                    ) subq
                "
            },
            {
                "InsertSqlInjectionQuery5",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' '' UNION SELECT * FROM books/*'
                    ) subq
                "
            },
            {
                "InsertOneWithExcludeFieldsTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) subq
                "
            },
            {
                "InsertOneWithNoReadPermissionsTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @" AND 0 = 1 
                    ) subq
                "
            },
            {
                "InsertOneInBooksViewAll",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _simple_all_books + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'My New Book' AND publisher_id = 1234
                    ) subq
                "
            },
            {
                "InsertOneInStocksViewSelected",
                @"
                    SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid,
                        'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName, piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName = 'SciFi' AND piecesAvailable = 0
                    ) subq
                "
            },
            {
                "InsertOneRowWithBuiltInMethodAsDefaultvaluesTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id, 'user_value' VALUE user_value, 'current_date' VALUE cur_date, 'current_timestamp' VALUE cur_timestamp, 'random_number' VALUE random_number, 'next_date' VALUE nd, 'default_string_with_parenthesis' VALUE default_string_with_parenthesis, 'default_function_string_with_parenthesis' VALUE default_function_string_with_parenthesis, 'default_integer' VALUE default_integer, 'default_date_string' VALUE dds) AS data
                    FROM (
                        SELECT t.id AS id, t.user_value AS user_value,
                            TO_CHAR(t.current_date, 'YYYY-MM-DD HH24:MI:SS') AS cur_date,
                            TO_CHAR(t.current_timestamp, 'YYYY-MM-DD HH24:MI:SS') AS cur_timestamp,
                            t.random_number AS random_number,
                            TO_CHAR(t.next_date, 'YYYY-MM-DD HH24:MI:SS') AS nd,
                            t.default_string_with_parenthesis AS default_string_with_parenthesis,
                            t.default_function_string_with_parenthesis AS default_function_string_with_parenthesis,
                            t.default_integer AS default_integer,
                            TO_CHAR(t.default_date_string, 'YYYY-MM-DD HH24:MI:SS') AS dds
                        FROM " + _defaultValueAsBuiltInMethodsTable + @" t
                        WHERE t.id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) subq
                "
            }
        };

        /// <summary>
        /// Validates we are able to successfully insert with an empty request body into a table
        /// that has default values available for its columns.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task InsertOneWithDefaultValuesAndEmptyRequestBody()
        {
            // Validate that we can insert when request body is empty but we have columns that have default values.
            string requestBody = @"
            {
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: string.Empty,
                entityNameOrPath: _entityWithDefaultValues,
                sqlQuery: GetQuery(nameof(InsertOneWithDefaultValuesAndEmptyRequestBody)),
                operationType: EntityActionOperation.Insert,
                exceptionExpected: false,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created
                );
        }

        #region overridden tests
        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingForeignKeyConstraint()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 12345
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "ORA-02291",
                expectedStatusCode: HttpStatusCode.Conflict,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingUniqueKeyConstraint()
        {
            string requestBody = @"
            {
                ""categoryid"": 1,
                ""pieceid"": 1,
                ""categoryName"": ""SciFi""
            }"
            ;

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "ORA-00001",
                expectedStatusCode: HttpStatusCode.Conflict,
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
        public new async Task InsertWithUncastablePKValue()
        {
            string requestBody = @"
            {
                ""title"": ""BookTitle"",
                ""publisher_id"": ""StringFailsToCastToInt""
            }";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"StringFailsToCastToInt\" cannot be resolved as column \"PUBLISHER_ID\" with type \"Decimal\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        [TestMethod]
        public new async Task InsertOneInViewBadRequestTest()
        {
            string requestBody = @"
            {
                ""name"": ""new publisher"",
                ""title"": ""New Book""
            }";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "ORA-01779",
                expectedStatusCode: HttpStatusCode.InternalServerError,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        /// <summary>
        /// Oracle's metadata exposes unmapped physical columns in lowercase (see
        /// OracleMetadataProvider.GetExposedColumnName), so the Tree primary key is addressed as
        /// 'treeid' rather than the 'treeId' the shared engines use. Mapped columns keep their
        /// configured exposed names.
        /// </summary>
        [TestMethod]
        public override async Task InsertOneWithMappingTest()
        {
            string requestBody = @"
            {
                ""treeid"" : 3,
                ""Scientific Name"": ""Cupressus Sempervirens"",
                ""United State's Region"": ""South East""
            }";

            string expectedLocationHeader = $"treeid/3";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(InsertOneWithMappingTest)),
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        /// <summary>
        /// Oracle's books_sold table has no computed 'last_sold_on_date' column, so the field is
        /// unknown to the entity and is reported as an unexpected body field rather than as a
        /// computed field (the message MSSQL/PostgreSQL emit for their computed column).
        /// </summary>
        [TestMethod]
        public override async Task InsertOneWithComputedFieldInRequestBody()
        {
            string requestBody = @"
            {
                ""id"": 2,
                ""last_sold_on_date"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Invalid request body. Contained unexpected fields in body: last_sold_on_date",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
            );
        }

        /// <summary>
        /// Oracle maps NUMBER columns to System.Decimal and preserves the physical column casing,
        /// so the uncastable-value error names the uppercase PUBLISHER_ID column with type Decimal.
        /// </summary>
        [TestMethod]
        public override async Task InsertOneWithInvalidTypeInJsonBodyTest()
        {
            string requestBody = @"
            {
                ""title"": [""My New Book"", ""Another new Book"", {""author"": ""unknown""}],
                ""publisher_id"": [1234,4321]
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"[1234,4321]\" cannot be resolved as column \"PUBLISHER_ID\" with type \"Decimal\".",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }
    }
}
