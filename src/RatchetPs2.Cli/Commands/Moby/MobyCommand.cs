using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "moby",
            "Work with moby model files.",
            MobyExportGltfCommand.Build(),
            MobyPackCommand.Build(),
            MobyUnpackCommand.Build());
    }
}
