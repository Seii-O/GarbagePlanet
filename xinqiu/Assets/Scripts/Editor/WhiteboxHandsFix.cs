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
    /// 根因：Unity 相机沿本地 -Z 渲染，但双手曾被放在相机本地 +Z（相机背后），
    /// 于是整只手都被视锥剔除，运行后完全看不见。
    ///
    /// 触发：
    /// ① 编辑器启动后修复当前激活场景；
    /// ② 之后每次打开场景也修复（对应从 Build Settings 切到别的白膜场景再运行）；
    /// ③ 菜单「Tools/星球白膜/④ 修复第一人称双手（立即）」可手动强制修复当前场景。
    ///
    /// 判定：仅当双手根节点本地 z 仍 &gt; 0（处于相机背后）才重建；已在 -Z 的场景跳过。
    /// 重建为自包含逻辑，不依赖 WhiteboxBuilder，避免跨脚本编译失败导致修复失效。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxHandsFix
    {
        const string HandMatPath = "Assets/_Game/Materials/M_Hands_Whitebox.mat";

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
            // 手动执行：允许在“当前场景根本没有双手”时直接补上可见的双手
            FixScene(EditorSceneManager.GetActiveScene(), true);
        }

        static void FixScene(Scene s)
        {
            FixScene(s, false);
        }

        /// <param name="allowCreate">true=当前场景若完全没有双手，也直接给主相机创建一双可见的。</param>
        static void FixScene(Scene s, bool allowCreate)
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
                        Debug.Log("[WhiteboxHandsFix] 当前场景缺少第一人称双手，已给主相机创建（相机前方 -Z，已启用渲染）。");
                    }
                }
            }
            else
            {
                foreach (var hands in handsList)
                {
                    bool needFix = hands.LeftHand == null || hands.RightHand == null
                        || hands.LeftHand.localPosition.z > 0f || hands.RightHand.localPosition.z > 0f
                        || !hands.gameObject.activeInHierarchy
                        || !AllRenderersEnabled(hands);
                    if (!needFix) continue;

                    Camera cam = ResolveCamera(hands);
                    if (cam == null) continue;

                    RebuildHands(cam, hands.Player, hands.CameraRig);
                    fixedAny = true;
                    Debug.Log("[WhiteboxHandsFix] 已将第一人称双手重建到相机前方（-Z），修复“运行后双手看不见”。");
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

        // 自包含重建：销毁旧 Hands，按正确 -Z 几何重建，沿用 player / cameraRig 引用。
        static void RebuildHands(Camera cam, PlayerController player, PlayerCameraRig rig)
        {
            Transform oldHands = cam.transform.Find("Hands");
            Material handMat = LoadHandMat();
            if (oldHands != null) Object.DestroyImmediate(oldHands.gameObject);

            var root = new GameObject("Hands");
            root.transform.SetParent(cam.transform, false);

            CreateArm(root.transform, "HandLeft",
                new Vector3(-0.62f, -0.58f, -0.32f),
                new Vector3(-0.22f, -0.34f, -0.70f), handMat);
            CreateArm(root.transform, "HandRight",
                new Vector3(0.62f, -0.58f, -0.32f),
                new Vector3(0.22f, -0.34f, -0.70f), handMat);

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
