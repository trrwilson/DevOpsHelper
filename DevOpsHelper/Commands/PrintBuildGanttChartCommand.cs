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
    class PrintBuildGanttChart : CommandBase
    {
        public static void Init(CommandLineApplication command)
        {
            command.Description = "Compare PR artifact sizes to reference and post differences";

            // GET https://msasg.visualstudio.com/Skyman/_apis/build/builds/26886480/timeline/{timelineId}?api-version=5.0
            var requiredOptions = new OptionDefinition[]
            {
                OptionDefinition.Url,
                OptionDefinition.AccessToken,
                OptionDefinition.FindFailures.BuildId,
            };

            command.AddOptions(requiredOptions);

            var localCommand = new PrintBuildGanttChart(command, requiredOptions);
            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public PrintBuildGanttChart(CommandLineApplication command, OptionDefinition[] options)
            : base(command, options) { }

        public async Task<int> RunAsync()
        {
            if (!base.DoCommonSetup()) return -1;

            var buildId = OptionDefinition.FindFailures.BuildId.ValueFrom(this.baseCommand);
            var tasks = await this.client.GetBuildTimelineRecordsAsync(buildId);
            var jobGroups = tasks.Where(task => task.Type == "Job" && task.State == "completed")
                .OrderBy(job => job.Start)
                .ThenBy(job => job.Start.ToString("HH:mm"))
                .ThenBy(job => job.Finish)
                .ThenBy(job => job.Finish.ToString("HH:mm"))
                .GroupBy(task => task.ParentId)
                .ToList();

            Console.WriteLine("gantt");
            Console.WriteLine($"title Build {buildId} Timeline");
            Console.WriteLine("dateFormat HH:mm");
            Console.WriteLine("axisFormat %H:%M");

            for (int i = 0; i < jobGroups.Count; i++)
            {
                Console.WriteLine($"section {i}");// {jobGroup.First().ParentId}");
                foreach (var job in jobGroups[i])
                {
                    var jobOffset = job.Start - jobGroups[0].First().Start;
                    var jobMinutes = (int)Math.Round((job.Finish - job.Start).TotalMinutes);
                    Console.WriteLine($"{job.Name} "
                        + $": {(jobGroups[i] == jobGroups.First() || jobGroups[i] == jobGroups.Last() ? "milestone, " : "")}"
                        + $"{jobOffset.ToString(@"hh\:mm")}, {jobMinutes}m");
                }
            }

            return 0;
        }
    }
}
