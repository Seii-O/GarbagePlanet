// @trace MECH-005, MECH-007, MECH-016, ART-004, TOOL-003
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 准星交互：相机中心射线 → 唯一目标 → 描边高亮 → 左键拾取/拆解。
    /// [待拍板 C5] 描边阈值与拾取距离冲突：白膜取 min(pickRange, highlightRange)，
    /// 拾取范围仍用 pickRange[工具等级]，两者都可配置，未写成既定结论。
    /// 本次按需求把两者一起 ×2：拾取 4/6/8 m，描边 6 m（Lv.2 起两者同值）。
    /// 优先级：距离小者优先，距离相同取 instanceID 小者（策划案 §6.3）。
    /// </summary>
    public sealed class InteractionController : MonoBehaviour
    {
        // 拾取距离翻倍（4/6/8 m）后一次射线会覆盖更多垃圾，命中缓存同步放大，避免近处密集垃圾把目标挤掉
        readonly RaycastHit[] hits = new RaycastHit[64];
        WhiteboxGame game;
        TrashItem current;
        SlotMachine currentMachine;      // 准星当前对准的老虎机实体（与垃圾目标互不干扰）
        float machineDistance;
        int barLeft = -1, barMax = -1; // 黄条上次写入值，用于幂等，避免每帧无谓刷新

        public TrashItem CurrentTarget => current; // MECH-016：狗避开玩家准星锁定的目标
        public float CurrentDistance { get; private set; }
        public SlotMachine CurrentMachine => currentMachine; // 供 HUD 提示"按 F 抽奖"用

        public void Init(WhiteboxGame g)
        {
            game = g;
            current = null;
            CurrentDistance = 0f;
            SetMachine(null); // 清掉上一局可能残留的机器高亮
            barLeft = barMax = -1; // 强制下一帧重刷，清掉上一局可能残留的黄条状态
        }

        void Update()
        {
            if (game == null || game.ViewCamera == null || game.Config == null) return;
            if (game.IsUiBlocking) { Clear(); return; }

            WhiteboxConfig cfg = game.Config;
            WhiteboxState state = game.State;
            ToolSystem tool = game.Tool;
            float range = cfg.PickRange[Mathf.Clamp(state.toolLevel - 1, 0, 2)];
            if (tool != null) range = tool.MaxRaycastRange(); // 拾取距离 4/6/8 + 夹/专精，钳 9 m
            float highlight = Mathf.Min(range, cfg.HighlightRange); // C5 口径不变：描边恒 ≤6 m

            Transform cam = game.ViewCamera.transform;
            Ray ray = new Ray(cam.position, cam.forward);
            int count = Physics.RaycastNonAlloc(ray, hits, range, ~0, QueryTriggerInteraction.Ignore);

            TrashItem candidate = null;
            float candidateDist = float.MaxValue;
            float blockerDist = float.MaxValue;
            SlotMachine machineCandidate = null;
            float machineCandidateDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                if (h.collider == null) continue;
                if (game.Player != null && h.collider.transform.IsChildOf(game.Player)) continue;

                // 老虎机实体：与垃圾并列的准星目标。命中它时不算"遮挡垃圾"的 blocker，
                // 但仍要参与机器自己的遮挡判定（比它更近的实体会挡住它）。
                SlotMachine machine = h.collider.GetComponentInParent<SlotMachine>();
                if (machine != null)
                {
                    if (h.distance < machineCandidateDist)
                    {
                        machineCandidate = machine;
                        machineCandidateDist = h.distance;
                    }
                    continue;
                }

                TrashItem item = h.collider.GetComponentInParent<TrashItem>();
                if (item == null)
                {
                    if (h.distance < blockerDist) blockerDist = h.distance;
                    continue;
                }

                // 工具加成按类别生效：金属专精的 +0.5 m 只让金属垃圾进入候选，不改其他类别
                if (tool != null && h.distance > tool.EffectiveRange(item.Category)) continue;

                bool closer = h.distance < candidateDist - 0.0001f;
                bool tie = Mathf.Abs(h.distance - candidateDist) <= 0.0001f
                           && candidate != null && item.GetInstanceID() < candidate.GetInstanceID();
                if (closer || tie)
                {
                    candidate = item;
                    candidateDist = h.distance;
                }
            }

            if (candidate != null && blockerDist < candidateDist - 0.0001f) candidate = null;
            if (machineCandidate != null && blockerDist < machineCandidateDist - 0.0001f) machineCandidate = null;

            // 老虎机准星目标：只在玩家自己的交互半径内高亮；距离判定用机器自带半径，不跟拾取距离混用。
            if (machineCandidate != null && machineCandidateDist > machineCandidate.InteractRange)
                machineCandidate = null;
            SetMachine(machineCandidate, machineCandidateDist);

            // F 键：对准老虎机且在交互距离内 → 触发抽奖（GetKeyDown 边沿，避免按住连开）。
            // 面板打开后 IsUiBlocking 会在下一帧走上面的 Clear() 分支，不会重复触发。
            if (currentMachine != null && machineDistance <= currentMachine.InteractRange
                && Input.GetKeyDown(KeyCode.F))
            {
                currentMachine.Interact();
                return; // 本帧交出控制权，等面板的 ApplyUiState 生效
            }

            if (candidate != current)
            {
                if (current != null) current.SetHighlight(false);
                current = candidate;
                CurrentDistance = candidate != null ? candidateDist : 0f;
                if (current != null && CurrentDistance <= highlight) current.SetHighlight(true);
            }
            else if (current != null)
            {
                CurrentDistance = candidateDist;
                current.SetHighlight(CurrentDistance <= highlight);
            }

            // 修复：current 为准星未命中垃圾时为 null，必须先判空再取 Category。
            // 工具加成只在「有目标」时才有意义，无目标时用基础 range 即可（后续 click 也要求 current != null）。
            float clickRange = (current != null && tool != null) ? tool.EffectiveRange(current.Category) : range;
            if (current != null && CurrentDistance <= clickRange && Input.GetMouseButtonDown(0))
                ResolveClick();

            // 黄条与白边同源：每帧按当前准星目标同步，准星离开 / 超出高亮范围立即隐藏。
            // 旧实现只在「点击 / Clear」时写入缓存，准星移开后缓存不清除，导致黄条残留，
            // 必须再点一次别的物体才会消失。放在点击之后，保证扣耐久当帧就刷新。
            SyncDurabilityBar(highlight);
        }

        void ResolveClick()
        {
            int cat = (int)current.Category;
            WhiteboxConfig cfg = game.Config;
            WhiteboxState state = game.State;
            ToolSystem tool = game.Tool;
            int damage = cfg.DismantleDamage[Mathf.Clamp(state.toolLevel - 1, 0, 2)];
            bool firstHit = current.Large && current.Durability >= current.BaseDurability;
            if (tool != null) damage = tool.EffectiveDamage(current.Category, firstHit);
            TrashItem target = current;

            if (!target.Large)
            {
                if (!state.CanCollect(cfg, cat)) { game.Notice("背包已满"); return; }
                int n = state.Collect(cfg, cat, 1); // NUM-001
                if (n <= 0) { game.Notice("背包已满"); return; }
                WorldPickupPopup.Show(target, game.ViewCamera, game.PlanetCenter, target.Category, n);
                game.Spawner.Despawn(target);
                Clear();
                game.OnWorldEvent();
                if (game.Gacha != null) game.Gacha.OnTrashSearched(target.Category); // 新增玩法：搜索垃圾堆发现隐藏款
                return;
            }

            // 大型垃圾：容量不足时忽略点击，不改耐久（策划案 §6.2）
            if (!state.CanCollect(cfg, cat)) { game.Notice("背包已满，无法拆解"); return; }
            int left = target.Hit(damage);
            // 不再手动写黄条：Update 末尾的 SyncDurabilityBar 会读 target.Durability 同帧刷新。
            if (left > 0) return;

            int yieldAmount = target.YieldAmount;
            if (tool != null) yieldAmount = tool.EffectiveYield(current.Category);
            int gain = state.Collect(cfg, cat, yieldAmount);
            if (gain <= 0) { game.Notice("背包已满，无法拆解"); return; }
            state.dismantled += 1;
            WorldPickupPopup.Show(target, game.ViewCamera, game.PlanetCenter, target.Category, gain);
            game.Spawner.Despawn(target);
            Clear();
            game.OnWorldEvent();
            if (game.Gacha != null) game.Gacha.OnTrashSearched(target.Category); // 新增玩法：搜索垃圾堆发现隐藏款
        }

        /// <summary>切换老虎机准星目标：只在变化时写高亮，避免每帧刷新。</summary>
        void SetMachine(SlotMachine machine, float distance = 0f)
        {
            if (currentMachine == machine)
            {
                if (machine != null) machineDistance = distance;
                return;
            }
            if (currentMachine != null) currentMachine.SetHighlight(false);
            currentMachine = machine;
            machineDistance = machine != null ? distance : 0f;
            if (currentMachine != null) currentMachine.SetHighlight(true);
        }

        void Clear()
        {
            if (current != null) current.SetHighlight(false);
            current = null;
            CurrentDistance = 0f;
            SetMachine(null); // 面板打开/暂停时取消机器高亮，关闭后由下一帧重新检测
            SetDurability(0, 0);
        }

        /// <summary>
        /// 大型垃圾耐久条与白边共用同一判定：准星锁定的唯一目标、是大型垃圾、
        /// 且距离不超过白边的高亮范围（highlight = min(pickRange, HighlightRange)）。
        /// 任一条件不满足就写 (0,0) 隐藏，因此准星移开、被遮挡、超出范围都立即消失。
        /// </summary>
        void SyncDurabilityBar(float highlight)
        {
            bool show = current != null && current.Large && CurrentDistance <= highlight;
            SetDurability(show ? current.Durability : 0,
                          show ? current.BaseDurability : 0);
        }

        /// <summary>黄条唯一写入口：值变化才转发，避免每帧重复刷新 UI。</summary>
        void SetDurability(int left, int max)
        {
            if (left == barLeft && max == barMax) return;
            barLeft = left; barMax = max;
            if (game != null && game.Hud != null) game.Hud.SetDurability(left, max);
        }
    }
}
