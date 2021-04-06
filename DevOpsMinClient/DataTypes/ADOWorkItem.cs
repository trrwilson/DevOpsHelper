using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOWorkItemJsonConverter))]
    public class ADOWorkItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int Revision { get; set; }
        public string State { get; set; }
        public DateTime ResolvedDate { get; set; }
        public DateTime DeploymentDate { get; set; }
        public DateTime LastHitDate { get; set; }
        public IDictionary<string, string> RawFields { get; } = new Dictionary<string, string>();
        public List<ADOWorkItemRelationInfo> Relations { get; } = new List<ADOWorkItemRelationInfo>();
        public ADOPerson AssignedTo { get; set; }

        public static class FieldNames
        {
            public static string AreaPath => "System.AreaPath";
            public static string StoryPoints => "Microsoft.VSTS.Scheduling.StoryPoints";
            public static string Title => "System.Title";
            public static string State => "System.State";
            public static string ReproSteps => "Microsoft.VSTS.TCM.ReproSteps";
            public static string ResolvedDate => "Microsoft.VSTS.Common.ResolvedDate";
            public static string History => "System.History";
            public static string DeploymentDate => "Bing.DeploymentDate";
            public static string AssignedTo => "System.AssignedTo";
            public static string AcceptedDate => "Microsoft.VSTS.CodeReview.AcceptedDate";
            public static string IncidentCount => "IcM.IncidentCount";
        }

        public class ADOWorkItemJsonConverter : ADOBaseObjectConverter<ADOWorkItem>
        {
            protected override ADOWorkItem PopulateFromToken(JToken jsonToken)
            {
                var result = new ADOWorkItem()
                {
                    Id = TokenOrDefault<int>(jsonToken, "$.id"),
                    Revision = TokenOrDefault<int>(jsonToken, "$.rev"),
                    Title = TokenOrDefault<string>(jsonToken, $"$['fields']['{FieldNames.Title}']"),
                    ResolvedDate = TokenOrDefault<DateTime>(jsonToken, $"$['fields']['{FieldNames.ResolvedDate}']"),
                    State = TokenOrDefault<string>(jsonToken, $"$['fields']['{FieldNames.State}']"),
                    DeploymentDate = TokenOrDefault<DateTime>(jsonToken, $"$['fields']['{FieldNames.DeploymentDate}']"),
                    AssignedTo = TokenOrDefault<ADOPerson>(jsonToken, $"['fields']['{FieldNames.AssignedTo}']"),
                    LastHitDate = TokenOrDefault<DateTime>(jsonToken, $"['fields']['{FieldNames.AcceptedDate}']"),
                };

                var fieldsObject = TokenOrDefault<JObject>(jsonToken, "$.fields");
                foreach (var fieldProperty in fieldsObject?.Properties())
                {
                    result.RawFields.Add(fieldProperty.Name, fieldProperty.Value.ToString());
                }

                var relationsArray = TokenOrDefault<JArray>(jsonToken, "$.relations", new JArray());
                foreach (var relationToken in relationsArray)
                {
                    result.Relations.Add(relationToken.ToObject<ADOWorkItemRelationInfo>());
                }

                return result;
            }
        }
    }
}
