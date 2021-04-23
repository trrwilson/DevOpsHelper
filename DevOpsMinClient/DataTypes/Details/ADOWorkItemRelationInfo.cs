using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    [JsonConverter(typeof(WorkItemRelationInfoConverter))]
    public class ADOWorkItemRelationInfo : IADOUpdateableCollectionItem
    {
        [ADOBindableToken("$.rel")]
        public string Type { get; set; }
        [ADOBindableToken("$.url")]
        public string Url { get; set; }
        [ADOBindableToken("$.attributes.name")]
        public string Name { get; set; }
        public int Index { get; set; } = -1;

        public class WorkItemRelationInfoConverter : ADOBindableTokenConverter<ADOWorkItemRelationInfo>
        {
            public override void WriteJson(JsonWriter writer, ADOWorkItemRelationInfo relation, JsonSerializer serializer)
            {
                serializer.Serialize(writer, JObject.FromObject(new
                {
                    rel = "ArtifactLink",
                    url = relation.Url,
                    attributes = new
                    {
                        name = relation.Name
                    }
                }));
            }
        }
    }
}
