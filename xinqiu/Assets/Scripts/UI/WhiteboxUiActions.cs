// @trace MECH-013, PLAT-005
using UnityEngine;

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
            if (Game != null && Game.StationLogic != null) Game.StationLogic.StartProcess(category);
        }

        public void Craft(int recipe)
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.StartCraft(recipe);
        }

        public void UpgradeTool()
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.UpgradeTool();
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

        public void Quit()
        {
#if UNITY_EDITOR
            Debug.Log("[WhiteboxUI] 编辑器下退出按钮不会关闭 Unity；停止 Play 或按 ESC 即可");
#endif
            Application.Quit();
        }
    }
}
