using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoBridge.UI
{
    public class PicoBridgeServerListItem : MonoBehaviour
    {
        public Button selectButton;
        public TMP_Text endpointText;
        public TMP_Text ageText;

        private string _ip;
        private int _port;
        private Action<string, int> _onSelected;

        private void Awake()
        {
            if (selectButton != null)
                selectButton.onClick.AddListener(HandleSelected);
        }

        private void OnDestroy()
        {
            if (selectButton != null)
                selectButton.onClick.RemoveListener(HandleSelected);
        }

        public void Configure(string ip, int port, float ageSeconds, Action<string, int> onSelected)
        {
            _ip = ip;
            _port = port;
            _onSelected = onSelected;

            if (endpointText != null)
                endpointText.text = $"{ip}:{port}";
            if (ageText != null)
                ageText.text = ageSeconds < 1f ? "now" : $"{Mathf.FloorToInt(ageSeconds)}s";
        }

        private void HandleSelected()
        {
            _onSelected?.Invoke(_ip, _port);
        }
    }
}
