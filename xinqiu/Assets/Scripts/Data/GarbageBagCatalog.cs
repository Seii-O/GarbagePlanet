// @trace BAG-003, UI-006
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 垃圾背包的「单品目录」：8 张可展示卡片（每类 2 种），纯静态定义，不持有任何运行时状态。
    ///
    /// 边界：本目录只描述玩家捡到的垃圾/废弃物；工具与技能书见 ToolConfig / ToolBagConfig，
    /// 金币交易见 CoinTradeConfig，中转站加工见 StationSystem。四者互不混用。
    ///
    /// ItemIconPaths 是工程内美术资源的相对路径，供 Editor 构建脚本加载 Sprite；
    /// 运行时视图只拿构建时注入的 Sprite，不访问 AssetDatabase。
    /// </summary>
    public static class GarbageBagCatalog
    {
        public const int ItemCount = 8;
        public const int ItemsPerCategory = 2;

        public static readonly string[] Names =
        {
            "塑料瓶", "塑料袋", "易拉罐", "铁片", "果皮", "枯叶", "电池", "电路板"
        };

        public static readonly int[] Categories =
        {
            0, 0, 1, 1, 2, 2, 3, 3
        };

        /// <summary>相对 Assets/Art/UI/垃圾星球_UI资源库/ 的图标路径（不含扩展名）。</summary>
        public static readonly string[] ItemIconPaths =
        {
            "01_垃圾图标_八种/01_塑料_塑料瓶",
            "01_垃圾图标_八种/02_塑料_塑料袋",
            "01_垃圾图标_八种/03_金属_易拉罐",
            "01_垃圾图标_八种/04_金属_铁片",
            "01_垃圾图标_八种/05_有机_果皮",
            "01_垃圾图标_八种/06_有机_枯叶",
            "01_垃圾图标_八种/07_电子_电池",
            "01_垃圾图标_八种/08_电子_电路板"
        };

        public static readonly string[] CategoryIconPaths =
        {
            "02_类别图标/ic_类别_塑料",
            "02_类别图标/ic_类别_金属",
            "02_类别图标/ic_类别_有机",
            "02_类别图标/ic_类别_电子"
        };

        public static readonly string[] MaterialIconPaths =
        {
            "06_UI界面拆件/图标_材料与成品/ic_材料_再生塑料",
            "06_UI界面拆件/图标_材料与成品/ic_材料_金属零件",
            "06_UI界面拆件/图标_材料与成品/ic_材料_有机原料",
            "06_UI界面拆件/图标_材料与成品/ic_材料_电子元件"
        };

        public static int CategoryOf(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= ItemCount) return -1;
            return Categories[itemIndex];
        }

        public static int FirstItemOf(int category)
        {
            return Mathf.Clamp(category, 0, 3) * ItemsPerCategory;
        }

        public static string NameOf(int itemIndex)
        {
            return itemIndex >= 0 && itemIndex < ItemCount ? Names[itemIndex] : "";
        }
    }
}
