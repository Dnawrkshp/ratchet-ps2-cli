using System.Numerics;
using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static void ApplyCustomStaticTemplateDeform(
        IReadOnlyList<ImportedMesh> meshes,
        IReadOnlyDictionary<int, TemplateDecodedMesh> templateMeshes,
        int replaceMeshIndex)
    {
        foreach (var mesh in meshes)
        {
            if (replaceMeshIndex != -1 && mesh.TemplateMeshIndex != replaceMeshIndex)
            {
                continue;
            }

            if (!templateMeshes.TryGetValue(mesh.TemplateMeshIndex, out var templateMesh)
                || templateMesh.Positions.Count == 0)
            {
                throw new InvalidDataException(
                    $"Custom static template-preserve mode requires decoded template mesh {mesh.TemplateMeshIndex}.");
            }

            var sourceBounds = Bounds3.From(mesh.Positions);
            var templateBounds = Bounds3.From(templateMesh.Positions);
            mesh.Positions.Clear();
            foreach (var templatePosition in templateMesh.Positions)
            {
                var normalized = templateBounds.Normalize(templatePosition);
                mesh.Positions.Add(sourceBounds.Lerp(normalized));
            }

            mesh.Indices.Clear();
            mesh.Indices.AddRange(TriangleIndexUtils.BuildSequentialIndices(mesh.Positions.Count));
            mesh.Joints = templateMesh.Joints.Select(row => row.ToArray()).ToList();
            mesh.Weights = templateMesh.Weights.Select(row => row.ToArray()).ToList();
        }
    }

    private static void ApplyCustomStaticTemplateVertexLayout(
        IReadOnlyList<ImportedMesh> meshes,
        IReadOnlyDictionary<int, TemplateDecodedMesh> templateMeshes,
        int replaceMeshIndex)
    {
        foreach (var mesh in meshes)
        {
            if (replaceMeshIndex != -1 && mesh.TemplateMeshIndex != replaceMeshIndex)
            {
                continue;
            }

            if (!templateMeshes.TryGetValue(mesh.TemplateMeshIndex, out var templateMesh)
                || templateMesh.Positions.Count == 0)
            {
                throw new InvalidDataException(
                    $"Custom static template-vertex-layout mode requires decoded template mesh {mesh.TemplateMeshIndex}.");
            }

            if (mesh.Positions.Count > templateMesh.Positions.Count)
            {
                throw new InvalidDataException(
                    $"Custom static mesh has {mesh.Positions.Count} vertices, but template mesh {mesh.TemplateMeshIndex} has only {templateMesh.Positions.Count} vertices.");
            }

            var sourcePositions = mesh.Positions.ToArray();
            mesh.Positions.Clear();
            mesh.Positions.AddRange(sourcePositions);
            var fill = sourcePositions.Length == 0
                ? Vector3.Zero
                : sourcePositions.Aggregate(Vector3.Zero, (sum, value) => sum + value) / sourcePositions.Length;
            while (mesh.Positions.Count < templateMesh.Positions.Count)
            {
                mesh.Positions.Add(fill);
            }

            if (mesh.TexCoords is not null)
            {
                var sourceTexCoords = mesh.TexCoords.ToArray();
                var texCoordFill = sourceTexCoords.Length == 0
                    ? Vector2.Zero
                    : sourceTexCoords.Aggregate(Vector2.Zero, (sum, value) => sum + value) / sourceTexCoords.Length;
                while (mesh.TexCoords.Count < mesh.Positions.Count)
                {
                    mesh.TexCoords.Add(texCoordFill);
                }
            }

            mesh.Joints = templateMesh.Joints.Select(row => row.ToArray()).ToList();
            mesh.Weights = templateMesh.Weights.Select(row => row.ToArray()).ToList();
        }
    }

    private static void AddHiddenTemplateMeshes(
        List<ImportedMesh> meshes,
        IReadOnlyList<MobyMeshTableEntry> templateEntries,
        IReadOnlyDictionary<int, TemplateDecodedMesh> templateMeshes,
        int visibleMeshIndex)
    {
        var existing = meshes.Select(mesh => mesh.TemplateMeshIndex).ToHashSet();
        for (var meshIndex = 0; meshIndex < templateEntries.Count; meshIndex++)
        {
            if (meshIndex == visibleMeshIndex || existing.Contains(meshIndex))
            {
                continue;
            }

            if (!templateMeshes.TryGetValue(meshIndex, out var templateMesh)
                || templateMesh.Positions.Count == 0)
            {
                continue;
            }

            var center = templateMesh.Positions.Aggregate(Vector3.Zero, (sum, value) => sum + value) / templateMesh.Positions.Count;
            var positions = Enumerable.Repeat(center, templateMesh.Positions.Count).ToList();
            var hiddenMesh = new ImportedMesh(
                meshIndex,
                templateEntries[meshIndex].MeshType,
                positions,
                TriangleIndexUtils.BuildSequentialIndices(positions.Count),
                texCoords: null,
                templateMesh.Joints.Select(row => row.ToArray()).ToList(),
                templateMesh.Weights.Select(row => row.ToArray()).ToList(),
                metadata: null)
            {
                CustomStaticHideMesh = true
            };
            meshes.Add(hiddenMesh);
        }
    }
}
