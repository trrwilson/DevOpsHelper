using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.QueryFilters;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsHelper.Commands
{
    class PrintBranchSizesCommand : CommandBase
    {
        public static void Init(CommandLineApplication command)
        {
            command.Description = "For the latest builds of specified branches, print sizes of provided artifacts";

            var requiredOptions = new OptionDefinition[]
            {
                OptionDefinition.Url,
                OptionDefinition.AccessToken,
                OptionDefinition.PrintBranchSizes.PipelineId,
                OptionDefinition.PrintBranchSizes.BranchNames,
                OptionDefinition.PrintBranchSizes.ArtifactDefinitions,
            };
            var extraOptions = new OptionDefinition[]
            {
                OptionDefinition.PrintBranchSizes.BranchPrefix,
            };

            command.AddOptions(requiredOptions, extraOptions);

            var localCommand = new PrintBranchSizesCommand(command, requiredOptions);

            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public async Task<int> RunAsync()
        {
            if (!this.DoCommonSetup()) return -1;

            var fullBranchNames = OptionDefinition.PrintBranchSizes.BranchNames.ValueFrom(this.baseCommand);
            var branchPrefix = OptionDefinition.PrintBranchSizes.BranchPrefix.ValueFrom(this.baseCommand);
            var adjustedBranchNames = fullBranchNames
                .Select(fullBranchName => !fullBranchName.StartsWith(branchPrefix)
                    ? $"{branchPrefix}{fullBranchName}"
                    : fullBranchName)
                .ToList();

            var artifactDefinitions = OptionDefinition.PrintBranchSizes.ArtifactDefinitions.ValueFrom(this.baseCommand);

            Console.WriteLine($"Running a lot of queries for {adjustedBranchNames.Count}"
                + $" branches and {artifactDefinitions.Count} artifacts...");

            var allSizeEntriesFromBuilds = (await Task.WhenAll(
                adjustedBranchNames
                    .Select(async name => await GetSizesFromBranchAsync(name))))
                .SelectMany(list => list.Where(_ => true));

            Console.WriteLine();

            PrintSummaryTable(adjustedBranchNames, artifactDefinitions, allSizeEntriesFromBuilds);

            return 0;
        }

        private async Task<List<BranchArtifactSizeInfo>> GetSizesFromBranchAsync(string branchName)
        {
            var builds = await client.GetBuildsAsync(new ADOBuildFilter()
            {
                Branch = branchName,
                Definition = OptionDefinition.PrintBranchSizes.PipelineId.ValueFrom(this.baseCommand),
                SortOrder = "startTimeDescending",
                MaxResults = 5,
            });

            var artifactDefinitions = OptionDefinition.PrintBranchSizes.ArtifactDefinitions.ValueFrom(this.baseCommand);
            var artifactPaths = artifactDefinitions.Select(item => item.Path);
            var sizes = await client.GetArtifactSizesFromBuildsAsync(builds, artifactPaths);

            var results = new List<BranchArtifactSizeInfo>();

            foreach (var size in sizes)
            {
                var matchingDefinition = artifactDefinitions.First(definition => definition.Path == size.Key);
                results.Add(new BranchArtifactSizeInfo()
                {
                    Branch = branchName,
                    ArtifactName = matchingDefinition.Name,
                    Size = size.Value,
                });
            }

            if (!builds.Any())
            {
                Console.WriteLine($"Warning: no matching builds were found for {branchName}. Is this a correct,"
                    + $" complete path like /refs/heads/main ?");
            }
            else if (!results.Any())
            {
                Console.WriteLine($"Warning: builds but no results were found for {branchName}. Are the artifact paths"
                    + $" correct and expected on this branch?");
            }

            return results;
        }

        public PrintBranchSizesCommand(CommandLineApplication app, OptionDefinition[] options)
            : base(app, options) { }

        private static void PrintSummaryTable(
            List<string> longBranchNames,
            List<(string Name, string Path)> artifactDefinitions,
            IEnumerable<BranchArtifactSizeInfo> sizeResults)
        {
            var shortBranchNames = longBranchNames
                .Select(name => name.StartsWith("refs/heads/") ? name["refs/heads/".Length..] : name);
            var artifactNames = artifactDefinitions.Select(definition => definition.Name).ToList();

            var nameWidth = artifactNames.Max(name => name.Length) + 2;
            var branchWidth = shortBranchNames.Max(branch => branch.Length) + 2;

            var tableBuilder = new StringBuilder();
            tableBuilder.Append("".PadLeft(branchWidth));
            artifactNames.ForEach(name => tableBuilder.Append(name.PadLeft(nameWidth)));
            tableBuilder.Append('\n');

            foreach (var shortBranch in shortBranchNames)
            {
                tableBuilder.Append(shortBranch.PadLeft(branchWidth));
                foreach (var name in artifactNames)
                {
                    var match = sizeResults
                        .Where(result => result.Branch.EndsWith(shortBranch) && result.ArtifactName == name)
                        .FirstOrDefault();
                    tableBuilder.Append($"{(match == null ? "N/A" : match.Size)}".PadLeft(nameWidth));
                }
                tableBuilder.Append('\n');
            }

            Console.WriteLine(tableBuilder.ToString());
        }

        private class BranchArtifactSizeInfo
        {
            public string Branch;
            public string ArtifactName;
            public int Size;
        }
    }
}
