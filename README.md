# DevOpsHelper
A minimal client library and command line application for batching automated use of Azure DevOps via its REST APIs.

### This is doing it wrong

This project, without a doubt, reinvents the wheel. Azure DevOps [already has .NET client libraries](https://docs.microsoft.com/en-us/azure/devops/integrate/concepts/dotnet-client-libraries?view=azure-devops) and creating a new one atop the REST APIs is philosophically objectionable.

Yet I did it anyway.
- Auth was very tough to get work with a corporate DevOps instance/project; the REST APIs together with a PAT made this very straightforward
- The official client libraries expose a *ton*, but not necessary in the ways I found intuitive or useful
### Structure

`DevOpsMinClient` is a reusable client library that exposes a collection of partial object models (deserialized from REST objects) and operations centralized on a `DevOpsClient` class.

`DevOpsMinClientTests` is a collection of tests for the client library.

`DevOpsHelper` is a CLI using the tools in the client library to do interesting, automation-focused tasks.

### Version history

|  |  |  |
|--|--|--|
| 0.0.1 | 04.06.21 | Initial submission. Bare functionality, very little error checking, still a lot of cleanup needed. |
