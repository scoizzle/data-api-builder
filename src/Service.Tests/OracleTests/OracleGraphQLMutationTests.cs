// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests
{

    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleGraphQLMutationTests : GraphQLMutationTestBase
    {
        private static string _invalidForeignKeyError =
            "ORA-02291: integrity constraint (*.BOOK_PUBLISHER_FK) violated - parent key not found";

        #region Test Fixture Setup
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
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        #region  Positive Tests

        /// <summary>
        /// <code>Do: </code> Inserts new book and return its id and title
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task InsertMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS DATA
                FROM books table0
                WHERE id = 5001
                  AND title = 'My New Book'
                  AND publisher_id = 1234
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await InsertMutation(oracleQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'column1' VALUE table0.__column1,
                    'column2' VALUE table0.__column2
                ) AS DATA
                FROM GQLmappings table0
                WHERE __column1 = 2
                ORDER BY __column1 asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await InsertMutationWithVariablesAndMappings(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new sale item into sales table that automatically calculates the total price
        /// based on subtotal and tax.
        /// <code>Check: Calculated column is persisted successfully with correct calculated result. </code>
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForComputedColumns()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'item_name' VALUE table0.item_name,
                    'subtotal' VALUE table0.subtotal,
                    'tax' VALUE table0.tax,
                    'total' VALUE table0.total
                ) AS DATA
                FROM sales table0
                WHERE id = 5001
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await InsertMutationForComputedColumns(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code>Insert mutation using variables with default and non-default values.
        /// <code>Check: </code>If book with the expected values of the new book
        /// is present in the database and if the mutation query has returned the correct information.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS DATA
                FROM books table0
                WHERE id = 5001
                  AND title = 'My New Book'
                  AND publisher_id = 1234
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await InsertMutationWithVariables(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code>Inserts new book, recieves exception due to
        /// attempting to insert a Foreign Key that does not exist,
        /// return expected error message.
        /// <code>Check: </code>if the mutation query has returned the correct error message.
        /// </summary>
        [TestMethod]
        public async Task InsertWithInvalidForeignKey()
        {
            await InsertWithInvalidForeignKey(_invalidForeignKeyError);
        }

        /// <summary>
        /// <code>Do: </code> Update book with id and change its title
        /// <code>Check: </code> If book with expected values is present in the database
        /// and if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS DATA
                FROM books table0
                WHERE id = 1
                  AND title = 'Even Better Title'
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await UpdateMutation(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code> Updates a book and returns all columns including a column with a mapping
        /// <code>Check: </code> Mutation should be successful and the resulting book object returned should
        /// contain the column name as the mapped alias.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'column1' VALUE table0.__column1,
                    'column2' VALUE table0.__column2,
                    'column3' VALUE table0.column3
                ) AS DATA
                FROM GQLmappings table0
                WHERE __column1 = 1
                  AND column3 = 'UpdatedValue'
                ORDER BY __column1 asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await UpdateMutationWithVariablesAndMappings(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update mutation using variables with default and non-default values.
        /// <code>Check: </code>If book with the expected values is present in the database
        /// and if the mutation query has returned the correct information.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS DATA
                FROM books table0
                WHERE id = 1
                  AND title = 'Even Better Title'
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await UpdateMutationWithVariables(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code>Try to set a relationship field to null when the underlying column is not nullable.
        /// <code>Check: </code>That the mutation fails appropriately
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationForNonNullableFk()
        {
            await UpdateMutationForNonNullableFk("ORA-01407: cannot update (*.PUBLISHER_ID) to NULL");
        }

        /// <summary>
        /// <code>Do: </code>Updates a table with a single composite primary key and return its columns
        /// <code>Check: </code>If row of table with expected values is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationForCompositePrimaryKey()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'categoryid' VALUE table0.categoryid,
                    'pieceid' VALUE table0.pieceid,
                    'categoryName' VALUE table0.categoryName,
                    'piecesAvailable' VALUE table0.""piecesAvailable"",
                    'piecesRequired' VALUE table0.""piecesRequired""
                ) AS DATA
                FROM stocks table0
                WHERE categoryid = 2
                  AND pieceid = 1
                  AND ""piecesAvailable"" = 10
                ORDER BY categoryid asc, pieceid asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await UpdateMutationForCompositePrimaryKey(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code>Delete a book, should return empty response
        /// <code>Check: </code>mutation did not throw an exception and successfully deleted the book
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS DATA
                FROM books table0
                WHERE id = 1
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await DeleteMutation(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code>Delete mutation using variables with default and non-default values.
        /// <code>Check: </code>mutation did not throw an exception and successfully deleted the book
        /// </summary>
        [TestMethod]
        public async Task DeleteMutationWithVariables()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title
                ) AS DATA
                FROM books table0
                WHERE id = 1
                ORDER BY id asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await DeleteMutationWithVariables(oracleQuery);
        }

        /// <summary>
        /// <code>Do: </code> Deletes a row and uses mappings to return selected fields
        /// <code>Check: </code>Mutation is successful and deleted object returns proper mapped column names
        /// </summary>
        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string oracleQuery = @"
                SELECT JSON_OBJECT(
                    'column1' VALUE table0.__column1,
                    'column2' VALUE table0.__column2
                ) AS DATA
                FROM GQLmappings table0
                WHERE __column1 = 1
                ORDER BY __column1 asc
                FETCH FIRST 1 ROWS ONLY
            ";

            await DeleteMutationWithVariablesAndMappings(oracleQuery);
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Test all three mutation operations on a view to make sure they all fail
        /// </summary>
        [TestMethod]
        public async Task MutationOnReadOnlyView()
        {
            await MutationOnReadOnlyView("ORA-42399: cannot perform a DML operation on a read-only view");
        }

        #endregion

        /// <inheritdoc/>
        protected override string MakeQueryOnComplexView(string filterValue, int id)
        {
            return @"
                SELECT JSON_OBJECT(
                    'id' VALUE table0.id,
                    'title' VALUE table0.title,
                    'name' VALUE table0.name
                ) AS DATA
                FROM books_publishers_view_composite_insertable table0
                WHERE title = '" + filterValue + @"'
                  AND id = " + id + @"
                ORDER BY id ASC
                FETCH FIRST 1 ROWS ONLY";
        }
    }
}
