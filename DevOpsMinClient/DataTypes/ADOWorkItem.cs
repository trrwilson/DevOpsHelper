using DevOpsMinClient.DataTypes.Details;
using DevOpsMinClient.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOWorkItemJsonConverter))]
    public class ADOWorkItem
    {
        [ADOBindableToken("$.id")]
        public int Id { get; set; }
        [ADOBindableToken("$.rev")]
        public int Revision { get; set; }
        [ADOBindableFieldToken("System.Title")]
        public string Title { get; set; }
        [ADOBindableFieldToken("System.State")]
        public string State { get; set; }
        [ADOBindableFieldToken("System.AreaPath")]
        public string AreaPath { get; set; }
        [ADOBindableFieldToken("System.IterationPath")]
        public string IterationPath { get; set; }
        [ADOBindableFieldToken("Microsoft.VSTS.TCM.ReproSteps")]
        public string ReproSteps { get; set; }
        [ADOBindableFieldToken("Microsoft.VSTS.Common.ResolvedBy")]
        public ADOPerson ResolvedBy { get; set; }
        [ADOBindableFieldToken("Microsoft.VSTS.Common.ResolvedDate")]
        public DateTime ResolvedDate { get; set; }
        [ADOBindableFieldToken("Bing.DeploymentDate")]
        public DateTime DeploymentDate { get; set; }
        [ADOBindableFieldToken("Microsoft.VSTS.CodeReview.AcceptedDate")]
        public DateTime LastHitDate { get; set; }
        [ADOBindableToken("$.relations")]
        public List<ADOWorkItemRelationInfo> Relations { get; set; }
        [ADOBindableFieldToken("IcM.IncidentCount")]
        public int IncidentCount { get; set; }
        [ADOBindableFieldToken("System.History")]
        public string History { get; set; }
        [ADOBindableToken("$", hideFromDiff:true)]
        public JObject originalSerializedJson { get; set; }
        [ADOBindableFieldToken("System.AssignedTo")]
        public ADOPerson AssignedTo { get; set; }
        [ADOBindableFieldToken("System.Tags")]
        public string Tags { get; set; }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext _)
        {
            for (int i = 0; i < this.Relations.Count; i++)
            {
                this.Relations[i].Index = i;
            }
        }

        public JsonPatchBuilder GenerateDeltaPatch()
        {
            var original = originalSerializedJson.ToObject<ADOWorkItem>();
            original ??= new ADOWorkItem();
            var result = new JsonPatchBuilder(this);
            result += JsonPatchBuilder.GenerateDeltaPatch(original, this);
            return result;
        }

        private class ADOWorkItemJsonConverter : ADOBindableTokenConverter<ADOWorkItem>
        {
            public override ADOWorkItem ReadJson(JsonReader reader, Type _, ADOWorkItem __, bool ___, JsonSerializer ____)
            {
                var result = base.ReadJson(reader, _, __, ___, ____);
                result.Relations ??= new List<ADOWorkItemRelationInfo>();
                for (int i = 0; i < result.Relations.Count; i++)
                {
                    result.Relations[i].Index = i;
                }
                return result;
            }
        }
    }
}
