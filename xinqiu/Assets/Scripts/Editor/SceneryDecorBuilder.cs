// @trace ART-013, ART-002, ART-003, MECH-002
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 场景装饰生成 + 生态联动接线（菜单⑯）。
    ///
    /// 生成四类纯装饰（无 Collider / 无 TrashItem / 无玩法引用），贴到球面并避开已有设施：
    ///   - 枯树 ×5    env_decor_tree_N
    ///   - 广告牌 ×3  env_decor_board_N（阶段0倒地 → 阶段3立起）
    ///   - 污染水塘 ×3 env_decor_pool_N（与已有 env_west_water 错开；扁圆盘随半径缩放）
    ///   - 垃圾山 ×3  env_decor_mound_N（脏块堆 → 复绿丘）
    ///
    /// 每处挂 SceneryDecor 记录"脏→净"部件引用；sys_whitebox 上挂 SceneryDecorSystem
    /// 统一读 game.Eco.Stage 驱动视觉；WireScenery 注入 WhiteboxGame。
    /// 全部走 Editor API，不写场景 YAML；可重复执行（先删旧装饰再建）。
    /// </summary>
    public static class SceneryDecorBuilder
    {
        const string SysRoot = "sys_whitebox";
        const string WorldRoot = "WorldRoot";
        const string Tag = "[SceneryDecorBuilder] ";
        const string MaterialFolder = "Assets/Art/Materials";
        // 树源 FBX 内含 a/b/c 三棵；正式场景只使用 a，并保存为单独预制体。
        const string ArtTreeSource = "Assets/Art/tree (2)/tree/tree.fbx";
        const string ArtTreeChild = "fir_sapling_medium_a_LOD0";
        const string ArtTreePrefab = "Assets/Art/Generated/PF_DecorTree_Single.prefab";
        const string ArtTrashMountain = "Assets/Art/lajishan/lajishan.fbx";

        // 生成方向：避开 中转站(forward)/水塘(left)/四象限地标(right,back,left,forward)/玩家(up)/狗(up 邻域)。
        // 全部归一化后交给 SphericalSurface.Place，半径由配置决定，不写死。
        static readonly Vector3[] TreeDirs =
        {
            new Vector3( 0.85f, 0.30f, -0.55f),
            new Vector3(-0.70f, 0.45f, -0.60f),
            new Vector3( 0.60f, 0.20f,  0.80f),
            new Vector3(-0.80f, 0.15f,  0.65f),
            new Vector3( 0.10f, 0.72f, -0.75f)
        };
        static readonly Vector3[] BoardDirs =
        {
            new Vector3( 0.95f,-0.10f,  0.35f),
            new Vector3(-0.55f,-0.20f, -0.90f),
            new Vector3( 0.35f, 0.55f,  0.80f)
        };
        static readonly Vector3[] PoolDirs =
        {
            new Vector3( 0.90f, 0.05f, -0.75f),
            new Vector3(-0.85f, 0.10f,  0.45f),
            new Vector3(-0.25f,-0.30f, -0.95f)
        };
        static readonly Vector3[] MoundDirs =
        {
            new Vector3(-0.95f, 0.20f, -0.35f),
            new Vector3( 0.45f,-0.25f,  0.95f),
            new Vector3(-0.45f, 0.60f,  0.75f)
        };

        // 视觉尺寸：水塘与垃圾山整体放大，但仍是纯装饰；球面位置与半径依旧由 PlanetLayout 统一决定。
        const float PoolVisualScale = 1.6f;
        const float MoundVisualScale = 1.6f;

        // 美术归一化目标（root 本地单位，root 自己再乘 MoundVisualScale）：高度上限 / 水平最大边。
        // 垃圾山取「贴着绿丘」的一坨：宽度跟绿丘（1.7）相当，高度略高于绿丘（0.85）留出堆尖。
        const float TreeArtWidth = 3.5f;
        const float TreeArtHeight = 2.5f;
        const float MoundArtWidth = 1.9f;
        const float MoundArtHeight = 1.35f;

        static readonly Color TreeBarkDirty = new Color(0.28f, 0.24f, 0.19f);
        static readonly Color TreeBarkClean = new Color(0.42f, 0.38f, 0.26f);
        static readonly Color BoardDirty = new Color(0.46f, 0.42f, 0.33f);
        static readonly Color BoardClean = new Color(0.85f, 0.90f, 0.93f);
        static readonly Color JunkDirty = new Color(0.38f, 0.33f, 0.26f);
        static readonly Color JunkClean = new Color(0.36f, 0.42f, 0.22f);
        static readonly Color PoolDirty = new Color(0.22f, 0.29f, 0.30f);
        static readonly Color PoolClean = new Color(0.31f, 0.70f, 0.77f);
        static readonly Color LeafColor = new Color(0.37f, 0.64f, 0.20f);
        static readonly Color GrassColor = new Color(0.45f, 0.68f, 0.26f);

        [MenuItem("Tools/垃圾星球白膜/⑯ 生成场景装饰与生态联动", false, 21)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请打开 WhiteboxPrototype.unity 再执行本菜单");
                return;
            }
            BuildInScene(scene);
        }

        /// <summary>
        /// 只给「场景里已有的装饰」补挂美术模型，不删除、不重建、不动位置。
        /// 给已经在白膜场景里调过位置的情况用：⑯ 会整批重建装饰，本菜单只写模型引用。
        /// 枯树 → tree.fbx，垃圾山 → lajishan.fbx；美术在 Play 时由 SceneryDecor.Init 实例化。
        /// </summary>
        [MenuItem("Tools/垃圾星球白膜/⑯b 装饰补挂美术（垃圾山→lajishan，不重建）", false, 22)]
        public static void AttachArt()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请打开 WhiteboxPrototype.unity 再执行本菜单");
                return;
            }

            GameObject tree = EnsureSingleTreePrefab();
            GameObject mountain = AssetDatabase.LoadAssetAtPath<GameObject>(ArtTrashMountain);
            if (tree == null) Debug.LogWarning(Tag + "无法生成单棵树预制体 " + ArtTreePrefab);
            if (mountain == null) Debug.LogError(Tag + "找不到 " + ArtTrashMountain + "，垃圾山无法换美术");

            int trees = 0, mounds = 0, skipped = 0;
            SceneryDecor[] decors = Object.FindObjectsByType<SceneryDecor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SceneryDecor deco in decors)
            {
                GameObject model = null;
                float width = 0f, height = 0f;
                if (deco.Kind == SceneryDecorKind.DeadTree && tree != null)
                {
                    model = tree; width = TreeArtWidth; height = TreeArtHeight;
                }
                else if (deco.Kind == SceneryDecorKind.TrashMountain && mountain != null)
                {
                    model = mountain; width = MoundArtWidth; height = MoundArtHeight;
                }
                else
                {
                    skipped++;
                    continue;
                }

                var so = new SerializedObject(deco);
                so.FindProperty("artModel").objectReferenceValue = model;
                so.FindProperty("artFitWidth").floatValue = width;
                so.FindProperty("artFitHeight").floatValue = height;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(deco);

                if (deco.Kind == SceneryDecorKind.DeadTree) trees++;
                else mounds++;

                Debug.Log(Tag + deco.name + " ← " + AssetDatabase.GetAssetPath(model));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "补挂美术完成：枯树 " + trees + " 处，垃圾山 " + mounds + " 处，跳过 "
                      + skipped + " 处（广告牌 / 水塘暂不用美术模型），场景已保存。按 Play 看效果。");
        }

        /// <summary>从三棵一组的 FBX 中抽出 a 树，生成独立预制体并替换场景中全部枯树。</summary>
        [MenuItem("Tools/垃圾星球白膜/⑯c 用单棵美术树替换全部树", false, 23)]
        public static void ReplaceTreesWithSingleArt()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请打开 WhiteboxPrototype.unity 再执行本菜单");
                return;
            }

            GameObject tree = EnsureSingleTreePrefab();
            if (tree == null) return;

            int replaced = 0;
            SceneryDecor[] decors = Object.FindObjectsByType<SceneryDecor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SceneryDecor deco in decors)
            {
                if (deco == null || deco.Kind != SceneryDecorKind.DeadTree) continue;
                deco.SetArtModel(tree, TreeArtWidth, TreeArtHeight);
                InstallEditorArt(deco, tree);
                EditorUtility.SetDirty(deco);
                replaced++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "单棵树替换完成：预制体=" + ArtTreePrefab + "，场景树=" + replaced
                      + "；编辑态已预摆，Play 时由 SceneryDecor 接管，不会重复实例化。");
        }

        /// <summary>批处理入口：打开白膜场景生成并保存，供命令行验证。</summary>
        public static void BuildBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            BuildInScene(scene);
        }

        static void BuildInScene(Scene scene)
        {
            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>(WhiteboxBuilder.ConfigPath);
            float radius = cfg != null ? cfg.PlanetRadius : 8f;
            Transform world = GameObject.Find(WorldRoot)?.transform;
            if (world == null)
            {
                Debug.LogError(Tag + "找不到 WorldRoot，请先执行 ① 生成场景与预制体");
                return;
            }

            // 幂等：先删掉上一次生成的装饰
            RemoveOld(world);

            var all = new List<SceneryDecor>();
            Material bark = Mat("M_Decor_Bark.mat", TreeBarkDirty);
            Material board = Mat("M_Decor_Board.mat", BoardDirty);
            Material junk = Mat("M_Decor_Junk.mat", JunkDirty);
            Material water = Mat("M_Decor_Pool.mat", PoolDirty);
            Material leaf = Mat("M_Decor_Leaf.mat", LeafColor);
            Material grass = Mat("M_Decor_Grass.mat", GrassColor);

            for (int i = 0; i < TreeDirs.Length; i++)
                all.Add(BuildTree(world, "env_decor_tree_" + (i + 1), TreeDirs[i], radius, bark, leaf));
            for (int i = 0; i < BoardDirs.Length; i++)
                all.Add(BuildBoard(world, "env_decor_board_" + (i + 1), BoardDirs[i], radius, bark, board, junk));
            for (int i = 0; i < PoolDirs.Length; i++)
                all.Add(BuildPool(world, "env_decor_pool_" + (i + 1), PoolDirs[i], cfg, water, junk, grass));
            for (int i = 0; i < MoundDirs.Length; i++)
                all.Add(BuildMound(world, "env_decor_mound_" + (i + 1), MoundDirs[i], radius, junk, leaf, grass));

            // 挂联动系统并 Wire 到 WhiteboxGame
            GameObject sys = GameObject.Find(SysRoot);
            if (sys == null)
            {
                Debug.LogError(Tag + "找不到 " + SysRoot + "，请先执行 ①");
                return;
            }
            SceneryDecorSystem system = sys.GetComponent<SceneryDecorSystem>();
            if (system == null) system = sys.AddComponent<SceneryDecorSystem>();
            system.Wire(all.ToArray());

            WhiteboxGame game = sys.GetComponent<WhiteboxGame>();
            if (game != null) game.WireScenery(system);
            else Debug.LogWarning(Tag + "sys_whitebox 上没有 WhiteboxGame，Wire 跳过");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完成：装饰 " + all.Count + " 处（树5/牌3/塘3/山3），系统已挂 " + SysRoot
                      + "，Wire=" + (game != null) + "，场景已保存");
        }

        static void RemoveOld(Transform world)
        {
            var old = new List<GameObject>();
            for (int i = 0; i < world.childCount; i++)
            {
                Transform c = world.GetChild(i);
                if (c.name.StartsWith("env_decor_", System.StringComparison.Ordinal)) old.Add(c.gameObject);
            }
            for (int i = 0; i < old.Count; i++) Object.DestroyImmediate(old[i]);
        }

        // ---------------------------------------------------------------
        // 四类装饰（全部无碰撞体、无 TrashItem）
        // ---------------------------------------------------------------
        static SceneryDecor BuildTree(Transform world, string name, Vector3 dir, float radius,
            Material bark, Material leaf)
        {
            GameObject root = NewRoot(world, name, dir, radius, 0f);
            // 树干（倾斜的枯木）
            GameObject trunk = Prim(root.transform, "Trunk", PrimitiveType.Cylinder,
                new Vector3(0.13f, 1.1f, 0.13f), new Vector3(0f, 1.1f, 0f), bark);
            trunk.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
            // 三根枯枝
            var branches = new List<Transform>();
            for (int b = 0; b < 3; b++)
            {
                GameObject br = Prim(root.transform, "Branch_" + b, PrimitiveType.Cylinder,
                    new Vector3(0.05f, 0.62f, 0.05f), new Vector3(0f, 1.85f, 0f), bark);
                br.transform.localRotation = Quaternion.Euler(38f - b * 34f, b * 120f, 22f);
                br.transform.localPosition += br.transform.localRotation * new Vector3(0f, 0.5f, 0f);
                branches.Add(br.transform);
            }
            // 树冠：几团低模绿叶，恢复时长出
            var crowns = new List<Transform>();
            Vector3[] cp = { new Vector3(0,2.4f,0), new Vector3(0.42f,2.1f,0.18f),
                             new Vector3(-0.38f,2.2f,-0.2f), new Vector3(0.1f,2.7f,-0.3f) };
            for (int c = 0; c < cp.Length; c++)
            {
                // 满级缩放写在场景里，由 SceneryDecor.Init 缓存；系统按 visualT 压到 0 或放回
                GameObject cl = Prim(root.transform, "Crown_" + c, PrimitiveType.Sphere,
                    new Vector3(0.7f, 0.55f, 0.7f), cp[c], leaf);
                crowns.Add(cl.transform);
            }

            // 树干 + 枝干参与变色
            var tintList = new List<Renderer> { trunk.GetComponent<Renderer>() };
            foreach (var br in branches) tintList.Add(br.GetComponent<Renderer>());
            var deco = root.AddComponent<SceneryDecor>();
            deco.Setup(SceneryDecorKind.DeadTree,
                tintList.ToArray(), Fill(tintList.Count, TreeBarkDirty), Fill(tintList.Count, TreeBarkClean),
                new Transform[0], crowns.ToArray(), new Transform[0], new Quaternion[0], new Quaternion[0]);
            deco.SetArtModel(EnsureSingleTreePrefab(), TreeArtWidth, TreeArtHeight);
            return deco;
        }

        static SceneryDecor BuildBoard(Transform world, string name, Vector3 dir, float radius,
            Material bark, Material board, Material junk)
        {
            GameObject root = NewRoot(world, name, dir, radius, 0f);
            Prim(root.transform, "Post_L", PrimitiveType.Cylinder,
                new Vector3(0.07f, 0.85f, 0.07f), new Vector3(-0.45f, 0.85f, 0f), bark);
            Prim(root.transform, "Post_R", PrimitiveType.Cylinder,
                new Vector3(0.07f, 0.85f, 0.07f), new Vector3(0.45f, 0.85f, 0f), bark);
            // 牌面枢轴：阶段0倒地(-72°)，阶段3立起(0°)
            GameObject pivot = new GameObject("BoardPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            Quaternion from = Quaternion.Euler(-72f, 12f, 0f);
            Quaternion to = Quaternion.Euler(0f, 0f, 0f);
            pivot.transform.localRotation = from;
            GameObject panel = Prim(pivot.transform, "Panel", PrimitiveType.Cube,
                new Vector3(1.5f, 0.9f, 0.08f), new Vector3(0f, 0.75f, 0f), board);
            // 残片：恢复时消失
            var debris = new List<Transform>();
            for (int d = 0; d < 3; d++)
            {
                GameObject db = Prim(root.transform, "Debris_" + d, PrimitiveType.Cube,
                    new Vector3(0.3f, 0.16f, 0.25f),
                    new Vector3(-0.7f + d * 0.62f, 0.12f, 0.35f), junk);
                debris.Add(db.transform);
            }
            var deco = root.AddComponent<SceneryDecor>();
            deco.Setup(SceneryDecorKind.FallenBillboard,
                new Renderer[] { panel.GetComponent<Renderer>() },
                new[] { BoardDirty }, new[] { BoardClean },
                debris.ToArray(), new Transform[0],
                new[] { pivot.transform }, new[] { from }, new[] { to });
            return deco;
        }

        static SceneryDecor BuildPool(Transform world, string name, Vector3 dir, WhiteboxConfig cfg,
            Material water, Material junk, Material grass)
        {
            GameObject root = NewRoot(world, name, dir, radiusOf(cfg), 0.02f);
            root.transform.localScale = Vector3.one * PoolVisualScale;
            // 扁圆盘水面
            GameObject disc = Prim(root.transform, "Surface", PrimitiveType.Cylinder,
                new Vector3(1.5f, 0.03f, 1.5f), Vector3.zero, water);
            // 漂浮碎屑：恢复时消失
            var debris = new List<Transform>();
            for (int d = 0; d < 4; d++)
            {
                float a = d * 1.57f;
                GameObject db = Prim(root.transform, "Float_" + d, PrimitiveType.Cube,
                    new Vector3(0.18f, 0.06f, 0.14f),
                    new Vector3(Mathf.Cos(a) * 0.8f, 0.06f, Mathf.Sin(a) * 0.8f), junk);
                debris.Add(db.transform);
            }
            // 芦苇：恢复时长出
            var reeds = new List<Transform>();
            for (int r = 0; r < 4; r++)
            {
                float a = r * 1.57f + 0.6f;
                GameObject rd = Prim(root.transform, "Reed_" + r, PrimitiveType.Cylinder,
                    new Vector3(0.03f, 0.4f, 0.03f),
                    new Vector3(Mathf.Cos(a) * 1.35f, 0.3f, Mathf.Sin(a) * 1.35f), grass);
                rd.transform.localScale = new Vector3(0.03f, 0.4f, 0.03f);
                reeds.Add(rd.transform);
            }
            var deco = root.AddComponent<SceneryDecor>();
            deco.Setup(SceneryDecorKind.PollutedPool,
                new Renderer[] { disc.GetComponent<Renderer>() },
                new[] { PoolDirty }, new[] { PoolClean },
                debris.ToArray(), reeds.ToArray(), new Transform[0], new Quaternion[0], new Quaternion[0]);
            return deco;
        }

        static SceneryDecor BuildMound(Transform world, string name, Vector3 dir, float radius,
            Material junk, Material leaf, Material grass)
        {
            GameObject root = NewRoot(world, name, dir, radius, 0f);
            root.transform.localScale = Vector3.one * MoundVisualScale;
            // 歪扭垃圾块堆：不同大小/角度的方块
            var junkBlocks = new List<Transform>();
            var junkRenderers = new List<Renderer>();
            Vector3[] pos = { new Vector3(0,0.35f,0), new Vector3(0.55f,0.28f,0.2f),
                              new Vector3(-0.5f,0.25f,-0.15f), new Vector3(0.1f,0.7f,-0.3f),
                              new Vector3(-0.2f,0.62f,0.4f) };
            Vector3[] sca = { new Vector3(1.1f,0.7f,0.9f), new Vector3(0.7f,0.55f,0.8f),
                              new Vector3(0.8f,0.5f,0.7f), new Vector3(0.65f,0.6f,0.6f),
                              new Vector3(0.6f,0.5f,0.7f) };
            for (int j = 0; j < pos.Length; j++)
            {
                GameObject blk = Prim(root.transform, "Junk_" + j, PrimitiveType.Cube, sca[j], pos[j], junk);
                blk.transform.localRotation = Quaternion.Euler(j * 13f, j * 37f, j * 9f);
                junkBlocks.Add(blk.transform);
                junkRenderers.Add(blk.GetComponent<Renderer>());
            }
            // 复绿丘：恢复时长大（盖住垃圾堆）
            GameObject mound = Prim(root.transform, "GreenMound", PrimitiveType.Sphere,
                new Vector3(1.7f, 0.85f, 1.5f), new Vector3(0f, 0.35f, 0f), leaf);
            mound.transform.localScale = new Vector3(1.7f, 0.85f, 1.5f);
            var grows = new List<Transform> { mound.transform };
            for (int g = 0; g < 3; g++)
            {
                float a = g * 2.1f;
                GameObject tuft = Prim(root.transform, "Grass_" + g, PrimitiveType.Capsule,
                    new Vector3(0.12f, 0.3f, 0.12f),
                    new Vector3(Mathf.Cos(a) * 0.9f, 0.45f, Mathf.Sin(a) * 0.9f), grass);
                tuft.transform.localScale = new Vector3(0.12f, 0.3f, 0.12f);
                grows.Add(tuft.transform);
            }
            var deco = root.AddComponent<SceneryDecor>();
            deco.Setup(SceneryDecorKind.TrashMountain,
                junkRenderers.ToArray(), Fill(junkRenderers.Count, JunkDirty), Fill(junkRenderers.Count, JunkClean),
                junkBlocks.ToArray(), grows.ToArray(), new Transform[0], new Quaternion[0], new Quaternion[0]);
            // 垃圾山美术：lajishan.fbx 当「垃圾堆」用，替换上面的白膜方块堆（缺失则继续用方块堆）
            deco.SetArtModel(AssetDatabase.LoadAssetAtPath<GameObject>(ArtTrashMountain), MoundArtWidth, MoundArtHeight);
            return deco;
        }

        static float radiusOf(WhiteboxConfig cfg) { return cfg != null ? cfg.PlanetRadius : 8f; }

        static GameObject NewRoot(Transform world, string name, Vector3 dir, float radius, float height)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(world, false);
            SphericalSurface.Place(root.transform, Vector3.zero, dir, radius, height, Vector3.up);
            return root;
        }

        /// <summary>创建无碰撞体的基础几何体（低模白膜风格）。</summary>
        static GameObject Prim(Transform parent, string name, PrimitiveType type,
            Vector3 scale, Vector3 localPos, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = localPos;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c); // 纯装饰：不参与碰撞/拾取
            return go;
        }

        static Material Mat(string file, Color c)
        {
            string path = MaterialFolder + "/" + file;
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                if (sh == null) sh = Shader.Find("Diffuse");
                m = new Material(sh) { name = file.Replace(".mat", "") };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Color[] Fill(int n, Color c)
        {
            var a = new Color[n];
            for (int i = 0; i < n; i++) a[i] = c;
            return a;
        }

        static GameObject EnsureSingleTreePrefab()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ArtTreeSource);
            if (source == null)
            {
                Debug.LogError(Tag + "树源模型不存在：" + ArtTreeSource);
                return null;
            }
            Transform chosen = source.transform.Find(ArtTreeChild);
            if (chosen == null)
            {
                Debug.LogError(Tag + "树源模型缺少子节点 " + ArtTreeChild);
                return null;
            }

            const string folder = "Assets/Art/Generated";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Art", "Generated");

            GameObject root = new GameObject("PF_DecorTree_Single");
            GameObject single = Object.Instantiate(chosen.gameObject, root.transform, false);
            single.name = "Tree";
            single.transform.localPosition = Vector3.zero;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            PrefabUtility.SaveAsPrefabAsset(root, ArtTreePrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(ArtTreePrefab);
        }

        static void InstallEditorArt(SceneryDecor deco, GameObject prefab)
        {
            Transform old = deco.transform.Find(SceneryDecor.ArtVisualName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            GameObject visual = PrefabUtility.InstantiatePrefab(prefab, deco.transform) as GameObject;
            if (visual == null) return;
            visual.name = SceneryDecor.ArtVisualName;
            SceneryDecor.FitArt(visual, deco.transform, TreeArtWidth, TreeArtHeight);
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Transform child in deco.transform)
                if (child != visual.transform) child.gameObject.SetActive(false);
        }
    }
}
