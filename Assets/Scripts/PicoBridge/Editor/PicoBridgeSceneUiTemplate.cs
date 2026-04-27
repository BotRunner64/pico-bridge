#if UNITY_EDITOR
using PicoBridge.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PicoBridge.Editor
{
    public static class PicoBridgeSceneUiTemplate
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string CanvasName = "[Building Block] Controller Canvas Interaction Canvas";
        private const string TemplateName = "PicoBridge Panel Template";

        private static readonly Vector2 CanvasSize = new Vector2(1120f, 820f);
        private static readonly Color PanelColor = new Color(0.035f, 0.041f, 0.052f, 0.94f);
        private static readonly Color PreviewEmptyColor = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color DisconnectedColor = new Color(0.95f, 0.22f, 0.22f, 1f);
        private static readonly Color MutedTextColor = new Color(0.70f, 0.76f, 0.84f, 1f);

        [MenuItem("PicoBridge/Rebuild Scene UI Template")]
        public static void RebuildOpenSceneUiTemplate()
        {
            RebuildSceneUiTemplate(EditorSceneManager.GetActiveScene(), saveScene: false);
        }

        public static void RebuildSampleSceneUiTemplate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RebuildSceneUiTemplate(scene, saveScene: true);
        }

        private static void RebuildSceneUiTemplate(Scene scene, bool saveScene)
        {
            var canvasObject = GameObject.Find(CanvasName);
            if (canvasObject == null)
            {
                Debug.LogError($"[PicoBridge] Canvas not found: {CanvasName}");
                return;
            }

            var canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError($"[PicoBridge] {CanvasName} has no Canvas component.");
                return;
            }

            EnsureCanvasInputComponents(canvas);
            ConfigureCanvas(canvas);
            RemoveTemplateAndTestChildren(canvas.transform);

            var root = CreateRect(TemplateName, canvas.transform);
            SetStretch(root, 20f);
            AddImage(root.gameObject, PanelColor);
            var rootLayout = AddVerticalLayout(root.gameObject, 18, 18, 18, 18, 14f);
            rootLayout.childControlHeight = true;
            rootLayout.childControlWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childForceExpandWidth = true;

            var view = root.gameObject.AddComponent<PicoBridgePanelView>();
            var controller = root.gameObject.AddComponent<PicoBridgePanelController>();
            AssignControllerReferences(controller, view, Object.FindObjectOfType<PicoBridgeManager>());

            BuildHeader(root, view);
            BuildPreview(root, view);
            BuildFooter(root, view);
            SetLayerRecursively(root.gameObject, LayerMask.NameToLayer("UI"));

            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
                EditorSceneManager.SaveScene(scene);

            Debug.Log("[PicoBridge] Compact scene UI template rebuilt.");
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas.renderMode != RenderMode.WorldSpace)
                canvas.renderMode = RenderMode.WorldSpace;

            var rect = canvas.GetComponent<RectTransform>();
            if (rect != null)
                rect.sizeDelta = CanvasSize;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
                scaler.dynamicPixelsPerUnit = Mathf.Max(scaler.dynamicPixelsPerUnit, 12f);
        }

        private static void EnsureCanvasInputComponents(Canvas canvas)
        {
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void RemoveTemplateAndTestChildren(Transform canvasTransform)
        {
            for (int i = canvasTransform.childCount - 1; i >= 0; i--)
            {
                var child = canvasTransform.GetChild(i);
                if (child.name == TemplateName || child.name == "Button" || child.name == "Scrollbar")
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void BuildHeader(RectTransform parent, PicoBridgePanelView view)
        {
            var header = CreateRow("Connection", parent, 66f, 14f);

            var pill = CreateRect("ConnectionStatus", header);
            view.statusPillImage = AddImage(pill.gameObject, DisconnectedColor);
            AddLayoutElement(pill.gameObject, 210f, 54f, 0f, 0f);

            view.statusPillText = CreateText("Label", pill, "Disconnected", 24, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            SetStretch(view.statusPillText.rectTransform, 0f);

            view.endpointText = CreateText("Endpoint", header, "", 30, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            view.endpointText.enableWordWrapping = false;
            AddLayoutElement(view.endpointText.gameObject, -1f, 54f, 1f, 0f);
        }

        private static void BuildPreview(RectTransform parent, PicoBridgePanelView view)
        {
            var preview = CreateRect("CameraPreview", parent);
            AddLayoutElement(preview.gameObject, -1f, 588f, 1f, 1f);

            view.cameraPreviewImage = preview.gameObject.AddComponent<RawImage>();
            view.cameraPreviewImage.color = PreviewEmptyColor;
        }

        private static void BuildFooter(RectTransform parent, PicoBridgePanelView view)
        {
            var footer = CreateRow("Signals", parent, 58f, 18f);

            var tracking = CreateRow("Tracking", footer, 52f, 10f);
            AddLayoutElement(tracking.gameObject, -1f, 52f, 1f, 0f);
            view.trackingStatusImage = CreateSignalDot("Dot", tracking, DisconnectedColor);
            view.trackingStatusText = CreateText("TrackingStatus", tracking, "Tracking idle", 22, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            AddLayoutElement(view.trackingStatusText.gameObject, -1f, 44f, 1f, 0f);

            view.cameraStatusText = CreateText("CameraStatus", footer, "Camera idle", 22, FontStyles.Bold, TextAlignmentOptions.Right, MutedTextColor);
            view.cameraStatusText.enableWordWrapping = false;
            AddLayoutElement(view.cameraStatusText.gameObject, 300f, 52f, 0f, 0f);
        }

        private static RectTransform CreateRow(string name, RectTransform parent, float height, float spacing)
        {
            var rect = CreateRect(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            AddLayoutElement(rect.gameObject, -1f, height, 0f, 0f);
            return rect;
        }

        private static Image CreateSignalDot(string name, RectTransform parent, Color color)
        {
            var dot = CreateRect(name, parent);
            var image = AddImage(dot.gameObject, color);
            AddLayoutElement(dot.gameObject, 18f, 18f, 0f, 0f);
            return image;
        }

        private static TMP_Text CreateText(string name, RectTransform parent, string text, int fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private static Image AddImage(GameObject target, Color color)
        {
            var image = target.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static VerticalLayoutGroup AddVerticalLayout(GameObject target, int left, int right, int top, int bottom, float spacing)
        {
            var layout = target.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(left, right, top, bottom);
            layout.spacing = spacing;
            return layout;
        }

        private static LayoutElement AddLayoutElement(GameObject target, float width, float height, float flexibleWidth, float flexibleHeight)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null)
                element = target.AddComponent<LayoutElement>();

            if (width > 0f)
                element.preferredWidth = width;
            if (height > 0f)
            {
                element.preferredHeight = height;
                element.minHeight = height;
            }

            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        private static void SetStretch(RectTransform rectTransform, float inset)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(inset, inset);
            rectTransform.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            if (layer >= 0)
                gameObject.layer = layer;

            foreach (Transform child in gameObject.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static void AssignControllerReferences(PicoBridgePanelController controller, PicoBridgePanelView view, PicoBridgeManager manager)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("view").objectReferenceValue = view;
            serialized.FindProperty("manager").objectReferenceValue = manager;
            serialized.FindProperty("rebuildCompactLayoutOnStart").boolValue = false;
            serialized.FindProperty("compactCanvasSize").vector2Value = CanvasSize;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
