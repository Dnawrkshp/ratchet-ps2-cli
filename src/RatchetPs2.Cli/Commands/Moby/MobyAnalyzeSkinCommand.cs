using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Experimental.Moby.Diagnostics;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyAnalyzeSkinCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to a UYA/DL moby model binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the skin analysis JSON.");
        var decodeScaleOption = new Option<float?>("--decode-scale")
        {
            Description = "Override vertex decode scale. Defaults to moby scale / 1024."
        };

        var command = CliCommandBuilder.Create(
            "analyze-skin",
            "Analyze decoded moby skin joint bounds.",
            gameOption,
            inputOption,
            outputOption,
            decodeScaleOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var decodeScale = parseResult.GetValue(decodeScaleOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA or DL.");
                return;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby skin analysis currently supports UYA and DL. Received {gameId}.");
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
            var analysis = MobySkinAnalyzer.AnalyzeReferenceSkin(model, decodeScale);
            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, MobySkinAnalyzer.WriteJson(analysis));

            Console.WriteLine(
                $"Analyzed {gameId} moby skin '{inputFile.FullName}' and wrote '{outputFile.FullName}'.");
        });

        return command;
    }
}
