using RatchetPs2.Cli.Abstractions;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapCommand
{
    public static Command Build()
    {
        return CliCommandBuilder.Create(
            "map",
            "Work with full map extraction packages.",
            MapExtractCommand.Build(),
            MapExtractWadCommand.Build(),
            MapUnpackWadCommand.Build());
    }
}
