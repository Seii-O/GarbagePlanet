using UnityEditor;
using UnityEditor.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    [InitializeOnLoad]
    static class TitlePlayModeStartup
    {
        static TitlePlayModeStartup()
        {
            EditorApplication.delayCall += SetStartupScene;
        }

        static void SetStartupScene()
        {
            SceneAsset title = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleBuilder.ScenePath);
            if (title != null) EditorSceneManager.playModeStartScene = title;
        }
    }
}
