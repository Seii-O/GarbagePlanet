// @trace MECH-013, PLAT-005, TOOL-002, UI-005
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

        public void UpgradeBackpack()
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.UpgradeBackpack();
        }

        public void CloseStation()
        {
            if (Game != null && Game.StationOpen) Game.ToggleStation();
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
        /// 局内「退出」：先存档再回到标题主界面（Title.unity），由标题页的「退出游戏」真正退进程。
        /// 暂停页与结算页的退出按钮都绑到这里；不再直接 Application.Quit，
        /// 否则编辑器里只会打一行日志、看起来像"点了没反应"。
        /// </summary>
        public void Quit()
        {
            // 回标题前落盘当前进度，标题页「继续游戏」才能接着玩；无 Game/无 state 时安全跳过。
            if (Game != null && Game.State != null) WhiteboxState.Save(Game.State);
            Time.timeScale = 1f; // 暂停时是 0，不还原会让标题与下次进关卡都卡在时间静止
            Debug.Log("[WhiteboxUI] 局内退出：存档 → 返回标题 " + TitleController.TitleSceneName);
            SceneManager.LoadScene(TitleController.TitleSceneName);
        }
    }
}
