// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{

    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Test Nested Filter for Many-One relationship.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyOne()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}series table1
                        WHERE table1.name = 'Foundation'
                        AND table0.series_id = table1.id )";

            await TestNestedFilterManyOne(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Test Nested Filter for One-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterOneMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}comics table1
                        WHERE table1.""categoryName"" = 'Tales'
                        AND table0.id = table1.series_id )";

            await TestNestedFilterOneMany(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Test Nested Filter for Many-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}book_author_link table1
                        LEFT OUTER JOIN {GetPreIndentDefaultSchema()}authors table2
                        ON table1.author_id = table2.id
                        WHERE table2.name = 'Jelte'
                        AND table0.id = table1.book_id )";

            await TestNestedFilterManyMany(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Test Nested Filter for Many-One relationship (Field is null).
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterFieldIsNull()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}stocks_price table1
                        WHERE table1.price IS NULL
                        AND table0.categoryid = table1.categoryid
                        AND table0.pieceid = table1.pieceid )";

            await TestNestedFilterFieldIsNull(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Test Nested Filter with more than one level of Nesting (level 2) (author -> book -> publisher)
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterChained()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}book_author_link table1
                        LEFT OUTER JOIN {GetPreIndentDefaultSchema()}books table2
                        ON table1.book_id = table2.id
                        LEFT OUTER JOIN {GetPreIndentDefaultSchema()}publishers table3
                        ON table2.publisher_id = table3.id
                        WHERE table3.name = 'Big Company'
                        AND table0.id = table1.author_id )";

            await TestNestedFilterChained(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Gets the default schema name.
        /// </summary>
        /// <returns></returns>
        protected override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        /// <summary>
        /// Get the sql query that retrieves all records from the database that match
        /// the predicates for the relationship nested filter test.
        /// </summary>
        public override string MakeQueryOnForRelationshipFilter(string predicates)
        {
            string query = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title,
                    'publisher_id' VALUE table0.publisher_id
                ) AS data
                FROM books table0
                WHERE " + predicates + @"
                ORDER BY table0.id asc";

            return query;
        }

        /// <summary>
        /// Gets the query to execute on database
        /// </summary>
        protected override string GetQuery(string querySuffix)
        {
            return @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS data
                FROM books table0
                " + querySuffix;
        }
    }
}
