using DevOpsHelper.Helpers;
using DevOpsMinClient.DataTypes;
using DevOpsMinClient.DataTypes.QueryFilters;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevOpsHelper.Commands
{
    class CheckPRSizeCommand : CommandBase
    {
        public static void Init(CommandLineApplication command)
        {
            command.Description = "Compare PR artifact sizes to reference and post differences";

            var requiredOptions = new OptionDefinition[]
            {
                OptionDefinition.Url,
                OptionDefinition.AccessToken,
                OptionDefinition.PrintPullRequestSizeChanges.NamedArtifactPaths,
                OptionDefinition.PrintPullRequestSizeChanges.PipelineId,
                OptionDefinition.PrintPullRequestSizeChanges.PullRequestCount,
                OptionDefinition.PrintPullRequestSizeChanges.ReferenceBranch,
                OptionDefinition.PrintPullRequestSizeChanges.ReferenceBuildCount,
                OptionDefinition.PrintPullRequestSizeChanges.RepositoryId,
            };

            command.AddOptions(requiredOptions);

            var localCommand = new CheckPRSizeCommand(command, requiredOptions);
            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public CheckPRSizeCommand(CommandLineApplication command, OptionDefinition[] options)
            : base(command, options) { }

        public async Task<int> RunAsync()
        {
            if (!base.DoCommonSetup()) return -1;

            var artifacts = OptionDefinition.PrintPullRequestSizeChanges.NamedArtifactPaths.ValueFrom(this.baseCommand)
                .Split(';')
                .Select(artifactToken => new ArtifactEntryInfo(artifactToken))
                .ToList();

            var referenceBuilds = await this.GetReferenceBuildsByCommitId();
            var referenceBuildsWithSize = referenceBuilds
                .Select(buildByCommit => (
                    Commit: buildByCommit.Key,
                    Build: buildByCommit.Value,
                    Size: (Dictionary<string, int>)null))
                .ToDictionary(
                    group => group.Commit,
                    group => (group.Build, group.Size));

            var pullRequestsWithBuilds = await this.GetPullRequestsWithBuildsAsync();

            foreach (var prBuildEntry in pullRequestsWithBuilds)
            {
                await this.CheckPullRequestBuildAsync(prBuildEntry, referenceBuildsWithSize, artifacts);
            }

            return 0;
        }

        private async Task<Dictionary<string, ADOBuild>> GetReferenceBuildsByCommitId()
        {
            var maxNumBuilds = OptionDefinition.PrintPullRequestSizeChanges
                .ReferenceBuildCount.ValueFrom(this.baseCommand);
            Console.WriteLine($"Gathering information on the {maxNumBuilds} most recent reference builds...");

            var recentReferenceBuilds = await client.GetBuildsAsync(new ADOBuildFilter()
            {
                Definition = OptionDefinition.PrintPullRequestSizeChanges.PipelineId.ValueFrom(this.baseCommand),
                Branch = OptionDefinition.PrintPullRequestSizeChanges.ReferenceBranch.ValueFrom(this.baseCommand),
                Reason = "batchedCI",
                SortOrder = "startTimeDescending",
                MaxResults = maxNumBuilds,
            });

            var referenceBuildsByCommit = recentReferenceBuilds
                .GroupBy(build => build.HeadCommit)
                .ToDictionary(buildGroup => buildGroup.Key, buildGroup => buildGroup.ToList().First());
            return referenceBuildsByCommit;
        }

        private async Task<List<(ADOPullRequest PullRequest, ADOBuild Build)>> GetPullRequestsWithBuildsAsync()
        {
            var maxNumPullRequests = OptionDefinition.PrintPullRequestSizeChanges
                .PullRequestCount.ValueFrom(this.baseCommand);
            Console.WriteLine($"Fetching information on the last {maxNumPullRequests} pull requests...");

            var prs = (await client.GetPullRequestsAsync(new ADOPullRequestFilter()
            {
                TargetRepositoryId = OptionDefinition.PrintPullRequestSizeChanges.RepositoryId.ValueFrom(this.baseCommand),
                TargetBranch = OptionDefinition.PrintPullRequestSizeChanges.ReferenceBranch.ValueFrom(this.baseCommand),
                Status = "all",
                MaxResults = maxNumPullRequests,
            })).Where(pr => pr.Status != "abandoned");

            Console.WriteLine($"Getting build info for {prs.Count()} non-abandoned pull requests...");

            var prsWithBuilds = (await Task.WhenAll(prs
                .Select(async pr => (
                    PullRequest: pr,
                    Build: await client.GetAssociatedBuildAsync(pr)
                 ))))
                 .Where(prWithBuild => prWithBuild.Build != null)
                 .OrderByDescending(prWithBuild => prWithBuild.Build.StartTime)
                 .ToList();
            return prsWithBuilds;
        }

#nullable enable
        private async Task CheckPullRequestBuildAsync(
            (ADOPullRequest, ADOBuild) entry,
            Dictionary<string, (ADOBuild, Dictionary<string, int>?)> referenceBuildsWithSizes,
            List<ArtifactEntryInfo> artifacts)
        {
#nullable restore
            var (pr, build) = entry;

            Console.WriteLine();
            Console.WriteLine($"PR {pr.Id} ({pr.Status}) "
                + $"by {pr.CreatedBy.DisplayName} @ {pr.CreationDate.ToLocalTime()}: \"{pr.Title.Truncate(45)}\"");

            var pathsToQuery = artifacts.Select(artifact => artifact.FullPath);

            var prSizesByPath = await client.GetArtifactSizesFromBuildAsync(build, pathsToQuery);

            if (!prSizesByPath.Any())
            {
                Console.WriteLine($"  --> No requested artifacts found in matching PR builds.");
                return;
            }

            var prSizesByArtifact = prSizesByPath
                .Select(pair => (
                    Artifact: artifacts.Where(artifact => artifact.FullPath == pair.Key).FirstOrDefault(),
                    Size: pair.Value))
                .ToDictionary(entry => entry.Artifact, entry => entry.Size);

            var firstCommonCommit = await client.GetMergeBasisCommitIdAsync(
                pr.Repository.Id,
                pr.LastTargetCommitId,
                build.HeadCommit);
            Console.WriteLine(
                $"Build: {build.Id} started {build.StartTime.ToLocalTime()} "
                + $"with merge basis {firstCommonCommit.Substring(0, 7)}");

            var currentInfo = (pr, build, prSizesByArtifact);

            var (referenceBuild, cachedSizes) = referenceBuildsWithSizes.GetValueOrDefault(firstCommonCommit, (null, null));
            if (referenceBuild == null)
            {
                Console.WriteLine($"  --> No reference build found for {firstCommonCommit[0..7]}.");
                return;
            }
            cachedSizes ??= await client.GetArtifactSizesFromBuildAsync(referenceBuild, pathsToQuery);
            referenceBuildsWithSizes[firstCommonCommit] = (referenceBuild, cachedSizes);
            var referenceSizesByArtifact = cachedSizes
                .Select(pair => (
                    Artifact: artifacts.Where(artifact => artifact.FullPath == pair.Key).FirstOrDefault(),
                    Size: pair.Value))
                .ToDictionary(entry => entry.Artifact, entry => entry.Size);

            var referenceInfo = (referenceBuild, referenceSizesByArtifact);

            var comment = await this.BuildCommentAsync(currentInfo, referenceInfo, firstCommonCommit);
            var (existingThread, existingComment)  = await this.GetExistingThreadAndCommentAsync(pr);

            if (existingComment is null)
            {
                Console.WriteLine($"  --> Creating new PR size comment...");
                await client.PostPRCommentAsync(
                    pr,
                    comment.ToString(),
                    active: comment.Entries.Any(entry => entry.ObservedSize > entry.ReferenceSize));
            }
            else if (!existingComment.IsSameAs(comment))
            {
                Console.WriteLine($"  --> Updating PR size comment...");
                await client.UpdatePullRequestCommentAsync(
                    pr,
                    existingThread,
                    existingThread.Comments.First(),
                    comment.ToString());
            }
            else
            {
                Console.WriteLine($"  --> PR size comment is already up to date.");
            }
        }

        private async Task<SizeComment> BuildCommentAsync(
            (ADOPullRequest, ADOBuild, Dictionary<ArtifactEntryInfo, int>) currentPullRequestInfo,
            (ADOBuild referenceBuild, Dictionary<ArtifactEntryInfo, int>) referenceBuildInfo,
            string firstCommonCommit)

        {
            var (pr, build, sizes) = currentPullRequestInfo;
            var (referenceBuild, referenceSizes) = referenceBuildInfo;

            var comment = new SizeComment()
            {
                ProjectUrl = $"{client.GetUrlWithoutProject()}/{client.GetProjectFromUrl()}",
                Build = referenceBuild,
                Commit = (await client.GetCommitsAsync(new ADOCommitFilter()
                {
                    RepositoryId = pr.Repository.Id,
                    Id = pr.LastSourceCommitId,
                })).FirstOrDefault(),
            };

            foreach (var (currentArtifact, currentSize) in sizes)
            {
                var (referenceArtifact, referenceSize) = referenceSizes
                    .FirstOrDefault(artifactInfo => artifactInfo.Key.FullPath == currentArtifact.FullPath);
                var referenceSizeToReport = referenceArtifact is null ? -1 : referenceSize;
                comment.Entries.Add((currentArtifact.DisplayName, currentSize, referenceSizeToReport));

                var sizeLine = $"  --> {currentArtifact.DisplayName,-25} : {currentSize,-9} b";
                if (referenceSizeToReport >= 0)
                {
                    var sizeDiff = currentSize - referenceSizeToReport;
                    sizeLine += $"  vs. reference {referenceSizeToReport,-9} b "
                        + (sizeDiff > 0 ? $"(+{sizeDiff})" : sizeDiff < 0 ? $"(-{Math.Abs(sizeDiff)})" : "(no change)");
                }
                Console.WriteLine(sizeLine);
            }

            return comment;
        }

        public async Task<(ADOPullRequestCommentThread Thread, SizeComment Comment)> GetExistingThreadAndCommentAsync(
            ADOPullRequest pr)
        {
            var existingThreads = await client.GetCommentThreadsAsync(pr);
            var threadWithComment = existingThreads
                .Where(thread => thread.Comments.Any(threadComment => !string.IsNullOrEmpty(threadComment.Content)))
                .Select(thread => (
                    thread,
                    ParsedComment: thread.Comments
                        .Select(threadComment => SizeComment.Parse(threadComment.Content))
                        .Where(sizeComment => sizeComment != null)
                        .FirstOrDefault()
                    ))
                .Where(threadCommentPair => threadCommentPair.ParsedComment != null)
                .FirstOrDefault();
            return threadWithComment;
        }

        private class ArtifactEntryInfo
        {
            public string DisplayName;
            public string ArtifactName;
            public string PathInArtifact;
            public string FullPath => $"{this.ArtifactName}/{this.PathInArtifact}";

            // Format: DisplayName:ArtifactName/Rest/Of/PathInArtifact
            public ArtifactEntryInfo(string unparsed)
            {
                var endOfDisplay = unparsed.IndexOf(':');
                var endOfArtifact = unparsed.IndexOf('/');
                if (endOfDisplay < 0 || endOfArtifact < 0 || endOfDisplay >= endOfArtifact)
                {
                    throw new ArgumentException("An artifact request definition must be of the form display:artifact/path");
                }
                this.DisplayName = unparsed[0..endOfDisplay];
                this.ArtifactName = unparsed[(endOfDisplay + 1)..endOfArtifact];
                this.PathInArtifact = unparsed[(endOfArtifact + 1)..];
            }

        }
    }
}
