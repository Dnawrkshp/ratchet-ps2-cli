using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;

namespace RatchetPs2.Core.Skyboxes;

public sealed record SkyboxGameProfile
{
    public static SkyboxGameProfile Default { get; } = new();

    public string GameLabel { get; init; } = "Skybox";

    public bool TextureIsSwizzled { get; init; }

    public bool UseDlLevel7LoaderRotationPatch { get; init; }

    public static SkyboxGameProfile ForGame(GameId gameId)
    {
        return gameId switch
        {
            GameId.DL => Default with
            {
                GameLabel = gameId.ToString(),
                TextureIsSwizzled = true,
                UseDlLevel7LoaderRotationPatch = true
            },
            GameId.UYA => Default with
            {
                GameLabel = gameId.ToString()
            },
            _ => throw new NotSupportedException($"Skybox glTF export does not support {gameId}.")
        };
    }

    public TextureConversionOptions CreateTextureConversionOptions()
    {
        return new TextureConversionOptions
        {
            IsSwizzled = TextureIsSwizzled
        };
    }

    public SkyboxGltfExportOptions CreateExportOptions(
        string? bufferFileName,
        int? levelNumber,
        int shellCount,
        bool includeDiagnostics = true,
        bool minify = false,
        GltfExportMetadataMode metadataMode = GltfExportMetadataMode.Full)
    {
        return new SkyboxGltfExportOptions
        {
            BufferFileName = bufferFileName,
            GameLabel = GameLabel,
            TextureConversionOptions = CreateTextureConversionOptions(),
            ShellRotationOverrides = ShellRotationOverridesFor(levelNumber, shellCount),
            IncludeDiagnostics = includeDiagnostics,
            Minify = minify,
            MetadataMode = metadataMode
        };
    }

    public IReadOnlyDictionary<int, SkyboxShellRotationOverride> ShellRotationOverridesFor(
        int? levelNumber,
        int shellCount)
    {
        if (shellCount <= 1
            || !UseDlLevel7LoaderRotationPatch
            || levelNumber is not { } level
            || !UsesDlLevel7SkyLoaderPatch(level))
        {
            return new Dictionary<int, SkyboxShellRotationOverride>();
        }

        var overrides = new Dictionary<int, SkyboxShellRotationOverride>(shellCount - 1);
        for (var shellIndex = 1; shellIndex < shellCount; shellIndex++)
        {
            overrides[shellIndex] = new SkyboxShellRotationOverride(
                RotationX: 0x2AAA,
                RotationY: 0x1D4C,
                Reason: "DL ParseSky loader patch for levels where Level % 0x14 == 7");
        }

        return overrides;
    }

    private static bool UsesDlLevel7SkyLoaderPatch(int levelNumber)
    {
        return levelNumber >= 1
            && levelNumber - 1 < 0x4F
            && levelNumber % 0x14 == 7;
    }
}
