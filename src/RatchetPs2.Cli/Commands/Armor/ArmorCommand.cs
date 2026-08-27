using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Armor;

internal static class ArmorCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "armor",
            "Work with player armor assets.",
            ArmorExtractMultiplayerWadCommand.Build(),
            ArmorExtractWadCommand.Build(),
            ArmorExportMultiplayerDzoCommand.Build(),
            ArmorExportDzoCommand.Build());
    }
}
