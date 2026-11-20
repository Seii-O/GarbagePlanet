// @trace SAVE-001, SAVE-002
using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 5 槽 JSON 存档管理器。每槽独立文件：persistentDataPath/trash_planet_save_slot_N.json。
    ///
    /// 设计要点：
    /// - 槽位元数据（保存时间）与 WhiteboxState 一起写进同一个 JSON，显示时间不依赖文件时间戳。
    /// - 原子写入 .tmp → Delete → Move，避免中途崩溃写坏存档。
    /// - 兼容旧版单文件存档：首次运行时若 slot1 不存在但旧档存在，自动迁移到 slot1。
    /// - ActiveSlot=0 表示"新游戏尚未选择存档槽"，此时自动保存不落盘，直到玩家在局内选槽保存。
    /// - 玩家位置/视角由 WhiteboxGame 注册的 CapturePoseHook 在每次 Save 前写入 state。
    /// </summary>
    public static class SaveSlotSystem
    {
        public const int SlotCount = 5;
        public const int SlotVersion = 1;

        const string KeyActiveSlot = "tp.activeSlot";
        const string SlotFilePrefix = "trash_planet_save_slot_";
        const string SlotFileSuffix = ".json";
        const string LegacyFileName = "trash_planet_whitebox_save.json";

        [Serializable]
        sealed class SlotData
        {
            public int version = SlotVersion;
            public string savedAt = "";  // ISO 8601 (round-trip)
            public WhiteboxState state;
        }

        /// <summary>由 WhiteboxGame 在 Awake 注册；Save 前回调，把玩家位置/视角写进 state。</summary>
        public static Action<WhiteboxState> CapturePoseHook;

        public static int ActiveSlot
        {
            get
            {
                int v = PlayerPrefs.GetInt(KeyActiveSlot, 0);
                return v >= 1 && v <= SlotCount ? v : 0;
            }
            set
            {
                int v = value >= 1 && value <= SlotCount ? value : 0;
                PlayerPrefs.SetInt(KeyActiveSlot, v);
                PlayerPrefs.Save();
            }
        }

        public static bool IsValid(int slot) => slot >= 1 && slot <= SlotCount;

        public static string SlotPath(int slot) => Path.Combine(
            Application.persistentDataPath, SlotFilePrefix + slot + SlotFileSuffix);

        static string LegacyPath => Path.Combine(Application.persistentDataPath, LegacyFileName);

        // ------------------------------------------------------------------
        // 查询
        // ------------------------------------------------------------------

        public static bool Exists(int slot) => IsValid(slot) && File.Exists(SlotPath(slot));

        /// <summary>返回用于按钮显示的时间文本；无档返回空串。读不到时间时退回文件修改时间。</summary>
        public static string SavedAtText(int slot)
        {
            if (!Exists(slot)) return "";
            try
            {
                SlotData d = JsonUtility.FromJson<SlotData>(File.ReadAllText(SlotPath(slot)));
                if (d != null && !string.IsNullOrEmpty(d.savedAt))
                {
                    DateTime dt;
                    if (DateTime.TryParse(d.savedAt, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out dt))
                        return dt.ToString("yyyy-MM-dd HH:mm");
                    return d.savedAt;
                }
            }
            catch { }
            try { return File.GetLastWriteTime(SlotPath(slot)).ToString("yyyy-MM-dd HH:mm"); }
            catch { return ""; }
        }

        // ------------------------------------------------------------------
        // 读 / 写 / 删
        // ------------------------------------------------------------------

        /// <summary>读指定槽；无档、坏档、版本不符都返回全新状态（不抛异常）。</summary>
        public static WhiteboxState Load(int slot)
        {
            if (!Exists(slot)) return new WhiteboxState();
            try
            {
                SlotData d = JsonUtility.FromJson<SlotData>(File.ReadAllText(SlotPath(slot)));
                if (d == null || d.version != SlotVersion || d.state == null) return new WhiteboxState();
                return WhiteboxState.NormalizeLoaded(d.state);
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSlot] 读取槽 " + slot + " 失败：" + e.Message);
                return new WhiteboxState();
            }
        }

        public static WhiteboxState LoadActive()
        {
            int slot = ActiveSlot;
            return slot > 0 ? Load(slot) : new WhiteboxState();
        }

        /// <summary>写入指定槽并把 ActiveSlot 指向它。写失败不改变 ActiveSlot。</summary>
        public static void Save(int slot, WhiteboxState s)
        {
            if (!IsValid(slot) || s == null) return;
            try
            {
                if (CapturePoseHook != null) CapturePoseHook(s);
                SlotData d = new SlotData
                {
                    version = SlotVersion,
                    savedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                    state = s
                };
                string path = SlotPath(slot);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                ActiveSlot = slot;
                Debug.Log("[SaveSlot] 已保存到槽 " + slot + "：" + path);
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSlot] 保存槽 " + slot + " 失败：" + e.Message);
            }
        }

        /// <summary>所有既有自动保存入口走这里：未选槽时只留在内存，不落盘。</summary>
        public static void SaveActive(WhiteboxState s)
        {
            int slot = ActiveSlot;
            if (slot == 0) return;
            Save(slot, s);
        }

        public static void Delete(int slot)
        {
            if (!IsValid(slot)) return;
            try { if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot)); }
            catch (Exception e) { Debug.LogWarning("[SaveSlot] 删除槽 " + slot + " 失败：" + e.Message); }
            if (ActiveSlot == slot) ActiveSlot = 0;
        }

        public static void DeleteActive()
        {
            int slot = ActiveSlot;
            if (slot > 0) Delete(slot);
            ActiveSlot = 0;
        }

        // ------------------------------------------------------------------
        // 旧档迁移（一次性、非破坏性）
        // ------------------------------------------------------------------

        /// <summary>旧版单文件存档迁到 slot1；不删除旧文件，避免误删用户数据。</summary>
        public static void EnsureLegacyMigrated()
        {
            if (Exists(1) || !File.Exists(LegacyPath)) return;
            try
            {
                WhiteboxState old = JsonUtility.FromJson<WhiteboxState>(File.ReadAllText(LegacyPath));
                if (old == null) return;
                SlotData d = new SlotData
                {
                    version = SlotVersion,
                    savedAt = File.GetLastWriteTime(LegacyPath).ToString("o", CultureInfo.InvariantCulture),
                    state = WhiteboxState.NormalizeLoaded(old)
                };
                string path = SlotPath(1);
                File.WriteAllText(path + ".tmp", JsonUtility.ToJson(d, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);
                Debug.Log("[SaveSlot] 已把旧版单文件存档迁移到槽 1");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSlot] 旧档迁移失败（不影响新系统）：" + e.Message);
            }
        }
    }
}
