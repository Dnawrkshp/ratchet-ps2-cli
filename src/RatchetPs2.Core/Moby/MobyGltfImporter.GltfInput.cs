using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static readonly Regex ExportedNodeNameRegex = ExportedNodeNamePattern();

    private static ImportedGltf ReadExporterShapedGltf(Stream gltf, Func<string, Stream> openBuffer)
    {
        using var document = JsonDocument.Parse(gltf);
        var root = document.RootElement;
        var buffers = GltfAccessorReader.ReadBuffers(root, openBuffer);
        var accessors = root.GetProperty("accessors");
        var bufferViews = root.GetProperty("bufferViews");
        var meshes = root.GetProperty("meshes");
        var nodes = root.GetProperty("nodes");
        var importedMeshes = new List<ImportedMesh>();

        for (var nodeIndex = 0; nodeIndex < nodes.GetArrayLength(); nodeIndex++)
        {
            var node = nodes[nodeIndex];
            if (!node.TryGetProperty("mesh", out var meshIndexElement) || !node.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString() ?? string.Empty;
            var match = ExportedNodeNameRegex.Match(name);
            if (!match.Success)
            {
                continue;
            }

            var templateMeshIndex = int.Parse(match.Groups["index"].Value);
            var meshTypeName = match.Groups["type"].Value;
            var parsedMeshTypeName = meshTypeName == "MeshType2" ? nameof(MobyMeshType.FarLod) : meshTypeName;
            if (!Enum.TryParse<MobyMeshType>(parsedMeshTypeName, out var meshType))
            {
                throw new InvalidDataException($"Unsupported exporter mesh type '{meshTypeName}' in node '{name}'.");
            }

            var gltfMesh = meshes[meshIndexElement.GetInt32()];
            var primitives = gltfMesh.GetProperty("primitives");
            var primitiveData = ReadExporterShapedMeshPrimitives(
                primitives,
                accessors,
                bufferViews,
                buffers,
                meshIndexElement.GetInt32(),
                $"Mesh node '{name}'");

            var metadata = TryReadMobyMeshMetadata(gltfMesh, node);
            importedMeshes.Add(new ImportedMesh(
                templateMeshIndex,
                meshType,
                primitiveData.Positions,
                primitiveData.Indices,
                primitiveData.TexCoords,
                primitiveData.Joints,
                primitiveData.Weights,
                metadata));
        }

        if (importedMeshes.Count == 0)
        {
            throw new InvalidDataException("No exporter-shaped moby mesh nodes were found. Expected names like node_0000_HighLod.");
        }

        return new ImportedGltf(importedMeshes);
    }

    private static GltfPrimitiveData ReadExporterShapedMeshPrimitives(
        JsonElement primitives,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers,
        int gltfMeshIndex,
        string context)
    {
        GltfPrimitiveData? result = null;
        PrimitiveAttributeSignature? resultSignature = null;
        for (var primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++)
        {
            var primitive = primitives[primitiveIndex];
            var primitiveData = GltfPrimitiveReader.ReadTrianglePrimitive(
                primitive,
                accessors,
                bufferViews,
                buffers,
                [],
                gltfMeshIndex,
                primitiveIndex,
                context);
            if (primitiveData is null)
            {
                continue;
            }

            var signature = ReadPrimitiveAttributeSignature(primitive);
            if (result is null)
            {
                result = primitiveData;
                resultSignature = signature;
                continue;
            }

            if (resultSignature == signature)
            {
                result.Indices.AddRange(primitiveData.Indices);
                continue;
            }

            var indexOffset = checked((uint)result.Positions.Count);
            result.Positions.AddRange(primitiveData.Positions);
            result.Indices.AddRange(primitiveData.Indices.Select(index => checked(index + indexOffset)));
            if (result.TexCoords is not null && primitiveData.TexCoords is not null)
            {
                result.TexCoords.AddRange(primitiveData.TexCoords);
            }
            else if (result.TexCoords is not null || primitiveData.TexCoords is not null)
            {
                throw new InvalidDataException($"{context} primitives must consistently provide TEXCOORD_0.");
            }

            if (result.Joints is not null && result.Weights is not null && primitiveData.Joints is not null && primitiveData.Weights is not null)
            {
                result.Joints.AddRange(primitiveData.Joints);
                result.Weights.AddRange(primitiveData.Weights);
            }
            else if (result.Joints is not null || result.Weights is not null || primitiveData.Joints is not null || primitiveData.Weights is not null)
            {
                throw new InvalidDataException($"{context} primitives must consistently provide JOINTS_0 and WEIGHTS_0.");
            }
        }

        return result ?? throw new InvalidDataException($"{context} must contain at least one triangle primitive.");
    }

    private static PrimitiveAttributeSignature ReadPrimitiveAttributeSignature(JsonElement primitive)
    {
        var attributes = primitive.GetProperty("attributes");
        return new PrimitiveAttributeSignature(
            ReadAttributeAccessor(attributes, "POSITION"),
            ReadAttributeAccessor(attributes, "TEXCOORD_0"),
            ReadAttributeAccessor(attributes, "JOINTS_0"),
            ReadAttributeAccessor(attributes, "WEIGHTS_0"));
    }

    private static int? ReadAttributeAccessor(JsonElement attributes, string name)
    {
        return attributes.TryGetProperty(name, out var accessor) ? accessor.GetInt32() : null;
    }

    private readonly record struct PrimitiveAttributeSignature(
        int? Position,
        int? TexCoord,
        int? Joints,
        int? Weights);

    private static ImportedGltf ReadCustomStaticGltf(
        Stream gltf,
        Func<string, Stream> openBuffer,
        List<MobyMeshTableEntry> templateEntries,
        int replaceMeshIndex,
        bool splitMeshes,
        bool expandTemplateMeshes,
        bool isolatedTriangleTopology,
        int? maxTrianglesPerMesh,
        int? maxGeneratedMeshes,
        int? maxHighLodMeshes,
        int? initialTriangleCap,
        int? initialTriangleCount,
        MobyGltfImportOptions options)
    {
        if (replaceMeshIndex < -1 || replaceMeshIndex >= templateEntries.Count)
        {
            throw new InvalidDataException(
                $"Custom static replace mesh index {replaceMeshIndex} is outside the template mesh table.");
        }

        using var document = JsonDocument.Parse(gltf);
        var root = document.RootElement;
        var buffers = GltfAccessorReader.ReadBuffers(root, openBuffer);
        var accessors = root.GetProperty("accessors");
        var bufferViews = root.GetProperty("bufferViews");
        var meshes = root.GetProperty("meshes");
        var materialNames = GltfPrimitiveReader.ReadMaterialNames(root);
        if (meshes.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Custom static glTF has no meshes.");
        }

        var meshNodeTransforms = GltfNodeTransforms.ReadMeshNodeTransforms(root);
        var sourceMeshes = ReadCustomStaticSourceMeshes(meshes, accessors, bufferViews, buffers, materialNames, meshNodeTransforms, options);
        if (options.CustomStaticSplitConnectedComponents)
        {
            sourceMeshes = SplitCustomStaticSourcesByConnectedComponents(
                sourceMeshes,
                options.CustomStaticSplitConnectedComponentMinTriangles);
        }
        if (!string.IsNullOrWhiteSpace(options.CustomStaticSplitSideAxis))
        {
            sourceMeshes = SplitCustomStaticSourcesBySide(
                sourceMeshes,
                options.CustomStaticSplitSideAxis,
                options.CustomStaticSplitSideDeadzoneRatio);
        }
        if (options.CustomStaticSplitAnatomicalRegions
            || (options.CustomStaticTransferReferenceSkinning && options.CustomStaticReferenceSkinningMaterialRegions))
        {
            sourceMeshes = SplitCustomStaticSourcesByAnatomicalRegion(sourceMeshes, options.CustomStaticSplitAnatomicalRegions);
        }
        if (sourceMeshes.Count == 0)
        {
            throw new InvalidDataException("Custom static glTF has no triangle primitives.");
        }

        var originalTemplateMeshCount = templateEntries.Count;
        if (options.CustomStaticGenerateMeshTable)
        {
            templateEntries.Clear();
            templateEntries.Add(CreateGeneratedCustomStaticMeshEntry(
                MobyMeshType.HighLod,
                checked((byte)Math.Clamp(options.CustomStaticGeneratedMeshSlotCapacity, 3, 127))));
            replaceMeshIndex = 0;
            expandTemplateMeshes = true;
        }

        var targetMeshIndices = replaceMeshIndex == -1
            ? Enumerable.Range(0, templateEntries.Count)
                .Where(meshIndex => IsCustomStaticBodyMeshTarget(templateEntries[meshIndex]))
                .ToList()
            : splitMeshes
                ? expandTemplateMeshes
                    ? [replaceMeshIndex]
                    : Enumerable.Range(replaceMeshIndex, templateEntries.Count - replaceMeshIndex).ToList()
                : [replaceMeshIndex];
        if (targetMeshIndices.Count == 0)
        {
            throw new InvalidDataException("Custom static import found no body mesh entries to replace.");
        }
        if (options.CustomStaticGenerateCompactRigidRows || options.CustomStaticPreserveTemplateRowContract)
        {
            targetMeshIndices = targetMeshIndices
                .Where(meshIndex => IsCompactRigidRowCompatible(templateEntries[meshIndex]))
                .ToList();
            if (targetMeshIndices.Count == 0)
            {
                throw new InvalidDataException("Custom static compact rigid-row mode found no compatible donor mesh entries.");
            }
        }

        var splitSourceMeshes = splitMeshes
            ? OrderCustomStaticSourcesForSplit(sourceMeshes, options)
            : sourceMeshes;
        var importedMeshes = splitMeshes
            ? SplitCustomStaticMeshes(splitSourceMeshes, templateEntries, targetMeshIndices, expandTemplateMeshes, replaceMeshIndex, isolatedTriangleTopology, maxTrianglesPerMesh, maxGeneratedMeshes, maxHighLodMeshes, initialTriangleCap, initialTriangleCount, options.CustomStaticUseMinimalExpandedMeshSlots, options.CustomStaticStrictTriangleCap, options.CustomStaticGenerateMinimalVifContainer, options.CustomStaticCompactTopologyPacket)
            : BuildUnsplitCustomStaticMeshes(sourceMeshes[0], templateEntries, targetMeshIndices);
        return new ImportedGltf(importedMeshes, splitSourceMeshes, originalTemplateMeshCount);
    }

    private static bool IsCustomStaticBodyMeshTarget(MobyMeshTableEntry entry)
    {
        return entry.MeshType is not MobyMeshType.Bangle and not MobyMeshType.Metal;
    }

    private static bool IsCompactRigidRowCompatible(MobyMeshTableEntry entry)
    {
        var data = entry.VertexData;
        if (data.Length < 0x10)
        {
            return false;
        }

        var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
        var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
        var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
        var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
        var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;
        return vertexTableOffset > 0
            && vertexTableOffset % 0x10 == 0
            && inFileVertexCount > 0
            && vertexTableOffset <= data.Length
            && vertexTableOffset + inFileVertexCount * 0x10 <= data.Length;
    }

    private static List<CustomStaticSourceMesh> ReadCustomStaticSourceMeshes(
        JsonElement meshes,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers,
        IReadOnlyList<string?> materialNames,
        IReadOnlyDictionary<int, List<Matrix4x4>> meshNodeTransforms,
        MobyGltfImportOptions options)
    {
        var sourceMeshes = new List<CustomStaticSourceMesh>();
        for (var meshIndex = 0; meshIndex < meshes.GetArrayLength(); meshIndex++)
        {
            var gltfMesh = meshes[meshIndex];
            var transforms = meshNodeTransforms.TryGetValue(meshIndex, out var nodeTransforms) && nodeTransforms.Count > 0
                ? nodeTransforms
                : [Matrix4x4.Identity];
            var primitives = gltfMesh.GetProperty("primitives");
            for (var primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++)
            {
                var primitive = primitives[primitiveIndex];
                var primitiveData = GltfPrimitiveReader.ReadTrianglePrimitive(
                    primitive,
                    accessors,
                    bufferViews,
                    buffers,
                    materialNames,
                    meshIndex,
                    primitiveIndex,
                    $"Custom static glTF primitive {meshIndex}:{primitiveIndex}");
                if (primitiveData is null)
                {
                    continue;
                }

                var positions = primitiveData.Positions;
                var indices = primitiveData.Indices;
                var texCoords = primitiveData.TexCoords;
                var joints = primitiveData.Joints;
                var weights = primitiveData.Weights;
                var materialIndex = primitiveData.MaterialIndex;
                var materialName = primitiveData.MaterialName;
                var appliedUvScale = GltfTexCoordUtils.ApplyMaterialUvScale(
                    texCoords,
                    materialName,
                    options.CustomStaticMaterialUvScales);
                var clampedUvComponentCount = options.CustomStaticClampUvs
                    ? GltfTexCoordUtils.ClampToUnitRange(texCoords)
                    : 0;

                foreach (var transform in transforms)
                {
                    var transformedPositions = GltfNodeTransforms.TransformPositions(positions, transform);
                    sourceMeshes.Add(new CustomStaticSourceMesh(
                        meshIndex,
                        primitiveIndex,
                        materialIndex,
                        materialName,
                        appliedUvScale,
                        clampedUvComponentCount,
                        null,
                        null,
                        transformedPositions,
                        indices.Select(index => index).ToList(),
                        Enumerable.Range(0, indices.Count / 3).ToList(),
                        texCoords?.Select(texCoord => texCoord).ToList(),
                        joints?.Select(row => row.ToArray()).ToList(),
                        weights?.Select(row => row.ToArray()).ToList()));
                }
            }
        }

        return sourceMeshes;
    }

    private static List<CustomStaticSourceMesh> SplitCustomStaticSourcesByForcedSourceTriangleSkinJoints(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        IReadOnlyList<MobyGltfSourceTriangleSkinJoint> forcedSourceTriangleSkinJoints)
    {
        var result = new List<CustomStaticSourceMesh>();
        foreach (var source in sources)
        {
            var matchingRules = forcedSourceTriangleSkinJoints
                .Where(rule => rule.MeshIndex == source.MeshIndex && rule.PrimitiveIndex == source.PrimitiveIndex)
                .ToList();
            if (matchingRules.Count == 0 || source.Indices.Count < 3)
            {
                result.Add(source);
                continue;
            }

            var triangleCount = source.Indices.Count / 3;
            var forcedByJoint = new SortedDictionary<ushort, List<uint>>();
            var unforcedIndices = new List<uint>();
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                var rule = matchingRules.FirstOrDefault(item => item.TriangleIndices.Contains(triangleIndex));
                List<uint> target;
                if (rule is null)
                {
                    target = unforcedIndices;
                }
                else if (!forcedByJoint.TryGetValue(rule.Joint, out target!))
                {
                    target = [];
                    forcedByJoint.Add(rule.Joint, target);
                }
                var offset = triangleIndex * 3;
                target.Add(source.Indices[offset]);
                target.Add(source.Indices[offset + 1]);
                target.Add(source.Indices[offset + 2]);
            }

            if (unforcedIndices.Count >= 3)
            {
                result.Add(BuildCustomStaticRegionSourceMesh(source, unforcedIndices) with { ForcedSkinJoint = null });
            }

            foreach (var (joint, forcedIndices) in forcedByJoint)
            {
                if (forcedIndices.Count >= 3)
                {
                    result.Add(BuildCustomStaticRegionSourceMesh(source, forcedIndices) with { ForcedSkinJoint = joint });
                }
            }
        }

        return result;
    }

    private static List<CustomStaticSourceMesh> SplitCustomStaticSourcesByConnectedComponents(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        int minStandaloneTriangles)
    {
        var result = new List<CustomStaticSourceMesh>();
        var resolvedMinStandaloneTriangles = Math.Max(0, minStandaloneTriangles);
        foreach (var source in sources)
        {
            if (source.Indices.Count < 6)
            {
                result.Add(source);
                continue;
            }

            var triangleCount = source.Indices.Count / 3;
            var trianglesByVertex = new Dictionary<uint, List<int>>();
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                var offset = triangleIndex * 3;
                for (var corner = 0; corner < 3; corner++)
                {
                    var sourceIndex = source.Indices[offset + corner];
                    if (sourceIndex >= source.Positions.Count)
                    {
                        continue;
                    }

                    if (!trianglesByVertex.TryGetValue(sourceIndex, out var triangles))
                    {
                        triangles = [];
                        trianglesByVertex.Add(sourceIndex, triangles);
                    }

                    triangles.Add(triangleIndex);
                }
            }

            var visited = new bool[triangleCount];
            var components = new List<List<int>>();
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                if (visited[triangleIndex])
                {
                    continue;
                }

                var component = new List<int>();
                var stack = new Stack<int>();
                visited[triangleIndex] = true;
                stack.Push(triangleIndex);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    component.Add(current);
                    var offset = current * 3;
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var sourceIndex = source.Indices[offset + corner];
                        if (!trianglesByVertex.TryGetValue(sourceIndex, out var neighbors))
                        {
                            continue;
                        }

                        foreach (var neighbor in neighbors)
                        {
                            if (visited[neighbor])
                            {
                                continue;
                            }

                            visited[neighbor] = true;
                            stack.Push(neighbor);
                        }
                    }
                }

                components.Add(component);
            }

            if (components.Count <= 1)
            {
                result.Add(source);
                continue;
            }

            var bundledSmallComponentIndices = new List<uint>();
            foreach (var component in components.OrderBy(component => component.Min()))
            {
                var componentIndices = new List<uint>(component.Count * 3);
                foreach (var triangleIndex in component.Order())
                {
                    var offset = triangleIndex * 3;
                    componentIndices.Add(source.Indices[offset]);
                    componentIndices.Add(source.Indices[offset + 1]);
                    componentIndices.Add(source.Indices[offset + 2]);
                }

                if (componentIndices.Count < 3)
                {
                    continue;
                }

                if (component.Count < resolvedMinStandaloneTriangles)
                {
                    bundledSmallComponentIndices.AddRange(componentIndices);
                }
                else
                {
                    result.Add(BuildCustomStaticRegionSourceMesh(source, componentIndices));
                }
            }

            if (bundledSmallComponentIndices.Count >= 3)
            {
                result.Add(BuildCustomStaticRegionSourceMesh(source, bundledSmallComponentIndices));
            }
        }

        return result;
    }

    private static List<CustomStaticSourceMesh> SplitCustomStaticSourcesBySide(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        string? sideAxis,
        float sideDeadzoneRatio)
    {
        var sideAxisIsZ = string.Equals(sideAxis, "z", StringComparison.OrdinalIgnoreCase);
        var resolvedDeadzoneRatio = float.IsFinite(sideDeadzoneRatio) && sideDeadzoneRatio >= 0f
            ? sideDeadzoneRatio
            : 0.02f;
        var result = new List<CustomStaticSourceMesh>();
        foreach (var source in sources)
        {
            if (source.Indices.Count < 6 || source.Positions.Count == 0)
            {
                result.Add(source);
                continue;
            }

            var bounds = Bounds3.From(source.Positions);
            var min = GetCustomStaticSideCoordinate(bounds.Min, sideAxisIsZ);
            var max = GetCustomStaticSideCoordinate(bounds.Max, sideAxisIsZ);
            var center = (min + max) * 0.5f;
            var deadzone = MathF.Max(0.001f, MathF.Abs(max - min) * resolvedDeadzoneRatio);
            var trianglesBySide = new SortedDictionary<int, List<uint>>();
            for (var i = 0; i + 2 < source.Indices.Count; i += 3)
            {
                var a = source.Indices[i];
                var b = source.Indices[i + 1];
                var c = source.Indices[i + 2];
                if (a >= source.Positions.Count || b >= source.Positions.Count || c >= source.Positions.Count)
                {
                    continue;
                }

                var centroid = (source.Positions[(int)a] + source.Positions[(int)b] + source.Positions[(int)c]) / 3f;
                var coordinate = GetCustomStaticSideCoordinate(centroid, sideAxisIsZ);
                var side = coordinate < center - deadzone
                    ? -1
                    : coordinate > center + deadzone
                        ? 1
                        : 0;
                if (!trianglesBySide.TryGetValue(side, out var triangles))
                {
                    triangles = [];
                    trianglesBySide.Add(side, triangles);
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            if (trianglesBySide.Count <= 1)
            {
                result.Add(source);
                continue;
            }

            foreach (var (_, sideIndices) in trianglesBySide)
            {
                if (sideIndices.Count >= 3)
                {
                    result.Add(BuildCustomStaticRegionSourceMesh(source, sideIndices));
                }
            }
        }

        return result;
    }

    private static float GetCustomStaticSideCoordinate(Vector3 position, bool sideAxisIsZ)
        => sideAxisIsZ ? position.Z : position.X;

    private static List<CustomStaticSourceMesh> SplitCustomStaticSourcesByAnatomicalRegion(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        bool splitGenericMaterials)
    {
        var result = new List<CustomStaticSourceMesh>();
        foreach (var source in sources)
        {
            var material = source.MaterialName?.Trim().ToLowerInvariant();
            if ((!splitGenericMaterials && material is not ("torso" or "legs")) || source.Indices.Count < 6)
            {
                result.Add(source);
                continue;
            }

            var bounds = Bounds3.From(source.Positions);
            var height = MathF.Max(0.0001f, bounds.Max.Y - bounds.Min.Y);
            var trianglesByRegion = new SortedDictionary<int, List<uint>>();
            for (var i = 0; i + 2 < source.Indices.Count; i += 3)
            {
                var a = source.Indices[i];
                var b = source.Indices[i + 1];
                var c = source.Indices[i + 2];
                if (a >= source.Positions.Count || b >= source.Positions.Count || c >= source.Positions.Count)
                {
                    continue;
                }

                var centroid = (source.Positions[(int)a] + source.Positions[(int)b] + source.Positions[(int)c]) / 3f;
                var normalizedY = (centroid.Y - bounds.Min.Y) / height;
                var region = ResolveCustomStaticAnatomicalSourceRegion(material, centroid.Z, normalizedY, splitGenericMaterials);
                if (!trianglesByRegion.TryGetValue(region, out var triangles))
                {
                    triangles = [];
                    trianglesByRegion.Add(region, triangles);
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            if (trianglesByRegion.Count <= 1)
            {
                result.Add(source);
                continue;
            }

            foreach (var (_, regionIndices) in trianglesByRegion)
            {
                if (regionIndices.Count >= 3)
                {
                    result.Add(BuildCustomStaticRegionSourceMesh(source, regionIndices));
                }
            }
        }

        return result;
    }

    private static int ResolveCustomStaticAnatomicalSourceRegion(
        string? material,
        float z,
        float normalizedY,
        bool splitGenericMaterials)
    {
        if (material == "torso")
        {
            if (z < -0.22f)
            {
                return -1;
            }

            if (z > 0.22f)
            {
                return 1;
            }

            return 0;
        }

        if (splitGenericMaterials)
        {
            if (normalizedY <= 0.18f)
            {
                return z < -0.012f ? -30 : z > 0.012f ? 30 : 29;
            }

            if (normalizedY <= 0.43f)
            {
                return z < -0.012f ? -20 : z > 0.012f ? 20 : 19;
            }

            if (normalizedY >= 0.74f && MathF.Abs(z) <= 0.35f)
            {
                return 40;
            }

            if (normalizedY >= 0.52f && z < -0.26f)
            {
                return -40;
            }

            if (normalizedY >= 0.52f && z > 0.26f)
            {
                return 41;
            }

            return 0;
        }

        if (normalizedY > 0.43f)
        {
            return 0;
        }

        if (z < -0.012f)
        {
            return -1;
        }

        if (z > 0.012f)
        {
            return 1;
        }

        return 0;
    }

    private static CustomStaticSourceMesh BuildCustomStaticRegionSourceMesh(
        CustomStaticSourceMesh source,
        IReadOnlyList<uint> sourceIndices)
    {
        var localIndexBySourceIndex = new Dictionary<uint, uint>();
        var positions = new List<Vector3>();
        var indices = new List<uint>(sourceIndices.Count);
        var sourceTriangleIndices = source.SourceTriangleIndices is null ? null : new List<int>(sourceIndices.Count / 3);
        var texCoords = source.TexCoords is null ? null : new List<Vector2>();
        var joints = source.Joints is null ? null : new List<ushort[]>();
        var weights = source.Weights is null ? null : new List<float[]>();
        var sourceTriangleIndexByKey = source.SourceTriangleIndices is null
            ? null
            : BuildSourceTriangleIndexLookup(source);

        for (var i = 0; i < sourceIndices.Count; i++)
        {
            var sourceIndex = sourceIndices[i];
            if (!localIndexBySourceIndex.TryGetValue(sourceIndex, out var localIndex))
            {
                localIndex = checked((uint)positions.Count);
                localIndexBySourceIndex.Add(sourceIndex, localIndex);
                positions.Add(source.Positions[(int)sourceIndex]);
                texCoords?.Add(source.TexCoords![(int)sourceIndex]);
                joints?.Add(source.Joints![(int)sourceIndex].ToArray());
                weights?.Add(source.Weights![(int)sourceIndex].ToArray());
            }

            indices.Add(localIndex);
            if (sourceTriangleIndices is not null && i % 3 == 2)
            {
                var key = BuildTriangleKey(sourceIndices[i - 2], sourceIndices[i - 1], sourceIndices[i]);
                sourceTriangleIndices.Add(sourceTriangleIndexByKey is not null && sourceTriangleIndexByKey.TryGetValue(key, out var sourceTriangleIndex)
                    ? sourceTriangleIndex
                    : sourceTriangleIndices.Count);
            }
        }

        return source with
        {
            Positions = positions,
            Indices = indices,
            SourceTriangleIndices = sourceTriangleIndices,
            TexCoords = texCoords,
            Joints = joints,
            Weights = weights
        };
    }

    private static Dictionary<(uint A, uint B, uint C), int> BuildSourceTriangleIndexLookup(CustomStaticSourceMesh source)
    {
        var result = new Dictionary<(uint A, uint B, uint C), int>();
        var triangleCount = source.Indices.Count / 3;
        for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            var offset = triangleIndex * 3;
            var key = BuildTriangleKey(source.Indices[offset], source.Indices[offset + 1], source.Indices[offset + 2]);
            var sourceTriangleIndex = source.SourceTriangleIndices is not null && triangleIndex < source.SourceTriangleIndices.Count
                ? source.SourceTriangleIndices[triangleIndex]
                : triangleIndex;
            result[key] = sourceTriangleIndex;
        }

        return result;
    }

    private static (uint A, uint B, uint C) BuildTriangleKey(uint a, uint b, uint c)
    {
        return (a, b, c);
    }

    private static List<CustomStaticSourceMesh> OrderCustomStaticSourcesForSplit(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        MobyGltfImportOptions options)
    {
        return sources
            .Select((source, originalOrder) => new
            {
                Source = source,
                OriginalOrder = originalOrder,
                TextureSortKey = GetCustomStaticTextureSortKey(source, options)
            })
            .OrderBy(item => item.TextureSortKey)
            .ThenBy(item => item.Source.MaterialIndex ?? int.MaxValue)
            .ThenBy(item => item.Source.MaterialName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OriginalOrder)
            .Select((item, splitOrder) => item.Source with
            {
                OriginalOrder = item.OriginalOrder,
                SplitOrder = splitOrder
            })
            .ToList();
    }

    private static int GetCustomStaticTextureSortKey(
        CustomStaticSourceMesh source,
        MobyGltfImportOptions options)
    {
        return source.MaterialName is not null
            && options.CustomStaticMaterialTextureIds is not null
            && options.CustomStaticMaterialTextureIds.TryGetValue(source.MaterialName, out var textureId)
                ? textureId
                : int.MaxValue;
    }


    private static List<ImportedMesh> BuildUnsplitCustomStaticMeshes(
        CustomStaticSourceMesh source,
        IReadOnlyList<MobyMeshTableEntry> templateEntries,
        IReadOnlyList<int> targetMeshIndices)
    {
        return targetMeshIndices
            .Select(meshIndex =>
            {
                var templateEntry = templateEntries[meshIndex];
                return new ImportedMesh(
                    meshIndex,
                    templateEntry.MeshType,
                    source.Positions.Select(position => position).ToList(),
                    source.Indices.Select(index => index).ToList(),
                    source.TexCoords?.Select(texCoord => texCoord).ToList(),
                    source.Joints?.Select(row => row.ToArray()).ToList(),
                    source.Weights?.Select(row => row.ToArray()).ToList(),
                    metadata: null)
                {
                    CustomStaticSourceMeshIndex = source.MeshIndex,
                    CustomStaticSourcePrimitiveIndex = source.PrimitiveIndex,
                    CustomStaticSourceMaterialIndex = source.MaterialIndex,
                    CustomStaticSourceMaterialName = source.MaterialName,
                    CustomStaticAppliedUvScale = source.AppliedUvScale,
                    CustomStaticSourceStartTriangle = 0,
                    CustomStaticSourceTriangleCount = source.Indices.Count / 3,
                    CustomStaticSourceTriangleIndices = source.SourceTriangleIndices?.ToList(),
                    CustomStaticForcedSkinJoint = source.ForcedSkinJoint
                };
            })
            .ToList();
    }

    private static List<ImportedMesh> SplitCustomStaticMeshes(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        List<MobyMeshTableEntry> templateEntries,
        List<int> targetMeshIndices,
        bool expandTemplateMeshes,
        int replaceMeshIndex,
        bool isolatedTriangleTopology,
        int? maxTrianglesPerMesh,
        int? maxGeneratedMeshes,
        int? maxHighLodMeshes,
        int? initialTriangleCap,
        int? initialTriangleCount,
        bool useMinimalExpandedMeshSlots,
        bool strictTriangleCap,
        bool generateMinimalVifContainer,
        bool compactTopologyPacket)
    {
        var importedMeshes = new List<ImportedMesh>();
        var targetOrdinal = 0;
        var clonePrototypeMeshIndex = replaceMeshIndex >= 0 ? replaceMeshIndex : targetMeshIndices[0];
        var highLodMeshLimit = maxHighLodMeshes is > 0
            ? maxHighLodMeshes.Value
            : DefaultCustomStaticMaxHighLodMeshes;
        var highLodMeshCount = 0;
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            var triangleOffset = 0;
            while (triangleOffset < source.Indices.Count)
            {
                if (maxGeneratedMeshes is > 0 && importedMeshes.Count >= maxGeneratedMeshes.Value)
                {
                    return importedMeshes;
                }

                if (targetOrdinal >= targetMeshIndices.Count)
                {
                    if (!expandTemplateMeshes)
                    {
                        throw new InvalidDataException(
                            $"Custom static split import ran out of template mesh entries while processing source mesh {source.MeshIndex}:{source.PrimitiveIndex}. " +
                            $"Created {importedMeshes.Count} chunks from {sources.Count} source primitives; more donor mesh entries or vertex-data generation are required.");
                    }

                    templateEntries.Add(useMinimalExpandedMeshSlots
                        ? templateEntries[clonePrototypeMeshIndex].VifData.Length == 0
                            ? CreateGeneratedCustomStaticMeshEntry(
                                templateEntries[clonePrototypeMeshIndex].MeshType,
                                templateEntries[clonePrototypeMeshIndex].VertexCount)
                            : CreateMinimalExpandedMeshEntry(templateEntries[clonePrototypeMeshIndex])
                        : CloneMeshEntry(templateEntries[clonePrototypeMeshIndex]));
                    targetMeshIndices.Add(templateEntries.Count - 1);
                }

                var targetMeshIndex = targetMeshIndices[targetOrdinal++];
                var templateEntry = templateEntries[targetMeshIndex];
                var maxVertices = ResolveCustomStaticChunkVertexCapacity(templateEntry, HasUsableSkinRows(source));
                var effectiveMaxTrianglesPerMesh = GetEffectiveMaxTrianglesPerMesh(
                    sources,
                    sourceIndex,
                    source,
                    triangleOffset,
                    maxTrianglesPerMesh,
                    initialTriangleCap,
                    initialTriangleCount,
                    !strictTriangleCap && templateEntry.MeshType == MobyMeshType.HighLod
                        ? highLodMeshLimit - highLodMeshCount
                        : null);
                CustomStaticChunk chunk;
                try
                {
                    chunk = BuildCustomStaticChunk(source, triangleOffset, maxVertices, templateEntry, isolatedTriangleTopology, effectiveMaxTrianglesPerMesh, generateMinimalVifContainer, compactTopologyPacket);
                }
                catch (InvalidDataException ex) when (ex.Message.Contains("cannot fit template topology payload budget", StringComparison.Ordinal))
                {
                    importedMeshes.Add(BuildHiddenCustomStaticMesh(targetMeshIndex, templateEntry));
                    continue;
                }

                triangleOffset = chunk.NextTriangleOffset;
                var importedMesh = new ImportedMesh(
                    targetMeshIndex,
                    templateEntry.MeshType,
                    chunk.Positions,
                    chunk.Indices,
                    chunk.TexCoords,
                    chunk.Joints,
                    chunk.Weights,
                    metadata: null)
                {
                    CustomStaticSourceMeshIndex = source.MeshIndex,
                    CustomStaticSourcePrimitiveIndex = source.PrimitiveIndex,
                    CustomStaticSourceMaterialIndex = source.MaterialIndex,
                    CustomStaticSourceMaterialName = source.MaterialName,
                    CustomStaticAppliedUvScale = source.AppliedUvScale,
                    CustomStaticSourceStartTriangle = chunk.StartTriangleOffset / 3,
                    CustomStaticSourceTriangleCount = chunk.SourceTriangleCount,
                    CustomStaticSourceTriangleIndices = chunk.SourceTriangleIndices,
                    CustomStaticForcedSkinJoint = source.ForcedSkinJoint
                };
                importedMeshes.Add(importedMesh);
                if (importedMesh.MeshType == MobyMeshType.HighLod)
                {
                    highLodMeshCount++;
                }
            }
        }

        return importedMeshes;
    }

    private static ImportedMesh BuildHiddenCustomStaticMesh(
        int targetMeshIndex,
        MobyMeshTableEntry templateEntry)
    {
        var vertexCount = Math.Max(0, (int)templateEntry.VertexCount);
        var positions = Enumerable.Repeat(Vector3.Zero, vertexCount).ToList();
        return new ImportedMesh(
            targetMeshIndex,
            templateEntry.MeshType,
            positions,
            TriangleIndexUtils.BuildSequentialIndices(vertexCount),
            texCoords: null,
            joints: null,
            weights: null,
            metadata: null)
        {
            CustomStaticHideMesh = true
        };
    }

    private static int ResolveCustomStaticChunkVertexCapacity(MobyMeshTableEntry templateEntry, bool sourceHasSkinRows)
    {
        const int compactEpilogueRows = 7;
        var maxVertices = Math.Min((int)templateEntry.VertexCount, 127);
        if (sourceHasSkinRows)
        {
            return Math.Max(3, maxVertices);
        }

        if (templateEntry.VertexData.Length >= 0x0C)
        {
            var headerCapacity = BitConverter.ToUInt16(templateEntry.VertexData, 0x0A);
            if (headerCapacity > compactEpilogueRows)
            {
                maxVertices = Math.Min(maxVertices, headerCapacity - compactEpilogueRows);
            }
        }

        return Math.Max(3, maxVertices);
    }

    private static bool HasUsableSkinRows(CustomStaticSourceMesh source)
    {
        return source.Joints is not null
            && source.Weights is not null
            && source.Joints.Count == source.Positions.Count
            && source.Weights.Count == source.Positions.Count
            && source.Weights.Any(row => row.Any(weight => weight > 0.00001f));
    }

    private static int? GetEffectiveMaxTrianglesPerMesh(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        int sourceIndex,
        CustomStaticSourceMesh source,
        int startTriangleOffset,
        int? defaultMaxTrianglesPerMesh,
        int? initialTriangleCap,
        int? initialTriangleCount,
        int? remainingHighLodMeshSlots)
    {
        var effectiveMaxTrianglesPerMesh = defaultMaxTrianglesPerMesh;
        if (source.MeshIndex == 0
            && source.PrimitiveIndex == 0
            && initialTriangleCap is > 0
            && initialTriangleCount is > 0
            && startTriangleOffset / 3 < initialTriangleCount.Value)
        {
            effectiveMaxTrianglesPerMesh = initialTriangleCap;
        }

        if (effectiveMaxTrianglesPerMesh is > 0 && remainingHighLodMeshSlots is > 0)
        {
            var remainingTriangles = CountRemainingCustomStaticTriangles(sources, sourceIndex, startTriangleOffset);
            var minimumTrianglesPerChunk = TriangleIndexUtils.DivideRoundUp(remainingTriangles, remainingHighLodMeshSlots.Value);
            if (minimumTrianglesPerChunk > effectiveMaxTrianglesPerMesh.Value)
            {
                effectiveMaxTrianglesPerMesh = minimumTrianglesPerChunk;
            }
        }

        return effectiveMaxTrianglesPerMesh;
    }

    private static int CountRemainingCustomStaticTriangles(
        IReadOnlyList<CustomStaticSourceMesh> sources,
        int sourceIndex,
        int startTriangleOffset)
    {
        var remaining = Math.Max(0, sources[sourceIndex].Indices.Count - startTriangleOffset) / 3;
        for (var i = sourceIndex + 1; i < sources.Count; i++)
        {
            remaining += sources[i].Indices.Count / 3;
        }

        return remaining;
    }

    private static MobyMeshTableEntry CloneMeshEntry(MobyMeshTableEntry source)
    {
        return new MobyMeshTableEntry
        {
            VifListOffset = 0,
            VifListSize = source.VifListSize,
            VifListTextureSize = source.VifListTextureSize,
            VertexDataOffset = 0,
            VertexDataSize = source.VertexDataSize,
            Unknown0A = source.Unknown0A,
            CommonTransformJointIndex = source.CommonTransformJointIndex,
            VertexCount = source.VertexCount,
            MeshType = source.MeshType,
            VifData = (byte[])source.VifData.Clone(),
            VertexData = (byte[])source.VertexData.Clone(),
            VifTextureData = source.VifTextureData is null ? null : (byte[])source.VifTextureData.Clone(),
            GifTag = source.GifTag is null
                ? null
                : new MobyGifTag
                {
                    TextureIds = (byte[])source.GifTag.TextureIds.Clone(),
                    GifDataOffset = source.GifTag.GifDataOffset
                }
        };
    }

    private static MobyMeshTableEntry CreateMinimalExpandedMeshEntry(MobyMeshTableEntry source)
    {
        var vertexTableOffset = TryReadValidVertexTableOffset(source.VertexData, out var offset)
            ? offset
            : 0x30;
        var vertexCount = Math.Max(1, (int)source.VertexCount);
        var vertexData = new byte[vertexTableOffset + vertexCount * 0x10];
        WriteGeneratedCompactVertexHeader(vertexData, vertexTableOffset, vertexCount);

        return new MobyMeshTableEntry
        {
            VifListOffset = 0,
            VifListSize = 0,
            VifListTextureSize = 0,
            VertexDataOffset = 0,
            VertexDataSize = checked((byte)(vertexData.Length / 0x10)),
            Unknown0A = source.Unknown0A,
            CommonTransformJointIndex = source.CommonTransformJointIndex,
            VertexCount = source.VertexCount,
            MeshType = source.MeshType,
            VifData = (byte[])source.VifData.Clone(),
            VertexData = vertexData,
            VifTextureData = BuildCustomStaticTextureMetadataPayload(),
            GifTag = new MobyGifTag
            {
                TextureIds = BuildEmptyGifTextureIdList(),
                GifDataOffset = 0
            }
        };
    }

    private static bool TryReadValidVertexTableOffset(byte[] data, out int vertexTableOffset)
    {
        vertexTableOffset = 0;
        if (data.Length < 0x10)
        {
            return false;
        }

        vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
        return vertexTableOffset > 0
            && vertexTableOffset % 0x10 == 0
            && vertexTableOffset <= data.Length;
    }

    private static CustomStaticChunk BuildCustomStaticChunk(
        CustomStaticSourceMesh source,
        int startTriangleOffset,
        int maxVertices,
        MobyMeshTableEntry templateEntry,
        bool isolatedTriangleTopology,
        int? maxTrianglesPerMesh,
        bool generateMinimalVifContainer,
        bool compactTopologyPacket)
    {
        if (maxVertices < 3)
        {
            throw new InvalidDataException("Custom static split import requires template meshes with at least three vertices.");
        }

        var positions = new List<Vector3>();
        var indices = new List<uint>();
        var texCoords = source.TexCoords is null ? null : new List<Vector2>();
        var joints = source.Joints is null ? null : new List<ushort[]>();
        var weights = source.Weights is null ? null : new List<float[]>();
        var localIndexBySourceIndex = new Dictionary<uint, uint>();
        var triangleOffset = startTriangleOffset;
        var sourceTriangleIndices = source.SourceTriangleIndices is null ? null : new List<int>();
        while (triangleOffset + 2 < source.Indices.Count)
        {
            if (maxTrianglesPerMesh is > 0 && indices.Count / 3 >= maxTrianglesPerMesh.Value)
            {
                break;
            }

            var triangle = new[]
            {
                source.Indices[triangleOffset],
                source.Indices[triangleOffset + 1],
                source.Indices[triangleOffset + 2]
            };
            var newVertexCount = triangle.Count(index => !localIndexBySourceIndex.ContainsKey(index));
            if (positions.Count + newVertexCount > maxVertices)
            {
                if (indices.Count == 0)
                {
                    throw new InvalidDataException(
                        $"A custom static triangle in source mesh {source.MeshIndex}:{source.PrimitiveIndex} cannot fit template vertex capacity {maxVertices}.");
                }

                break;
            }

            var candidateIndices = new List<uint>(indices.Count + 3);
            candidateIndices.AddRange(indices);
            var candidatePositions = new List<Vector3>(positions);
            var candidateTexCoords = texCoords is null ? null : new List<Vector2>(texCoords);
            var candidateJoints = joints is null ? null : joints.Select(row => row.ToArray()).ToList();
            var candidateWeights = weights is null ? null : weights.Select(row => row.ToArray()).ToList();
            var candidateLocalIndexBySourceIndex = new Dictionary<uint, uint>(localIndexBySourceIndex);
            foreach (var sourceIndex in triangle)
            {
                if (!candidateLocalIndexBySourceIndex.TryGetValue(sourceIndex, out var localIndex))
                {
                    if (sourceIndex >= source.Positions.Count)
                    {
                        throw new InvalidDataException($"Custom static glTF index {sourceIndex} is outside source mesh {source.MeshIndex}:{source.PrimitiveIndex}.");
                    }

                    localIndex = checked((uint)candidatePositions.Count);
                    candidateLocalIndexBySourceIndex.Add(sourceIndex, localIndex);
                    candidatePositions.Add(source.Positions[(int)sourceIndex]);
                    candidateTexCoords?.Add(source.TexCoords![(int)sourceIndex]);
                    candidateJoints?.Add(source.Joints![(int)sourceIndex].ToArray());
                    candidateWeights?.Add(source.Weights![(int)sourceIndex].ToArray());
                }

                candidateIndices.Add(localIndex);
            }

            var topologyFitsBudget = generateMinimalVifContainer
                ? GeneratedTopologyFitsGeneratedMinimalPacketBudget(
                    candidateIndices,
                    templateEntry,
                    candidatePositions.Count,
                    isolatedTriangleTopology,
                    compactTopologyPacket)
                : GeneratedTopologyFitsCompactPacketBudget(
                    candidateIndices,
                    templateEntry,
                    candidatePositions.Count,
                    isolatedTriangleTopology);
            if (!topologyFitsBudget)
            {
                if (indices.Count == 0)
                {
                    throw new InvalidDataException(
                        $"A custom static triangle in source mesh {source.MeshIndex}:{source.PrimitiveIndex} cannot fit template topology payload budget.");
                }

                break;
            }

            positions = candidatePositions;
            texCoords = candidateTexCoords;
            joints = candidateJoints;
            weights = candidateWeights;
            indices = candidateIndices;
            localIndexBySourceIndex = candidateLocalIndexBySourceIndex;
            if (sourceTriangleIndices is not null)
            {
                var sourceTriangleIndex = triangleOffset / 3;
                sourceTriangleIndices.Add(sourceTriangleIndex < source.SourceTriangleIndices!.Count
                    ? source.SourceTriangleIndices[sourceTriangleIndex]
                    : sourceTriangleIndex);
            }
            triangleOffset += 3;
        }

        if (HasUsableSkinRows(source))
        {
            PreserveSourceVertexOrder(source, localIndexBySourceIndex, ref positions, ref indices, ref texCoords, ref joints, ref weights);
        }

        return new CustomStaticChunk(positions, indices, texCoords, joints, weights, startTriangleOffset, triangleOffset, indices.Count / 3, sourceTriangleIndices);
    }

    private static void PreserveSourceVertexOrder(
        CustomStaticSourceMesh source,
        IReadOnlyDictionary<uint, uint> localIndexBySourceIndex,
        ref List<Vector3> positions,
        ref List<uint> indices,
        ref List<Vector2>? texCoords,
        ref List<ushort[]>? joints,
        ref List<float[]>? weights)
    {
        if (indices.Count == 0)
        {
            return;
        }

        var sourceIndexByOldLocalIndex = localIndexBySourceIndex
            .ToDictionary(pair => pair.Value, pair => pair.Key);
        if (sourceIndexByOldLocalIndex.Count != positions.Count)
        {
            return;
        }

        var remapByOldLocalIndex = new Dictionary<uint, uint>();
        var newPositions = new List<Vector3>(sourceIndexByOldLocalIndex.Count);
        var newTexCoords = texCoords is null ? null : new List<Vector2>(sourceIndexByOldLocalIndex.Count);
        var newJoints = joints is null ? null : new List<ushort[]>(sourceIndexByOldLocalIndex.Count);
        var newWeights = weights is null ? null : new List<float[]>(sourceIndexByOldLocalIndex.Count);

        var newIndex = 0u;
        foreach (var (oldLocalIndex, sourceIndex) in sourceIndexByOldLocalIndex.OrderBy(pair => pair.Value))
        {
            remapByOldLocalIndex[oldLocalIndex] = newIndex++;
            newPositions.Add(source.Positions[(int)sourceIndex]);
            newTexCoords?.Add(source.TexCoords![(int)sourceIndex]);
            newJoints?.Add(source.Joints![(int)sourceIndex].ToArray());
            newWeights?.Add(source.Weights![(int)sourceIndex].ToArray());
        }

        positions = newPositions;
        texCoords = newTexCoords;
        joints = newJoints;
        weights = newWeights;
        indices = indices.Select(index => remapByOldLocalIndex[index]).ToList();
    }

    [GeneratedRegex("^node_(?<index>\\d{4})_(?<type>[A-Za-z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedNodeNamePattern();
}
