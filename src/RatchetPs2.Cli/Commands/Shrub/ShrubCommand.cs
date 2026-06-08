using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Shrub;

internal static class ShrubCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "shrub",
            "Work with shrub static foliage geometry files.",
            ShrubExportGltfCommand.Build(),
            ShrubExportGltfBatchCommand.Build());
    }
}
