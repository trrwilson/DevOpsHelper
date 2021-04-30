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
    class QueryBugCommand : CommandBase
    {
        public static void Init(CommandLineApplication command)
        {
            command.Description = "Compare PR artifact sizes to reference and post differences";

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

            command.AddOptions(requiredOptions);

            var localCommand = new QueryBugCommand(command, requiredOptions);
            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public QueryBugCommand(CommandLineApplication command, OptionDefinition[] options)
            : base(command, options) { }

        public async Task<int> RunAsync()
        {
            if (!base.DoCommonSetup()) return -1;

            await client.GetWorkItemsFromQueryAsync(OptionDefinition.UpdateTestFailureBugs.QueryId.ValueFrom(this.baseCommand));
            return 0;
        }
    }
}