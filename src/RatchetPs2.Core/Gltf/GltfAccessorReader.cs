using System.Numerics;
using System.Text.Json;

namespace RatchetPs2.Core.Gltf;

public static class GltfAccessorReader
{
    public static List<byte[]> ReadBuffers(JsonElement root, Func<string, Stream> openBuffer)
    {
        ArgumentNullException.ThrowIfNull(openBuffer);

        var result = new List<byte[]>();
        foreach (var buffer in root.GetProperty("buffers").EnumerateArray())
        {
            if (!buffer.TryGetProperty("uri", out var uriElement))
            {
                throw new InvalidDataException("Only external or data URI glTF buffers are supported.");
            }

            var uri = uriElement.GetString() ?? throw new InvalidDataException("glTF buffer URI is empty.");
            if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                result.Add(ReadDataUriBuffer(uri));
                continue;
            }

            using var stream = openBuffer(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            result.Add(memory.ToArray());
        }

        return result;
    }

    public static List<Vector3> ReadVec3Accessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        var accessor = accessors[accessorIndex];
        RequireAccessor(accessor, componentType: 5126, type: "VEC3");
        var (buffer, offset, stride) = GetAccessorBuffer(accessor, bufferViews, buffers, 12);
        var count = accessor.GetProperty("count").GetInt32();
        var result = new List<Vector3>(count);
        for (var i = 0; i < count; i++)
        {
            var itemOffset = offset + i * stride;
            result.Add(new Vector3(
                BitConverter.ToSingle(buffer, itemOffset),
                BitConverter.ToSingle(buffer, itemOffset + 4),
                BitConverter.ToSingle(buffer, itemOffset + 8)));
        }

        return result;
    }

    public static List<float> ReadScalarFloatAccessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        var accessor = accessors[accessorIndex];
        RequireAccessor(accessor, componentType: 5126, type: "SCALAR");
        var (buffer, offset, stride) = GetAccessorBuffer(accessor, bufferViews, buffers, 4);
        var count = accessor.GetProperty("count").GetInt32();
        var result = new List<float>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(BitConverter.ToSingle(buffer, offset + i * stride));
        }

        return result;
    }

    public static List<Vector2> ReadVec2Accessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        var accessor = accessors[accessorIndex];
        RequireAccessor(accessor, componentType: 5126, type: "VEC2");
        var (buffer, offset, stride) = GetAccessorBuffer(accessor, bufferViews, buffers, 8);
        var count = accessor.GetProperty("count").GetInt32();
        var result = new List<Vector2>(count);
        for (var i = 0; i < count; i++)
        {
            var itemOffset = offset + i * stride;
            result.Add(new Vector2(
                BitConverter.ToSingle(buffer, itemOffset),
                BitConverter.ToSingle(buffer, itemOffset + 4)));
        }

        return result;
    }

    public static List<uint> ReadIndexAccessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        var accessor = accessors[accessorIndex];
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var bytesPerComponent = componentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            _ => throw new InvalidDataException($"Unsupported index component type {componentType}.")
        };
        RequireAccessor(accessor, componentType: null, type: "SCALAR");
        var (buffer, offset, stride) = GetAccessorBuffer(accessor, bufferViews, buffers, bytesPerComponent);
        var count = accessor.GetProperty("count").GetInt32();
        var result = new List<uint>(count);
        for (var i = 0; i < count; i++)
        {
            var itemOffset = offset + i * stride;
            result.Add(componentType switch
            {
                5121 => buffer[itemOffset],
                5123 => BitConverter.ToUInt16(buffer, itemOffset),
                5125 => BitConverter.ToUInt32(buffer, itemOffset),
                _ => throw new InvalidDataException($"Unsupported index component type {componentType}.")
            });
        }

        return result;
    }

    public static List<ushort[]> ReadVec4UShortAccessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        var accessor = accessors[accessorIndex];
        RequireAccessor(accessor, componentType: 5123, type: "VEC4");
        var (buffer, offset, stride) = GetAccessorBuffer(accessor, bufferViews, buffers, 8);
        var count = accessor.GetProperty("count").GetInt32();
        var result = new List<ushort[]>(count);
        for (var i = 0; i < count; i++)
        {
            var itemOffset = offset + i * stride;
            result.Add(
            [
                BitConverter.ToUInt16(buffer, itemOffset),
                BitConverter.ToUInt16(buffer, itemOffset + 2),
                BitConverter.ToUInt16(buffer, itemOffset + 4),
                BitConverter.ToUInt16(buffer, itemOffset + 6)
            ]);
        }

        return result;
    }

    public static List<float[]> ReadVec4FloatAccessor(
        int accessorIndex,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        var accessor = accessors[accessorIndex];
        RequireAccessor(accessor, componentType: 5126, type: "VEC4");
        var (buffer, offset, stride) = GetAccessorBuffer(accessor, bufferViews, buffers, 16);
        var count = accessor.GetProperty("count").GetInt32();
        var result = new List<float[]>(count);
        for (var i = 0; i < count; i++)
        {
            var itemOffset = offset + i * stride;
            result.Add(
            [
                BitConverter.ToSingle(buffer, itemOffset),
                BitConverter.ToSingle(buffer, itemOffset + 4),
                BitConverter.ToSingle(buffer, itemOffset + 8),
                BitConverter.ToSingle(buffer, itemOffset + 12)
            ]);
        }

        return result;
    }

    private static byte[] ReadDataUriBuffer(string uri)
    {
        var commaIndex = uri.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex < 0)
        {
            throw new InvalidDataException("Invalid glTF data URI buffer.");
        }

        var metadata = uri[..commaIndex];
        if (!metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only base64 glTF data URI buffers are supported.");
        }

        return Convert.FromBase64String(uri[(commaIndex + 1)..]);
    }

    private static (byte[] Buffer, int Offset, int Stride) GetAccessorBuffer(
        JsonElement accessor,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers,
        int defaultStride)
    {
        var bufferView = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
        var bufferIndex = bufferView.GetProperty("buffer").GetInt32();
        var buffer = buffers[bufferIndex];
        var offset = bufferView.TryGetProperty("byteOffset", out var viewOffset) ? viewOffset.GetInt32() : 0;
        offset += accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0;
        var stride = bufferView.TryGetProperty("byteStride", out var strideElement)
            ? strideElement.GetInt32()
            : defaultStride;
        return (buffer, offset, stride);
    }

    private static void RequireAccessor(JsonElement accessor, int? componentType, string type)
    {
        if (componentType is not null && accessor.GetProperty("componentType").GetInt32() != componentType.Value)
        {
            throw new InvalidDataException($"Expected glTF accessor component type {componentType.Value}.");
        }

        if (!string.Equals(accessor.GetProperty("type").GetString(), type, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected glTF accessor type {type}.");
        }
    }
}
