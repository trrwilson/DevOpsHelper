using DevOpsHelper.Commands;
using Microsoft.Extensions.CommandLineUtils;

namespace DevOpsHelper
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                FullName = "Azure DevOps Helper Tool",
            };
            app.VersionOption("--version", "0.0.1").ShowInHelpText = false;
            app.HelpOption("-?|--help").ShowInHelpText = false;
            app.Command("QueryBugs", QueryBugCommand.Init);
            app.Command("GetArtifact", GetArtifactCommand.Init);
            app.Command("CheckPRSize", CheckPRSizeCommand.Init);
            app.Command("UpdateTestFailureBugs", UpdateTestFailureBugsCommand.Init);
            app.Command("PrintBranchSizes", PrintBranchSizesCommand.Init);
            app.Command("FindFailures", FindFailuresCommand.Init);
            app.OnExecute(() =>
            {
                app.ShowHelp();
                return 1;
            });
            app.Execute(args);
        }
    }
}
