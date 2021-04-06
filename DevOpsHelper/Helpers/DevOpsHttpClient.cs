using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsHelper.Helpers
{
    public class DevOpsHttpClient : HttpClient
    {
        private string personalAccessToken;
        public string PersonalAccessToken
        {
            get => this.personalAccessToken;
            set
            {
                this.personalAccessToken = value;
                this.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes($":{value}")));
            }
        }

        public DevOpsHttpClient() : base()
        {
            this.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public Task<HttpResponseMessage> GetBuildArtifactAsync(DevOpsUrl url, int buildId, string artifactName)
        {
            return this.GetAsync($"{url}/"
                + $"_apis/build/builds/{buildId}"
                + $"/artifacts?artifactName={artifactName}"
                + $"&api-version=5.0");
        }

        public Task<HttpResponseMessage> GetArtifactHierarchyAsync(DevOpsUrl url, int buildId, int artifactId)
        {
            var content = JObject.FromObject(new
            {
                contributionIds = new List<string> { "ms.vss-build-web.run-artifacts-data-provider" },
                dataProviderContext = new
                {
                    properties = new
                    {
                        artifactId = artifactId,
                        buildId = buildId,
                        sourcePage = new
                        {
                            routeValues = new
                            {
                                project = url.Project,
                            },
                        },
                    },
                },
            }).ToString();

            var contentStr = content.ToString();

            return this.PostAsync(
                $"{url.Organization}"
                    + $"/_apis/Contribution/HierarchyQuery/project/{url.Project}"
                    + $"?api-version=5.0-preview.1",
                new StringContent(contentStr, Encoding.UTF8, "application/json"));
        }
    }
}
