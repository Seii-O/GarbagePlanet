// @trace UI-009
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 结算界面拆件集。Editor 构建时从「垃圾星球_UI资源库 1」加载并序列化到场景，
    /// 运行时工厂/视图只从这里取 Sprite，不访问 AssetDatabase。
    /// 每个 Image 的 Source Image 都是 Inspector 可改的真实拆件。
    /// </summary>
    [Serializable]
    public sealed class SettlementArtSet
    {
        public Sprite panelBig, card, listRow, bottomBar;
        public Sprite btnPrimary, btnSecondary, btnGhost, btnDisabled;
        public Sprite statusDotTeal, labelTeal, labelAmber;
        public Sprite ringBottom, ringAmber, ringTeal;
        public Sprite badgeCircle, medallion;
        public Sprite dotEmpty, dotFilled;
        public Sprite stepDone, stepCurrent, stepDefault;
        public Sprite barPlastic, barMetal, barOrganic, barElectronic;
        public Sprite divDotted, divSolid;
        public Sprite icStar, icCheck, icClock;
    }
}
