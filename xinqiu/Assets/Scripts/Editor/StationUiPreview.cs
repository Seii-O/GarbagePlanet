// @trace ART-004, ART-008
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 离线预览：把场景里的中转站面板（以及数量弹窗）渲成 PNG，用来在不开编辑器窗口的情况下
    /// 检查排版有没有压字 / 溢出 / 错位。只读场景，不保存任何修改。
    ///
    /// 跑法：Unity.exe -batchmode -projectPath &lt;工程&gt;
    ///       -executeMethod TrashPlanet.Whitebox.Editor.StationUiPreview.RenderBatch
    /// 输出：docs/preview-station.png / docs/preview-quantity.png
    /// </summary>
    public static class StationUiPreview
    {
        const string Tag = "[StationUiPreview] ";
        const int W = 1920;
        const int H = 1080;

        public static void RenderBatch()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
                // 先按最新脚本重建一次面板（幂等），保证预览看到的就是工程里的样子
                bool rebuilt = StationUiBuilder.Rebuild(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log(Tag + "rebuild=" + rebuilt);

                GameObject ui = FindRoot(scene, WhiteboxUiBuilder.RootName);
                if (ui == null) { Debug.LogError(Tag + "没有 UI_Whitebox"); EditorApplication.Exit(1); return; }

                SampleTexts(ui.transform);

                GameObject station = ui.transform.Find("Panel_Station")?.gameObject;
                GameObject qty = ui.transform.Find("Panel_Quantity")?.gameObject;

                // 只留 UI 根，其余场景根（星球/玩家/天空）先关掉，免得渲染时挡住面板
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    if (roots[i] != ui && roots[i].name != "EventSystem") roots[i].SetActive(false);

                // 调试 HUD / 常驻提示在真机上由 WhiteboxCanvasHud 控制显隐，预览里先藏掉
                string[] hide = { "Hud_Debug", "Txt_Hint", "Txt_Notice", "Txt_Deposit" };
                for (int i = 0; i < hide.Length; i++)
                {
                    Transform t = Find(ui.transform, hide[i]);
                    if (t != null) t.gameObject.SetActive(false);
                }

                Render(ui, new[] { station }, "docs/preview-station.png");
                if (qty != null) Render(ui, new[] { qty }, "docs/preview-quantity.png");

                bool pass = WhiteboxValidator.Validate();
                Debug.Log(Tag + "validate=" + pass);
                EditorApplication.Exit(pass ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "预览失败：" + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>填一套有代表性的运行期文案（长度接近真实值，才能看出会不会压字）。</summary>
        static void SampleTexts(Transform root)
        {
            SetText(root, "Txt_Repaired", "已修复: 否（先造 1 块再生建材板）  ·  背包 Lv.1/2（总 6/20）");
            SetText(root, "Txt_Raw", "待加工垃圾  塑/金/有/电: 12/8/3/0");
            SetText(root, "Txt_StationMaterials", "材料  塑板/金件/有机/芯: 12/15/2/6");
            string[] cats = { "塑料", "金属", "有机", "电子" };
            string[] mats = { "再生塑料", "金属零件", "有机原料", "电子元件" };
            int[] batch = { 5, 4, 5, 2 };
            int[] times = { 3, 2, 0, 0 };
            for (int i = 0; i < 4; i++)
                SetText(root, "Btn_Process_" + i, "② 加工" + cats[i] + " ×" + batch[i] + "→" + mats[i]
                                                  + "  可" + times[i] + "次");
            string[] recipes = { "再生建材板", "生态肥料包", "水质过滤器" };
            string[] costs = { "再生塑料×2 + 金属零件×1", "有机原料×3", "再生塑料×1 + 电子元件×2" };
            for (int i = 0; i < 3; i++)
                SetText(root, "Btn_Craft_" + i, "③ " + recipes[i] + "（" + costs[i] + "）可" + (i == 0 ? 1 : 0) + "次");
            SetText(root, "Btn_Upgrade", "④ 升级工具（Lv.1 → 2）");
            SetText(root, "Btn_BagUpgrade", "⑤ 升级背包（总 20 → 40，需 15 金属零件）");
            SetText(root, "Txt_Job", "加工塑料 ×3  45%");
            SetText(root, "JobBar", null);
            SetText(root, "Txt_QtyTitle", "加工塑料 · 选择数量");
            SetText(root, "Txt_QtyInfo", "可执行 1 ~ 3 次  ·  预计 15 s");
            SetText(root, "Txt_QtyValue", "× 3");
            SetText(root, "Txt_QtyDetail", "消耗 15 塑料垃圾  →  +9 再生塑料");
        }

        static void SetText(Transform root, string name, string value)
        {
            if (value == null) return;
            Transform t = Find(root, name);
            if (t == null) return;
            Text txt = t.GetComponentInChildren<Text>(true);
            if (txt != null) txt.text = value;
        }

        static void Render(GameObject ui, GameObject[] show, string outPath)
        {
            Canvas canvas = ui.GetComponent<Canvas>();
            RenderMode oldMode = canvas.renderMode;
            Camera oldCam = canvas.worldCamera;
            float oldPlane = canvas.planeDistance;

            var camGo = new GameObject("PreviewCamera", typeof(Camera));
            Camera cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.09f, 0.08f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = H * 0.5f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 500f;
            cam.transform.position = new Vector3(0f, 0f, -100f);
            camGo.hideFlags = HideFlags.HideAndDontSave;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 100f;

            // 面板默认失活：按需打开
            if (show != null)
                foreach (GameObject go in show)
                    if (go != null) go.SetActive(true);

            Canvas.ForceUpdateCanvases();

            RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../docs"));
            string abs = Path.Combine(Application.dataPath, "../" + outPath);
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            Debug.Log(Tag + "已输出 " + abs);

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);

            if (show != null)
                foreach (GameObject go in show)
                    if (go != null) go.SetActive(false);

            canvas.renderMode = oldMode;
            canvas.worldCamera = oldCam;
            canvas.planeDistance = oldPlane;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }
    }
}
