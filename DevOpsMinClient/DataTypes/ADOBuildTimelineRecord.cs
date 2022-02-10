using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using System;
using System.Diagnostics;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOBindableTokenConverter<ADOBuildTimelineRecord>))]
    [DebuggerDisplay("Name = {Name}")]
    public class ADOBuildTimelineRecord
    {
        [ADOBindableToken("$.type")]
        public string Type { get; set; }
        [ADOBindableToken("$.name")]
        public string Name { get; set; }
        [ADOBindableToken("$.startTime")]
        public DateTime Start { get; set; }
        [ADOBindableToken("$.finishTime")]
        public DateTime Finish { get; set; }
        [ADOBindableToken("$.state")]
        public string State { get; set; }
        [ADOBindableToken("$.result")]
        public string Result { get; set; }
        [ADOBindableToken("$.queueId")]
        public int QueueId { get; set; }
        [ADOBindableToken("$.parentId")]
        public string ParentId { get; set; }
    }
}
