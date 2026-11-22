// @trace TOOL-016
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 工具 / 技能书图标表的一键生成 + 接线（菜单⑲）。
    /// 做三件事：
    /// 1) 把 Assets/Art/UI/ToolIcons 下的 PNG 统一设为 Sprite（无 mipmap、512 上限、透明边缘）；
    /// 2) 用 Editor API 生成 Assets/Data/ToolIconSet.asset，键同时含 toolId 与 rewardId；
    /// 3) 把图标表接到场景里的 InventoryPanelView（背包格子显示图标）。
    /// 红线：不手写 .asset YAML；查不到图标只回退文字，不影响任何数值逻辑。
    /// </summary>
    public static class ToolIconSetBuilder
    {
        const string Tag = "[ToolIcon] ";
        public const string IconFolder = "Assets/Art/UI/ToolIcons";
        public const string BodyFolder = "Assets/Art/UI/ToolBodies";
        public const string SetPath = "Assets/Data/ToolIconSet.asset";
        const string ConfigPath = "Assets/Data/ToolConfig.asset";

        [MenuItem("Tools/垃圾星球白膜/⑲ 生成工具图标表（接入背包）", false, 30)]
        public static void BuildMenu()
        {
            int n = Build();
            if (Application.isBatchMode) return;
            EditorUtility.DisplayDialog("工具图标表",
                n > 0
                    ? "已生成 " + n + " 条图标映射：\n" + SetPath
                      + "\n\n图标已设为 Sprite，并接到背包格子（InventoryPanelView）。\n进 Play 按 I 打开背包即可看到。"
                    : "未找到任何图标 PNG。\n请确认 " + IconFolder + " 下有 ic_*.png。", "好");
        }

        public static int Build()
        {
            if (!Directory.Exists(IconFolder))
            {
                Debug.LogWarning(Tag + "图标目录不存在：" + IconFolder);
                return 0;
            }

            ToolConfig cfg = AssetDatabase.LoadAssetAtPath<ToolConfig>(ConfigPath);

            var ids = new List<string>();
            var sprites = new List<Sprite>();

            string[] files = Directory.GetFiles(IconFolder, "ic_*.png", SearchOption.TopDirectoryOnly);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                string assetPath = files[i].Replace('\\', '/');
                string fileName = Path.GetFileNameWithoutExtension(assetPath);   // ic_T-06 / ic_book_flow
                if (!fileName.StartsWith("ic_")) continue;
                string key = fileName.Substring(3);
                if (string.IsNullOrEmpty(key)) continue;

                Sprite sp = EnsureSprite(assetPath);
                if (sp == null)
                {
                    Debug.LogWarning(Tag + "无法导入为 Sprite：" + assetPath);
                    continue;
                }

                AddPair(ids, sprites, key, sp);

                // 工具额外登记 rewardId 键（背包格子里存的是 rewardId）
                if (cfg != null)
                {
                    ToolDef def = cfg.Find(key);
                    if (def != null && !string.IsNullOrEmpty(def.rewardId) && def.rewardId != key)
                        AddPair(ids, sprites, def.rewardId, sp);
                }
            }

            // 本体图登记为 body:xxx（图鉴大图等后续使用，不影响背包图标）
            if (Directory.Exists(BodyFolder))
            {
                string[] bodies = Directory.GetFiles(BodyFolder, "body_*.png", SearchOption.TopDirectoryOnly);
                System.Array.Sort(bodies, System.StringComparer.Ordinal);
                for (int i = 0; i < bodies.Length; i++)
                {
                    string assetPath = bodies[i].Replace('\\', '/');
                    string fileName = Path.GetFileNameWithoutExtension(assetPath);
                    if (!fileName.StartsWith("body_")) continue;
                    Sprite sp = EnsureSprite(assetPath);
                    if (sp != null) AddPair(ids, sprites, "body:" + fileName.Substring(5), sp);
                }
            }

            if (ids.Count == 0)
            {
                Debug.LogWarning(Tag + "没有可用的图标，未生成资产。");
                return 0;
            }

            ToolIconSet set = AssetDatabase.LoadAssetAtPath<ToolIconSet>(SetPath);
            if (set == null)
            {
                EnsureFolder("Assets/Data");
                set = ScriptableObject.CreateInstance<ToolIconSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.SetAll(ids.ToArray(), sprites.ToArray());
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            WireScene(set);

            Debug.Log(Tag + "已生成图标表：" + ids.Count + " 条 → " + SetPath);
            return ids.Count;
        }

        static void AddPair(List<string> ids, List<Sprite> sprites, string key, Sprite sp)
        {
            if (string.IsNullOrEmpty(key) || sp == null) return;
            int idx = ids.IndexOf(key);
            if (idx >= 0) { sprites[idx] = sp; return; }
            ids.Add(key);
            sprites.Add(sp);
        }

        /// <summary>确保 PNG 以 Sprite 形式导入（一次性改导入设置，已符合则跳过）。</summary>
        static Sprite EnsureSprite(string assetPath)
        {
            TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);

            bool dirty = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
            if (ti.maxTextureSize > 512) { ti.maxTextureSize = 512; dirty = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
            if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; dirty = true; }

            if (dirty)
            {
                ti.SaveAndReimport();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        static void WireScene(ToolIconSet set)
        {
            bool any = false;
            InventoryPanelView inv = Object.FindObjectOfType<InventoryPanelView>(true);
            if (inv != null) any |= WireOne(inv, set, "背包面板");
            GachaPanelView gacha = Object.FindObjectOfType<GachaPanelView>(true);
            if (gacha != null) any |= WireOne(gacha, set, "抽卡面板");
            if (!any)
                Debug.Log(Tag + "场景里没找到背包/抽卡面板（未生成？），图标表已生成但未接线。");
        }

        static bool WireOne(Component target, ToolIconSet set, string label)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty("iconSet");
            if (p == null)
            {
                Debug.LogWarning(Tag + label + " 缺少 iconSet 字段，请检查脚本是否已编译。");
                return false;
            }
            p.objectReferenceValue = set;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            if (target.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            Debug.Log(Tag + "已把图标表接到" + label + "：" + target.gameObject.name);
            return true;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
