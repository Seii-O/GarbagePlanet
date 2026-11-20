using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 播放器设置一键应用：把 Unity 出厂默认的 `DefaultCompany / My project`
    /// 换成项目自身的公司名与产品名（打包、存档路径、窗口标题都会用到）。
    ///
    /// 为什么用编辑器脚本而不是手改 ProjectSettings.asset：
    /// 编辑器运行时持有 ProjectSettings 的内存副本，退出时会覆盖磁盘上的 YAML，
    /// 只有走 PlayerSettings API 改内存副本，磁盘与内存才会一致。
    ///
    /// 菜单：Tools/垃圾星球白膜/⑥ 应用播放器设置
    /// 首次导入脚本后自动应用一次（每个编辑器会话仅一次，幂等）。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxPlayerSettingsFix
    {
        public const string Company = "GarbagePlanet";
        public const string Product = "垃圾星球";

        const string SessionKey = "TrashPlanet.Whitebox.PlayerSettings.Applied.v1";
        const string Tag = "[WhiteboxPlayerSettings] ";

        static WhiteboxPlayerSettingsFix()
        {
            // SessionState 跨域重载保持，避免每次脚本改动都重跑
            if (SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += AutoApply;
        }

        static void AutoApply()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (Application.isBatchMode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoApply; // 等编译/导入结束再改
                return;
            }
            SessionState.SetBool(SessionKey, true);
            Apply();
        }

        [MenuItem("Tools/垃圾星球白膜/⑦ 应用播放器设置", false, 16)]
        public static void Apply()
        {
            bool changed = false;
            if (PlayerSettings.companyName != Company)
            {
                PlayerSettings.companyName = Company;
                changed = true;
            }
            if (PlayerSettings.productName != Product)
            {
                PlayerSettings.productName = Product;
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                // ProjectSettings 不在 AssetDatabase 管辖范围，靠 Save Project 落盘
                try { EditorApplication.ExecuteMenuItem("File/Save Project"); }
                catch (System.Exception e) { Debug.LogWarning(Tag + "Save Project 调用失败：" + e.Message); }
                Debug.Log(Tag + "已应用 companyName=" + Company + " / productName=" + Product);
            }
            else
            {
                Debug.Log(Tag + "无需修改，当前已是 companyName=" + Company + " / productName=" + Product);
            }
        }
    }
}
