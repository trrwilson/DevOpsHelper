using Newtonsoft.Json;

namespace DevOpsMinClient.DataTypes.Details
{
    public class ADOTriggerDetails
    {
        [JsonProperty("pr.number")]
        public int Id { get; set; }
    }
}
