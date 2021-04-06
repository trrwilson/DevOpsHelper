using DevOpsMinClient;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsHelper.Commands
{
    public class CommandBase
    {
        protected CommandLineApplication baseCommand;
        protected OptionDefinition[] requiredOptions;
        protected ADOClient client;

        public CommandBase(CommandLineApplication command, OptionDefinition[] requiredOptions)
        {
            this.baseCommand = command;
            command.HelpOption("--help");
            this.requiredOptions = requiredOptions;
        }

        protected bool DoCommonSetup()
        {
            if (!this.baseCommand.CheckAndPrintMissingOptions(this.requiredOptions))
            {
                return false;
            }

            if (requiredOptions.Contains(OptionDefinition.Url))
            {
                this.client = new ADOClient(OptionDefinition.Url.ValueFrom(this.baseCommand))
                {
                    PersonalAccessToken = OptionDefinition.AccessToken.ValueFrom(this.baseCommand),
                };
            }


            return true;
        }
    }
}
