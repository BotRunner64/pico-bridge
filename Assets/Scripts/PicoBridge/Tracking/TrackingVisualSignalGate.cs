using System;
using UnityEngine;

namespace PicoBridge.Tracking
{
    public enum TrackingVisualSignalSource
    {
        Body,
        LeftHand,
        RightHand,
        LeftController,
        RightController
    }

    /// <summary>
    /// Keeps debug tracking meshes invisible until the matching device reports valid tracking.
    /// </summary>
    public class TrackingVisualSignalGate : MonoBehaviour
    {
        [SerializeField] private TrackingVisualSignalSource signalSource;
        [SerializeField] private bool visibleInEditor;
        [SerializeField] private bool disableColliders = true;
        [SerializeField] private float refreshInterval = 0.1f;

        private Renderer[] _renderers = Array.Empty<Renderer>();
        private Collider[] _colliders = Array.Empty<Collider>();
        private bool _visible = true;
        private bool _hasValidSignal;
        private float _nextRefreshTime;

        public void Configure(TrackingVisualSignalSource source)
        {
            signalSource = source;
            RefreshTargets();
            HideImmediately();
        }

        private void Awake()
        {
            RefreshTargets();
            HideImmediately();
        }

        private void OnEnable()
        {
            RefreshTargets();
            _nextRefreshTime = 0f;
            HideImmediately();
        }

        private void OnTransformChildrenChanged()
        {
            RefreshTargets();
            ApplyVisibility(_hasValidSignal, true);
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
                _hasValidSignal = HasValidSignal();
            }

            ApplyVisibility(_hasValidSignal, false);
        }

        private void RefreshTargets()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = disableColliders
                ? GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
        }

        private void HideImmediately()
        {
            _hasValidSignal = false;
            ApplyVisibility(false, true);
        }

        private void ApplyVisibility(bool visible, bool force)
        {
            if (!force && _visible == visible)
                return;

            _visible = visible;

            foreach (var rendererComponent in _renderers)
            {
                if (rendererComponent != null)
                    rendererComponent.enabled = visible;
            }

            foreach (var colliderComponent in _colliders)
            {
                if (colliderComponent != null)
                    colliderComponent.enabled = visible;
            }
        }

        private bool HasValidSignal()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            switch (signalSource)
            {
                case TrackingVisualSignalSource.Body:
                    return TrackingSignalStatus.HasValidSignal(TrackingSignalKind.Body);
                case TrackingVisualSignalSource.LeftHand:
                    return TrackingSignalStatus.HasValidSignal(TrackingSignalKind.LeftHand);
                case TrackingVisualSignalSource.RightHand:
                    return TrackingSignalStatus.HasValidSignal(TrackingSignalKind.RightHand);
                case TrackingVisualSignalSource.LeftController:
                    return TrackingSignalStatus.HasValidSignal(TrackingSignalKind.LeftController);
                case TrackingVisualSignalSource.RightController:
                    return TrackingSignalStatus.HasValidSignal(TrackingSignalKind.RightController);
                default:
                    return false;
            }
#else
            return visibleInEditor;
#endif
        }

    }
}
