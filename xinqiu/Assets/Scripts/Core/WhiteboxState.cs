// @trace NUM-001, NUM-004, NUM-007
using System;
using System.IO;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    public enum TrashCategory { Plastic = 0, Metal = 1, Organic = 2, Electronic = 3 }

    /// <summary>
    /// 运行时状态与全部可复算公式。数值不写死，一律来自 WhiteboxConfig。
    /// 存档为单文件 JSON（策划案 §4.2 / §12），原子写入 + 版本字段 + 读失败回退。
    /// </summary>
    [Serializable]
    public sealed class WhiteboxState
    {
        const string SaveFileName = "trash_planet_whitebox_save.json";

        public int version = 1;
        public int[] bag = new int[4];        // 背包：4 类垃圾
        public int[] raw = new int[4];        // 中转站：已存入待加工的垃圾
        public int[] materials = new int[4];  // 再生塑料 / 金属零件 / 有机原料 / 电子元件
        public int toolLevel = 1;
        public float landProgress;
        public float waterProgress;
        public int collected;
        public int processed;
        public int crafted;
        public int dismantled;
        public int boards;
        public int fertilizer;
        public int filters;
        public int processedMask;
        public float playTime;
        public bool stationRepaired;
        public bool visitedStation;
        public float movedDistance;
        public int questStep;
        public bool finished;

        /// <summary>NUM-001：collected = min(batch, capacityLeft[cat])，0 时目标不消失。</summary>
        public int Collect(WhiteboxConfig cfg, int cat, int batch)
        {
            int index = Mathf.Clamp(cat, 0, 3);
            int left = Mathf.Max(0, cfg.BackpackCap[index] - bag[index]);
            int n = Mathf.Max(0, Mathf.Min(Mathf.Max(0, batch), left));
            if (n <= 0) return 0;
            bag[index] += n;
            collected += n;
            return n;
        }

        public bool CanCollect(WhiteboxConfig cfg, int cat)
        {
            int index = Mathf.Clamp(cat, 0, 3);
            return bag[index] < cfg.BackpackCap[index];
        }

        /// <summary>NUM-004：progress = min(1.0, p + items × 0.10)，负值钳到 0，溢出钳到 1。</summary>
        public static float AddProgress(WhiteboxConfig cfg, float current, int items)
        {
            return Mathf.Clamp01(Mathf.Max(0f, current) + Mathf.Max(0, items) * cfg.RepairPerItem);
        }

        public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static WhiteboxState Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return new WhiteboxState();
                WhiteboxState s = JsonUtility.FromJson<WhiteboxState>(File.ReadAllText(SavePath));
                if (s == null || s.version != 1) return new WhiteboxState();
                s.bag = Fix(s.bag);
                s.raw = Fix(s.raw);
                s.materials = Fix(s.materials);
                return s;
            }
            catch (Exception e)
            {
                Debug.LogError("[WhiteboxState] 读档失败，回退新局：" + e.Message);
                return new WhiteboxState();
            }
        }

        public static void Save(WhiteboxState s)
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s, true));
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
                Debug.Log("[WhiteboxState] 已存档：" + SavePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[WhiteboxState] 存档失败：" + e.Message);
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning("[WhiteboxState] 删档失败：" + e.Message); }
        }

        static int[] Fix(int[] a)
        {
            if (a != null && a.Length == 4) return a;
            var r = new int[4];
            if (a != null) Array.Copy(a, r, Mathf.Min(a.Length, 4));
            return r;
        }
    }
}
