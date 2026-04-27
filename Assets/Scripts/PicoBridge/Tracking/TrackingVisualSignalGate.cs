using System;
using System.Collections.Generic;
using Unity.XR.PXR;
using UnityEngine;
using UnityEngine.XR;
#if PICO_OPENXR_SDK
using Unity.XR.OpenXR.Features.PICOSupport;
#endif

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

        private readonly List<InputDevice> _devices = new List<InputDevice>();
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
                    return HasValidBodySignal();
                case TrackingVisualSignalSource.LeftHand:
#if !PICO_OPENXR_SDK
                    return HasValidHandSignal(HandType.HandLeft);
#else
                    return false;
#endif
                case TrackingVisualSignalSource.RightHand:
#if !PICO_OPENXR_SDK
                    return HasValidHandSignal(HandType.HandRight);
#else
                    return false;
#endif
                case TrackingVisualSignalSource.LeftController:
                    return HasValidControllerSignal(InputDeviceCharacteristics.Left);
                case TrackingVisualSignalSource.RightController:
                    return HasValidControllerSignal(InputDeviceCharacteristics.Right);
                default:
                    return false;
            }
#else
            return visibleInEditor;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool HasValidBodySignal()
        {
            var tracking = false;
            var status = new BodyTrackingStatus();
#if PICO_OPENXR_SDK
            BodyTrackingFeature.GetBodyTrackingState(ref tracking, ref status);
#else
            PXR_MotionTracking.GetBodyTrackingState(ref tracking, ref status);
#endif
            return tracking && status.stateCode == BodyTrackingStatusCode.BT_VALID;
        }

        private bool HasValidControllerSignal(InputDeviceCharacteristics hand)
        {
            _devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(hand | InputDeviceCharacteristics.Controller, _devices);

            foreach (var device in _devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && isTracked)
                    return true;

                if (device.TryGetFeatureValue(CommonUsages.trackingState, out var trackingState) &&
                    (trackingState & (InputTrackingState.Position | InputTrackingState.Rotation)) != 0)
                    return true;
            }

            return false;
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR && !PICO_OPENXR_SDK
        private static bool HasValidHandSignal(HandType handType)
        {
            var joints = new HandJointLocations();
            return PXR_HandTracking.GetJointLocations(handType, ref joints) &&
                   joints.isActive > 0 &&
                   joints.jointLocations != null &&
                   joints.jointCount > 0;
        }
#endif
    }
}
