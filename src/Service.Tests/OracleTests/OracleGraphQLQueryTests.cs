// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{

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
            string oracleQuery = $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) FROM (SELECT id, title FROM books ORDER BY id asc FETCH FIRST 100 ROWS ONLY)";
            await MultipleResultQuery(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithMappings()
        {
            string oracleQuery = $"SELECT JSON_ARRAYAGG(JSON_OBJECT('column1' VALUE __column1, 'column2' VALUE __column2)) FROM (SELECT __column1, __column2 FROM GQLMappings ORDER BY __column1 asc FETCH FIRST 100 ROWS ONLY)";
            await MultipleResultQueryWithMappings(oracleQuery);
        }

        /// <summary>
        /// Gets array of results for querying a table containing computed columns.
        /// </summary>
        /// <check>rows from sales table</check>
        [TestMethod]
        public async Task MultipleResultQueryContainingComputedColumns()
        {
            string oracleQuery = @"SELECT JSON_ARRAYAGG(JSON_OBJECT(
                    'id' VALUE id,
                    'item_name' VALUE item_name,
                    'subtotal' VALUE subtotal,
                    'tax' VALUE tax,
                    'total' VALUE total
                ))
                FROM (SELECT
                    id,
                    item_name,
                    subtotal,
                    tax,
                    total
                FROM sales ORDER BY id asc FETCH FIRST 100 ROWS ONLY)";
            await MultipleResultQueryContainingComputedColumns(oracleQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string oracleQuery = $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) FROM (SELECT id, title FROM books ORDER BY id asc FETCH FIRST 100 ROWS ONLY)";
            await MultipleResultQueryWithVariables(oracleQuery);
        }

        /// <summary>
        /// Tests In operator using query variables
        /// </summary>
        [TestMethod]
        public async Task InQueryWithVariables()
        {
            string oracleQuery = $"SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE id, 'title' VALUE title)) FROM (SELECT id, title FROM books WHERE id IN (1,2) ORDER BY id asc FETCH FIRST 100 ROWS ONLY)";
            await InQueryWithVariables(oracleQuery);
        }

        /// <summary>
        /// Tests In operator with null's and empty values
        /// <checks>Runs an oracle query and then validates that the result from the oracle query graphql call matches the oracle query result.</checks>
        /// </summary>
        [TestMethod]
        public async Task InQueryWithNullAndEmptyvalues()
        {
            string oracleQuery = $"SELECT JSON_ARRAYAGG(JSON_OBJECT('string_types' VALUE string_types)) FROM (SELECT string_types FROM type_table WHERE string_types IN ('lksa;jdflasdf;alsdflksdfkldj', '', NULL))";
            await InQueryWithNullAndEmptyvalues(oracleQuery);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string oracleQuery = @"
SELECT JSON_ARRAYAGG(
    JSON_OBJECT(
        'id' VALUE table0.id,
        'title' VALUE table0.title,
        'websiteplacement' VALUE (
            SELECT JSON_OBJECT(
                'id' VALUE table1.id,
                'price' VALUE table1.price
            )
            FROM book_website_placements table1
            WHERE table0.id = table1.book_id
        )
    )
) AS data
FROM books table0
ORDER BY table0.id ASC
FETCH FIRST 100 ROWS ONLY
            ";

            await OneToOneJoinQuery(oracleQuery);
        }

        /// <summary>
        /// Test Many-To-One relationship (book -> publisher)
        /// <summary>
        [TestMethod]
        public async Task ManyToOneJoinQuery()
        {
            string oracleQuery = @"
SELECT JSON_ARRAYAGG(
    JSON_OBJECT(
        'id' VALUE table0.id,
        'title' VALUE table0.title,
        'publishers' VALUE (
            SELECT JSON_OBJECT(
                'id' VALUE table1.id,
                'name' VALUE table1.name
            )
            FROM publishers table1
            WHERE table0.publisher_id = table1.id
        )
    )
) AS data
FROM books table0
ORDER BY table0.id ASC
FETCH FIRST 100 ROWS ONLY
            ";

            await ManyToOneJoinQuery(oracleQuery);
        }

        /// <summary>
        /// Test One-To-Many relationship (publisher -> books)
        /// <summary>
        [TestMethod]
        public async Task OneToManyJoinQuery()
        {
            string oracleQuery = @"
SELECT JSON_ARRAYAGG(
    JSON_OBJECT(
        'id' VALUE table0.id,
        'name' VALUE table0.name,
        'books' VALUE (
            SELECT JSON_ARRAYAGG(
                JSON_OBJECT(
                    'id' VALUE table1.id,
                    'title' VALUE table1.title
                )
            )
            FROM books table1
            WHERE table0.id = table1.publisher_id
            ORDER BY table1.id ASC
            FETCH FIRST 100 ROWS ONLY
        )
    )
) AS data
FROM publishers table0
ORDER BY table0.id ASC
FETCH FIRST 5 ROWS ONLY
            ";

            await OneToManyJoinQuery(oracleQuery);
        }

        /// <summary>
        /// Test Many-To-Many relationship (book -> authors)
        /// <summary>
        [TestMethod]
        public async Task ManyToManyJoinQuery()
        {
            string oracleQuery = @"
SELECT JSON_ARRAYAGG(
    JSON_OBJECT(
        'id' VALUE table0.id,
        'title' VALUE table0.title,
        'authors' VALUE (
            SELECT JSON_ARRAYAGG(
                JSON_OBJECT(
                    'id' VALUE table2.id,
                    'name' VALUE table2.name,
                    'birthdate' VALUE table2.birthdate
                )
            )
            FROM book_author_link table1
            INNER JOIN authors table2 ON table1.author_id = table2.id
            WHERE table0.id = table1.book_id
            ORDER BY table2.id ASC
            FETCH FIRST 100 ROWS ONLY
        )
    )
) AS data
FROM books table0
ORDER BY table0.id ASC
FETCH FIRST 100 ROWS ONLY
            ";

            await ManyToManyJoinQuery(oracleQuery);
        }

        /// <summary>
        /// Test finding an item based on its primary key(simple and composite), whether autogenerated or not.
        /// </summary>
        [DataTestMethod]
        [DataRow("books", "id", "1", "id,title")]
        [DataRow("books", "id", "4", "id,title")]
        [DataRow("GQLmappings", "column1", "1", "column1,column2")]
        [DataRow("stocks", "categoryid,pieceid", "2,1", "categoryid,pieceid,categoryName")]
        public async Task FindByPrimaryKey(
            string entityName,
            string primaryKeyRoute,
            string primaryKeyValue,
            string queryFields)
        {
            await FindByPrimaryKey(entityName, primaryKeyRoute, primaryKeyValue, queryFields, GetQuery(entityName, primaryKeyRoute));
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS data
                FROM books table0
                WHERE id = 1
                ORDER BY id ASC
                FETCH FIRST 1 ROWS ONLY
            ";

            await QueryWithSingleColumnPrimaryKey(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithCompositeKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'categoryid' VALUE table0.categoryid,
                    'pieceid' VALUE table0.pieceid,
                    'categoryName' VALUE table0.categoryName,
                    'piecesAvailable' VALUE table0.""piecesAvailable"",
                    'piecesRequired' VALUE table0.""piecesRequired""
                ) AS data
                FROM stocks table0
                WHERE categoryid = 2 AND pieceid = 1
                ORDER BY categoryid ASC, pieceid ASC
                FETCH FIRST 1 ROWS ONLY
            ";

            await QueryWithCompositeKey(oracleQuery);
        }

        [TestMethod]
        public async Task QueryWithNullableForeignKey()
        {
            string oracleQuery = @"
                SELECT JSON_ARRAYAGG(
                    JSON_OBJECT(
                        'id' VALUE table0.id,
                        'title' VALUE table0.title,
                        'myseries' VALUE (
                            SELECT JSON_OBJECT(
                                'id' VALUE table1.id,
                                'name' VALUE table1.name
                            )
                            FROM series table1
                            WHERE table0.series_id = table1.id
                        )
                    )
                ) AS data
                FROM comics table0
                ORDER BY table0.id ASC
                FETCH FIRST 100 ROWS ONLY
            ";

            await QueryWithNullableForeignKey(oracleQuery);
        }

        #endregion

        /// <inheritdoc/>
        protected override string GetQuery(string entityName, string primaryKeyRoute)
        {
            string[] primaryKeys = primaryKeyRoute.Split(',');
            string whereClause;

            if (entityName == "GQLmappings")
            {
                whereClause = "WHERE __column1 = 1";
            }
            else if (primaryKeys.Length > 1)
            {
                whereClause = "WHERE categoryid = 2 AND pieceid = 1";
            }
            else
            {
                whereClause = "WHERE id = 1";
            }

            return @$"
                SELECT JSON_OBJECT('*' VALUE '*') AS data
                FROM {entityName} table0
                {whereClause}
                ORDER BY {primaryKeys[0]} ASC
                FETCH FIRST 1 ROWS ONLY
            ";
        }
    }
}
