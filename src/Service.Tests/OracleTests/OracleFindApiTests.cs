// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained for Oracle.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleFindApiTests : FindApiTestBase
    {
        protected static string DEFAULT_SCHEMA = "SYSTEM";

        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                $"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 2 ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindByDateTimePKTest",
                $"SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, " +
                $"'instant' VALUE instant, 'price' VALUE price, 'is_wholesale_price' VALUE is_wholesale_price) AS data " +
                $"FROM (SELECT * FROM {_tableWithDateTimePK} " +
                $"WHERE categoryid = 2 AND pieceid = 1 " +
                $"AND instant = TO_TIMESTAMP('2023-08-21 15:11:04', 'YYYY-MM-DD HH24:MI:SS') " +
                $"FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindEmptyTable",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_emptyTableTableName})"
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE 1 != 1)"
            },
            {
                "FindManyStoredProcedureTest",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) AS data " +
                $"FROM (SELECT id, title FROM books ORDER BY id ASC)"
            },
            {
                "FindOneStoredProcedureTestUsingParameter",
                $"SELECT JSON_OBJECT('id' VALUE id, 'name' VALUE name) AS data " +
                $"FROM (SELECT id, name FROM publishers WHERE id = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindOnTableWithUniqueCharacters",
                $"SELECT JSON_ARRAYAGG(" +
                $"JSON_OBJECT('┬─┬ノ( º _ ºノ)' VALUE \"NoteNum\", " +
                $"'始計' VALUE \"DetailAssessmentAndPlanning\", " +
                $"'作戰' VALUE \"WagingWar\", " +
                $"'謀攻' VALUE \"StrategicAttack\")) AS data " +
                $"FROM (SELECT * FROM {_integrationUniqueCharactersTable})"
            },
            {
                "FindOnTableWithNamingCollision",
                $"SELECT JSON_ARRAYAGG(" +
                $"JSON_OBJECT('upc' VALUE upc, 'comic_name' VALUE comic_name, 'issue' VALUE issue)) AS data " +
                $"FROM (SELECT upc, comic_name, issue FROM {_collisionTable} " +
                $"WHERE 1 = 1 ORDER BY upc ASC)"
            },
            {
                "FindViewAll",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_simple_all_books} ORDER BY id)"
            },
            {
                "FindViewWithKeyAndMapping",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('book_id' VALUE id)) AS data " +
                $"FROM (SELECT id FROM {_book_view_with_key_and_mapping} ORDER BY id FETCH FIRST 100 ROWS ONLY)"
            },
            {
                "FindViewSelected",
                $"SELECT JSON_OBJECT(" +
                $"'categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, " +
                $"'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable) AS data " +
                $"FROM (SELECT * FROM {_simple_subset_stocks} " +
                $"WHERE categoryid = 2 AND pieceid = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 1)"
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 2)"
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id > 3)"
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id >= 4)"
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 5)"
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id <= 4)"
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id != 3)"
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE NOT (id < 2))"
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE NOT (title IS NULL))"
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE title IS NOT NULL)"
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 3 AND id > 1)"
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 3 OR id > 4)"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 4 AND id > 1 AND title != 'Awesome book')"
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 1 OR id = 2 OR id = 3)"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE (id > 2 AND id < 4) OR title = 'Awesome book')"
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE (NOT (id < 3) OR id < 4) OR NOT (title = 'Awesome book'))"
            },
            {
                "FindTestWithFilterContainingSpecialCharacters",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE title = 'SOME%CONN')"
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                $"SELECT JSON_OBJECT('id' VALUE id, 'content' VALUE content) AS data " +
                $"FROM (SELECT id, content FROM reviews WHERE id = 567 AND book_id = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindByIdTestWithQueryStringFields",
                $"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data " +
                $"FROM (SELECT id, title FROM {_integrationTableName} WHERE id = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithQueryStringOneField",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id)) AS data " +
                $"FROM (SELECT id FROM {_integrationTableName})"
            },
            {
                "FindTestWithQueryStringAllFields",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName})"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY title ASC, id ASC)"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY publisher_id DESC, id ASC)"
            },
            {
                "FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height)) AS data " +
                $"FROM (SELECT treeId, species AS \"fancyName\", region, height FROM {_integrationMappingTable} ORDER BY species ASC)"
            },
            {
                "FindTestWithFirstSingleKeyPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTest_NoQueryParams_PaginationNextLink",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationPaginationTableName} ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)"
            },
            {
                "FindTest_Negative1QueryParams_Pagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationPaginationTableName} ORDER BY id ASC)"
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM reviews WHERE 1=1 ORDER BY book_id ASC, id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id > 7 ORDER BY id ASC)"
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM reviews " +
                $"WHERE book_id > 1 OR (book_id = 1 AND id > 567) ORDER BY book_id ASC, id ASC)"
            },
            {
                "FindTestWithPaginationVerifSinglePrimaryKeyInAfter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithPaginationVerifMultiplePrimaryKeysInAfter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM reviews ORDER BY book_id ASC, id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithIntTypeNullValuesOrderByAsc",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('typeid' VALUE id, 'int_types' VALUE int_types)) AS data " +
                $"FROM (SELECT id, int_types FROM type_table ORDER BY int_types ASC, id ASC)"
            },
            {
                "FindMany_MappedColumn_NoOrderByQueryParameter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('bkid' VALUE id, 'name' VALUE bkname)) AS data " +
                $"FROM (SELECT id, bkname FROM mappedbookmarks ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id DESC, publisher_id ASC)"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderByInReverse",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY publisher_id ASC, id DESC)"
            },
            {
                "FindTestWithFirstSingleKeyIncludedInOrderByAndPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstTwoOrderByAndPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC FETCH FIRST 2 ROWS ONLY)"
            },
            {
                "FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM {_tableWithCompositePrimaryKey} ORDER BY id DESC, book_id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM {_tableWithCompositePrimaryKey} ORDER BY book_id ASC, id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstAndMultiColumnOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY publisher_id DESC, title DESC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY publisher_id DESC, id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstMultiKeyPaginationAndOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM {_tableWithCompositePrimaryKey} " +
                $"WHERE 1=1 ORDER BY content DESC, book_id ASC, id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithMappedFieldsToBeReturned",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId, 'Scientific Name' VALUE species, " +
                $"'United State''s Region' VALUE region, 'height' VALUE height)) AS data " +
                $"FROM (SELECT treeId, species AS \"Scientific Name\", region AS \"United State's Region\", height FROM {_integrationMappingTable})"
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('Scientific Name' VALUE species)) AS data " +
                $"FROM (SELECT species AS \"Scientific Name\" FROM {_integrationMappingTable})"
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId)) AS data " +
                $"FROM (SELECT treeId FROM {_integrationMappingTable})"
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height)) AS data " +
                $"FROM (SELECT treeId, species AS \"fancyName\", region, height FROM {_integrationMappingTable} WHERE species = 'Tsuga terophylla')"
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height)) AS data " +
                $"FROM (SELECT treeId, species AS \"fancyName\", region, height FROM {_integrationMappingTable} ORDER BY species ASC)"
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height)) AS data " +
                $"FROM (SELECT treeId, species AS \"fancyName\", region, height FROM {_integrationMappingTable} ORDER BY species ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height)) AS data " +
                $"FROM (SELECT treeId, species AS \"fancyName\", region, height FROM {_integrationMappingTable} " +
                $"WHERE species > 'Pseudotsuga menziesii' ORDER BY species ASC, treeId ASC)"
            },
            {
                "FindManyTestWithDatabasePolicy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'name' VALUE name)) AS data " +
                $"FROM (SELECT id, name FROM {_foreignKeyTableName} WHERE id != 1234 OR id > 1940 ORDER BY id ASC)"
            },
            {
                "FindInAccessibleRowWithDatabasePolicy",
                $"SELECT JSON_OBJECT('id' VALUE id, 'name' VALUE name) AS data " +
                $"FROM (SELECT id, name FROM {_foreignKeyTableName} WHERE id = 1234 AND (id != 1234 OR id > 1940) " +
                $"ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindAllOnTableWithSecPolicy",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'category' VALUE category, 'revenue' VALUE revenue, 'accessible_role' VALUE accessible_role)) AS data " +
                $"FROM (SELECT id, category, revenue, accessible_role FROM {_tableWithSecurityPolicy} WHERE id <= 2)"
            },
            {
                "FindOneOnTableWithSecPolicy",
                $"SELECT JSON_OBJECT('id' VALUE id, 'category' VALUE category, 'revenue' VALUE revenue, 'accessible_role' VALUE accessible_role) AS data " +
                $"FROM (SELECT id, category, revenue, accessible_role FROM {_tableWithSecurityPolicy} WHERE id = 2 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindOneOnTableWithSecPolicyWithNoAccessibleRow",
                $"SELECT JSON_OBJECT('id' VALUE id, 'category' VALUE category, 'revenue' VALUE revenue, 'accessible_role' VALUE accessible_role) AS data " +
                $"FROM (SELECT id, category, revenue, accessible_role FROM {_tableWithSecurityPolicy} WHERE id = 3 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindWithSelectAndOrderbyQueryStringsOnViews",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid' VALUE categoryid, 'categoryName' VALUE categoryName)) AS data " +
                $"FROM (SELECT categoryid, categoryName FROM {_simple_subset_stocks} ORDER BY piecesAvailable ASC, categoryid ASC, pieceid ASC)"
            },
            {
                "FindWithSelectAndOrderbyQueryStringsOnTables",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) AS data " +
                $"FROM (SELECT id, title FROM {_integrationTableName} ORDER BY publisher_id ASC, id ASC)"
            },
            {
                "FindTestFilterForVarcharColumnWithNullAndNonNullValues",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('*' VALUE id)) AS data " +
                $"FROM (SELECT * FROM {_integrationBrokenMappingTable} WHERE habitat = 'sand')"
            }
        };

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

        #region RestApiTestBase Overrides

        public override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        public override string GetDefaultSchemaForEdmModel()
        {
            return $"{DEFAULT_SCHEMA}.";
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        [TestMethod]
        public override async Task FindTestOnTableWithSecurityPolicy()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _entityWithSecurityPolicy,
                sqlQuery: GetQuery("FindAllOnTableWithSecPolicy")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithSecurityPolicy,
                sqlQuery: GetQuery("FindOneOnTableWithSecPolicy")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/3",
                queryString: string.Empty,
                entityNameOrPath: _entityWithSecurityPolicy,
                sqlQuery: GetQuery("FindOneOnTableWithSecPolicyWithNoAccessibleRow")
            );
        }

        #endregion
    }
}
