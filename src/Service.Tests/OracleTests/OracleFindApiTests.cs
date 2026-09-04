// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_emptyTableTableName})"
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(" +
                $"JSON_OBJECT('┬─┬ノ( º _ ºノ)' VALUE NoteNum, " +
                $"'始計' VALUE DetailAssessmentAndPlanning, " +
                $"'作戰' VALUE WagingWar, " +
                $"'謀攻' VALUE StrategicAttack) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT NoteNum, DetailAssessmentAndPlanning, WagingWar, StrategicAttack FROM {_integrationUniqueCharactersTable})"
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                "FindBooksPubViewComposite",
                $"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'pub_id' VALUE pub_id, 'name' VALUE name) AS data " +
                $"FROM (SELECT id, title, pub_id, name FROM {_composite_subset_bookPub} " +
                $"WHERE id = 2 AND pub_id = 1234 AND name = 'Big Company' AND title = 'Also Awesome book' FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilterOnView",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT categoryid, pieceid, categoryName, piecesAvailable FROM {_simple_subset_stocks} WHERE pieceid = 1)"
            },
            {
                "FindTestWithFilterQueryOneNotFilterOnView",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('categoryid' VALUE categoryid, 'pieceid' VALUE pieceid, 'categoryName' VALUE categoryName, 'piecesAvailable' VALUE piecesAvailable) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT categoryid, pieceid, categoryName, piecesAvailable FROM {_simple_subset_stocks} WHERE NOT (categoryid > 1))"
            },
            {
                "FindTestWithFilterQueryOneLtFilterOnView",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'pub_id' VALUE pub_id, 'name' VALUE name) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT id, title, pub_id, name FROM {_composite_subset_bookPub} WHERE id < 5)"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 1)"
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 2)"
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id > 3)"
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id >= 4)"
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 5)"
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id <= 4)"
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id != 3)"
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE NOT (id < 2))"
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE NOT (title IS NULL))"
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE title IS NOT NULL)"
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 3 AND id > 1)"
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 3 OR id > 4)"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id < 4 AND id > 1 AND title != 'Awesome book')"
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE id = 1 OR id = 2 OR id = 3)"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE (id > 2 AND id < 4) OR title = 'Awesome book')"
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} WHERE (NOT (id < 3) OR id < 4) OR NOT (title = 'Awesome book'))"
            },
            {
                "FindTestWithFilterContainingSpecialCharacters",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT treeId, species, region, height FROM {_integrationMappingTable} ORDER BY species ASC)"
            },
            {
                "FindTestWithFirstSingleKeyPagination",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTest_NoQueryParams_PaginationNextLink",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'bkname' VALUE bkname) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT id, bkname FROM {_integrationPaginationTableName} ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)"
            },
            {
                "FindTest_Negative1QueryParams_Pagination",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'bkname' VALUE bkname) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT id, bkname FROM {_integrationPaginationTableName} ORDER BY id ASC)"
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'book_id' VALUE book_id)) AS data " +
                $"FROM (SELECT id, book_id FROM reviews WHERE 1=1 ORDER BY book_id ASC, id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstTwoOrderByAndPagination",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY publisher_id DESC, title DESC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'Scientific Name' VALUE species, " +
                $"'United State''s Region' VALUE region, 'height' VALUE height) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT treeId, species, region, height FROM {_integrationMappingTable} ORDER BY treeId ASC)"
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('Scientific Name' VALUE species) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT species FROM {_integrationMappingTable})"
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                $"SELECT JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId)) AS data " +
                $"FROM (SELECT treeId FROM {_integrationMappingTable} ORDER BY treeId ASC)"
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT treeId, species, region, height FROM {_integrationMappingTable} WHERE species = 'Tsuga terophylla' ORDER BY treeId ASC)"
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT treeId, species, region, height FROM {_integrationMappingTable} ORDER BY species ASC)"
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT treeId, species, region, height FROM {_integrationMappingTable} ORDER BY species ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('treeid' VALUE treeId, 'fancyName' VALUE species, 'region' VALUE region, 'height' VALUE height) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT treeId, species, region, height FROM {_integrationMappingTable} " +
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
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('speciesid' VALUE speciesid, 'region' VALUE region, 'habitat' VALUE habitat) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationBrokenMappingTable} WHERE habitat = 'sand')"
            },
            {
                "FindByIdWithSelectFieldsWithoutPKOnTable",
                $"SELECT JSON_OBJECT('title' VALUE title) AS data " +
                $"FROM (SELECT title FROM {_integrationTableName} WHERE id = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindWithSelectFieldsWithoutPKOnTable",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title' VALUE title) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT title FROM {_integrationTableName} ORDER BY id)"
            },
            {
                "FindByIdWithSelectFieldsWithSomePKOnTableWithCompositePK",
                $"SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'categoryName' VALUE categoryName) AS data " +
                $"FROM (SELECT categoryid, categoryName FROM {_Composite_NonAutoGenPK_TableName} WHERE categoryid = 1 AND pieceid = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindWithSelectFieldsWithSomePKOnTableWithCompositePK",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('categoryid' VALUE categoryid, 'categoryName' VALUE categoryName) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT categoryid, categoryName FROM {_Composite_NonAutoGenPK_TableName} ORDER BY categoryid ASC, pieceid ASC FETCH FIRST 101 ROWS ONLY)"
            },
            {
                "FindByIdWithSelectFieldsWithoutPKOnTableWithCompositePK",
                $"SELECT JSON_OBJECT('categoryName' VALUE categoryName) AS data " +
                $"FROM (SELECT categoryName FROM {_Composite_NonAutoGenPK_TableName} WHERE categoryid = 1 AND pieceid = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindWithSelectFieldsWithoutPKOnTableWithCompositePK",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('categoryName' VALUE categoryName) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT categoryName FROM {_Composite_NonAutoGenPK_TableName} ORDER BY categoryid ASC, pieceid ASC FETCH FIRST 101 ROWS ONLY)"
            },
            {
                "FindTestWithSelectFieldsWithoutKeyFieldsOnView",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title' VALUE title) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT title FROM {_simple_all_books} ORDER BY id)"
            },
            {
                "FindTestWithSelectFieldsWithSomeKeyFieldsOnViewWithMultipleKeyFields",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('categoryid' VALUE categoryid, 'categoryName' VALUE categoryName) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT categoryid, categoryName FROM {_simple_subset_stocks} ORDER BY categoryid, pieceid)"
            },
            {
                "FindTestWithSelectFieldsWithoutKeyFieldsOnViewWithMultipleKeyFields",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('categoryName' VALUE categoryName) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT categoryName FROM {_simple_subset_stocks} ORDER BY categoryid, pieceid)"
            },
            {
                "FindByIdTestWithSelectFieldsOnView",
                $"SELECT JSON_OBJECT('id' VALUE id, 'title' VALUE title) AS data " +
                $"FROM (SELECT id, title FROM {_simple_all_books} WHERE id = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindByIdTestWithSelectFieldsOnViewWithoutKeyFields",
                $"SELECT JSON_OBJECT('title' VALUE title) AS data " +
                $"FROM (SELECT title FROM {_simple_all_books} WHERE id = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindByIdTestWithSelectFieldsWithSomeKeyFieldsOnViewWithMultipleKeyFields",
                $"SELECT JSON_OBJECT('categoryid' VALUE categoryid, 'categoryName' VALUE categoryName) AS data " +
                $"FROM (SELECT categoryid, categoryName FROM {_simple_subset_stocks} WHERE categoryid = 1 AND pieceid = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindByIdTestWithSelectFieldsWithoutKeyFieldsOnViewWithMultipleKeyFields",
                $"SELECT JSON_OBJECT('categoryName' VALUE categoryName) AS data " +
                $"FROM (SELECT categoryName FROM {_simple_subset_stocks} WHERE categoryid = 1 AND pieceid = 1 FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFilterQueryOneGeFilterOnView",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_simple_all_books} WHERE id >= 4 ORDER BY id)"
            },
            {
                "FindTest_OrderByNotFirstQueryParam_PaginationNextLink",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT id FROM {_integrationPaginationTableName} ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)"
            },
            {
                "FindTestWithQueryStringSpaceInNamesOrderByAsc",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('ID Number' VALUE \"ID Number\", 'First Name' VALUE \"First Name\", 'Last Name' VALUE \"Last Name\") RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT \"ID Number\", \"First Name\", \"Last Name\" FROM {_integrationTableHasColumnWithSpace} ORDER BY \"ID Number\" ASC)"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('ID Number' VALUE \"ID Number\", 'First Name' VALUE \"First Name\", 'Last Name' VALUE \"Last Name\") RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT \"ID Number\", \"First Name\", \"Last Name\" FROM {_integrationTableHasColumnWithSpace} ORDER BY \"Last Name\" ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'publisher_id' VALUE publisher_id) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationTableName} ORDER BY id ASC, title ASC FETCH FIRST 1 ROWS ONLY)"
            },
            {
                "FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'name' VALUE name, 'birthdate' VALUE birthdate) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT id, name, birthdate FROM {_integrationTieBreakTable} ORDER BY birthdate ASC, name ASC, id DESC FETCH FIRST 2 ROWS ONLY)"
            },
            {
                "FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'name' VALUE name, 'birthdate' VALUE birthdate) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT id, name, birthdate FROM {_integrationTieBreakTable} " +
                $"WHERE ((birthdate > DATE '2001-01-01') OR (birthdate = DATE '2001-01-01' AND name > 'Aniruddh') OR " +
                $"(birthdate = DATE '2001-01-01' AND name = 'Aniruddh' AND id > 125)) " +
                $"ORDER BY birthdate ASC, name ASC, id ASC FETCH FIRST 2 ROWS ONLY)"
            },
            {
                "FindTestFilterForVarcharColumnWithNotMaximumSize",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('speciesid' VALUE speciesid, 'region' VALUE region, 'habitat' VALUE habitat) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationBrokenMappingTable} WHERE habitat = 'sand')"
            },
            {
                "FindTestFilterForVarcharColumnWithNotMaximumSizeAndNoTruncation",
                $"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('speciesid' VALUE speciesid, 'region' VALUE region, 'habitat' VALUE habitat) RETURNING CLOB), TO_CLOB('[]')) AS data " +
                $"FROM (SELECT * FROM {_integrationBrokenMappingTable} WHERE habitat = 'forestland')"
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
        [Ignore("Oracle test schema does not install a VPD policy on revenues.")]
        public override Task FindTestOnTableWithSecurityPolicy()
        {
            return Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle stored-procedure REST result/parameter casing is not yet aligned with the other engines.")]
        public override Task FindManyStoredProcedureTest()
        {
            return Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle stored-procedure REST result/parameter casing is not yet aligned with the other engines.")]
        public override Task FindOneStoredProcedureTestUsingParameter()
        {
            return Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle stored-procedure REST result/parameter casing is not yet aligned with the other engines.")]
        public override Task FindStoredProcedureWithNonEmptyPrimaryKeyRoute()
        {
            return Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle stored-procedure REST result/parameter casing is not yet aligned with the other engines.")]
        public override Task FindStoredProcedureWithMissingParameter()
        {
            return Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle stored-procedure REST result/parameter casing is not yet aligned with the other engines.")]
        public override Task FindStoredProcedureWithNonexistentParameter()
        {
            return Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Oracle stored-procedure REST result/parameter casing is not yet aligned with the other engines.")]
        public override Task FindApiTestForSPWithRequiredParamsInRequestBody()
        {
            return Task.CompletedTask;
        }

        [DataTestMethod]
        [DataRow(" UNION SELECT * FROM books/*")]
        [DataRow(" UNION SELECT * FROM books--")]
        [DataRow(" WHERE 1=1/*")]
        [DataRow(" WHERE 1=1--")]
        [DataRow("; SELECT * FROM information_schema.tables/*")]
        [DataRow("; SELECT * FROM information_schema.tables--")]
        [DataRow("; SELECT * FROM v$version/*")]
        [DataRow("; SELECT * FROM v$version--")]
        [DataRow("id UNION SELECT * FROM books/*")]
        [DataRow("id UNION SELECT * FROM books--")]
        [DataRow("id WHERE 1=1/*")]
        [DataRow("id WHERE 1=1--")]
        [DataRow("id; SELECT * FROM information_schema.tables/*")]
        [DataRow("id; SELECT * FROM information_schema.tables--")]
        [DataRow("id; SELECT * FROM v$version/*")]
        [DataRow("id; SELECT * FROM v$version--")]
        [DataRow("id; DROP TABLE books;/*")]
        [DataRow("id; DROP TABLE books;--")]
        public override async Task FindByIdTestWithSqlInjectionInPKRoute(string sqlInjection)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{sqlInjection}",
                queryString: $"?$select=id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: sqlInjection.Contains("/*")
                    ? "Support for url template with implicit primary key field names is not yet added."
                    : $"Parameter \"{sqlInjection}\" cannot be resolved as column \"ID\" with type \"Decimal\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        [TestMethod]
        [Ignore("Oracle SQL error message format differs from other engines.")]
        public override async Task FindByIdTestInvalidOrderByColumn()
        {
            await Task.CompletedTask;
        }

        #endregion
    }
}
