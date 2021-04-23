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
    [JsonConverter(typeof(ADOBindableTokenConverter<ADOBuildArtifactEntry>))]
    public class ADOBuildArtifactEntry
    {
        [ADOBindableToken("$.artifactId")]
        public UInt64 Id { get; set; }
        [ADOBindableToken("$.sourcePath")]
        public string Path { get; set; }
        [ADOBindableToken("$.size")]
        public int Size { get; set; }
    }
}
