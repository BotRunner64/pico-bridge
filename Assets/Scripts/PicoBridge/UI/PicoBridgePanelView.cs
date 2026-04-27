using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoBridge.UI
{
    public class PicoBridgePanelView : MonoBehaviour
    {
        [Header("Status")]
        public Image statusPillImage;
        public TMP_Text statusPillText;
        public TMP_Text subtitleText;
        public TMP_Text statusText;
        public TMP_Text serverSummaryText;

        [Header("Connection")]
        public TMP_InputField ipInput;
        public TMP_InputField portInput;
        public Button connectButton;
        public TMP_Text connectButtonLabel;
        public Button refreshButton;
        public RectTransform serverListContent;
        public GameObject emptyServerMessage;
        public PicoBridgeServerListItem serverListItemTemplate;

        [Header("Tracking")]
        public Toggle headToggle;
        public Toggle controllersToggle;
        public Toggle handsToggle;
        public Toggle bodyToggle;
        public Toggle motionToggle;

        [Header("Camera")]
        public Button cameraPreviewButton;
        public TMP_Text cameraPreviewButtonLabel;
        public RawImage cameraPreviewImage;
        public TMP_Text cameraStatusText;

        [Header("Diagnostics")]
        public TMP_Text diagnosticsText;
    }
}
