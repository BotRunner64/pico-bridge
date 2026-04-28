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
        private const string PrefabName = "PicoBridgePanel";
        private const string PrefabFolderPath = "Assets/Prefabs/PicoBridge";
        private const string PanelPrefabPath = PrefabFolderPath + "/PicoBridgePanel.prefab";

        private const float MinUiOpacity = 0.05f;
        private const string CollapseExpandedIcon = "▼";
        private const string EndpointPlaceholder = "Endpoint waiting";
        private static readonly Vector2 CanvasSize = new Vector2(1120f, 860f);
        private static readonly Color PanelColor = new Color(0.055f, 0.066f, 0.073f, 0.94f);
        private static readonly Color SurfaceColor = new Color(0.086f, 0.102f, 0.112f, 0.92f);
        private static readonly Color StrokeColor = new Color(0.36f, 0.45f, 0.49f, 0.34f);
        private static readonly Color BadgeColor = new Color(0.086f, 0.102f, 0.112f, 0.84f);
        private static readonly Color PreviewEmptyColor = new Color(0.018f, 0.023f, 0.026f, 0.72f);
        private static readonly Color DisconnectedColor = new Color(0.88f, 0.22f, 0.29f, 1f);
        private static readonly Color AccentColor = new Color(0.22f, 0.82f, 0.74f, 1f);
        private static readonly Color SignalInactiveColor = new Color(0.16f, 0.19f, 0.205f, 1f);
        private static readonly Color TextColor = new Color(0.94f, 0.975f, 0.985f, 1f);
        private static readonly Color MutedTextColor = new Color(0.66f, 0.72f, 0.75f, 1f);
        private static readonly string[] TrackingSignalLabels =
        {
            "HEAD",
            "L CTRL",
            "R CTRL",
            "L HAND",
            "R HAND",
            "BODY",
            "MOTION"
        };

        [MenuItem("PicoBridge/Rebuild Panel Prefab")]
        public static void RebuildPanelPrefabMenu()
        {
            RebuildPanelPrefab();
        }

        public static void RebuildSampleSceneUiTemplate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            InstallPanelPrefabInScene(scene, saveScene: true);
        }

        public static void RebuildPanelPrefab()
        {
            EnsurePrefabFolder();

            var root = BuildPanelRoot(parent: null, manager: null);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, PanelPrefabPath);
            Object.DestroyImmediate(root.gameObject);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PicoBridge] Panel prefab rebuilt: {PanelPrefabPath}");
        }

        public static void InstallPanelPrefabInScene(Scene scene, bool saveScene)
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

            var root = InstantiatePanelPrefab(canvas.transform, Object.FindObjectOfType<PicoBridgeManager>());

            Selection.activeGameObject = root != null ? root.gameObject : null;
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
                EditorSceneManager.SaveScene(scene);

            Debug.Log("[PicoBridge] Panel prefab instance rebuilt.");
        }

        private static RectTransform InstantiatePanelPrefab(Transform parent, PicoBridgeManager manager)
        {
            EnsurePanelPrefabExists();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[PicoBridge] Panel prefab missing after rebuild, falling back to direct scene UI: {PanelPrefabPath}");
                return BuildPanelRoot(parent, manager);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var root = instance.GetComponent<RectTransform>();
            SetStretch(root, 20f);
            AssignSceneReferences(instance, manager);
            SetLayerRecursively(instance, LayerMask.NameToLayer("UI"));
            return root;
        }

        private static RectTransform BuildPanelRoot(Transform parent, PicoBridgeManager manager)
        {
            var root = CreateRect(PrefabName, parent);
            SetStretch(root, 20f);
            var panelImage = AddImage(root.gameObject, PanelColor);
            AddOutline(panelImage, StrokeColor, new Vector2(1.5f, -1.5f));
            var rootCanvasGroup = AddRootCanvasGroup(root);

            var view = root.gameObject.AddComponent<PicoBridgePanelView>();
            view.rootCanvasGroup = rootCanvasGroup;
            view.panelImage = panelImage;
            var controller = root.gameObject.AddComponent<PicoBridgePanelController>();

            view.panelContentRoot = CreateRect("PanelContent", root);
            SetStretch(view.panelContentRoot, 0f);
            var contentLayout = AddVerticalLayout(view.panelContentRoot.gameObject, 26, 26, 24, 24, 14f);
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;

            BuildHeader(view.panelContentRoot, view);
            BuildPreview(view.panelContentRoot, view);
            BuildFooter(view.panelContentRoot, view);
            BuildCollapseBadge(root, view);
            AssignControllerReferences(controller, view, manager);
            SetLayerRecursively(root.gameObject, LayerMask.NameToLayer("UI"));
            return root;
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
                if (child.name == TemplateName || child.name == PrefabName || child.name == "Button" || child.name == "Scrollbar")
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PrefabFolderPath))
                AssetDatabase.CreateFolder("Assets/Prefabs", "PicoBridge");
        }

        private static void EnsurePanelPrefabExists()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath) == null)
                RebuildPanelPrefab();
        }

        private static void AssignSceneReferences(GameObject panelInstance, PicoBridgeManager manager)
        {
            var view = panelInstance.GetComponent<PicoBridgePanelView>();
            var controller = panelInstance.GetComponent<PicoBridgePanelController>();
            if (controller != null)
                AssignControllerReferences(controller, view, manager);
        }

        private static void BuildHeader(RectTransform parent, PicoBridgePanelView view)
        {
            var header = CreateRow("Connection", parent, 72f, 14f);

            var pill = CreateRect("ConnectionStatus", header);
            view.statusPillImage = AddImage(pill.gameObject, DisconnectedColor);
            AddOutline(view.statusPillImage, new Color(1f, 1f, 1f, 0.18f), new Vector2(1.5f, -1.5f));
            AddLayoutElement(pill.gameObject, -1f, 56f, 1f, 0f);

            view.statusPillText = CreateText("Label", pill, "Disconnected", 24, FontStyles.Bold, TextAlignmentOptions.Center, TextColor);
            SetStretch(view.statusPillText.rectTransform, 0f);

            var endpoint = CreateRect("EndpointStatus", header);
            var endpointImage = AddImage(endpoint.gameObject, SurfaceColor);
            AddOutline(endpointImage, StrokeColor, new Vector2(1.5f, -1.5f));
            AddLayoutElement(endpoint.gameObject, -1f, 56f, 1f, 0f);

            view.endpointText = CreateText("Label", endpoint, EndpointPlaceholder, 24, FontStyles.Bold, TextAlignmentOptions.Center, MutedTextColor);
            view.endpointText.enableWordWrapping = false;
            SetStretch(view.endpointText.rectTransform, 14f);
        }

        private static void BuildPreview(RectTransform parent, PicoBridgePanelView view)
        {
            var preview = CreateRect("CameraPreview", parent);
            view.cameraPreviewRoot = preview;
            var previewImage = AddImage(preview.gameObject, SurfaceColor);
            AddOutline(previewImage, StrokeColor, new Vector2(1.5f, -1.5f));
            AddLayoutElement(preview.gameObject, -1f, 540f, 1f, 1f);

            var feed = CreateRect("Feed", preview);
            SetStretch(feed, 8f);
            view.cameraPreviewImage = feed.gameObject.AddComponent<RawImage>();
            view.cameraPreviewImage.color = PreviewEmptyColor;
        }

        private static void BuildFooter(RectTransform parent, PicoBridgePanelView view)
        {
            var footer = CreateColumn("Signals", parent, 8f);
            var footerImage = AddImage(footer.gameObject, SurfaceColor);
            AddOutline(footerImage, StrokeColor, new Vector2(1.5f, -1.5f));
            var footerLayout = footer.GetComponent<VerticalLayoutGroup>();
            footerLayout.padding = new RectOffset(12, 12, 10, 10);
            AddLayoutElement(footer.gameObject, -1f, 132f, 0f, 0f);

            var tracking = CreateRow("TrackingSignals", footer, 50f, 8f);
            view.trackingSignalImages = new Image[TrackingSignalLabels.Length];
            view.trackingSignalLabels = new TMP_Text[TrackingSignalLabels.Length];
            for (int i = 0; i < TrackingSignalLabels.Length; i++)
                CreateSignalPill(tracking, view, i);

            var statusRow = CreateRow("StatusAndOpacity", footer, 42f, 12f);
            view.cameraStatusText = CreateText("CameraStatus", statusRow, "Camera idle", 18, FontStyles.Bold, TextAlignmentOptions.Left, MutedTextColor);
            view.cameraStatusText.enableWordWrapping = false;
            AddLayoutElement(view.cameraStatusText.gameObject, -1f, 34f, 1f, 0f);

            var opacityControl = CreateRow("OpacityControl", statusRow, 34f, 8f);
            AddLayoutElement(opacityControl.gameObject, -1f, 34f, 1f, 0f);
            var opacityLabel = CreateText("Label", opacityControl, "UI", 16, FontStyles.Bold, TextAlignmentOptions.Center, MutedTextColor);
            opacityLabel.enableWordWrapping = false;
            AddLayoutElement(opacityLabel.gameObject, 28f, 34f, 0f, 0f);
            view.uiOpacitySlider = CreateOpacitySlider(opacityControl);
        }

        private static void BuildCollapseBadge(RectTransform parent, PicoBridgePanelView view)
        {
            var badge = CreateRect("CollapseBadge", parent);
            badge.anchorMin = new Vector2(0.5f, 0f);
            badge.anchorMax = new Vector2(0.5f, 0f);
            badge.pivot = new Vector2(0.5f, 0f);
            badge.anchoredPosition = new Vector2(0f, 8f);
            badge.sizeDelta = new Vector2(54f, 34f);

            var image = AddImage(badge.gameObject, BadgeColor);
            AddOutline(image, StrokeColor, new Vector2(1.5f, -1.5f));
            var button = badge.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var layout = AddLayoutElement(badge.gameObject, 54f, 34f, 0f, 0f);
            layout.ignoreLayout = true;

            view.collapseButton = button;
            view.collapseButtonText = CreateText("Label", badge, CollapseExpandedIcon, 24, FontStyles.Bold, TextAlignmentOptions.Center, TextColor);
            view.collapseButtonText.enableWordWrapping = false;
            SetStretch(view.collapseButtonText.rectTransform, 0f);
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

        private static RectTransform CreateColumn(string name, RectTransform parent, float spacing)
        {
            var rect = CreateRect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return rect;
        }

        private static void CreateSignalPill(RectTransform parent, PicoBridgePanelView view, int index)
        {
            var pill = CreateRect(TrackingSignalLabels[index], parent);
            view.trackingSignalImages[index] = AddImage(pill.gameObject, SignalInactiveColor);
            AddOutline(view.trackingSignalImages[index], new Color(1f, 1f, 1f, 0.1f), new Vector2(1f, -1f));
            AddLayoutElement(pill.gameObject, -1f, 46f, 1f, 0f);

            view.trackingSignalLabels[index] = CreateText("Label", pill, TrackingSignalLabels[index], 16, FontStyles.Bold, TextAlignmentOptions.Center, MutedTextColor);
            view.trackingSignalLabels[index].enableWordWrapping = false;
            SetStretch(view.trackingSignalLabels[index].rectTransform, 0f);
        }

        private static Slider CreateOpacitySlider(RectTransform parent)
        {
            var sliderRect = CreateRect("OpacityBar", parent);
            AddLayoutElement(sliderRect.gameObject, -1f, 34f, 1f, 0f);

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.minValue = MinUiOpacity;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.wholeNumbers = false;
            slider.transition = Selectable.Transition.ColorTint;
            slider.colors = CreateControlColors();

            var background = CreateRect("Background", sliderRect);
            var backgroundImage = AddImage(background.gameObject, SignalInactiveColor);
            SetStretch(background, 0f);
            background.offsetMin = new Vector2(0f, 10f);
            background.offsetMax = new Vector2(0f, -10f);

            var fillArea = CreateRect("Fill Area", sliderRect);
            SetStretch(fillArea, 0f);
            fillArea.offsetMin = new Vector2(3f, 10f);
            fillArea.offsetMax = new Vector2(-3f, -10f);

            var fill = CreateRect("Fill", fillArea);
            var fillImage = AddImage(fill.gameObject, AccentColor);
            SetStretch(fill, 0f);

            var handleArea = CreateRect("Handle Slide Area", sliderRect);
            SetStretch(handleArea, 0f);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            var handle = CreateRect("Handle", handleArea);
            var handleImage = AddImage(handle.gameObject, TextColor);
            handle.sizeDelta = new Vector2(18f, 28f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            backgroundImage.raycastTarget = true;
            fillImage.raycastTarget = false;
            return slider;
        }

        private static ColorBlock CreateControlColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = TextColor;
            colors.highlightedColor = new Color(0.82f, 1f, 0.96f, 1f);
            colors.pressedColor = AccentColor;
            colors.selectedColor = TextColor;
            colors.disabledColor = new Color(0.42f, 0.46f, 0.48f, 0.45f);
            colors.colorMultiplier = 1f;
            return colors;
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
            ApplySlicedSprite(image);
            return image;
        }

        private static void AddOutline(Graphic graphic, Color color, Vector2 distance)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private static void ApplySlicedSprite(Image image)
        {
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null)
                return;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        private static CanvasGroup AddRootCanvasGroup(RectTransform root)
        {
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            return group;
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
            serialized.FindProperty("uiCollapsed").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
