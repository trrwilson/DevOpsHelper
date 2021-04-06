using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Linq;

namespace DevOpsHelper
{
    public static class CommandLineUtilExtensions
    {
        public static CommandOption FindOption(
            this CommandLineApplication command,
            OptionDefinition definition,
            bool searchParents = true)
        {
            for (var thisLevel = command; thisLevel != null; thisLevel = searchParents ? thisLevel.Parent : null)
            {
                var matches = thisLevel.Options
                    .Where(option => option.Template.ToLower().Contains(definition.Template.ToLower()));
                if (matches.Any())
                {
                    return matches.First();
                }
            }
            return null;
        }

        public static void AddOptions(this CommandLineApplication command, params OptionDefinition[] options)
        {
            options.ToList().ForEach(option => command.Option(option.Template, option.Description, option.OptionType));
        }

        public static void AddOptions(this CommandLineApplication command, params OptionDefinition[][] options)
        {
            foreach (var optionGroup in options)
            {
                foreach (var option in optionGroup)
                {
                    _ = command.Option(option.Template, option.Description, option.OptionType);
                }
            }
        }

        public static bool CheckAndPrintMissingOptions(
            this CommandLineApplication command,
            params OptionDefinition[] options)
        {
            var missingOptions = options
                .Where(definition => string.IsNullOrEmpty(definition.ValueFrom(command)))
                .Select(definition => command.FindOption(definition));

            if (missingOptions.Any())
            {
                Console.WriteLine($"The '{command.Name}' command is missing the following required arguments:");
                foreach (var missingOption in missingOptions)
                {
                    Console.WriteLine(String.Format("  {0,-4} {1,-14} {2}",
                        string.IsNullOrEmpty(missingOption.ShortName) ? "" : $"-{missingOption.ShortName}",
                        string.IsNullOrEmpty(missingOption.LongName) ? "" : $"--{missingOption.LongName}",
                        missingOption.Description));
                }
                return false;
            }
            return true;
        }

    }
}
