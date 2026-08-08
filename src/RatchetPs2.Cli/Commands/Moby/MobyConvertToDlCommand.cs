using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Moby;
using RatchetPs2.Games.DL.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyConvertToDlCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the UYA moby model binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the DL moby model binary.");
        var command = CliCommandBuilder.Create(
            "convert-to-dl",
            "Convert a UYA moby's standard animations and skeleton storage to DL format.",
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            if (inputFile is null || outputFile is null)
            {
                return;
            }
            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError($"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            using var input = inputFile.OpenRead();
            var model = MobyModelReader.Read(
                input,
                new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Standard });
            try
            {
                DlMobyConverter.ConvertFromUya(model);
            }
            catch (InvalidDataException ex)
            {
                parseResult.GetResult(inputOption)?.AddError(ex.Message);
                return;
            }

            var bytes = MobyModelPacker.Build(model);
            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, bytes);
            Console.WriteLine(
                $"Converted UYA moby '{inputFile.FullName}' to DL compact animations at '{outputFile.FullName}' ({bytes.Length} bytes).");
        });

        return command;
    }
}
