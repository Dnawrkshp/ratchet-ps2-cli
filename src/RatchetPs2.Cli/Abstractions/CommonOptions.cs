using System.CommandLine;

namespace RatchetPs2.Cli.Abstractions;

internal static class CommonOptions
{
    public static Option<string> Game() => new("--game")
    {
        Description = "Game id: 1, 2, 3, 4, RC1, GC, UYA, or DL",
        Required = true
    };

    public static Option<FileInfo> InputFile(string description = "Path to the input file") => new("--input")
    {
        Description = description,
        Required = true
    };

    public static Option<FileInfo> OutputFile(string description = "Path to the output file") => new("--output")
    {
        Description = description,
        Required = true
    };

    public static Option<bool> MinifyGltf() => new("--minify")
    {
        Description = "Write minified runtime glTF JSON and omit exporter-only metadata/diagnostics."
    };
}
