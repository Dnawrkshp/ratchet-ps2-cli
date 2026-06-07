using System.Numerics;

namespace RatchetPs2.Core.Gltf;

public static class GltfCoordinateBasis
{
    public const string Ps2XzyBasisDescription = "gltf=(x,z,-y)_from_ps2";

    public static Vector3 FromPs2Position(short sourceX, short sourceY, short sourceZ, float scale)
    {
        return FromPs2Position(sourceX * scale, sourceY * scale, sourceZ * scale);
    }

    public static Vector3 FromPs2Position(float sourceX, float sourceY, float sourceZ)
    {
        return new Vector3(sourceX, sourceZ, -sourceY);
    }
}
