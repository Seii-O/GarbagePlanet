// @trace TOOL-015, TOOL-016
using System.Collections.Generic;
using PlanetCleaner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 手持工具白膜构建器（Tools/垃圾星球白膜/⑱ 生成手持工具模型）。
    /// 全程 Editor API，不手写场景 YAML；只碰相机 Hands/HandRight 下的手持模型。
    /// 唯一例外：若双手挂点已偏离标准可视几何（历史 y=-0.58 或手动拖过 Hierarchy），
    /// 会先调用 WhiteboxHandsFix 按标准几何重建一次双手，否则工具会跟着生成到画面外。
    /// 不动狗 / 其它 prefab，也不主动改手型。
    /// 美术到位后：把 entries 里各模型的 Mesh 换成美术模型即可，其余逻辑不变。
    /// </summary>
    public static class ToolViewModelBuilder
    {
        const string Tag = "[ToolViewModelBuilder] ";
        const string MaterialFolder = "Assets/Art/Materials";
        const string ArtFolder = "Assets/Art/Models/Tools";   // 美术模型约定目录：{toolId}.fbx
        const float HeldSize = 0.30f;                          // 手持后最长边约 30cm

        // 10 件工具：toolId + 中文名（与 ToolConfig 一致）
        static readonly string[] Ids =
        {
            "T-01", "T-02", "T-03", "T-04", "T-05", "T-06", "T-07", "T-08", "T-09", "T-10"
        };
        static readonly string[] Names =
        {
            "拾取器", "压缩钳", "磁吸爪", "堆肥铲", "解码钳",
            "加强拾取夹", "磁铁手套", "星球净化器", "生态专家", "快速分解"
        };

        // 4 本技能书也能拿手上看（纯视觉，不改变层数/效果）；key 直接用 rewardId
        static readonly string[] BookIds =
        {
            "book_sorting", "book_handwork", "book_flow", "book_tireless"
        };
        static readonly string[] BookNames =
        {
            "快分拣", "熟手工", "一气呵成", "不知疲倦"
        };

        [MenuItem("Tools/垃圾星球白膜/⑱ 生成手持工具模型", false, 22)]
        public static void BuildMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath + " 再生成");
                return;
            }
            bool ok = BuildInScene(scene);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("手持工具模型",
                    ok ? "已在双手 HandRight 下生成 " + (Ids.Length + BookIds.Length) + " 件手持模型（10 工具 + 4 技能书）并接线。\n有美术 FBX 的用美术模型，缺的回退白膜。\n进入 Play 后在背包装备/切换即可看到手上拿着。"
                        : "生成失败，请看 Console 的 [ToolViewModelBuilder] 日志。", "好");
        }

        /// <summary>命令行/CI 入口。</summary>
        public static void BuildBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            BuildInScene(scene);
        }

        static bool BuildInScene(Scene scene)
        {
            // 找 FirstPersonHands → RightHand
            FirstPersonHands hands = Object.FindFirstObjectByType<FirstPersonHands>();
            if (hands == null || hands.RightHand == null)
            {
                Debug.LogError(Tag + "场景里找不到 FirstPersonHands / RightHand，先跑 ③ 生成双手");
                return false;
            }
            // 双手挂点若已被挪出可视区（历史 y=-0.58 或手动拖过 Hierarchy），先按标准几何重建。
            // 否则工具是挂在手上的子物体，会跟着一起落到画面外——表现就是「按数字没模型，手也没有」。
            Camera handCam = hands.transform.parent != null
                ? hands.transform.parent.GetComponent<Camera>() : Camera.main;
            if (WhiteboxHandsFix.EnsureVisible(hands, handCam))
            {
                hands = Object.FindFirstObjectByType<FirstPersonHands>();
                if (hands == null || hands.RightHand == null)
                {
                    Debug.LogError(Tag + "重建双手后仍找不到 RightHand，已中止");
                    return false;
                }
            }
            // 手本体显隐跟随开关（Tools > 星球白膜 > ⑤），默认只留工具
            WhiteboxHandsFix.ApplyHandMeshVisibility(hands);

            // 找 WhiteboxGame
            WhiteboxGame game = Object.FindFirstObjectByType<WhiteboxGame>();
            if (game == null)
            {
                Debug.LogError(Tag + "场景里找不到 WhiteboxGame");
                return false;
            }

            Material mat = ToolMat();
            Transform right = hands.RightHand;

            // 幂等：先删旧的手持根节点
            Transform old = right.Find("HeldTools");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            GameObject root = new GameObject("HeldTools");
            root.transform.SetParent(right, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            var entries = new ToolViewModel.Entry[Ids.Length + BookIds.Length];
            for (int i = 0; i < Ids.Length; i++)
            {
                GameObject model = BuildOne(root.transform, Ids[i], Names[i], i, mat);
                model.SetActive(false); // 默认空手，运行时按 activeToolId 显隐
                entries[i] = new ToolViewModel.Entry { toolId = Ids[i], model = model };
            }
            for (int i = 0; i < BookIds.Length; i++)
            {
                int slot = Ids.Length + i;
                GameObject model = BuildOne(root.transform, BookIds[i], BookNames[i], slot, mat);
                model.SetActive(false);
                entries[slot] = new ToolViewModel.Entry { toolId = BookIds[i], model = model };
            }

            // 挂视图脚本并注入映射（挂在同一 root，跟随右手晃动）
            ToolViewModel vm = root.GetComponent<ToolViewModel>();
            if (vm == null) vm = root.AddComponent<ToolViewModel>();
            vm.Wire(entries);

            // Wire 到 Game（运行时 Init 刷新显隐）
            game.WireHeldTools(vm);
            EditorUtility.SetDirty(game);
            EditorUtility.SetDirty(vm);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完成：HandRight/HeldTools 下生成 " + (Ids.Length + BookIds.Length) + " 件手持模型并接线");
            return true;
        }

        /// <summary>每件工具用简单图元拼一个可辨识的白膜形状，统一放在右手掌心前方（本地 +Z）。</summary>
        static GameObject BuildOne(Transform parent, string id, string name, int index, Material mat)
        {
            GameObject root = new GameObject("Tool_" + id + "_" + name);
            root.transform.SetParent(parent, false);
            // 沿右手掌心摆放：HandRight 腕点在 (±0.324,-0.195,+0.941)、掌心在 (±0.232,-0.177,+0.580)，
            // 工具落在「腕点 + (-0.10, 0.10, -0.42)」→ 相机空间约 (0.224,-0.095,+0.521)，
            // 即掌心前上方一点。注意本地 +Z 是前方，偏移写 -0.42 是「比腕点靠近相机」。
            root.transform.localPosition = new Vector3(-0.10f, 0.10f, -0.42f);
            root.transform.localRotation = Quaternion.identity;

            // 有美术模型（Assets/Art/Models/Tools/{toolId}.fbx）就用美术模型；没有则回退白膜图元
            if (BuildArtModel(root.transform, id)) return root;

            // 技能书（index >= 工具数）：白膜回退就是一本厚书
            if (index >= Ids.Length)
            {
                Part(root.transform, "Cover", PrimitiveType.Cube, new Vector3(0.16f, 0.22f, 0.04f), new Vector3(0f, 0f, -0.14f), mat);
                return root;
            }

            switch (index)
            {
                case 0: // T-01 拾取器：伸缩杆 + 夹头
                    Part(root.transform, "Shaft", PrimitiveType.Cylinder, new Vector3(0.03f, 0.16f, 0.03f), new Vector3(0, 0, -0.10f), mat);
                    Part(root.transform, "JawL", PrimitiveType.Cube, new Vector3(0.03f, 0.02f, 0.09f), new Vector3(-0.04f, 0, -0.20f), mat);
                    Part(root.transform, "JawR", PrimitiveType.Cube, new Vector3(0.03f, 0.02f, 0.09f), new Vector3(0.04f, 0, -0.20f), mat);
                    break;
                case 1: // T-02 压缩钳：两片压板
                    Part(root.transform, "PlateA", PrimitiveType.Cube, new Vector3(0.14f, 0.02f, 0.10f), new Vector3(0, 0.03f, -0.12f), mat);
                    Part(root.transform, "PlateB", PrimitiveType.Cube, new Vector3(0.14f, 0.02f, 0.10f), new Vector3(0, -0.03f, -0.12f), mat);
                    break;
                case 2: // T-03 磁吸爪：圆盘 + 三爪
                    Part(root.transform, "Disc", PrimitiveType.Cylinder, new Vector3(0.10f, 0.02f, 0.10f), new Vector3(0, 0, -0.14f), mat);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * Mathf.PI * 2f / 3f;
                        Part(root.transform, "Claw" + k, PrimitiveType.Cube, new Vector3(0.02f, 0.02f, 0.07f),
                            new Vector3(Mathf.Cos(a) * 0.08f, Mathf.Sin(a) * 0.08f, -0.19f), mat);
                    }
                    break;
                case 3: // T-04 堆肥铲：柄 + 铲面
                    Part(root.transform, "Handle", PrimitiveType.Cylinder, new Vector3(0.025f, 0.13f, 0.025f), new Vector3(0, 0, -0.08f), mat);
                    Part(root.transform, "Blade", PrimitiveType.Cube, new Vector3(0.13f, 0.02f, 0.14f), new Vector3(0, 0, -0.22f), mat);
                    break;
                case 4: // T-05 解码钳：手柄 + 探头
                    Part(root.transform, "Grip", PrimitiveType.Cube, new Vector3(0.06f, 0.06f, 0.10f), new Vector3(0, 0, -0.06f), mat);
                    Part(root.transform, "Probe", PrimitiveType.Cylinder, new Vector3(0.015f, 0.10f, 0.015f), new Vector3(0, 0, -0.18f), mat);
                    break;
                case 5: // T-06 加强拾取夹：加长夹
                    Part(root.transform, "Bar", PrimitiveType.Cube, new Vector3(0.05f, 0.03f, 0.18f), new Vector3(0, 0, -0.12f), mat);
                    Part(root.transform, "TipA", PrimitiveType.Cube, new Vector3(0.02f, 0.07f, 0.05f), new Vector3(-0.045f, 0, -0.22f), mat);
                    Part(root.transform, "TipB", PrimitiveType.Cube, new Vector3(0.02f, 0.07f, 0.05f), new Vector3(0.045f, 0, -0.22f), mat);
                    break;
                case 6: // T-07 磁铁手套：掌块 + 磁极
                    Part(root.transform, "Glove", PrimitiveType.Cube, new Vector3(0.12f, 0.05f, 0.13f), new Vector3(0, 0, -0.11f), mat);
                    Part(root.transform, "Pole", PrimitiveType.Cylinder, new Vector3(0.05f, 0.03f, 0.05f), new Vector3(0, 0, -0.20f), mat);
                    break;
                case 7: // T-08 星球净化器：罐体 + 环
                    Part(root.transform, "Tank", PrimitiveType.Cylinder, new Vector3(0.09f, 0.12f, 0.09f), new Vector3(0, 0, -0.13f), mat);
                    Part(root.transform, "Ring", PrimitiveType.Cylinder, new Vector3(0.11f, 0.02f, 0.11f), new Vector3(0, 0.06f, -0.13f), mat);
                    break;
                case 8: // T-09 生态专家：平板 + 支架
                    Part(root.transform, "Panel", PrimitiveType.Cube, new Vector3(0.14f, 0.10f, 0.015f), new Vector3(0, 0, -0.12f), mat);
                    Part(root.transform, "Stem", PrimitiveType.Cylinder, new Vector3(0.015f, 0.05f, 0.015f), new Vector3(0, -0.06f, -0.12f), mat);
                    break;
                default: // T-10 快速分解：刀刃 + 柄
                    Part(root.transform, "Edge", PrimitiveType.Cube, new Vector3(0.03f, 0.02f, 0.20f), new Vector3(0, 0, -0.16f), mat);
                    Part(root.transform, "Hilt", PrimitiveType.Cube, new Vector3(0.07f, 0.04f, 0.06f), new Vector3(0, 0, -0.05f), mat);
                    break;
            }
            return root;
        }

        /// <summary>美术模型路径约定：Assets/Art/Models/Tools/{toolId}.fbx。</summary>
        public static string ArtModelPath(string toolId)
        {
            return ArtFolder + "/" + toolId + ".fbx";
        }

        /// <summary>
        /// 用美术 FBX 替换白膜：实例化后去掉碰撞、关阴影、按包围盒归一化尺寸并居中前推。
        /// 找不到模型返回 false，由调用方回退白膜图元——所以缺模型也不会坏。
        /// </summary>
        static bool BuildArtModel(Transform parent, string id)
        {
            string path = ArtModelPath(id);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return false;

            GameObject inst = PrefabUtility.InstantiatePrefab(asset) as GameObject;
            if (inst == null) inst = Object.Instantiate(asset);
            if (inst == null) return false;

            inst.name = "Art_" + id;
            inst.transform.SetParent(parent, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localScale = Vector3.one;
            // AI 生成模型朝向不固定：先给一个基准朝向，视觉不对再调这一行
            inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            foreach (Collider c in inst.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            Bounds b = ComputeBounds(inst);
            if (b.size.sqrMagnitude > 1e-8f)
            {
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float k = HeldSize / Mathf.Max(0.0001f, maxDim);
                inst.transform.localScale = Vector3.one * k;

                b = ComputeBounds(inst);
                inst.transform.localPosition = -parent.InverseTransformPoint(b.center)
                                              + new Vector3(0f, 0f, -HeldSize * 0.45f);
            }

            Debug.Log(Tag + "使用美术模型：" + path + "（" + id + "）");
            return true;
        }

        static Bounds ComputeBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs == null || rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static GameObject Part(Transform parent, string name, PrimitiveType type,
            Vector3 scale, Vector3 pos, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) { mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            return go;
        }

        static Material ToolMat()
        {
            string path = MaterialFolder + "/M_HeldTool_Whitebox.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh) { name = "M_HeldTool_Whitebox" };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.62f, 0.66f, 0.72f, 1f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.62f, 0.66f, 0.72f, 1f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
