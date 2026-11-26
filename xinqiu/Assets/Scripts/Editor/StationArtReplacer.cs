using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 只替换中转站的可见白膜，不动根节点、碰撞体、场景引用和交互位置。
    /// RecyclePlant 以子物体形式保存在场景中，因此编辑态与 Play 模式使用同一套美术。
    /// </summary>
    public static class StationArtReplacer
    {
        const string ArtPath = "Assets/Art/RecyclePlant.fbx";
        const string StationName = "env_station_whitebox";
        const string ArtVisualName = "Art Visual";
        const string Tag = "[StationArtReplacer] ";

        static readonly Vector3[] BoundsCorners =
        {
            new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, 1f),
            new Vector3(-1f,  1f, -1f), new Vector3(-1f,  1f, 1f),
            new Vector3( 1f, -1f, -1f), new Vector3( 1f, -1f, 1f),
            new Vector3( 1f,  1f, -1f), new Vector3( 1f,  1f, 1f)
        };

        [MenuItem("Tools/垃圾星球白膜/⑯d 用 RecyclePlant 替换选中中转站", false, 24)]
        public static void ReplaceSelectedStation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + "请退出 Play 模式后再替换中转站美术");
                return;
            }

            GameObject station = ResolveStation();
            if (station == null)
            {
                Debug.LogError(Tag + "请先选中场景中的 " + StationName);
                return;
            }

            GameObject artAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath);
            if (artAsset == null)
            {
                Debug.LogError(Tag + "找不到美术资源：" + ArtPath);
                return;
            }

            Renderer[] legacyRenderers = LegacyRenderers(station.transform);
            if (legacyRenderers.Length == 0)
            {
                Debug.LogError(Tag + "中转站没有可用于测量占地的原白膜 Renderer");
                return;
            }
            Bounds targetBounds = LocalBounds(station.transform, legacyRenderers);

            Transform existing = station.transform.Find(ArtVisualName);
            GameObject visual;
            if (existing != null)
            {
                visual = existing.gameObject;
                visual.SetActive(true);
            }
            else
            {
                visual = PrefabUtility.InstantiatePrefab(artAsset, station.transform) as GameObject;
                if (visual == null)
                {
                    Debug.LogError(Tag + "实例化 RecyclePlant 失败");
                    return;
                }
                visual.name = ArtVisualName;
            }

            Undo.RegisterCreatedObjectUndo(visual, "Replace station art with RecyclePlant");
            FitToStation(station.transform, visual.transform, targetBounds);

            // 原白膜继续保留碰撞体和层级，只关闭渲染；新美术的碰撞体不参与玩法，避免改变交互范围。
            for (int i = 0; i < legacyRenderers.Length; i++) legacyRenderers[i].enabled = false;
            Collider[] artColliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < artColliders.Length; i++) artColliders[i].enabled = false;

            EditorUtility.SetDirty(station);
            EditorSceneManager.MarkSceneDirty(station.scene);
            EditorSceneManager.SaveScene(station.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = station;
            Debug.Log(Tag + station.name + " 已替换为 " + ArtPath
                + "；保留原碰撞与交互，关闭白膜 Renderer=" + legacyRenderers.Length);
        }

        static GameObject ResolveStation()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                Transform t = selected.transform;
                while (t.parent != null && t.name != StationName) t = t.parent;
                if (t.name == StationName && t.gameObject.scene.IsValid()) return t.gameObject;
            }
            return GameObject.Find(StationName);
        }

        static Renderer[] LegacyRenderers(Transform station)
        {
            Transform art = station.Find(ArtVisualName);
            var result = new List<Renderer>();
            Renderer[] all = station.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
                if (art == null || !all[i].transform.IsChildOf(art)) result.Add(all[i]);
            return result.ToArray();
        }

        static void FitToStation(Transform station, Transform visual, Bounds target)
        {
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            Bounds source = LocalBounds(station, renderers);
            float scaleX = target.size.x / Mathf.Max(0.0001f, source.size.x);
            float scaleZ = target.size.z / Mathf.Max(0.0001f, source.size.z);
            float scale = Mathf.Min(scaleX, scaleZ);
            visual.localScale = Vector3.one * scale;

            source = LocalBounds(station, renderers);
            visual.localPosition += new Vector3(
                target.center.x - source.center.x,
                target.min.y - source.min.y,
                target.center.z - source.center.z);
        }

        static Bounds LocalBounds(Transform root, Renderer[] renderers)
        {
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds b = renderers[i].bounds;
                for (int c = 0; c < BoundsCorners.Length; c++)
                {
                    Vector3 world = b.center + Vector3.Scale(b.extents, BoundsCorners[c]);
                    Vector3 local = root.InverseTransformPoint(world);
                    if (!initialized) { result = new Bounds(local, Vector3.zero); initialized = true; }
                    else result.Encapsulate(local);
                }
            }
            return result;
        }
    }
}
