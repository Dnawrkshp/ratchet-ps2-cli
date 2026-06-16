using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Shrubs;

public static partial class ShrubGltfExporter
{
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
            WindingCorrectedTriangleCount = mesh.Groups.Sum(group => group.WindingCorrectedTriangleCount),
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
}
