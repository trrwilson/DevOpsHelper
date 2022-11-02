using DevOpsHelper.Helpers;
using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.Details;
using DevOpsMinClient.DataTypes.QueryFilters;
using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevOpsHelper.Commands
{
    class UpdateTestFailureBugsCommand : CommandBase
    {
        public static void Init(CommandLineApplication command)
        {
            command.Description = "Scan a pipeline for failures and synchronizes with a query";

            var requiredOptions = new OptionDefinition[]
            {
                OptionDefinition.Url,
                OptionDefinition.AccessToken,
                OptionDefinition.UpdateTestFailureBugs.PipelineId,
                OptionDefinition.UpdateTestFailureBugs.Branch,
                OptionDefinition.UpdateTestFailureBugs.QueryId,
                OptionDefinition.UpdateTestFailureBugs.BugAreaPath,
                OptionDefinition.UpdateTestFailureBugs.BugIterationPath,
                OptionDefinition.UpdateTestFailureBugs.BugTag,
                OptionDefinition.UpdateTestFailureBugs.AutoFileThreshold,
            };
            var nonRequiredOptions = new OptionDefinition[]
            {
                OptionDefinition.UpdateTestFailureBugs.UseNonAnalyticsStrategy,
                OptionDefinition.UpdateTestFailureBugs.FailureIgnorePatterns,
                OptionDefinition.UpdateTestFailureBugs.CommonBranchPrefix,
                OptionDefinition.UpdateTestFailureBugs.IdleDayAutoCloseCount,
                OptionDefinition.UpdateTestFailureBugs.BuildReasons,
            };
            command.AddOptions(requiredOptions, nonRequiredOptions);

            var localCommand = new UpdateTestFailureBugsCommand(command, requiredOptions);

            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public UpdateTestFailureBugsCommand(CommandLineApplication application, OptionDefinition[] options)
            : base(application, options) { }

        public async Task<int> RunAsync()
        {
            if (!base.DoCommonSetup()) return -1;

            Options.Command = this.baseCommand;

            // This command can be handled with a strategy that leverages the Analytics API (which allows learning
            // about many test failures across many builds with a single REST query) or with a strategy that
            // iteratively uses catch-all HierarchyQuery capabilities to simulate clicking into build results one
            // by one. Analytics generates much less traffic and should be used when available.
            var allFailureData = Options.UseNonAnalyticsStrategy
                ? await GetNonAnalyticsFailureCollectionsAsync()
                : await GetAnalyticsFailureCollectionsAsync();

            ConsolidateWorkItemAutomationInfo(allFailureData);
            await ReflectFailuresToWorkItemsAsync(allFailureData);
            await UpdateWorkItemsAsync(allFailureData.SelectMany(failure => failure.WorkItems));
            await ProcessQueryCleanupAsync(allFailureData);

            return 0;
        }

        // If DevOps Analytics APIs are unavailable, we can't issue single queries for an aggregate view of failures
        // across builds. Instead, we'll use catch-all "hierarchy" queries to simulate clicking into each build and
        // looking at failures like a human would -- just a lot faster and more precisely.
        private async Task<List<FailureInfoCollection>> GetNonAnalyticsFailureCollectionsAsync()
        {
            var builds = await this.GetMatchingBuildsAsync();

            Console.WriteLine($"Finding all failures associated with matching builds...");
            var allFailures = await this.client.GetTestFailuresForBuildsAsync(builds);
            Console.WriteLine($"Found {allFailures.Count} total failure instances...");

            // Windows test containers may have an extension (e.g. "unit_test.exe") while non-Windows containers may
            // not (e.g. "unit_test"). We'll normalize (via removing extensions) to ensure we treat them similarly.
            string GetNormalizedContainerName(string rawContainerName)
            {
                var containerFileInfo = new FileInfo(rawContainerName);
                var containerExtension = containerFileInfo.Extension;
                return rawContainerName[..^containerExtension.Length];
            }

            // We want to combine the tallies of failure instances when it's the same test -- which we'll consider
            // equivalent to "shares the same name" and "shares the same automated test container." The latter
            // safeguards against "GenericTest1" in two different test binaries getting lumped together.
            var groupedFailures = allFailures
                .GroupBy(failure => (
                    Name: failure.TestFullName,
                    Container: GetNormalizedContainerName(failure.ContainerName)));
            Console.WriteLine($"  ...and found {groupedFailures.Count()} distinct buckets.");

            // Now we'll generate "failure info collections" to organize data about a failure bucket. This includes
            // the name and container for the test, a reference to the detailed failure information, and full
            // information about all active work items associated with the failure. That last part needs to be queried
            // per-bucket and we parallelize that (to do: meter this instead of blasting it all) to keep it reasonably
            // quick.
            var failureCollections = (await Task.WhenAll(groupedFailures.Select(async failureGroup =>
            {
                var (name, container) = failureGroup.Key;
                var workItemsForName = await this.client.GetWorkItemsRelatedToTestNameAsync(name);
                var workItemsForNameAndContainer = workItemsForName
                    .Where(workItem => workItem.AutomatedTestContainer == container);
                return new FailureInfoCollection()
                {
                    Name = name,
                    Container = container,
                    DetailedFailures = failureGroup
                        .OrderByDescending(failure => failure.When)
                        .ToList(),
                    WorkItems = workItemsForNameAndContainer
                        .OrderByDescending(workItem => workItem.Id)
                        .ToList(),
                };
            }))).ToList();

            failureCollections = failureCollections
                .OrderByDescending(failure => failure.DetailedFailures.Count)
                .ToList();

            Console.WriteLine($"SUMMARY OF FAILURES FOUND: ");
            foreach (var failureCollection in failureCollections)
            {
                Console.WriteLine($"---");
                Console.WriteLine($"Test Name: {failureCollection.Name}");
                Console.WriteLine($"Container: {failureCollection.Container}");
                Console.WriteLine($"Hits     : {failureCollection.DetailedFailures.Count}");
                if (failureCollection.WorkItems.Any())
                {
                    Console.WriteLine($"Bugs     : {string.Join(' ', failureCollection.WorkItems.Select(bug => bug.Id))}");
                }
            }

            return failureCollections;
        }

        private async Task<IEnumerable<ADOBuild>> GetMatchingBuildsAsync()
        {
            Console.WriteLine($"Getting builds for {Options.Branch} from the last {Options.AutoCloseIdleDays} days...");
            var builds = await this.client.GetBuildsAsync(new ADOBuildFilter()
            {
                Branch = Options.Branch,
                Definition = Options.PipelineId,
                Oldest = DateTime.Now - TimeSpan.FromDays(Options.AutoCloseIdleDays),
                MaxResults = 100,
            });
            Console.WriteLine($"Found {builds.Count} builds.");// Getting all failure instances...");

            if (Options.BuildReasons.Any())
            {
                bool IsAllowedReason(string reason)
                    => Options.BuildReasons.Any(allowedReason => allowedReason.ToLower() == reason.ToLower());
                builds = builds.Where(build => IsAllowedReason(build.Reason)).ToList();
                Console.WriteLine($"{builds.Count} matched allowed reason filters: {string.Join(',', Options.BuildReasons)}");
            }

            return builds;
        }

        private async Task<List<FailureInfoCollection>> GetAnalyticsFailureCollectionsAsync()
        {
            var matchingResultInfo = await QueryFailureInfoAsync();
            var allFailureData = await QueryWorkItemFailureCollectionsAsync(matchingResultInfo);
            return allFailureData;
        }

        private async Task<IEnumerable<ADOSimpleTestResultInfo>> QueryFailureInfoAsync()
        {
            var filter = new ADOTestQueryFilter()
            {
                Pipeline = Options.PipelineId,
                Start = DateTime.Now - TimeSpan.FromDays(Options.AutoCloseIdleDays),
                Outcome = "Failed",
                Branch = Options.Branch,
            };

            Console.Write($"Querying failures for {Options.Branch}... ");
            var queriedFailures = await client.GetTestResultsAsync(filter);

            var failuresByBuildType = queriedFailures.GroupBy(failure => failure.BuildReason);

            Console.WriteLine($"Found {queriedFailures.Count} instances across these build reasons:");
            foreach (var pair in failuresByBuildType)
            {
                Console.WriteLine($"{pair.Key,30} : {pair.Count()}");
            }

            if (Options.BuildReasons.Any())
            {
                Console.WriteLine($"Only considering failures for build reasons: {string.Join(",", Options.BuildReasons)}");
                queriedFailures = queriedFailures
                    .Where(failure => Options.BuildReasons.Any(reason => reason == failure.BuildReason))
                    .ToList();
            }

            var fullNameToFailureGroups = queriedFailures.GroupBy(failure => failure.TestFullName);
            var filters = Options.FailureIgnorePatterns;
            var nameToFailureGroups = fullNameToFailureGroups
                .Where(group => filters == null || filters.All(
                    filter => !Regex.IsMatch(group.Key, filter)));

            var totalFailures = queriedFailures.Count;
            var distinctFailures = fullNameToFailureGroups.Count();
            var filteredDistinctFailures = nameToFailureGroups.Count();

            Console.WriteLine($"found {totalFailures} total failures "
                + $"({distinctFailures} distinct"
                    + (filteredDistinctFailures != distinctFailures ? $", {filteredDistinctFailures} after filters)" : ")")
                    + ".");

            return nameToFailureGroups.SelectMany(group => group.ToList());
        }

        private async Task<List<FailureInfoCollection>> QueryWorkItemFailureCollectionsAsync(
            IEnumerable<ADOSimpleTestResultInfo> allResultInfo)
        {
            Console.Write($"Querying failure information for {allResultInfo.Count()} events... ");
            var unflattenedResult = (await Task.WhenAll(allResultInfo
                .GroupBy(result => result.TestFullName)
                .Select(async nameDataPair => await QueryInfoForFailureGroupAsync(nameDataPair.ToList()))))
                .ToList();

            // SelectMany doesn't play nice with async lambdas, so we do this as a separate step.
            var flattenedResult = unflattenedResult
                .SelectMany(failureChunk => failureChunk)
                .OrderByDescending(failureInfo => failureInfo.FailureCount)
                .ToList();

            Console.WriteLine($"found {flattenedResult.Count} entries.");

            return flattenedResult;
        }

        private async Task<List<FailureInfoCollection>> QueryInfoForFailureGroupAsync(
            List<ADOSimpleTestResultInfo> failureGroup)
        {
            static string NormalizedContainerName(string input)
            {
                var fileInfo = new FileInfo(input);
                return fileInfo.Name[..^fileInfo.Extension.Length];
            }

            var failuresByContainer = failureGroup
                .GroupBy(failure => NormalizedContainerName(failure.ContainerName))
                .ToList();
            var allWorkItems = (await client.GetWorkItemsRelatedToTestNameAsync(failureGroup.First().TestFullName)
                ).OrderByDescending(workItem => workItem.Id);
            var failureInfosWithContainers = failuresByContainer
                .Select(failureContainerPair => new FailureInfoCollection()
                {
                    Name = failureGroup.First().TestFullName,
                    Container = failureContainerPair.Key,
                    SimpleFailures = failureContainerPair.ToList(),
                    WorkItems = allWorkItems
                        .Where(workItem => workItem.AutomatedTestContainer == failureContainerPair.Key)
                        .ToList()
                })
                .ToList();
            var containerlessWorkitems = allWorkItems
                .Where(workItem => string.IsNullOrEmpty(workItem.AutomatedTestContainer));
            if (containerlessWorkitems.Any())
            {
                failureInfosWithContainers.Add(new FailureInfoCollection()
                {
                    Name = failureGroup.First().TestFullName,
                    SimpleFailures = new(),
                    WorkItems = containerlessWorkitems.ToList()
                });
            }
            return failureInfosWithContainers;
        }

        private static void ConsolidateWorkItemAutomationInfo(List<FailureInfoCollection> failureBuckets)
        {
            // First: if we have a bucket that has work items but no container AND a corresponding bucket that has a
            //  container but no work items, merge them.
            foreach (var migrationCandidate in failureBuckets.Where(failure => string.IsNullOrEmpty(failure.Container)).ToList())
            {
                var migrationDestination = failureBuckets
                    .FirstOrDefault(bucket => bucket.Name == migrationCandidate.Name && bucket.WorkItems.Count == 0);
                if (migrationDestination != null)
                {
                    foreach (var containerlessWorkItem in migrationCandidate.WorkItems)
                    {
                        containerlessWorkItem.AutomatedTestName = migrationDestination.Name;
                        containerlessWorkItem.AutomatedTestContainer = migrationDestination.Container;
                        migrationDestination.WorkItems.Add(containerlessWorkItem);
                        failureBuckets.Remove(migrationCandidate);
                    }
                }
            }

            // Next: if we have more than one bug for the same failure bucket, ensure we only track hit count for the first.
            foreach (var bucket in failureBuckets)
            {
                for (int i = 1; i < bucket.WorkItems.Count; i++)
                {
                    var olderWorkItem = bucket.WorkItems[i];
                    if (olderWorkItem.IncidentCount > 0)
                    {
                        olderWorkItem.History += "[Automated message] This bug's incident count is now cleared as related failures are "
                            + $"tracked by the newer work item:<br/>#{bucket.WorkItems[0].Id}";
                        Console.WriteLine($"Clearing hit count for {olderWorkItem.Id} as {bucket.WorkItems[0].Id} already tracks {bucket.Name}");
                        olderWorkItem.IncidentCount = 0;
                    }
                }
            }
        }

        private async Task ReflectFailuresToWorkItemsAsync(List<FailureInfoCollection> allFailureInfo)
        {
            var trimmedBranchName = Options.Branch.StartsWith(Options.CommonBranchPrefix)
                ? Options.Branch[(Options.CommonBranchPrefix.Length)..]
                : Options.Branch;

            Console.WriteLine($"Matching information between {allFailureInfo.Count} failure entries and work items...");

            var bucketsWithoutWorkItems = allFailureInfo.Where(bucket =>
                bucket.FailureCount >= Options.AutoFileThreshold && bucket.WorkItems.Count == 0);

            foreach (var bucket in bucketsWithoutWorkItems)
            {
                var latestDetailedFailure = 
                    bucket.DetailedFailures != null ? bucket.DetailedFailures.OrderByDescending(failure => failure.When).First()
                        : await this.client.GetLatestDetailedTestResultInfoAsync(bucket.SimpleFailures);

                if (latestDetailedFailure != null)
                {
                    var newItem = new ADOWorkItem()
                    {
                        WorkItemType = "Bug",
                        Title = string.Format("* {0} [{1}] failed in {2} ({3})",
                                latestDetailedFailure.TestName.Truncate(100),
                                latestDetailedFailure.ContainerName,
                                latestDetailedFailure.BuildLabel,
                                trimmedBranchName),
                        ReproSteps = GetReproStepsAutogenHtml(bucket.FailureCount, latestDetailedFailure),
                    };
                    bucket.WorkItems.Add(newItem);
                    Console.WriteLine($"New bug: {newItem.Title.Truncate(95)}...");
                }
            }

            foreach (var bucket in allFailureInfo.Where(bucket => bucket.WorkItems.Any()))
            {
                var newestItem = bucket.WorkItems[0];
                SyncFailureRelationInfo(bucket, newestItem);

                static string GetEnsuredPrefix(string input, string prefix)
                    => !string.IsNullOrEmpty(input) && input.StartsWith(prefix) ? input : prefix;
                newestItem.AreaPath = GetEnsuredPrefix(newestItem.AreaPath, Options.BugAreaPath);
                newestItem.IterationPath = GetEnsuredPrefix(newestItem.IterationPath, Options.BugIterationPath);
                newestItem.IncidentCount = bucket.FailureCount;
                newestItem.LastHitDate = (bucket.DetailedFailures != null 
                        ? bucket.DetailedFailures.Select(failure => failure.When)
                        : bucket.SimpleFailures.Select(failure => failure.When))
                    .OrderByDescending(failureDate => failureDate)
                    .FirstOrDefault();
                newestItem.AutomatedTestName = bucket.Name;
                newestItem.AutomatedTestContainer = bucket.Container;
            }
        }

        private void SyncFailureRelationInfo(FailureInfoCollection info, ADOWorkItem workItem)
        {
            // Remove any existing relations from the work item that are no longer part of the contemporary failure
            // collection
            foreach (var relation in workItem.Relations.ToList())
            {
                bool ShouldRemoveTestRelation() => relation.Name == "Test" && (info.DetailedFailures != null
                    ? !info.DetailedFailures.Any(failure => failure.GetTestUrl() == relation.Url)
                    : !info.SimpleFailures.Any(failure => failure.GetTestUrl() == relation.Url));
                bool ShouldRemoveResultRelation() => relation.Name == "Test Result" && (info.DetailedFailures != null
                    ? !info.DetailedFailures.Any(failure => failure.GetResultUrl() == relation.Url)
                    : !info.SimpleFailures.Any(failure => failure.GetResultUrl() == relation.Url));
                bool ShouldRemoveBuildRelation() => relation.Name == "Build" && (info.DetailedFailures != null
                    ? !info.DetailedFailures.Any(failure => failure.GetBuildUrl() == relation.Url)
                    : !info.SimpleFailures.Any(failure => failure.GetBuildUrl() == relation.Url));

                if (ShouldRemoveTestRelation() || ShouldRemoveResultRelation() || ShouldRemoveBuildRelation())
                {
                    workItem.Relations.Remove(relation);
                }
            }

            void AddRelationIfNew(string url, string name)
            {
                if (!workItem.Relations.Any(relation => relation.Url == url))
                {
                    workItem.Relations.Add(new ADOWorkItemRelationInfo()
                    {
                        Url = url,
                        Name = name,
                    });
                }
            }

            foreach (var (buildUrl, testUrl, resultUrl) in
                (info.DetailedFailures != null
                    ? info.DetailedFailures.Select(failure => (failure.GetBuildUrl(), failure.GetTestUrl(), failure.GetResultUrl()))
                    : info.SimpleFailures.Select(failure => (failure.GetBuildUrl(), failure.GetTestUrl(), failure.GetResultUrl()))))
            {
                AddRelationIfNew(buildUrl, "Build");
                AddRelationIfNew(testUrl, "Test");
                AddRelationIfNew(resultUrl, "Test Result");
            }
        }

        private async Task UpdateWorkItemsAsync(IEnumerable<ADOWorkItem> workItems)
        {
            Console.WriteLine($"Checking {workItems.Count()} work items for updates...");
            int updated = 0;
            foreach (var workItem in workItems)
            {
                if (await client.TryUpdateWorkItemAsync(workItem))
                {
                    Console.WriteLine($" Updated: {workItem.Id}: {workItem.Title.Truncate(80),-80}");
                    updated++;
                }
            }
            Console.WriteLine($"Done updating work items."
                + $" {workItems.Count() - updated}/{workItems.Count()} items were already up to date.");
        }

        private async Task ProcessQueryCleanupAsync(
            List<FailureInfoCollection> failureInfoList)
        {
            Console.Write($"Querying all existing work items... ");

            var inQueryItems = await client.GetWorkItemsFromQueryAsync(Options.QueryId);

            Console.WriteLine($"checking each of {inQueryItems.Count} items from the query...");

            var countNoChanges = 0;

            foreach (var inQueryItem in inQueryItems)
            {
                var failuresForItem = failureInfoList
                    .Where(entry => entry.WorkItems.Any(workItem => workItem.Id == inQueryItem.Id))
                    .SelectMany(entry => entry.DetailedFailures != null
                        ? entry.DetailedFailures.Select(detailedFailure => detailedFailure.ToSimpleInfo())
                        : entry.SimpleFailures)
                    .ToList();

                ChangesForUnassociatedRelations(inQueryItem, failuresForItem);
                ChangesForNoFailures(inQueryItem, failuresForItem);
                ChangesWhenResolved(inQueryItem, failuresForItem);
                ChangesWhenActive(inQueryItem, failuresForItem);
                ChangesWhenUnassigned(inQueryItem, failuresForItem);
                await ChangesForConsistencyAsync(inQueryItem, failuresForItem);

                var updateAttempted = await this.client.TryUpdateWorkItemAsync(inQueryItem);
                countNoChanges += updateAttempted ? 0 : 1;
            }

            Console.WriteLine($"...done. {countNoChanges}/{inQueryItems.Count} were up to date.");
        }

        private static void ChangesForUnassociatedRelations(
            ADOWorkItem workItem,
            List<ADOSimpleTestResultInfo> failuresForWorkItem)
        {
            foreach (var relation in workItem.Relations.ToList())
            {
                if ((relation.Name == "Test" && !failuresForWorkItem
                    .Any(workItemFailure => workItemFailure.GetTestUrl() == relation.Url))
                    || (relation.Name == "Test Result" && !failuresForWorkItem
                    .Any(workItemFailure => workItemFailure.GetResultUrl() == relation.Url))
                    || (relation.Name == "Build" && !failuresForWorkItem
                    .Any(workItemFailure => workItemFailure.GetBuildUrl() == relation.Url)))
                {
                    workItem.Relations.Remove(relation);
                }
            }
        }

        private static void ChangesForNoFailures(
            ADOWorkItem workItem,
            List<ADOSimpleTestResultInfo> failuresForWorkItem)
        {
            if (!failuresForWorkItem.Any())
            {
                Console.WriteLine($" Resolving (no failures): {workItem.Id} -- {workItem.Title.Truncate(65)}");
                workItem.IncidentCount = 0;
                if (workItem.State == "Active" || workItem.State == "New")
                {
                    workItem.ResolvedBy = null;
                    workItem.State = "Resolved";
                    workItem.History += $"[Automatic message] this bug is being automatically resolved because it no longer has"
                            + $" any observed failures in the last {Options.AutoCloseIdleDays} days.";
                }
            }
        }

        private static void ChangesWhenResolved(
            ADOWorkItem workItem,
            List<ADOSimpleTestResultInfo> failuresForWorkItem)
        {
            if (WorkItemIsResolvedAndShouldNotBe(workItem, failuresForWorkItem))
            {
                Console.WriteLine($" Reactivating: {workItem.Id} -- {workItem.Title.Truncate(65)}");
                workItem.State = "New";
                workItem.History +=
                    $"[Automatic message] This bug has been automatically reactivated as it has associated "
                        + $"failures recorded that happened <i>after</i> the resolution date of "
                        + $"{workItem.ResolvedDate}.";
                workItem.AssignedTo = null;
            }
        }

        private static bool WorkItemIsResolvedAndShouldNotBe(
            ADOWorkItem workItem,
            List<ADOSimpleTestResultInfo> failuresForWorkItem)
        {
            return workItem.State == "Resolved"
                && failuresForWorkItem
                    .Any(failure => failure.When > workItem.ResolvedDate + TimeSpan.FromHours(2));
        }

        private static void ChangesWhenActive(
            ADOWorkItem workItem,
            List<ADOSimpleTestResultInfo> failuresForWorkItem)
        {
            if (workItem.State != "New" && workItem.State != "Active")
            {
                return;
            }

            if (WorkItemHasOnlyPreFixFailures(workItem, failuresForWorkItem))
            {
                Console.WriteLine($" (Re-)Resolving: {workItem.Id} -- {workItem.Title.Truncate(65)}");
                var relevantDate = workItem.DeploymentDate > workItem.ResolvedDate
                                ? workItem.DeploymentDate : workItem.ResolvedDate;
                workItem.IncidentCount = 0;
                workItem.State = "Resolved";
                workItem.History +=
                        $"[Automatic message] this bug is being automatically resolved because all linked "
                        + $"failures have a recorded time <i>before</i> the resolve or deployment date of "
                        + $"{relevantDate}";
            }

            workItem.State = workItem.State == "Active" && workItem.AssignedTo == null ? "New" : workItem.State;
        }

        private static void ChangesWhenUnassigned(ADOWorkItem workItem, List<ADOSimpleTestResultInfo> _)
        {
            if (workItem.AssignedTo != null)
            {
                return;
            }

            if (workItem.ResolvedBy != null)
            {
                Console.WriteLine($"{workItem.Id}: Resolved with empty AssignedTo ->"
                    + $" ResolvedBy of {workItem.ResolvedBy.DisplayName}");
                workItem.AssignedTo = workItem.ResolvedBy;
            }
        }

        private async Task ChangesForConsistencyAsync(ADOWorkItem workItem, List<ADOSimpleTestResultInfo> simpleFailures)
        {
            var currentRawTags = string.IsNullOrEmpty(workItem.Tags) ? "" : workItem.Tags;
            var currentTags = currentRawTags
                .Split(';')
                .Select(tag => tag.Trim())
                .ToList();
            var tagsToEnsure = Options.BugTag
                .Split(';');
            var missingTags = tagsToEnsure
                .Where(tagToEnsure => !currentRawTags.ToLower().Contains(tagToEnsure.ToLower()));
            currentTags.AddRange(missingTags);
            workItem.Tags = string.Join("; ", currentTags);

            var currentReproStepsVersion = GetReproStepsAutogenVersionFromHtml(workItem.ReproSteps);
            if (simpleFailures.Any() && currentReproStepsVersion != reproStepsAutogenHtmlVersion)
            {
                Console.WriteLine($" Updating repro steps (v '{currentReproStepsVersion}' to v '{reproStepsAutogenHtmlVersion}')"
                    + $" {workItem.Id}: {workItem.Title.Truncate(50),-50}...");
                var detailedFailure = await this.client.GetLatestDetailedTestResultInfoAsync(simpleFailures);
                if (detailedFailure != null)
                {
                    var newReproSteps = GetReproStepsAutogenHtml(simpleFailures.Count, detailedFailure);
                    workItem.ReproSteps = $"{newReproSteps}";
                }
            }
        }

        private static bool WorkItemHasOnlyPreFixFailures(
            ADOWorkItem workItem,
            List<ADOSimpleTestResultInfo> failuresForWorkItem)
        {
            var mostRelevantDate = workItem.ResolvedDate > workItem.DeploymentDate ?
                workItem.ResolvedDate : workItem.DeploymentDate;
            return (workItem.State == "New" || workItem.State == "Active")
                && failuresForWorkItem.Any()
                && failuresForWorkItem.All(failure => failure.When < workItem.DeploymentDate);
        }

        private static readonly string reproStepsAutogenHtmlVersion = "02022022.2";

        private static string GetReproStepsAutogenVersionFromHtml(string reproStepsHtml)
        {
            var match = Regex.Match(reproStepsHtml, "AutogenReproStepsVersion:([^<]*)");
            return match.Success ? match.Groups[1].ToString().Trim() : string.Empty;
        }

        private static string GetReproStepsAutogenHtml(int failureCount, ADODetailedTestResultInfo failure)
        {
            var builder = new StringBuilder().Append(
                $"<i>* This bug was automatically filed.</i><br/>"
                + $"It had {failureCount} recent hits (last {Options.AutoCloseIdleDays} days) when generated.<br/>"
                + $"<i>Note:</i> The <code>IcM.IncidentCount</code> field (lower right) will show the latest {Options.AutoCloseIdleDays}-day rolling count.<br/>"
                + $"<b>Test full name:</b> {failure.TestFullName}<br/>"
                + $"<b>Test container:</b> {failure.ContainerName}<br/>"
                + $"<b>Test run:</b> {failure.RunName}<br/><br/>");

            // If this test had data rows (expandable sub results, like test case sections), dump the failed rows for
            // usable details; otherwise, use the top level
            var failedSubResults = failure.SubResults.Where(subresult => subresult.Outcome != "Passed");
            if (failedSubResults.Any())
            {
                foreach (var failedSubResult in failedSubResults)
                {
                    builder.Append(
                        $"<b>Row</b>: {failedSubResult.Name}</br>"
                        + $"<b>Error:</b></br>"
                        + $"<code>{failedSubResult.ErrorMessage}</code></br></br>"
                        + $"<b>Stack:</b></br>"
                        + $"<code>{failedSubResult.StackTrace}</code></br><hr/>");
                }
            }
            else
            {
                builder.Append(
                    $"<b>Error:</b><br/>"
                    + $"<code>{failure.ErrorMessage}</code><br/><br/>"
                    + $"<b>Stack:</b><br/>"
                    + $"<code>{failure.StackTrace}</code><br/>");
            }

            // Sneaky version watermark for future update checks
            builder.Append(
                $"<p style=\"color:white;font-size=1px\">AutogenReproStepsVersion:{reproStepsAutogenHtmlVersion}</p><br/>");

            return builder.ToString();
        }

        private class FailureInfoCollection
        {
            public string Name;
            public string Container;
            public List<ADOSimpleTestResultInfo> SimpleFailures;
            public List<ADODetailedTestResultInfo> DetailedFailures;
            public List<ADOWorkItem> WorkItems;

            public int FailureCount { get => this.DetailedFailures?.Count ?? this.SimpleFailures.Count; }
        }

        private class Options
        {
            public static CommandLineApplication Command;

            public static bool UseNonAnalyticsStrategy
            {
                get
                {
                    var optionValue = OptionDefinition.UpdateTestFailureBugs.UseNonAnalyticsStrategy
                        .ValueFrom(Options.Command, defaultValue: null);
                    return optionValue != null;
                }
            }

            private static Lazy<string> LazyBranch = new(() =>
            OptionDefinition.UpdateTestFailureBugs.Branch.ValueFrom(Options.Command));
            public static string Branch { get => LazyBranch.Value; }

            private static Lazy<int> LazyPipelineId = new(() =>
            OptionDefinition.UpdateTestFailureBugs.PipelineId.ValueFrom(Options.Command));
            public static int PipelineId { get => LazyPipelineId.Value; }

            private static Lazy<List<string>> LazyFailureIgnorePatterns = new(() =>
            OptionDefinition.UpdateTestFailureBugs.FailureIgnorePatterns.ValueFrom(Options.Command));
            public static List<string> FailureIgnorePatterns { get => LazyFailureIgnorePatterns.Value; }

            private static Lazy<string> LazyCommonBranchPrefix = new(() =>
            OptionDefinition.UpdateTestFailureBugs.CommonBranchPrefix.ValueFrom(Options.Command));
            public static string CommonBranchPrefix { get => LazyCommonBranchPrefix.Value; }

            private static Lazy<string> LazyBugAreaPath = new(() =>
                OptionDefinition.UpdateTestFailureBugs.BugAreaPath.ValueFrom(Options.Command));
            public static string BugAreaPath { get => LazyBugAreaPath.Value; }

            private static Lazy<string> LazyBugIterationPath = new(() =>
                OptionDefinition.UpdateTestFailureBugs.BugIterationPath.ValueFrom(Options.Command));
            public static string BugIterationPath { get => LazyBugIterationPath.Value; }

            private static Lazy<int> LazyAutoFileThreshold = new(() =>
            OptionDefinition.UpdateTestFailureBugs.AutoFileThreshold.ValueFrom(Options.Command, int.MaxValue));
            public static int AutoFileThreshold { get => LazyAutoFileThreshold.Value; }

            private static Lazy<Guid> LazyQueryId = new(() =>
            OptionDefinition.UpdateTestFailureBugs.QueryId.ValueFrom(Options.Command));
            public static Guid QueryId { get => LazyQueryId.Value; }

            private static Lazy<string> LazyBugTag = new(() =>
            OptionDefinition.UpdateTestFailureBugs.BugTag.ValueFrom(Options.Command));
            public static string BugTag { get => LazyBugTag.Value; }

            private static Lazy<int> LazyAutoCloseIdleDays = new(() =>
                OptionDefinition.UpdateTestFailureBugs.IdleDayAutoCloseCount.ValueFrom(Options.Command, 14));
            public static int AutoCloseIdleDays { get => LazyAutoCloseIdleDays.Value; }

            public static Lazy<List<string>> LazyBuildReasons = new(() =>
                OptionDefinition.UpdateTestFailureBugs.BuildReasons.ValueFrom(Options.Command));
            public static List<string> BuildReasons { get => LazyBuildReasons.Value; }
        }

    }
}
