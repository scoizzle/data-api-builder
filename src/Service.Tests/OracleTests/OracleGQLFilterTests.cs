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
        protected static string DEFAULT_SCHEMA = "SYSTEM";

        /// <summary>
        /// Set the database engine for the tests.
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.ORACLE;
            await InitializeTestFixture();
        }

        [TestMethod]
        [Ignore("Oracle sorts strings with binary collation, producing different order than SQL Server.")]
        public override async Task TestGetNullIntFields()
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Test Nested Filter for Many-One relationship.
        /// </summary>
        [TestMethod]
        new public async Task TestPassingVariablesToFilter()
        {
            Assert.Inconclusive("Oracle maps numeric id columns to GraphQL Decimal, so Int! variables are rejected.");
        }

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
        /// Test Nested Filter for One-Many relationship.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterOneMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}comics table1
                        WHERE table1.title = 'Cinderella'
                        AND table1.series_id = table0.id )";

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
                        FROM {GetPreIndentDefaultSchema()}authors table1
                        INNER JOIN {GetPreIndentDefaultSchema()}book_author_link table3
                        ON table3.author_id = table1.id
                        WHERE table1.name = 'Aaron'
                        AND table3.book_id = table0.id )";

            await TestNestedFilterManyMany(existsPredicate);
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
                        AND table1.categoryid = table0.categoryid
                        AND table1.pieceid = table0.pieceid)";

            await TestNestedFilterFieldIsNull(existsPredicate, roleName: "authenticated");
        }

        [TestMethod]
        public async Task TestStringFiltersEqWithMappings()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'column1' VALUE column1, 'column2' VALUE column2
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT ""__column1"" AS column1, ""__column2"" AS column2
                    FROM GQLmappings
                    WHERE ""__column2"" = 'Filtered Record'
                    ORDER BY ""__column1"" ASC
                ) table0";

            await TestStringFiltersEqWithMappings(oracleQuery);
        }

        [TestMethod]
        public async Task TestStringFiltersINWithMappings()
        {
            string oracleQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'column1' VALUE column1, 'column2' VALUE column2
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT ""__column1"" AS column1, ""__column2"" AS column2
                    FROM GQLmappings
                    WHERE ""__column2"" IN ('Filtered Record')
                    ORDER BY ""__column1"" ASC
                ) table0";

            await TestStringFiltersINWithMappings(oracleQuery);
        }

        [TestMethod]
        public async Task TestNestedFilterWithinNestedFilter()
        {
            string defaultSchema = GetPreIndentDefaultSchema();
            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}authors table1
                        INNER JOIN {defaultSchema}book_author_link table6
                        ON table6.book_id = table0.id
                        WHERE (EXISTS (SELECT 1 FROM {defaultSchema}books table2
                                       INNER JOIN {defaultSchema}book_author_link table4
                                       ON table4.author_id = table1.id
                                       WHERE table2.title LIKE 'Awesome'
                                       AND table4.book_id = table2.id)
                                       AND table1.name = 'Aaron') AND table6.author_id = table1.id)";

            await TestNestedFilterWithinNestedFilter(existsPredicate, roleName: "authenticated");
        }

        [TestMethod]
        public async Task TestNestedFilterWithAnd()
        {
            string defaultSchema = GetPreIndentDefaultSchema();
            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}authors table1
                        INNER JOIN {defaultSchema}book_author_link table3
                        ON table3.book_id = table0.id
                        WHERE table1.name = 'Aniruddh'
                        AND table3.author_id = table1.id)
                        AND EXISTS (SELECT 1 FROM {defaultSchema}publishers table4
                                    WHERE table4.name = 'Small Town Publisher'
                                    AND table0.publisher_id = table4.id)";

            await TestNestedFilterWithAnd(existsPredicate, roleName: "authenticated");
        }

        [TestMethod]
        public async Task TestNestedFilterWithOr()
        {
            string defaultSchema = GetPreIndentDefaultSchema();
            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}publishers table1
                    WHERE table1.name = 'TBD Publishing One'
                    AND table0.publisher_id = table1.id)
                OR EXISTS( SELECT 1 FROM {defaultSchema}authors table3
                           INNER JOIN {defaultSchema}book_author_link table5
                           ON table5.book_id = table0.id
                           WHERE table3.name = 'Aniruddh'
                           AND table5.author_id = table3.id)";

            await TestNestedFilterWithOr(existsPredicate, roleName: "authenticated");
        }

        [DataTestMethod]
        [DataRow("{ title: { endsWith: \"_CONN\" } }", "%\\_CONN")]
        [DataRow("{ title: { contains: \"%_\" } }", "%\\%\\_%")]
        [DataRow("{ title: { endsWith: \"%_CONN\" } }", "%\\%\\_CONN")]
        [DataRow("{ title: { startsWith: \"CONN%\" } }", "CONN\\%%")]
        public new async Task TestStringFiltersWithSpecialCharacters(string dynamicFilter, string dbFilterInput)
        {
            string oracleQuery = $@"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title' VALUE title) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM (
                    SELECT title FROM books
                    WHERE title LIKE '{dbFilterInput}' ESCAPE '\'
                    ORDER BY title ASC
                ) table0";

            await base.TestStringFiltersWithSpecialCharacters(dynamicFilter, oracleQuery);
        }

        [TestMethod]
        public async Task TestNestedFilterWithOrAndIN()
        {
            string defaultSchema = GetPreIndentDefaultSchema();
            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}publishers table1
                    WHERE table1.name IN ('TBD Publishing One')
                    AND table0.publisher_id = table1.id)
                OR EXISTS( SELECT 1 FROM {defaultSchema}authors table3
                           INNER JOIN {defaultSchema}book_author_link table5
                           ON table5.book_id = table0.id
                           WHERE table3.name IN ('Aniruddh')
                           AND table5.author_id = table3.id)";

            await TestNestedFilterWithOrAndIN(existsPredicate, roleName: "authenticated");
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
        /// the predicates for filtering.
        /// </summary>
        protected override string MakeQueryOn(
            string table,
            List<string> queriedColumns,
            string predicate,
            string schema = "",
            List<string> pkColumns = null)
        {
            string jsonObjectParts = string.Join(", ",
                queriedColumns.Select(column => $"'{column}' VALUE table0.{column}"));

            if (pkColumns == null)
            {
                pkColumns = new() { "id" };
            }

            string orderBy = string.Join(", ", pkColumns.Select(column => $"table0.{column}"));
            string schemaPrefix = string.IsNullOrEmpty(schema) ? string.Empty : $"{schema}.";

            return $@"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    {jsonObjectParts}
                ) RETURNING CLOB), TO_CLOB('[]')) AS data
                FROM {schemaPrefix}{table} table0
                WHERE {predicate}
                ORDER BY {orderBy} ASC";
        }
    }
}
