using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PicoBridge.UI
{
    /// <summary>
    /// Configures world-space Canvas input for controller rays on device and mouse input in Editor.
    /// The visible bridge panel is owned by PicoBridgePanelController.
    /// </summary>
    public class PicoBridgeUI : MonoBehaviour
    {
        [Header("Scene UI Input")]
        [SerializeField] private bool configureWorldSpaceCanvasInput = true;
        [SerializeField] private bool enableMouseInputInEditor = true;
        [SerializeField] private bool enableTouchInputInEditor;

        private void Start()
        {
            if (configureWorldSpaceCanvasInput)
                ConfigureWorldSpaceCanvasInput();
        }

        private void ConfigureWorldSpaceCanvasInput()
        {
            EnsureXrEventSystem();

            var canvases = FindObjectsOfType<Canvas>(true);
            foreach (var canvas in canvases)
            {
                if (canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (trackedRaycaster == null)
                    trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                trackedRaycaster.enabled = true;

                var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (Application.isEditor && enableMouseInputInEditor)
                {
                    if (graphicRaycaster == null)
                        graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                    graphicRaycaster.enabled = true;
                }
                else if (graphicRaycaster != null)
                    graphicRaycaster.enabled = false;
            }
        }

        private void EnsureXrEventSystem()
        {
            var eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
            {
                if (module is XRUIInputModule)
                    continue;

                Destroy(module);
            }

            var xrUiInputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (xrUiInputModule == null)
                xrUiInputModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();

            xrUiInputModule.activeInputMode = XRUIInputModule.ActiveInputMode.InputSystemActions;
            xrUiInputModule.enableXRInput = true;
            xrUiInputModule.enableMouseInput = Application.isEditor && enableMouseInputInEditor;
            xrUiInputModule.enableTouchInput = Application.isEditor && enableTouchInputInEditor;
            xrUiInputModule.enableBuiltinActionsAsFallback = true;
        }
    }
}
