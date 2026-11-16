// @trace ART-004
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 统一字体入口：优先用工程内的中文字体（Assets/Art/Fonts/ 下的任意 .ttf/.otf），
    /// 找不到才退回 Unity 内置的 LegacyRuntime.ttf。
    ///
    /// 为什么要这一层：
    /// 1. 内置 LegacyRuntime.ttf（Liberation Sans）没有中文字形，中文实际是走系统字体回退渲染的，
    ///    小字号下笔画粗细不稳、边缘发灰，看起来就是「糊」；
    /// 2. 想换字体时只要把字体文件丢进 Assets/Art/Fonts/（例如思源黑体 Noto Sans SC / Source Han Sans，
    ///    OFL 授权可商用），重跑一次重建菜单即可，不用改代码、不用逐个 Text 拖引用。
    /// </summary>
    public static class UiFont
    {
        const string FontDir = "Assets/Art/Fonts/";

        static Font cached;
        static bool looked;

        public static Font Load()
        {
            if (looked) return cached;
            looked = true;

            cached = FromProject();
            if (cached != null) return cached;

            try
            {
                cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (cached != null) return cached;
            }
            catch { /* Unity 2022 之前没有 LegacyRuntime.ttf */ }

            try { cached = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { cached = null; }
            return cached;
        }

        /// <summary>工程内 Assets/Art/Fonts/ 下的第一个字体资源。</summary>
        static Font FromProject()
        {
            string[] guids = AssetDatabase.FindAssets("t:Font");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.StartsWith(FontDir)) continue;
                Font f = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (f != null) return f;
            }
            return null;
        }
    }
}
