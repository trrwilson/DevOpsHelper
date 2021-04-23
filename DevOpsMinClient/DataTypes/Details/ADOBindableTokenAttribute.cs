using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ADOBindableTokenAttribute : System.Attribute
    {
        public string Path { get; init; }
        public object OriginalGenericValue { get; set; }
        public Type ValueType { get; set; }
        public bool HideFromDiff { get; init; }
        public string UrlPath
        {
            get => this.Path.Contains("['")
                ? this.Path.Replace("$", "").Replace("['", "/").Replace("']", "")
                : this.Path.Replace("$", "").Replace(".", "/");
        }

        public ADOBindableTokenAttribute([CallerMemberName] string path = "", bool hideFromDiff = false)
        {
            this.Path = path.StartsWith("$") ? path : $"$.{path}";
            this.HideFromDiff = hideFromDiff;
        }
    }

    public class ADOBindableFieldTokenAttribute : ADOBindableTokenAttribute
    {
        public ADOBindableFieldTokenAttribute(string fieldName) : base($"$['fields']['{fieldName}']")
        {

        }
    }
}
