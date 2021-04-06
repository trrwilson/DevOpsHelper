using DevOpsMinClient.DataTypes.Details;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    [JsonConverter(typeof(ADOBuildArtifactEntryConverter))]
    public class ADOBuildArtifactEntry
    {
        public UInt64 Id { get; set; }
        public string Path { get; set; }
        public int Size { get; set; }

        public class ADOBuildArtifactEntryConverter : ADOBaseObjectConverter<ADOBuildArtifactEntry>
        {
            protected override ADOBuildArtifactEntry PopulateFromToken(JToken jsonToken)
            {
                return new ADOBuildArtifactEntry()
                {
                    Id = TokenOrDefault<UInt64>(jsonToken, "$['artifactId']"),
                    Path = TokenOrDefault<string>(jsonToken, "$['sourcePath']"),
                    Size = TokenOrDefault<int>(jsonToken, "$['size']"),
                };
            }
        }
    }
}
