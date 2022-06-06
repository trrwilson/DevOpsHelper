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

        // Uses HierarchyQuery to retrieve failure information associated with a provided build and queries to return
        // a collection of full detailed result data. Requires full access (unscoped) personal access tokens, which
        // generally aren't available to interactive users.
        public async Task<List<ADODetailedTestResultInfo>> GetTestFailuresForBuildAsync(ADOBuild build)
        {
            var testHierarchyJson = await this.GetBuildTestHierarchyObjectAsync(build.Id);

            // We want to find all "result groups" in this hierarchy document that have failures -- and a default
            // query will only report result details for failures.
            var resultGroupsWithFailures = testHierarchyJson.SelectTokens("$..resultsForGroup")
                .Children();

            var runAndTestIds = new List<(int RunId, int TestId)>();
            foreach (var failureGroup in resultGroupsWithFailures)
            {
                var groupJson = failureGroup as JObject;
                if (groupJson.TryGetValue("results", out JArray failureResults)
                    && groupJson.TryGetValue("groupByValue", out JObject groupByValueJson)
                    && groupByValueJson.TryGetValue("id", out int runId))
                {
                    foreach (var failureResult in failureResults.Children<JObject>())
                    {
                        if (failureResult.TryGetValue("id", out int testId))
                        {
                            runAndTestIds.Add((runId, testId));
                        }
                    }
                }
            }

            var failures = await Task.WhenAll(runAndTestIds
                .Select(async idPair => await this.GetDetailedTestResultInfoAsync(idPair.RunId, idPair.TestId)));

            return failures.ToList();
        }

        public async Task<List<ADODetailedTestResultInfo>> GetTestFailuresForBuildsAsync(IEnumerable<ADOBuild> builds)
        {
            // SelectMany doesn't work right with async, so we'll get the individual lists and then flatten them in a separate
            // step. The queries are done in full parallel (to do : more metered parallelism)
            var listsOfFailures = await Task.WhenAll(builds.Select(async build => await this.GetTestFailuresForBuildAsync(build)));
            return listsOfFailures.SelectMany(item => item).ToList();
        }

        // Gets a hierarchy document that includes test result data for the provided build identifier. Querying for
        // hierarchy documents requires a full-access personal access token (not generally accessible to interactive
        // users) and generally simulates the retrieval of backend data that happens when clicking through to the test
        // results tab in a browser.
        public async Task<JObject> GetBuildTestHierarchyObjectAsync(int buildId)
        {
            var requestUrl = $"{this.GetUrlWithoutProject()}"
                + $"/_apis/Contribution/HierarchyQuery/project/{this.GetProjectFromUrl()}"
                + $"?api-version=5.0-preview.1";
            var responseText = await this.PostAsync(requestUrl, new
            {
                contributionIds = new List<string>
                {
                    "ms.vss-test-web.test-tab-build-resultdetails-data-provider",
                },
                dataProviderContext = new { properties = new { sourcePage = new
                {
                    url = $"{this.baseUrl}/_build/results?buildId={buildId}&view=ms.vss-test-web.build-test-results-tab",
                    routeValues = new
                    {
                        project = this.GetProjectFromUrl(),
                    },
                }}},
            });;

            return JObject.Parse(responseText);
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

        public async Task<List<ADOWorkItem>> GetWorkItemsRelatedToTestNameAsync(string testName, bool includeClosed = false)
        {
            var url = $"{this.baseUrl}"
                + $"/_apis/test/Results/WorkItems"
                + $"?api-version=5.1-preview.1"
                + $"&workItemCategory=Microsoft.BugCategory"
                + $"&automatedTestName={HttpUtility.UrlEncode(testName)}";
            var response = await this.GetAsync(url);
            var jsonResponse = JObject.Parse(response);
            var responseIds = string.Join(
                ',',
                jsonResponse["value"]
                .Select(item => item.Value<int>("id"))
                .Distinct());
            if (string.IsNullOrEmpty(responseIds))
            {
                return new();
            }
            var workItemUrl = $"{this.baseUrl}"
                + $"/_apis/wit/workItems?ids={responseIds}&$expand=1";
            var workItemJsonText = await this.GetAsync(workItemUrl);
            var workItemJson = JObject.Parse(workItemJsonText);
            var workItemResults = workItemJson["value"]
                .Select(item => item.ToObject<ADOWorkItem>())
                .Where(item => includeClosed || item.State != "Closed")
                .ToList();
            return workItemResults;
        }

        public async Task<List<ADOBuildTimelineRecord>> GetBuildTimelineRecordsAsync(int buildId)
        {
            var url = $"{this.baseUrl}/_apis/build/builds/{buildId}/timeline";

            var response = await this.GetAsync(url);
            var timelineJson = JObject.Parse(response);

            var result = timelineJson["records"]
                .Select(item => item.ToObject<ADOBuildTimelineRecord>())
                .ToList();

            return result;
        }

        protected virtual async Task<string> GetAsync(string url)
        {
            var httpResult = await this.baseClient.GetAsync(url);
            await this.CheckResponseAsync(url, httpResult, "get");
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
            await this.CheckResponseAsync(url, response, "post");
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
            await this.CheckResponseAsync(url, response, "patch");
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
            await this.CheckResponseAsync(url, response, "post (PR comment)");
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

            var ids = intObj.Value<JArray>("workItems").Select(item => item.Value<int>("id"));
            ;
            var bugDetailsText = await this.GetAsync($"{this.baseUrl}"
                        + $"/_apis/wit/workItems?ids={string.Join(',', ids)}&$expand=1");
            var workItems = JObject.Parse(bugDetailsText)["value"]
                .Select(item => item.ToObject<ADOWorkItem>())
                .ToList();

            var workItem2s = JObject.Parse(bugDetailsText)["value"]
                .Select(item => item.ToObject<ADOWorkItem>())
                .ToList();
            return workItems;
        }

        public async Task<List<ADOSimpleTestResultInfo>> GetTestResultsAsync(ADOTestQueryFilter filter)
        {
            var url = $"{this.GetAnalyticsUrl()}/_odata/v4.0-preview/TestResults"
                + $"?$apply={filter}"
                + $"/groupby(("
                    + "Outcome, "
                    + "PipelineRun/PipelineRunId, "
                    + "PipelineRun/RunReason, "
                    + "Test/TestCaseReferenceId, "
                    + "Test/ContainerName, "
                    + "Test/FullyQualifiedTestName, "
                    + "Test/TestName, "
                    + "TestResultId, "
                    + "TestRun/CompletedDate, "
                    + "TestRun/TestRunId"
                + "))";

            var response = await this.GetAsync(url);
            var jsonResponse = JObject.Parse(response);
            return jsonResponse["value"]
                .Select(resultJson => resultJson.ToObject<ADOSimpleTestResultInfo>())
                .ToList();
        }

        public async Task<ADODetailedTestResultInfo> GetDetailedTestResultInfoAsync(int runId, int resultId)
        {
            var url = $"{this.baseUrl}/_apis/test/Runs/{runId}/Results/{resultId}?detailsToInclude=SubResults";
            var response = await this.GetAsync(url);
            var result = JsonConvert.DeserializeObject<ADODetailedTestResultInfo>(response);
            if (string.IsNullOrEmpty(result.BuildLabel))
            {
                this.ErrorReceived?.Invoke(
                    "DetailedTestResult",
                    $"Couldn't find detailed test result info for run result ID {resultId}.");
                return null;
            }
            return result;
        }

        public Task<ADODetailedTestResultInfo> GetDetailedTestResultInfoAsync(ADOSimpleTestResultInfo simpleInfo)
            => this.GetDetailedTestResultInfoAsync(simpleInfo.RunId, simpleInfo.RunResultId);

        public async Task<ADODetailedTestResultInfo> GetLatestDetailedTestResultInfoAsync(
            List<ADOSimpleTestResultInfo> orderedSimpleInfos)
        {
            for (int i = orderedSimpleInfos.Count - 1; i >= 0; i--)
            {
                var detailed = await GetDetailedTestResultInfoAsync(orderedSimpleInfos[i]);
                if (!string.IsNullOrEmpty(detailed.BuildLabel))
                {
                    return detailed;
                }
            }
            return null;
        }

        public async Task<bool> TryUpdateWorkItemAsync(ADOWorkItem workItem)
        {
            var patch = workItem.GenerateDeltaPatch();
            string responseText;
            if (patch.PatchCount < 2)
            {
                return false;
            }
            else if (workItem.originalSerializedJson == null)
            {
                responseText = await this.PostAsync(
                    $"{this.baseUrl}/_apis/wit/workitems/${workItem.WorkItemType}?api-version=6.0",
                    patch);
            }
            else
            {
                responseText = await this.PatchAsync(
                    $"{this.baseUrl}/_apis/wit/workitems/{workItem.Id}?api-version=6.0",
                    patch.ToString());
            }
            workItem.originalSerializedJson = JObject.Parse(responseText);
            JsonConvert.PopulateObject(responseText, workItem);
            return true;
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

        private async Task CheckResponseAsync(string requestUrl, HttpResponseMessage response, string label)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.OK
                && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                string message = $"\nRequest URL:\n{requestUrl}\nResponse:\n{await response.Content.ReadAsStringAsync()}";
                this.ErrorReceived?.Invoke(label, message);
            }
        }

    }
}
