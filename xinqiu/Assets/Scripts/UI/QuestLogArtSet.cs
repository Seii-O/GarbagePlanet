// @trace UI-008
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 任务日志界面的美术拆件集。Editor 构建时从「垃圾星球_UI资源库 1」加载并序列化到场景，
    /// 运行时工厂/视图只从这里取 Sprite，不访问 AssetDatabase。
    /// 每个 Image 的 Source Image 都是可在 Inspector 修改的真实拆件。
    /// </summary>
    [Serializable]
    public sealed class QuestLogArtSet
    {
        public Sprite panelBig, card, listRow, highlightRow, bottomBar;
        public Sprite btnPrimary, btnSecondary, btnGhost, btnDisabled, btnCloseSquare;
        public Sprite statusDotTeal, statusDotAmber, labelTeal, labelAmber;
        public Sprite dotEmpty, dotFilled, ringBottom, ringTeal;
        public Sprite icTask, icCheck, icLock, icArrow, icClose, icStar;
        public Sprite stepDone, stepCurrent, stepDefault;

        public Sprite Step(int state)
        {
            if (state == 0) return stepDone;
            if (state == 1) return stepCurrent;
            return stepDefault;
        }
    }
}
