using DevOpsHelper.Helpers;
using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.Details;
using DevOpsMinClient.DataTypes.QueryFilters;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                OptionDefinition.UpdateTestFailureBugs.FailureIgnorePatterns,
                OptionDefinition.UpdateTestFailureBugs.CommonBranchPrefix,
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

            var matchingResultInfo = await QueryFailureInfoAsync();
            var allFailureData = await QueryWorkItemFailureCollectionsAsync(matchingResultInfo);

            ConsolidateWorkItemAutomationInfo(allFailureData);
            await ReflectFailuresToWorkItemsAsync(allFailureData);
            await UpdateWorkItemsAsync(allFailureData.SelectMany(failure => failure.WorkItems));
            await ProcessQueryCleanupAsync(allFailureData);

            return 0;
        }

        private async Task<IEnumerable<ADOSimpleTestResultInfo>> QueryFailureInfoAsync()
        {
            var branch = OptionDefinition.UpdateTestFailureBugs.Branch.ValueFrom(this.baseCommand);
            var filter = new ADOTestQueryFilter()
            {
                Pipeline = OptionDefinition.UpdateTestFailureBugs.PipelineId.ValueFrom(this.baseCommand),
                Start = DateTime.Now - TimeSpan.FromDays(14),
                Outcome = "Failed",
                Branch = branch,
            };

            Console.Write($"Querying failures for {branch}... ");
            var queriedFailures = await client.GetTestResultsAsync(filter);
            var fullNameToFailureGroups = queriedFailures.GroupBy(failure => failure.TestFullName);
            var filters = OptionDefinition.UpdateTestFailureBugs.FailureIgnorePatterns.ValueFrom(this.baseCommand);
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
                .OrderByDescending(failureInfo => failureInfo.Failures.Count)
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
                    Failures = failureContainerPair.ToList(),
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
                    Failures = new(),
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
            var branchName = OptionDefinition.UpdateTestFailureBugs.Branch.ValueFrom(this.baseCommand);
            var branchPrefix = OptionDefinition.UpdateTestFailureBugs.CommonBranchPrefix.ValueFrom(this.baseCommand, "");
            var trimmedBranchName = branchName.StartsWith(branchPrefix) ? branchName[(branchPrefix.Length)..] : branchName;

            var bugAreaPath = OptionDefinition.UpdateTestFailureBugs.BugAreaPath.ValueFrom(this.baseCommand);
            var bugIterationPath = OptionDefinition.UpdateTestFailureBugs.BugIterationPath.ValueFrom(this.baseCommand);

            var newBugHitThreshold = OptionDefinition.UpdateTestFailureBugs.
                    AutoFileThreshold.ValueFrom(this.baseCommand, int.MaxValue);

            Console.WriteLine($"Matching information between {allFailureInfo.Count} failure entries and work items...");

            var bucketsWithoutWorkItems = allFailureInfo.Where(bucket =>
                bucket.Failures.Count >= newBugHitThreshold
                && bucket.WorkItems.Count == 0);
            foreach (var bucket in bucketsWithoutWorkItems)
            {
                var detailedFailure = await this.client.GetDetailedTestResultInfoAsync(bucket.Failures.First());

                var newItem = new ADOWorkItem()
                {
                    WorkItemType = "Bug",
                    Title = string.Format("* {0} [{1}] failed in {2} ({3})",
                            detailedFailure.TestName.Truncate(100),
                            detailedFailure.ContainerName,
                            detailedFailure.BuildLabel,
                            trimmedBranchName),
                    ReproSteps = $"* This bug was automatically filed.<br/>"
                            + $"It had {bucket.Failures.Count} recent hits when generated.<br/>"
                            + $"<b>Test full name:</b> {detailedFailure.TestFullName}<br/>"
                            + $"<b>Test container:</b> {detailedFailure.ContainerName}<br/><br/>"
                            + $"<b>Test run:</b> {detailedFailure.RunName}"
                            + $"<b>Error:</b><br/>"
                            + $"<code>{detailedFailure.ErrorMessage}</code><br/><br/>"
                            + $"<b>Stack:</b><br/>"
                            + $"<code>{detailedFailure.StackTrace}</code><br/>",
                };
                bucket.WorkItems.Add(newItem);
                Console.WriteLine($"New bug: {newItem.Title.Truncate(95)}...");
            }

            foreach (var bucket in allFailureInfo.Where(bucket => bucket.WorkItems.Any()))
            {
                var newestItem = bucket.WorkItems[0];
                SyncFailureRelationInfo(bucket, newestItem);

                static string GetEnsuredPrefix(string input, string prefix)
                    => !string.IsNullOrEmpty(input) && input.StartsWith(prefix) ? input : prefix;
                newestItem.AreaPath = GetEnsuredPrefix(newestItem.AreaPath, bugAreaPath);
                newestItem.IterationPath = GetEnsuredPrefix(newestItem.IterationPath, bugIterationPath);
                newestItem.IncidentCount = bucket.Failures.Count;
                newestItem.LastHitDate = bucket.Failures
                    .Select(failure => failure.When)
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
                if ((relation.Name == "Test" && !info.Failures
                    .Any(workItemFailure => workItemFailure.GetTestUrl() == relation.Url))
                    || (relation.Name == "Test Result" && !info.Failures
                    .Any(workItemFailure => workItemFailure.GetResultUrl() == relation.Url))
                    || (relation.Name == "Build" && !info.Failures
                    .Any(workItemFailure => workItemFailure.GetBuildUrl() == relation.Url)))
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

            foreach (var failure in info.Failures)
            {
                AddRelationIfNew(failure.GetBuildUrl(), "Build");
                AddRelationIfNew(failure.GetTestUrl(), "Test");
                AddRelationIfNew(failure.GetResultUrl(), "Test Result");
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

            var inQueryItems = await client.GetWorkItemsFromQueryAsync(
                OptionDefinition.UpdateTestFailureBugs.QueryId.ValueFrom(this.baseCommand));

            Console.WriteLine($"checking each of {inQueryItems.Count} items from the query...");

            var countNoChanges = 0;

            foreach (var inQueryItem in inQueryItems)
            {
                var failuresForItem = failureInfoList
                    .Where(entry => entry.WorkItems.Any(workItem => workItem.Id == inQueryItem.Id))
                    .SelectMany(entry => entry.Failures)
                    .ToList();

                ChangesForUnassociatedRelations(inQueryItem, failuresForItem);
                ChangesForNoFailures(inQueryItem, failuresForItem);
                ChangesWhenResolved(inQueryItem, failuresForItem);
                ChangesWhenActive(inQueryItem, failuresForItem);
                ChangesWhenUnassigned(inQueryItem, failuresForItem);
                ChangesForConsistency(inQueryItem, failuresForItem);

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
                    workItem.State = "Resolved";
                    workItem.History += $"[Automatic message] this bug is being automatically resolved because it no longer has"
                            + $" any observed failures in the last 14 days.";
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

        private void ChangesForConsistency(ADOWorkItem workItem, List<ADOSimpleTestResultInfo> _)
        {
            var currentRawTags = string.IsNullOrEmpty(workItem.Tags) ? "" : workItem.Tags;
            var currentTags = currentRawTags
                .Split(';')
                .Select(tag => tag.Trim())
                .ToList();
            var tagsToEnsure = OptionDefinition.UpdateTestFailureBugs.BugTag.ValueFrom(this.baseCommand)
                .Split(';');
            var missingTags = tagsToEnsure
                .Where(tagToEnsure => !currentRawTags.ToLower().Contains(tagToEnsure.ToLower()));
            currentTags.AddRange(missingTags);
            workItem.Tags = string.Join("; ", currentTags);
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

        private class FailureInfoCollection
        {
            public string Name;
            public string Container;
            public List<ADOSimpleTestResultInfo> Failures;
            public List<ADOWorkItem> WorkItems;
        }
    }
}
