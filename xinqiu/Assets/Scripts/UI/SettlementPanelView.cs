// @trace UI-009
using System;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 结算面板控制器：按「12_结算.png」展示评级、六行统计、五阶段进度。
    /// 只读 State / Config / Purification，不写玩法数据；按钮转调 WhiteboxGame 既有入口。
    /// </summary>
    public sealed class SettlementPanelView : MonoBehaviour
    {
        public const string PanelName = "Panel_SettlementFinal";

        static readonly string[] StageNames = { "探索", "回收", "加工", "制造", "恢复" };

        WhiteboxGame game;
        GameObject panel;
        bool bound;
        [SerializeField] SettlementArtSet art;
        [SerializeField] Font font;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake() { Resolve(); }

        public void Wire(WhiteboxGame g) { game = g; bound = false; Resolve(); }

        public void SetArt(SettlementArtSet a, Font f) { art = a; font = f; }

        /// <summary>游玩结束弹出：先解析面板再刷新， Association 由 Builder 持久化注入。</summary>
        public void Show()
        {
            if (!Resolve()) return;
            panel.SetActive(true);
            Refresh();
            if (game != null) game.ApplyUiState();
        }

        public void Hide()
        {
            if (panel == null) return;
            panel.SetActive(false);
            if (game != null) game.ApplyUiState();
        }

        bool Resolve()
        {
            if (bound && panel != null) return true;
            if (game == null) game = FindObjectOfType<WhiteboxGame>();
            Transform found = FindDeep(transform, PanelName);
            if (found == null)
            {
                SettlementUiFactory.Build(transform, art, font);
                found = FindDeep(transform, PanelName);
                if (found == null) return false;
            }
            panel = found.gameObject;
            BindClick("Btn_ContinuePlaying", ContinuePlaying);
            BindClick("Btn_BackToTitle", BackToTitle);
            panel.SetActive(false);
            bound = true;
            return true;
        }

        void ContinuePlaying()
        {
            Hide();
            if (game != null) game.DismissSettlement();
        }

        void BackToTitle()
        {
            if (game == null) return;
            if (game.State != null) WhiteboxState.Save(game.State);
            game.ReturnToTitle();
        }

        // ---------------- 动态刷新 ----------------
        void Refresh()
        {
            if (game == null || game.State == null || panel == null) return;
            WhiteboxState s = game.State;
            bool covered = game.Purification != null && game.Purification.FullyCovered;
            SettlementScore.Result score = SettlementScore.Calculate(s, game.Config, covered);
            float target = game.Config != null && game.Config.MvpProgressTarget > 0f
                ? game.Config.MvpProgressTarget : 0.3f;

            SetText(panel.transform, "Txt_Grade", score.grade);
            SetText(panel.transform, "Txt_GradeNote", "生态完全恢复 · " + score.total + " 分");

            SetStat("Stat_Collected", s.collected.ToString(), "件");
            SetStat("Stat_Processed", s.processed.ToString(), "件");
            SetStat("Stat_Crafted", s.crafted.ToString(), "件");
            SetStat("Stat_Land", Mathf.Clamp(Mathf.RoundToInt(s.landProgress / target * 100f), 0, 100).ToString(), "%");
            SetStat("Stat_Water", Mathf.Clamp(Mathf.RoundToInt(s.waterProgress / target * 100f), 0, 100).ToString(), "%");
            SetStat("Stat_Time", SettlementScore.FormatTime(s.playTime), "");

            int[] stageValues =
            {
                Mathf.RoundToInt(s.movedDistance),                 // 探索：移动距离（米）
                s.collected,                                       // 回收：拾取件数
                s.processed,                                       // 加工：加工次数
                s.crafted,                                         // 制造：产品件数
                Mathf.RoundToInt(Mathf.Clamp01((s.landProgress + s.waterProgress)
                    / (2f * target)) * 100f)                      // 恢复：生态恢复百分比
            };
            string[] stageDetails = { "行进 " + stageValues[0] + " 米", "回收 " + stageValues[1] + " 件",
                "加工 " + stageValues[2] + " 件", "制造 " + stageValues[3] + " 件",
                "恢复 " + stageValues[4] + " %" };
            for (int i = 0; i < SettlementUiFactory.StageCount; i++)
            {
                Transform node = FindDeep(panel.transform, "Stage_" + i);
                if (node == null) continue;
                SetText(node, "Txt_Detail", stageDetails[i]);
                Sprite idx = i < 4 ? art?.stepDone : (covered ? art?.stepDone : art?.stepCurrent);
                SetImageSprite(node, "Img_Idx", idx);
            }
        }

        void SetStat(string statName, string value, string unit)
        {
            Transform row = FindDeep(panel.transform, statName);
            if (row == null) return;
            SetText(row, "Txt_Value", value);
            SetText(row, "Txt_Unit", unit);
        }

        void BindClick(string name, Action call)
        {
            Button b = FindButton(panel.transform, name);
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => call());
        }

        static void SetImageSprite(Transform root, string name, Sprite sprite)
        {
            if (sprite == null) return;
            Transform t = FindDeep(root, name);
            Image img = t != null ? t.GetComponent<Image>() : null;
            if (img != null && img.sprite != sprite) img.sprite = sprite;
        }

        static void SetText(Transform root, string name, string value)
        {
            if (root == null) return;
            Text t = FindDeep(root, name)?.GetComponent<Text>();
            if (t != null && t.text != value) t.text = value;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
