// @trace ART-004
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 一次性自动修复：把旧版垃圾预制体的「底部蓝色地标环」转换为「整体白色描边」，并收窄描边；
    /// 同时把立体感光照（压低环境光 + 冷色补光 + 轮廓光）应用到当前白膜场景。
    /// 只针对已存在且需更新的预制体 / 白膜场景，不重建场景、不动 Hierarchy 里的 UI 面板与双手。
    /// 用 SessionState 防重复执行；与 WhiteboxUiBuilder 的 UI 生成相互独立。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxTrashOutlineFix
    {
        const string Key = "TrashPlanet.Whitebox.OutlineFixed.v2";

        static WhiteboxTrashOutlineFix()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (Application.isBatchMode) { EditorApplication.update -= Tick; return; }
            if (SessionState.GetBool(Key, false)) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick;

            if (WhiteboxBuilder.HasLegacyRingPrefabs() || WhiteboxBuilder.OutlineNeedsRefresh())
            {
                Debug.Log("[WhiteboxTrashOutlineFix] 检测到描边需更新（去蓝环 / 收窄白描边），自动刷新垃圾预制体");
                WhiteboxBuilder.RefreshTrashOutlines();
            }

            // 立体感光照：仅当当前打开的是白膜场景、且尚未写入补光/轮廓光时应用，幂等。
            Scene s = EditorSceneManager.GetActiveScene();
            if (s.path == WhiteboxBuilder.ScenePath && !WhiteboxBuilder.LightingApplied(s))
            {
                Debug.Log("[WhiteboxTrashOutlineFix] 对当前白膜场景应用立体感光照（环境光压低 + 补光 + 轮廓光）");
                WhiteboxBuilder.ApplyVolumeLighting(s);
                EditorSceneManager.SaveScene(s);
            }
        }
    }
}
