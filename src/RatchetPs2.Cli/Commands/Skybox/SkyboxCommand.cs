using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Skybox;

internal static class SkyboxCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "skybox",
            "Work with skybox geometry files.",
            SkyboxExportGltfCommand.Build(),
            SkyboxExportGltfBatchCommand.Build());
    }
}
