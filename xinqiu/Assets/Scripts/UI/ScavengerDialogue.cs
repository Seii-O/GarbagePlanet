using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>随玩家动作出现的单句字幕，不接收输入。</summary>
    public sealed class ScavengerDialogue : MonoBehaviour
    {
        [SerializeField, Min(0f)] float openingDelay = 1.2f;
        [SerializeField, Min(1f)] float minimumInterval = 18f;
        [SerializeField, Min(1f)] float maximumInterval = 40f;
        [SerializeField, Min(1f)] float showSeconds = 3f;

        static readonly string[][] PickupLines =
        {
            new[] { "这瓶子扁成这样，还挺省地方。", "捡起来才发现，瓶盖跑得比我快。", "轻是轻，响起来倒挺有存在感。" },
            new[] { "嘶，好冰。", "好响……全星球都知道我捡到了。", "这个有分量。我的肩膀也同意。" },
            new[] { "这个你别吃。上次的教训还不够吗？", "有机的。闻起来也很有说服力。", "嗯……这种东西还是别凑近看。" },
            new[] { "它还有电吗？算了，我也快没电了。", "别短路，我今天已经够忙了。", "好，至少有一个东西比我还需要充电。" },
            new[] { "终于拆开了。刚才到底是谁拧这么紧的？", "搞定。幸好它没长脚。", "我下次一定先挑个小的。" }
        };

        readonly System.Random random = new System.Random();
        readonly int[] lastLine = { -1, -1, -1, -1, -1 };
        WhiteboxGame game;
        Text subtitle;
        Font ownedFont;
        float wait, visibleFor, openingIn;
        float startDistance;
        int openingStep;
        bool pickedDuringOpening;

        public void Init(WhiteboxGame owner)
        {
            game = owner;
            openingStep = game.State.playTime <= 0f && game.State.collected == 0 ? 0 : 3;
            openingIn = openingDelay;
            startDistance = game.State.movedDistance;
            ResetWait();
        }

        void Update()
        {
            if (game == null) return;
            if (game.IsUiBlocking)
            {
                if (subtitle != null) subtitle.enabled = false;
                return;
            }

            float dt = Time.deltaTime;
            if (visibleFor > 0f)
            {
                visibleFor = Mathf.Max(0f, visibleFor - dt);
                subtitle.enabled = visibleFor > 0f;
                return;
            }

            switch (openingStep)
            {
                case 0:
                    openingIn -= dt;
                    if (openingIn <= 0f) OpeningLine("好，落地了。脚底下那声最好是石头。");
                    break;
                case 1:
                    if (game.State.movedDistance - startDistance >= 1f)
                        OpeningLine("跟紧我。踩到会响的，就装作没踩到。");
                    break;
                case 2:
                    if (pickedDuringOpening) OpeningLine("第一件。嗯……我是不是不该抬头看？");
                    break;
                default:
                    wait -= dt;
                    break;
            }
        }

        void OpeningLine(string line)
        {
            openingStep++;
            Show(line);
        }

        public void OnPlayerPickup(TrashCategory category, bool dismantled)
        {
            if (game == null || game.IsUiBlocking) return;
            if (openingStep < 3)
            {
                pickedDuringOpening = true;
                return;
            }
            if (visibleFor > 0f || wait > 0f) return;

            int group = dismantled ? PickupLines.Length - 1 : (int)category;
            string[] lines = PickupLines[group];
            int previous = lastLine[group];
            int index = random.Next(previous < 0 ? lines.Length : lines.Length - 1);
            if (previous >= 0 && index >= previous) index++;
            lastLine[group] = index;
            Show(lines[index]);
            ResetWait();
        }

        void ResetWait()
        {
            float max = Mathf.Max(minimumInterval, maximumInterval);
            wait = Mathf.Lerp(minimumInterval, max, (float)random.NextDouble());
        }

        void Show(string message)
        {
            if (subtitle == null) Build();
            subtitle.text = message;
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
            rect.offsetMax = new Vector2(0f, 135f);

            ownedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "SimHei" }, 30);
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
