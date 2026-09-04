// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation for Oracle.
    /// </summary>
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLQueryTests : GraphQLQueryTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        #region Tests

        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (
                    SELECT id, title FROM books ORDER BY id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQuery(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryContainingComputedColumns()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'item_name' VALUE item_name,
                        'subtotal' VALUE subtotal, 'tax' VALUE tax, 'total' VALUE total)
                ) AS data
                FROM (
                    SELECT id, item_name, subtotal, tax, total FROM sales ORDER BY id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQueryContainingComputedColumns(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (
                    SELECT id, title FROM books ORDER BY id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQueryWithVariables(oracleQuery);
        }

        [TestMethod]
        public async Task InQueryWithVariables()
        {
            Assert.Inconclusive("Oracle maps numeric id columns to GraphQL Decimal, so [Int]! variables are rejected.");
        }

        [TestMethod]
        public async Task InQueryWithNullAndEmptyvalues()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('string_types' VALUE string_types)
                ) AS data
                FROM (
                    SELECT string_types FROM type_table
                    WHERE string_types IN ('lksa;jdflasdf;alsdflksdfkldj', ' ', NULL)
                )";
            await InQueryWithNullAndEmptyvalues(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithMappings()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('column1' VALUE ""__column1"", 'column2' VALUE ""__column2"")
                ) AS data
                FROM (
                    SELECT ""__column1"", ""__column2"" FROM GQLmappings ORDER BY ""__column1"" ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await MultipleResultQueryWithMappings(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('title' VALUE title) AS data
                FROM (SELECT title FROM books WHERE id = 2 FETCH FIRST 1 ROWS ONLY)";
            await QueryWithSingleColumnPrimaryKey(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('column1' VALUE ""__column1"") AS data
                FROM (SELECT ""__column1"" FROM GQLmappings WHERE ""__column1"" = 1 FETCH FIRST 1 ROWS ONLY)";
            await QueryWithSingleColumnPrimaryKeyAndMappings(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT('content' VALUE content) AS data
                FROM (SELECT content FROM reviews WHERE id = 568 AND book_id = 1 FETCH FIRST 1 ROWS ONLY)";
            await QueryWithMultipleColumnPrimaryKey(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithNullableForeignKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'title' VALUE title,
                    'myseries' VALUE (
                        SELECT JSON_OBJECT('name' VALUE name)
                        FROM series WHERE id = comics.series_id
                    )
                ) AS data
                FROM comics WHERE id = 1";
            await QueryWithNullableForeignKey(oracleQuery);
        }

        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY title DESC FETCH FIRST 100 ROWS ONLY)";
            await TestOrderByInListQuery(oracleQuery);
        }

        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'content' VALUE content)
                ) AS data
                FROM (SELECT id, content FROM reviews ORDER BY content ASC, id DESC FETCH FIRST 100 ROWS ONLY)";
            await TestOrderByInListQueryOnCompPkType(oracleQuery);
        }

        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(
                    JSON_OBJECT('book_id' VALUE id, 'book_title' VALUE title) RETURNING CLOB
                ), TO_CLOB('[]')) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 2 ROWS ONLY)";
            await TestAliasSupportForGraphQLQueryFields(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(
                    JSON_OBJECT('book_id' VALUE id, 'title' VALUE title) RETURNING CLOB
                ), TO_CLOB('[]')) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 2 ROWS ONLY)";
            await TestSupportForMixOfRawDbFieldFieldAndAlias(oracleQuery);
        }

        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title, 'issue_number' VALUE issue_number)
                ) AS data
                FROM (SELECT id, title, issue_number FROM foo.magazines ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestQueryingTypeWithNullableIntFields(oracleQuery);
        }

        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'username' VALUE username)
                ) AS data
                FROM (SELECT id, username FROM website_users FETCH FIRST 100 ROWS ONLY)";
            await TestQueryingTypeWithNullableStringFields(oracleQuery);
        }

        [TestMethod]
        public async Task TestQueryingTypeWithNullableDateTimeFields()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('datetime_types' VALUE datetime_types)
                ) AS data
                FROM (SELECT datetime_types FROM type_table ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestQueryingTypeWithNullableDateTimeFields(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryWithExplicitlyNullArguments()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestQueryWithExplicitlyNullArguments(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryOnBasicView()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books_view_all ORDER BY id ASC FETCH FIRST 5 ROWS ONLY)";
            await TestQueryOnBasicView(oracleQuery);
        }

        [TestMethod]
        public virtual async Task TestQueryOnCompositeView()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'name' VALUE name)
                ) AS data
                FROM (SELECT id, name FROM books_publishers_view_composite ORDER BY id ASC FETCH FIRST 5 ROWS ONLY)";
            await TestQueryOnCompositeView(oracleQuery);
        }

        [TestMethod]
        public override async Task QueryWithNullResult()
        {
            await base.QueryWithNullResult();
        }

        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY title DESC, id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestNullFieldsInOrderByAreIgnored(oracleQuery);
        }

        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT('id' VALUE id, 'title' VALUE title)
                ) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(oracleQuery);
        }

        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title,
                    'websiteplacement' VALUE websiteplacement
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT b.id, b.title,
                        (SELECT JSON_OBJECT('price' VALUE p.price)
                         FROM book_website_placements p
                         WHERE p.book_id = b.id
                         FETCH FIRST 1 ROWS ONLY) AS websiteplacement
                    FROM books b
                    ORDER BY b.id ASC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await OneToOneJoinQuery(oracleQuery);
        }

        [TestMethod]
        public async Task InFilterOneToOneJoinQuery()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'id' VALUE id, 'title' VALUE title,
                    'websiteplacement' VALUE websiteplacement
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT b.id, b.title,
                        (SELECT JSON_OBJECT('price' VALUE p.price, 'book_id' VALUE p.book_id)
                         FROM book_website_placements p
                         WHERE p.book_id = b.id
                         FETCH FIRST 1 ROWS ONLY) AS websiteplacement
                    FROM books b
                    WHERE b.title IN ('Awesome book', 'Also Awesome book')
                      AND EXISTS (
                          SELECT 1 FROM book_website_placements p2
                          WHERE p2.book_id IN (1, 2) AND p2.book_id = b.id
                      )
                    ORDER BY b.id DESC
                    FETCH FIRST 100 ROWS ONLY
                )";
            await InFilterOneToOneJoinQuery(oracleQuery);
        }

        [TestMethod]
        public async Task OneToOneJoinQueryWithMappedFieldNamesInRelationship()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'fancyName' VALUE fancyName, 'fungus' VALUE fungus
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT t.species AS fancyName,
                        (SELECT JSON_OBJECT('habitat' VALUE f.habitat)
                         FROM fungi f
                         WHERE f.habitat = t.species
                         FETCH FIRST 1 ROWS ONLY) AS fungus
                    FROM trees t
                    FETCH FIRST 100 ROWS ONLY
                )";
            await OneToOneJoinQueryWithMappedFieldNamesInRelationship(oracleQuery);
        }

        [TestMethod]
        public async Task TestSettingOrderByOrderUsingVariable()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT id, title FROM books ORDER BY id DESC FETCH FIRST 4 ROWS ONLY)";
            await TestSettingOrderByOrderUsingVariable(oracleQuery);
        }

        [TestMethod]
        public async Task TestSettingComplexArgumentUsingVariables()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT id, title FROM books ORDER BY id ASC FETCH FIRST 100 ROWS ONLY)";
            await base.TestSettingComplexArgumentUsingVariables(oracleQuery);
        }

        [DataTestMethod]
        [DataRow(null, null, 1113, "Real Madrid")]
        [DataRow(new string[] { "new_club_id" }, new string[] { "id" }, 1111, "Manchester United")]
        public async Task TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
            string[] sourceFields,
            string[] targetFields,
            int club_id,
            string club_name)
        {
            await TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
                sourceFields,
                targetFields,
                club_id,
                club_name,
                DatabaseType.Oracle,
                TestCategory.ORACLE);
        }

        [TestMethod]
        public async Task TestSupportForAggregationsWithAliases()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'max' VALUE max_cat, 'max_price' VALUE max_price,
                    'min_price' VALUE min_price, 'avg_price' VALUE avg_price,
                    'sum_price' VALUE sum_price
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT MAX(categoryid) AS max_cat, MAX(price) AS max_price,
                           MIN(price) AS min_price, AVG(price) AS avg_price,
                           SUM(price) AS sum_price
                    FROM stocks_price
                )";
            await TestSupportForAggregationsWithAliases(oracleQuery);
        }

        [TestMethod]
        [Ignore("Oracle numeric aggregation types differ from SQL Server (Decimal vs Int).")]
        public async Task TestSupportForGroupByAggregationsWithAliases()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'max' VALUE max_cat, 'max_price' VALUE max_price,
                    'min_price' VALUE min_price, 'avg_price' VALUE avg_price,
                    'sum_price' VALUE sum_price, 'count' VALUE cnt
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT MAX(categoryid) AS max_cat, MAX(price) AS max_price,
                           MIN(price) AS min_price, AVG(price) AS avg_price,
                           SUM(price) AS sum_price, COUNT(categoryid) AS cnt
                    FROM stocks_price
                    GROUP BY categoryid
                    ORDER BY categoryid
                )";
            await TestSupportForGroupByAggregationsWithAliases(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForMinAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('min_price' VALUE min_price) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT MIN(price) AS min_price FROM stocks_price)";
            await TestSupportForMinAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForMaxAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('max_price' VALUE max_price) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT MAX(price) AS max_price FROM stocks_price)";
            await TestSupportForMaxAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForAvgAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('avg_price' VALUE avg_price) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT AVG(price) AS avg_price FROM stocks_price)";
            await TestSupportForAvgAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForSumAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('sum_price' VALUE sum_price) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT SUM(price) AS sum_price FROM stocks_price)";
            await TestSupportForSumAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForCountAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('count_categoryid' VALUE cnt) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT COUNT(categoryid) AS cnt FROM stocks_price)";
            await TestSupportForCountAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForHavingAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('max' VALUE max_id) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (SELECT MAX(id) AS max_id FROM publishers HAVING MAX(id) > 2346)";
            await TestSupportForHavingAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForGroupByHavingAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('sum_price' VALUE sum_price) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT SUM(price) AS sum_price FROM stocks_price
                    GROUP BY categoryid, pieceid
                    HAVING SUM(price) > 50
                )";
            await TestSupportForGroupByHavingAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForGroupByHavingFieldsAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'categoryid' VALUE categoryid, 'pieceid' VALUE pieceid,
                    'sum_price' VALUE sum_price, 'count_piece' VALUE count_piece
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT categoryid, pieceid, SUM(price) AS sum_price, COUNT(pieceid) AS count_piece
                    FROM stocks_price
                    GROUP BY categoryid, pieceid
                    HAVING SUM(price) > 50 AND COUNT(pieceid) <= 100
                )";
            await TestSupportForGroupByHavingFieldsAggregation(oracleQuery);
        }

        [TestMethod]
        public async Task TestSupportForGroupByNoAggregation()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'categoryid' VALUE categoryid, 'pieceid' VALUE pieceid
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT categoryid, pieceid FROM stocks_price
                    GROUP BY categoryid, pieceid
                    ORDER BY categoryid, pieceid
                )";
            await TestSupportForGroupByNoAggregation(oracleQuery);
        }

        [TestMethod]
        [Ignore("Oracle sorts strings with binary collation, producing different order than SQL Server.")]
        public new async Task TestGetNullIntFields()
        {
            await Task.CompletedTask;
        }

        #endregion
    }
}
