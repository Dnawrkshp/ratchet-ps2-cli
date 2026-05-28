using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Experimental.Moby.Diagnostics;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyAnalyzeVertexControlCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var inputOption = new Option<FileInfo[]>("--input")
        {
            Description = "Path to one or more UYA/DL moby model binaries.",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        };
        var outputOption = CommonOptions.OutputFile("Path to write the vertex-control analysis JSON.");

        var command = CliCommandBuilder.Create(
            "analyze-vertex-control",
            "Analyze moby vertex row control words across one or more model binaries.",
            gameOption,
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFiles = parseResult.GetValue(inputOption);
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
                    $"Moby vertex-control analysis currently supports UYA and DL. Received {gameId}.");
                return;
            }

            if (inputFiles is null || inputFiles.Length == 0)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            var openedStreams = new List<FileStream>();
            try
            {
                var inputs = new List<MobyVertexControlAnalysisInput>();
                foreach (var inputFile in inputFiles)
                {
                    var stream = inputFile.OpenRead();
                    openedStreams.Add(stream);
                    inputs.Add(new MobyVertexControlAnalysisInput(inputFile.FullName, stream));
                }

                var analysis = MobyVertexControlAnalyzer.Analyze(
                    inputs,
                    new MobyModelReadOptions
                    {
                        AnimationFormat = MobyGameFormats.Resolve(gameModuleResolver, gameId)
                    });
                outputFile.Directory?.Create();
                File.WriteAllBytes(outputFile.FullName, MobyVertexControlAnalyzer.WriteJson(analysis));

                Console.WriteLine(
                    $"Analyzed {inputFiles.Length} {gameId} moby model(s) and wrote '{outputFile.FullName}'.");
            }
            finally
            {
                foreach (var stream in openedStreams)
                {
                    stream.Dispose();
                }
            }
        });

        return command;
    }
}
