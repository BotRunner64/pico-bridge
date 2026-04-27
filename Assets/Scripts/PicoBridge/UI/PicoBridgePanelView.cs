using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoBridge.UI
{
    public class PicoBridgePanelView : MonoBehaviour
    {
        [Header("Connection")]
        public Image statusPillImage;
        public TMP_Text statusPillText;
        public TMP_Text endpointText;

        [Header("Tracking")]
        public Image trackingStatusImage;
        public TMP_Text trackingStatusText;

        [Header("Camera")]
        public RawImage cameraPreviewImage;
        public TMP_Text cameraStatusText;
    }
}
