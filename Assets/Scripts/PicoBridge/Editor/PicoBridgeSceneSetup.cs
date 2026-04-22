#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoBridge;
using PicoBridge.UI;

namespace PicoBridge.Editor
{
    public static class PicoBridgeSceneSetup
    {
        [MenuItem("PicoBridge/Setup Scene")]
        public static void SetupScene()
        {
            // Find or create PicoBridge root
            var root = GameObject.Find("PicoBridge");
            if (root == null)
            {
                root = new GameObject("PicoBridge");
                Undo.RegisterCreatedObjectUndo(root, "Create PicoBridge");
            }

            // Add manager
            if (root.GetComponent<PicoBridgeManager>() == null)
                Undo.AddComponent<PicoBridgeManager>(root);

            // Add UI
            if (root.GetComponent<PicoBridgeUI>() == null)
                Undo.AddComponent<PicoBridgeUI>(root);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[PicoBridge] Scene setup complete. Configure server address in Inspector.");
        }

        [MenuItem("PicoBridge/Validate Project Settings")]
        public static void ValidateSettings()
        {
            bool allGood = true;

            // Check scripting backend
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
            {
                Debug.LogWarning("[PicoBridge] Android scripting backend should be IL2CPP");
                allGood = false;
            }

            // Check min SDK
            if ((int)PlayerSettings.Android.minSdkVersion < 29)
            {
                Debug.LogWarning("[PicoBridge] Android min SDK should be >= 29");
                allGood = false;
            }

            // Check internet permission
            if (!PlayerSettings.Android.forceInternetPermission)
            {
                Debug.LogWarning("[PicoBridge] Internet permission not enabled");
                allGood = false;
            }

            // Check application identifier
            string appId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (appId.Contains("UnityTechnologies") || appId.Contains("template"))
            {
                Debug.LogWarning($"[PicoBridge] Application identifier looks like a template: {appId}");
                allGood = false;
            }

            if (allGood)
                Debug.Log("[PicoBridge] All project settings look good!");
        }
    }
}
#endif
