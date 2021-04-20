using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.QueryFilters;
using DevOpsMinClient.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace DevOpsMinClient
{
    public class ADOClient : IDisposable
    {
        private string personalAccessToken;
        private HttpClient baseClient = new HttpClient();
        private string baseUrl;
        public event Action<string, string> ErrorReceived;

        public string PersonalAccessToken
        {
            get => this.personalAccessToken;
            set
            {
                this.personalAccessToken = value;
                this.baseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes($":{value}")));
            }
        }

        private async Task<JObject> GetResponseJsonAsync(HttpResponseMessage httpResponse)
        {
            var responseText = await httpResponse.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(responseText);
            return responseJson;
        }

        public ADOClient(string devOpsUrl)
        {
            this.baseUrl = devOpsUrl.EndsWith('/') ? devOpsUrl.Substring(0, devOpsUrl.Length - 1) : devOpsUrl;
            this.baseClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<Dictionary<string, ADOBuildArtifactEntry>> GetBuildArtifactInfoAsync(
            int buildId,
            string artifactPath)
        {
            var result = new Dictionary<string, ADOBuildArtifactEntry>();
            var traversal = artifactPath.Split('/');
            if (traversal.Length == 0)
            {
                return result;
            }

            var artifactObject = await this.GetBuildArtifactAsync(buildId, traversal.FirstOrDefault());
            if (!artifactObject.TryGetValue("id", out var artifactIdToken))
            {
                return result;
            }

            var hierarchyObject = await this.GetArtifactHierarchyObjectAsync(buildId, artifactIdToken.Value<int>(), artifactPath);

            var itemsToken = hierarchyObject.SelectToken(
                "$['dataProviders']['ms.vss-build-web.run-artifacts-data-provider']['items']") as JArray;
            foreach (var filterLevel in traversal.Skip(1))
            {
                var nextObject = itemsToken?.FirstOrDefault(item =>
                {
                    var itemObject = item as JObject;
                    return itemObject.TryGetValue("name", out var itemObjectNameToken)
                        && itemObjectNameToken.Value<string>().EndsWith($"/{filterLevel}");
                });
                itemsToken = nextObject?.Value<JArray>("items");
            }

            static void AddItemsToDictionary(JArray items, IDictionary<string, ADOBuildArtifactEntry> dictionary)
            {
                if (items == null) return;

                foreach (var item in items)
                {
                    var entry = item.ToObject<ADOBuildArtifactEntry>();
                    dictionary[entry.Path] = entry;
                    AddItemsToDictionary(item.Value<JArray>("items"), dictionary);
                }
            }

            AddItemsToDictionary(itemsToken as JArray, result);

            return result;
        }

        public async Task<JObject> GetBuildArtifactAsync(int buildId, string artifactName)
        {
            var response = await this.GetAsync(
                $"{this.baseUrl}/"
                    + $"_apis/build/builds/{buildId}"
                    + $"/artifacts?artifactName={artifactName}"
                    + $"&api-version=5.0");
            var jsonResponse = JObject.Parse(response);

            return jsonResponse;
        }

        public async Task<string> GetArtifactDownloadUrlAsync(int buildId, string path)
        {
            if (!RegexExtensions.TryMatch(path, "/?([^/]*)((/.*)*)", out var match))
            {
                return string.Empty;
            }
            var artifactName = match.Groups[1].Value;
            var artifactPath = match.Groups.Count > 2 ? match.Groups[2].Value : null;


            var artifactInfoJson = await this.GetBuildArtifactAsync(buildId, artifactName);
            if (!artifactInfoJson.TryGetValue<int>("id", out var artifactId))
            {
                return string.Empty;
            }

            var jsonDownloadObject = await this.GetArtifactHierarchyObjectAsync(buildId, artifactId, path);
            return jsonDownloadObject.SelectTokenValueOrDefault<string>(
                "$['dataProviders']['ms.vss-build-web.run-artifacts-download-data-provider']['downloadUrl']");
        }

        public async Task<JObject> GetArtifactHierarchyObjectAsync(int buildId, int artifactId, string downloadPath = null)
        {
            downloadPath = RegexExtensions.TryMatch(downloadPath, "/?[^/]*(/.*)", out var match)
                ? match.Groups[1].Value : downloadPath;

            var responseText = await this.PostAsync(
                $"{this.GetUrlWithoutProject()}"
                    + $"/_apis/Contribution/HierarchyQuery/project/{this.GetProjectFromUrl()}"
                    + $"?api-version=5.0-preview.1",
                new
                {
                    contributionIds = new List<string> { 
                        !string.IsNullOrEmpty(downloadPath) ? "ms.vss-build-web.run-artifacts-download-data-provider"
                        : "ms.vss-build-web.run-artifacts-data-provider" },
                    dataProviderContext = new
                    {
                        properties = new
                        {
                            artifactId,
                            buildId,
                            compressDownload = true,
                            path = downloadPath,
                            sourcePage = new
                            {
                                routeValues = new
                                {
                                    project = this.GetProjectFromUrl(),
                                },
                            },
                        },
                    },
                });
            var jsonResponse = JObject.Parse(responseText);

            return jsonResponse;
        }

        public async Task<ADOBuild> GetAssociatedBuildAsync(ADOPullRequest pullRequest)
        {
            var responseText = await this.PostAsync(
                $"{this.GetUrlWithoutProject()}"
                    + $"/_apis/Contribution/HierarchyQuery/project/{this.GetProjectFromUrl()}"
                    + $"?api-version=5.0-preview.1",
                new
                {
                    contributionIds = new List<string> { "ms.vss-code-web.pr-detail-data-provider" },
                    dataProviderContext = new
                    {
                        properties = new
                        {
                            pullRequestId = pullRequest.Id,
                            repositoryId = pullRequest.Repository.Id,
                            types = 192,
                            sourcePage = new
                            {
                                routeValues = new
                                {
                                    project = this.GetProjectFromUrl()
                                }
                            }
                        }
                    }
                });
            var jsonResponse = JObject.Parse(responseText);
            var buildIdToken = jsonResponse.SelectToken($"$..['buildId']");

            return buildIdToken is null || buildIdToken.Type != JTokenType.Integer
                ? null : await this.GetBuildAsync(buildIdToken.Value<int>());
        }

        public async Task<List<ADOWorkItem>> GetWorkItemsRelatedToTestNameAsync(string testName)
        {
            var url = $"{this.baseUrl}"
                + $"/_apis/test/Results/WorkItems"
                + $"?api-version=5.1-preview.1"
                + $"&workItemCategory=Microsoft.BugCategory"
                + $"&automatedTestName={HttpUtility.UrlEncode(testName)}";
            var response = await this.GetAsync(url);
            var jsonResponse = JObject.Parse(response);
            var workItemResults = await Task.WhenAll(jsonResponse["value"]
                .Select(async workItemReference =>
                {
                    var workItemUrl = $"{this.baseUrl}"
                        + $"/_apis/wit/workItems?ids={workItemReference.Value<int>("id")}&$expand=1";
                    var workItemResponse = await this.GetAsync(workItemUrl);
                    var workItemJson = JObject.Parse(workItemResponse);
                    return workItemJson["value"].First().ToObject<ADOWorkItem>();
                }));
            return workItemResults
                .GroupBy(workItem => workItem.Id)
                .Select(idWorkItemPair => idWorkItemPair.First())
                .ToList();
        }

        public async Task<JToken> GetTestResultDetailAsync(int testRunId, int testResultId)
        {
            var url = $"{this.baseUrl}/_apis/test/Runs/{testRunId}/Results/{testResultId}?api-version=5.1";
            var response = await this.GetAsync(url);
            var resultDetail = JObject.Parse(response);
            return resultDetail;
        }

        public async Task<List<ADOTestRun>> GetTestRunsAsync(ADOTestQueryFilter filter)
        {
            var url = $"{this.GetAnalyticsUrl()}"
                    + $"/_odata/v4.0-preview/TestResults?"
                    + $"&$apply={filter}"
                    ; // + $"&$select=Outcome, TestRunId, TestResultId, StartedDate";
            var responseText = await this.GetAsync(url);
            var resultJson = JObject.Parse(responseText);
            var resultTokens = (await Task.WhenAll(resultJson["value"]
                .Select(async token =>
                {
                    var testRun = token.ToObject<ADOTestRun>();
                    var testRunDetail = (JObject)await GetTestResultDetailAsync(testRun.RunId, testRun.ResultId);
                    testRun.Name = testRunDetail.SelectToken("$.testCase.name").Value<string>();
                    testRun.FullName = testRunDetail.SelectToken("$.automatedTestName").Value<string>();
                    return testRun;
                })))
                .OrderByDescending(result => result.RunId)
                .ToList();
            return resultTokens;
        }

        protected virtual async Task<string> GetAsync(string url)
        {
            var httpResult = await this.baseClient.GetAsync(url);
            await this.CheckResponseAsync(httpResult, "get");
            var textResult = await httpResult.Content.ReadAsStringAsync();
            return textResult;
        }

        protected virtual async Task<string> PostAsync(
            string url,
            string payload,
            string contentType = "application/json")
        {
            var response = await this.baseClient.PostAsync(
                url,
                payload != null ? new StringContent(payload, Encoding.UTF8, contentType) : null);
            await this.CheckResponseAsync(response, "post");
            var responseText = await response.Content.ReadAsStringAsync();
            return responseText;
        }

        protected virtual Task<string> PostAsync(string url, JsonPatchBuilder patches)
            => this.PostAsync(url, patches.ToString(), "application/json-patch+json");

        protected virtual Task<string> PostAsync(string url, dynamic payload)
            => this.PostAsync(url, payload != null ? JObject.FromObject(payload).ToString() : null);

        protected virtual async Task<string> PatchAsync(
            string url,
            string payload,
            string contentType = "application/json-patch+json")
        {
            var response = await this.baseClient.PatchAsync(
                url,
                new StringContent(payload, Encoding.UTF8, contentType));
            await this.CheckResponseAsync(response, "patch");
            var responseText = await response.Content.ReadAsStringAsync();
            return responseText;
        }

        protected virtual Task<string> PatchAsync(
            string url,
            dynamic payload,
            string contentType = "application/json-patch+json")
        => this.PatchAsync(url, JObject.FromObject(payload).ToString(), contentType);

        protected virtual Task<string> PatchAsync(string url, dynamic payload)
            => this.PatchAsync(url, JObject.FromObject(payload).ToString());

        public async Task<List<ADOPullRequest>> GetPullRequestsAsync(ADOPullRequestFilter filter)
        {
            var textResponse = await this.GetAsync(
                $"{this.baseUrl}"
                    + $"/_apis/git/pullrequests"
                    + $"?api-version=6.0"
                    + $"&{filter}");
            var jsonResponse = JObject.Parse(textResponse);
            return jsonResponse["value"]
                .Select(token => token.ToObject<ADOPullRequest>())
                .ToList();
        }

        public async Task<ADOBuild> GetBuildAsync(int buildId)
        {
            var textResponse = await this.GetAsync(
                $"{this.baseUrl}"
                    + $"/_apis/build/builds/{buildId}"
                    + $"?api-version=6.0");
            var jsonResponse = JObject.Parse(textResponse);
            return jsonResponse.ToObject<ADOBuild>();
        }

        public async Task<List<ADOBuild>> GetBuildsAsync(ADOBuildFilter filter)
        {          
            var textResponse = await this.GetAsync(
                $"{this.baseUrl}"
                    + $"/_apis/build/builds"
                    + $"?api-version=6.0"
                    + $"&{filter}");
            var jsonResponse = JObject.Parse(textResponse);
            return jsonResponse["value"]
                .Select(token => token.ToObject<ADOBuild>())
                .ToList();
        }

        public async Task PostPRCommentAsync(ADOPullRequest pullRequest, string comment, bool active = true)
        {
            var url = $"{this.baseUrl}"
                + $"/_apis/git/repositories/{pullRequest.Repository.Id}/pullRequests/{pullRequest.Id}/threads"
                + $"?api-version=6.0";

            var requestPayload = JObject.FromObject(new
            {
                comments = new dynamic[]
                {
                    new
                    {
                        parentCommentId = 0,
                        content = comment,
                        commentType = 1
                    }
                },
                status = active ? 1 : 2,
            }).ToString();

            var response = await this.baseClient.PostAsync(
                url,
                new StringContent(requestPayload, Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Attempt to get sizes for the provided artifact paths from any of the builds supplied. Populates only the first
        /// size value found and terminates traversal early if all requested values are populated.
        /// </summary>
        /// <param name="builds"></param>
        /// <param name="fullArtifactPaths"></param>
        /// <returns></returns>
        public async Task<Dictionary<string, int>> GetArtifactSizesFromBuildsAsync(
            IEnumerable<ADOBuild> builds,
            IEnumerable<string> fullArtifactPaths)
        {
            var pathListsByArtifactName = fullArtifactPaths
                .Select(path => path.StartsWith('/') ? path[1..] : path)
                .GroupBy(input => input.Substring(0, input.IndexOf('/')))
                .ToDictionary(pathGroup => pathGroup.Key, pathGroup => pathGroup.Select(path => path[path.IndexOf('/')..]));

            var results = new Dictionary<string, int>();

            var buildIds = string.Join(',', builds.Select(build => build.Id));

            foreach (var build in builds.Reverse())
            {
                if (results.Count >= fullArtifactPaths.Count())
                {
                    break;
                }
                var remainingArtifactPathsByArtifactName = pathListsByArtifactName
                    .Where(pathListByArtifactName => !results.ContainsKey(pathListByArtifactName.Key));
                foreach ((var artifactName, var queryPaths) in remainingArtifactPathsByArtifactName)
                {
                    var artifact = await this.GetBuildArtifactAsync(build.Id, artifactName);
                    var artifactInfo = await this.GetArtifactHierarchyObjectAsync(build.Id, artifact.Value<int>("id"));

                    foreach (var path in queryPaths)
                    {
                        var fullPath = $"{artifactName}{path}";
                        if (results.ContainsKey(fullPath))
                        {
                            // Already an entry for this path. Done.
                            break;
                        }

                        var sizeToken = artifactInfo.SelectToken($"$..*[?(@.name == '{path}')]");

                        if (sizeToken != null)
                        {
                            results.Add(fullPath, sizeToken.Value<int>("size"));
                        }
                    }
                }
            }

            return results;
        }

        public Task<Dictionary<string, int>> GetArtifactSizesFromBuildAsync(
            ADOBuild build,
            IEnumerable<string> artifactPaths)
            => this.GetArtifactSizesFromBuildsAsync(new ADOBuild[] { build }, artifactPaths);

        public async Task<int> GetArtifactSizeFromBuildAsync(ADOBuild build, string artifactPath)
        {
            var queryResults = await this.GetArtifactSizesFromBuildsAsync(
                new ADOBuild[] { build },
                new string[] { artifactPath });
            return queryResults.Any() ? queryResults.First().Value : -1;
        }

        public async Task<List<ADOCommit>> GetCommitsAsync(ADOCommitFilter filter)
        {
            var responseText = await this.GetAsync(
                $"{this.baseUrl}"
                    + $"/_apis/git/repositories/{filter.RepositoryId}/commits"
                    + $"?api-version=6.0"
                    + $"&{filter}");
            var responseJson = JObject.Parse(responseText);
            return responseJson.ContainsKey("value") ? responseJson["value"]
                .Select(token => token.ToObject<ADOCommit>())
                .ToList()
                : new List<ADOCommit> { responseJson.ToObject<ADOCommit>() };
        }

        public async Task<List<ADOPullRequestCommentThread>> GetCommentThreadsAsync(ADOPullRequest pullRequest)
        {
            var responseText = await this.GetAsync(
                $"{this.baseUrl}"
                    + $"/_apis/git/repositories/{pullRequest.Repository.Id}/pullRequests/{pullRequest.Id}/threads"
                    + $"?api-version=6.0");
            var responseJson = JObject.Parse(responseText);
            return responseJson["value"]
                .Select(token => token.ToObject<ADOPullRequestCommentThread>())
                .ToList();
        }

        public async Task UpdatePullRequestCommentAsync(
            ADOPullRequest pullRequest,
            ADOPullRequestCommentThread thread,
            ADOPullRequestComment comment,
            string newContent)
        {
            var responseText = await this.PatchAsync(
                $"{this.baseUrl}"
                    + $"/_apis/git/repositories/{pullRequest.Repository.Id}/pullRequests/{pullRequest.Id}"
                    + $"/threads/{thread.Id}/comments/{comment.Id}"
                    + $"?api-version=6.0",
                new
                {
                    content = newContent,
                },
                "application/json");
        }

        public async Task<ADOCommit> GetBuildHeadCommitAsync(ADOBuild build)
        {
            var result = await this.GetCommitsAsync(new ADOCommitFilter()
            {
                RepositoryId = build.Repository.Id,
                Id = build.HeadCommit,
            });
            return result.First();
        }

        public async Task<string> GetMergeBasisCommitIdAsync(
            Guid repositoryId,
            string mergeIntoCommitId,
            string mergeFromCommitId)
        {
            var responseText = await this.GetAsync(
                $"{this.baseUrl}/_apis/git/repositories/{repositoryId}/commits/{mergeIntoCommitId}/mergebases"
                    + $"?api-version=6.0-preview.1"
                    + $"&otherCommitId={mergeFromCommitId}");
            var responseJson = JObject.Parse(responseText);
            return responseJson.TryGetValue("value", out var resultsArrayToken)
                ? resultsArrayToken.Select(resultToken => resultToken.Value<string>("commitId")).FirstOrDefault()
                : "";
        }

        public async Task<List<ADOWorkItem>> GetWorkItemsFromQueryAsync(
            Guid queryId)
        {
            var responseText = await this.GetAsync(
                $"{this.baseUrl}"
                    + $"/_apis/wit/wiql/{queryId:D}"
                    + $"?api-version=6.0");
            var intObj = JObject.Parse(responseText);
            var referencedReponseTexts = (await Task.WhenAll(intObj["workItems"]
                .Select(async token => await this.GetAsync(token["url"].ToString()))))
                .ToList();
            var workItems = referencedReponseTexts
                .Select(text => JObject.Parse(text))
                .Select(token => token.ToObject<ADOWorkItem>())
                .ToList();
            return workItems;
        }

        public async Task<List<AdoTestResultInfo>> GetTestResultsAsync(ADOTestQueryFilter filter)
        {
            var url = $"{this.GetAnalyticsUrl()}/_odata/v4.0-preview/TestResults"
                + $"?$apply={filter}"
                + $"/groupby((Test/TestCaseReferenceId, TestResultId, TestRun/TestRunId, Test/TestName, Outcome, "
                + $"Test/FullyQualifiedTestName, PipelineRun/RunNumber, PipelineRun/PipelineRunId, TestRun/CompletedDate))";
            var response = await this.GetAsync(url);
            var jsonResponse = JObject.Parse(response);
            return jsonResponse["value"]
                .Select(value => value.ToObject<AdoTestResultInfo>())
                .ToList();
        }

        public async Task<ADOWorkItem> CreateWorkItemAsync(
            string type,
            string name,
            string areaPath,
            string reproSteps)
        {
            var url = $"{this.baseUrl}/_apis/wit/workitems/${type}?api-version=6.0";
            var content = new JsonPatchBuilder()
                .Add($"/fields/{ADOWorkItem.FieldNames.AreaPath}", areaPath)
                .Add($"/fields/{ADOWorkItem.FieldNames.Title}", name)
                .Add($"/fields/{ADOWorkItem.FieldNames.ReproSteps}", reproSteps);
            var response = await this.PostAsync(url, content);
            var responseJson = JObject.Parse(response);
            return responseJson.ToObject<ADOWorkItem>();
        }

        public async Task UpdateWorkItemAsync(
            ADOWorkItem workItem,
            JsonPatchBuilder patches)
        {
            var responseText = await this.PatchAsync(
                $"{this.baseUrl}/_apis/wit/workitems/{workItem.Id}?api-version=6.0",
                patches.ToString());
        }

        public async Task UpdateWorkItemAsync(
            int workItemId,
            params (string operation, string path, dynamic newValue)[] updates)
        {
            var responseText = await this.PatchAsync(
                $"{this.baseUrl}"
                    + $"/_apis/wit/workitems/{workItemId}"
                    + $"?api-version=6.0",
                JArray.FromObject(updates
                    .Select(update => new
                    {
                        op = update.operation,
                        path = update.path,
                        value = update.newValue
                    }).ToArray<dynamic>()).ToString());
        }

        public Task UpdateWorkItemFieldsAsync(
            int workItemId,
            params (string fieldName, string fieldValue)[] updates)
            => this.UpdateWorkItemAsync(workItemId, updates
                .Select(update =>
                (
                    "add",
                    $"/fields/{update.fieldName}",
                    update.fieldValue as dynamic
                )).ToArray());

        public Task UpdateWorkItemFieldAsync(int workItemId, string fieldName, string fieldValue)
            => this.UpdateWorkItemFieldsAsync(workItemId, (fieldName, fieldValue));

        public Task CommentOnWorkItemAsync(int workItemId, string comment)
            => this.UpdateWorkItemFieldsAsync(workItemId, ("System.History", comment));

        public async Task<List<ADOTestResultAnalyticsResult>> GetTestResultAnalyticsResults(ADOTestQueryFilter filter)
        {
            JObject summaryQueryObject = null;
            JObject detailedQueryObject = null;

            var urlBase = $"{this.GetAnalyticsUrl()}/_odata/v4.0-preview/TestResults";

            await Task.WhenAll(
                Task.Run(async () =>
                {
                    var url = $"{urlBase}Daily"
                        + $"?$apply=filter("
                            + $"Pipeline/PipelineId eq {filter.Pipeline}"
                            + $" and Date/Date ge {filter.Start:o}"
                            + $" and Branch/BranchName eq '{filter.Branch}'"
                        + ")/groupby((TestSK, Test/TestName), aggregate("
                            + $"ResultCount with sum as TotalCount"
                            + $", ResultFailCount with sum as FailedCount))"
                        + "/filter(FailedCount gt 0)";
                    var response = await this.GetAsync(url);
                    summaryQueryObject = JObject.Parse(response);
                }),
                Task.Run(async () =>
                {
                    var url = urlBase
                        + $"?$apply=filter("
                            + $"Pipeline/PipelineId eq {filter.Pipeline}"
                            + $" and StartedOn/Date ge {filter.Start:o}"
                            + $" and Branch/BranchName eq '{filter.Branch}'"
                        + ")/groupby((Test/TestSK, Test/FullyQualifiedTestName), aggregate("
                            + $"TestRun/ResultFailCount with sum as FailedCount"
                            + $", TestRun/StartedDate with max as MostRecentRun))"
                        + "/filter(FailedCount gt 0)";
                    var response = await this.GetAsync(url);
                    detailedQueryObject = JObject.Parse(response);
                }));

            var summaryDataByTestSK = summaryQueryObject["value"]
                .Select(token =>
                (
                    TestSK: token.Value<int>("TestSK"),
                    RunCount: token.Value<int>("TotalCount"),
                    FailCount: token.Value<int>("FailedCount"),
                    Name: token.SelectToken("$.Test.TestName").Value<string>()
                ))
                .ToDictionary(
                    summaryEntry => summaryEntry.TestSK,
                    summaryEntry => (summaryEntry.RunCount, summaryEntry.FailCount, summaryEntry.Name));
            var detailedDataByTestSK = detailedQueryObject["value"]
                .Select(token =>
                (
                    TestSK: token.SelectToken("$.Test.TestSK").Value<int>(),
                    FullName: token.SelectToken("$.Test.FullyQualifiedTestName").Value<string>(),
                    MostRecent: token.SelectToken("$.MostRecentRun").Value<DateTime>()
                ))
                .ToDictionary(
                    detailedEntry => detailedEntry.TestSK,
                    detailedEntry => (detailedEntry.FullName, detailedEntry.MostRecent));

            return summaryDataByTestSK.Select(pair =>
                {
                    var hasDetail = detailedDataByTestSK.TryGetValue(pair.Key, out var detailedData);

                    return new ADOTestResultAnalyticsResult()
                    {
                        FailureCount = pair.Value.FailCount,
                        RunCount = pair.Value.RunCount,
                        Name = pair.Value.Name,
                        FullName = hasDetail ? detailedData.FullName : string.Empty,
                        LastRun = hasDetail ? detailedData.MostRecent : DateTime.MinValue
                    };
                })
                .ToList();
        }

        public void Dispose()
        {
            this.baseClient.Dispose();
        }

        public string GetProjectFromUrl()
            => this.baseUrl[(this.baseUrl.LastIndexOf('/') + 1)..];

        public string GetUrlWithoutProject()
            => this.baseUrl.Substring(0, this.baseUrl.LastIndexOf('/'));

        public string GetAnalyticsUrl()
            => this.baseUrl.Insert(this.baseUrl.IndexOf('.') + 1, "analytics.");

        private async Task CheckResponseAsync(HttpResponseMessage response, string label)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.OK
                && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                this.ErrorReceived?.Invoke(label, await response.Content.ReadAsStringAsync());
            }
        }

    }
}
