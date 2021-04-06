using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsHelper.Helpers
{
    public class DevOpsUrl : Uri
    {

        public string Project
        { 
            get
            {
                var uriString = this.ToString();
                return uriString.Substring(uriString.LastIndexOf('/') + 1);
            }
        }

        public string Organization
        {
            get
            {
                var uriString = this.ToString();
                return uriString.Substring(0, uriString.LastIndexOf('/'));
            }
        }

        /// <summary>
        /// Convert the base URL of the form https://something.visualstudio.com/SomeCollection/Project/ to
        /// https://something.analytics.visualstudio.com/Project
        /// </summary>
        /// <returns></returns>
        public string GetAnalyticsUrl()
        {
            var baseUrl = this.ToString();
            return baseUrl.Insert(baseUrl.IndexOf('.') + 1, "analytics.");
        }

        public DevOpsUrl(string url) : base(url)
        {
        }
    }
}
