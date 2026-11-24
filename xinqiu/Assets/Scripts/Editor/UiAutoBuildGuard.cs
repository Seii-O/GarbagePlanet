// @trace UI-007
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// UI 自动重建总闸：脚本重编译 / 域重载 / 编辑器重启后，禁止所有 [InitializeOnLoad]
    /// Builder 的 AutoEnsure 自动重建面板，保护用户在 Inspector / 场景里手调好的 UI。
    ///
    /// 设计：
    /// - 用 EditorPrefs 持久化（跨编辑器重启保持），默认锁定 = true。
    /// - 各 Builder 的 AutoEnsure 入口查 UiAutoBuildGuard.Locked，锁定即 return，不删不建。
    /// - 手动菜单（⑳㉑㉒㉓ 等）不查此闸，仍是「存在即删重建」的强制入口：
    ///   要改布局时手动跑一次菜单，之后保持锁定即可。
    /// - 批处理（BuildBatch / -executeMethod）不经过 AutoEnsure，不受影响。
    ///
    /// 菜单：Tools/垃圾星球白膜/UI 自动重建锁定（勾选=锁定）
    /// </summary>
    public static class UiAutoBuildGuard
    {
        const string PrefKey = "TrashPlanet.Whitebox.UiAutoBuildLocked";
        const string MenuPath = "Tools/垃圾星球白膜/UI 自动重建锁定";
        const string Tag = "[UiAutoBuildGuard] ";

        /// <summary>true = 禁止自动重建（UI 定稿后应保持锁定）。</summary>
        public static bool Locked
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, false, 90)]
        static void Toggle()
        {
            bool next = !Locked;
            Locked = next;
            Debug.Log(Tag + (next
                ? "已锁定：编译/重启不会再自动重建 UI；要强制重建请手动跑对应菜单（⑳㉑㉒㉓ 等）。"
                : "已解锁：域重载后各 Builder 会按各自守卫规则补齐 UI。"));
        }

        [MenuItem(MenuPath, true, 90)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Locked);
            return true;
        }
    }
}
