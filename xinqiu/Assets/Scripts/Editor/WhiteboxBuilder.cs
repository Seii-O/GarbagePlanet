// @trace LEV-003, ART-001, ART-007, ART-011, ART-012, ART-014, MECH-017, PLAT-005
using System;
using System.Collections.Generic;
using System.IO;
using PlanetCleaner;
using TrashPlanet.Whitebox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 一键生成白膜工程骨架：材质 / 配置资产 / 预制体 / 场景 / 构建列表。
    /// 全程用 Editor API 构建，不手写任何场景或预制体 YAML（策划案 §12 流程红线）。
    ///
    /// 菜单：Tools/垃圾星球白膜/① 生成场景与预制体
    /// 命令行：Unity.exe -batchmode -quit -projectPath <工程> -executeMethod
    ///         TrashPlanet.Whitebox.Editor.WhiteboxBuilder.BuildAllBatch -logFile <日志>
    ///
    /// 首次导入脚本后会自动构建一次（场景不存在时），不替换用户当前打开的场景。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxBuilder
    {
        public const string ScenePath = "Assets/Scenes/WhiteboxPrototype.unity";
        public const string ConfigPath = "Assets/Data/WhiteboxConfig.asset";
        const string MaterialFolder = "Assets/Art/Materials";
        const string PrefabFolder = "Assets/Prefabs/Whitebox";
        const string SessionKey = "TrashPlanet.Whitebox.AutoBuilt.v2";
        const string LogTag = "[WhiteboxBuilder] ";

        // 白色描边相对物体的放大系数。原始 1.10（比物体大 10%）→ 收窄到当前的三分之二 = 1.06667（大 6.667%）。
        const float ThinOutlineScale = 1.06667f;

        static readonly string[] CategoryNames = { "plastic", "metal", "organic", "electronic" };
        static readonly string[] LargeNames = { "tire", "metal_device", "appliance" };

        sealed class Mats
        {
            public Material Ground, Water, Trash, Dog, Station, Rust, Landmark, Highlight, Player, Hands;
        }

        static WhiteboxBuilder()
        {
            EditorApplication.delayCall += AutoBuild;
        }

        static void AutoBuild()
        {
            if (Application.isBatchMode) return;
            // 全局总闸：UI 定稿并锁定后，编译/重启不再自动构建/校验，保持场景完全静默。
            if (UiAutoBuildGuard.Locked) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoBuild;
                return;
            }
            SessionState.SetBool(SessionKey, true);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.Log(LogTag + "首次导入：自动构建白膜场景与预制体");
                if (BuildAll()) WhiteboxValidator.Validate();
                return;
            }

            // 场景已存在（例如脚本改动触发了重编译）：补一次编辑器内校验并落盘报告，
            // 不重建、不替换用户当前打开的场景。
            string report = Path.Combine(Application.dataPath, "../docs/whitebox-validate.json");
            if (!File.Exists(report))
            {
                Debug.Log(LogTag + "场景已存在，补跑一次编辑器内校验");
                WhiteboxValidator.Validate();
            }
        }

        [MenuItem("Tools/垃圾星球白膜/① 生成场景与预制体", false, 10)]
        public static void BuildAllMenu()
        {
            // ① 是「整场景重建」：会把手调过的 UI 一起覆盖掉，先二次确认
            if (!Application.isBatchMode && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                if (!EditorUtility.DisplayDialog("重建白膜场景？",
                        "① 会用代码版整块重建 " + ScenePath + " 与 10 个预制体。\n\n"
                        + "你在 Hierarchy / Inspector 里手调过的 UI 布局、颜色、文案都会被覆盖。\n"
                        + "只是改 UI 的话，请点「取消」，直接在场景里改就好。",
                        "仍然重建（会覆盖手改）", "取消"))
                {
                    Debug.Log(LogTag + "已取消 ①，场景保持手改现状");
                    return;
                }
            }
            bool ok = BuildAll();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("白膜工程骨架",
                    ok ? "已生成：材质 / 配置 / 10 个预制体 / WhiteboxPrototype.unity / 构建列表\n\n"
                         + "按 Play 即可验证；详见 docs/ 下的验证文档。"
                         : "生成失败，请看 Console 里 [WhiteboxBuilder] 的错误日志。", "好");
        }

        [MenuItem("Tools/垃圾星球白膜/② 打开白膜场景", false, 11)]
        public static void OpenScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogWarning(LogTag + "场景不存在，先执行 ①");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>批处理入口：显式设置退出码，供 CI / 命令行复现。</summary>
        public static void BuildAllBatch()
        {
            try
            {
                bool ok = BuildAll();
                Debug.Log(LogTag + "batch result = " + (ok ? "OK" : "FAILED"));
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(LogTag + "batch exception: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static bool BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(LogTag + "请先退出 Play 模式再构建");
                return false;
            }

            Scene previous = SceneManager.GetActiveScene();
            Scene work = default(Scene);
            try
            {
                EnsureFolders();
                EnsureLayers();
                Mats mats = CreateMaterials();
                WhiteboxConfig cfg = CreateConfig();
                float radius = cfg.PlanetRadius;

                work = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(work);

                TrashItem[] trashPrefabs = CreateTrashPrefabs(mats, cfg);
                GameObject dogPrefab = CreateDogPrefab(mats, cfg);
                GameObject stationPrefab = CreateStationPrefab(mats);
                GameObject landmarkPrefab = CreateLandmarkPrefab(mats);

                BuildSceneContents(work, mats, cfg, radius, trashPrefabs,
                    dogPrefab, stationPrefab, landmarkPrefab);

                // Hierarchy 里可编辑的 uGUI 面板：只往当前场景补 UI 根与 EventSystem，
                // 不重建场景，也不动 chr_player_whitebox / Main Camera / Hands。
                WhiteboxUiBuilder.EnsureUiInScene(work);

                EditorSceneManager.MarkSceneDirty(work);
                if (!EditorSceneManager.SaveScene(work, ScenePath))
                {
                    Debug.LogError(LogTag + "场景保存失败：" + ScenePath);
                    return false;
                }
                AddSceneToBuildSettings(ScenePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(LogTag + "完成：场景=" + ScenePath
                          + "，预制体=" + (trashPrefabs.Length + 3)
                          + "，半径=" + radius
                          + "，Unity=" + Application.unityVersion);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogTag + "构建失败：" + e);
                return false;
            }
            finally
            {
                if (work.IsValid() && work.isLoaded)
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(work, true);
                }
            }
        }

        // ---------------------------------------------------------------
        // 资产
        // ---------------------------------------------------------------

        static Mats CreateMaterials()
        {
            var mats = new Mats
            {
                Ground = Mat("M_Ground_Whitebox.mat", WhiteboxConfig.Hex("#6E6053"), false, 0.20f),
                Water = Mat("M_Water_Whitebox.mat", WhiteboxConfig.Hex("#3A4A4E"), false, 0.45f),
                Trash = Mat("M_Trash_Whitebox.mat", WhiteboxConfig.Hex("#7A6A52"), false, 0.28f),
                Dog = Mat("M_Dog_Whitebox.mat", WhiteboxConfig.Hex("#D8C3A0"), false, 0.35f),
                Station = Mat("M_Station_Whitebox.mat", WhiteboxConfig.Hex("#DBD5C5"), false, 0.30f),
                Rust = Mat("M_Rust_Whitebox.mat", WhiteboxConfig.Hex("#8C3E2E"), false, 0.25f),
                Landmark = Mat("M_Landmark_Whitebox.mat", WhiteboxConfig.Hex("#5C5340"), false, 0.25f),
                Highlight = Mat("M_Highlight_Whitebox.mat", Color.white, true, 0f),
                Player = Mat("M_Player_Whitebox.mat", WhiteboxConfig.Hex("#E2E4EC"), false, 0.30f),
                Hands = Mat("M_Hands_Whitebox.mat", WhiteboxConfig.Hex("#EDE3D2"), false, 0.25f)
            };
            // 白色描边材质：只渲染背面（Front cull），由放大副本形成物体整体轮廓，替代旧的底部蓝色地标环
            if (mats.Highlight.HasProperty("_Cull"))
                mats.Highlight.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
            return mats;
        }

        static Material Mat(string file, Color c, bool unlit, float smoothness)
        {
            string path = MaterialFolder + "/" + file;
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader sh = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find(unlit ? "Unlit/Color" : "Standard");
                if (sh == null) sh = Shader.Find("Diffuse");
                m = new Material(sh) { name = file.Replace(".mat", "") };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static WhiteboxConfig CreateConfig()
        {
            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<WhiteboxConfig>();
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }
            EnsureDogSpotlightConfig(cfg); // 幂等补齐：只写缺失字段，不覆盖用户调过的数值
            return cfg;
        }

        /// <summary>
        /// MECH-017 / ART-014：用 Editor API 给旧配置资产补齐狗加戏字段，绝不手写 .asset YAML。
        /// 只在序列化值为 0（缺字段的典型表现）时写入脚本默认值；用户设为 0 关闭功能时保持 0。
        /// </summary>
        static void EnsureDogSpotlightConfig(WhiteboxConfig cfg)
        {
            var so = new SerializedObject(cfg);
            WriteIfMissing(so, "dogScoutInterval", 18f);
            WriteIfMissing(so, "dogScoutHold", 2.5f);
            WriteIfMissing(so, "dogScoutForward", 2.2f);
            WriteIfMissing(so, "dogScoutSide", 0.9f);
            WriteIfMissing(so, "dogScoutIdleTrigger", 1.2f);
            WriteIfMissing(so, "dogBobMeters", 0.01f, false);
            WriteIfMissing(so, "dogFetchLunge", 0.12f, false);
            WriteIfMissing(so, "dogTailWagHz", 2.2f);
            WriteIfMissing(so, "dogTailWagDeg", 14f, false);
            WriteIfMissing(so, "dogTailBoostDeg", 26f, false);
            WriteIfMissing(so, "dogLegGaitHz", 4.5f);
            WriteIfMissing(so, "dogLegGaitDeg", 12f, false);
            WriteIfMissing(so, "dogHeadLookDeg", 12f, false);
            if (so.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
            }
        }

        static void WriteIfMissing(SerializedObject so, string name, float fallback, bool zeroMeansMissing = true)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) return;
            float v = p.floatValue;
            if (zeroMeansMissing ? v <= 0f : v < 0f) p.floatValue = fallback;
        }

        static TrashItem[] CreateTrashPrefabs(Mats mats, WhiteboxConfig cfg)
        {
            var result = new TrashItem[7];
            for (int i = 0; i < 7; i++)
            {
                bool large = i >= 4;
                string name = large
                    ? "prop_trash_large_" + LargeNames[i - 4] + "_whitebox"
                    : "prop_trash_" + CategoryNames[i] + "_whitebox";

                var root = new GameObject(name);
                Vector3 shape = ShapeScale(i);
                PrimitiveType type = ShapeType(i);
                Primitive(root.transform, "Body", type, shape, Vector3.zero, mats.Trash, true);

                // 整体白色描边（背面渲染的放大副本）：替代旧的底部蓝色地标环（用户要求去掉蓝环、整体白描边）。
                // 不修改垃圾固有色（ART-004 / §9.1 禁忌②），描边只在该垃圾被准星锁定时激活。
                // ThinOutlineScale=1.06667：描边比物体大 6.667%，即原 10% 宽度的三分之二，更纤细。
                GameObject outline = Primitive(root.transform, "Outline", type, shape * ThinOutlineScale, Vector3.zero, mats.Highlight, false);
                var outlineMr = outline.GetComponent<MeshRenderer>();
                if (outlineMr != null) { outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; outlineMr.receiveShadows = false; }
                outline.SetActive(false);

                var item = root.AddComponent<TrashItem>();
                int cat = large ? (i - 4) : i;
                int durability = large ? cfg.LargeDurability[i - 4] : 1;
                item.Configure((TrashCategory)cat, large, durability, cfg.LargeYield, outline);

                GameObject prefab = SavePrefab(root, name);
                result[i] = prefab != null ? prefab.GetComponent<TrashItem>() : null;
                if (result[i] == null) Debug.LogWarning(LogTag + "垃圾预制体缺少 TrashItem：" + name);
            }
            TrashItem apple = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabFolder + "/prop_trash_organic_apple.prefab")?.GetComponent<TrashItem>();
            if (apple != null)
            {
                Array.Resize(ref result, result.Length + 1);
                result[result.Length - 1] = apple;
            }
            return result;
        }

        static PrimitiveType ShapeType(int i)
        {
            if (i == 1 || i == 2 || i == 4) return i == 2 ? PrimitiveType.Sphere : PrimitiveType.Cylinder;
            return PrimitiveType.Cube;
        }

        static Vector3 ShapeScale(int i)
        {
            switch (i)
            {
                case 0: return new Vector3(0.30f, 0.42f, 0.30f); // 塑料瓶
                case 1: return new Vector3(0.26f, 0.18f, 0.26f); // 易拉罐
                case 2: return new Vector3(0.30f, 0.30f, 0.30f); // 果皮
                case 3: return new Vector3(0.22f, 0.34f, 0.22f); // 电池
                case 4: return new Vector3(0.70f, 0.22f, 0.70f); // 轮胎
                case 5: return new Vector3(0.80f, 0.45f, 0.60f); // 金属设备
                default: return new Vector3(0.60f, 0.55f, 0.50f); // 废旧电器
            }
        }

        static GameObject CreateDogPrefab(Mats mats, WhiteboxConfig cfg)
        {
            // ART-011 / ART-012：1 套模型、1 套固定材质、体高 ≤ 0.6；无阶段变体、无换色逻辑。
            var root = new GameObject("chr_dog_whitebox");
            var renderers = new List<Renderer>();
            renderers.Add(Primitive(root.transform, "Body", PrimitiveType.Capsule,
                new Vector3(0.30f, 0.20f, 0.45f), Vector3.zero, mats.Dog, false).GetComponent<MeshRenderer>());
            renderers.Add(Primitive(root.transform, "Head", PrimitiveType.Sphere,
                new Vector3(0.26f, 0.26f, 0.26f), new Vector3(0f, 0.14f, 0.34f), mats.Dog, false).GetComponent<MeshRenderer>());
            Vector3[] legs =
            {
                new Vector3(-0.12f, -0.18f, 0.20f), new Vector3(0.12f, -0.18f, 0.20f),
                new Vector3(-0.12f, -0.18f, -0.20f), new Vector3(0.12f, -0.18f, -0.20f)
            };
            for (int i = 0; i < legs.Length; i++)
                renderers.Add(Primitive(root.transform, "Leg_" + i, PrimitiveType.Cube,
                    new Vector3(0.08f, 0.20f, 0.08f), legs[i], mats.Dog, false).GetComponent<MeshRenderer>());
            renderers.Add(Primitive(root.transform, "Tail", PrimitiveType.Cube,
                new Vector3(0.07f, 0.07f, 0.18f), new Vector3(0f, 0.10f, -0.34f), mats.Dog, false).GetComponent<MeshRenderer>());

            var dog = root.AddComponent<CompanionDog>();
            dog.FixedColorRenderers = renderers.ToArray();
            dog.FixedMaterial = mats.Dog;
            Debug.Log(LogTag + "狗固定色号=" + ColorUtility.ToHtmlStringRGB(cfg.DogColor)
                      + "，体高约 0.47（≤0.6）");
            return SavePrefab(root, "chr_dog_whitebox");
        }

        static GameObject CreateStationPrefab(Mats mats)
        {
            // ART-007：方正剪影、米白旧金属 + 锈红四角柱、漏斗 + 三排气管、唯一暖色光源。
            var root = new GameObject("env_station_whitebox");
            Primitive(root.transform, "Main", PrimitiveType.Cube,
                new Vector3(5f, 1.4f, 4f), Vector3.zero, mats.Station, true);
            Vector3[] corners =
            {
                new Vector3(-2.2f, 0f, -1.7f), new Vector3(2.2f, 0f, -1.7f),
                new Vector3(-2.2f, 0f, 1.7f), new Vector3(2.2f, 0f, 1.7f)
            };
            for (int i = 0; i < corners.Length; i++)
                Primitive(root.transform, "Pillar_" + i, PrimitiveType.Cube,
                    new Vector3(0.16f, 1.6f, 0.16f), corners[i], mats.Rust, false);
            Primitive(root.transform, "Funnel", PrimitiveType.Cylinder,
                new Vector3(0.55f, 0.18f, 0.55f), new Vector3(0f, 0.78f, 0f), mats.Station, false);
            for (int i = 0; i < 3; i++)
                Primitive(root.transform, "Pipe_" + i, PrimitiveType.Cylinder,
                    new Vector3(0.12f, 0.60f, 0.12f), new Vector3(-0.5f + i * 0.5f, 1.1f, -1.1f),
                    mats.Rust, false);

            return SavePrefab(root, "env_station_whitebox");
        }

        static GameObject CreateLandmarkPrefab(Mats mats)
        {
            var root = new GameObject("env_landmark_whitebox");
            Primitive(root.transform, "Marker", PrimitiveType.Cylinder,
                new Vector3(0.8f, 0.6f, 0.8f), Vector3.zero, mats.Landmark, false);
            return SavePrefab(root, "env_landmark_whitebox");
        }

        static GameObject Primitive(Transform parent, string name, PrimitiveType type,
            Vector3 scale, Vector3 localPos, Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                if (name == "HighlightRing") { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
            }
            if (!keepCollider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            return go;
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------------------------------------------------------------
        // 场景
        // ---------------------------------------------------------------

        static void BuildSceneContents(Scene scene, Mats mats, WhiteboxConfig cfg, float radius,
            TrashItem[] trashPrefabs, GameObject dogPrefab, GameObject stationPrefab, GameObject landmarkPrefab)
        {
            // 明亮的全局环境光负责暗部填充；方向光仅保留低模表面的立体层次。
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.56f, 0.52f);
            RenderSettings.ambientIntensity = 1.10f;
            RenderSettings.reflectionIntensity = 0.85f;
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = cfg.FogDistance[0];
            RenderSettings.fogColor = cfg.FogColors[0];

            var world = new GameObject("WorldRoot");
            world.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 星球：SphereCollider 半径 0.5 × 缩放 2R = R，供 PlayerController 自动量测
            GameObject sphere = Primitive(world.transform, "env_planet_surface", PrimitiveType.Sphere,
                Vector3.one * (radius * 2f), Vector3.zero, mats.Ground, true);
            SetLayer(sphere, "Planet");
            Renderer groundRenderer = sphere.GetComponent<Renderer>();

            // 水塘（西经区地标兼污染水域表现）
            GameObject water = Primitive(world.transform, "env_west_water", PrimitiveType.Cylinder,
                new Vector3(3.2f, 0.04f, 3.2f), Vector3.zero, mats.Water, false);
            SphericalSurface.Place(water.transform, Vector3.zero, Vector3.left, radius, 0.02f, Vector3.forward);
            Renderer waterRenderer = water.GetComponent<Renderer>();

            // 中转站（北经区，唯一建筑）
            GameObject station = (GameObject)PrefabUtility.InstantiatePrefab(stationPrefab);
            station.name = "env_station_whitebox";
            station.transform.SetParent(world.transform, true);
            SphericalSurface.Place(station.transform, Vector3.zero, Vector3.forward, radius, 0.7f, -Vector3.forward);

            // LEV-003：四区块地标，靠地标而非小地图定位；全部低于视线水平线（ART-001）
            Vector3[] dirs = { Vector3.right, Vector3.back, Vector3.left, Vector3.forward };
            for (int i = 0; i < dirs.Length; i++)
            {
                GameObject lm = (GameObject)PrefabUtility.InstantiatePrefab(landmarkPrefab);
                lm.name = "env_landmark_" + i;
                lm.transform.SetParent(world.transform, true);
                SphericalSurface.Place(lm.transform, Vector3.zero, dirs[i], radius, 0.6f, -dirs[i]);
            }

            // 主光（ART-005 角度由 EcoStage 插值）
            var sunGo = new GameObject("env_sun");
            sunGo.transform.SetParent(world.transform, false);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.10f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(cfg.LightAngle[0], -30f, 0f);
            RenderSettings.sun = sun;

            // 冷色补光：从主光对侧打，柔化暗部但不抹平梯度（体积感）
            var fillGo = new GameObject("env_fill_light");
            fillGo.transform.SetParent(world.transform, false);
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.75f, 0.82f, 1.0f);
            fill.intensity = 0.30f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(15f, 150f, 0f);

            // 轮廓光：从背后上方打，勾勒物体边缘 → 清晰的纵深与体积（避免扁平）
            var rimGo = new GameObject("env_rim_light");
            rimGo.transform.SetParent(world.transform, false);
            Light rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1.0f, 0.98f, 0.92f);
            rim.intensity = 0.20f;
            rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.Euler(55f, 210f, 0f);

            // 相机
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = cfg.SkyColors[0];
            cam.fieldOfView = 72f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = Mathf.Max(200f, radius * 8f);
            PlayerCameraRig rig = camGo.AddComponent<PlayerCameraRig>();
            SetPrivateFloat(rig, "pitchLimit", 60f);

            // 第一人称玩家（复用工程里已有的球面控制器）
            Vector3 spawnDir = Vector3.up;
            var player = new GameObject("chr_player_whitebox");
            player.transform.position = Vector3.up * (radius + 1.02f);
            player.transform.rotation = Quaternion.LookRotation(Vector3.forward, spawnDir);
            SetLayer(player, "Player");
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.35f; cc.center = Vector3.zero;
            cc.slopeLimit = 55f; cc.stepOffset = 0.35f; cc.skinWidth = 0.03f; cc.minMoveDistance = 0f;
            Primitive(player.transform, "Body", PrimitiveType.Capsule,
                new Vector3(0.7f, 0.85f, 0.7f), Vector3.zero, mats.Player, false);
            Primitive(player.transform, "Facing", PrimitiveType.Cube,
                new Vector3(0.14f, 0.14f, 0.22f), new Vector3(0f, 0.42f, 0.34f), mats.Player, false);
            PlayerController pc = player.AddComponent<PlayerController>();
            pc.PlanetCenter = world.transform;
            pc.CameraRig = rig;
            pc.PlanetRadius = radius;
            rig.Target = player.transform;
            rig.Player = pc;
            rig.PlanetCenter = world.transform;

            // 双手（第一人称白膜）
            var handsRoot = new GameObject("Hands");
            handsRoot.transform.SetParent(camGo.transform, false);
            Transform left = CreateHand(handsRoot.transform, "HandLeft", -0.62f, mats.Hands);
            Transform right = CreateHand(handsRoot.transform, "HandRight", 0.62f, mats.Hands);
            FirstPersonHands hands = handsRoot.AddComponent<FirstPersonHands>();
            hands.LeftHand = left; hands.RightHand = right; hands.Player = pc; hands.CameraRig = rig;

            // 伙伴狗（固定外观，出生在玩家身后）
            GameObject dogGo = (GameObject)PrefabUtility.InstantiatePrefab(dogPrefab);
            dogGo.name = "chr_dog_whitebox";
            dogGo.transform.SetParent(world.transform, true);
            Vector3 surface = SphericalSurface.Point(Vector3.zero, Vector3.up, radius, 0f);
            Vector3 dogAt = SphericalSurface.MoveOnSurface(Vector3.zero, surface, -Vector3.forward,
                cfg.DogFollowDistance);
            SphericalSurface.Place(dogGo.transform, Vector3.zero,
                SphericalSurface.Up(dogAt, Vector3.zero), radius, cfg.DogHeight * 0.5f, Vector3.forward);
            CompanionDog dog = dogGo.GetComponent<CompanionDog>();

            // 系统根节点：所有系统挂同一物体，由 WhiteboxGame 固定顺序初始化
            var sys = new GameObject("sys_whitebox");
            var trashRoot = new GameObject("env_trash_root");
            trashRoot.transform.SetParent(world.transform, false);

            TrashSpawner spawner = sys.AddComponent<TrashSpawner>();
            spawner.Prefabs = trashPrefabs;
            spawner.TrashRoot = trashRoot.transform;
            InteractionController interaction = sys.AddComponent<InteractionController>();
            StationSystem stationSystem = sys.AddComponent<StationSystem>();
            QuestChain quest = sys.AddComponent<QuestChain>();
            EcoStage eco = sys.AddComponent<EcoStage>();
            WhiteboxHud hud = sys.AddComponent<WhiteboxHud>();
            WhiteboxGame game = sys.AddComponent<WhiteboxGame>();
            game.Wire(cfg, world.transform, player.transform, cam,
                groundRenderer, waterRenderer, sun, station.transform,
                spawner, interaction, dog, stationSystem, quest, eco, hud);

            Debug.Log(LogTag + "场景物体已装配：planet / water / station / 4 landmarks / player / camera / hands / dog / systems");
        }

        static Transform CreateHand(Transform parent, string name, float x, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            // 注意：相机子物体的本地 +Z 才是前方（transform.forward = 本地 +Z），实测确认过：写成 -Z 会整只手落到相机背后被剔除。
            // 且必须在 FOV 72（竖直半角 36°）之内，否则整只手被视锥裁掉——旧挂点 y=-0.58 就栽在这。
            // 几何与 WhiteboxHandsFix 共用同一组常量：腕点 (±0.341,-0.36,-0.22) → 手部中心 (±0.242,-0.34,-0.61)。
            float s = x < 0f ? -1f : 1f;
            root.transform.localPosition = new Vector3(s * WhiteboxHandsFix.WristX,
                WhiteboxHandsFix.WristY, WhiteboxHandsFix.WristZ);

            // 只要工具时不生成手部图元（开关见 Tools > 星球白膜 > ⑤），挂点保留
            if (!WhiteboxHandsFix.ShowHandMesh) return root.transform;

            Primitive(root.transform, "Forearm", PrimitiveType.Capsule,
                new Vector3(0.11f, 0.34f, 0.11f),
                new Vector3(s * (WhiteboxHandsFix.HandX - WhiteboxHandsFix.WristX) * 0.5f, -0.06f, -0.19f), mat, false);
            Primitive(root.transform, "Palm", PrimitiveType.Cube,
                new Vector3(0.13f, 0.10f, 0.17f),
                new Vector3(s * (WhiteboxHandsFix.HandX - WhiteboxHandsFix.WristX),
                    WhiteboxHandsFix.HandY - WhiteboxHandsFix.WristY,
                    WhiteboxHandsFix.HandZ - WhiteboxHandsFix.WristZ), mat, false);
            return root.transform;
        }

        /// <summary>
        /// 把相机下的第一人称双手重建到相机前方（本地 +Z）。会从已有 Hands 上读取 player / cameraRig
        /// 引用并沿用其材质，避免重复创建材质资源。幂等于“已经在 +Z”的场景（调用方自行判断）。
        /// 用于一次性自动修复：旧版双手被放在 +Z（相机背后）导致运行后看不见。
        /// </summary>
        public static void RebuildHands(Camera cam)
        {
            if (cam == null) return;

            Transform oldHands = cam.transform.Find("Hands");
            FirstPersonHands old = oldHands != null ? oldHands.GetComponent<FirstPersonHands>() : null;
            PlayerController player = old != null ? old.Player : null;
            PlayerCameraRig rig = old != null ? old.CameraRig : null;

            Material handMat = null;
            if (oldHands != null)
            {
                var mr = oldHands.GetComponentInChildren<MeshRenderer>();
                if (mr != null) handMat = mr.sharedMaterial;
            }
            if (handMat == null)
                handMat = Mat("M_Hands_Whitebox.mat", WhiteboxConfig.Hex("#EDE3D2"), false, 0.25f);

            if (oldHands != null) UnityEngine.Object.DestroyImmediate(oldHands.gameObject);

            var root = new GameObject("Hands");
            root.transform.SetParent(cam.transform, false);
            Transform left = CreateHand(root.transform, "HandLeft", -0.62f, handMat);
            Transform right = CreateHand(root.transform, "HandRight", 0.62f, handMat);
            FirstPersonHands hands = root.AddComponent<FirstPersonHands>();
            hands.LeftHand = left;
            hands.RightHand = right;
            hands.Player = player;
            hands.CameraRig = rig;
        }

        static void SetPrivateFloat(UnityEngine.Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) return;
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------
        // 目录 / 设置
        // ---------------------------------------------------------------

        static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/Scripts", "Assets/Scripts/Core", "Assets/Scripts/Data",
                "Assets/Scripts/Gameplay", "Assets/Scripts/Station", "Assets/Scripts/Environment",
                "Assets/Scripts/UI", "Assets/Scripts/Boot", "Assets/Scripts/Editor",
                "Assets/Data", "Assets/Art", "Assets/Art/Materials", "Assets/Art/Models",
                "Assets/Art/UI", "Assets/Art/FX", "Assets/Art/Audio",
                "Assets/Audio", "Assets/Prefabs", PrefabFolder, "Assets/Scenes", "Assets/Settings"
            };
            for (int i = 0; i < folders.Length; i++) EnsureFolder(folders[i]);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash < 0) return;
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static void EnsureLayers()
        {
            string[] names = { "Planet", "Trash", "Player" };
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null) return;
            for (int n = 0; n < names.Length; n++)
            {
                bool found = false;
                for (int i = 0; i < layers.arraySize; i++)
                    if (layers.GetArrayElementAtIndex(i).stringValue == names[n]) { found = true; break; }
                if (found) continue;
                for (int i = 8; i < layers.arraySize; i++)
                {
                    SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                    slot.stringValue = names[n];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    break;
                }
            }
        }

        static void SetLayer(GameObject go, string layer)
        {
            int id = LayerMask.NameToLayer(layer);
            if (id >= 0) go.layer = id;
        }

        static void AddSceneToBuildSettings(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path != path) continue;
                scenes[i].enabled = true;
                EditorBuildSettings.scenes = scenes;
                return;
            }
            var list = new List<EditorBuildSettingsScene>(scenes);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("Tools/垃圾星球白膜/⑥ 刷新垃圾描边（去蓝环→白描边）", false, 12)]
        public static void RefreshTrashOutlinesMenu()
        {
            bool ok = RefreshTrashOutlines();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("垃圾描边刷新",
                    ok ? "已把 7 个垃圾预制体的描边改为「复制替换后的美术模型 + 沿表面法线外扩」；\n"
                         + "描边位置、厚度、走向现在与材质表现一致，不再跟随白膜轮廓。\n\n进入 Play 后准星对准垃圾即可查看。"
                        : "刷新失败，请看 Console 里 [TrashOutlineBuilder] 的错误日志。", "好");
        }

        /// <summary>
        /// 只重建 7 个垃圾预制体的描边表现，不重建场景、不动 Hierarchy 里的 UI 面板。
        /// 委托 TrashOutlineBuilder：从当前 Body（可能是已替换的美术模型）复制并沿法线外扩生成描边。
        /// 同时把配置描边色同步为白色。
        /// </summary>
        public static bool RefreshTrashOutlines()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(LogTag + "请先退出 Play 再刷新描边");
                return false;
            }
            try
            {
                WhiteboxConfig cfg = CreateConfig();
                var so = new SerializedObject(cfg);
                var hp = so.FindProperty("highlightHex");
                if (hp != null && hp.stringValue != "#FFFFFF") { hp.stringValue = "#FFFFFF"; so.ApplyModifiedProperties(); }
                EditorUtility.SetDirty(cfg);

                return TrashOutlineBuilder.RebuildAll();
            }
            catch (Exception e)
            {
                Debug.LogError(LogTag + "刷新描边失败：" + e);
                return false;
            }
        }

        /// <summary>是否存在仍带旧蓝环（HighlightRing）或缺白描边（Outline）的遗留预制体。</summary>
        public static bool HasLegacyRingPrefabs()
        {
            for (int i = 0; i < 7; i++)
            {
                string path = PrefabFolder + "/" + TrashPrefabName(i) + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                bool legacy = contents.transform.Find("HighlightRing") != null
                              || contents.transform.Find("Outline") == null;
                PrefabUtility.UnloadPrefabContents(contents);
                if (legacy) return true;
            }
            return false;
        }

        /// <summary>当前 7 个垃圾预制体的描边是否已贴合替换后的美术模型（否则需重刷）。</summary>
        public static bool OutlineNeedsRefresh()
        {
            return TrashOutlineBuilder.NeedsRebuild();
        }

        [MenuItem("Tools/垃圾星球白膜/⑦ 增强立体感光照（当前场景）", false, 13)]
        public static void ApplyVolumeLightingMenu()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogWarning(LogTag + "请在已打开的 WhiteboxPrototype 场景里执行；当前场景=" + (string.IsNullOrEmpty(scene.path) ? "(未保存)" : scene.path));
                return;
            }
            ApplyVolumeLighting(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("立体感光照",
                    "已对当前场景执行：环境光压低至 0.30 + 增加冷色补光与轮廓光。\n"
                    + "场景已保存；进入 Play 或直接看 Scene 视图都能看到更明显的明暗梯度与体积感。", "好");
        }

        /// <summary>
        /// 把立体感光照写入指定场景：压低环境光、补一盏冷色对侧补光、一盏背后轮廓光。
        /// 幂等：同名校灯已存在则跳过；不重建场景、不动 UI 面板与双手。
        /// </summary>
        public static void ApplyVolumeLighting(Scene scene)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.56f, 0.52f);
            RenderSettings.ambientIntensity = 1.10f;
            RenderSettings.reflectionIntensity = 0.85f;

            Transform worldRoot = null;
            foreach (GameObject r in scene.GetRootGameObjects())
                if (r.name == "WorldRoot") { worldRoot = r.transform; break; }
            Transform parent = worldRoot != null ? worldRoot : null;

            EnsureDirectionalLight(parent, "env_fill_light",
                new Color(0.75f, 0.82f, 1.0f), 0.55f, Quaternion.Euler(15f, 150f, 0f));
            EnsureDirectionalLight(parent, "env_rim_light",
                new Color(1.0f, 0.98f, 0.92f), 0.40f, Quaternion.Euler(55f, 210f, 0f));
        }

        /// <summary>
        /// 把配置里的星球半径烘焙进当前场景（星球体积 + 水体/中转站/地标的位置）。
        /// 运行期 WhiteboxGame 也会做同样的对齐，所以不跑这个菜单也能玩；
        /// 只是跑了之后 Scene 视图里看到的才是真实尺寸。
        /// </summary>
        [MenuItem("Tools/垃圾星球白膜/⑧ 按配置重设星球半径（当前场景）", false, 15)]
        public static void ApplyPlanetRadiusMenu()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogWarning(LogTag + "请在已打开的 WhiteboxPrototype 场景里执行；当前场景="
                                 + (string.IsNullOrEmpty(scene.path) ? "(未保存)" : scene.path));
                return;
            }
            WhiteboxConfig cfg = CreateConfig();
            bool changed = ApplyPlanetRadiusToScene(scene, cfg);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("星球半径",
                    changed
                        ? "已按配置把星球半径改为 " + cfg.PlanetRadius + " m，并同步挪动水体 / 中转站 / 地标。"
                        : "场景半径已与配置一致（" + cfg.PlanetRadius + " m），无需改动。", "好");
        }

        /// <summary>返回 true 表示场景确实被改过。</summary>
        public static bool ApplyPlanetRadiusToScene(Scene scene, WhiteboxConfig cfg)
        {
            if (cfg == null || !scene.IsValid()) return false;

            Transform world = null;
            PlayerController pc = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (world == null && root.name == "WorldRoot") world = root.transform;
                if (pc == null) pc = root.GetComponentInChildren<PlayerController>(true);
            }
            if (world == null) return false;

            bool changed = PlanetLayout.Apply(world, cfg.PlanetRadius);
            // 玩家控制器只是兜底值（运行时会按碰撞体重新量测），同步一下避免 Scene 视图里数值过期
            if (pc != null) pc.PlanetRadius = cfg.PlanetRadius;
            return changed;
        }

        static void EnsureDirectionalLight(Transform parent, string name, Color color, float intensity, Quaternion rot)
        {
            if (parent != null)
            {
                Transform existing = parent.Find(name);
                if (existing != null) return;
            }
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.transform.rotation = rot;
        }

        /// <summary>当前场景是否已写入立体感补光 / 轮廓光（幂等判断用）。</summary>
        public static bool LightingApplied(Scene scene)
        {
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.name != "WorldRoot") continue;
                if (r.transform.Find("env_fill_light") != null && r.transform.Find("env_rim_light") != null)
                    return true;
            }
            return false;
        }

        static string TrashPrefabName(int i)
        {
            bool large = i >= 4;
            return large
                ? "prop_trash_large_" + LargeNames[i - 4] + "_whitebox"
                : "prop_trash_" + CategoryNames[i] + "_whitebox";
        }
    }
}
