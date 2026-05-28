using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyKeepAnimationCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the source moby model binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the repacked moby model binary.");
        var animationOption = new Option<int>("--animation")
        {
            Description = "Animation index to keep and rewrite as animation 0.",
            Required = true
        };

        var command = CliCommandBuilder.Create(
            "keep-animation",
            "Repack a moby with one animation moved into slot 0.",
            gameOption,
            inputOption,
            outputOption,
            animationOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var animationIndex = parseResult.GetValue(animationOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA or DL for moby keep-animation.");
                return;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby keep-animation currently supports only UYA and DL. Received {gameId}.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
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

            using var input = inputFile.OpenRead();
            var model = MobyModelReader.Read(
                input,
                new MobyModelReadOptions
                {
                    AnimationFormat = MobyGameFormats.Resolve(gameModuleResolver, gameId)
                });

            try
            {
                MobyAnimationSlicer.KeepSingleAnimationAsZero(model, animationIndex);
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

            var bytes = MobyModelPacker.Build(model);

            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, bytes);

            Console.WriteLine(
                $"Repacked {gameId} moby '{inputFile.FullName}' with animation {animationIndex} as animation 0 to '{outputFile.FullName}' ({bytes.Length} bytes).");
        });

        return command;
    }
}
