using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyCopyAnimationCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var animationGameOption = new Option<string>("--animation-game")
        {
            Description = "Game format of the animation source moby: UYA or DL.",
            Required = true
        };
        var inputOption = CommonOptions.InputFile("Path to the target moby model binary.");
        var animationSourceOption = new Option<FileInfo>("--animation-source")
        {
            Description = "Path to the moby model binary to copy animation data from.",
            Required = true
        };
        var animationOption = new Option<int>("--animation")
        {
            Description = "Animation index to copy and rewrite as animation 0.",
            Required = true
        };
        var outputOption = CommonOptions.OutputFile("Path to write the repacked moby model binary.");

        var command = CliCommandBuilder.Create(
            "copy-animation",
            "Copy one moby animation into another moby as animation 0.",
            gameOption,
            animationGameOption,
            inputOption,
            animationSourceOption,
            animationOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var animationGameValue = parseResult.GetValue(animationGameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var animationSourceFile = parseResult.GetValue(animationSourceOption);
            var animationIndex = parseResult.GetValue(animationOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA or DL.");
                return;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby copy-animation currently supports only UYA and DL target mobys. Received {gameId}.");
                return;
            }

            if (string.IsNullOrWhiteSpace(animationGameValue)
                || !GameIdParser.TryParse(animationGameValue, out var animationGameId))
            {
                parseResult.GetResult(animationGameOption)?.AddError(
                    $"Unsupported --animation-game value '{animationGameValue}'. Expected UYA or DL.");
                return;
            }

            if (animationGameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(animationGameOption)?.AddError(
                    $"Moby copy-animation currently supports only UYA and DL animation sources. Received {animationGameId}.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (animationSourceFile is null)
            {
                parseResult.GetResult(animationSourceOption)?.AddError("Missing required --animation-source option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError(
                    $"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            if (!animationSourceFile.Exists)
            {
                parseResult.GetResult(animationSourceOption)?.AddError(
                    $"Animation source file '{animationSourceFile.FullName}' does not exist.");
                return;
            }

            var targetFormat = MobyGameFormats.Resolve(gameModuleResolver, gameId);
            var animationFormat = MobyGameFormats.Resolve(gameModuleResolver, animationGameId);

            using var input = inputFile.OpenRead();
            using var animationSource = animationSourceFile.OpenRead();
            var targetModel = MobyModelReader.Read(
                input,
                new MobyModelReadOptions
                {
                    AnimationFormat = targetFormat
                });
            var sourceModel = MobyModelReader.Read(
                animationSource,
                new MobyModelReadOptions
                {
                    AnimationFormat = animationFormat
                });

            try
            {
                MobyAnimationSlicer.CopyAnimationAsZero(
                    targetModel,
                    sourceModel,
                    animationIndex,
                    animationFormat);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                parseResult.GetResult(animationOption)?.AddError(ex.Message);
                return;
            }
            catch (InvalidDataException ex)
            {
                parseResult.GetResult(animationOption)?.AddError(ex.Message);
                return;
            }

            var bytes = MobyModelPacker.Build(targetModel);

            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, bytes);

            Console.WriteLine(
                $"Copied {animationGameId} animation {animationIndex} from '{animationSourceFile.FullName}' into {gameId} moby '{inputFile.FullName}' as animation 0 at '{outputFile.FullName}' ({bytes.Length} bytes).");
        });

        return command;
    }
}
