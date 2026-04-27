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

        private static readonly Color PanelColor = new Color(0.035f, 0.041f, 0.052f, 0.92f);
        private static readonly Color CardColor = new Color(0.078f, 0.089f, 0.11f, 0.94f);
        private static readonly Color CardAltColor = new Color(0.10f, 0.115f, 0.14f, 0.94f);
        private static readonly Color PrimaryColor = new Color(0.11f, 0.46f, 0.93f, 1f);
        private static readonly Color WarningColor = new Color(1f, 0.69f, 0.18f, 1f);
        private static readonly Color MutedColor = new Color(0.70f, 0.76f, 0.84f, 1f);

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
            RemoveTemplateAndTestChildren(canvas.transform);

            var root = CreateRect(TemplateName, canvas.transform);
            SetStretch(root, 20f);
            AddImage(root.gameObject, PanelColor);
            var rootLayout = AddVerticalLayout(root.gameObject, 18, 18, 18, 14, 12f);
            rootLayout.childControlHeight = true;
            rootLayout.childControlWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childForceExpandWidth = true;

            var view = root.gameObject.AddComponent<PicoBridgePanelView>();
            var controller = root.gameObject.AddComponent<PicoBridgePanelController>();
            AssignControllerReferences(controller, view, Object.FindObjectOfType<PicoBridgeManager>());

            BuildHeader(root, view);
            BuildContent(root, view);
            BuildFooter(root);
            SetLayerRecursively(root.gameObject, LayerMask.NameToLayer("UI"));

            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene)
                EditorSceneManager.SaveScene(scene);

            Debug.Log("[PicoBridge] Scene UI template rebuilt.");
        }

        private static void EnsureCanvasInputComponents(Canvas canvas)
        {
            if (canvas.renderMode != RenderMode.WorldSpace)
                canvas.renderMode = RenderMode.WorldSpace;
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
            var header = CreateRow("Header", parent, 70f, 12f);
            AddLayoutElement(header.gameObject, -1f, 70f, 0f);

            var titleGroup = CreateRect("TitleGroup", header);
            var titleLayout = AddVerticalLayout(titleGroup.gameObject, 0, 0, 0, 0, 0f);
            titleLayout.childControlHeight = false;
            titleLayout.childControlWidth = true;
            AddLayoutElement(titleGroup.gameObject, -1f, 70f, 1f);

            CreateText("Title", titleGroup, "PICO Bridge", 38, FontStyles.Bold, TextAlignmentOptions.Left, Color.white, 40f);
            view.subtitleText = CreateText("Subtitle", titleGroup, "Connect to a PC receiver on the same network", 18, FontStyles.Normal, TextAlignmentOptions.Left, MutedColor, 26f);

            var pill = CreateRect("StatusPill", header);
            view.statusPillImage = AddImage(pill.gameObject, WarningColor);
            AddLayoutElement(pill.gameObject, 172f, 46f, 0f);
            view.statusPillText = CreateText("StatusPillLabel", pill, "Disconnected", 19, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, 46f);
            SetStretch(view.statusPillText.rectTransform, 0f);
        }

        private static void BuildContent(RectTransform parent, PicoBridgePanelView view)
        {
            var content = CreateRow("Content", parent, 410f, 12f);
            AddLayoutElement(content.gameObject, -1f, 410f, 1f);

            var left = CreateColumn("LeftColumn", content, 10f);
            AddLayoutElement(left.gameObject, -1f, 410f, 1f);
            var right = CreateColumn("RightColumn", content, 10f);
            AddLayoutElement(right.gameObject, -1f, 410f, 1.08f);

            BuildConnectionCard(left, view);
            BuildTrackingCard(left, view);
            BuildCameraCard(right, view);
            BuildDiagnosticsCard(right, view);
        }

        private static void BuildConnectionCard(RectTransform parent, PicoBridgePanelView view)
        {
            var card = CreateCard("ConnectionCard", parent, 250f, CardColor);
            CreateCardTitle(card, "Connection");
            view.statusText = CreateText("StatusText", card, "Disconnected", 20, FontStyles.Bold, TextAlignmentOptions.Left, Color.white, 26f);
            view.serverSummaryText = CreateText("ServerSummary", card, "No PC receiver discovered yet", 16, FontStyles.Normal, TextAlignmentOptions.Left, MutedColor, 24f);

            var serverHeader = CreateRow("ServerHeader", card, 34f, 8f);
            CreateText("ServerListTitle", serverHeader, "Discovered PCs", 17, FontStyles.Bold, TextAlignmentOptions.Left, Color.white, 32f).GetComponent<LayoutElement>().flexibleWidth = 1f;
            view.refreshButton = CreateButton("RefreshButton", serverHeader, "Refresh", 16, 96f, 34f, new Color(1f, 1f, 1f, 0.10f), out _);

            view.serverListContent = CreateColumn("ServerListContent", card, 6f);
            AddLayoutElement(view.serverListContent.gameObject, -1f, 78f, 0f);
            view.emptyServerMessage = CreateText("EmptyServerMessage", view.serverListContent, "Listening for UDP discovery...", 16, FontStyles.Normal, TextAlignmentOptions.Center, MutedColor, 36f).gameObject;
            view.serverListItemTemplate = CreateServerListItemTemplate(view.serverListContent);

            var addressRow = CreateRow("AddressRow", card, 42f, 8f);
            view.ipInput = CreateInputField("IpInput", addressRow, "192.168.1.100", 1f, -1f);
            view.portInput = CreateInputField("PortInput", addressRow, "63901", 0f, 104f);
            view.connectButton = CreateButton("ConnectButton", card, "Connect", 19, -1f, 44f, PrimaryColor, out view.connectButtonLabel);
        }

        private static void BuildTrackingCard(RectTransform parent, PicoBridgePanelView view)
        {
            var card = CreateCard("TrackingCard", parent, 150f, CardAltColor);
            CreateCardTitle(card, "Tracking Streams");
            var rowA = CreateRow("TrackingRowA", card, 46f, 8f);
            view.headToggle = CreateToggle("HeadToggle", rowA, "Head", true);
            view.controllersToggle = CreateToggle("ControllersToggle", rowA, "Controllers", true);
            view.handsToggle = CreateToggle("HandsToggle", rowA, "Hands", true);

            var rowB = CreateRow("TrackingRowB", card, 46f, 8f);
            view.bodyToggle = CreateToggle("BodyToggle", rowB, "Body", false);
            view.motionToggle = CreateToggle("MotionToggle", rowB, "Motion", false);
        }

        private static void BuildCameraCard(RectTransform parent, PicoBridgePanelView view)
        {
            var card = CreateCard("CameraCard", parent, 296f, CardColor);
            var titleRow = CreateRow("CameraTitleRow", card, 40f, 10f);
            CreateText("CameraTitle", titleRow, "Camera Preview", 22, FontStyles.Bold, TextAlignmentOptions.Left, Color.white, 38f).GetComponent<LayoutElement>().flexibleWidth = 1f;
            view.cameraPreviewButton = CreateButton("PreviewButton", titleRow, "Preview", 17, 118f, 38f, PrimaryColor, out view.cameraPreviewButtonLabel);

            var previewFrame = CreateRect("PreviewFrame", card);
            AddLayoutElement(previewFrame.gameObject, -1f, 190f, 0f);
            view.cameraPreviewImage = previewFrame.gameObject.AddComponent<RawImage>();
            view.cameraPreviewImage.color = new Color(0f, 0f, 0f, 0.72f);

            view.cameraStatusText = CreateText("CameraStatus", card, "Connect to PC before preview", 16, FontStyles.Normal, TextAlignmentOptions.Left, MutedColor, 24f);
        }

        private static void BuildDiagnosticsCard(RectTransform parent, PicoBridgePanelView view)
        {
            var card = CreateCard("DiagnosticsCard", parent, 114f, CardAltColor);
            CreateCardTitle(card, "Diagnostics", 20);
            view.diagnosticsText = CreateText("DiagnosticsText", card, "UDP: not available\nTCP: None\nServer: none\nDiscovered: 0\nCamera: idle", 14, FontStyles.Normal, TextAlignmentOptions.TopLeft, MutedColor, 74f);
        }

        private static void BuildFooter(RectTransform parent)
        {
            var footer = CreateRect("FooterHint", parent);
            AddLayoutElement(footer.gameObject, -1f, 24f, 0f);
            CreateText("FooterText", footer, "Editor: mouse input enabled   |   PICO: use controller ray", 14, FontStyles.Normal, TextAlignmentOptions.Center, new Color(0.58f, 0.65f, 0.74f, 1f), 24f);
        }

        private static RectTransform CreateCard(string name, RectTransform parent, float height, Color color)
        {
            var card = CreateColumn(name, parent, 8f);
            AddImage(card.gameObject, color);
            AddLayoutElement(card.gameObject, -1f, height, 0f);
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            return card;
        }

        private static TMP_Text CreateCardTitle(RectTransform parent, string text, int size = 22)
        {
            return CreateText("Title", parent, text, size, FontStyles.Bold, TextAlignmentOptions.Left, Color.white, 30f);
        }

        private static Button CreateButton(string name, RectTransform parent, string label, int fontSize, float width, float height, Color color, out TMP_Text labelText)
        {
            var rect = CreateRect(name, parent);
            var image = AddImage(rect.gameObject, color);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            AddLayoutElement(rect.gameObject, width, height, width < 0f ? 1f : 0f);

            labelText = CreateText("Label", rect, label, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, height);
            SetStretch(labelText.rectTransform, 0f);
            return button;
        }

        private static Toggle CreateToggle(string name, RectTransform parent, string label, bool value)
        {
            var rect = CreateRect(name, parent);
            var image = AddImage(rect.gameObject, value ? new Color(0.12f, 0.78f, 0.38f, 1f) : new Color(1f, 1f, 1f, 0.12f));
            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = image;
            toggle.isOn = value;
            AddLayoutElement(rect.gameObject, -1f, 46f, 1f);

            var labelText = CreateText("Label", rect, label, 15, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, 46f);
            SetStretch(labelText.rectTransform, 0f);
            return toggle;
        }

        private static TMP_InputField CreateInputField(string name, RectTransform parent, string value, float flexibleWidth, float width)
        {
            var rect = CreateRect(name, parent);
            var image = AddImage(rect.gameObject, new Color(1f, 1f, 1f, 0.12f));
            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = image;
            AddLayoutElement(rect.gameObject, width, 42f, flexibleWidth);

            var viewport = CreateRect("TextArea", rect);
            SetStretch(viewport, 10f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = CreateText("Text", viewport, value, 16, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, Color.white, 30f);
            SetStretch(text.rectTransform, 0f);
            var placeholder = CreateText("Placeholder", viewport, value, 16, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, new Color(1f, 1f, 1f, 0.38f), 30f);
            SetStretch(placeholder.rectTransform, 0f);

            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.text = value;
            return field;
        }

        private static PicoBridgeServerListItem CreateServerListItemTemplate(RectTransform parent)
        {
            var rect = CreateRow("ServerListItemTemplate", parent, 38f, 8f);
            var image = AddImage(rect.gameObject, new Color(0.12f, 0.17f, 0.22f, 0.96f));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            AddLayoutElement(rect.gameObject, -1f, 38f, 0f);

            var item = rect.gameObject.AddComponent<PicoBridgeServerListItem>();
            item.selectButton = button;
            item.endpointText = CreateText("Endpoint", rect, "192.168.1.100:63901", 15, FontStyles.Bold, TextAlignmentOptions.Left, Color.white, 34f);
            item.endpointText.GetComponent<LayoutElement>().flexibleWidth = 1f;
            item.ageText = CreateText("Age", rect, "now", 13, FontStyles.Normal, TextAlignmentOptions.Right, MutedColor, 34f, 54f);
            rect.gameObject.SetActive(false);
            return item;
        }

        private static RectTransform CreateColumn(string name, RectTransform parent, float spacing)
        {
            var rect = CreateRect(name, parent);
            var layout = AddVerticalLayout(rect.gameObject, 0, 0, 0, 0, spacing);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return rect;
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
            AddLayoutElement(rect.gameObject, -1f, height, 0f);
            return rect;
        }

        private static TMP_Text CreateText(string name, RectTransform parent, string text, int fontSize, FontStyles style, TextAlignmentOptions alignment, Color color, float height, float width = -1f)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            AddLayoutElement(rect.gameObject, width, height, width < 0f ? 0f : 0f);
            return label;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private static Image AddImage(GameObject gameObject, Color color)
        {
            var image = gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static VerticalLayoutGroup AddVerticalLayout(GameObject gameObject, int left, int right, int top, int bottom, float spacing)
        {
            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(left, right, top, bottom);
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return layout;
        }

        private static LayoutElement AddLayoutElement(GameObject gameObject, float width, float height, float flexibleWidth)
        {
            var element = gameObject.GetComponent<LayoutElement>();
            if (element == null)
                element = gameObject.AddComponent<LayoutElement>();
            if (width > 0f)
                element.preferredWidth = width;
            if (height > 0f)
            {
                element.preferredHeight = height;
                element.minHeight = height;
            }
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = 0f;
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
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
