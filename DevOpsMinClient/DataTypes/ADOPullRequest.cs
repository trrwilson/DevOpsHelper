using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOPullRequestConverter))]
    public class ADOPullRequest
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public string LastSourceCommitId { get; set; }
        public string LastTargetCommitId { get; set; }
        public ADOPerson CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public ADORepositoryInfo Repository { get; set; }

        public class ADOPullRequestConverter : ADOBaseObjectConverter<ADOPullRequest>
        {
            protected override ADOPullRequest PopulateFromToken(JToken jsonToken)
            {

                return new ADOPullRequest()
                {
                    Id = TokenOrDefault<int>(jsonToken, "$.pullRequestId"),
                    Title = TokenOrDefault<string>(jsonToken, "$.title"),
                    Status = TokenOrDefault<string>(jsonToken, "$.status"),
                    CreatedBy = TokenOrDefault<ADOPerson>(jsonToken, "$.createdBy"),
                    CreationDate = TokenOrDefault<DateTime>(jsonToken, "$.creationDate"),
                    Repository = TokenOrDefault<ADORepositoryInfo>(jsonToken, "$.repository"),
                    LastSourceCommitId = TokenOrDefault<string>(jsonToken, "$.lastMergeCommit.commitId"),
                    LastTargetCommitId = TokenOrDefault<string>(jsonToken, "$.lastMergeTargetCommit.commitId")
                };
            }
        }
    }
}
