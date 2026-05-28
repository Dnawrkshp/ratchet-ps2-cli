using System.Numerics;
using System.Text.Json;

namespace RatchetPs2.Core.Gltf;

public static class GltfNodeTransforms
{
    public static IReadOnlyDictionary<int, List<Matrix4x4>> ReadMeshNodeTransforms(JsonElement root)
    {
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<int, List<Matrix4x4>>();
        }

        var transformsByMesh = new Dictionary<int, List<Matrix4x4>>();
        var visited = new bool[nodes.GetArrayLength()];
        var sceneRoots = ReadSceneRootNodes(root);
        if (sceneRoots.Count == 0)
        {
            sceneRoots.AddRange(Enumerable.Range(0, nodes.GetArrayLength()));
        }

        foreach (var nodeIndex in sceneRoots)
        {
            VisitGltfNode(nodeIndex, Matrix4x4.Identity);
        }

        for (var i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (!visited[i])
            {
                VisitGltfNode(i, Matrix4x4.Identity);
            }
        }

        return transformsByMesh;

        void VisitGltfNode(int nodeIndex, Matrix4x4 parentTransform)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
            {
                return;
            }

            visited[nodeIndex] = true;
            var node = nodes[nodeIndex];
            var worldTransform = ReadNodeLocalTransform(node) * parentTransform;
            if (node.TryGetProperty("mesh", out var meshElement))
            {
                var meshIndex = meshElement.GetInt32();
                if (!transformsByMesh.TryGetValue(meshIndex, out var transforms))
                {
                    transforms = [];
                    transformsByMesh.Add(meshIndex, transforms);
                }

                transforms.Add(worldTransform);
            }

            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var child in children.EnumerateArray())
            {
                VisitGltfNode(child.GetInt32(), worldTransform);
            }
        }
    }

    public static List<Vector3> TransformPositions(IReadOnlyList<Vector3> positions, Matrix4x4 transform)
    {
        if (transform == Matrix4x4.Identity)
        {
            return positions.Select(position => position).ToList();
        }

        return positions.Select(position => Vector3.Transform(position, transform)).ToList();
    }

    private static List<int> ReadSceneRootNodes(JsonElement root)
    {
        if (!root.TryGetProperty("scenes", out var scenes)
            || scenes.ValueKind != JsonValueKind.Array
            || scenes.GetArrayLength() == 0)
        {
            return [];
        }

        var sceneIndex = root.TryGetProperty("scene", out var sceneElement)
            ? sceneElement.GetInt32()
            : 0;
        if (sceneIndex < 0 || sceneIndex >= scenes.GetArrayLength())
        {
            return [];
        }

        var scene = scenes[sceneIndex];
        if (!scene.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return nodes.EnumerateArray().Select(node => node.GetInt32()).ToList();
    }

    private static Matrix4x4 ReadNodeLocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrixElement) && matrixElement.ValueKind == JsonValueKind.Array)
        {
            var values = matrixElement.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            if (values.Length == 16)
            {
                return new Matrix4x4(
                    values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7],
                    values[8], values[9], values[10], values[11],
                    values[12], values[13], values[14], values[15]);
            }
        }

        var scale = ReadVector3(node, "scale", Vector3.One);
        var rotation = ReadQuaternion(node, "rotation", Quaternion.Identity);
        var translation = ReadVector3(node, "translation", Vector3.Zero);
        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static Vector3 ReadVector3(JsonElement element, string propertyName, Vector3 fallback)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 3)
        {
            return fallback;
        }

        return new Vector3(values[0].GetSingle(), values[1].GetSingle(), values[2].GetSingle());
    }

    private static Quaternion ReadQuaternion(JsonElement element, string propertyName, Quaternion fallback)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 4)
        {
            return fallback;
        }

        return new Quaternion(values[0].GetSingle(), values[1].GetSingle(), values[2].GetSingle(), values[3].GetSingle());
    }
}
