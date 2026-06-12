using System.Numerics;

namespace RatchetPs2.Core.Geometry;

public static class TriangleGeometryUtils
{
    public static float Area(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).Length() * 0.5f;
    }

    public static bool HasEdgeLongerThan(Vector3 a, Vector3 b, Vector3 c, float maxEdgeLength)
    {
        var maxEdgeLengthSquared = maxEdgeLength * maxEdgeLength;
        return Vector3.DistanceSquared(a, b) > maxEdgeLengthSquared
            || Vector3.DistanceSquared(b, c) > maxEdgeLengthSquared
            || Vector3.DistanceSquared(c, a) > maxEdgeLengthSquared;
    }
}
