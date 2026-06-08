using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Shrubs;

public sealed record ShrubGltfExport(
    byte[] GltfBytes,
    byte[] BinBytes,
    byte[] DiagnosticsBytes);

public sealed class ShrubGltfExportOptions
{
    public string? BufferFileName { get; init; }

    public string GameLabel { get; init; } = "Shrub";

    public float PositionScale { get; init; } = 1f / 1024f;

    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }

    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }

    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }

    public string? ExternalBillboardTextureUri { get; init; }

    public TextureSize? ExternalBillboardTextureSize { get; init; }

    public TextureAlphaInfo? ExternalBillboardTextureAlpha { get; init; }
}

public static class ShrubGltfExporter
{
    private const string UnlitExtensionName = "KHR_materials_unlit";
    private const int GltfLinearFilter = 9729;
    private const int GltfWrapRepeat = 10497;

    public static ShrubGltfExport Export(
        Stream input,
        string gltfFileName = "shrub.gltf",
        ShrubGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Export(ShrubClassReader.Read(input), gltfFileName, options);
    }

    public static ShrubGltfExport Export(
        ShrubClass shrub,
        string gltfFileName = "shrub.gltf",
        ShrubGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(shrub);
        options ??= new ShrubGltfExportOptions();
        if (options.PositionScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.PositionScale));
        }

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        var mesh = BuildMesh(shrub, options);
        if (mesh.Groups.Count == 0)
        {
            throw new InvalidDataException("Shrub has no decoded triangles to export.");
        }

        var materialBuild = BuildMaterials(mesh.TextureIds, options);
        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var gltfBufferWriter = new GltfBufferWriter(writer);
        var primitives = new List<Dictionary<string, object>>();

        foreach (var group in mesh.Groups)
        {
            var positionAccessor = gltfBufferWriter.WriteVector3Accessor(
                group.Positions,
                target: GltfBufferWriter.ArrayBufferTarget,
                includeMinMax: true);
            var normalAccessor = gltfBufferWriter.WriteVector3Accessor(
                group.Normals,
                target: GltfBufferWriter.ArrayBufferTarget);
            var texCoordAccessor = gltfBufferWriter.WriteVector2Accessor(
                group.TexCoords,
                target: GltfBufferWriter.ArrayBufferTarget);
            var indexAccessor = gltfBufferWriter.WriteUInt32IndexAccessor(group.Indices);

            primitives.Add(new Dictionary<string, object>
            {
                ["attributes"] = new Dictionary<string, int>
                {
                    ["POSITION"] = positionAccessor,
                    ["NORMAL"] = normalAccessor,
                    ["TEXCOORD_0"] = texCoordAccessor
                },
                ["indices"] = indexAccessor,
                ["mode"] = 4,
                ["material"] = materialBuild.MaterialIndexByTextureId[group.TextureId],
                ["extras"] = new
                {
                    ShrubTextureId = group.TextureId,
                    group.PacketIndex,
                    group.FirstSourcePrimitiveIndex,
                    group.LastSourcePrimitiveIndex,
                    group.SourceVertexCount,
                    group.TriangleCount
                }
            });
        }

        var gameLabel = NormalizeLabel(options.GameLabel);
        var meshes = new List<object>
        {
            new
            {
                name = "shrub",
                primitives,
                extras = BuildMeshExtras(shrub, mesh)
            }
        };
        var nodes = new List<object>
        {
            new
            {
                name = "shrub",
                mesh = 0,
                extras = BuildNodeExtras(shrub, gameLabel, options.PositionScale)
            }
        };
        var gltfTextureCount = materialBuild.TextureIds.Count;

        if (BuildBillboardMesh(shrub, mesh, options, gltfBufferWriter, meshes.Count, materialBuild.Materials.Count, gltfTextureCount)
            is { } billboardMesh)
        {
            materialBuild.Materials.Add(BuildBillboardMaterial(options, gltfTextureCount));
            meshes.Add(billboardMesh.Mesh);
            nodes.Add(billboardMesh.Node);
        }

        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = $"RatchetPs2 {gameLabel} shrub glTF exporter" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = Enumerable.Range(0, nodes.Count).ToArray() } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materialBuild.Materials,
            ["buffers"] = new[] { new { uri = binFileName, byteLength = binBytes.Length } },
            ["bufferViews"] = gltfBufferWriter.BufferViews,
            ["accessors"] = gltfBufferWriter.Accessors,
            ["extensionsUsed"] = new[] { UnlitExtensionName },
            ["extras"] = BuildRootExtras(shrub, mesh, gameLabel)
        };

        var images = materialBuild.TextureIds.Select(textureId => new
        {
            name = $"tex_{textureId:0000}",
            uri = options.ExternalTextureUris![textureId]
        }).ToList();
        if (!string.IsNullOrWhiteSpace(options.ExternalBillboardTextureUri))
        {
            images.Add(new
            {
                name = "shrub_billboard",
                uri = options.ExternalBillboardTextureUri!
            });
        }

        if (images.Count > 0)
        {
            gltf["samplers"] = new[]
            {
                new
                {
                    magFilter = GltfLinearFilter,
                    minFilter = GltfLinearFilter,
                    wrapS = GltfWrapRepeat,
                    wrapT = GltfWrapRepeat
                }
            };
            gltf["images"] = images;
            gltf["textures"] = Enumerable.Range(0, images.Count).Select(sourceIndex => new
            {
                sampler = 0,
                source = sourceIndex
            }).ToArray();
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        var diagnosticsBytes = BuildDiagnostics(shrub, mesh, gameLabel, options, jsonOptions);

        return new ShrubGltfExport(gltfBytes, binBytes, diagnosticsBytes);
    }

    private static ShrubMesh BuildMesh(ShrubClass shrub, ShrubGltfExportOptions options)
    {
        var groups = new List<ShrubPrimitiveGroup>();
        var currentTextureId = -1;
        var sourcePrimitiveIndex = 0;

        foreach (var packet in shrub.Packets)
        {
            foreach (var primitive in packet.Primitives)
            {
                if (primitive is ShrubTexturePrimitive texturePrimitive)
                {
                    currentTextureId = texturePrimitive.TextureId;
                    sourcePrimitiveIndex++;
                    continue;
                }

                if (primitive is not ShrubVertexPrimitive vertexPrimitive)
                {
                    sourcePrimitiveIndex++;
                    continue;
                }

                var group = groups.LastOrDefault();
                if (group is null || group.TextureId != currentTextureId || group.PacketIndex != packet.PacketIndex)
                {
                    group = new ShrubPrimitiveGroup(
                        currentTextureId,
                        packet.PacketIndex,
                        sourcePrimitiveIndex);
                    groups.Add(group);
                }

                group.LastSourcePrimitiveIndex = sourcePrimitiveIndex;
                AppendVertexPrimitive(shrub, vertexPrimitive, group, options.PositionScale);
                sourcePrimitiveIndex++;
            }
        }

        groups.RemoveAll(group => group.Indices.Count == 0);
        return new ShrubMesh(groups);
    }

    private static void AppendVertexPrimitive(
        ShrubClass shrub,
        ShrubVertexPrimitive primitive,
        ShrubPrimitiveGroup group,
        float positionScale)
    {
        var baseIndex = group.Positions.Count;
        foreach (var vertex in primitive.Vertices)
        {
            group.Positions.Add(GltfCoordinateBasis.FromPs2Position(
                vertex.X * shrub.Header.Scale * positionScale,
                vertex.Y * shrub.Header.Scale * positionScale,
                vertex.Z * shrub.Header.Scale * positionScale));
            group.Normals.Add(ReadNormal(shrub, vertex.NormalIndex));
            group.TexCoords.Add(new Vector2(vertex.S / 4096f, vertex.T / 4096f));
            group.SourceVertexCount++;
        }

        if (primitive.GeometryType == ShrubGeometryType.TriangleList)
        {
            for (var i = 0; i + 2 < primitive.Vertices.Count; i += 3)
            {
                AddTriangle(group, baseIndex + i, baseIndex + i + 1, baseIndex + i + 2);
            }
        }
        else
        {
            for (var i = 0; i + 2 < primitive.Vertices.Count; i++)
            {
                AddTriangle(group, baseIndex + i, baseIndex + i + 1, baseIndex + i + 2);
            }
        }
    }

    private static Vector3 ReadNormal(ShrubClass shrub, int normalIndex)
    {
        if ((uint)normalIndex >= (uint)shrub.Normals.Count)
        {
            return Vector3.UnitY;
        }

        var normal = shrub.Normals[normalIndex];
        var vector = GltfCoordinateBasis.FromPs2Position(
            normal.X / (float)short.MaxValue,
            normal.Y / (float)short.MaxValue,
            normal.Z / (float)short.MaxValue);
        return vector.LengthSquared() <= 0.00000001f ? Vector3.UnitY : Vector3.Normalize(vector);
    }

    private static void AddTriangle(ShrubPrimitiveGroup group, int a, int b, int c)
    {
        var pa = group.Positions[a];
        var pb = group.Positions[b];
        var pc = group.Positions[c];
        var faceNormal = Vector3.Cross(pb - pa, pc - pa);
        if (faceNormal.LengthSquared() <= 0.00000001f)
        {
            return;
        }

        var normal = group.Normals[a] + group.Normals[b] + group.Normals[c];
        if (normal.LengthSquared() > 0.00000001f && Vector3.Dot(faceNormal, normal) < 0)
        {
            (b, c) = (c, b);
        }

        group.Indices.Add(checked((uint)a));
        group.Indices.Add(checked((uint)b));
        group.Indices.Add(checked((uint)c));
        group.TriangleCount++;
    }

    private static ShrubMaterialBuildResult BuildMaterials(
        IReadOnlyList<int> textureIds,
        ShrubGltfExportOptions options)
    {
        var materials = new List<Dictionary<string, object>>();
        var materialIndexByTextureId = new Dictionary<int, int>();
        var gltfTextureSourceIndexByTextureId = new Dictionary<int, int>();
        var exportedTextureIds = textureIds
            .Where(textureId => textureId >= 0 && options.ExternalTextureUris?.ContainsKey(textureId) == true)
            .Distinct()
            .Order()
            .ToArray();

        for (var i = 0; i < exportedTextureIds.Length; i++)
        {
            gltfTextureSourceIndexByTextureId[exportedTextureIds[i]] = i;
        }

        foreach (var textureId in textureIds.Distinct())
        {
            materialIndexByTextureId[textureId] = materials.Count;
            var material = new Dictionary<string, object>
            {
                ["name"] = textureId < 0 ? "shrub_untextured_preview" : $"shrub_tex_{textureId:0000}",
                ["doubleSided"] = true,
                ["extensions"] = new Dictionary<string, object>
                {
                    [UnlitExtensionName] = new Dictionary<string, object>()
                }
            };
            var pbr = new Dictionary<string, object>
            {
                ["metallicFactor"] = 0f,
                ["roughnessFactor"] = 1f
            };

            if (gltfTextureSourceIndexByTextureId.TryGetValue(textureId, out var gltfTextureIndex))
            {
                pbr["baseColorTexture"] = new { index = gltfTextureIndex };
                var alpha = options.ExternalTextureAlpha is not null
                    && options.ExternalTextureAlpha.TryGetValue(textureId, out var alphaInfo)
                        ? alphaInfo
                        : TextureAlphaInfo.Opaque;
                var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
                if (shrubAlpha.GltfAlphaMode is { } alphaMode)
                {
                    material["alphaMode"] = alphaMode;
                    if (shrubAlpha.AlphaMode == TextureAlphaMode.Mask)
                    {
                        material["alphaCutoff"] = 0.5f;
                    }
                }
            }
            else
            {
                pbr["baseColorFactor"] = textureId < 0
                    ? new[] { 0.72f, 0.82f, 0.58f, 1f }
                    : new[] { 1f, 1f, 1f, 1f };
            }

            material["pbrMetallicRoughness"] = pbr;
            material["extras"] = BuildMaterialExtras(textureId, options);
            materials.Add(material);
        }

        return new ShrubMaterialBuildResult(
            materials,
            materialIndexByTextureId,
            exportedTextureIds);
    }

    private static ShrubBillboardMeshBuild? BuildBillboardMesh(
        ShrubClass shrub,
        ShrubMesh mesh,
        ShrubGltfExportOptions options,
        GltfBufferWriter gltfBufferWriter,
        int meshIndex,
        int materialIndex,
        int textureIndex)
    {
        if (shrub.Billboard is not { } billboard)
        {
            return null;
        }

        var meshBounds = ComputeMeshBounds(mesh);
        var preview = ResolveBillboardPreview(billboard, meshBounds, options.PositionScale);
        var previewWidth = preview.Width;
        var previewHeight = preview.Height;
        if (previewWidth <= 0 || previewHeight <= 0)
        {
            return null;
        }

        var halfWidth = previewWidth * 0.5f;
        var halfHeight = previewHeight * 0.5f;
        var positions = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, halfHeight, 0),
            new Vector3(-halfWidth, halfHeight, 0)
        };
        var normals = Enumerable.Repeat(Vector3.UnitZ, 4).ToArray();
        var texCoords = new[]
        {
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(0, 0)
        };
        uint[] indices = [0, 1, 2, 0, 2, 3];

        var primitive = new Dictionary<string, object>
        {
            ["attributes"] = new Dictionary<string, int>
            {
                ["POSITION"] = gltfBufferWriter.WriteVector3Accessor(positions, includeMinMax: true),
                ["NORMAL"] = gltfBufferWriter.WriteVector3Accessor(normals),
                ["TEXCOORD_0"] = gltfBufferWriter.WriteVector2Accessor(texCoords)
            },
            ["indices"] = gltfBufferWriter.WriteUInt32IndexAccessor(indices),
            ["mode"] = 4,
            ["material"] = materialIndex,
            ["extras"] = new
            {
                ShrubBillboard = true,
                Texture = options.ExternalBillboardTextureUri,
                TextureIndex = string.IsNullOrWhiteSpace(options.ExternalBillboardTextureUri) ? null : (int?)textureIndex,
                billboard.FadeDistance,
                billboard.Width,
                billboard.Height,
                billboard.ZOffset,
                PreviewWidth = previewWidth,
                PreviewHeight = previewHeight,
                PreviewCenterY = preview.CenterY,
                preview.SizingMode
            }
        };

        var extras = BuildBillboardExtras(billboard, options, preview);
        return new ShrubBillboardMeshBuild(
            new
            {
                name = "shrub_billboard",
                primitives = new[] { primitive },
                extras
            },
            new
            {
                name = "shrub_billboard",
                mesh = meshIndex,
                translation = new[] { 0f, preview.CenterY, 0f },
                extras
            });
    }

    private static Dictionary<string, object> BuildBillboardMaterial(ShrubGltfExportOptions options, int textureIndex)
    {
        var material = new Dictionary<string, object>
        {
            ["name"] = "shrub_billboard",
            ["doubleSided"] = true,
            ["extensions"] = new Dictionary<string, object>
            {
                [UnlitExtensionName] = new Dictionary<string, object>()
            }
        };
        var pbr = new Dictionary<string, object>
        {
            ["metallicFactor"] = 0f,
            ["roughnessFactor"] = 1f
        };

        if (!string.IsNullOrWhiteSpace(options.ExternalBillboardTextureUri))
        {
            pbr["baseColorTexture"] = new { index = textureIndex };
        }
        else
        {
            pbr["baseColorFactor"] = new[] { 0.85f, 0.95f, 0.72f, 0.65f };
        }

        var alpha = options.ExternalBillboardTextureAlpha ?? TextureAlphaInfo.Opaque;
        var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
        if (shrubAlpha.GltfAlphaMode is { } alphaMode)
        {
            material["alphaMode"] = alphaMode;
            if (shrubAlpha.AlphaMode == TextureAlphaMode.Mask)
            {
                material["alphaCutoff"] = 0.5f;
            }
        }

        material["pbrMetallicRoughness"] = pbr;
        material["extras"] = BuildBillboardMaterialExtras(options);
        return material;
    }

    private static object BuildBillboardMaterialExtras(ShrubGltfExportOptions options)
    {
        var alpha = options.ExternalBillboardTextureAlpha ?? TextureAlphaInfo.Opaque;
        var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
        var size = options.ExternalBillboardTextureSize ?? new TextureSize(0, 0);

        return new
        {
            ShrubBillboardMaterial = true,
            ShrubTextureId = (int?)null,
            ShrubTextureUri = options.ExternalBillboardTextureUri,
            TextureWidth = size.Width,
            TextureHeight = size.Height,
            shrubAlpha.HasAlpha,
            AlphaMode = shrubAlpha.AlphaMode.ToString(),
            shrubAlpha.GltfAlphaMode,
            alpha.MinAlpha,
            alpha.MaxAlpha,
            ShrubTextureAlpha.FullOpacityAlpha,
            shrubAlpha.UsesBinaryAlpha
        };
    }

    private static object BuildBillboardExtras(
        ShrubBillboard billboard,
        ShrubGltfExportOptions options,
        ShrubBillboardPreview preview)
    {
        return new
        {
            ShrubBillboard = true,
            Texture = options.ExternalBillboardTextureUri,
            billboard.FadeDistance,
            billboard.Width,
            billboard.Height,
            billboard.ZOffset,
            PreviewWidth = preview.Width,
            PreviewHeight = preview.Height,
            PreviewCenterY = preview.CenterY,
            preview.SizingMode
        };
    }

    private static ShrubBillboardPreview ResolveBillboardPreview(
        ShrubBillboard billboard,
        ShrubMeshBounds meshBounds,
        float positionScale)
    {
        var sourceHasUsableDimensions = billboard.Width > 2f && billboard.Height > 2f;
        var width = sourceHasUsableDimensions
            ? billboard.Width * positionScale
            : FallbackBillboardWidth(meshBounds);
        var height = sourceHasUsableDimensions
            ? billboard.Height * positionScale
            : FallbackBillboardHeight(meshBounds, width);

        return new ShrubBillboardPreview(
            width,
            height,
            meshBounds.Center.Y,
            sourceHasUsableDimensions ? "SourceBillboard" : "MeshBoundsFallback");
    }

    private static float FallbackBillboardWidth(ShrubMeshBounds meshBounds)
    {
        var size = meshBounds.Size;
        var horizontalWidth = MathF.Max(size.X, size.Z);
        return horizontalWidth > 0.0001f ? horizontalWidth : MathF.Max(size.Y, 1f);
    }

    private static float FallbackBillboardHeight(ShrubMeshBounds meshBounds, float fallbackWidth)
    {
        var height = meshBounds.Size.Y;
        return MathF.Max(MathF.Max(height, fallbackWidth), 1f);
    }

    private static ShrubMeshBounds ComputeMeshBounds(ShrubMesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in mesh.Groups.SelectMany(group => group.Positions))
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        if (min.X == float.MaxValue)
        {
            min = Vector3.Zero;
            max = Vector3.One;
        }

        return new ShrubMeshBounds(min, max);
    }

    private static object BuildMaterialExtras(int textureId, ShrubGltfExportOptions options)
    {
        var alpha = options.ExternalTextureAlpha is not null && options.ExternalTextureAlpha.TryGetValue(textureId, out var alphaInfo)
            ? alphaInfo
            : TextureAlphaInfo.Opaque;
        var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
        var size = options.ExternalTextureSizes is not null && options.ExternalTextureSizes.TryGetValue(textureId, out var resolvedSize)
            ? resolvedSize
            : new TextureSize(0, 0);

        return new
        {
            ShrubTextureId = textureId,
            ShrubTextureUri = options.ExternalTextureUris is not null && options.ExternalTextureUris.TryGetValue(textureId, out var uri)
                ? uri
                : null,
            TextureWidth = size.Width,
            TextureHeight = size.Height,
            shrubAlpha.HasAlpha,
            AlphaMode = shrubAlpha.AlphaMode.ToString(),
            shrubAlpha.GltfAlphaMode,
            alpha.MinAlpha,
            alpha.MaxAlpha,
            ShrubTextureAlpha.FullOpacityAlpha,
            shrubAlpha.UsesBinaryAlpha
        };
    }

    private static object BuildRootExtras(ShrubClass shrub, ShrubMesh mesh, string gameLabel)
    {
        return new
        {
            ExportType = $"{gameLabel} shrub geometry",
            Note = "Preview geometry reconstructed from packed shrub VIF packets using Wrench shrub packet semantics.",
            CoordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription,
            Header = BuildHeaderExtras(shrub),
            Geometry = BuildGeometryExtras(mesh)
        };
    }

    private static object BuildNodeExtras(ShrubClass shrub, string gameLabel, float positionScale)
    {
        return new
        {
            Game = gameLabel,
            OClass = $"0x{(ushort)shrub.Header.OClass:X4}",
            shrub.Header.Scale,
            PositionScale = positionScale,
            CoordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription
        };
    }

    private static object BuildMeshExtras(ShrubClass shrub, ShrubMesh mesh)
    {
        return new
        {
            Header = BuildHeaderExtras(shrub),
            Geometry = BuildGeometryExtras(mesh),
            Packets = shrub.Packets.Select(packet => new
            {
                packet.PacketIndex,
                Offset = $"0x{packet.Entry.Offset:X}",
                Size = $"0x{packet.Entry.Size:X}",
                packet.Header.TextureCount,
                packet.Header.GifTagCount,
                packet.Header.VertexCount,
                packet.Header.VertexOffset,
                PrimitiveCount = packet.Primitives.Count
            }).ToArray()
        };
    }

    private static object BuildHeaderExtras(ShrubClass shrub)
    {
        var header = shrub.Header;
        return new
        {
            OClass = $"0x{(ushort)header.OClass:X4}",
            SClass = $"0x{(ushort)header.SClass:X4}",
            ModeBits = $"0x{header.ModeBits:X4}",
            header.MipDistance,
            header.Scale,
            header.InstanceCount,
            header.PacketCount,
            NormalsOffset = $"0x{header.NormalsOffset:X}",
            BillboardOffset = header.BillboardOffset == 0 ? "none" : $"0x{header.BillboardOffset:X}",
            header.DrawnCount,
            header.ScisCount,
            header.BillboardCount,
            BoundingSphere = new
            {
                header.BoundingSphere.X,
                header.BoundingSphere.Y,
                header.BoundingSphere.Z,
                Radius = header.BoundingSphere.W
            },
            Billboard = shrub.Billboard is null ? null : new
            {
                shrub.Billboard.FadeDistance,
                shrub.Billboard.Width,
                shrub.Billboard.Height,
                shrub.Billboard.ZOffset
            }
        };
    }

    private static object BuildGeometryExtras(ShrubMesh mesh)
    {
        return new
        {
            PrimitiveCount = mesh.Groups.Count,
            mesh.VertexCount,
            mesh.TriangleCount,
            TextureIds = mesh.TextureIds,
            TexturedTriangleCount = mesh.Groups.Where(group => group.TextureId >= 0).Sum(group => group.TriangleCount),
            UntexturedTriangleCount = mesh.Groups.Where(group => group.TextureId < 0).Sum(group => group.TriangleCount),
            TextureTriangleCounts = mesh.Groups
                .GroupBy(group => group.TextureId)
                .ToDictionary(group => group.Key.ToString(), group => group.Sum(item => item.TriangleCount))
        };
    }

    private static byte[] BuildDiagnostics(
        ShrubClass shrub,
        ShrubMesh mesh,
        string gameLabel,
        ShrubGltfExportOptions options,
        JsonSerializerOptions jsonOptions)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            ExportType = $"{gameLabel} shrub geometry",
            shrub.ByteLength,
            Header = BuildHeaderExtras(shrub),
            Geometry = BuildGeometryExtras(mesh),
            Packets = shrub.Packets.Select(packet => new
            {
                packet.PacketIndex,
                Offset = $"0x{packet.Entry.Offset:X}",
                Size = $"0x{packet.Entry.Size:X}",
                packet.Header.TextureCount,
                packet.Header.GifTagCount,
                packet.Header.VertexCount,
                packet.Header.VertexOffset,
                TexturePrimitiveCount = packet.Primitives.OfType<ShrubTexturePrimitive>().Count(),
                VertexPrimitiveCount = packet.Primitives.OfType<ShrubVertexPrimitive>().Count()
            }).ToArray(),
            Textures = mesh.TextureIds.Select(textureId => BuildMaterialExtras(textureId, options)).ToArray()
        }, jsonOptions);
    }

    private static string NormalizeLabel(string? label)
    {
        return string.IsNullOrWhiteSpace(label)
            ? "Shrub"
            : label.Trim().ToUpperInvariant();
    }

    private sealed class ShrubPrimitiveGroup
    {
        public ShrubPrimitiveGroup(int textureId, int packetIndex, int firstSourcePrimitiveIndex)
        {
            TextureId = textureId;
            PacketIndex = packetIndex;
            FirstSourcePrimitiveIndex = firstSourcePrimitiveIndex;
            LastSourcePrimitiveIndex = firstSourcePrimitiveIndex;
        }

        public int TextureId { get; }

        public int PacketIndex { get; }

        public int FirstSourcePrimitiveIndex { get; }

        public int LastSourcePrimitiveIndex { get; set; }

        public int SourceVertexCount { get; set; }

        public int TriangleCount { get; set; }

        public List<Vector3> Positions { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector2> TexCoords { get; } = [];

        public List<uint> Indices { get; } = [];
    }

    private sealed record ShrubMesh(IReadOnlyList<ShrubPrimitiveGroup> Groups)
    {
        public int VertexCount => Groups.Sum(group => group.Positions.Count);

        public int TriangleCount => Groups.Sum(group => group.TriangleCount);

        public IReadOnlyList<int> TextureIds => Groups
            .Select(group => group.TextureId)
            .Distinct()
            .Order()
            .ToArray();
    }

    private sealed record ShrubMaterialBuildResult(
        List<Dictionary<string, object>> Materials,
        Dictionary<int, int> MaterialIndexByTextureId,
        IReadOnlyList<int> TextureIds);

    private sealed record ShrubMeshBounds(Vector3 Min, Vector3 Max)
    {
        public Vector3 Size => Max - Min;

        public Vector3 Center => (Min + Max) * 0.5f;
    }

    private sealed record ShrubBillboardPreview(
        float Width,
        float Height,
        float CenterY,
        string SizingMode);

    private sealed record ShrubBillboardMeshBuild(object Mesh, object Node);
}
