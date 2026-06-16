using System.Numerics;

namespace RatchetPs2.Core.Gltf;

public sealed class GltfBufferWriter
{
    public const int ArrayBufferTarget = 34962;
    public const int ElementArrayBufferTarget = 34963;
    public const int FloatComponentType = 5126;
    public const int UnsignedIntComponentType = 5125;
    public const int UnsignedByteComponentType = 5121;

    private readonly BinaryWriter _writer;

    public GltfBufferWriter(BinaryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public List<object> BufferViews { get; } = [];

    public List<object> Accessors { get; } = [];

    public int WriteVector3Accessor(
        IReadOnlyList<Vector3> values,
        int target = ArrayBufferTarget,
        bool includeMinMax = false)
    {
        ArgumentNullException.ThrowIfNull(values);

        Align(4);
        var byteOffset = checked((int)_writer.BaseStream.Position);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var value in values)
        {
            _writer.Write(value.X);
            _writer.Write(value.Y);
            _writer.Write(value.Z);
            min = Vector3.Min(min, value);
            max = Vector3.Max(max, value);
        }

        var bufferView = AddBufferView(byteOffset, values.Count * 3 * sizeof(float), target);
        var accessorDefinition = CreateAccessor(bufferView, FloatComponentType, values.Count, "VEC3");
        if (includeMinMax)
        {
            accessorDefinition["min"] = new[] { min.X, min.Y, min.Z };
            accessorDefinition["max"] = new[] { max.X, max.Y, max.Z };
        }

        return AddAccessor(accessorDefinition);
    }

    public int WriteVector2Accessor(IReadOnlyList<Vector2> values, int target = ArrayBufferTarget)
    {
        ArgumentNullException.ThrowIfNull(values);

        Align(4);
        var byteOffset = checked((int)_writer.BaseStream.Position);
        foreach (var value in values)
        {
            _writer.Write(value.X);
            _writer.Write(value.Y);
        }

        var bufferView = AddBufferView(byteOffset, values.Count * 2 * sizeof(float), target);
        return AddAccessor(CreateAccessor(bufferView, FloatComponentType, values.Count, "VEC2"));
    }

    public int WriteVector4Accessor(IReadOnlyList<Vector4> values, int target = ArrayBufferTarget)
    {
        ArgumentNullException.ThrowIfNull(values);

        Align(4);
        var byteOffset = checked((int)_writer.BaseStream.Position);
        foreach (var value in values)
        {
            _writer.Write(value.X);
            _writer.Write(value.Y);
            _writer.Write(value.Z);
            _writer.Write(value.W);
        }

        var bufferView = AddBufferView(byteOffset, values.Count * 4 * sizeof(float), target);
        return AddAccessor(CreateAccessor(bufferView, FloatComponentType, values.Count, "VEC4"));
    }

    public int WriteScalarFloatAccessor(
        IReadOnlyList<float> values,
        int target = ArrayBufferTarget,
        bool includeMinMax = false)
    {
        ArgumentNullException.ThrowIfNull(values);

        Align(4);
        var byteOffset = checked((int)_writer.BaseStream.Position);
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var value in values)
        {
            _writer.Write(value);
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        var bufferView = AddBufferView(byteOffset, values.Count * sizeof(float), target);
        var accessor = CreateAccessor(bufferView, FloatComponentType, values.Count, "SCALAR");
        if (includeMinMax)
        {
            accessor["min"] = new[] { values.Count == 0 ? 0f : min };
            accessor["max"] = new[] { values.Count == 0 ? 0f : max };
        }

        return AddAccessor(accessor);
    }

    public int WriteNormalizedByteVector4Accessor(IReadOnlyList<Vector4> values, int target = ArrayBufferTarget)
    {
        ArgumentNullException.ThrowIfNull(values);

        Align(4);
        var byteOffset = checked((int)_writer.BaseStream.Position);
        foreach (var value in values)
        {
            _writer.Write(ToNormalizedByte(value.X));
            _writer.Write(ToNormalizedByte(value.Y));
            _writer.Write(ToNormalizedByte(value.Z));
            _writer.Write(ToNormalizedByte(value.W));
        }

        var bufferView = AddBufferView(byteOffset, values.Count * 4, target);
        var accessor = CreateAccessor(bufferView, UnsignedByteComponentType, values.Count, "VEC4");
        accessor["normalized"] = true;
        return AddAccessor(accessor);
    }

    public int WriteUInt32IndexAccessor(IReadOnlyList<uint> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        Align(4);
        var byteOffset = checked((int)_writer.BaseStream.Position);
        foreach (var index in indices)
        {
            _writer.Write(index);
        }

        var bufferView = AddBufferView(byteOffset, indices.Count * sizeof(uint), ElementArrayBufferTarget);
        var accessor = CreateAccessor(bufferView, UnsignedIntComponentType, indices.Count, "SCALAR");
        accessor["min"] = new[] { indices.Count == 0 ? 0L : indices.Min(index => (long)index) };
        accessor["max"] = new[] { indices.Count == 0 ? 0L : indices.Max(index => (long)index) };
        return AddAccessor(accessor);
    }

    private int AddBufferView(int byteOffset, int byteLength, int target)
    {
        var bufferView = BufferViews.Count;
        BufferViews.Add(new
        {
            buffer = 0,
            byteOffset,
            byteLength,
            target
        });
        return bufferView;
    }

    private int AddAccessor(Dictionary<string, object> accessor)
    {
        var accessorIndex = Accessors.Count;
        Accessors.Add(accessor);
        return accessorIndex;
    }

    private static Dictionary<string, object> CreateAccessor(
        int bufferView,
        int componentType,
        int count,
        string type)
    {
        return new Dictionary<string, object>
        {
            ["bufferView"] = bufferView,
            ["byteOffset"] = 0,
            ["componentType"] = componentType,
            ["count"] = count,
            ["type"] = type
        };
    }

    private void Align(int alignment)
    {
        var remainder = _writer.BaseStream.Position % alignment;
        if (remainder != 0)
        {
            _writer.Write(new byte[alignment - remainder]);
        }
    }

    private static byte ToNormalizedByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }
}
