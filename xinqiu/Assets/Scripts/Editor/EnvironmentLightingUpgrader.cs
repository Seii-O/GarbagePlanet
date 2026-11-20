using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>Removes legacy station point lights; the scene now uses global ambient lighting.</summary>
    public static class EnvironmentLightingUpgrader
    {
        const string StationPrefab = "Assets/Prefabs/Whitebox/env_station_whitebox.prefab";

        [MenuItem("Trash Planet/Art/Apply Bright Environment Lighting")]
        public static void RemoveStationPointLights()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(StationPrefab);
            int removed = 0;
            try
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int i = lights.Length - 1; i >= 0; i--)
                {
                    if (lights[i] == null || lights[i].type != LightType.Point) continue;
                    Object.DestroyImmediate(lights[i].gameObject);
                    removed++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, StationPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[EnvironmentLighting] Removed " + removed
                      + " station point light(s); bright ambient lighting is now authoritative.");
        }
    }
}
