// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OracleTests.RestApiTests.Delete
{
    [TestClass, TestCategory(TestCategory.ORACLE)]
    public class OracleDeleteApiTests : DeleteApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "DeleteOneTest",
                @"
                    SELECT JSON_OBJECT('id' VALUE id) AS data
                    FROM " + _integrationTableName + @"
                    WHERE id = 5
                "
            }
        };

        [TestMethod]
        public async Task DeleteOneInViewBadRequestTest()
        {
            string expectedErrorMessage = $"cannot delete from view";
            await base.DeleteOneInViewBadRequestTest(
                expectedErrorMessage,
                isExpectedErrorMsgSubstr: true);
        }

        #region overridden tests

        /// <inheritdoc />
        [TestMethod]
        public override async Task DeleteOneTest()
        {
            await base.DeleteOneTest();
        }

        /// <inheritdoc />
        [TestMethod]
        public override async Task DeleteOneInStocksViewBadRequestTest()
        {
            await base.DeleteOneInStocksViewBadRequestTest();
        }

        /// <inheritdoc />
        [TestMethod]
        public override async Task DeleteOneWithNonNullDefaultValue()
        {
            await base.DeleteOneWithNonNullDefaultValue();
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

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
