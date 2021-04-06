using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DevOpsMinClient.DataTypes
{
    public class ADOBuild
    {
        [JsonExtensionData]
        private IDictionary<string, JToken> extraJsonTokensByName = new Dictionary<string, JToken>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            if (this.extraJsonTokensByName.TryGetValue("triggerInfo", out var triggerToken))
            {
                this.PullRequestId = triggerToken.ToObject<ADOTriggerDetails>().Id;
            }
        }

        [JsonProperty("buildNumber")]
        public string Label { get; set; }

        public DateTime StartTime { get; set; }

        public int Id { get; set; }

        [JsonProperty("sourceVersion")]
        public string HeadCommit { get; set; }
        public string Reason { get; set; }


        public ADOPerson RequestedFor { get; set; }

        public ADORepositoryInfo Repository { get; set; }

        public string SourceBranch { get; set; }

        public int PullRequestId { get; set; } = 0;
    }
}
