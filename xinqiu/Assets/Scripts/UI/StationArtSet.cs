// @trace UI-007
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 五页签中转站界面的美术资源集：Editor 构建时从新库（垃圾星球_UI资源库 1）加载并
    /// 序列化到场景，运行时工厂与视图只从这里取 Sprite，绝不访问 AssetDatabase。
    ///
    /// 这样每个 Image 的 Source Image 都是真实的拆件/图标（Inspector 可见可改），
    /// 而不是一整张静态整页图；美术换图只要覆盖同名 PNG 或改这里的引用。
    /// </summary>
    [Serializable]
    public sealed class StationArtSet
    {
        // 容器 / 面板
        public Sprite panelBig, panelSmall, card, listRow, highlightRow, headerBar, bottomBar;
        public Sprite divSolid, divDotted, divDashed;
        // 按钮
        public Sprite btnPrimary, btnPrimaryDown, btnSecondary, btnSecondaryDown, btnGhost;
        public Sprite btnSmallPrimary, btnSmallSecondary, btnSmallGhost, btnDanger, btnDisabled, btnCloseSquare;
        // 页签 / 标签 / 徽章
        public Sprite tabNormal, tabSelected, tabBar, statusBadge, statusDotTeal, statusDotAmber, statusDotRed;
        public Sprite labelAmber, labelTeal, countBadge, stickerAmber, stickerTeal;
        // 进度 / 数值
        public Sprite barBottom, fillAmber, fillTeal, fillGreen, fillRed;
        public Sprite dotEmpty, dotFilled, ringBottom, ringTeal, ringAmber;
        // 类别图标 4
        public Sprite[] catIcons = new Sprite[4];
        // 垃圾单品图标 8
        public Sprite[] itemIcons = new Sprite[8];
        // 材料图标 4 / 成品图标 3
        public Sprite[] materialIcons = new Sprite[4];
        public Sprite[] productIcons = new Sprite[3];
        // 功能图标
        public Sprite icClose, icArrow, icReceive, icSort, icProcess, icCraft, icRepair, icUpgrade;
        public Sprite icPlus, icMinus, icCheck, icClock, icStar, icBackpack;

        /// <summary>取类别图标，越界返回 null。</summary>
        public Sprite Cat(int i) => catIcons != null && i >= 0 && i < catIcons.Length ? catIcons[i] : null;
        public Sprite Item(int i) => itemIcons != null && i >= 0 && i < itemIcons.Length ? itemIcons[i] : null;
        public Sprite Material(int i) => materialIcons != null && i >= 0 && i < materialIcons.Length ? materialIcons[i] : null;
        public Sprite Product(int i) => productIcons != null && i >= 0 && i < productIcons.Length ? productIcons[i] : null;
    }
}
