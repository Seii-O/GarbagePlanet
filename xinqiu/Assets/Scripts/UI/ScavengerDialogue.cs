using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 关卡开场叙事 + 行为语境字幕。新游戏先锁住移动播放序章，随后按移动、停留、
    /// 玩家拾取/拆解、狗狗叼回/探路等行为，从 100 条内容库中挑选对应台词。
    /// </summary>
    public sealed class ScavengerDialogue : MonoBehaviour
    {
        [SerializeField, Min(0f)] float openingDelay = 0.8f;
        [SerializeField, Min(1f)] float introSeconds = 3.2f;
        [SerializeField, Min(1f)] float showSeconds = 3f;
        const float DialogueInterval = 15f;

        static readonly string[] IntroLines =
        {
            "这颗星球曾经适宜生存，直到堆积的垃圾污染了土地与水源，人们陆续离开。",
            "如今，只剩一座仍亮着灯的中转站，守着灰暗的荒地。",
            "你是新任星球环保管理员，身边只有一条忠实的伙伴狗。",
            "收集、分拣并再造垃圾，修复土地——垃圾仍在产生，回收必须跑得比浪费更快。",
            "从一堆垃圾，到一颗重新呼吸的星球。这条路，你不是一个人走。"
        };

        // 20 条：新游戏前期重点强化“同行”。
        static readonly string[] EarlyCompanionLines =
        {
            "【你】第一次搭档，先说好：我负责计划，你负责把计划踩乱。",
            "【狗狗】汪。翻译一下：你走前面，我负责确认你没走丢。",
            "【你】整颗星球只剩我们俩，至少开会不用订大会议室。",
            "【狗狗】汪汪！同行守则第一条：零食要共享。",
            "【你】一个人叫加班，两个人……叫组队加班。",
            "【狗狗】别怕，我会一直跟着你，尤其是你打开食物的时候。",
            "【你】前路很长。好在脚印旁边，还有四个小爪印。",
            "【狗狗】你看路，我闻路。我们都有光明的分工。",
            "【你】这地方安静得可怕。幸好你喘气挺有存在感。",
            "【狗狗】汪！我不是喘，我是在给队伍配背景音乐。",
            "【你】要把星球救回来，第一步是别让自己被垃圾绊倒。",
            "【狗狗】收到。我负责看垃圾，你负责被垃圾看。",
            "【你】中转站还亮着灯，像是在等我们来接夜班。",
            "【狗狗】夜班可以，夜宵也要安排。",
            "【你】只要我们还在走，这颗星球就不算被彻底放弃。",
            "【狗狗】我会跟紧。毕竟你连方向感都需要回收利用。",
            "【你】同行的好处，是尴尬的时候总有人假装没看见。",
            "【狗狗】放心，我看见了，但我不会写进报告。",
            "【你】我们把荒地一点点收拾干净，慢一点也没关系。",
            "【狗狗】你捡世界，我捡你的心情。还有地上的饼干。"
        };

        static readonly string[] PlasticLines =
        {
            "【你】塑料瓶轻得像没干坏事，寿命倒比我职业生涯长。",
            "【狗狗】这个不能咬？你们人类的玩具规则真复杂。",
            "【你】它被压得这么扁，倒是很懂节省背包空间。",
            "【狗狗】闻起来没有肉，差评，但我陪你捡。",
            "【你】又一只塑料瓶。它们是不是在这里开同学会？",
            "【狗狗】汪！抓到一只透明的，不会跑的。",
            "【你】瓶盖不见了。它大概比瓶子更早开始新生活。",
            "【狗狗】我可以帮忙找瓶盖，报酬按小饼干结算。",
            "【你】塑料很轻，留下的问题一点也不轻。",
            "【狗狗】你装进包里，我负责用眼神监督它别逃跑。",
            "【你】再捡一个，我们离“地面能看”又近了一厘米。",
            "【狗狗】这个颜色不错，可惜不能当飞盘。"
        };

        static readonly string[] MetalLines =
        {
            "【你】这块金属很有分量，我的肩膀已经提交意见了。",
            "【狗狗】哐当！很好，全星球都知道我们捡到了。",
            "【你】锈成这样还挺倔，和这颗星球一样不肯彻底认输。",
            "【狗狗】它声音很大，我喜欢。再碰一下？",
            "【你】金属可以再利用，我的腰目前不确定。",
            "【狗狗】你负责搬，我负责在旁边显得很忙。",
            "【你】又冷又硬，至少它对自己的性格很诚实。",
            "【狗狗】别担心，搬不动时我会对它进行语言劝导。",
            "【你】这罐子空了很久，回声倒保存得不错。",
            "【狗狗】汪——听见了吗？它也汪了一声。",
            "【你】把它熔了重做，也许下次能成为有用的东西。",
            "【狗狗】比如一个自动发零食的机器，我只是建议。"
        };

        static readonly string[] OrganicLines =
        {
            "【你】有机垃圾。理论上能分解，气味已经提前行动了。",
            "【狗狗】这个我能吃吗？你的表情告诉我最好别问第二次。",
            "【你】别凑近闻。算了，你已经闻完一整份报告了。",
            "【狗狗】报告结论：很有故事，不建议续集。",
            "【你】它曾经可能是一顿饭，现在是一场生态谈判。",
            "【狗狗】我愿意负责谈判，前提是对方别再冒泡。",
            "【你】堆肥是归宿，背包只是它短暂的候车室。",
            "【狗狗】那我离候车室远一点，保持礼貌距离。",
            "【你】自然会处理它，但自然显然也需要一点帮忙。",
            "【狗狗】我可以挖坑！等等，你为什么突然紧张？",
            "【你】这一袋看不出原料，倒能看出放了很久。",
            "【狗狗】我宣布它获得“最不想带回家奖”。"
        };

        static readonly string[] ElectronicLines =
        {
            "【你】电子垃圾：看起来没电，麻烦倒是满格。",
            "【狗狗】它会动吗？不动的话我先对它叫两声。",
            "【你】屏幕碎了，但它映出的荒地还挺高清。",
            "【狗狗】我在里面看见一只帅狗。设备基本功能正常。",
            "【你】不知道它存过多少数据，现在只剩一层灰。",
            "【狗狗】能查到零食库存吗？不能就是严重故障。",
            "【你】别短路，我今天的计划已经够曲折了。",
            "【狗狗】放心，我站远一点提供无线支持。",
            "【你】电池要单独处理，不能再让它给土壤“充电”。",
            "【狗狗】明白，这种电我不负责叼。舌头投了反对票。",
            "【你】旧机器退役了，里面的材料还没下班。",
            "【狗狗】我们把它带回去，也许能修成自动投球机。"
        };

        static readonly string[] DismantleLines =
        {
            "【你】终于拆开了。刚才到底是谁把它拧得这么紧？",
            "【狗狗】我全程提供精神支持，主要表现为摇尾巴。",
            "【你】一下、两下……环保工作也有很物理的一面。",
            "【狗狗】你敲它，我对它施加心理压力。完美配合。",
            "【你】大型垃圾只是普通垃圾穿了件厚外套。",
            "【狗狗】外套脱下来了。里面还是不能吃，遗憾。",
            "【你】工具升级果然有用，我的手腕终于不用单独加班。",
            "【狗狗】拆得漂亮！碎片数量充分证明了你的努力。",
            "【你】搞定。幸好它没在最后一刻长腿逃跑。",
            "【狗狗】它要是长腿，我会追。你负责在后面喘。"
        };

        static readonly string[] WalkingLines =
        {
            "【你】这颗星球没有路，走的人多了……还是没有路。",
            "【狗狗】跟着我，我鼻子认路。偶尔也认错零食袋。",
            "【你】再往前看看。最糟也不过是发现更多工作。",
            "【狗狗】你走慢一点，我的小短腿正在超频。",
            "【你】地平线很远，好在我们不用今天就走完。",
            "【狗狗】你的脚印很直，说明迷路得很有信心。",
            "【你】风景正在恢复，只是目前主色调叫“需要打扫”。",
            "【狗狗】并肩走不了，我就走你旁边偏后一点。也算同行。"
        };

        static readonly string[] IdleLines =
        {
            "【你】休息十秒。环保管理员也属于需要维护的设备。",
            "【狗狗】你不走了吗？那我先把尾巴摇够本。",
            "【你】安静下来才听见，中转站的机器还在远处响。",
            "【狗狗】我去前面看看。别动，我回来还要找到你。",
            "【你】发呆不算偷懒，这是在进行全局环境评估。",
            "【狗狗】评估结果呢？我觉得草地适合趴。",
            "【你】慢一点也好，至少能确认我们没有把彼此落下。",
            "【狗狗】我一直在。顺便说一句，你背包漏不漏零食？"
        };

        static readonly string[] DogFetchLines =
        {
            "【狗狗】看，我也捡到了！夸奖和零食可以一起发。",
            "【你】干得漂亮。看来这份工作终于有第二名员工了。",
            "【狗狗】叼回来了。工资请直接打进饭盆。",
            "【你】你负责叼，我负责记账。我们都假装这样很公平。",
            "【狗狗】目标回收完毕！尾巴雷达正在搜索下一个。",
            "【你】同行效率确实高，尤其同行有四条腿的时候。"
        };

        public static int GameplayLibraryCount => EarlyCompanionLines.Length + PlasticLines.Length
            + MetalLines.Length + OrganicLines.Length + ElectronicLines.Length
            + DismantleLines.Length + WalkingLines.Length + IdleLines.Length + DogFetchLines.Length;

        readonly System.Random random = new System.Random();
        WhiteboxGame game;
        Text subtitle;
        Font ownedFont;
        string lastMessage;
        float visibleFor;
        float wait;
        float introWait;
        float idleFor;
        float walkedSinceLine;
        Vector3 lastPlayerPosition;
        bool hasLastPlayerPosition;
        int introIndex;

        public bool IsIntroPlaying { get; private set; }

        public void Init(WhiteboxGame owner)
        {
            game = owner;
            bool freshGame = SaveSlotSystem.ActiveSlot == 0
                && game.State.playTime <= 0f && game.State.collected == 0;
            IsIntroPlaying = freshGame;
            introIndex = -1;
            introWait = openingDelay;
            if (game.Player != null)
            {
                lastPlayerPosition = game.Player.position;
                hasLastPlayerPosition = true;
            }
            ResetWait();
            Debug.Assert(GameplayLibraryCount == 100,
                "[ScavengerDialogue] 对话内容库应为 100 条，当前=" + GameplayLibraryCount);
        }

        void Update()
        {
            if (game == null) return;

            if (IsIntroPlaying)
            {
                TickIntro(Time.unscaledDeltaTime);
                return;
            }

            if (game.IsUiBlocking)
            {
                if (subtitle != null) subtitle.enabled = false;
                return;
            }

            float dt = Time.deltaTime;
            TrackPlayerBehaviour(dt);
            wait = Mathf.Max(0f, wait - dt);

            if (visibleFor > 0f)
            {
                visibleFor = Mathf.Max(0f, visibleFor - dt);
                if (subtitle != null) subtitle.enabled = visibleFor > 0f;
                return;
            }

            if (wait > 0f) return;

            bool early = IsEarlyGame();
            string[] pool;
            if (early && random.NextDouble() < 0.65) pool = EarlyCompanionLines;
            else if (idleFor >= 7f) pool = IdleLines;
            else if (walkedSinceLine >= 4f) pool = WalkingLines;
            else pool = EarlyCompanionLines;
            Show(Pick(pool));
            walkedSinceLine = 0f;
            ResetWait();
        }

        void TickIntro(float dt)
        {
            if (subtitle == null) Build();
            if (visibleFor > 0f)
            {
                visibleFor = Mathf.Max(0f, visibleFor - dt);
                subtitle.enabled = true;
                return;
            }

            introWait -= dt;
            if (introWait > 0f) return;
            introIndex++;
            if (introIndex < IntroLines.Length)
            {
                subtitle.text = IntroLines[introIndex];
                subtitle.enabled = true;
                visibleFor = introSeconds;
                introWait = 0.25f;
                return;
            }

            subtitle.enabled = false;
            IsIntroPlaying = false;
            game.ApplyUiState();
            wait = DialogueInterval;
        }

        void TrackPlayerBehaviour(float dt)
        {
            if (game.Player == null) return;
            Vector3 current = game.Player.position;
            float moved = hasLastPlayerPosition ? Vector3.Distance(current, lastPlayerPosition) : 0f;
            lastPlayerPosition = current;
            hasLastPlayerPosition = true;
            if (moved > 0.005f)
            {
                walkedSinceLine += moved;
                idleFor = 0f;
            }
            else idleFor += dt;
        }

        public void OnPlayerPickup(TrashCategory category, bool dismantled)
        {
            if (game == null || IsIntroPlaying || game.IsUiBlocking || wait > 0f || visibleFor > 0f) return;
            string[] pool = dismantled && random.NextDouble() < 0.7
                ? DismantleLines : LinesFor(category);
            Show(Pick(pool));
            ResetWait();
        }

        public void OnDogFetch(TrashCategory category)
        {
            if (game == null || IsIntroPlaying || game.IsUiBlocking || wait > 0f || visibleFor > 0f) return;
            string[] pool = random.NextDouble() < 0.75 ? DogFetchLines : LinesFor(category);
            Show(Pick(pool));
            ResetWait();
        }

        public void OnDogScout()
        {
            if (game == null || IsIntroPlaying || game.IsUiBlocking || wait > 0f || visibleFor > 0f) return;
            Show(Pick(IdleLines));
            ResetWait();
        }

        string[] LinesFor(TrashCategory category)
        {
            switch (category)
            {
                case TrashCategory.Metal: return MetalLines;
                case TrashCategory.Organic: return OrganicLines;
                case TrashCategory.Electronic: return ElectronicLines;
                default: return PlasticLines;
            }
        }

        bool IsEarlyGame() => game.State.playTime < 180f || game.State.collected < 12;

        string Pick(string[] lines)
        {
            string line = lines[random.Next(lines.Length)];
            for (int i = 0; i < 5 && line == lastMessage && lines.Length > 1; i++)
                line = lines[random.Next(lines.Length)];
            lastMessage = line;
            return line;
        }

        void ResetWait()
        {
            wait = DialogueInterval;
        }

        void Show(string message)
        {
            if (subtitle == null) Build();
            subtitle.text = message.Replace("【你】", "你：").Replace("【狗狗】", "狗狗：");
            visibleFor = showSeconds;
            subtitle.enabled = true;
        }

        void Build()
        {
            var overlay = new GameObject("Gameplay Subtitles", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            overlay.transform.SetParent(transform, false);
            Canvas canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;

            var label = new GameObject("Subtitle", typeof(RectTransform), typeof(Text), typeof(Outline));
            label.transform.SetParent(overlay.transform, false);
            RectTransform rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0.10f, 0f);
            rect.anchorMax = new Vector2(0.90f, 0f);
            rect.offsetMin = new Vector2(0f, 86f);
            rect.offsetMax = new Vector2(0f, 145f);

            ownedFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "SimHei" }, 30);
            subtitle = label.GetComponent<Text>();
            subtitle.font = ownedFont != null ? ownedFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            subtitle.fontSize = 30;
            subtitle.fontStyle = FontStyle.Bold;
            subtitle.color = new Color(1f, 0.97f, 0.89f);
            subtitle.alignment = TextAnchor.MiddleCenter;
            subtitle.raycastTarget = false;

            Outline outline = label.GetComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.06f, 0.04f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        void OnDestroy()
        {
            if (ownedFont != null) Destroy(ownedFont);
        }
    }
}
