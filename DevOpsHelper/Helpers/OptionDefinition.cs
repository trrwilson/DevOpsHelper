using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DevOpsHelper
{
    public class OptionDefinition
    {
        public string Template { get; init; }
        public string Description { get; init; }
        public CommandOptionType OptionType { get; init; } = CommandOptionType.SingleValue;

        private static IDictionary<string, string> OptionDefaults { get; } = new Dictionary<string, string>();

        static OptionDefinition()
        {
            if (File.Exists("optiondefaults.config"))
            {
                using (var fileStream = File.OpenRead("optiondefaults.config"))
                using (var fileReader = new StreamReader(fileStream))
                {
                    while (!fileReader.EndOfStream)
                    {
                        var line = fileReader.ReadLine();
                        if (line.StartsWith('#') || line.StartsWith("//")) continue;
                        var option = line[0..(Math.Max(0, line.IndexOf(' ')))].Trim();
                        var value = line[Math.Max(0, line.IndexOf(' '))..].Trim();
                        if (!string.IsNullOrEmpty(option))
                        {
                            OptionDefaults[option] = value;
                        }
                    }
                }
            }
        }

        protected OptionDefinition(string template, string description)
        {
            this.Template = template;
            this.Description = description;
        }

        public string ValueFrom(CommandLineApplication command, string defaultValue = null)
        {
            var option = command.FindOption(this);
            if (option.HasValue())
            {
                return option.Value();
            }
            else if (Preferences.AllowFileDefaults 
                && OptionDefaults.TryGetValue($"--{option.LongName}", out var fileLongDefault))
            {
                return fileLongDefault;
            }
            else if (Preferences.AllowFileDefaults 
                && OptionDefaults.TryGetValue($"-{option.ShortName}", out var fileShortDefault))
            {
                return fileShortDefault;
            }
            else
            {
                return defaultValue;
            }
        }

        public static OptionDefinition Url { get; } = new OptionDefinition(
            "-u|--url",
            "The URL to connect to.");
        public static OptionDefinition AccessToken { get; } = new OptionDefinition(
            "-t|--pat",
            "The personal access token to use when connecting.");

        public static class Preferences
        {
            public static bool AllowFileDefaults { get; set; } = true;
        }

        public static class GetArtifacts
        {
            public static OptionDefinition<int> BuildId { get; } = new (
                "-b|--build",
                "The build ID to query.");
            public static OptionDefinition Artifact { get; } = new OptionDefinition(
                "-a|--artifact",
                "The artifact name to fetch.");
            public static OptionDefinition Output { get; } = new OptionDefinition(
                "-o|--output",
                "The folder to output the artifact to.");
        }

        public static class QueryArtifact
        {
            public static OptionDefinition BuildId => GetArtifacts.BuildId;
            public static OptionDefinition Artifact => GetArtifacts.Artifact;
            public static OptionDefinition Path { get; } = new OptionDefinition(
                "-p|--path",
                "The path within the artifact to query.");
        }

        public static class PrintBranchSizes
        {
            public static OptionDefinition BuildId => GetArtifacts.BuildId;
            public static OptionDefinition<List<string>> BranchNames { get; } = new(
                "--branch-names",
                "Semicolon-delimited list of branch names to query.",
                new List<string>());
            public static OptionDefinition<List<(string Name, string Path)>> ArtifactDefinitions { get; } = new(
                "--artifact-paths",
                "Semicolon-delimited list of colon-delimited name/path pairs to query.",
                new List<(string, string)>());
            public static OptionDefinition<int> PipelineId { get; } = new(
                "--pipeline-id",
                "The pipeline identifier to query for builds.");
            public static OptionDefinition<string> BranchPrefix { get; } = new(
                "--branch-prefix",
                "A common prefix, e.g. 'refs/heads/', to add to all branch names if not present.",
                "refs/heads/");
        }

        public static class PrintPullRequestSizeChanges
        {
            public static OptionDefinition NamedArtifactPaths { get; } = new OptionDefinition(
                "--artifact-paths",
                "A semicolon-delimited list of colon-separated pairs in name:path format.");
            public static OptionDefinition<int> PipelineId { get; } = new (
                "--pipeline-id",
                "The ID associated with the build pipeline to query, as seen from the build page in a browser.");
            public static OptionDefinition<Guid> RepositoryId { get; } = new (
                "--repository-id",
                "The GUID associated with the repository (source code grouping) to search.");
            public static OptionDefinition ReferenceBranch { get; } = new OptionDefinition(
                "--reference-branch",
                "The branch to use as the reference, typically a mainline development branch.");
            public static OptionDefinition<int> ReferenceBuildCount { get; } = new (
                "--num-reference-builds",
                "The maximum number of reference builds to cache and query. Bigger is slower but may catch more history.");
            public static OptionDefinition<int> PullRequestCount { get; } = new (
                "--num-pull-requests",
                "The number of pull requests (most recent first) to analyze.");
        }

        public static class UpdateTestFailureBugs
        {
            public static OptionDefinition<int> PipelineId = new OptionDefinition<int>(
                "--pipeline-id",
                "The identifier for the pipeline that should be queried for failures.");
            public static OptionDefinition Branch = new OptionDefinition(
                "--reference-branch",
                "The branch for which test failures should be considered.");
            public static OptionDefinition<Guid> QueryId = new OptionDefinition<Guid>(
                "--query-id",
                "The GUID for the query that should no-longer-matching items cleared.");
            public static OptionDefinition BugAreaPath = new OptionDefinition(
                "--bug-area-path",
                "The area path that test failure bugs should fall under.");
            public static OptionDefinition BugIterationPath = new(
                "--bug-iteration-path",
                "The iteration path that test failure bugs should fall under.");
            public static OptionDefinition BugTag = new(
                "--bug-tag",
                "A tag to ensure is applied to all test failure bugs.");
            public static OptionDefinition<int> AutoFileThreshold = new(
                "--auto-file-threshold",
                "If non-negative, the number of recent failures after which a test will auto-file a new bug.");
            public static OptionDefinition<List<string>> FailureIgnorePatterns = new(
                "--failure-ignore-patterns",
                "A semicolon-delimited list of regex patterns against which matching failures will be ignored.",
                new List<string>());
            public static OptionDefinition<int> IdleDayAutoCloseCount = new(
                "--auto-close-after-days",
                "The number of days after which a bug will be automatically resolved with no new hits.");
            public static OptionDefinition<string> CommonBranchPrefix { get; } = new(
                "--branch-prefix",
                "A common prefix, e.g. 'refs/heads/', to assume for all branch names if not present.",
                "refs/heads/");
        }

        public static class FindFailures
        {
            public static OptionDefinition<int> BuildId = new(
                "--build-id",
                "The identifier for the build to query results for.");
            public static OptionDefinition TestName = new(
                "--test-name",
                "The substring to match test names against.");
        }
    }

    public class OptionDefinition<T> : OptionDefinition
    {
        private T optionDefault = default;

        public OptionDefinition(string template, string description, T standardDefault = default)
            : base(template, description)
        {
            this.optionDefault = standardDefault;
        }

        public T ValueFrom(CommandLineApplication command, T defaultValue = default)
        {
            var programmaticDefaultToUse = EqualityComparer<T>.Default.Equals(defaultValue, default(T))
                ? this.optionDefault
                : defaultValue;
            var textDefault = EqualityComparer<T>.Default.Equals(programmaticDefaultToUse, default(T))
                ? string.Empty
                : $"{programmaticDefaultToUse}";
            var textValue = base.ValueFrom(command, null);

            return string.IsNullOrEmpty(textValue)
                ? programmaticDefaultToUse
                : (T)Convert.ChangeType(programmaticDefaultToUse switch
                {
                    string s => textValue,
                    int n => int.Parse(textValue),
                    Guid g => Guid.Parse(textValue),
                    List<string> l => textValue.Split(';').ToList(),
                    List<(string, string)> ll => textValue.Split(';').Select(semiColonSplit =>
                    {
                        var colonSplit = semiColonSplit.Split(':');
                        return (colonSplit[0], colonSplit[1]);
                    }).ToList(),
                    _ => throw new ArgumentException()
                }, typeof(T));
            ;
        }
    }
}
