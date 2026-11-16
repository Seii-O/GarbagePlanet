// @trace ART-004, ART-008, MECH-013
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 中转站面板的「纯展示」数据填充：把 State 里的背包 / 材料数值摊到每一行上。
    ///
    /// 定位：只读 State，只写 Text.text 与 Image.fillAmount，不参与任何判定，
    /// 也不改 RectTransform / 颜色 / 字号（排版和美术全在场景里，美术可直接换 Image 的图）。
    /// 因此它和 WhiteboxCanvasHud 是并列的两个视图组件，删掉本组件只会丢掉「每行的小数字」，
    /// 中转站的核心逻辑（StationSystem）与结算、提示完全不受影响。
    /// </summary>
    public sealed class StationBoardView : MonoBehaviour
    {
        [Header("装配（编辑器构建脚本写入；为空时按名字找）")]
        public WhiteboxGame Game;
        public Text BagSum;

        [Header("背包 4 行（塑料 / 金属 / 有机 / 电子）")]
        public Text[] BagCounts = new Text[4];
        public Text[] BagCaps = new Text[4];
        public Image[] BagFills = new Image[4];

        [Header("材料 4 格")]
        public Text[] MaterialCounts = new Text[4];

        readonly int[] lastBag = new int[4];
        readonly int[] lastCap = new int[4];
        readonly int[] lastMat = new int[4];
        bool resolved;
        int lastLevel = -1;

        void Awake() { Resolve(); }

        void OnEnable() { Resolve(); Refresh(); }

        void Update() { Refresh(); }

        public void Resolve()
        {
            if (resolved) return;
            resolved = true;
            if (Game == null) Game = FindObjectOfType<WhiteboxGame>();
            if (BagSum == null) BagSum = FindText("Txt_BagSum");
            for (int i = 0; i < 4; i++)
            {
                if (BagCounts == null || BagCounts.Length < 4) BagCounts = new Text[4];
                if (BagCaps == null || BagCaps.Length < 4) BagCaps = new Text[4];
                if (BagFills == null || BagFills.Length < 4) BagFills = new Image[4];
                BagCounts[i] = BagCounts[i] != null ? BagCounts[i] : FindText("Txt_BagCount_" + i);
                BagCaps[i] = BagCaps[i] != null ? BagCaps[i] : FindText("Txt_BagCap_" + i);
                BagFills[i] = BagFills[i] != null ? BagFills[i] : FindImage("Img_BagBarFill_" + i);
                if (MaterialCounts == null || MaterialCounts.Length < 4) MaterialCounts = new Text[4];
                MaterialCounts[i] = MaterialCounts[i] != null ? MaterialCounts[i] : FindText("Txt_MatVal_" + i);
            }
        }

        void Refresh()
        {
            if (Game == null || Game.State == null || Game.Config == null) return;
            WhiteboxState s = Game.State;
            int[] caps = Game.Config.BackpackCapAt(s.bagLevel);

            // 数值没变就不重建字符串（面板开着时每帧调，避免无谓的 GC）
            if (s.bagLevel == lastLevel && Same(s.bag, lastBag) && Same(caps, lastCap)
                && Same(s.materials, lastMat)) return;
            lastLevel = s.bagLevel;
            Copy(s.bag, lastBag);
            Copy(caps, lastCap);
            Copy(s.materials, lastMat);

            int sum = 0, capSum = 0;
            for (int i = 0; i < 4; i++)
            {
                int cap = caps != null && i < caps.Length ? Mathf.Max(1, caps[i]) : 1;
                int have = s.bag != null && i < s.bag.Length ? s.bag[i] : 0;
                sum += have;
                capSum += cap;
                Set(BagCounts, i, have.ToString());
                Set(BagCaps, i, "/" + cap);
                if (BagFills != null && i < BagFills.Length && BagFills[i] != null)
                    BagFills[i].fillAmount = Mathf.Clamp01(have / (float)cap);
                Set(MaterialCounts, i, s.materials != null && i < s.materials.Length
                    ? s.materials[i].ToString() : "0");
            }
            if (BagSum != null) SetText(BagSum, sum + " / " + capSum);
        }

        static bool Same(int[] a, int[] b)
        {
            for (int i = 0; i < 4; i++)
            {
                int x = a != null && i < a.Length ? a[i] : 0;
                if (x != b[i]) return false;
            }
            return true;
        }

        static void Copy(int[] a, int[] b)
        {
            for (int i = 0; i < 4; i++) b[i] = a != null && i < a.Length ? a[i] : 0;
        }

        static void Set(Text[] arr, int i, string value)
        {
            if (arr == null || i >= arr.Length || arr[i] == null) return;
            SetText(arr[i], value);
        }

        static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }

        Text FindText(string name)
        {
            Transform t = Find(name, typeof(Text));
            return t != null ? t.GetComponent<Text>() : null;
        }

        Image FindImage(string name)
        {
            Transform t = Find(name, typeof(Image));
            return t != null ? t.GetComponent<Image>() : null;
        }

        Transform Find(string name, System.Type type)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name && all[i].GetComponent(type) != null) return all[i];
            return null;
        }
    }
}
