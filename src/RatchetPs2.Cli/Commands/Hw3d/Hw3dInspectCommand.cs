using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Hud.Hw3d;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Hw3d;

internal static class Hw3dInspectCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the hudw3d / HW3D binary file.");
        var outputOption = new Option<FileInfo?>("--output")
        {
            Description = "Optional path to write the structural report instead of printing only to stdout."
        };
        var svgOption = new Option<FileInfo?>("--svg")
        {
            Description = "Optional path to write a preliminary SVG visualization for supported HBN files."
        };

        var command = CliCommandBuilder.Create(
            "inspect",
            "Inspect an HW3D binary and dump the currently understood outer structure.",
            inputOption,
            outputOption,
            svgOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var svgFile = parseResult.GetValue(svgOption);

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            using var stream = inputFile.OpenRead();
            var archive = Hw3dReader.Read(stream);
            var report = Hw3dReader.Describe(archive);

            if (outputFile is not null)
            {
                outputFile.Directory?.Create();
                File.WriteAllText(outputFile.FullName, report);
            }

            if (svgFile is not null)
            {
                var svg = Hw3dReader.GenerateSvg(archive);
                if (svg is not null)
                {
                    svgFile.Directory?.Create();
                    File.WriteAllText(svgFile.FullName, svg);
                }
            }

            Console.WriteLine(report);
        });

        return command;
    }
}