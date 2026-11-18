using System;
using System.Collections.Generic;
using System.IO;
using PlanetCleaner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PlanetCleaner.EditorTools
{
    /// <summary>
    /// 一键生成白膜场景：相机 / 平行光 / 星球 / 垃圾占位 / 拖拽旋转。
    /// 菜单：Tools > 星球白膜 > ① 一键生成白膜场景
    /// </summary>
    public static class WhiteboxSceneBuilder
    {
        const string GameRootFolder = "Assets/_Game";
        const string SceneFolder = "Assets/_Game/Scenes";
        const string MaterialFolder = "Assets/_Game/Materials";
        const string ScriptFolder = "Assets/_Game/Scripts";
        const string ScenePath = SceneFolder + "/Whitebox.unity";

        const float PlanetRadius = 1f;
        const int TrashCount = 500;

        [MenuItem("Tools/星球白膜/① 一键生成白膜场景", false, 10)]
        public static void BuildWhiteboxScene()
        {
            bool batch = Application.isBatchMode;

            if (!batch && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("先退出 Play",
                    "请先退出 Play 模式，再生成白膜场景。", "好");
                return;
            }

            if (!batch && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            try
            {
                EnsureFolders();
                EnsureUserLayer("Planet");
                EnsureUserLayer("Trash");
                EnsureUserLayer("Player");
                EnsureTag("Trash");
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[白膜] Tag/Layer 写入失败，不影响场景生成：" + e.Message);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.21f, 0.28f);
            RenderSettings.skybox = null;

            Camera cam = BuildCamera();
            BuildLight();
            GameObject root = BuildPlanet(cam);
            GameObject player = BuildPlayer(cam, root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            Selection.activeGameObject = player;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log($"[白膜] 场景已生成：{ScenePath}");

            AssetDatabase.SaveAssets();

            if (batch) return;

            EditorUtility.DisplayDialog(
                "白膜场景已生成",
                "场景：Assets/_Game/Scenes/Whitebox.unity\n\n" +
                "① 按 Play\n" +
                "② 鼠标移动 = 看视角（自动锁定鼠标）\n" +
                "③ WASD 走动；Esc 解锁鼠标可左键拖星球，L 重新锁定\n\n" +
                "已包含：相机 / 平行光 / 星球 / 18 个垃圾占位 / 第一人称玩家 + 双手",
                "好");
        }

        [MenuItem("Tools/星球白膜/② 重开白膜场景", false, 11)]
        public static void OpenWhiteboxScene()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("场景不存在", "先执行 ① 生成白膜场景。", "好");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/星球白膜/③ 添加第一人称玩家（含双手）", false, 12)]
        public static void AddPlayerToCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("先退出 Play", "请先退出 Play 模式再添加玩家。", "好");
                return;
            }

            // 找星球根：优先 PlanetRoot，其次 Whitebox 体系的 WorldRoot，
            // 再退一步找场景里第一个星球球体。找不到就提示去正确的白膜场景。
            GameObject planetRoot = GameObject.Find("PlanetRoot");
            if (planetRoot == null) planetRoot = GameObject.Find("WorldRoot");
            if (planetRoot == null)
            {
                var sphere = UnityEngine.Object.FindObjectOfType<SphereCollider>();
                if (sphere != null) planetRoot = sphere.gameObject;
            }
            if (planetRoot == null)
            {
                EditorUtility.DisplayDialog("找不到星球根（PlanetRoot / WorldRoot）",
                    "当前场景里没有星球根，也没找到星球球体。\n\n" +
                    "《垃圾星球》是“站在星球上”的第一人称游戏，必须先有星球根：\n" +
                    "• 双击打开 Assets/Scenes/WhiteboxPrototype.unity，直接 Play（自带玩家+双手）；或\n" +
                    "• 运行菜单 Tools > 星球白膜 > ① 一键生成白膜场景。",
                    "好");
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                EditorUtility.DisplayDialog("找不到 Main Camera",
                    "当前场景里没有 Main Camera，请先执行 ① 生成白膜场景。", "好");
                return;
            }

            GameObject old = GameObject.Find("Player");
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old);

            EnsureUserLayer("Player");

            GameObject player = BuildPlayer(cam, planetRoot.transform);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = player;

            Debug.Log("[白膜] 第一人称玩家已添加：鼠标看视角，WASD 移动，Esc 解锁鼠标拖星球");
        }

        // ---------------------------------------------------------------
        // 场景内容
        // ---------------------------------------------------------------

        static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.045f, 0.075f, 1f);
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;

            go.transform.position = new Vector3(0f, 0f, -4.4f);
            go.transform.rotation = Quaternion.identity;

            go.AddComponent<AudioListener>();
            return cam;
        }

        static void BuildLight()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;

            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            // 补一盏很弱的背光，避免暗面死黑
            var fill = new GameObject("Fill Light");
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.45f, 0.55f, 0.85f);
            fillLight.intensity = 0.25f;
            fillLight.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(-20f, 140f, 0f);
        }

        static GameObject BuildPlanet(Camera cam)
        {
            var root = new GameObject("PlanetRoot");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Planet";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * (PlanetRadius * 2f);

            int planetLayer = LayerMask.NameToLayer("Planet");
            if (planetLayer >= 0) sphere.layer = planetLayer;

            Material planetMat = CreateOrLoadMaterial(
                MaterialFolder + "/M_Planet_Whitebox.mat",
                new Color(0.55f, 0.64f, 0.74f, 1f), 0.18f);
            sphere.GetComponent<MeshRenderer>().sharedMaterial = planetMat;

            Material trashMat = CreateOrLoadMaterial(
                MaterialFolder + "/M_Trash_Whitebox.mat",
                new Color(0.78f, 0.47f, 0.22f, 1f), 0.08f);

            int trashLayer = LayerMask.NameToLayer("Trash");
            bool hasTrashTag = Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, "Trash") >= 0;

            // 斐波那契球面分布：均匀、不结块、不会掉进球体内部
            for (int i = 0; i < TrashCount; i++)
            {
                float y = 1f - (i / (float)(TrashCount - 1)) * 2f;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float theta = i * 2.39996323f; // 黄金角

                Vector3 dir = new Vector3(Mathf.Cos(theta) * r, y, Mathf.Sin(theta) * r).normalized;

                var trash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                trash.name = $"Trash_{i:00}";
                trash.transform.SetParent(root.transform, false);
                trash.transform.localPosition = dir * (PlanetRadius + 0.02f);
                // 底面贴球面，再绕自身轴转一点，避免所有方块朝向一致
                trash.transform.localRotation =
                    Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, i * 37f, 0f);
                trash.transform.localScale = Vector3.one * 0.18f;

                trash.GetComponent<MeshRenderer>().sharedMaterial = trashMat;
                if (trashLayer >= 0) trash.layer = trashLayer;
                if (hasTrashTag) trash.tag = "Trash";
            }

            var rotator = root.AddComponent<PlanetDragRotator>();
            rotator.TargetCamera = cam;

            return root;
        }

        static GameObject BuildPlayer(Camera cam, Transform planetRoot)
        {
            const float playerHalfHeight = 1f; // CharacterController height = 2

            // 出生点：星球正面偏上（朝着相机那一侧），方便一进 Play 就看到人
            Vector3 dir = new Vector3(0f, 0.62f, -0.78f).normalized;
            Vector3 spawnPos = planetRoot.position + dir * (PlanetRadius + playerHalfHeight + 0.03f);

            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, dir);
            forward.Normalize();

            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(spawnPos, Quaternion.LookRotation(forward, dir));

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) go.layer = playerLayer;

            var controller = go.AddComponent<CharacterController>();
            controller.height = playerHalfHeight * 2f;
            controller.radius = 0.35f;
            controller.center = Vector3.zero;
            controller.slopeLimit = 55f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.03f;
            controller.minMoveDistance = 0f;

            Material bodyMat = CreateOrLoadMaterial(
                MaterialFolder + "/M_Player_Whitebox.mat",
                new Color(0.88f, 0.89f, 0.94f, 1f), 0.35f);

            // 白膜身体：胶囊 + 一个小方块标出正前方
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.7f, 0.85f, 0.7f);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.42f, 0.34f);
            nose.transform.localScale = new Vector3(0.14f, 0.14f, 0.22f);
            UnityEngine.Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            var rig = cam.GetComponent<PlayerCameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<PlayerCameraRig>();

            var player = go.AddComponent<PlayerController>();
            player.PlanetCenter = planetRoot;
            player.CameraRig = rig;
            player.PlanetRadius = PlanetRadius;

            rig.Target = go.transform;
            rig.Player = player;
            rig.PlanetCenter = planetRoot;

            BuildHands(cam, player, rig);

            return go;
        }

        public static void BuildHands(Camera cam, PlayerController player, PlayerCameraRig rig)
        {
            // 重建时先清掉旧的双手
            Transform oldHands = cam.transform.Find("Hands");
            if (oldHands != null)
                UnityEngine.Object.DestroyImmediate(oldHands.gameObject);

            Material handMat = CreateOrLoadMaterial(
                MaterialFolder + "/M_Hands_Whitebox.mat",
                new Color(0.93f, 0.89f, 0.83f, 1f), 0.25f);

            var root = new GameObject("Hands");
            root.transform.SetParent(cam.transform, false);

            // 注意：Unity 相机沿本地 -Z 渲染，双手必须放在 -Z（相机前方）才可见。
            // 旧版写成 +0.32f / +0.70f，整只手都在相机背后，运行后完全看不见。
            Transform left = CreateArm(root.transform, "HandLeft",
                new Vector3(-0.62f, -0.58f, -0.32f),
                new Vector3(-0.22f, -0.34f, -0.70f), handMat);

            Transform right = CreateArm(root.transform, "HandRight",
                new Vector3(0.62f, -0.58f, -0.32f),
                new Vector3(0.22f, -0.34f, -0.70f), handMat);

            var hands = root.AddComponent<FirstPersonHands>();
            hands.LeftHand = left;
            hands.RightHand = right;
            hands.Player = player;
            hands.CameraRig = rig;
        }

        /// <summary>白膜手臂：一根胶囊（小臂）+ 一个方块（手）。坐标是相机局部空间。</summary>
        static Transform CreateArm(Transform parent, string name, Vector3 start, Vector3 end, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = start;
            root.transform.localRotation = Quaternion.identity;

            Vector3 dir = end - start;
            float length = Mathf.Max(dir.magnitude, 0.01f);
            Vector3 dirN = dir / length;

            GameObject forearm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            forearm.name = "Forearm";
            forearm.transform.SetParent(root.transform, false);
            forearm.transform.localPosition = dirN * (length * 0.45f);
            forearm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dirN);
            forearm.transform.localScale = new Vector3(0.11f, length * 0.5f, 0.11f);
            UnityEngine.Object.DestroyImmediate(forearm.GetComponent<Collider>());

            var forearmRenderer = forearm.GetComponent<MeshRenderer>();
            forearmRenderer.sharedMaterial = mat;
            forearmRenderer.shadowCastingMode = ShadowCastingMode.Off;
            forearmRenderer.receiveShadows = false;

            GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.name = "Hand";
            hand.transform.SetParent(root.transform, false);
            hand.transform.localPosition = dir - dirN * 0.03f;
            hand.transform.localRotation = Quaternion.LookRotation(dirN, Vector3.up);
            hand.transform.localScale = new Vector3(0.13f, 0.10f, 0.17f);
            UnityEngine.Object.DestroyImmediate(hand.GetComponent<Collider>());

            var handRenderer = hand.GetComponent<MeshRenderer>();
            handRenderer.sharedMaterial = mat;
            handRenderer.shadowCastingMode = ShadowCastingMode.Off;
            handRenderer.receiveShadows = false;

            return root.transform;
        }

        // ---------------------------------------------------------------
        // 资源与设置
        // ---------------------------------------------------------------

        static Material CreateOrLoadMaterial(string path, Color color, float smoothness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Diffuse");

                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolders()
        {
            EnsureFolder(GameRootFolder);
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(ScriptFolder);
            EnsureFolder("Assets/_Game/Scripts/Input");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static void EnsureUserLayer(string layerName)
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var so = new SerializedObject(assets[0]);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null) return;

            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;
            }

            // 0~7 是内置层，用户层从 8 开始
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                so.ApplyModifiedProperties();
                return;
            }

            Debug.LogWarning($"[白膜] Layer 槽位已满，跳过：{layerName}");
        }

        static void EnsureTag(string tag)
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var so = new SerializedObject(assets[0]);
            SerializedProperty tags = so.FindProperty("tags");
            if (tags == null) return;

            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            so.ApplyModifiedProperties();
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
    }
}
