// @trace PLAT-005
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PlanetCleaner;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 第一人称双手修复（幂等、自动 + 手动）。
    ///
    /// 根因（2026-11-22 运行时实测确认）：相机子物体的本地坐标里 **+Z 才是前方**
    /// （transform.forward 即本地 +Z；Unity 的“-Z 向前”只适用于 view 空间，不是 local）。
    /// 旧代码把 -Z 当成了前方，双手挂在 z=-0.22，整只手落到相机背后被视锥剔除，
    /// 表现就是“按数字没有模型，手也没有”。实测日志：掌心 WorldToScreenPoint.z = -0.58（负=背后）。
    ///
    /// 触发：
    /// ① 编辑器启动后修复当前激活场景；
    /// ② 之后每次打开场景也修复（对应从 Build Settings 切到别的白膜场景再运行）；
    /// ③ 菜单「Tools/星球白膜/④ 修复第一人称双手（立即）」可手动强制修复当前场景。
    ///
    /// 修正方式：只移动 HandLeft / HandRight 两个挂点，**不重建**——
    /// 重建会把 HandRight/HeldTools 下 14 件手持模型一起删掉。只有双手整体缺失时才重建。
    ///
    /// 另外，光改对轴向还不够：相机 FOV 72（竖直半角 36°），旧挂点 y=-0.58 会让整只手
    /// 落到画面下边缘之外。下面的常量是运行时实测通过的最终几何，工具 Builder 共用同一组。
    [InitializeOnLoad]
    public static class WhiteboxHandsFix
    {
        const string HandMatPath = "Assets/_Game/Materials/M_Hands_Whitebox.mat";

        // 双手几何（相机本地坐标，单位米；+Z 为前方）：腕点 -> 掌心。
        // 2026-11-22 数值来自运行时实测通过的版本（掌心 screen=(1258,313)、z=0.58）：
        //   掌心 (±0.232, -0.177, +0.580) → x_ndc≈±0.31、y_ndc≈-0.42，落在画面右下内侧；
        //   腕点在 (±0.324, -0.195, +0.941)，小臂从右下角斜插进画面。
        public const float WristX = 0.324f;
        public const float WristY = -0.195f;
        public const float WristZ = 0.941f;
        public const float HandX = 0.232f;
        public const float HandY = -0.177f;
        public const float HandZ = 0.580f;

        // ---- 手部本体显隐 ----
        // 白膜阶段的两个胶囊/方块只用来占位，美术上不好看。默认只留手持工具、不显示手本体。
        // 注意：这里只关渲染子节点（Forearm / Hand / Palm），HandLeft / HandRight 两个挂点必须留着，
        // 因为 HeldTools 挂在 HandRight 下面，且 FirstPersonHands 的晃动逻辑写的就是这两个挂点。
        const string ShowHandMeshKey = "TrashPlanet.Whitebox.ShowHandMesh";
        static readonly string[] HandMeshNames = { "Forearm", "Hand", "Palm" };

        /// <summary>是否渲染白模手本体（胶囊小臂 + 方块掌）。默认 false：只要工具。</summary>
        public static bool ShowHandMesh
        {
            get => EditorPrefs.GetBool(ShowHandMeshKey, false);
            set => EditorPrefs.SetBool(ShowHandMeshKey, value);
        }

        /// <summary>按 <see cref="ShowHandMesh"/> 开关手部本体渲染，只动 Forearm/Hand/Palm 这几个子节点。返回 true 表示有改动。</summary>
        public static bool ApplyHandMeshVisibility(FirstPersonHands hands)
        {
            if (hands == null) return false;
            bool want = ShowHandMesh;
            bool changed = false;
            foreach (var t in hands.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(HandMeshNames, t.name) < 0) continue;
                // 只处理挂在 HandLeft / HandRight 直接下级的那几个，避免误伤手持模型里同名的图元
                if (t.parent == null) continue;
                if (t.parent.name != "HandLeft" && t.parent.name != "HandRight") continue;
                if (t.gameObject.activeSelf != want)
                {
                    t.gameObject.SetActive(want);
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// 双手挂点是否已偏离可视几何（历史 y=-0.58，或手动拖过 Hierarchy）。
        /// 供 ⑱ 调用：偏了就按标准几何重建，避免工具跟着生成到画面外。
        /// </summary>
        public static bool IsOffSpec(FirstPersonHands hands)
        {
            if (hands == null || hands.RightHand == null) return true;
            Vector3 r = hands.RightHand.localPosition;
            return Mathf.Abs(r.y - WristY) > 0.08f
                || Mathf.Abs(r.z - WristZ) > 0.08f
                || Mathf.Abs(Mathf.Abs(r.x) - WristX) > 0.08f;
        }

        /// <summary>把两个挂点摆到标准几何。只改坐标，不销毁节点，HandRight/HeldTools 下的手持模型原样保留。</summary>
        public static bool ApplySpec(FirstPersonHands hands)
        {
            if (hands == null || hands.RightHand == null || hands.LeftHand == null) return false;
            hands.RightHand.localPosition = new Vector3(WristX, WristY, WristZ);
            hands.LeftHand.localPosition = new Vector3(-WristX, WristY, WristZ);
            hands.RightHand.localRotation = Quaternion.identity;
            hands.LeftHand.localRotation = Quaternion.identity;
            hands.gameObject.SetActive(true);

            if (ShowHandMesh)
            {
                // 历史场景里手部渲染器可能被关掉过，顺手打开
                var rs = hands.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rs) if (!r.enabled) r.enabled = true;
            }

            // 手本体显隐按开关执行：默认关（只留工具），挂点与 HeldTools 不受影响
            ApplyHandMeshVisibility(hands);
            return true;
        }

        /// <summary>挂点偏了就按标准几何摆回去；返回 true 表示改动过。不重建，避免丢掉手持模型。</summary>
        public static bool EnsureVisible(FirstPersonHands hands, Camera cam)
        {
            if (Application.isBatchMode) return false;
            if (!IsOffSpec(hands) || cam == null) return false;
            Vector3 r = hands.RightHand != null ? hands.RightHand.localPosition : Vector3.zero;
            if (!ApplySpec(hands)) return false;
            Debug.Log("[WhiteboxHandsFix] 双手挂点不在可视区（" + r.ToString("F2")
                      + "），已摆回标准几何（腕点 x=±" + WristX + " y=" + WristY + " z=" + WristZ + "，+Z 为前方）");
            return true;
        }

        static WhiteboxHandsFix()
        {
            EditorApplication.update += TickActiveScene;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        static void TickActiveScene()
        {
            EditorApplication.update -= TickActiveScene;
            FixScene(EditorSceneManager.GetActiveScene());
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            FixScene(scene);
        }

        [MenuItem("Tools/星球白膜/④ 修复第一人称双手（立即）", false, 14)]
        public static void FixHandsNow()
        {
            // 手动执行：允许在“当前场景根本没有双手”时直接补上可见的双手，并强制按标准几何重建
            FixScene(EditorSceneManager.GetActiveScene(), true, true);
        }

        const string ShowHandMeshMenu = "Tools/星球白膜/⑤ 显示白模手本体";

        /// <summary>切换白模手的显示。默认关闭（只要手持工具），切换后立刻应用到当前场景并保存。</summary>
        [MenuItem(ShowHandMeshMenu, false, 15)]
        public static void ToggleHandMesh()
        {
            ShowHandMesh = !ShowHandMesh;
            FixScene(EditorSceneManager.GetActiveScene(), false, true);
            Debug.Log("[WhiteboxHandsFix] 白模手本体 " + (ShowHandMesh ? "已显示" : "已隐藏（只保留手持工具）"));
        }

        [MenuItem(ShowHandMeshMenu, true)]
        static bool ToggleHandMeshValidate()
        {
            Menu.SetChecked(ShowHandMeshMenu, ShowHandMesh);
            return true;
        }

        static void FixScene(Scene s)
        {
            FixScene(s, false, false);
        }

        /// <param name="allowCreate">true=当前场景若完全没有双手，也直接给主相机创建一双可见的。</param>
        /// <param name="force">true=手动菜单调用，即使判定“不需要修”也按标准几何重建。</param>
        static void FixScene(Scene s, bool allowCreate, bool force = false)
        {
            if (Application.isBatchMode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (string.IsNullOrEmpty(s.path)) return;

            bool fixedAny = false;
            var handsList = Object.FindObjectsOfType<FirstPersonHands>(true);

            if (handsList.Length == 0)
            {
                if (allowCreate)
                {
                    Camera cam = Camera.main ?? FirstEnabledCamera();
                    if (cam != null && cam.transform.Find("Hands") == null)
                    {
                        var player = Object.FindObjectOfType<PlayerController>();
                        var rig = Object.FindObjectOfType<PlayerCameraRig>();
                        RebuildHands(cam, player, rig);
                        fixedAny = true;
                        Debug.Log("[WhiteboxHandsFix] 当前场景缺少第一人称双手，已给主相机创建（本地 +Z 为前方）。");
                    }
                }
            }
            else
            {
                foreach (var hands in handsList)
                {
                    Camera cam = ResolveCamera(hands);

                    // 挂点被删过：只能整体重建（HeldTools 会一起没，需重跑 ⑱）
                    if (hands.LeftHand == null || hands.RightHand == null)
                    {
                        if (cam == null) continue;
                        RebuildHands(cam, hands.Player, hands.CameraRig);
                        fixedAny = true;
                        Debug.Log("[WhiteboxHandsFix] 双手挂点缺失，已重建（请重跑 ⑱ 生成手持模型）");
                        continue;
                    }

                    bool needFix = force
                        || !hands.gameObject.activeInHierarchy
                        || !AllRenderersEnabled(hands)
                        || IsOffSpec(hands);
                    if (!needFix) continue;
                    if (!ApplySpec(hands)) continue;

                    fixedAny = true;
                    Debug.Log("[WhiteboxHandsFix] 双手挂点已摆回标准几何（腕点 x=±" + WristX + " y=" + WristY + " z=" + WristZ + "，+Z 为前方）");
                }
            }

            if (fixedAny)
            {
                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
            }
        }

        static bool AllRenderersEnabled(FirstPersonHands hands)
        {
            // 不显示手本体时，Forearm/Hand 处于关闭是预期结果，不能判成“需要修”，否则每次编译都会重存场景
            if (!ShowHandMesh) return true;

            var renderers = hands.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (!r.enabled) return false;
            return true;
        }

        static Camera FirstEnabledCamera()
        {
            foreach (var c in Object.FindObjectsOfType<Camera>())
                if (c.enabled && c.gameObject.activeInHierarchy) return c;
            return null;
        }

        static Camera ResolveCamera(FirstPersonHands hands)
        {
            if (hands.CameraRig != null)
            {
                var cam = hands.CameraRig.GetComponent<Camera>();
                if (cam != null) return cam;
            }
            if (hands.transform.parent != null)
                return hands.transform.parent.GetComponent<Camera>();
            return null;
        }

        // 自包含重建：销毁旧 Hands，按标准几何重建，沿用 player / cameraRig 引用。
        static void RebuildHands(Camera cam, PlayerController player, PlayerCameraRig rig)
        {
            Transform oldHands = cam.transform.Find("Hands");
            Material handMat = LoadHandMat();
            if (oldHands != null) Object.DestroyImmediate(oldHands.gameObject);

            var root = new GameObject("Hands");
            root.transform.SetParent(cam.transform, false);

            CreateArm(root.transform, "HandLeft",
                new Vector3(-WristX, WristY, WristZ),
                new Vector3(-HandX, HandY, HandZ), handMat);
            CreateArm(root.transform, "HandRight",
                new Vector3(WristX, WristY, WristZ),
                new Vector3(HandX, HandY, HandZ), handMat);

            var hf = root.AddComponent<FirstPersonHands>();
            hf.LeftHand = root.transform.Find("HandLeft");
            hf.RightHand = root.transform.Find("HandRight");
            hf.Player = player;
            hf.CameraRig = rig;
        }

        static void CreateArm(Transform parent, string name, Vector3 start, Vector3 end, Material mat)
        {
            var arm = new GameObject(name);
            arm.transform.SetParent(parent, false);
            arm.transform.localPosition = start;
            arm.transform.localRotation = Quaternion.identity;

            // 只要工具时不必生成白模图元，留一个空挂点即可（挂点本身必须有，工具挂在它下面）
            if (!ShowHandMesh) return;

            Vector3 dir = end - start;
            float length = Mathf.Max(dir.magnitude, 0.01f);
            Vector3 dirN = dir / length;

            var forearm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            forearm.name = "Forearm";
            forearm.transform.SetParent(arm.transform, false);
            forearm.transform.localPosition = dirN * (length * 0.45f);
            forearm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dirN);
            forearm.transform.localScale = new Vector3(0.11f, length * 0.5f, 0.11f);
            Object.DestroyImmediate(forearm.GetComponent<Collider>());
            var fr = forearm.GetComponent<MeshRenderer>();
            fr.sharedMaterial = mat;
            fr.shadowCastingMode = ShadowCastingMode.Off;
            fr.receiveShadows = false;

            var hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.name = "Hand";
            hand.transform.SetParent(arm.transform, false);
            hand.transform.localPosition = dir - dirN * 0.03f;
            hand.transform.localRotation = Quaternion.LookRotation(dirN, Vector3.up);
            hand.transform.localScale = new Vector3(0.13f, 0.10f, 0.17f);
            Object.DestroyImmediate(hand.GetComponent<Collider>());
            var hr = hand.GetComponent<MeshRenderer>();
            hr.sharedMaterial = mat;
            hr.shadowCastingMode = ShadowCastingMode.Off;
            hr.receiveShadows = false;
        }

        static Material LoadHandMat()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(HandMatPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader) { name = "M_Hands_Whitebox" };
                AssetDatabase.CreateAsset(mat, HandMatPath);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.93f, 0.89f, 0.83f, 1f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            return mat;
        }
    }
}
