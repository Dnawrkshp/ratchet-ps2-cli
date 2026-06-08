using RatchetPs2.Cli.Commands;
using RatchetPs2.Cli.Commands.Hw3d;
using RatchetPs2.Cli.Commands.Moby;
using RatchetPs2.Cli.Commands.Pif;
using RatchetPs2.Cli.Commands.Shrub;
using RatchetPs2.Cli.Commands.Skybox;
using RatchetPs2.Cli.Commands.Tie;
using RatchetPs2.Cli.Commands.Wad;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.DL;
using RatchetPs2.Games.GC;
using RatchetPs2.Games.RC1;
using RatchetPs2.Games.UYA;
using System.CommandLine;

var rootCommand = new RootCommand("Cross-platform CLI for Ratchet & Clank PS2 tooling.");

var gameModules = new IGameModule[]
{
    new RC1GameModule(),
    new GCGameModule(),
    new UYAGameModule(),
    new DLGameModule()
};

var gameModuleResolver = new GameModuleResolver(gameModules);

rootCommand.Subcommands.Add(HelloCommand.Build(gameModuleResolver));
rootCommand.Subcommands.Add(Hw3dCommand.Build());
rootCommand.Subcommands.Add(MobyCommand.Build(gameModuleResolver));
rootCommand.Subcommands.Add(PifCommand.Build());
rootCommand.Subcommands.Add(ShrubCommand.Build());
rootCommand.Subcommands.Add(SkyboxCommand.Build());
rootCommand.Subcommands.Add(TieCommand.Build());
rootCommand.Subcommands.Add(WadCommand.Build());

return rootCommand.Parse(args).Invoke();
