using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Tie;

internal static class TieCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "tie",
            "Work with tie static world geometry files.",
            TieInspectCommand.Build(),
            TieExportGltfCommand.Build(),
            TieExportGltfBatchCommand.Build());
    }
}
