using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoBridge.UI
{
    public class PicoBridgePanelView : MonoBehaviour
    {
        [Header("Root")]
        public CanvasGroup rootCanvasGroup;
        public Image panelImage;
        public RectTransform panelContentRoot;

        [Header("Connection")]
        public Image statusPillImage;
        public TMP_Text statusPillText;
        public TMP_Text endpointText;

        [Header("Tracking")]
        public Image[] trackingSignalImages;
        public TMP_Text[] trackingSignalLabels;

        [Header("Camera")]
        public RawImage cameraPreviewImage;
        public TMP_Text cameraStatusText;

        [Header("Controls")]
        public Slider uiOpacitySlider;
        public Button collapseButton;
        public TMP_Text collapseButtonText;
    }
}
