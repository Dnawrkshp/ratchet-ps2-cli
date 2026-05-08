using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Hw3d;

internal static class Hw3dCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "hw3d",
            "Inspect experimental HUD widget 3D (HW3D) files.",
            Hw3dInspectCommand.Build());
    }
}