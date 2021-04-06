using DevOpsMinClient.DataTypes.QueryFilters;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClientTests
{
    public class ClientOperationTests
    {
        [Test]
        public async Task DeserializePRViaClient()
        {
            var client = new TestClient();
            client.ResponseFile = "referenceOutput/pullRequests.json";
            var prs = await client.GetPullRequestsAsync(new ADOPullRequestFilter());
            Assert.IsTrue(prs.Count > 0);
        }
    }
}
