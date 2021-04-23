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
    [JsonConverter(typeof(ADOBindableTokenConverter<ADOPullRequest>))]
    public class ADOPullRequest
    {
        [ADOBindableToken("$.pullRequestId")]
        public int Id { get; set; }
        [ADOBindableToken("$.title")]
        public string Title { get; set; }
        [ADOBindableToken("$.status")]
        public string Status { get; set; }
        [ADOBindableToken("$.lastMergeCommit.commitId")]
        public string LastSourceCommitId { get; set; }
        [ADOBindableToken("$.lastMergeTargetCommit.commitId")]
        public string LastTargetCommitId { get; set; }
        [ADOBindableToken("$.createdBy")]
        public ADOPerson CreatedBy { get; set; }
        [ADOBindableToken("$.creationDate")]
        public DateTime CreationDate { get; set; }
        [ADOBindableToken("$.repository")]
        public ADORepositoryInfo Repository { get; set; }
    }
}
