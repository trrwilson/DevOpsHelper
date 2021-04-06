using DevOpsHelper.Helpers;
using DevOpsMinClient;
using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.QueryFilters;
using DevOpsMinClient.Helpers;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Linq;
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
                OptionDefinition.UpdateTestFailureBugs.AutoFileThreshold,
            };

            command.AddOptions(requiredOptions);

            var localCommand = new UpdateTestFailureBugsCommand(command, requiredOptions);

            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public UpdateTestFailureBugsCommand(CommandLineApplication application, OptionDefinition[] options)
            : base(application, options) { }

        public async Task<int> RunAsync()
        {
            if (!base.DoCommonSetup()) return -1;

            var failureInfoList = await QueryFailureInfoAsync();
            var workItemInfoList = await GenerateWorkItemMapAsync(failureInfoList);
            await UpdateWorkItemsWithFailureInfoAsync(workItemInfoList);
            await ProcessQueryCleanupAsync(failureInfoList);

            return 0;
        }

        private async Task<List<FailureInfoCollection>> QueryFailureInfoAsync()
        {
            var branch = OptionDefinition.UpdateTestFailureBugs.Branch.ValueFrom(this.baseCommand);
            var filter = new ADOTestQueryFilter()
            {
                Pipeline = OptionDefinition.UpdateTestFailureBugs.PipelineId.ValueFrom(this.baseCommand),
                Start = DateTime.Now - TimeSpan.FromDays(14),
                Outcome = "Failed",
                Branch = branch,
            };

            Console.WriteLine($"Querying failures for {branch}...");
            var queriedFailures = await client.GetTestFailuresAsync(filter);
            var nameToFailureGroups = queriedFailures.GroupBy(failure => failure.TestFullName);

            Console.WriteLine($"Finding work items related to {queriedFailures.Count} failures "
                + $"({nameToFailureGroups.Count()} distinct)...");

            var result = (await Task.WhenAll(nameToFailureGroups
                .Select(async nameDataPair =>
                {
                    var allFailures = nameDataPair.ToList();
                    var allWorkItems = await client.GetWorkItemsRelatedToTestNameAsync(nameDataPair.Key);
                    return new FailureInfoCollection()
                    {
                        Name = nameDataPair.Key,
                        Failures = allFailures,
                        WorkItems = allWorkItems
                    };
                })))
                .ToList();

            return result;
        }

        private async Task<List<WorkItemInfoCollection>> GenerateWorkItemMapAsync(
            List<FailureInfoCollection> failureInfoList)
        {
            var result = new List<WorkItemInfoCollection>();

            foreach (var failureEntry in failureInfoList)
            {
                var nonClosedWorkItems = failureEntry.WorkItems
                    .Where(workItem => workItem.State != "Closed")
                    .ToList();

                if (nonClosedWorkItems.Count == 0
                    && !AnyWorkItemResolvedAfterAllFailures(failureEntry.WorkItems, failureEntry.Failures)
                    && failureEntry.Failures.Count 
                        > OptionDefinition.UpdateTestFailureBugs
                            .AutoFileThreshold.ValueFrom(this.baseCommand, int.MaxValue))
                {
                    var newBug = await client.CreateWorkItemAsync(
                        type: "Bug",
                        name: string.Format("{0} failed in {1} ({2} hits)",
                            failureEntry.Failures.First().TestName.Truncate(100),
                            OptionDefinition.UpdateTestFailureBugs.Branch.ValueFrom(this.baseCommand),
                            failureEntry.Failures.Count),
                        areaPath: OptionDefinition.UpdateTestFailureBugs.BugAreaPath.ValueFrom(this.baseCommand),
                        $"This bug was automatically filed.");

                    Console.WriteLine($" Filed new bug: {newBug.Id} -- {newBug.Title.Truncate(70)}");

                    failureEntry.WorkItems.Add(newBug);
                    result.Add(new WorkItemInfoCollection()
                    {
                        WorkItem = newBug,
                        Failures = failureEntry.Failures,
                    });
                }
                else if (nonClosedWorkItems.Count > 0)
                {
                    var mostRecentWorkItem = nonClosedWorkItems
                        .OrderByDescending(workItem => workItem.Id)
                        .First();
                    if (!result.Any(entry => entry.WorkItem.Id == mostRecentWorkItem.Id))
                    {
                        result.Add(new WorkItemInfoCollection()
                        {
                            WorkItem = mostRecentWorkItem,
                            Failures = new List<ADOTestFailureInfo>(),
                        });
                    }
                    result
                        .First(entry => entry.WorkItem.Id == mostRecentWorkItem.Id)
                        .Failures
                        .AddRange(failureEntry.Failures);
                }
            }

            return result;
        }

        private static bool AnyWorkItemResolvedAfterAllFailures(
            IEnumerable<ADOWorkItem> workItems,
            IEnumerable<ADOTestFailureInfo> failures)
        {
            return workItems.Any(workItem => failures.All(failure => workItem.ResolvedDate > failure.When));
        }

        private static bool WorkItemWasResolvedAfterFailures(
            ADOWorkItem workItem,
            List<ADOTestFailureInfo> failures)
        {
            return failures.All(failure => workItem.ResolvedDate > failure.When);
        }

        private async Task UpdateWorkItemsWithFailureInfoAsync(
            List<WorkItemInfoCollection> workItemInfoList)
        {
            var bugAreaPath = OptionDefinition.UpdateTestFailureBugs.BugAreaPath.ValueFrom(this.baseCommand);
            Console.WriteLine($"Beginning scan of {workItemInfoList.Count} work items...");

            foreach (var workItemEntry in workItemInfoList)
            {
                var workItem = workItemEntry.WorkItem;
                var failures = workItemEntry.Failures;

                Console.Write($" {workItem.Id}: {workItem.Title.Truncate(50)} -- {failures.Count} failure(s) -- ");

                var workItemPatches = new JsonPatchBuilder(workItem);

                void AddPayloadIfNew(string url, string name)
                {
                    if (workItem.Relations.Any(relation => relation.Url == url)
                        || workItemPatches.ToString().Contains(url))
                    {
                        return;
                    }
                    workItemPatches.Add("/relations/-", new
                    {
                        rel = "ArtifactLink",
                        url,
                        attributes = new
                        {
                            name,
                        }
                    });
                }

                for (int i = 0; i < workItem.Relations.Count; i++)
                {
                    var relation = workItem.Relations[i];
                    if ((relation.Name == "Test" && !failures
                        .Any(workItemFailure => workItemFailure.GetTestUrl() == relation.Url))
                        || (relation.Name == "Test Result" && !failures
                        .Any(workItemFailure => workItemFailure.GetResultUrl() == relation.Url))
                        || (relation.Name == "Build" && !failures
                        .Any(workItemFailure => workItemFailure.GetBuildUrl() == relation.Url)))
                    {
                        workItemPatches.Remove($"/relations/{i}");
                    }
                }

                foreach (var workItemFailure in failures)
                {
                    AddPayloadIfNew(workItemFailure.GetBuildUrl(), "Build");
                    AddPayloadIfNew(workItemFailure.GetTestUrl(), "Test");
                    AddPayloadIfNew(workItemFailure.GetResultUrl(), "Test Result");
                }

                if (!workItem.RawFields.TryGetValue(ADOWorkItem.FieldNames.AreaPath, out var areaPath)
                    || !areaPath.StartsWith(bugAreaPath))
                {
                    workItemPatches.Replace($"/fields/{ADOWorkItem.FieldNames.AreaPath}", bugAreaPath);
                }

                if (!workItem.RawFields.TryGetValue(ADOWorkItem.FieldNames.IncidentCount, out var incidentCount)
                    || incidentCount != $"{failures.Count}")
                {
                    var path = $"/fields/{ADOWorkItem.FieldNames.IncidentCount}";
                    if (incidentCount == null)
                    {
                        workItemPatches.Add(path, $"{failures.Count}");
                    }
                    else
                    {
                        workItemPatches.Replace(path, $"{failures.Count}");
                    }
                }

                if (failures.Any(failure => failure.When > workItem.LastHitDate))
                {
                    var latestHit = failures
                        .Select(failure => failure.When)
                        .OrderByDescending(failureDate => failureDate)
                        .First();
                    workItemPatches.Add($"/fields/{ADOWorkItem.FieldNames.AcceptedDate}", $"{latestHit:o}");
                }

                if (workItemPatches.PatchCount > 1)
                {
                    Console.Write("updating -- ");
                    await client.UpdateWorkItemAsync(workItem, workItemPatches);
                    Console.WriteLine("done.");
                }
                else
                {
                    Console.WriteLine("already up to date.");
                }
            }
        }

        private async Task ProcessQueryCleanupAsync(
            List<FailureInfoCollection> failureInfoList)
        {
            var countNoChanges = 0;

            Console.WriteLine($"Querying all existing work items...");

            var inQueryItems = await client.GetWorkItemsFromQueryAsync(
                OptionDefinition.UpdateTestFailureBugs.QueryId.ValueFrom(this.baseCommand));

            Console.WriteLine($"Checking each of {inQueryItems.Count} work items...");

            foreach (var inQueryItem in inQueryItems)
            {
                var failuresForItem = failureInfoList
                    .Where(entry => entry.WorkItems.Any(workItem => workItem.Id == inQueryItem.Id))
                    .SelectMany(entry => entry.Failures)
                    .ToList();

                if (WorkItemIsResolvedAndShouldNotBe(inQueryItem, failuresForItem))
                {
                    await AutoReactivateWorkItemAsync(client, inQueryItem);
                }
                else if (WorkItemIsActiveAndShouldNotBe(inQueryItem, failuresForItem))
                {
                    await AutoResolveWorkItemAsync(client, inQueryItem);
                }
                else if (!failuresForItem.Any())
                {
                    await ResolveWorkItemForNoHitsAsync(client, inQueryItem);
                }
                else
                {
                    countNoChanges++;
                }
            }

            Console.WriteLine($"...done. {countNoChanges}/{inQueryItems.Count} were up to date.");
        }

        private static bool WorkItemIsResolvedAndShouldNotBe(
            ADOWorkItem workItem,
            List<ADOTestFailureInfo> failuresForWorkItem)
        {
            return workItem.State == "Resolved"
                && failuresForWorkItem
                    .Any(failure => failure.When > workItem.ResolvedDate + TimeSpan.FromHours(2));
        }

        private static async Task AutoReactivateWorkItemAsync(
            ADOClient client,
            ADOWorkItem workItem)
        {
            Console.WriteLine($" Reactivating: {workItem.Id} -- {workItem.Title.Truncate(65)}");
            var patches = new JsonPatchBuilder(workItem)
                .Replace($"/fields/{ADOWorkItem.FieldNames.State}", "Active")
                .Remove($"/fields/{ADOWorkItem.FieldNames.AssignedTo}")
                .Add($"/fields/{ADOWorkItem.FieldNames.History}",
                    $"[Automatic message] This bug has been automatically reactivated as it has associated "
                    + $"failures recorded that happened <i>after</i> the resolution date of "
                    + $"{workItem.ResolvedDate}.");
            await client.UpdateWorkItemAsync(workItem, patches);
        }

        private static bool WorkItemIsActiveAndShouldNotBe(
            ADOWorkItem workItem,
            List<ADOTestFailureInfo> failuresForWorkItem)
        {
            var mostRelevantDate = workItem.ResolvedDate > workItem.DeploymentDate ?
                workItem.ResolvedDate : workItem.DeploymentDate;
            return (workItem.State == "New" || workItem.State == "Active")
                && failuresForWorkItem.All(failure => failure.When < workItem.DeploymentDate);
        }

        private static async Task AutoResolveWorkItemAsync(
            ADOClient client,
            ADOWorkItem workItem)
        {
            Console.WriteLine($" Resolving: {workItem.Id} -- {workItem.Title.Truncate(65)}");
            var relevantDate = workItem.DeploymentDate > workItem.ResolvedDate
                            ? workItem.DeploymentDate : workItem.ResolvedDate;
            var patches = new JsonPatchBuilder(workItem)
                .Replace($"/fields/{ADOWorkItem.FieldNames.State}", "Resolved")
                .Add($"/fields/{ADOWorkItem.FieldNames.History}",
                    $"[Automatic message] this bug is being automatically resolved because all linked "
                    + $"failures have a recorded time <i>before</i> the resolve or deployment date of "
                    + $"{relevantDate}");
            await client.UpdateWorkItemAsync(workItem, patches);
        }

        private static async Task ResolveWorkItemForNoHitsAsync(ADOClient client, ADOWorkItem workItem)
        {
            Console.WriteLine($"Clearing (no hits): {workItem.Id} : {workItem.Title}");
            var patches = new JsonPatchBuilder(workItem)
                .Remove($"/fields/{ADOWorkItem.FieldNames.IncidentCount}")
                .Replace($"/fields/{ADOWorkItem.FieldNames.State}", "Resolved")
                .Add($"/fields/{ADOWorkItem.FieldNames.History}",
                    $"[Automated message] Resolving this bug as no linked test failures have been observed"
                    + $" in the last 14 days.");
            await client.UpdateWorkItemAsync(workItem, patches);
        }

        struct FailureInfoCollection
        {
            public string Name;
            public List<ADOTestFailureInfo> Failures;
            public List<ADOWorkItem> WorkItems;
        }

        struct WorkItemInfoCollection
        {
            public ADOWorkItem WorkItem;
            public List<ADOTestFailureInfo> Failures;
        }
    }
}
