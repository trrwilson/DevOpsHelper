﻿using System;
using System.Runtime.CompilerServices;

namespace DevOpsMinClient.DataTypes.Details
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ADOBindableTokenAttribute : System.Attribute
    {
        public string Path { get; init; }
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
