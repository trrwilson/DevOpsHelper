using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace DevOpsHelper.Commands
{
    class GetArtifactCommand : CommandBase
    {
        public static void Init(CommandLineApplication command)
        {
            command.Description = "Downloads a specified artifact from a pipeline build";

            var requiredOptions = new OptionDefinition[]
            {
                OptionDefinition.Url,
                OptionDefinition.AccessToken,
                OptionDefinition.GetArtifacts.Artifact,
                OptionDefinition.GetArtifacts.BuildId,
                OptionDefinition.GetArtifacts.Output,
            };
            command.AddOptions(requiredOptions);

            var localCommand = new GetArtifactCommand(command, requiredOptions);

            command.OnExecute(async () => await localCommand.RunAsync());
        }

        public async Task<int> RunAsync()
        {
            if (!this.DoCommonSetup()) return -1;

            var url = await client.GetArtifactDownloadUrlAsync(
                OptionDefinition.GetArtifacts.BuildId.ValueFrom(this.baseCommand),
                OptionDefinition.GetArtifacts.Artifact.ValueFrom(this.baseCommand));

            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(url);
            using var downloadStream = await response.Content.ReadAsStreamAsync();
            using var zipArchive = new ZipArchive(downloadStream);
            zipArchive.ExtractToDirectory(
                OptionDefinition.GetArtifacts.Output.ValueFrom(this.baseCommand),
                overwriteFiles: true);
            return 0;
        }

        public GetArtifactCommand(CommandLineApplication app, OptionDefinition[] options)
            : base(app, options) { }
    }
}
