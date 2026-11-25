// @trace ART-011, ART-012, ART-014
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 把 RobotPuppy.fbx 装到伙伴狗身上：只换视觉子物体，玩法逻辑一行不动。
    ///
    /// 做什么：
    /// 1. 给 FBX 设好导入参数（Generic 骨骼、导入动画、不导入相机/灯光）；
    /// 2. 打开 chr_dog_whitebox.prefab，删掉原来用图元拼的 Body / Head / Tail / Leg_0..3；
    /// 3. 实例化 FBX 成子物体 Model，按 ART-011 的体高 0.55 m 等比缩放，包围盒中心对齐根节点
    ///    （根节点被 SphericalSurface.Place 抬到地表上方 dogHeight/2，所以模型要上下各占一半）；
    /// 4. 清掉 CompanionDog 的固定色材质覆盖，否则 Init 会把机器人贴图刷成米黄 M_Dog_Whitebox。
    ///
    /// 不做什么：不改跟随/探路/叼回逻辑，不改碰撞与层，不改 dogHeight 等数值。
    /// 菜单：Tools/垃圾星球白膜/㉔ 用 RobotPuppy 替换小狗外观
    /// </summary>
    public static class DogModelReplacer
    {
        const string FbxPath = "Assets/Art/Characters/RobotPuppy.fbx";
        const string PrefabPath = "Assets/Prefabs/Whitebox/chr_dog_whitebox.prefab";
        const string MaterialFolder = "Assets/Art/Materials";
        const string MaterialSubFolder = "Dog";

        /// <summary>ART-011：体高上限 0.6 m，配置 dogHeight = 0.55。</summary>
        const float TargetHeight = 0.55f;

        /// <summary>模型朝向修正：FBX 正面不是 +Z 时改这里（例如 180 表示掉头）。</summary>
        const float YawOffset = 0f;

        /// <summary>原来程序化图元的节点名，替换时整组删除。</summary>
        static readonly string[] OldParts = { "Body", "Head", "Tail", "Leg_0", "Leg_1", "Leg_2", "Leg_3" };

        const string SessionKey = "TrashPlanet.DogModelReplaced.v2";

        /// <summary>
        /// 首次编译完成后自动装一次：用户不用找菜单。
        /// 幂等：prefab 里已经有 Model 且没有旧图元时直接跳过；退出 Play 后再补一次。
        /// </summary>
        [InitializeOnLoadMethod]
        static void AutoInstallOnce()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += TryAutoInstall;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAutoInstall;
            };
        }

        static void TryAutoInstall()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;   // 退出 Play 后由上面的回调补跑
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                // 刚复制进工程时 Unity 可能还没导入，这里强制同步导入一次
                string absolute = System.IO.Path.Combine(
                    System.IO.Directory.GetParent(Application.dataPath).FullName, FbxPath);
                if (!System.IO.File.Exists(absolute))
                {
                    Debug.LogWarning("[DogModelReplacer] 找不到模型文件：" + FbxPath + "，跳过自动替换");
                    return;
                }
                AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceSynchronousImport);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                Debug.LogWarning("[DogModelReplacer] 模型导入失败，稍后编译/退出 Play 时会再试：" + FbxPath);
                return;
            }
            if (AlreadyReplaced())
            {
                SessionState.SetBool(SessionKey, true);
                return;
            }

            SessionState.SetBool(SessionKey, true);
            ReplaceDogVisual();
        }

        static bool AlreadyReplaced()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            bool hasModel = prefab.transform.Find("Model") != null;
            bool hasOldBody = prefab.transform.Find("Body") != null;
            return hasModel && !hasOldBody;
        }

        [MenuItem("Tools/垃圾星球白膜/㉔ 用 RobotPuppy 替换小狗外观")]
        public static void ReplaceDogVisual()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DogModelReplacer] 请先退出 Play 模式再替换");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
                throw new InvalidOperationException("缺少模型文件或尚未导入：" + FbxPath);

            ConfigureImporter();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (modelAsset == null)
                throw new InvalidOperationException("FBX 尚未被 Unity 导入：" + FbxPath);

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                for (int i = 0; i < OldParts.Length; i++)
                {
                    Transform old = root.transform.Find(OldParts[i]);
                    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                }

                Transform previous = root.transform.Find("Model");
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);

                GameObject visual = UnityEngine.Object.Instantiate(modelAsset, root.transform);
                visual.name = "Model";
                visual.transform.localPosition = Vector3.zero;
                // 保留 FBX 根节点自带的朝向修正（有些导出带 -90° X），只叠加一个绕 Y 的偏航
                visual.transform.localRotation = Quaternion.Euler(0f, YawOffset, 0f) * visual.transform.localRotation;
                visual.transform.localScale = Vector3.one;

                FitToCapsule(root.transform, visual);
                EnsureUrpMaterials(visual);
                DisableFixedColorOverride(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int renderers = modelAsset.GetComponentsInChildren<Renderer>(true).Length;
            Debug.Log("[DogModelReplacer] 完成：" + PrefabPath + " 外观已换成 " + FbxPath
                      + "，体高 " + TargetHeight.ToString("0.00") + " m"
                      + "，渲染器 " + renderers + " 个"
                      + "，固定色覆盖已关闭（CompanionDog.fixedMaterial = null）");

            // 外观重建会连带删掉 Model 上原有的 Animator，这里顺手把骨骼动画接回去
            try
            {
                DogAnimatorSetup.Install();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DogModelReplacer] 接骨骼动画失败（可手动跑菜单 ㉕）：" + e.Message);
            }
        }

        /// <summary>批量入口（-executeMethod 用）。</summary>
        public static void Run()
        {
            ReplaceDogVisual();
        }

        // ---------------------------------------------------------------
        // 导入设置
        // ---------------------------------------------------------------

        static void ConfigureImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            }
            if (importer == null)
                throw new InvalidOperationException("无法获取 ModelImporter：" + FbxPath);

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }
            if (importer.importCameras)
            {
                importer.importCameras = false;
                changed = true;
            }
            if (importer.importLights)
            {
                importer.importLights = false;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------
        // 缩放与对位
        // ---------------------------------------------------------------

        /// <summary>按体高等比缩放，并把包围盒中心挪到根节点原点（底部 -h/2、顶部 +h/2）。</summary>
        static void FitToCapsule(Transform parent, GameObject visual)
        {
            Bounds bounds = CalculateBounds(visual);
            if (bounds.size.y <= 0.0001f)
                throw new InvalidOperationException("模型没有可渲染网格，无法按体高缩放：" + FbxPath);

            float scale = TargetHeight / bounds.size.y;
            visual.transform.localScale = Vector3.one * scale;

            bounds = CalculateBounds(visual);
            Vector3 center = parent.InverseTransformPoint(bounds.center);
            visual.transform.localPosition -= new Vector3(center.x, center.y, center.z);

            Debug.Log("[DogModelReplacer] 缩放系数=" + scale.ToString("0.0000")
                      + "，包围盒高=" + (bounds.size.y).ToString("0.000") + " m");
        }

        static Bounds CalculateBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        // ---------------------------------------------------------------
        // 材质：内嵌 FBX 材质若是内置管线 shader，URP 下会显示成粉红
        // ---------------------------------------------------------------

        static void EnsureUrpMaterials(GameObject visual)
        {
            Shader urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) return; // 非 URP 工程，保持原样

            string folder = MaterialFolder + "/" + MaterialSubFolder;
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(MaterialFolder, MaterialSubFolder);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            var done = new Dictionary<Material, Material>();

            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] mats = renderers[r].sharedMaterials;
                bool touched = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = mats[i];
                    if (src == null) continue;
                    if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline"))
                        continue;

                    Material dst;
                    if (!done.TryGetValue(src, out dst))
                    {
                        dst = CreateUrpCopy(src, urp, folder);
                        done[src] = dst;
                    }
                    mats[i] = dst;
                    touched = true;
                }
                if (touched) renderers[r].sharedMaterials = mats;
            }

            if (done.Count > 0)
                Debug.Log("[DogModelReplacer] 已把 " + done.Count + " 个 FBX 内嵌材质转成 URP/Lit（保留原贴图）");
        }

        static Material CreateUrpCopy(Material src, Shader urp, string folder)
        {
            string fileName = "M_RobotPuppy_" + Sanitize(src.name) + ".mat";
            string path = folder + "/" + fileName;

            Material dst = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (dst == null)
            {
                dst = new Material(urp);
                AssetDatabase.CreateAsset(dst, path);
            }
            dst.shader = urp;

            Texture tex = null;
            if (src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
            else if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");

            if (tex != null)
            {
                if (dst.HasProperty("_BaseMap")) dst.SetTexture("_BaseMap", tex);
                if (dst.HasProperty("_MainTex")) dst.SetTexture("_MainTex", tex);
                dst.mainTexture = tex;
            }

            Color color = Color.white;
            if (src.HasProperty("_Color")) color = src.GetColor("_Color");
            else if (src.HasProperty("_BaseColor")) color = src.GetColor("_BaseColor");
            if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", color);
            if (dst.HasProperty("_Color")) dst.SetColor("_Color", color);

            if (dst.HasProperty("_Smoothness")) dst.SetFloat("_Smoothness", 0.25f);
            if (dst.HasProperty("_Metallic")) dst.SetFloat("_Metallic", 0f);

            EditorUtility.SetDirty(dst);
            return dst;
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Default";
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) chars[i] = '_';
            }
            return new string(chars);
        }

        // ---------------------------------------------------------------
        // 关闭固定色覆盖
        // ---------------------------------------------------------------

        /// <summary>
        /// CompanionDog.Init 会用 fixedMaterial 刷所有 fixedColorRenderers。
        /// 换真模型后必须清空，否则机器人贴图会被米黄纯色覆盖（ART-012 的固定色只适用于白膜图元）。
        /// </summary>
        static void DisableFixedColorOverride(GameObject root)
        {
            CompanionDog dog = root.GetComponent<CompanionDog>();
            if (dog == null) return;
            dog.FixedColorRenderers = new Renderer[0];
            dog.FixedMaterial = null;
            EditorUtility.SetDirty(dog);
        }
    }
}
