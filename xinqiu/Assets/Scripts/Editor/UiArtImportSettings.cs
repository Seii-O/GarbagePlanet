// @trace ART-004, ART-008
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 把 UI 贴图统一导入成 Sprite（2D and UI），并按「9 宫格拆件」写入 border。
    ///
    /// 为什么需要这一步：资源库里的 PNG 默认是 Texture 导入（textureType=Default），
    /// AssetDatabase.LoadAssetAtPath&lt;Sprite&gt; 拿到的是 null，Image 只能画成实心矩形；
    /// 面板/按钮不写 border 就无法在拉伸后保持圆角与描边。
    ///
    /// 菜单：Tools/垃圾星球白膜/⑥ 应用 UI 贴图导入设置
    /// 美术以后往 中转站皮肤/ 里丢新图，重跑一次本菜单即可（同名的 9 宫格边距沿用表里的值）。
    /// </summary>
    public static class UiArtImportSettings
    {
        public const string SkinFolder = "Assets/Art/UI/中转站皮肤";
        // UI-007：美术把整页 UI 与拆件导入到带「 1」后缀的新库；旧的无后缀目录已不存在。
        // 保留旧常量名，指向实际存在的新库，避免改动所有调用点。
        public const string LibraryFolder = "Assets/Art/UI/垃圾星球_UI资源库 1";
        /// <summary>结束画面（游戏结束 / 结算）用的整图背景与封面，全图 Sprite、不切九宫格。</summary>
        public const string GameOverFolder = "Assets/Art/UI/结束画面";
        const string Tag = "[UiArtImportSettings] ";

        /// <summary>需要 9 宫格拉伸的拆件：名字 → 四边边距（px）。表里没有的一律按普通 Sprite 处理。</summary>
        static readonly Dictionary<string, int> Sliced = new Dictionary<string, int>
        {
            // 旧库兼容名（保留，避免其它场景引用时丢失边距）
            { "pn_主面板", 24 }, { "pn_列表行_亮", 12 }, { "cp_状态徽章", 18 },
            { "pg_条_底", 6 }, { "pg_滚动条底", 6 },
            // 新库「垃圾星球_UI资源库 1」：边距来自 06_UI界面拆件/九宫格参数.json
            { "pn_面板_大", 44 }, { "pn_面板_大_氛围", 44 }, { "pn_面板_小", 44 },
            { "pn_卡片", 34 }, { "pn_HUD卡", 32 }, { "pn_HUD卡_小", 28 },
            { "pn_列表行", 20 }, { "pn_高亮行", 20 },
            { "pn_头部条", 24 }, { "pn_底部操作条", 24 },
            { "bt_主按钮", 32 }, { "bt_主按钮_按下", 32 },
            { "bt_次按钮", 32 }, { "bt_次按钮_按下", 32 },
            { "bt_危险按钮", 32 }, { "bt_幽灵按钮", 32 }, { "bt_禁用按钮", 32 },
            { "bt_小按钮_主", 24 }, { "bt_小按钮_次", 24 }, { "bt_小按钮_幽灵", 24 },
            { "bt_关闭方键", 10 },
            { "cp_页签_选中", 26 }, { "cp_页签_未选中", 26 }, { "cp_页签条底", 20 },
            { "cp_状态徽章_正常", 22 }, { "cp_状态徽章_警告", 22 }, { "cp_状态徽章_离线", 22 },
            { "cp_目标标牌", 25 }, { "cp_标签_琥珀", 20 }, { "cp_数量角标底", 20 },
            { "pg_进度条_底", 8 }, { "pg_耐久条框", 30 },
            { "ct_格子_普通", 30 }, { "ct_格子_选中", 30 }, { "ct_格子_高亮", 30 },
            { "ct_格子_空", 30 }, { "ct_格子_锁定", 30 },
            { "ct_背包格_小", 14 }, { "ct_背包格_小_选中", 14 },
            { "hud_提示条_成功", 26 }, { "hud_提示条_失败", 26 },
        };

        [MenuItem("Tools/垃圾星球白膜/⑥ 应用 UI 贴图导入设置", false, 15)]
        public static void ApplyMenu()
        {
            int n = Apply();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("UI 贴图导入设置",
                    "已处理 " + n + " 张贴图（Sprite + 9 宫格边距）。\n\n"
                    + "美术换图：直接覆盖同名 PNG，或把新 Sprite 拖到对应 Image 的 Source Image 上。", "好");
        }

        /// <summary>批处理入口。返回实际改动的贴图数量。</summary>
        public static void ApplyBatch()
        {
            int n = Apply();
            Debug.Log(Tag + "batch 完成，改动 " + n + " 张");
        }

        public static int Apply()
        {
            int changed = 0;
            changed += ApplyFolder(SkinFolder);
            changed += ApplyFolder(LibraryFolder);
            changed += ApplyFolder(GameOverFolder);
            if (changed > 0) AssetDatabase.SaveAssets();
            Debug.Log(Tag + "导入设置更新 " + changed + " 张");
            return changed;
        }

        static int ApplyFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning(Tag + "目录不存在：" + folder);
                return 0;
            }
            string[] files = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories);
            int changed = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                if (Configure(ti, Path.GetFileNameWithoutExtension(path))) changed++;
            }
            return changed;
        }

        static bool Configure(TextureImporter ti, string name)
        {
            bool dirty = false;
            dirty |= Set(ti.textureType != TextureImporterType.Sprite,
                () => ti.textureType = TextureImporterType.Sprite);
            dirty |= Set(ti.spriteImportMode != SpriteImportMode.Single,
                () => ti.spriteImportMode = SpriteImportMode.Single);
            dirty |= Set(ti.mipmapEnabled, () => ti.mipmapEnabled = false);
            dirty |= Set(!ti.alphaIsTransparency, () => ti.alphaIsTransparency = true);
            dirty |= Set(ti.filterMode != FilterMode.Bilinear, () => ti.filterMode = FilterMode.Bilinear);
            dirty |= Set(ti.wrapMode != TextureWrapMode.Clamp, () => ti.wrapMode = TextureWrapMode.Clamp);
            dirty |= Set(ti.textureCompression != TextureImporterCompression.Uncompressed,
                () => ti.textureCompression = TextureImporterCompression.Uncompressed);
            dirty |= Set(ti.maxTextureSize < 2048, () => ti.maxTextureSize = 2048);
            dirty |= Set(!Mathf.Approximately(ti.spritePixelsPerUnit, 100f),
                () => ti.spritePixelsPerUnit = 100f);

            int border;
            Sliced.TryGetValue(name, out border);
            TextureImporterSettings s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            Vector4 want = new Vector4(border, border, border, border);
            if (s.spriteBorder != want)
            {
                s.spriteBorder = want;
                ti.SetTextureSettings(s);
                dirty = true;
            }
            // 进度环 / 图标这类非矩形贴图按 Tight 网格会更省顶点，但 UI 一律 FullRect 更可控
            if (s.spriteMeshType != SpriteMeshType.FullRect)
            {
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
                dirty = true;
            }

            if (dirty) ti.SaveAndReimport();
            return dirty;
        }

        static bool Set(bool condition, System.Action action)
        {
            if (!condition) return false;
            action();
            return true;
        }
    }
}
