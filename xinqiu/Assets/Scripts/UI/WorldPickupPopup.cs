using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>World-space pickup feedback: fade in 0.25 s, hold 1 s, fade out 0.25 s.</summary>
    public sealed class WorldPickupPopup : MonoBehaviour
    {
        const float FadeSeconds = 0.25f;
        const float HoldSeconds = 1f;

        static readonly string[] CategoryLootNames =
        {
            "塑料废料", "金属废料", "有机废料", "电子废料"
        };
        static Font cachedFont;

        Transform cameraTransform;
        CanvasGroup group;
        Vector3 surfaceUp;

        public static void Show(TrashItem item, Camera camera, Transform planetCenter,
            TrashCategory category, int amount)
        {
            if (item == null || camera == null || amount <= 0) return;

            Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
            Vector3 up = item.transform.position - center;
            if (up.sqrMagnitude < 0.0001f) up = item.transform.up;
            up.Normalize();

            Vector3 popupPosition = PositionAbove(item, up);
            var root = new GameObject("PickupPopup_" + category);
            // Hide the transient world-space Canvas from Unity editor gizmos. Without this,
            // Game View draws a white dashed Canvas rectangle whenever Gizmos are enabled.
            root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            root.transform.position = popupPosition;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.hideFlags = HideFlags.HideInInspector;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.hideFlags = HideFlags.HideInInspector;
            canvasRect.sizeDelta = new Vector2(460f, 76f);
            canvasRect.localScale = Vector3.one * 0.0042f;

            CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(root.transform, false);
            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text text = textGo.AddComponent<Text>();
            text.font = CreateChineseFont();
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = CategoryColor(category);
            text.text = "获得" + CategoryLootNames[Mathf.Clamp((int)category, 0, CategoryLootNames.Length - 1)]
                        + "+" + amount;
            text.raycastTarget = false;

            Outline outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.05f, 0.04f, 0.03f, 0.92f);
            outline.effectDistance = new Vector2(2f, -2f);

            WorldPickupPopup popup = root.AddComponent<WorldPickupPopup>();
            popup.cameraTransform = camera.transform;
            popup.group = canvasGroup;
            popup.surfaceUp = up;
            popup.StartCoroutine(popup.Play());
        }

        static Vector3 PositionAbove(TrashItem item, Vector3 up)
        {
            Vector3 origin = item.transform.position;
            float top = 0.12f;
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds b = renderer.bounds;
                float projectedExtent = Mathf.Abs(up.x) * b.extents.x
                                      + Mathf.Abs(up.y) * b.extents.y
                                      + Mathf.Abs(up.z) * b.extents.z;
                float projectedTop = Vector3.Dot(b.center - origin, up) + projectedExtent;
                if (projectedTop > top) top = projectedTop;
            }
            return origin + up * (top + 0.22f);
        }

        static Font CreateChineseFont()
        {
            if (cachedFont != null) return cachedFont;
            string[] preferred = { "Microsoft YaHei", "SimHei", "Arial Unicode MS", "Arial" };
            cachedFont = Font.CreateDynamicFontFromOSFont(preferred, 34);
            if (cachedFont == null)
                cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return cachedFont;
        }

        static Color CategoryColor(TrashCategory category)
        {
            switch (category)
            {
                case TrashCategory.Plastic: return new Color(0.38f, 0.90f, 1f, 1f);
                case TrashCategory.Metal: return new Color(0.92f, 0.94f, 0.96f, 1f);
                case TrashCategory.Organic: return new Color(0.65f, 0.92f, 0.42f, 1f);
                case TrashCategory.Electronic: return new Color(1f, 0.76f, 0.30f, 1f);
                default: return Color.white;
            }
        }

        void LateUpdate()
        {
            if (cameraTransform == null) return;
            // Canvas forward faces the camera; surfaceUp keeps text upright relative to the planet.
            Vector3 toCamera = cameraTransform.position - transform.position;
            if (toCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, surfaceUp);
        }

        IEnumerator Play()
        {
            yield return Fade(0f, 1f, FadeSeconds);
            float hold = 0f;
            while (hold < HoldSeconds)
            {
                hold += Time.unscaledDeltaTime;
                transform.position += surfaceUp * (0.055f * Time.unscaledDeltaTime);
                yield return null;
            }
            yield return Fade(1f, 0f, FadeSeconds);
            Destroy(gameObject);
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                group.alpha = Mathf.SmoothStep(from, to, t);
                transform.position += surfaceUp * (0.055f * Time.unscaledDeltaTime);
                yield return null;
            }
            group.alpha = to;
        }
    }
}
