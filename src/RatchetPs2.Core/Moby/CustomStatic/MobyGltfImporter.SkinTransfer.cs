using System.Numerics;
using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static ushort[] ResolveHeadSkinJoints(MobyAnimationFormat animationFormat)
    {
        return animationFormat == MobyAnimationFormat.Compact
            ? [10, 12]
            : [8];
    }

    private static ushort[] ResolveUpperCenterSkinJoints(MobyAnimationFormat animationFormat)
    {
        return animationFormat == MobyAnimationFormat.Compact
            ? [4, 10, 12]
            : [1, 4, 6, 8];
    }

    private static ushort[] ResolveCompactLeftShoulderRootJoints()
    {
        return [15, 16, 57];
    }

    private static ushort[] ResolveCompactRightShoulderRootJoints()
    {
        return [37, 38, 58];
    }

    private static void AssignReferenceBindWorldPositions(
        IReadOnlyList<ImportedMesh> meshes,
        MobyModel skinReference,
        float yawDegrees)
    {
        var jointPositions = ReadRigCommonTransformWorldPositions(skinReference);
        if (jointPositions.Count == 0)
        {
            return;
        }

        var fitted = FitReferenceJointPositionsToImportedMeshes(jointPositions, meshes, yawDegrees);
        var bindPositions = fitted.ToDictionary(joint => joint.Joint, joint => joint.Position);
        var bindWorldToLocalTransforms = BuildRigBindWorldToLocalTransforms(skinReference, bindPositions);
        foreach (var mesh in meshes)
        {
            if (!mesh.CustomStaticHideMesh)
            {
                mesh.RigBindWorldPositions = bindPositions;
                mesh.RigBindWorldToLocalTransforms = bindWorldToLocalTransforms;
            }
        }
    }

    private static List<(int Joint, Vector3 Position)> FitReferenceJointPositionsToImportedMeshes(
        IReadOnlyList<(int Joint, Vector3 Position)> joints,
        IReadOnlyList<ImportedMesh> meshes,
        float yawDegrees)
    {
        var meshPositions = meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions)
            .ToList();
        if (meshPositions.Count == 0 || joints.Count == 0)
        {
            return joints.ToList();
        }

        var referenceBounds = Bounds3.From(joints.Select(joint => joint.Position));
        var meshBounds = Bounds3.From(meshPositions);
        var referenceSize = referenceBounds.Size;
        var meshSize = meshBounds.Size;
        var scale = Math.Abs(referenceSize.Y) > 0.0001f
            ? meshSize.Y / referenceSize.Y
            : Math.Max(meshSize.X, meshSize.Z) / Math.Max(0.0001f, Math.Max(referenceSize.X, referenceSize.Z));
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            scale = 1f;
        }

        var referenceAnchor = new Vector3(
            referenceBounds.Center.X,
            referenceBounds.Min.Y,
            referenceBounds.Center.Z);
        var yaw = yawDegrees * MathF.PI / 180f;
        var yawRotation = Math.Abs(yaw) > 0.000001f
            ? Matrix4x4.CreateRotationY(yaw)
            : Matrix4x4.Identity;
        var meshAnchor = new Vector3(
            meshBounds.Center.X,
            meshBounds.Min.Y,
            meshBounds.Center.Z);
        return joints
            .Select(joint => (joint.Joint, Position: Vector3.Transform(joint.Position - referenceAnchor, yawRotation) * scale + meshAnchor))
            .ToList();
    }

    private static Dictionary<int, Matrix4x4> BuildRigBindWorldToLocalTransforms(
        MobyModel rigSource,
        IReadOnlyDictionary<int, Vector3> fittedPositionsByJoint)
    {
        var rotationsByJoint = ReadRigBindWorldRotations(rigSource);
        var result = new Dictionary<int, Matrix4x4>();
        foreach (var (joint, position) in fittedPositionsByJoint)
        {
            rotationsByJoint.TryGetValue(joint, out var rotation);
            if (rotation == default)
            {
                rotation = Quaternion.Identity;
            }

            var world = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
            if (Matrix4x4.Invert(world, out var inverse))
            {
                result[joint] = inverse;
            }
        }

        return result;
    }

    private static Dictionary<int, Quaternion> ReadRigBindWorldRotations(MobyModel rigSource)
    {
        var bones = rigSource.Skeleton?.Bones;
        var jointCount = Math.Min(rigSource.JointCount, bones?.Count ?? 0);
        if (bones is null || jointCount <= 0)
        {
            return [];
        }

        var parentByJoint = ReadCommonTransformParents(rigSource.CommonTransforms, jointCount);
        var skeletonWorldRotations = new Quaternion[jointCount];
        var worldRotations = new Quaternion[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            skeletonWorldRotations[i] = DecodeBoneWorldRotation(bones[i]);
        }

        for (var i = 0; i < jointCount; i++)
        {
            var localRotation = skeletonWorldRotations[i];
            var parent = parentByJoint[i];
            if (parent >= 0)
            {
                localRotation = Quaternion.Normalize(Quaternion.Inverse(skeletonWorldRotations[parent]) * skeletonWorldRotations[i]);
                worldRotations[i] = Quaternion.Normalize(worldRotations[parent] * localRotation);
            }
            else
            {
                worldRotations[i] = localRotation;
            }
        }

        return Enumerable.Range(0, jointCount).ToDictionary(index => index, index => worldRotations[index]);
    }

    private static int[] ReadCommonTransformParents(byte[]? commonTransforms, int jointCount)
    {
        var parents = Enumerable.Repeat(-1, jointCount).ToArray();
        if (commonTransforms is null || commonTransforms.Length < jointCount * 0x10)
        {
            return parents;
        }

        for (var i = 0; i < jointCount; i++)
        {
            var rawParent = BitConverter.ToUInt16(commonTransforms, i * 0x10 + 0x0C) >> 6;
            parents[i] = rawParent >= i ? -1 : rawParent;
        }

        return parents;
    }

    private static Quaternion DecodeBoneWorldRotation(MobyMatrix4 bone)
    {
        var basis = new Matrix4x4(
            1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, -1f, 0f, 0f,
            0f, 0f, 0f, 1f);
        var basisInverse = Matrix4x4.Transpose(basis);
        var sourceRotation = new Matrix4x4(
            bone.Row1.X, bone.Row1.Y, bone.Row1.Z, 0f,
            bone.Row2.X, bone.Row2.Y, bone.Row2.Z, 0f,
            bone.Row3.X, bone.Row3.Y, bone.Row3.Z, 0f,
            0f, 0f, 0f, 1f);
        var mappedRotation = basis * sourceRotation * basisInverse;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(mappedRotation));
    }

    private static void RotateImportedMeshesYaw(IEnumerable<ImportedMesh> meshes, float yawDegrees)
    {
        var yaw = yawDegrees * MathF.PI / 180f;
        var rotation = Matrix4x4.CreateRotationY(yaw);
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < mesh.Positions.Count; i++)
            {
                mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], rotation);
            }
        }
    }

    private static void RotateImportedMeshesPitch(IEnumerable<ImportedMesh> meshes, float pitchDegrees)
    {
        var pitch = pitchDegrees * MathF.PI / 180f;
        var rotation = Matrix4x4.CreateRotationX(pitch);
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < mesh.Positions.Count; i++)
            {
                mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], rotation);
            }
        }
    }

    private static void RotateImportedMeshesRoll(IEnumerable<ImportedMesh> meshes, float rollDegrees)
    {
        var roll = rollDegrees * MathF.PI / 180f;
        // Custom Blender-authored glTFs are exported Y-up, so Blender's visual Z-up
        // roll corresponds to the imported vertical Y axis here.
        var rotation = Matrix4x4.CreateRotationY(roll);
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < mesh.Positions.Count; i++)
            {
                mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], rotation);
            }
        }
    }

    private static void CollapseImportedMeshesOutsideProbe(
        IEnumerable<ImportedMesh> meshes,
        IReadOnlySet<int> visibleMeshIndices)
    {
        foreach (var mesh in meshes)
        {
            if (visibleMeshIndices.Contains(mesh.TemplateMeshIndex) || mesh.Positions.Count == 0)
            {
                continue;
            }

            var center = mesh.Positions.Aggregate(Vector3.Zero, (sum, value) => sum + value) / mesh.Positions.Count;
            for (var i = 0; i < mesh.Positions.Count; i++)
            {
                mesh.Positions[i] = center;
            }

            mesh.CustomStaticHideMesh = true;
        }
    }

    private static void RemoveImportedMeshesOutsideProbe(
        List<ImportedMesh> meshes,
        IReadOnlySet<int> visibleMeshIndices)
    {
        meshes.RemoveAll(mesh => !visibleMeshIndices.Contains(mesh.TemplateMeshIndex));
    }

    private static void CompactCustomStaticGeneratedProbeMeshTable(
        MobyModel model,
        List<ImportedMesh> meshes,
        IReadOnlySet<int> visibleMeshIndices)
    {
        if (model.MeshTable is null)
        {
            return;
        }

        var selectedMeshes = meshes
            .Where(mesh => visibleMeshIndices.Contains(mesh.TemplateMeshIndex))
            .OrderBy(mesh => mesh.TemplateMeshIndex)
            .ToList();
        if (selectedMeshes.Count == 0)
        {
            throw new InvalidDataException("--custom-static-probe-meshes did not match any generated mesh chunks.");
        }

        var compactEntries = new List<MobyMeshTableEntry>(selectedMeshes.Count);
        var compactMeshes = new List<ImportedMesh>(selectedMeshes.Count);
        for (var i = 0; i < selectedMeshes.Count; i++)
        {
            var sourceMesh = selectedMeshes[i];
            if (sourceMesh.TemplateMeshIndex < 0 || sourceMesh.TemplateMeshIndex >= model.MeshTable.Entries.Count)
            {
                throw new InvalidDataException(
                    $"Generated probe mesh index {sourceMesh.TemplateMeshIndex} is outside the mesh table.");
            }

            compactEntries.Add(CloneMeshEntry(model.MeshTable.Entries[sourceMesh.TemplateMeshIndex]));
            compactMeshes.Add(CloneImportedMeshForTemplateIndex(sourceMesh, i));
        }

        model.MeshTable.Entries.Clear();
        model.MeshTable.Entries.AddRange(compactEntries);
        meshes.Clear();
        meshes.AddRange(compactMeshes);
        model.BangleTable = null;
        model.CornCob = null;
        UpdateMeshCounts(model);
    }

    private static ImportedMesh CloneImportedMeshForTemplateIndex(ImportedMesh source, int templateMeshIndex)
    {
        var clone = new ImportedMesh(
            templateMeshIndex,
            source.MeshType,
            source.Positions.ToList(),
            source.Indices.ToList(),
            source.TexCoords?.ToList(),
            source.Joints?.Select(row => row.ToArray()).ToList(),
            source.Weights?.Select(row => row.ToArray()).ToList(),
            source.Metadata)
        {
            CustomStaticHideMesh = source.CustomStaticHideMesh,
            CustomStaticSourceMeshIndex = source.CustomStaticSourceMeshIndex,
            CustomStaticSourcePrimitiveIndex = source.CustomStaticSourcePrimitiveIndex,
            CustomStaticSourceMaterialIndex = source.CustomStaticSourceMaterialIndex,
            CustomStaticSourceMaterialName = source.CustomStaticSourceMaterialName,
            CustomStaticAppliedUvScale = source.CustomStaticAppliedUvScale,
            RigBindWorldPositions = source.RigBindWorldPositions,
            RigBindWorldToLocalTransforms = source.RigBindWorldToLocalTransforms,
            CustomStaticSourceStartTriangle = source.CustomStaticSourceStartTriangle,
            CustomStaticSourceTriangleCount = source.CustomStaticSourceTriangleCount,
            CustomStaticSourceTriangleIndices = source.CustomStaticSourceTriangleIndices?.ToList(),
            CustomStaticForcedSkinJoint = source.CustomStaticForcedSkinJoint
        };
        clone.SkinTransferDiagnostics.AddRange(source.SkinTransferDiagnostics);

        return clone;
    }

    private static MobyBoundingSphere? RecalculateCustomStaticBoundingSphere(
        IEnumerable<ImportedMesh> meshes,
        float outputQuantizationScale,
        float padding)
    {
        var positions = meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions)
            .ToList();
        if (positions.Count == 0)
        {
            return null;
        }

        var bounds = Bounds3.From(positions);
        var center = bounds.Center;
        var radius = 0f;
        foreach (var position in positions)
        {
            radius = Math.Max(radius, Vector3.Distance(center, position));
        }

        var safePadding = Math.Max(1f, padding);
        var safeScale = Math.Abs(outputQuantizationScale) > 1e-8f
            ? outputQuantizationScale
            : 1f / 1024f;
        return new MobyBoundingSphere
        {
            X = center.X / safeScale,
            Y = -center.Z / safeScale,
            Z = center.Y / safeScale,
            Radius = radius * safePadding / safeScale
        };
    }

    private static void ApplyApproximateRigSkinning(
        IReadOnlyList<ImportedMesh> meshes,
        MobyModel rigSource,
        bool useSourcePose)
    {
        var jointPositions = useSourcePose
            ? ReadRigCommonTransformWorldPositions(rigSource)
            : BuildFittedRigJointPositions(meshes, rigSource);
        if (jointPositions.Count == 0)
        {
            throw new InvalidDataException("Rig source has no usable common transform joint positions.");
        }

        foreach (var mesh in meshes)
        {
            if (mesh.CustomStaticHideMesh || mesh.Positions.Count == 0)
            {
                continue;
            }

            mesh.RigBindWorldPositions = jointPositions.ToDictionary(joint => joint.Joint, joint => joint.Position);
            mesh.Joints = new List<ushort[]>(mesh.Positions.Count);
            mesh.Weights = new List<float[]>(mesh.Positions.Count);
            foreach (var position in mesh.Positions)
            {
                var influences = jointPositions
                    .Select(joint => new
                    {
                        joint.Joint,
                        DistanceSquared = Vector3.DistanceSquared(position, joint.Position)
                    })
                    .OrderBy(joint => joint.DistanceSquared)
                    .Take(3)
                    .Select(joint =>
                    {
                        var weight = 1f / MathF.Max(0.0001f, MathF.Sqrt(joint.DistanceSquared));
                        return new MobySkinInfluence(checked((ushort)joint.Joint), weight);
                    })
                    .ToList();
                NormalizeInfluences(influences);

                var joints = new ushort[4];
                var weights = new float[4];
                for (var i = 0; i < influences.Count; i++)
                {
                    joints[i] = influences[i].Joint;
                    weights[i] = influences[i].Weight;
                }

                mesh.Joints.Add(joints);
                mesh.Weights.Add(weights);
            }
        }
    }

    private static void TransferReferenceSkinning(
        IReadOnlyList<ImportedMesh> meshes,
        MobyModel skinReference,
        float scale,
        int sampleCount,
        float? verticalWindow,
        bool sameSide,
        string sideAxis,
        float sideDeadzoneRatio,
        bool materialRegions,
        bool disableAnatomicalFilters,
        bool preserveLowerBodyFilters,
        bool preserveShoulderFilters,
        float shoulderInwardBias,
        bool triangleCoherent,
        bool splitPrimarySeams,
        bool rigidMeshCentroid,
        bool rigidTriangleCentroid,
        int smoothPrimaryIterations,
        float distancePower,
        float yawDegrees,
        IReadOnlyDictionary<int, ushort>? forcedSkinJointsByMeshIndex,
        IReadOnlyList<MobyGltfSourceTriangleSkinJoint>? forcedSourceTriangleSkinJoints,
        MobyAnimationFormat animationFormat)
    {
        var samples = BuildReferenceSkinSamples(skinReference, scale);
        if (samples.Count == 0)
        {
            throw new InvalidDataException("Skin reference moby has no decodable skinned vertices.");
        }

        var fittedSamples = FitReferenceSkinSamplesToImportedMeshes(samples, meshes, yawDegrees);
        fittedSamples = BiasReferenceShoulderSamplesInward(fittedSamples, shoulderInwardBias, animationFormat);
        var nearestSampleCount = Math.Clamp(sampleCount, 1, Math.Min(16, fittedSamples.Count));
        var visiblePositions = meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions)
            .ToList();
        var sideAxisIsZ = string.Equals(sideAxis, "z", StringComparison.OrdinalIgnoreCase);
        var visibleBounds = Bounds3.From(visiblePositions);
        var visibleMin = visibleBounds.Min;
        var visibleMax = visibleBounds.Max;
        var sideCenter = visiblePositions.Count == 0 ? 0f : (GetSideCoordinate(visibleMin, sideAxisIsZ) + GetSideCoordinate(visibleMax, sideAxisIsZ)) * 0.5f;
        var sideSpan = visiblePositions.Count == 0 ? 0f : GetSideCoordinate(visibleMax - visibleMin, sideAxisIsZ);
        var resolvedSideDeadzoneRatio = float.IsFinite(sideDeadzoneRatio) && sideDeadzoneRatio >= 0f
            ? sideDeadzoneRatio
            : 0.03f;
        var sideDeadzone = visiblePositions.Count == 0 ? 0f : MathF.Max(0.001f, MathF.Abs(sideSpan) * resolvedSideDeadzoneRatio);
        var resolvedDistancePower = float.IsFinite(distancePower) && distancePower > 0f ? distancePower : 1f;
        var visibleMinY = visiblePositions.Count == 0 ? 0f : visibleBounds.Min.Y;
        var visibleMaxY = visiblePositions.Count == 0 ? 1f : visibleBounds.Max.Y;
        foreach (var mesh in meshes)
        {
            if (mesh.CustomStaticHideMesh || mesh.Positions.Count == 0)
            {
                continue;
            }

            var meshSamples = materialRegions
                ? FilterReferenceSamplesForMaterialRegion(
                    fittedSamples,
                    mesh,
                    mesh.CustomStaticSourceMaterialName,
                    visibleMinY,
                    visibleMaxY,
                    nearestSampleCount,
                    animationFormat)
                : fittedSamples;
            if (splitPrimarySeams)
            {
                TransferReferenceSkinningBySplitPrimarySeams(
                    mesh,
                    meshSamples,
                    nearestSampleCount,
                    verticalWindow,
                    sameSide,
                    sideCenter,
                    sideDeadzone,
                    sideAxisIsZ,
                    resolvedDistancePower,
                    animationFormat,
                    mesh.CustomStaticSourceMaterialName,
                    visibleMinY,
                    visibleMaxY,
                    disableAnatomicalFilters: disableAnatomicalFilters,
                    preserveLowerBodyFilters: preserveLowerBodyFilters,
                    preserveShoulderFilters: preserveShoulderFilters);
            }
            else if (rigidTriangleCentroid)
            {
                TransferReferenceSkinningByRigidTriangleCentroids(
                    mesh,
                    meshSamples,
                    nearestSampleCount,
                    verticalWindow,
                    sameSide,
                    sideCenter,
                    sideDeadzone,
                    sideAxisIsZ,
                    resolvedDistancePower,
                    animationFormat,
                    mesh.CustomStaticSourceMaterialName,
                    visibleMinY,
                    visibleMaxY,
                    disableAnatomicalFilters: disableAnatomicalFilters,
                    preserveLowerBodyFilters: preserveLowerBodyFilters,
                    preserveShoulderFilters: preserveShoulderFilters);
            }
            else if (rigidMeshCentroid)
            {
                TransferReferenceSkinningByMeshCentroid(
                    mesh,
                    meshSamples,
                    nearestSampleCount,
                    verticalWindow,
                    sameSide,
                    sideCenter,
                    sideDeadzone,
                    sideAxisIsZ,
                    resolvedDistancePower,
                    animationFormat,
                    mesh.CustomStaticSourceMaterialName,
                    visibleMinY,
                    visibleMaxY,
                    disableAnatomicalFilters: disableAnatomicalFilters,
                    preserveLowerBodyFilters: preserveLowerBodyFilters,
                    preserveShoulderFilters: preserveShoulderFilters);
            }
            else if ((triangleCoherent || ShouldUseAutomaticTriangleCoherentSkinning(mesh, materialRegions)) && mesh.Indices.Count >= 3)
            {
                TransferReferenceSkinningByTriangleCentroids(
                    mesh,
                    meshSamples,
                    nearestSampleCount,
                    verticalWindow,
                    sameSide,
                    sideCenter,
                    sideDeadzone,
                    sideAxisIsZ,
                    resolvedDistancePower,
                    animationFormat,
                    mesh.CustomStaticSourceMaterialName,
                    visibleMinY,
                    visibleMaxY,
                    disableAnatomicalFilters: disableAnatomicalFilters,
                    preserveLowerBodyFilters: preserveLowerBodyFilters,
                    preserveShoulderFilters: preserveShoulderFilters);
            }
            else
            {
                mesh.Joints = new List<ushort[]>(mesh.Positions.Count);
                mesh.Weights = new List<float[]>(mesh.Positions.Count);
                mesh.SkinTransferDiagnostics.Clear();
                foreach (var position in mesh.Positions)
                {
                    var transfer = TransferReferenceSkinningForVertex(
                        position,
                        meshSamples,
                        nearestSampleCount,
                        verticalWindow,
                        sameSide,
                        sideCenter,
                        sideDeadzone,
                        sideAxisIsZ,
                        resolvedDistancePower,
                        animationFormat,
                        mesh.CustomStaticSourceMaterialName,
                        visibleMinY,
                        visibleMaxY,
                        disableAnatomicalFilters: disableAnatomicalFilters,
                        preserveLowerBodyFilters: preserveLowerBodyFilters,
                        preserveShoulderFilters: preserveShoulderFilters);
                    mesh.Joints.Add(transfer.Joints);
                    mesh.Weights.Add(transfer.Weights);
                    mesh.SkinTransferDiagnostics.Add(transfer.Diagnostics);
                }
            }

            if (materialRegions
                && !splitPrimarySeams
                && !rigidMeshCentroid
                && !rigidTriangleCentroid
                && mesh.CustomStaticSourceMaterialName is "torso" or "legs")
            {
                ApplyConservativeTriangleOutlierSkinning(mesh);
            }

            if (disableAnatomicalFilters
                && (preserveLowerBodyFilters || preserveShoulderFilters)
                && mesh.Joints is not null
                && mesh.Weights is not null)
            {
                ApplyPerVertexPreservedAnatomicalFilters(
                    mesh,
                    meshSamples,
                    nearestSampleCount,
                    verticalWindow,
                    sameSide,
                    sideCenter,
                    sideDeadzone,
                    sideAxisIsZ,
                    resolvedDistancePower,
                    animationFormat,
                    visibleMinY,
                    visibleMaxY,
                    preserveLowerBodyFilters,
                    preserveShoulderFilters);
            }

            ApplyForcedSkinJoint(mesh, forcedSkinJointsByMeshIndex);
            ApplyForcedSourceTriangleSkinJoints(mesh, forcedSourceTriangleSkinJoints);

            if (smoothPrimaryIterations > 0
                && mesh.Joints is not null
                && mesh.Weights is not null
                && mesh.Joints.Count == mesh.Positions.Count
                && mesh.Weights.Count == mesh.Positions.Count)
            {
                SmoothPrimaryReferenceSkinning(mesh, smoothPrimaryIterations);
            }
        }
    }

    private static void ApplyForcedSourceTriangleSkinJoints(
        ImportedMesh mesh,
        IReadOnlyList<MobyGltfSourceTriangleSkinJoint>? forcedSourceTriangleSkinJoints)
    {
        if (forcedSourceTriangleSkinJoints is null
            || forcedSourceTriangleSkinJoints.Count == 0
            || mesh.CustomStaticSourceMeshIndex is not { } sourceMeshIndex
            || mesh.CustomStaticSourcePrimitiveIndex is not { } sourcePrimitiveIndex
            || mesh.CustomStaticSourceStartTriangle is not { } sourceStartTriangle
            || mesh.Joints is not { } joints
            || mesh.Weights is not { } weights)
        {
            return;
        }

        var matchingRules = forcedSourceTriangleSkinJoints
            .Where(rule => rule.MeshIndex == sourceMeshIndex && rule.PrimitiveIndex == sourcePrimitiveIndex)
            .ToList();
        if (matchingRules.Count == 0)
        {
            return;
        }

        var vertexCount = Math.Min(mesh.Positions.Count, Math.Min(joints.Count, weights.Count));
        var triangleCount = mesh.Indices.Count / 3;
        for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            var sourceTriangleIndex = mesh.CustomStaticSourceTriangleIndices is not null
                && triangleIndex < mesh.CustomStaticSourceTriangleIndices.Count
                    ? mesh.CustomStaticSourceTriangleIndices[triangleIndex]
                    : sourceStartTriangle + triangleIndex;
            var rule = matchingRules.FirstOrDefault(item => item.TriangleIndices.Contains(sourceTriangleIndex));
            if (rule is null)
            {
                continue;
            }

            var offset = triangleIndex * 3;
            for (var corner = 0; corner < 3; corner++)
            {
                var vertexIndex = mesh.Indices[offset + corner];
                if (vertexIndex >= vertexCount)
                {
                    continue;
                }

                var index = (int)vertexIndex;
                joints[index] = [rule.Joint, 0, 0, 0];
                weights[index] = [1f, 0f, 0f, 0f];
                if (index < mesh.SkinTransferDiagnostics.Count)
                {
                    mesh.SkinTransferDiagnostics[index] = mesh.SkinTransferDiagnostics[index] with
                    {
                        PrimaryJoint = rule.Joint
                    };
                }
            }
        }
    }

    private static void ApplyForcedSkinJoint(
        ImportedMesh mesh,
        IReadOnlyDictionary<int, ushort>? forcedSkinJointsByMeshIndex)
    {
        if (forcedSkinJointsByMeshIndex is null
            || !forcedSkinJointsByMeshIndex.TryGetValue(mesh.TemplateMeshIndex, out var joint))
        {
            if (mesh.CustomStaticForcedSkinJoint is not { } forcedSourceJoint)
            {
                return;
            }

            joint = forcedSourceJoint;
        }

        var joints = mesh.Joints;
        var weights = mesh.Weights;
        if (joints is null || weights is null)
        {
            return;
        }

        var vertexCount = Math.Min(mesh.Positions.Count, Math.Min(joints.Count, weights.Count));
        for (var i = 0; i < vertexCount; i++)
        {
            joints[i] = [joint, 0, 0, 0];
            weights[i] = [1f, 0f, 0f, 0f];
            if (i < mesh.SkinTransferDiagnostics.Count)
            {
                mesh.SkinTransferDiagnostics[i] = mesh.SkinTransferDiagnostics[i] with
                {
                    PrimaryJoint = joint
                };
            }
        }
    }

    private static void ApplyCompactUpperCenterHeadGuard(
        ImportedMesh mesh,
        float minY,
        float maxY)
    {
        var joints = mesh.Joints;
        var weights = mesh.Weights;
        if (joints is null || weights is null)
        {
            return;
        }

        var height = MathF.Max(0.0001f, maxY - minY);
        var vertexCount = Math.Min(mesh.Positions.Count, Math.Min(joints.Count, weights.Count));
        for (var i = 0; i < vertexCount; i++)
        {
            var position = mesh.Positions[i];
            var normalizedY = (position.Y - minY) / height;
            if (normalizedY < 0.16f || MathF.Abs(position.Z) > 0.24f)
            {
                continue;
            }

            var primaryJoint = GetPrimaryJoint(joints[i], weights[i]);
            if (primaryJoint is not (1 or 4 or 6 or 59 or 60 or 61 or 63 or 64 or 65 or 66 or 68))
            {
                continue;
            }

            joints[i] = [12, 0, 0, 0];
            weights[i] = [1f, 0f, 0f, 0f];
            if (i < mesh.SkinTransferDiagnostics.Count)
            {
                mesh.SkinTransferDiagnostics[i] = mesh.SkinTransferDiagnostics[i] with
                {
                    PrimaryJoint = 12
                };
            }
        }
    }

    private static void ApplyPerVertexPreservedAnatomicalFilters(
        ImportedMesh mesh,
        IReadOnlyList<ReferenceSkinSample> meshSamples,
        int nearestSampleCount,
        float? verticalWindow,
        bool sameSide,
        float sideCenter,
        float sideDeadzone,
        bool sideAxisIsZ,
        float distancePower,
        MobyAnimationFormat animationFormat,
        float minY,
        float maxY,
        bool preserveLowerBodyFilters,
        bool preserveShoulderFilters)
    {
        var joints = mesh.Joints;
        var weights = mesh.Weights;
        if (joints is null || weights is null)
        {
            return;
        }

        var vertexCount = Math.Min(mesh.Positions.Count, Math.Min(joints.Count, weights.Count));
        for (var i = 0; i < vertexCount; i++)
        {
            var position = mesh.Positions[i];
            if (!ShouldPreserveLowerBodyAnatomicalFilters(
                    preserveLowerBodyFilters,
                    position,
                    minY,
                    maxY,
                    animationFormat)
                && !ShouldPreserveShoulderAnatomicalFilters(
                    preserveShoulderFilters,
                    position,
                    minY,
                    maxY,
                    animationFormat)
                && !ShouldPreserveUpperCenterAnatomicalFilters(
                    preserveShoulderFilters,
                    position,
                    minY,
                    maxY,
                    animationFormat))
            {
                continue;
            }

            var transfer = TransferReferenceSkinningForVertex(
                position,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                mesh.CustomStaticSourceMaterialName,
                minY,
                maxY,
                disableAnatomicalFilters: true,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);
            joints[i] = transfer.Joints;
            weights[i] = transfer.Weights;
            if (i < mesh.SkinTransferDiagnostics.Count)
            {
                mesh.SkinTransferDiagnostics[i] = transfer.Diagnostics;
            }
        }
    }

    private static void ApplyConservativeTriangleOutlierSkinning(ImportedMesh mesh)
    {
        if (mesh.Joints is null || mesh.Weights is null || mesh.Indices.Count < 3)
        {
            return;
        }

        var vertexCount = Math.Min(mesh.Positions.Count, Math.Min(mesh.Joints.Count, mesh.Weights.Count));
        if (vertexCount == 0)
        {
            return;
        }

        var primaryJoints = new ushort[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            primaryJoints[i] = GetPrimaryJoint(mesh.Joints[i], mesh.Weights[i]);
        }

        var votes = Enumerable.Range(0, vertexCount)
            .Select(_ => new Dictionary<ushort, int>())
            .ToArray();
        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= vertexCount || b < 0 || b >= vertexCount || c < 0 || c >= vertexCount)
            {
                continue;
            }

            VoteOutlier(a, b, c);
            VoteOutlier(b, a, c);
            VoteOutlier(c, a, b);
        }

        var changed = false;
        for (var i = 0; i < vertexCount; i++)
        {
            if (votes[i].Count == 0)
            {
                continue;
            }

            var winner = votes[i]
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .First();
            if (winner.Value < 2 || winner.Key == primaryJoints[i])
            {
                continue;
            }

            primaryJoints[i] = winner.Key;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        mesh.Joints = new List<ushort[]>(vertexCount);
        mesh.Weights = new List<float[]>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            mesh.Joints.Add([primaryJoints[i], 0, 0, 0]);
            mesh.Weights.Add([1f, 0f, 0f, 0f]);
        }

        void VoteOutlier(int outlier, int neighborA, int neighborB)
        {
            var joint = primaryJoints[outlier];
            var neighborJoint = primaryJoints[neighborA];
            if (neighborJoint == joint || neighborJoint != primaryJoints[neighborB])
            {
                return;
            }

            votes[outlier][neighborJoint] = votes[outlier].TryGetValue(neighborJoint, out var count)
                ? count + 1
                : 1;
        }
    }

    private static bool ShouldUseAutomaticTriangleCoherentSkinning(ImportedMesh mesh, bool materialRegions)
    {
        if (!materialRegions || string.IsNullOrWhiteSpace(mesh.CustomStaticSourceMaterialName))
        {
            return false;
        }

        return mesh.CustomStaticSourceMaterialName.Trim().ToLowerInvariant() switch
        {
            // Torso material in custom humanoid imports often includes arms. Centroid voting maps
            // long arm-spanning triangles back to core joints, so keep torso per-vertex.
            "torso" => false,
            _ => true
        };
    }

    private static void SmoothPrimaryReferenceSkinning(ImportedMesh mesh, int iterations)
    {
        if (mesh.Joints is null || mesh.Weights is null || mesh.Indices.Count < 3)
        {
            return;
        }

        var neighbors = Enumerable.Range(0, mesh.Positions.Count)
            .Select(_ => new HashSet<int>())
            .ToArray();
        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= neighbors.Length || b < 0 || b >= neighbors.Length || c < 0 || c >= neighbors.Length)
            {
                continue;
            }

            neighbors[a].Add(b);
            neighbors[a].Add(c);
            neighbors[b].Add(a);
            neighbors[b].Add(c);
            neighbors[c].Add(a);
            neighbors[c].Add(b);
        }

        var primaryJoints = new ushort[mesh.Positions.Count];
        for (var i = 0; i < primaryJoints.Length; i++)
        {
            primaryJoints[i] = GetPrimaryJoint(mesh.Joints[i], mesh.Weights[i]);
        }

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var next = (ushort[])primaryJoints.Clone();
            for (var i = 0; i < primaryJoints.Length; i++)
            {
                if (neighbors[i].Count == 0)
                {
                    continue;
                }

                var votes = new Dictionary<ushort, int>
                {
                    [primaryJoints[i]] = 1
                };
                foreach (var neighbor in neighbors[i])
                {
                    var joint = primaryJoints[neighbor];
                    votes[joint] = votes.TryGetValue(joint, out var current) ? current + 1 : 1;
                }

                var winner = votes
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key == primaryJoints[i] ? 0 : 1)
                    .ThenBy(pair => pair.Key)
                    .First();
                if (winner.Value >= Math.Max(2, (neighbors[i].Count + 2) / 2))
                {
                    next[i] = winner.Key;
                }
            }

            primaryJoints = next;
        }

        mesh.Joints = new List<ushort[]>(primaryJoints.Length);
        mesh.Weights = new List<float[]>(primaryJoints.Length);
        foreach (var joint in primaryJoints)
        {
            mesh.Joints.Add([joint, 0, 0, 0]);
            mesh.Weights.Add([1f, 0f, 0f, 0f]);
        }
    }

    internal static ushort GetPrimaryJoint(IReadOnlyList<ushort> joints, IReadOnlyList<float> weights)
    {
        var bestJoint = joints.Count > 0 ? joints[0] : (ushort)0;
        var bestWeight = weights.Count > 0 ? weights[0] : 0f;
        for (var i = 1; i < Math.Min(joints.Count, weights.Count); i++)
        {
            if (weights[i] > bestWeight)
            {
                bestJoint = joints[i];
                bestWeight = weights[i];
            }
        }

        return bestJoint;
    }

    private static void TransferReferenceSkinningByRigidTriangleCentroids(
        ImportedMesh mesh,
        IReadOnlyList<ReferenceSkinSample> meshSamples,
        int nearestSampleCount,
        float? verticalWindow,
        bool sameSide,
        float sideCenter,
        float sideDeadzone,
        bool sideAxisIsZ,
        float distancePower,
        MobyAnimationFormat animationFormat,
        string? materialName = null,
        float minY = 0f,
        float maxY = 1f,
        bool disableAnatomicalFilters = false,
        bool preserveLowerBodyFilters = false,
        bool preserveShoulderFilters = false)
    {
        if (mesh.Indices.Count < 3)
        {
            TransferReferenceSkinningByMeshCentroid(
                mesh,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);
            return;
        }

        var sourcePositions = mesh.Positions.ToArray();
        var sourceTexCoords = mesh.TexCoords?.ToArray();
        var newPositions = new List<Vector3>(mesh.Indices.Count);
        var newIndices = new List<uint>(mesh.Indices.Count);
        var newTexCoords = sourceTexCoords is null ? null : new List<Vector2>(mesh.Indices.Count);
        var newJoints = new List<ushort[]>(mesh.Indices.Count);
        var newWeights = new List<float[]>(mesh.Indices.Count);

        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= sourcePositions.Length || b < 0 || b >= sourcePositions.Length || c < 0 || c >= sourcePositions.Length)
            {
                continue;
            }

            var centroid = (sourcePositions[a] + sourcePositions[b] + sourcePositions[c]) / 3f;
            var transfer = TransferReferenceSkinningForVertex(
                centroid,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);

            AppendRigidTriangleCentroidVertex(a);
            AppendRigidTriangleCentroidVertex(b);
            AppendRigidTriangleCentroidVertex(c);

            void AppendRigidTriangleCentroidVertex(int sourceIndex)
            {
                var newIndex = checked((uint)newPositions.Count);
                newPositions.Add(sourcePositions[sourceIndex]);
                newIndices.Add(newIndex);
                if (newTexCoords is not null)
                {
                    newTexCoords.Add(sourceTexCoords is not null && sourceIndex < sourceTexCoords.Length
                        ? sourceTexCoords[sourceIndex]
                        : Vector2.Zero);
                }

                newJoints.Add(transfer.Joints.ToArray());
                newWeights.Add(transfer.Weights.ToArray());
            }
        }

        if (newPositions.Count == 0)
        {
            TransferReferenceSkinningByMeshCentroid(
                mesh,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);
            return;
        }

        mesh.Positions.Clear();
        mesh.Positions.AddRange(newPositions);
        mesh.Indices.Clear();
        mesh.Indices.AddRange(newIndices);
        if (mesh.TexCoords is not null)
        {
            mesh.TexCoords.Clear();
            mesh.TexCoords.AddRange(newTexCoords ?? []);
        }

        mesh.Joints = newJoints;
        mesh.Weights = newWeights;
    }

    private static void TransferReferenceSkinningBySplitPrimarySeams(
        ImportedMesh mesh,
        IReadOnlyList<ReferenceSkinSample> meshSamples,
        int nearestSampleCount,
        float? verticalWindow,
        bool sameSide,
        float sideCenter,
        float sideDeadzone,
        bool sideAxisIsZ,
        float distancePower,
        MobyAnimationFormat animationFormat,
        string? materialName = null,
        float minY = 0f,
        float maxY = 1f,
        bool disableAnatomicalFilters = false,
        bool preserveLowerBodyFilters = false,
        bool preserveShoulderFilters = false)
    {
        if (mesh.Indices.Count < 3)
        {
            TransferReferenceSkinningByMeshCentroid(
                mesh,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);
            return;
        }

        var sourcePositions = mesh.Positions.ToArray();
        var sourceTexCoords = mesh.TexCoords?.ToArray();
        var newPositions = new List<Vector3>(mesh.Positions.Count);
        var newIndices = new List<uint>(mesh.Indices.Count);
        var newTexCoords = sourceTexCoords is null ? null : new List<Vector2>(mesh.Positions.Count);
        var newJoints = new List<ushort[]>(mesh.Positions.Count);
        var newWeights = new List<float[]>(mesh.Positions.Count);
        var newDiagnostics = new List<SkinTransferVertexDiagnostics>(mesh.Positions.Count);
        var indexBySourceAndPrimaryJoint = new Dictionary<(int SourceIndex, ushort PrimaryJoint), uint>();

        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= sourcePositions.Length || b < 0 || b >= sourcePositions.Length || c < 0 || c >= sourcePositions.Length)
            {
                continue;
            }

            var centroid = (sourcePositions[a] + sourcePositions[b] + sourcePositions[c]) / 3f;
            var transfer = TransferReferenceSkinningForVertex(
                centroid,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);
            var primaryJoint = GetPrimaryJoint(transfer.Joints, transfer.Weights);

            AppendSeamSplitVertex(a, primaryJoint, transfer);
            AppendSeamSplitVertex(b, primaryJoint, transfer);
            AppendSeamSplitVertex(c, primaryJoint, transfer);

            void AppendSeamSplitVertex(int sourceIndex, ushort trianglePrimaryJoint, SkinTransferResult triangleTransfer)
            {
                var key = (sourceIndex, trianglePrimaryJoint);
                if (!indexBySourceAndPrimaryJoint.TryGetValue(key, out var newIndex))
                {
                    newIndex = checked((uint)newPositions.Count);
                    indexBySourceAndPrimaryJoint.Add(key, newIndex);
                    newPositions.Add(sourcePositions[sourceIndex]);
                    if (newTexCoords is not null)
                    {
                        newTexCoords.Add(sourceTexCoords is not null && sourceIndex < sourceTexCoords.Length
                            ? sourceTexCoords[sourceIndex]
                            : Vector2.Zero);
                    }

                    newJoints.Add(triangleTransfer.Joints.ToArray());
                    newWeights.Add(triangleTransfer.Weights.ToArray());
                    newDiagnostics.Add(triangleTransfer.Diagnostics with
                    {
                        Position = sourcePositions[sourceIndex],
                        PrimaryJoint = trianglePrimaryJoint
                    });
                }

                newIndices.Add(newIndex);
            }
        }

        if (newPositions.Count == 0)
        {
            TransferReferenceSkinningByMeshCentroid(
                mesh,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);
            return;
        }

        mesh.Positions.Clear();
        mesh.Positions.AddRange(newPositions);
        mesh.Indices.Clear();
        mesh.Indices.AddRange(newIndices);
        if (mesh.TexCoords is not null)
        {
            mesh.TexCoords.Clear();
            mesh.TexCoords.AddRange(newTexCoords ?? []);
        }

        mesh.Joints = newJoints;
        mesh.Weights = newWeights;
        mesh.SkinTransferDiagnostics.Clear();
        mesh.SkinTransferDiagnostics.AddRange(newDiagnostics);
    }

    private static void TransferReferenceSkinningByMeshCentroid(
        ImportedMesh mesh,
        IReadOnlyList<ReferenceSkinSample> meshSamples,
        int nearestSampleCount,
        float? verticalWindow,
        bool sameSide,
        float sideCenter,
        float sideDeadzone,
        bool sideAxisIsZ,
        float distancePower,
        MobyAnimationFormat animationFormat,
        string? materialName = null,
        float minY = 0f,
        float maxY = 1f,
        bool disableAnatomicalFilters = false,
        bool preserveLowerBodyFilters = false,
        bool preserveShoulderFilters = false)
    {
        var centroid = mesh.Positions.Aggregate(Vector3.Zero, (sum, value) => sum + value) / mesh.Positions.Count;
        var transfer = TransferReferenceSkinningForVertex(
            centroid,
            meshSamples,
            nearestSampleCount,
            verticalWindow,
            sameSide,
            sideCenter,
            sideDeadzone,
            sideAxisIsZ,
            distancePower,
            animationFormat,
            materialName,
            minY,
            maxY,
            disableAnatomicalFilters: disableAnatomicalFilters,
            preserveLowerBodyFilters: preserveLowerBodyFilters,
            preserveShoulderFilters: preserveShoulderFilters);

        mesh.Joints = new List<ushort[]>(mesh.Positions.Count);
        mesh.Weights = new List<float[]>(mesh.Positions.Count);
        mesh.SkinTransferDiagnostics.Clear();
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            mesh.Joints.Add(transfer.Joints.ToArray());
            mesh.Weights.Add(transfer.Weights.ToArray());
            mesh.SkinTransferDiagnostics.Add(transfer.Diagnostics);
        }
    }

    private static void TransferReferenceSkinningByTriangleCentroids(
        ImportedMesh mesh,
        IReadOnlyList<ReferenceSkinSample> meshSamples,
        int nearestSampleCount,
        float? verticalWindow,
        bool sameSide,
        float sideCenter,
        float sideDeadzone,
        bool sideAxisIsZ,
        float distancePower,
        MobyAnimationFormat animationFormat,
        string? materialName = null,
        float minY = 0f,
        float maxY = 1f,
        bool disableAnatomicalFilters = false,
        bool preserveLowerBodyFilters = false,
        bool preserveShoulderFilters = false)
    {
        var influenceByVertex = Enumerable.Range(0, mesh.Positions.Count)
            .Select(_ => new Dictionary<ushort, float>())
            .ToArray();

        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= mesh.Positions.Count || b < 0 || b >= mesh.Positions.Count || c < 0 || c >= mesh.Positions.Count)
            {
                continue;
            }

            var centroid = (mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c]) / 3f;
            var transfer = TransferReferenceSkinningForVertex(
                centroid,
                meshSamples,
                nearestSampleCount,
                verticalWindow,
                sameSide,
                sideCenter,
                sideDeadzone,
                sideAxisIsZ,
                distancePower,
                animationFormat,
                materialName,
                minY,
                maxY,
                disableAnatomicalFilters: disableAnatomicalFilters,
                preserveLowerBodyFilters: preserveLowerBodyFilters,
                preserveShoulderFilters: preserveShoulderFilters);

            AddTriangleInfluenceVote(influenceByVertex[a], transfer);
            AddTriangleInfluenceVote(influenceByVertex[b], transfer);
            AddTriangleInfluenceVote(influenceByVertex[c], transfer);
        }

        mesh.Joints = new List<ushort[]>(mesh.Positions.Count);
        mesh.Weights = new List<float[]>(mesh.Positions.Count);
        mesh.SkinTransferDiagnostics.Clear();
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            var influences = influenceByVertex[i]
                .Select(pair => new MobySkinInfluence(pair.Key, pair.Value))
                .OrderByDescending(influence => influence.Weight)
                .Take(4)
                .ToList();
            if (influences.Count == 0)
            {
                var fallback = TransferReferenceSkinningForVertex(
                    mesh.Positions[i],
                    meshSamples,
                    nearestSampleCount,
                    verticalWindow,
                    sameSide,
                    sideCenter,
                    sideDeadzone,
                    sideAxisIsZ,
                    distancePower,
                    animationFormat,
                    materialName,
                    minY,
                    maxY);
                mesh.Joints.Add(fallback.Joints);
                mesh.Weights.Add(fallback.Weights);
                mesh.SkinTransferDiagnostics.Add(fallback.Diagnostics);
                continue;
            }

            NormalizeInfluences(influences);
            var joints = new ushort[4];
            var weights = new float[4];
            for (var j = 0; j < influences.Count; j++)
            {
                joints[j] = influences[j].Joint;
                weights[j] = influences[j].Weight;
            }

            mesh.Joints.Add(joints);
            mesh.Weights.Add(weights);
            mesh.SkinTransferDiagnostics.Add(CreateSyntheticTransferDiagnostics(mesh.Positions[i], joints, weights));
        }
    }

    private static void AddTriangleInfluenceVote(
        Dictionary<ushort, float> influenceByJoint,
        SkinTransferResult transferredInfluences)
    {
        for (var i = 0; i < Math.Min(transferredInfluences.Joints.Length, transferredInfluences.Weights.Length); i++)
        {
            var weight = transferredInfluences.Weights[i];
            if (weight <= 0.00001f)
            {
                continue;
            }

            var joint = transferredInfluences.Joints[i];
            influenceByJoint[joint] = influenceByJoint.TryGetValue(joint, out var current)
                ? current + weight
                : weight;
        }
    }

    private static SkinTransferVertexDiagnostics CreateSyntheticTransferDiagnostics(
        Vector3 position,
        ushort[] joints,
        float[] weights)
    {
        return new SkinTransferVertexDiagnostics(
            position,
            GetPrimaryJoint(joints, weights),
            0,
            0f,
            0f,
            0f,
            0,
            -1,
            -1,
            Vector3.Zero);
    }

    private static IReadOnlyList<ReferenceSkinSample> FilterReferenceSamplesForMaterialRegion(
        IReadOnlyList<ReferenceSkinSample> samples,
        ImportedMesh mesh,
        string? materialName,
        float minY,
        float maxY,
        int requiredCount,
        MobyAnimationFormat animationFormat,
        bool strictLowerBodySide = false,
        bool strictShoulderRoot = false)
    {
        if (samples.Count == 0 || string.IsNullOrWhiteSpace(materialName))
        {
            return samples;
        }

        var height = MathF.Max(0.0001f, maxY - minY);
        (float Min, float Max)? range = materialName.Trim().ToLowerInvariant() switch
        {
            "head" => (0.68f, 1.05f),
            "torso" => (0.30f, 1.02f),
            "legs" => (0.08f, 0.54f),
            "shoes" or "shoe" or "sneaker" or "sneakers" => (-0.05f, 0.24f),
            _ => null
        };
        if (range is null)
        {
            return samples;
        }

        var filtered = samples
            .Where(sample =>
            {
                var t = (sample.Position.Y - minY) / height;
                return t >= range.Value.Min && t <= range.Value.Max;
            })
            .ToList();
        var yFiltered = filtered.Count >= requiredCount ? filtered : samples;
        var jointFiltered = FilterReferenceSamplesForAnatomicalRegion(
            yFiltered,
            mesh,
            materialName,
            requiredCount,
            animationFormat);
        return jointFiltered.Count >= requiredCount ? jointFiltered : yFiltered;
    }

    private static IReadOnlyList<ReferenceSkinSample> FilterReferenceSamplesForAnatomicalRegion(
        IReadOnlyList<ReferenceSkinSample> samples,
        ImportedMesh mesh,
        string? materialName,
        int requiredCount,
        MobyAnimationFormat animationFormat)
    {
        if (samples.Count == 0 || mesh.Positions.Count == 0 || string.IsNullOrWhiteSpace(materialName))
        {
            return samples;
        }

        var bounds = Bounds3.From(mesh.Positions);
        var center = bounds.Center;
        var material = materialName.Trim().ToLowerInvariant();
        if (material == "torso")
        {
            return samples;
        }

        ushort[] allowedJoints = material switch
        {
            "head" => ResolveHeadSkinJoints(animationFormat),
            "legs" => ResolveLegRegionJoints(center.Z, animationFormat),
            "shoes" or "shoe" or "sneaker" or "sneakers" => ResolveShoeRegionJoints(center.Z, animationFormat),
            _ => []
        };
        if (allowedJoints.Length == 0)
        {
            return samples;
        }

        var allowed = allowedJoints.ToHashSet();
        var filtered = samples
            .Where(sample => allowed.Contains(sample.PrimaryJoint))
            .ToList();
        return filtered.Count >= requiredCount ? filtered : samples;
        static ushort[] ResolveLegRegionJoints(float centerZ, MobyAnimationFormat animationFormat)
        {
            if (animationFormat == MobyAnimationFormat.Compact)
            {
                if (centerZ < -0.08f)
                {
                    return [1, 4, 6, 59, 60, 61, 63];
                }

                if (centerZ > 0.08f)
                {
                    return [1, 4, 6, 64, 65, 66, 68];
                }

                return [1, 4, 6, 59, 60, 61, 63, 64, 65, 66, 68];
            }

            if (centerZ < -0.08f)
            {
                return [1, 4, 6, 73, 74, 75, 78];
            }

            if (centerZ > 0.08f)
            {
                return [1, 4, 6, 67, 68, 69, 72];
            }

            return [1, 4, 6, 67, 68, 69, 72, 73, 74, 75, 78];
        }

        static ushort[] ResolveShoeRegionJoints(float centerZ, MobyAnimationFormat animationFormat)
        {
            if (animationFormat == MobyAnimationFormat.Compact)
            {
                if (centerZ < -0.03f)
                {
                    return [60, 61, 63];
                }

                if (centerZ > 0.03f)
                {
                    return [65, 66, 68];
                }

                return [60, 61, 63, 65, 66, 68];
            }

            if (centerZ < -0.03f)
            {
                return [74, 75, 78];
            }

            if (centerZ > 0.03f)
            {
                return [68, 69, 72];
            }

            return [68, 69, 72, 74, 75, 78];
        }
    }

    private static IReadOnlyList<ReferenceSkinSample> FilterReferenceSamplesForAnatomicalPosition(
        IReadOnlyList<ReferenceSkinSample> samples,
        Vector3 position,
        string? materialName,
        float minY,
        float maxY,
        int requiredCount,
        MobyAnimationFormat animationFormat,
        bool strictLowerBodySide = false,
        bool strictShoulderRoot = false)
    {
        if (samples.Count == 0)
        {
            return samples;
        }

        var height = MathF.Max(0.0001f, maxY - minY);
        var normalizedY = (position.Y - minY) / height;
        var material = materialName?.Trim().ToLowerInvariant() ?? string.Empty;
        ushort[] allowedJoints = material switch
        {
            "head" => ResolveHeadSkinJoints(animationFormat),
            "torso" => ResolveTorsoPositionJoints(position.Z, normalizedY, animationFormat),
            "legs" => ResolveLegPositionJoints(position.Z, normalizedY, animationFormat, strictLowerBodySide),
            "shoes" or "shoe" or "sneaker" or "sneakers" => ResolveShoePositionJoints(position.Z, animationFormat, strictLowerBodySide),
            _ => ResolveGenericHumanoidPositionJoints(position.Z, normalizedY)
        };
        if (allowedJoints.Length == 0)
        {
            return samples;
        }

        var allowed = allowedJoints.ToHashSet();
        var filtered = samples
            .Where(sample => allowed.Contains(sample.PrimaryJoint))
            .ToList();
        return filtered.Count >= requiredCount ? filtered : samples;

        static ushort[] ResolveTorsoPositionJoints(float z, float normalizedY, MobyAnimationFormat animationFormat)
        {
            if (animationFormat == MobyAnimationFormat.Compact)
            {
                if (normalizedY >= 0.58f && MathF.Abs(z) <= 0.42f)
                {
                    return ResolveUpperCenterSkinJoints(animationFormat);
                }

                if (z < -0.18f)
                {
                    return [10, 12, 15, 16, 37, 57];
                }

                if (z > 0.18f)
                {
                    return [10, 12, 37, 38, 58];
                }

                return ResolveUpperCenterSkinJoints(animationFormat);
            }

            if (z < -0.22f)
            {
                return [48, 49, 51, 52, 53, 54, 58, 59, 60, 64, 65];
            }

            if (z > 0.22f)
            {
                return [28, 29, 31, 32, 33, 34, 38, 40, 44, 45];
            }

            return [1, 4, 6, 8];
        }

        static ushort[] ResolveLegPositionJoints(
            float z,
            float normalizedY,
            MobyAnimationFormat animationFormat,
            bool strictLowerBodySide)
        {
            if (normalizedY > 0.43f)
            {
                return [1, 4, 6];
            }

            if (animationFormat == MobyAnimationFormat.Compact)
            {
                var threshold = strictLowerBodySide ? 0f : 0.012f;
                if (z < -threshold)
                {
                    return [59, 60, 61, 63];
                }

                if (z > threshold)
                {
                    return [64, 65, 66, 68];
                }

                return [59, 60, 61, 63, 64, 65, 66, 68];
            }

            if (z < -0.012f)
            {
                return [73, 74, 75, 78];
            }

            if (z > 0.012f)
            {
                return [67, 68, 69, 72];
            }

            return [67, 68, 69, 72, 73, 74, 75, 78];
        }

        static ushort[] ResolveShoePositionJoints(
            float z,
            MobyAnimationFormat animationFormat,
            bool strictLowerBodySide)
        {
            if (animationFormat == MobyAnimationFormat.Compact)
            {
                var threshold = strictLowerBodySide ? 0f : 0.006f;
                if (z < -threshold)
                {
                    return [60, 61, 63];
                }

                if (z > threshold)
                {
                    return [65, 66, 68];
                }

                return [60, 61, 63, 65, 66, 68];
            }

            if (z < -0.006f)
            {
                return [74, 75, 78];
            }

            if (z > 0.006f)
            {
                return [68, 69, 72];
            }

            return [68, 69, 72, 74, 75, 78];
        }

        ushort[] ResolveGenericHumanoidPositionJoints(float z, float normalizedY)
        {
            if (animationFormat == MobyAnimationFormat.Compact)
            {
                if (normalizedY <= 0.18f)
                {
                    return ResolveShoePositionJoints(z, animationFormat, strictLowerBodySide);
                }

                if (normalizedY <= 0.43f)
                {
                    return ResolveLegPositionJoints(z, normalizedY, animationFormat, strictLowerBodySide);
                }

                if (normalizedY >= 0.60f && MathF.Abs(z) <= 0.35f)
                {
                    return ResolveHeadSkinJoints(animationFormat);
                }

                if (normalizedY >= 0.56f && MathF.Abs(z) <= 0.18f)
                {
                    return [10, 12];
                }

                if (normalizedY >= 0.67f && MathF.Abs(z) <= 0.42f)
                {
                    return [10, 12];
                }

                if (normalizedY >= 0.52f && z < -0.26f)
                {
                    return ResolveCompactArmPositionJoints(z, negativeSide: true, strictShoulderRoot);
                }

                if (normalizedY >= 0.52f && z > 0.26f)
                {
                    return ResolveCompactArmPositionJoints(z, negativeSide: false, strictShoulderRoot);
                }

                return ResolveUpperCenterSkinJoints(animationFormat);
            }

            return normalizedY <= 0.18f
                ? ResolveShoePositionJoints(z, animationFormat, strictLowerBodySide)
                : [];
        }

        static ushort[] ResolveCompactArmPositionJoints(float z, bool negativeSide, bool strictShoulderRoot)
        {
            var distance = MathF.Abs(z);
            if (negativeSide)
            {
                if (strictShoulderRoot && distance < 0.54f)
                {
                    return ResolveCompactLeftShoulderRootJoints();
                }

                if (distance >= 0.64f)
                {
                    return [18, 19, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34];
                }

                if (distance >= 0.50f)
                {
                    return [17, 18, 19, 20];
                }

                if (distance >= 0.38f)
                {
                    return [16, 17, 18];
                }

                return ResolveCompactLeftShoulderRootJoints();
            }

            if (strictShoulderRoot && distance < 0.54f)
            {
                return ResolveCompactRightShoulderRootJoints();
            }

            if (distance >= 0.64f)
            {
                return [40, 41, 42, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56];
            }

            if (distance >= 0.50f)
            {
                return [39, 40, 41, 42];
            }

            if (distance >= 0.38f)
            {
                return [38, 39, 40];
            }

            return ResolveCompactRightShoulderRootJoints();
        }
    }

    private static SkinTransferResult TransferReferenceSkinningForVertex(
        Vector3 position,
        IReadOnlyList<ReferenceSkinSample> fittedSamples,
        int nearestSampleCount,
        float? verticalWindow,
        bool sameSide,
        float sideCenter,
        float sideDeadzone,
        bool sideAxisIsZ,
        float distancePower,
        MobyAnimationFormat animationFormat,
        string? materialName = null,
        float minY = 0f,
        float maxY = 1f,
        bool disableAnatomicalFilters = false,
        bool preserveLowerBodyFilters = false,
        bool preserveShoulderFilters = false)
    {
        var candidates = fittedSamples;
        if (verticalWindow is { } window && window > 0f)
        {
            var constrained = fittedSamples
                .Where(sample => Math.Abs(sample.Position.Y - position.Y) <= window)
                .ToList();
            if (constrained.Count >= nearestSampleCount)
            {
                candidates = constrained;
            }
        }
        var positionSideCoordinate = GetSideCoordinate(position, sideAxisIsZ);
        if (sameSide && MathF.Abs(positionSideCoordinate - sideCenter) > sideDeadzone)
        {
            var side = MathF.Sign(positionSideCoordinate - sideCenter);
            var constrained = candidates
                .Where(sample =>
                {
                    var sampleSideCoordinate = GetSideCoordinate(sample.Position, sideAxisIsZ);
                    return MathF.Sign(sampleSideCoordinate - sideCenter) == side
                        || MathF.Abs(sampleSideCoordinate - sideCenter) <= sideDeadzone;
                })
                .ToList();
            if (constrained.Count >= nearestSampleCount)
            {
                candidates = constrained;
            }
        }
        if (!disableAnatomicalFilters
            || ShouldPreserveLowerBodyAnatomicalFilters(
                preserveLowerBodyFilters,
                position,
                minY,
                maxY,
                animationFormat)
            || ShouldPreserveShoulderAnatomicalFilters(
                preserveShoulderFilters,
                position,
                minY,
                maxY,
                animationFormat)
            || ShouldPreserveUpperCenterAnatomicalFilters(
                preserveShoulderFilters,
                position,
                minY,
                maxY,
                animationFormat))
        {
            candidates = FilterReferenceSamplesForAnatomicalPosition(
                candidates,
                position,
                materialName,
                minY,
                maxY,
                nearestSampleCount,
                animationFormat,
                strictLowerBodySide: preserveLowerBodyFilters && disableAnatomicalFilters,
                strictShoulderRoot: preserveShoulderFilters && disableAnatomicalFilters);
        }

        var orderedSamples = candidates
            .Select(sample => new
            {
                Sample = sample,
                DistanceSquared = Vector3.DistanceSquared(position, sample.Position)
            })
            .OrderBy(sample => sample.DistanceSquared)
            .Take(Math.Max(nearestSampleCount, 2))
            .ToList();
        var nearestSamples = orderedSamples
            .Take(nearestSampleCount)
            .ToList();
        var influenceByJoint = new Dictionary<ushort, float>();
        foreach (var nearestSample in nearestSamples)
        {
            var distance = MathF.Max(0.0001f, MathF.Sqrt(nearestSample.DistanceSquared));
            var sampleWeight = 1f / MathF.Pow(distance, distancePower);
            for (var i = 0; i < Math.Min(nearestSample.Sample.Joints.Length, nearestSample.Sample.Weights.Length); i++)
            {
                var weight = nearestSample.Sample.Weights[i] * sampleWeight;
                if (weight <= 0.00001f)
                {
                    continue;
                }

                var joint = nearestSample.Sample.Joints[i];
                influenceByJoint[joint] = influenceByJoint.TryGetValue(joint, out var current)
                    ? current + weight
                    : weight;
            }
        }

        var influences = influenceByJoint
            .Select(pair => new MobySkinInfluence(pair.Key, pair.Value))
            .OrderByDescending(influence => influence.Weight)
            .Take(4)
            .ToList();
        if (influences.Count == 0)
        {
            influences.Add(new MobySkinInfluence(0, 1f));
        }

        NormalizeInfluences(influences);
        var joints = new ushort[4];
        var weights = new float[4];
        for (var i = 0; i < influences.Count; i++)
        {
            joints[i] = influences[i].Joint;
            weights[i] = influences[i].Weight;
        }

        var nearest = orderedSamples.Count > 0 ? orderedSamples[0] : null;
        var nearestDistance = nearest is null ? 0f : MathF.Sqrt(nearest.DistanceSquared);
        var secondDistance = orderedSamples.Count > 1 ? MathF.Sqrt(orderedSamples[1].DistanceSquared) : nearestDistance;
        var confidence = secondDistance > 0.000001f
            ? Math.Clamp((secondDistance - nearestDistance) / secondDistance, 0f, 1f)
            : 1f;
        return new SkinTransferResult(
            joints,
            weights,
            new SkinTransferVertexDiagnostics(
                position,
                GetPrimaryJoint(joints, weights),
                nearest?.Sample.PrimaryJoint ?? 0,
                nearestDistance,
                secondDistance,
                confidence,
                candidates.Count,
                nearest?.Sample.SourceMeshIndex ?? -1,
                nearest?.Sample.SourceVertexIndex ?? -1,
                nearest?.Sample.Position ?? Vector3.Zero));
    }

    private static bool ShouldPreserveLowerBodyAnatomicalFilters(
        bool preserveLowerBodyFilters,
        Vector3 position,
        float minY,
        float maxY,
        MobyAnimationFormat animationFormat)
    {
        if (!preserveLowerBodyFilters)
        {
            return false;
        }

        var height = MathF.Max(0.0001f, maxY - minY);
        var normalizedY = (position.Y - minY) / height;
        return animationFormat == MobyAnimationFormat.Compact
            ? normalizedY <= 0.43f
            : normalizedY <= 0.18f;
    }

    private static bool ShouldPreserveShoulderAnatomicalFilters(
        bool preserveShoulderFilters,
        Vector3 position,
        float minY,
        float maxY,
        MobyAnimationFormat animationFormat)
    {
        if (!preserveShoulderFilters)
        {
            return false;
        }

        var height = MathF.Max(0.0001f, maxY - minY);
        var normalizedY = (position.Y - minY) / height;
        return animationFormat == MobyAnimationFormat.Compact
            && normalizedY >= 0.52f
            && normalizedY <= 0.86f
            && MathF.Abs(position.Z) >= 0.24f
            && MathF.Abs(position.Z) <= 0.52f;
    }

    private static bool ShouldPreserveUpperCenterAnatomicalFilters(
        bool preserveShoulderFilters,
        Vector3 position,
        float minY,
        float maxY,
        MobyAnimationFormat animationFormat)
    {
        if (!preserveShoulderFilters)
        {
            return false;
        }

        var height = MathF.Max(0.0001f, maxY - minY);
        var normalizedY = (position.Y - minY) / height;
        return animationFormat == MobyAnimationFormat.Compact
            && normalizedY >= 0.58f
            && normalizedY <= 0.92f
            && MathF.Abs(position.Z) <= 0.42f;
    }

    private static List<ReferenceSkinSample> BuildReferenceSkinSamples(MobyModel skinReference, float scale)
    {
        var entries = skinReference.MeshTable?.Entries;
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var decoded = DecodeTemplateMeshes(entries, scale);
        var preferredTypes = decoded
            .Where(pair => entries[pair.Key].MeshType == MobyMeshType.HighLod)
            .ToList();
        var sourceMeshes = preferredTypes.Count > 0
            ? preferredTypes
            : decoded
                .Where(pair => entries[pair.Key].MeshType is not MobyMeshType.Bangle and not MobyMeshType.Metal)
                .ToList();

        var samples = new List<ReferenceSkinSample>();
        foreach (var (meshIndex, mesh) in sourceMeshes)
        {
            var count = Math.Min(mesh.Positions.Count, Math.Min(mesh.Joints.Count, mesh.Weights.Count));
            for (var i = 0; i < count; i++)
            {
                if (mesh.Weights[i].All(weight => weight <= 0.00001f))
                {
                    continue;
                }

                samples.Add(new ReferenceSkinSample(
                    mesh.Positions[i],
                    mesh.Joints[i],
                    mesh.Weights[i],
                    meshIndex,
                    i,
                    GetPrimaryJoint(mesh.Joints[i], mesh.Weights[i])));
            }
        }

        return samples;
    }

    private static List<ReferenceSkinSample> FitReferenceSkinSamplesToImportedMeshes(
        IReadOnlyList<ReferenceSkinSample> samples,
        IReadOnlyList<ImportedMesh> meshes,
        float yawDegrees)
    {
        var meshPositions = meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions)
            .ToList();
        if (meshPositions.Count == 0 || samples.Count == 0)
        {
            return samples.ToList();
        }

        var referenceBounds = Bounds3.From(samples.Select(sample => sample.Position));
        var meshBounds = Bounds3.From(meshPositions);
        var referenceSize = referenceBounds.Size;
        var meshSize = meshBounds.Size;
        var scale = Math.Abs(referenceSize.Y) > 0.0001f
            ? meshSize.Y / referenceSize.Y
            : Math.Max(meshSize.X, meshSize.Z) / Math.Max(0.0001f, Math.Max(referenceSize.X, referenceSize.Z));
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            scale = 1f;
        }

        var referenceAnchor = new Vector3(
            referenceBounds.Center.X,
            referenceBounds.Min.Y,
            referenceBounds.Center.Z);
        var yaw = yawDegrees * MathF.PI / 180f;
        var yawRotation = Math.Abs(yaw) > 0.000001f
            ? Matrix4x4.CreateRotationY(yaw)
            : Matrix4x4.Identity;
        var meshAnchor = new Vector3(
            meshBounds.Center.X,
            meshBounds.Min.Y,
            meshBounds.Center.Z);
        return samples
            .Select(sample => sample with { Position = Vector3.Transform(sample.Position - referenceAnchor, yawRotation) * scale + meshAnchor })
            .ToList();
    }

    private static List<ReferenceSkinSample> BiasReferenceShoulderSamplesInward(
        IReadOnlyList<ReferenceSkinSample> samples,
        float inwardBias,
        MobyAnimationFormat animationFormat)
    {
        if (animationFormat != MobyAnimationFormat.Compact || !float.IsFinite(inwardBias) || inwardBias <= 0f)
        {
            return samples.ToList();
        }

        return samples
            .Select(sample =>
            {
                if (!IsCompactShoulderBiasJoint(sample.PrimaryJoint))
                {
                    return sample;
                }

                var z = sample.Position.Z;
                var biasedAbsZ = MathF.Max(0f, MathF.Abs(z) - inwardBias);
                var biasedZ = z < 0f ? -biasedAbsZ : biasedAbsZ;
                return sample with { Position = new Vector3(sample.Position.X, sample.Position.Y, biasedZ) };
            })
            .ToList();
    }

    private static bool IsCompactShoulderBiasJoint(ushort joint)
    {
        return joint is 15 or 16 or 37 or 38 or 57 or 58;
    }

    private static List<(int Joint, Vector3 Position)> BuildFittedRigJointPositions(
        IReadOnlyList<ImportedMesh> meshes,
        MobyModel rigSource)
    {
        var rigPositions = ReadRigCommonTransformWorldPositions(rigSource);
        if (rigPositions.Count == 0)
        {
            return [];
        }

        var meshPositions = meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions)
            .ToList();
        if (meshPositions.Count == 0)
        {
            return rigPositions;
        }

        var rigBounds = Bounds3.From(rigPositions.Select(position => position.Position));
        var meshBounds = Bounds3.From(meshPositions);
        var rigSize = rigBounds.Size;
        var meshSize = meshBounds.Size;
        var scale = Math.Abs(rigSize.Y) > 0.0001f
            ? meshSize.Y / rigSize.Y
            : Math.Max(meshSize.X, meshSize.Z) / Math.Max(0.0001f, Math.Max(rigSize.X, rigSize.Z));
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            scale = 1f;
        }

        var rigCenter = new Vector3(rigBounds.Center.X, rigBounds.Min.Y, rigBounds.Center.Z);
        var meshCenter = new Vector3(meshBounds.Center.X, meshBounds.Min.Y, meshBounds.Center.Z);
        return rigPositions
            .Select(position => (position.Joint, Position: (position.Position - rigCenter) * scale + meshCenter))
            .ToList();
    }

    private static byte[] BuildFittedRigCommonTransforms(
        IReadOnlyList<ImportedMesh> meshes,
        MobyModel rigSource)
    {
        if (rigSource.CommonTransforms is null)
        {
            return [];
        }

        var jointCount = Math.Min(rigSource.JointCount, rigSource.CommonTransforms.Length / 0x10);
        if (jointCount <= 0)
        {
            return [];
        }

        var fittedWorldByJoint = BuildFittedRigJointPositions(meshes, rigSource)
            .ToDictionary(joint => joint.Joint, joint => joint.Position);
        var output = (byte[])rigSource.CommonTransforms.Clone();
        for (var joint = 0; joint < jointCount; joint++)
        {
            if (!fittedWorldByJoint.TryGetValue(joint, out var worldPosition))
            {
                continue;
            }

            var offset = joint * 0x10;
            var rawParent = BitConverter.ToUInt16(rigSource.CommonTransforms, offset + 0x0C) >> 6;
            var parent = rawParent >= joint ? -1 : rawParent;
            var localPosition = worldPosition;
            if (parent >= 0 && fittedWorldByJoint.TryGetValue(parent, out var parentPosition))
            {
                localPosition -= parentPosition;
            }

            BitConverter.GetBytes(localPosition.X).CopyTo(output, offset);
            BitConverter.GetBytes(-localPosition.Z).CopyTo(output, offset + 0x04);
            BitConverter.GetBytes(localPosition.Y).CopyTo(output, offset + 0x08);
        }

        return output;
    }

    private static List<(int Joint, Vector3 Position)> ReadRigCommonTransformWorldPositions(MobyModel rigSource)
    {
        var jointCount = Math.Min(rigSource.JointCount, rigSource.CommonTransforms?.Length / 0x10 ?? 0);
        if (rigSource.CommonTransforms is null || jointCount <= 0)
        {
            return [];
        }

        var parents = new int[jointCount];
        var localPositions = new Vector3[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            var offset = i * 0x10;
            var x = BitConverter.ToSingle(rigSource.CommonTransforms, offset);
            var sourceY = BitConverter.ToSingle(rigSource.CommonTransforms, offset + 0x04);
            var sourceZ = BitConverter.ToSingle(rigSource.CommonTransforms, offset + 0x08);
            localPositions[i] = new Vector3(x, sourceZ, -sourceY);
            var rawParent = BitConverter.ToUInt16(rigSource.CommonTransforms, offset + 0x0C) >> 6;
            parents[i] = rawParent >= i ? -1 : rawParent;
        }

        var worldPositions = new Vector3[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            worldPositions[i] = parents[i] >= 0
                ? worldPositions[parents[i]] + localPositions[i]
                : localPositions[i];
        }

        return worldPositions
            .Select((position, joint) => (joint, position))
            .ToList();
    }

    private static float GetSideCoordinate(Vector3 position, bool sideAxisIsZ)
        => sideAxisIsZ ? position.Z : position.X;
}
