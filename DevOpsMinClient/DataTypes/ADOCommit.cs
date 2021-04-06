using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DevOpsMinClient.DataTypes
{
    public class ADOCommit
    {
        [JsonProperty("commitId")]
        public string Id { get; set; }

        [JsonProperty("parents")]
        public List<string> ParentCommitIds { get; set; }

        public CommitterInfo Committer { get; set; }

        public string Comment { get; set; }

        public class CommitterInfo
        {
            public string Name { get; set; }
            public DateTime Date { get; set; }
        }
    }
}
