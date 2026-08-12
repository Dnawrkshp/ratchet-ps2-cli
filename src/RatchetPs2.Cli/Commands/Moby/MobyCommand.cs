using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        return CliCommandBuilder.Create(
            "moby",
            "Work with moby model files.",
            MobyAnalyzeSkinCommand.Build(gameModuleResolver),
            MobyAnalyzeVertexControlCommand.Build(gameModuleResolver),
            MobyConvertToDlCommand.Build(),
            MobyCopyAnimationCommand.Build(gameModuleResolver),
            MobyDebugSkinTransferCommand.Build(gameModuleResolver),
            MobyDefaultAnimationCommand.Build(gameModuleResolver),
            MobyExportDzoCommand.Build(),
            MobyExportGltfCommand.Build(gameModuleResolver),
            MobyImportGltfCommand.Build(gameModuleResolver),
            MobyKeepAnimationCommand.Build(gameModuleResolver),
            MobyPackCommand.Build(gameModuleResolver),
            MobyRepackBinCommand.Build(gameModuleResolver),
            MobyUnpackCommand.Build(gameModuleResolver));
    }
}
