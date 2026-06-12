using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public enum FirstPersonArmsCoverage
    {
        HandsAndForearms,
        HandsOnly,
        FullArms,
        ArmsWithoutShoulders
    }

    internal static class SyntyFirstPersonArmsMeshBuilder
    {
        private static readonly string[] FullArmBoneNameTokens =
        {
            "Clavicle",
            "Shoulder",
            "Elbow",
            "UpperArm",
            "LowerArm",
            "ForeArm",
            "LeftArm",
            "RightArm",
            "LeftForeArm",
            "RightForeArm",
            "LeftShoulder",
            "RightShoulder",
            "Hand",
            "Finger",
            "Thumb",
            "IndexFinger",
            "MiddleFinger",
            "RingFinger",
            "Pinky",
            "Little"
        };

        private static readonly string[] ForearmAndHandBoneNameTokens =
        {
            "Elbow",
            "LowerArm",
            "ForeArm",
            "Hand",
            "Finger",
            "Thumb",
            "IndexFinger",
            "MiddleFinger",
            "RingFinger",
            "Pinky",
            "Little"
        };

        private static readonly string[] HandOnlyBoneNameTokens =
        {
            "Hand",
            "Finger",
            "Thumb",
            "IndexFinger",
            "MiddleFinger",
            "RingFinger",
            "Pinky",
            "Little"
        };

        public static Mesh ExtractArmsMesh(
            SkinnedMeshRenderer source,
            float minArmBoneWeight,
            FirstPersonArmsCoverage coverage = FirstPersonArmsCoverage.HandsAndForearms)
        {
            if (source == null || source.sharedMesh == null)
            {
                return null;
            }

            var armBoneIndices = CollectArmBoneIndices(source.bones, coverage);
            if (armBoneIndices.Count == 0)
            {
                return null;
            }

            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[SyntyFirstPersonArmsMeshBuilder] Mesh '{mesh.name}' is not readable. " +
                    "Enable Read/Write on the model import settings or bake first-person arms in the prefab.",
                    source);
                return null;
            }

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv = mesh.uv;
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return null;
            }

            var vertexIsArm = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertexIsArm[i] = IsArmWeighted(boneWeights[i], armBoneIndices, minArmBoneWeight);
            }

            var subMeshCount = mesh.subMeshCount;
            var includedTriangles = new List<int>(mesh.triangles.Length);
            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = triangles[i];
                    var b = triangles[i + 1];
                    var c = triangles[i + 2];
                    if (vertexIsArm[a] || vertexIsArm[b] || vertexIsArm[c])
                    {
                        includedTriangles.Add(a);
                        includedTriangles.Add(b);
                        includedTriangles.Add(c);
                    }
                }
            }

            if (includedTriangles.Count == 0)
            {
                return null;
            }

            var remap = new int[vertices.Length];
            for (var i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var newVertices = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newTangents = new List<Vector4>();
            var newUv = new List<Vector2>();
            var newBoneWeights = new List<BoneWeight>();
            var newTriangles = new List<int>(includedTriangles.Count);

            for (var i = 0; i < includedTriangles.Count; i++)
            {
                var sourceIndex = includedTriangles[i];
                if (remap[sourceIndex] < 0)
                {
                    remap[sourceIndex] = newVertices.Count;
                    newVertices.Add(vertices[sourceIndex]);
                    if (normals != null && normals.Length == vertices.Length)
                    {
                        newNormals.Add(normals[sourceIndex]);
                    }

                    if (tangents != null && tangents.Length == vertices.Length)
                    {
                        newTangents.Add(tangents[sourceIndex]);
                    }

                    if (uv != null && uv.Length == vertices.Length)
                    {
                        newUv.Add(uv[sourceIndex]);
                    }

                    newBoneWeights.Add(boneWeights[sourceIndex]);
                }

                newTriangles.Add(remap[sourceIndex]);
            }

            var armsMesh = new Mesh
            {
                name = mesh.name + "_FirstPersonArms"
            };
            armsMesh.SetVertices(newVertices);
            if (newNormals.Count == newVertices.Count)
            {
                armsMesh.SetNormals(newNormals);
            }
            else
            {
                armsMesh.RecalculateNormals();
            }

            if (newTangents.Count == newVertices.Count)
            {
                armsMesh.SetTangents(newTangents);
            }

            if (newUv.Count == newVertices.Count)
            {
                armsMesh.SetUVs(0, newUv);
            }

            armsMesh.boneWeights = newBoneWeights.ToArray();
            armsMesh.bindposes = mesh.bindposes;
            armsMesh.SetTriangles(newTriangles, 0);
            armsMesh.RecalculateBounds();
            return armsMesh;
        }

        private static HashSet<int> CollectArmBoneIndices(IReadOnlyList<Transform> bones, FirstPersonArmsCoverage coverage)
        {
            var indices = new HashSet<int>();
            if (bones == null)
            {
                return indices;
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                if (IsArmBoneName(bone.name, coverage))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static bool IsArmBoneName(string boneName, FirstPersonArmsCoverage coverage)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return false;
            }

            if (coverage == FirstPersonArmsCoverage.ArmsWithoutShoulders)
            {
                if (IsShoulderOnlyBoneName(boneName))
                {
                    return false;
                }

                return IsArmBoneName(boneName, FirstPersonArmsCoverage.FullArms);
            }

            var tokens = ResolveBoneNameTokens(coverage);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (boneName.IndexOf(tokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsShoulderOnlyBoneName(string boneName)
        {
            return boneName.IndexOf("Clavicle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Shoulder", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string[] ResolveBoneNameTokens(FirstPersonArmsCoverage coverage)
        {
            switch (coverage)
            {
                case FirstPersonArmsCoverage.HandsOnly:
                    return HandOnlyBoneNameTokens;
                case FirstPersonArmsCoverage.FullArms:
                    return FullArmBoneNameTokens;
                default:
                    return ForearmAndHandBoneNameTokens;
            }
        }

        private static bool IsArmWeighted(BoneWeight boneWeight, HashSet<int> armBoneIndices, float minArmBoneWeight)
        {
            var armWeight = 0f;
            if (armBoneIndices.Contains(boneWeight.boneIndex0))
            {
                armWeight += boneWeight.weight0;
            }

            if (armBoneIndices.Contains(boneWeight.boneIndex1))
            {
                armWeight += boneWeight.weight1;
            }

            if (armBoneIndices.Contains(boneWeight.boneIndex2))
            {
                armWeight += boneWeight.weight2;
            }

            if (armBoneIndices.Contains(boneWeight.boneIndex3))
            {
                armWeight += boneWeight.weight3;
            }

            return armWeight >= minArmBoneWeight;
        }

        private static readonly string[] TorsoBoneCoreNames =
        {
            "Hips",
            "Pelvis",
            "Spine",
            "Spine1",
            "Spine2",
            "Chest",
            "UpperChest"
        };

        public static Mesh ExtractBodyWithoutTorsoMesh(
            SkinnedMeshRenderer source,
            float minTorsoBoneWeight = 0.35f)
        {
            if (source == null || source.sharedMesh == null)
            {
                return null;
            }

            var torsoBoneIndices = CollectTorsoBoneIndices(source.bones);
            if (torsoBoneIndices.Count == 0)
            {
                return null;
            }

            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[SyntyFirstPersonArmsMeshBuilder] Mesh '{mesh.name}' is not readable. " +
                    "Enable Read/Write on the body model import settings to hide the torso under clothing.",
                    source);
                return null;
            }

            var vertices = mesh.vertices;
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return null;
            }

            var vertexIsTorso = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertexIsTorso[i] = IsTorsoWeighted(boneWeights[i], torsoBoneIndices, minTorsoBoneWeight);
            }

            return BuildFilteredMesh(
                mesh,
                vertices,
                boneWeights,
                vertexIsTorso,
                includeWhenMarked: false,
                mesh.name + "_WithoutTorso");
        }

        private static readonly string[] LegBoneCoreNamesToHide =
        {
            "LeftUpLeg",
            "RightUpLeg",
            "LeftLeg",
            "RightLeg",
            "Thigh_L",
            "Thigh_R",
            "UpperLeg_L",
            "UpperLeg_R",
            "Knee_L",
            "Knee_R",
            "LowerLeg_L",
            "LowerLeg_R"
        };

        public static Mesh ExtractBodyWithoutLegsMesh(
            SkinnedMeshRenderer source,
            float minLegBoneWeight = 0.35f)
        {
            if (source == null || source.sharedMesh == null)
            {
                return null;
            }

            var legBoneIndices = CollectLegBoneIndicesToHide(source.bones);
            if (legBoneIndices.Count == 0)
            {
                return null;
            }

            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[SyntyFirstPersonArmsMeshBuilder] Mesh '{mesh.name}' is not readable. " +
                    "Enable Read/Write on the body model import settings to hide legs under pants.",
                    source);
                return null;
            }

            var vertices = mesh.vertices;
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return null;
            }

            var vertexIsLeg = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertexIsLeg[i] = IsLegWeighted(boneWeights[i], legBoneIndices, minLegBoneWeight);
            }

            return BuildFilteredMesh(
                mesh,
                vertices,
                boneWeights,
                vertexIsLeg,
                includeWhenMarked: false,
                mesh.name + "_WithoutLegs");
        }

        public static Mesh ExtractBodyWithoutFeetMesh(
            SkinnedMeshRenderer source,
            float minFootBoneWeight = 0.35f)
        {
            if (source == null || source.sharedMesh == null)
            {
                return null;
            }

            var footBoneIndices = CollectFootBoneIndicesToHide(source.bones);
            if (footBoneIndices.Count == 0)
            {
                return null;
            }

            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[SyntyFirstPersonArmsMeshBuilder] Mesh '{mesh.name}' is not readable. " +
                    "Enable Read/Write on the body model import settings to hide feet under boots.",
                    source);
                return null;
            }

            var vertices = mesh.vertices;
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return null;
            }

            var vertexIsFoot = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertexIsFoot[i] = IsFootWeighted(boneWeights[i], footBoneIndices, minFootBoneWeight);
            }

            return BuildFilteredMesh(
                mesh,
                vertices,
                boneWeights,
                vertexIsFoot,
                includeWhenMarked: false,
                mesh.name + "_WithoutFeet");
        }

        public static Mesh ExtractBodyWithoutHandsMesh(
            SkinnedMeshRenderer source,
            float minHandBoneWeight = 0.35f)
        {
            if (source == null || source.sharedMesh == null)
            {
                return null;
            }

            var handBoneIndices = CollectHandBoneIndicesToHide(source.bones);
            if (handBoneIndices.Count == 0)
            {
                return null;
            }

            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[SyntyFirstPersonArmsMeshBuilder] Mesh '{mesh.name}' is not readable. " +
                    "Enable Read/Write on the body model import settings to hide palms and fingers under gloves.",
                    source);
                return null;
            }

            var vertices = mesh.vertices;
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return null;
            }

            var vertexIsHand = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertexIsHand[i] = IsHandWeighted(boneWeights[i], handBoneIndices, minHandBoneWeight);
            }

            return BuildFilteredMesh(
                mesh,
                vertices,
                boneWeights,
                vertexIsHand,
                includeWhenMarked: false,
                mesh.name + "_WithoutHands");
        }

        private static HashSet<int> CollectHandBoneIndicesToHide(IReadOnlyList<Transform> bones)
        {
            var indices = new HashSet<int>();
            if (bones == null)
            {
                return indices;
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone != null && IsHandBoneNameToHide(bone.name))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static bool IsHandBoneNameToHide(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return false;
            }

            var coreName = ExtractBoneCoreName(boneName);
            if (coreName.IndexOf("ForeArm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("LowerArm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("UpperArm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("Elbow", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("Shoulder", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("Clavicle", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            for (var i = 0; i < HandOnlyBoneNameTokens.Length; i++)
            {
                if (coreName.IndexOf(HandOnlyBoneNameTokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHandWeighted(BoneWeight boneWeight, HashSet<int> handBoneIndices, float minHandBoneWeight)
        {
            var handWeight = 0f;
            if (handBoneIndices.Contains(boneWeight.boneIndex0))
            {
                handWeight += boneWeight.weight0;
            }

            if (handBoneIndices.Contains(boneWeight.boneIndex1))
            {
                handWeight += boneWeight.weight1;
            }

            if (handBoneIndices.Contains(boneWeight.boneIndex2))
            {
                handWeight += boneWeight.weight2;
            }

            if (handBoneIndices.Contains(boneWeight.boneIndex3))
            {
                handWeight += boneWeight.weight3;
            }

            return handWeight >= minHandBoneWeight;
        }

        private static HashSet<int> CollectFootBoneIndicesToHide(IReadOnlyList<Transform> bones)
        {
            var indices = new HashSet<int>();
            if (bones == null)
            {
                return indices;
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone != null && IsFootOrToeBoneName(bone.name))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static bool IsFootWeighted(BoneWeight boneWeight, HashSet<int> footBoneIndices, float minFootBoneWeight)
        {
            var footWeight = 0f;
            if (footBoneIndices.Contains(boneWeight.boneIndex0))
            {
                footWeight += boneWeight.weight0;
            }

            if (footBoneIndices.Contains(boneWeight.boneIndex1))
            {
                footWeight += boneWeight.weight1;
            }

            if (footBoneIndices.Contains(boneWeight.boneIndex2))
            {
                footWeight += boneWeight.weight2;
            }

            if (footBoneIndices.Contains(boneWeight.boneIndex3))
            {
                footWeight += boneWeight.weight3;
            }

            return footWeight >= minFootBoneWeight;
        }

        private static HashSet<int> CollectLegBoneIndicesToHide(IReadOnlyList<Transform> bones)
        {
            var indices = new HashSet<int>();
            if (bones == null)
            {
                return indices;
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone != null && IsLegBoneNameToHide(bone.name))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static bool IsLegBoneNameToHide(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName) || IsFootOrToeBoneName(boneName))
            {
                return false;
            }

            var coreName = ExtractBoneCoreName(boneName);
            for (var i = 0; i < LegBoneCoreNamesToHide.Length; i++)
            {
                if (string.Equals(coreName, LegBoneCoreNamesToHide[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (coreName.IndexOf("UpLeg", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("UpperLeg", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("Thigh", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("Knee", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                coreName.IndexOf("LowerLeg", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return coreName.IndexOf("Leg", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFootOrToeBoneName(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return false;
            }

            var coreName = ExtractBoneCoreName(boneName);
            return coreName.IndexOf("Foot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   coreName.IndexOf("Toe", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   coreName.StartsWith("Ball", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegWeighted(BoneWeight boneWeight, HashSet<int> legBoneIndices, float minLegBoneWeight)
        {
            var legWeight = 0f;
            if (legBoneIndices.Contains(boneWeight.boneIndex0))
            {
                legWeight += boneWeight.weight0;
            }

            if (legBoneIndices.Contains(boneWeight.boneIndex1))
            {
                legWeight += boneWeight.weight1;
            }

            if (legBoneIndices.Contains(boneWeight.boneIndex2))
            {
                legWeight += boneWeight.weight2;
            }

            if (legBoneIndices.Contains(boneWeight.boneIndex3))
            {
                legWeight += boneWeight.weight3;
            }

            return legWeight >= minLegBoneWeight;
        }

        private static HashSet<int> CollectTorsoBoneIndices(IReadOnlyList<Transform> bones)
        {
            var indices = new HashSet<int>();
            if (bones == null)
            {
                return indices;
            }

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone != null && IsTorsoBoneName(bone.name))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static bool IsTorsoBoneName(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return false;
            }

            if (IsLimbOrHeadBoneName(boneName))
            {
                return false;
            }

            var coreName = ExtractBoneCoreName(boneName);
            for (var i = 0; i < TorsoBoneCoreNames.Length; i++)
            {
                if (string.Equals(coreName, TorsoBoneCoreNames[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return coreName.StartsWith("Spine", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLimbOrHeadBoneName(string boneName)
        {
            return boneName.IndexOf("Shoulder", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Clavicle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Arm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Finger", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Leg", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Foot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Toe", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Neck", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Eye", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("Jaw", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTorsoWeighted(BoneWeight boneWeight, HashSet<int> torsoBoneIndices, float minTorsoBoneWeight)
        {
            var torsoWeight = 0f;
            if (torsoBoneIndices.Contains(boneWeight.boneIndex0))
            {
                torsoWeight += boneWeight.weight0;
            }

            if (torsoBoneIndices.Contains(boneWeight.boneIndex1))
            {
                torsoWeight += boneWeight.weight1;
            }

            if (torsoBoneIndices.Contains(boneWeight.boneIndex2))
            {
                torsoWeight += boneWeight.weight2;
            }

            if (torsoBoneIndices.Contains(boneWeight.boneIndex3))
            {
                torsoWeight += boneWeight.weight3;
            }

            return torsoWeight >= minTorsoBoneWeight;
        }

        private static string ExtractBoneCoreName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var separatorIndex = name.LastIndexOf(':');
            return separatorIndex >= 0 && separatorIndex < name.Length - 1
                ? name.Substring(separatorIndex + 1)
                : name;
        }

        private static Mesh BuildFilteredMesh(
            Mesh mesh,
            Vector3[] vertices,
            BoneWeight[] boneWeights,
            bool[] vertexMask,
            bool includeWhenMarked,
            string meshName)
        {
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv = mesh.uv;
            var subMeshCount = mesh.subMeshCount;
            var includedTriangles = new List<int>(mesh.triangles.Length);
            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = triangles[i];
                    var b = triangles[i + 1];
                    var c = triangles[i + 2];
                    var keep = includeWhenMarked
                        ? vertexMask[a] || vertexMask[b] || vertexMask[c]
                        : !vertexMask[a] && !vertexMask[b] && !vertexMask[c];
                    if (!keep)
                    {
                        continue;
                    }

                    includedTriangles.Add(a);
                    includedTriangles.Add(b);
                    includedTriangles.Add(c);
                }
            }

            if (includedTriangles.Count == 0)
            {
                return null;
            }

            var remap = new int[vertices.Length];
            for (var i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var newVertices = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newTangents = new List<Vector4>();
            var newUv = new List<Vector2>();
            var newBoneWeights = new List<BoneWeight>();
            var newTriangles = new List<int>(includedTriangles.Count);

            for (var i = 0; i < includedTriangles.Count; i++)
            {
                var sourceIndex = includedTriangles[i];
                if (remap[sourceIndex] < 0)
                {
                    remap[sourceIndex] = newVertices.Count;
                    newVertices.Add(vertices[sourceIndex]);
                    if (normals != null && normals.Length == vertices.Length)
                    {
                        newNormals.Add(normals[sourceIndex]);
                    }

                    if (tangents != null && tangents.Length == vertices.Length)
                    {
                        newTangents.Add(tangents[sourceIndex]);
                    }

                    if (uv != null && uv.Length == vertices.Length)
                    {
                        newUv.Add(uv[sourceIndex]);
                    }

                    newBoneWeights.Add(boneWeights[sourceIndex]);
                }

                newTriangles.Add(remap[sourceIndex]);
            }

            var filteredMesh = new Mesh
            {
                name = meshName
            };
            filteredMesh.SetVertices(newVertices);
            if (newNormals.Count == newVertices.Count)
            {
                filteredMesh.SetNormals(newNormals);
            }
            else
            {
                filteredMesh.RecalculateNormals();
            }

            if (newTangents.Count == newVertices.Count)
            {
                filteredMesh.SetTangents(newTangents);
            }

            if (newUv.Count == newVertices.Count)
            {
                filteredMesh.SetUVs(0, newUv);
            }

            filteredMesh.boneWeights = newBoneWeights.ToArray();
            filteredMesh.bindposes = mesh.bindposes;
            filteredMesh.SetTriangles(newTriangles, 0);
            filteredMesh.RecalculateBounds();
            return filteredMesh;
        }
    }
}
