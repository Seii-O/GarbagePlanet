// 新增玩法：垃圾堆隐藏款 + 转盘奖励（本次需求，尚未纳入 design-freeze 编号）
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 独立转盘界面视图。UI 对象由 GachaBuilder 用 Editor API 生成到 UI_Whitebox 画布下，
    /// 本脚本在 Awake 里按名字查找并运行时绑定按钮（不用 Editor 持久监听，避免签名坑）。
    /// 面板默认失活，显隐完全由 GachaSystem 通过 Show/Hide 控制，不被 WhiteboxCanvasHud 覆盖。
    /// </summary>
    public sealed class GachaPanelView : MonoBehaviour
    {
        const string PanelName = "Panel_Gacha";
        static readonly string[] RaritySlots = { "普通", "精英", "史诗" };

        GachaSystem system;
        bool bound;

        RectTransform panel;
        RectTransform needle;
        Text txtTickets;
        Text txtDraws;
        Text txtPity;
        Text txtResult;
        Text txtHint;
        Button btnDraw;
        Button btnClose;

        Coroutine spinRoutine;
        bool spinning;
        bool pendingRefresh;

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        void Awake()
        {
            Bind();
        }

        bool Bind()
        {
            if (bound) return true;
            Transform root = transform;
            panel = root.Find(PanelName) as RectTransform;
            if (panel == null)
            {
                Debug.LogWarning("[GachaPanelView] 未找到 " + PanelName + "，请先跑 Tools/垃圾星球白膜/⑨ 生成转盘配置与界面");
                return false;
            }

            needle = FindRt(panel, "Wheel_Needle");
            txtTickets = FindText(panel, "Txt_Tickets");
            txtDraws = FindText(panel, "Txt_Draws");
            txtPity = FindText(panel, "Txt_Pity");
            txtResult = FindText(panel, "Txt_Result");
            txtHint = FindText(panel, "Txt_Hint");
            btnDraw = FindButton(panel, "Btn_Draw");
            btnClose = FindButton(panel, "Btn_Close");

            // 运行时绑定，签名明确（UnityAction 无参），不依赖 Editor 持久化参数
            if (btnDraw != null) btnDraw.onClick.AddListener(OnDrawClick);
            if (btnClose != null) btnClose.onClick.AddListener(OnCloseClick);

            panel.gameObject.SetActive(false);
            bound = true;
            return true;
        }

        public void Init(GachaSystem s)
        {
            system = s;
            if (!Bind()) return;
            RefreshTexts(0, 0, 0, 0, null, null, false);
        }

        void OnDrawClick()
        {
            if (system != null && !spinning) system.Draw();
        }

        void OnCloseClick()
        {
            if (system != null) system.Close();
        }

        public void Show()
        {
            if (!Bind()) return;
            panel.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (!Bind()) return;
            if (spinRoutine != null) { StopCoroutine(spinRoutine); spinRoutine = null; }
            spinning = false;
            pendingRefresh = false;
            if (btnDraw != null) btnDraw.interactable = true;
            panel.gameObject.SetActive(false);
        }

        public void Refresh(int tickets, int draws, int pity, int hardPity,
            GachaDrawResult result, WhiteboxState state)
        {
            if (!Bind()) return;
            RefreshTexts(tickets, draws, pity, hardPity, result, state, true);
        }

        void RefreshTexts(int tickets, int draws, int pity, int hardPity,
            GachaDrawResult result, WhiteboxState state, bool fromSystem)
        {
            if (txtTickets != null) txtTickets.text = "转盘券  ×" + tickets;
            if (txtDraws != null) txtDraws.text = "已抽  " + draws + " 次";
            if (txtPity != null)
            {
                string p = hardPity > 0
                    ? "距史诗保底  " + Mathf.Max(0, hardPity - pity) + " 抽"
                    : "距史诗保底  未启用";
                txtPity.text = p + "　（连续未出 " + pity + "）";
            }
            if (txtResult != null)
            {
                if (result != null && result.reward != null)
                {
                    // 优先用抽奖时记录的入库后数量；旧结果/外部结果再回退查 state
                    int owned = result.ownedCount > 0 ? result.ownedCount
                                : (state != null ? state.RewardCount(result.reward.id) : 1);
                    txtResult.text = "【" + result.RarityName + "】" + result.KindName + "：" + result.reward.displayName
                                     + "\n" + result.reward.description
                                     + "\n" + (result.isNew ? "★ 新获得！" : "已有 ×" + owned);
                    txtResult.color = Hex(result.Color);
                }
                else if (result != null)
                {
                    txtResult.text = "奖励池为空，请在 GachaConfig 配置奖励";
                    txtResult.color = Color.white;
                }
                else
                {
                    txtResult.text = "搜索垃圾堆有机会发现隐藏款，获得转盘券\n按 R 开启 / 关闭转盘";
                    txtResult.color = new Color(0.86f, 0.87f, 0.84f, 1f);
                }
            }
            if (txtHint != null)
            {
                txtHint.text = tickets > 0 ? "点中心【开始】消耗 1 券　·　R 关闭" : "没有券：去捡垃圾找隐藏款吧　·　R 关闭";
            }
            bool allowDraw = !spinning && (!pendingRefresh || fromSystem);
            if (btnDraw != null) btnDraw.interactable = tickets > 0 && allowDraw;
        }

        /// <summary>抽奖后播放指针旋转：在结果档位对应的扇区内随机落点，缓动到目标角度。</summary>
        public void PlaySpin(GachaDrawResult result)
        {
            if (!Bind() || needle == null || result == null) return;
            if (spinRoutine != null) StopCoroutine(spinRoutine);
            pendingRefresh = true;
            spinRoutine = StartCoroutine(Spin(result));
        }

        IEnumerator Spin(GachaDrawResult result)
        {
            spinning = true;
            if (btnDraw != null) btnDraw.interactable = false;
            if (txtResult != null) txtResult.text = "转盘旋转中…";

            // 与该档位的某个扇区对齐：先按稀有度取扇区编号，再取该扇区中心顺时针角。
            // Unity 的 z 正方向是逆时针，因此指针目标 z = -顺时针角；再在扇区内加小抖动避免每次同点。
            int[] slots = WheelLayout.IndicesOf(result.rarity);
            int pick = slots.Length > 0 ? slots[Random.Range(0, slots.Length)] : 0;
            float centerClockwise = WheelLayout.CenterAngleAt(pick);
            float jitter = Random.Range(-WheelLayout.SectorAngle * 0.28f, WheelLayout.SectorAngle * 0.28f);
            float targetZ = -(centerClockwise + jitter);

            float from = needle.localEulerAngles.z;
            float to = from + 360f * 3f + Mathf.DeltaAngle(from, targetZ);
            float dur = 1.15f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float ease = 1f - Mathf.Pow(1f - k, 3f); // easeOutCubic
                needle.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(from, to, ease));
                yield return null;
            }
            needle.localEulerAngles = new Vector3(0f, 0f, targetZ);
            spinning = false;
            spinRoutine = null;
            pendingRefresh = false;

            // 转完再显示完整结果（Draw 里已写入 lastResult）
            if (system != null) system.RefreshPanel();
        }

        static Text FindText(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        static RectTransform FindRt(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t as RectTransform : null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }

        static Color Hex(string s)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(s, out c)) return c;
            return Color.white;
        }
    }
}
