// @trace MECH-013, PLAT-005, TOOL-002, UI-005, UI-006
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// uGUI 按钮的持久化回调目标。
    ///
    /// 按钮的 OnClick 由 WhiteboxUiBuilder 用 UnityEventTools.AddPersistentListener
    /// 直接绑到这些 public 方法上，所以 Inspector 里能看到完整的绑定关系，
    /// 用户也可以把按钮改绑到自己的脚本而不必改 C#。
    /// </summary>
    public sealed class WhiteboxUiActions : MonoBehaviour
    {
        [Tooltip("由 WhiteboxUiBuilder 写入；指向场景里的 sys_whitebox")]
        public WhiteboxGame Game;

        public void DepositAll()
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.DepositAll();
        }

        public void Process(int category)
        {
            if (Game == null || Game.StationLogic == null) return;
            // 有弹窗：先进入待确认，由 Panel_Quantity 让玩家选次数（默认拉满）
            // 没弹窗（旧场景还没补齐 UI）：退化为「一键全部」，功能不丢
            if (Game.HasQuantityDialog) Game.StationLogic.BeginProcess(category);
            else Game.StationLogic.StartProcessAll(category);
        }

        public void Craft(int recipe)
        {
            if (Game == null || Game.StationLogic == null) return;
            if (Game.HasQuantityDialog) Game.StationLogic.BeginCraft(recipe);
            else Game.StationLogic.StartCraftAll(recipe);
        }

        public void UpgradeTool()
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.UpgradeTool();
        }

        public void UpgradeSpecTool(int category)
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.UpgradeSpecTool(category);
        }

        public void CloseStation()
        {
            if (Game != null && Game.StationOpen) Game.ToggleStation();
        }


        /// <summary>HUD 常驻「任务日志」按钮：转给统一仲裁，与 J 键同一开关链路。</summary>
        public void OpenQuestLog()
        {
            if (Game != null) Game.ToggleQuestLog();
        }

        public void Resume()
        {
            if (Game != null) Game.SetPaused(false);
        }

        public void Restart()
        {
            if (Game != null) Game.Restart();
        }

        /// <summary>
        /// 局内「退出」：先弹「是否存档」确认框，由玩家决定——
        /// 【是】进存档面板选槽命名保存后回主界面；【否】直接回主界面。
        /// 暂停页与结算页的退出按钮都绑到这里；不再直接 Application.Quit，
        /// 否则编辑器里只会打一行日志、看起来像"点了没反应"。
        /// </summary>
        public void Quit()
        {
            QuitConfirmDialog dialog = FindFirstObjectByType<QuitConfirmDialog>();
            if (dialog != null)
            {
                dialog.Open();
                Debug.Log("[WhiteboxUI] 局内退出：弹出是否存档确认框");
                return;
            }

            // 确认框缺失（旧场景未补齐 UI）：降级为原行为——存档后直接回主界面，功能不丢
            if (Game != null && Game.State != null) WhiteboxState.Save(Game.State);
            Time.timeScale = 1f;
            Debug.LogWarning("[WhiteboxUI] 未找到退出确认框，退出降级为：存档 → 返回标题");
            SceneManager.LoadScene(TitleController.TitleSceneName);
        }
    }
}
