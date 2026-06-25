using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.Versioning;

namespace RatchetPs2.Wasm;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    private static readonly JsonSerializerOptions WorkerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JSExport]
    public static string GetApiVersionExport() => GetApiVersion();

    [JSExport]
    public static string ParseDlGameplayCoreJson(byte[] gameplayBytes)
    {
        ArgumentNullException.ThrowIfNull(gameplayBytes);

        return JsonSerializer.Serialize(ParseDlGameplayCore(gameplayBytes), WorkerJsonOptions);
    }

    [JSExport]
    public static byte[] BuildDlLevelWadRenderPackageEnvelope(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = BuildDlLevelWadRenderPackage(levelWadBytes);
        var entriesJson = JsonSerializer.SerializeToUtf8Bytes(package.Entries, WorkerJsonOptions);
        var result = new byte[4 + entriesJson.Length + package.PackedBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), entriesJson.Length);
        entriesJson.CopyTo(result.AsSpan(4));
        package.PackedBytes.CopyTo(result.AsSpan(4 + entriesJson.Length));
        return result;
    }
}
