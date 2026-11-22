// @trace ART-004
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 垃圾选中描边的唯一构建入口（ART-004）。
    ///
    /// 旧做法的缺陷：Outline 用 ShapeScale/ShapeType 生成的白膜图元（Cube/Cylinder/Sphere）再等比放大
    /// 1.067 倍。这样描边跟随的是"未替换材质前的白模轮廓"，且与已被美术替换的 Body 不是同一套几何，
    /// 出现位置错开、厚度不均、走向不贴合纹理边缘的问题；启动时 WhiteboxTrashOutlineFix 还会把描边刷回白膜。
    ///
    /// 新做法：Outline 直接复制当前 prefab 里"替换后的美术 Body"整套层级（网格、UV、变换完全一致），
    /// 再把每份网格的顶点沿平滑法线在世界空间外扩固定厚度。因为与 Body 共用同一几何与变换，
    /// 描边位置、走向天然贴合材质表面与纹理边缘；沿法线外扩保证各方向厚度一致。
    ///
    /// 生成物全部通过 Editor API 写入（符合"不手写 YAML"红线），且只碰垃圾 prefab 的 Outline，
    /// 不修改 Body 的网格/材质/碰撞体，也不触碰玩家、狗、中转站、场景与 UI。
    /// </summary>
    public static class TrashOutlineBuilder
    {
        const string PrefabFolder = "Assets/Prefabs/Whitebox";
        const string OutlineMeshFolder = "Assets/Art/Generated/TrashOutlines";
        const string OutlineMaterialPath = "Assets/Art/Materials/M_Highlight_Whitebox.mat";

        // 描边厚度：按物体最大世界尺寸的比例取值，并钳制在合理区间，避免大物体过粗、小物体过细。
        const float ThicknessRatio = 0.02f;
        const float MinThickness = 0.008f;
        const float MaxThickness = 0.02f;

        // 与 WhiteboxBuilder 的垃圾 prefab 命名保持一致（7 个：4 普通 + 3 大型）。
        static readonly string[] CategoryNames = { "plastic", "metal", "organic", "electronic" };
        static readonly string[] LargeNames = { "tire", "metal_device", "appliance" };

        static string PrefabName(int i)
        {
            return i >= 4
                ? "prop_trash_large_" + LargeNames[i - 4] + "_whitebox"
                : "prop_trash_" + CategoryNames[i] + "_whitebox";
        }

        /// <summary>手动入口：重建全部 7 个垃圾 prefab 的贴合描边。</summary>
        [MenuItem("Tools/垃圾星球白膜/⑥B 重建垃圾描边（贴合替换后模型）", false, 12)]
        public static void RebuildMenu()
        {
            bool ok = RebuildAll();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("垃圾描边重建",
                    ok
                        ? "已把 7 个垃圾预制体的描边改为复制替换后的美术模型，并沿表面法线外扩。\n" +
                          "描边位置、厚度、走向现在与材质表现一致。"
                        : "重建未全部成功，请看 Console 里 [TrashOutlineBuilder] 的日志。", "好");
        }

        /// <summary>重建全部 7 个垃圾 prefab 的描边；返回是否全部成功。</summary>
        public static bool RebuildAll()
        {
            Material outlineMat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (outlineMat == null)
            {
                Debug.LogError("[TrashOutlineBuilder] 缺少描边材质：" + OutlineMaterialPath);
                return false;
            }

            bool allOk = true;
            for (int i = 0; i < 7; i++)
            {
                string name = PrefabName(i);
                string path = PrefabFolder + "/" + name + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning("[TrashOutlineBuilder] 找不到垃圾预制体：" + path);
                    allOk = false;
                    continue;
                }

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform body = contents.transform.Find("Body");
                    if (body == null)
                    {
                        Debug.LogWarning("[TrashOutlineBuilder] " + name + " 缺少 Body，跳过");
                        allOk = false;
                        continue;
                    }

                    Transform ring = contents.transform.Find("HighlightRing");
                    if (ring != null) UnityEngine.Object.DestroyImmediate(ring.gameObject);

                    GameObject outline = BuildOutline(contents.transform, body, outlineMat, name);

                    TrashItem item = contents.GetComponent<TrashItem>();
                    if (item != null)
                    {
                        SerializedObject so = new SerializedObject(item);
                        SerializedProperty op = so.FindProperty("outline");
                        if (op != null) op.objectReferenceValue = outline;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    Debug.Log("[TrashOutlineBuilder] 已重建描边：" + name);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[TrashOutlineBuilder] 重建描边失败 " + name + "：" + e);
                    allOk = false;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return allOk;
        }

        /// <summary>
        /// 供 TrashArtReplacer 复用：基于给定的美术 Body 生成紧贴的 Outline，并挂到 root 下。
        /// </summary>
        public static GameObject BuildOutline(Transform root, Transform body, Material outlineMaterial, string key)
        {
            Transform old = root.Find("Outline");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            // 复制替换后的 Body 整套层级：网格、UV、局部旋转/缩放全部一致 → 位置与走向天然贴合。
            GameObject outline = UnityEngine.Object.Instantiate(body.gameObject, root);
            outline.name = "Outline";
            outline.SetActive(false);

            foreach (Collider c in outline.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);

            MeshFilter[] bodyFilters = body.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter[] outlineFilters = outline.GetComponentsInChildren<MeshFilter>(true);
            int count = Mathf.Min(bodyFilters.Length, outlineFilters.Length);
            for (int i = 0; i < count; i++)
            {
                Mesh src = bodyFilters[i].sharedMesh;
                if (src == null) continue;
                string assetPath = OutlineMeshFolder + "/" + Sanitize(key) + "_Outline_" + i + ".asset";
                Mesh inflated = GetOrCreateInflatedMesh(src, bodyFilters[i].transform, assetPath);
                if (inflated != null) outlineFilters[i].sharedMesh = inflated;
            }

            foreach (Renderer r in outline.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = outlineMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            return outline;
        }

        /// <summary>
        /// 判定 7 个 prefab 的描边是否已是"贴合美术模型"版本。
        /// 旧的白膜图元描边会在这里被判为需重建：网格数量不一致 / 网格不是本项目生成的描边网格 / 材质不对。
        /// </summary>
        public static bool NeedsRebuild()
        {
            Material outlineMat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            for (int i = 0; i < 7; i++)
            {
                string name = PrefabName(i);
                string path = PrefabFolder + "/" + name + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform body = contents.transform.Find("Body");
                    Transform outline = contents.transform.Find("Outline");
                    if (body == null || outline == null) return true;

                    if (contents.transform.Find("HighlightRing") != null) return true;

                    MeshFilter[] bf = body.GetComponentsInChildren<MeshFilter>(true);
                    MeshFilter[] of = outline.GetComponentsInChildren<MeshFilter>(true);
                    if (bf.Length != of.Length) return true;

                    for (int m = 0; m < of.Length; m++)
                    {
                        Mesh om = of[m].sharedMesh;
                        // 本项目生成的描边网格带 "_Outline_" 标记；内置图元（Cube/Sphere/Cylinder）不匹配。
                        if (om == null || !om.name.Contains("_Outline_")) return true;
                    }

                    foreach (Renderer r in outline.GetComponentsInChildren<Renderer>(true))
                        if (r.sharedMaterial != outlineMat) return true;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            return false;
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/Generated"))
                AssetDatabase.CreateFolder("Assets/Art", "Generated");
            if (!AssetDatabase.IsValidFolder(OutlineMeshFolder))
                AssetDatabase.CreateFolder("Assets/Art/Generated", "TrashOutlines");
        }

        static Mesh GetOrCreateInflatedMesh(Mesh src, Transform t, string path)
        {
            EnsureFolder();
            Mesh dst = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (dst == null)
            {
                dst = new Mesh();
                dst.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(dst, path);
            }
            else
            {
                dst.Clear();
            }

            CopyMeshData(src, dst);

            // 用 Body 当前的 lossyScale 估算世界尺寸，得到均匀的世界空间描边厚度。
            Vector3 localSize = src.bounds.size;
            Vector3 worldSize = new Vector3(
                localSize.x * Mathf.Abs(t.lossyScale.x),
                localSize.y * Mathf.Abs(t.lossyScale.y),
                localSize.z * Mathf.Abs(t.lossyScale.z));
            float largest = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));
            float thickness = Mathf.Clamp(largest * ThicknessRatio, MinThickness, MaxThickness);

            InflateAlongNormals(dst, t, thickness);
            EditorUtility.SetDirty(dst);
            return dst;
        }

        static void CopyMeshData(Mesh src, Mesh dst)
        {
            dst.indexFormat = src.indexFormat;
            dst.vertices = src.vertices;

            Vector3[] normals = src.normals;
            if (normals != null && normals.Length == src.vertexCount) dst.normals = normals;
            if (src.uv != null && src.uv.Length == src.vertexCount) dst.uv = src.uv;
            if (src.uv2 != null && src.uv2.Length == src.vertexCount) dst.uv2 = src.uv2;
            if (src.tangents != null && src.tangents.Length == src.vertexCount) dst.tangents = src.tangents;
            if (src.colors32 != null && src.colors32.Length == src.vertexCount) dst.colors32 = src.colors32;

            dst.subMeshCount = src.subMeshCount;
            for (int i = 0; i < src.subMeshCount; i++)
                dst.SetTriangles(src.GetTriangles(i), i, false);
            dst.RecalculateBounds();
        }

        /// <summary>
        /// 顶点沿平滑法线外扩：按位置合并法线，避免硬边处描边开裂；位移在世界空间计算再换回局部空间，
        /// 使非均匀缩放的子物体也保持均匀厚度。
        /// </summary>
        static void InflateAlongNormals(Mesh mesh, Transform t, float thickness)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length != verts.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }
            if (normals == null || normals.Length != verts.Length) return;

            var groups = new Dictionary<string, List<int>>();
            for (int i = 0; i < verts.Length; i++)
            {
                string key = PositionKey(verts[i]);
                List<int> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<int>();
                    groups.Add(key, group);
                }
                group.Add(i);
            }

            Vector3[] smooth = new Vector3[verts.Length];
            foreach (List<int> group in groups.Values)
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < group.Count; i++) sum += normals[group[i]];
                if (sum.sqrMagnitude < 1e-12f) sum = normals[group[0]];
                sum.Normalize();
                for (int i = 0; i < group.Count; i++) smooth[group[i]] = sum;
            }

            Matrix4x4 localToWorld = t.localToWorldMatrix;
            Matrix4x4 worldToLocal = t.worldToLocalMatrix;
            Matrix4x4 normalMatrix = Matrix4x4.Inverse(localToWorld).transpose;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 worldNormal = normalMatrix.MultiplyVector(smooth[i]);
                if (worldNormal.sqrMagnitude < 1e-12f) continue;
                worldNormal.Normalize();
                Vector3 localDelta = worldToLocal.MultiplyVector(worldNormal * thickness);
                verts[i] += localDelta;
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        static string PositionKey(Vector3 p)
        {
            return Mathf.RoundToInt(p.x * 10000f) + "_" +
                   Mathf.RoundToInt(p.y * 10000f) + "_" +
                   Mathf.RoundToInt(p.z * 10000f);
        }
    }
}
