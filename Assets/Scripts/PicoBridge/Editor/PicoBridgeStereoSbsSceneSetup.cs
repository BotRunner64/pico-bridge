#if UNITY_EDITOR
using System;
using PicoBridge.Camera;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PicoBridge.Editor
{
    public static class PicoBridgeStereoSbsSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ScreenName = "StereoVideoScreen";
        private const string ShaderName = "PicoBridge/StereoSBS";
        private const string MaterialPath = "Assets/Shaders/PicoBridge/StereoSbs.mat";

        [MenuItem("PicoBridge/Install Stereo SBS Screen")]
        public static void InstallMenu()
        {
            InstallInScene(EditorSceneManager.GetActiveScene(), saveScene: false);
        }

        public static void InstallSampleSceneFromCommandLine()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!InstallInScene(scene, saveScene: true))
                throw new InvalidOperationException("Failed to install the stereo SBS screen.");
        }

        public static bool InstallInScene(Scene scene, bool saveScene)
        {
            var mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null || mainCamera.gameObject.scene != scene)
            {
                Debug.LogError("[PicoBridge] The active scene has no camera tagged MainCamera.");
                return false;
            }

            var manager = UnityEngine.Object.FindObjectOfType<PicoBridgeManager>();
            if (manager == null || manager.gameObject.scene != scene)
            {
                Debug.LogError("[PicoBridge] The active scene has no PicoBridgeManager.");
                return false;
            }

            var material = EnsureMaterial();
            if (material == null)
                return false;

            var screenTransform = mainCamera.transform.Find(ScreenName);
            GameObject screen;
            if (screenTransform == null)
            {
                screen = new GameObject(ScreenName);
                Undo.RegisterCreatedObjectUndo(screen, "Create stereo SBS screen");
                Undo.SetTransformParent(screen.transform, mainCamera.transform, false, "Parent stereo SBS screen");
            }
            else
            {
                screen = screenTransform.gameObject;
                Undo.RecordObject(screen.transform, "Configure stereo SBS screen");
            }

            screen.transform.localPosition = Vector3.forward;
            screen.transform.localRotation = Quaternion.identity;
            screen.transform.localScale = Vector3.one;

            var filter = screen.GetComponent<MeshFilter>();
            if (filter == null)
                filter = Undo.AddComponent<MeshFilter>(screen);
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            var renderer = screen.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = Undo.AddComponent<MeshRenderer>(screen);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = false;

            var display = screen.GetComponent<StereoSbsDisplay>();
            if (display == null)
                display = Undo.AddComponent<StereoSbsDisplay>(screen);

            var serializedDisplay = new SerializedObject(display);
            serializedDisplay.FindProperty("manager").objectReferenceValue = manager;
            serializedDisplay.FindProperty("targetRenderer").objectReferenceValue = renderer;
            serializedDisplay.FindProperty("displayCamera").objectReferenceValue = mainCamera;
            serializedDisplay.ApplyModifiedProperties();

            Selection.activeGameObject = screen;
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
                EditorSceneManager.SaveScene(scene);

            AssetDatabase.SaveAssets();
            Debug.Log("[PicoBridge] Stereo SBS screen installed under Main Camera.");
            return true;
        }

        private static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[PicoBridge] Shader not found: {ShaderName}");
                return null;
            }

            material = new Material(shader) { name = "StereoSbs" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
#endif
