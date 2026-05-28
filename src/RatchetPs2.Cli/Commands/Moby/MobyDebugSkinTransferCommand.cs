using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Experimental.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyDebugSkinTransferCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the custom/static source glTF.");
        var skinReferenceOption = new Option<FileInfo>("--skin-reference")
        {
            Description = "Path to the UYA/DL moby whose decoded skin samples should drive the transfer.",
            Required = true
        };
        var outputOption = CommonOptions.OutputFile("Path to write the debug glTF.");
        var scaleOption = new Option<float>("--custom-static-scale") { Description = "Scale applied to source glTF positions.", DefaultValueFactory = _ => 1f };
        var yawOption = new Option<float>("--custom-static-yaw-degrees") { Description = "Yaw applied to source glTF positions." };
        var pitchOption = new Option<float>("--custom-static-pitch-degrees") { Description = "Pitch applied to source glTF positions." };
        var rollOption = new Option<float>("--custom-static-roll-degrees") { Description = "Roll applied to source glTF positions." };
        var outputScaleOption = new Option<float?>("--output-model-scale") { Description = "Override PS2 model scale used when decoding reference skin samples." };
        var splitConnectedOption = new Option<bool>("--split-connected-components") { Description = "Split source primitives into connected components before transfer." };
        var splitSideAxisOption = new Option<string?>("--split-side-axis") { Description = "Optionally split source triangles by side axis: x or z." };
        var splitSideDeadzoneOption = new Option<float>("--split-side-deadzone-ratio") { Description = "Deadzone ratio for source side splitting.", DefaultValueFactory = _ => 0.02f };
        var sampleCountOption = new Option<int>("--sample-count") { Description = "Nearest reference sample count.", DefaultValueFactory = _ => 1 };
        var verticalWindowOption = new Option<float?>("--vertical-window") { Description = "Optional vertical window for candidate filtering." };
        var sameSideOption = new Option<bool>("--same-side") { Description = "Restrict candidates to the same side of the fitted model." };
        var sideAxisOption = new Option<string>("--side-axis") { Description = "Side axis used by --same-side: x or z.", DefaultValueFactory = _ => "x" };
        var sideDeadzoneOption = new Option<float>("--side-deadzone-ratio") { Description = "Deadzone ratio for same-side filtering.", DefaultValueFactory = _ => 0.03f };
        var materialRegionsOption = new Option<bool>("--material-regions") { Description = "Apply material/anatomy region filtering." };
        var disableAnatomicalFiltersOption = new Option<bool>("--disable-anatomical-filters") { Description = "Skip humanoid anatomical candidate filters." };
        var triangleCoherentOption = new Option<bool>("--triangle-coherent") { Description = "Assign skinning using triangle-centroid coherence." };
        var splitPrimarySeamsOption = new Option<bool>("--split-primary-seams") { Description = "Split primary-joint seams during transfer." };
        var rigidMeshCentroidOption = new Option<bool>("--rigid-mesh-centroid") { Description = "Assign each mesh from its centroid." };
        var rigidTriangleCentroidOption = new Option<bool>("--rigid-triangle-centroid") { Description = "Assign each triangle from its centroid." };
        var smoothPrimaryIterationsOption = new Option<int>("--smooth-primary-iterations") { Description = "Primary-joint smoothing pass count." };
        var distancePowerOption = new Option<float>("--distance-power") { Description = "Distance power used when blending sample weights.", DefaultValueFactory = _ => 1f };
        var referenceYawOption = new Option<float>("--reference-yaw-degrees") { Description = "Yaw applied to fitted reference samples." };

        var command = CliCommandBuilder.Create(
            "debug-skin-transfer",
            "Export a joint-color glTF that visualizes custom moby skin-transfer decisions.",
            gameOption,
            inputOption,
            skinReferenceOption,
            outputOption,
            scaleOption,
            yawOption,
            pitchOption,
            rollOption,
            outputScaleOption,
            splitConnectedOption,
            splitSideAxisOption,
            splitSideDeadzoneOption,
            sampleCountOption,
            verticalWindowOption,
            sameSideOption,
            sideAxisOption,
            sideDeadzoneOption,
            materialRegionsOption,
            disableAnatomicalFiltersOption,
            triangleCoherentOption,
            splitPrimarySeamsOption,
            rigidMeshCentroidOption,
            rigidTriangleCentroidOption,
            smoothPrimaryIterationsOption,
            distancePowerOption,
            referenceYawOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var skinReferenceFile = parseResult.GetValue(skinReferenceOption);
            var outputFile = parseResult.GetValue(outputOption);
            var splitSideAxis = parseResult.GetValue(splitSideAxisOption);
            var sideAxis = parseResult.GetValue(sideAxisOption) ?? "x";
            var sampleCount = parseResult.GetValue(sampleCountOption);
            var distancePower = parseResult.GetValue(distancePowerOption);
            var smoothPrimaryIterations = parseResult.GetValue(smoothPrimaryIterationsOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError($"Unsupported --game value '{gameValue}'. Expected UYA or DL.");
                return;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError($"Moby skin-transfer debug supports UYA and DL. Received {gameId}.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (skinReferenceFile is null)
            {
                parseResult.GetResult(skinReferenceOption)?.AddError("Missing required --skin-reference option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError($"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            if (!skinReferenceFile.Exists)
            {
                parseResult.GetResult(skinReferenceOption)?.AddError($"Skin reference file '{skinReferenceFile.FullName}' does not exist.");
                return;
            }

            if (sampleCount < 1 || sampleCount > 16)
            {
                parseResult.GetResult(sampleCountOption)?.AddError("--sample-count must be between 1 and 16.");
                return;
            }

            if (distancePower <= 0f || !float.IsFinite(distancePower))
            {
                parseResult.GetResult(distancePowerOption)?.AddError("--distance-power must be greater than 0.");
                return;
            }

            if (smoothPrimaryIterations < 0 || smoothPrimaryIterations > 16)
            {
                parseResult.GetResult(smoothPrimaryIterationsOption)?.AddError("--smooth-primary-iterations must be between 0 and 16.");
                return;
            }

            if (!string.Equals(sideAxis, "x", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sideAxis, "z", StringComparison.OrdinalIgnoreCase))
            {
                parseResult.GetResult(sideAxisOption)?.AddError("--side-axis must be x or z.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(splitSideAxis)
                && !string.Equals(splitSideAxis, "x", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(splitSideAxis, "z", StringComparison.OrdinalIgnoreCase))
            {
                parseResult.GetResult(splitSideAxisOption)?.AddError("--split-side-axis must be x or z.");
                return;
            }

            outputFile.Directory?.Create();
            var bufferFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.buffer.bin");
            using var input = inputFile.OpenRead();
            using var skinReference = skinReferenceFile.OpenRead();
            using var output = File.Create(outputFile.FullName);
            using var buffer = File.Create(bufferFile);
            var inputDirectory = inputFile.DirectoryName ?? Directory.GetCurrentDirectory();
            MobySkinTransferDebugExporter.ExportGltf(
                input,
                bufferName => File.OpenRead(Path.Combine(inputDirectory, Uri.UnescapeDataString(bufferName))),
                skinReference,
                output,
                Path.GetFileName(bufferFile),
                buffer,
                new MobySkinTransferDebugOptions
                {
                    AnimationFormat = MobyGameFormats.Resolve(gameModuleResolver, gameId),
                    CustomStaticScale = parseResult.GetValue(scaleOption),
                    CustomStaticYawDegrees = parseResult.GetValue(yawOption),
                    CustomStaticPitchDegrees = parseResult.GetValue(pitchOption),
                    CustomStaticRollDegrees = parseResult.GetValue(rollOption),
                    SplitConnectedComponents = parseResult.GetValue(splitConnectedOption),
                    SplitSideAxis = splitSideAxis,
                    SplitSideDeadzoneRatio = parseResult.GetValue(splitSideDeadzoneOption),
                    OutputModelScale = parseResult.GetValue(outputScaleOption),
                    SampleCount = sampleCount,
                    VerticalWindow = parseResult.GetValue(verticalWindowOption),
                    SameSide = parseResult.GetValue(sameSideOption),
                    SideAxis = sideAxis,
                    SideDeadzoneRatio = parseResult.GetValue(sideDeadzoneOption),
                    MaterialRegions = parseResult.GetValue(materialRegionsOption),
                    DisableAnatomicalFilters = parseResult.GetValue(disableAnatomicalFiltersOption),
                    TriangleCoherent = parseResult.GetValue(triangleCoherentOption),
                    SplitPrimarySeams = parseResult.GetValue(splitPrimarySeamsOption),
                    RigidMeshCentroid = parseResult.GetValue(rigidMeshCentroidOption),
                    RigidTriangleCentroid = parseResult.GetValue(rigidTriangleCentroidOption),
                    SmoothPrimaryIterations = smoothPrimaryIterations,
                    DistancePower = distancePower,
                    ReferenceYawDegrees = parseResult.GetValue(referenceYawOption)
                });

            Console.WriteLine($"Wrote skin-transfer debug glTF '{outputFile.FullName}'.");
        });

        return command;
    }
}
