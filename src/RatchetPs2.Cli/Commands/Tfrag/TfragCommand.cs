using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Tfrag;

internal static class TfragCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "tfrag",
            "Work with tfrag terrain geometry files.",
            TfragExportGltfCommand.Build(),
            TfragExportGltfBatchCommand.Build());
    }
}
