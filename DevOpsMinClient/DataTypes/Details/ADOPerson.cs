using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    [JsonConverter(typeof(ADOPersonConverter))]
    public class ADOPerson
    {
        [ADOBindableToken("displayName")]
        public string DisplayName { get; set; }

        [ADOBindableToken("uniqueName")]
        public string Email { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is ADOPerson person)
            {
                return this.DisplayName == person.DisplayName && this.Email == person.Email;
            }
            return false;
        }

        public override int GetHashCode() => this.DisplayName.GetHashCode() + this.Email.GetHashCode();

        public class ADOPersonConverter : ADOBindableTokenConverter<ADOPerson>
        {
            public override void WriteJson(JsonWriter writer, ADOPerson value, JsonSerializer serializer)
            {
                serializer.Serialize(writer, new
                {
                    displayName = value.DisplayName,
                    uniqueName = value.Email,
                });
            }
        }
    }
}
