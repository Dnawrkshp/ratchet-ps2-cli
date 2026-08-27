using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfExporter
{
    private static GltfSkinContext? TryBuildDzoSkinContext(
        MobyModel model,
        float scale,
        List<object> nodes,
        GltfNodeHierarchy hierarchy,
        List<object> skins,
        MobyDzoGltfExportOptions options)
    {
        var bones = model.Skeleton?.Bones;
        var jointCount = Math.Min(model.JointCount, bones?.Count ?? 0);
        if (bones is null || jointCount <= 0)
        {
            return null;
        }

        var parentMode = ResolveSkeletonParentMode(model.CommonTransforms, jointCount, options.SkeletonParentMode);
        var parentByJoint = ReadCommonTransformParents(model.CommonTransforms, jointCount, parentMode);
        var ignoresParentRotation = options.HonorSkeletonParentRotationFlags
            ? ReadCommonTransformParentRotationFlags(model.CommonTransforms, jointCount, parentMode)
            : new bool[jointCount];
        var commonLocalPositions = ReadCommonTransformLocalPositions(model.CommonTransforms, jointCount, scale);
        var worldPositions = new Vector3[jointCount];
        var worldRotations = new Quaternion[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            (worldPositions[i], worldRotations[i]) = DecodeBoneWorldTransform(bones[i], scale);
        }

        var jointNodeIndices = new int[jointCount];
        var exportedLocalPositions = new Vector3[jointCount];
        var exportedLocalRotations = new Quaternion[jointCount];
        var exportedWorldPositions = new Vector3[jointCount];
        var exportedWorldRotations = new Quaternion[jointCount];
        var jointNodes = new Dictionary<string, object>[jointCount];
        var childrenByJoint = new List<int>[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            childrenByJoint[i] = [];
        }

        for (var i = 0; i < jointCount; i++)
        {
            var localPosition = worldPositions[i];
            var localRotation = worldRotations[i];
            var parent = parentByJoint[i];
            if (parent >= 0)
            {
                var inverseParentRotation = Quaternion.Inverse(worldRotations[parent]);
                localPosition = Vector3.Transform(worldPositions[i] - worldPositions[parent], inverseParentRotation);
                localRotation = Quaternion.Normalize(inverseParentRotation * worldRotations[i]);
                childrenByJoint[parent].Add(i);
            }

            if (commonLocalPositions[i].HasValue)
            {
                localPosition = commonLocalPositions[i]!.Value;
            }

            if (parent >= 0)
            {
                if (ignoresParentRotation[i])
                {
                    var inverseParentRotation = Quaternion.Inverse(exportedWorldRotations[parent]);
                    exportedWorldRotations[i] = localRotation;
                    exportedWorldPositions[i] = exportedWorldPositions[parent] + localPosition;
                    localPosition = Vector3.Transform(localPosition, inverseParentRotation);
                    localRotation = Quaternion.Normalize(inverseParentRotation * localRotation);
                }
                else
                {
                    exportedWorldRotations[i] = Quaternion.Normalize(exportedWorldRotations[parent] * localRotation);
                    exportedWorldPositions[i] = exportedWorldPositions[parent] + Vector3.Transform(localPosition, exportedWorldRotations[parent]);
                }
            }
            else
            {
                exportedWorldRotations[i] = localRotation;
                exportedWorldPositions[i] = localPosition;
            }

            exportedLocalPositions[i] = localPosition;
            exportedLocalRotations[i] = localRotation;

            var nodeIndex = nodes.Count;
            jointNodeIndices[i] = nodeIndex;
            var nodePosition = options.FlattenJointHierarchy ? exportedWorldPositions[i] : localPosition;
            var nodeRotation = options.FlattenJointHierarchy ? exportedWorldRotations[i] : localRotation;
            var node = new Dictionary<string, object>
            {
                ["name"] = $"joint_{i}",
                ["translation"] = new[] { nodePosition.X, nodePosition.Y, nodePosition.Z },
                ["rotation"] = new[] { nodeRotation.X, nodeRotation.Y, nodeRotation.Z, nodeRotation.W }
            };
            jointNodes[i] = node;
            nodes.Add(node);
        }

        if (!options.FlattenJointHierarchy)
        {
            for (var i = 0; i < jointCount; i++)
            {
                if (childrenByJoint[i].Count > 0)
                {
                    jointNodes[i]["children"] = childrenByJoint[i].Select(child => jointNodeIndices[child]).ToArray();
                }
            }
        }

        var skeletonRootNodeIndex = hierarchy.EnsureGroup(["Armature"]);
        for (var i = 0; i < jointCount; i++)
        {
            if (options.FlattenJointHierarchy || parentByJoint[i] < 0)
            {
                hierarchy.AddNodeToGroup(["Armature"], jointNodeIndices[i]);
            }
        }

        var skinIndex = skins.Count;
        var skin = new Dictionary<string, object>
        {
            ["name"] = "moby_skin",
            ["skeleton"] = skeletonRootNodeIndex,
            ["joints"] = jointNodeIndices
        };

        skins.Add(skin);

        var jointPaletteIndexByJoint = Enumerable.Range(0, jointCount).ToArray();
        return new GltfSkinContext
        {
            SkinIndex = skinIndex,
            JointPaletteIndexByJoint = jointPaletteIndexByJoint,
            JointNodeIndices = jointNodeIndices,
            ParentByJoint = parentByJoint,
            ChildrenByJoint = childrenByJoint,
            LocalPositions = exportedLocalPositions,
            LocalRotations = exportedLocalRotations,
            WorldPositions = exportedWorldPositions,
            WorldRotations = exportedWorldRotations,
            InverseBindMatrices = ResolveInverseBindMatrices(options.InverseBindMatrices, jointCount),
            JointNodes = jointNodes,
            Skin = skin
        };
    }

}
