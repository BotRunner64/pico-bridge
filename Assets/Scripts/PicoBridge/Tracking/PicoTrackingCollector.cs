using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.PXR;

namespace PicoBridge.Tracking
{
    /// <summary>
    /// Collects tracking data from PICO native APIs and serializes to XRobo-compatible JSON.
    /// </summary>
    public class PicoTrackingCollector
    {
        private const int BodyJointCount = (int)BodyTrackerRole.ROLE_NUM;

        public bool HeadEnabled = true;
        public bool ControllerEnabled = true;
        public bool HandTrackingEnabled = true;
        public bool BodyTrackingEnabled = false;
        public bool MotionTrackerEnabled = false;

        private readonly StringBuilder _sb = new StringBuilder(4096);

        /// <summary>
        /// Collect all enabled tracking data and return as JSON string.
        /// </summary>
        public string CollectJson()
        {
            _sb.Clear();
            _sb.Append('{');

            double predictTimeMs = PXR_System.GetPredictedDisplayTime();
            long predictTimeUs = (long)(predictTimeMs * 1000.0);
            _sb.Append($"\"predictTime\":{predictTimeUs}");

            // App state
            _sb.Append(",\"appState\":{\"focus\":true}");

            // Head
            if (HeadEnabled)
                AppendHead(predictTimeMs);

            // Controllers
            if (ControllerEnabled)
                AppendControllers(predictTimeMs);

            // Hands
            if (HandTrackingEnabled)
                AppendHands();

            // Body
            if (BodyTrackingEnabled)
                AppendBody();

            // Motion trackers
            if (MotionTrackerEnabled)
                AppendMotion();

            // Input mask + timestamp
            _sb.Append(",\"Input\":0");
            long tsNs = (long)(Time.realtimeSinceStartupAsDouble * 1_000_000_000);
            _sb.Append($",\"timeStampNs\":{tsNs}");

            _sb.Append('}');
            return _sb.ToString();
        }

        // ── Head ──────────────────────────────────────────

        private void AppendHead(double predictTimeMs)
        {
            PxrSensorState2 state = default;
            int frameIdx = 0;
            PXR_System.GetPredictedMainSensorStateNew(ref state, ref frameIdx);

            Vector3 p = ToUnityPosition(
                state.pose.position.x,
                state.pose.position.y,
                state.pose.position.z);
            Quaternion r = ToUnityRotation(
                state.pose.orientation.x,
                state.pose.orientation.y,
                state.pose.orientation.z,
                state.pose.orientation.w);
            _sb.Append(",\"Head\":{\"pose\":\"");
            AppendPose(p.x, p.y, p.z, r.x, r.y, r.z, r.w);
            _sb.Append($"\",\"status\":{state.status}}}");
        }

        // ── Controllers ───────────────────────────────────

        private void AppendControllers(double predictTimeMs)
        {
            _sb.Append(",\"Controller\":{");
            AppendController("left", PXR_Input.Controller.LeftController,
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, predictTimeMs);
            _sb.Append(',');
            AppendController("right", PXR_Input.Controller.RightController,
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, predictTimeMs);
            _sb.Append('}');
        }

        private void AppendController(string side, PXR_Input.Controller ctrl, InputDeviceCharacteristics chars, double predictTimeMs)
        {
            Vector3 pos = PXR_Input.GetControllerPredictPosition(ctrl, predictTimeMs);
            Quaternion rot = PXR_Input.GetControllerPredictRotation(ctrl, predictTimeMs);
            pos = ToUnityPosition(pos.x, pos.y, pos.z);
            rot = ToUnityRotation(rot.x, rot.y, rot.z, rot.w);

            _sb.Append($"\"{side}\":{{\"pose\":\"");
            AppendPose(pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w);
            _sb.Append('"');

            // Read input via Unity InputDevices (same approach as XRobo)
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(chars, devices);
            if (devices.Count > 0)
            {
                var dev = devices[0];
                dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis2D);
                dev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out var axisClick);
                dev.TryGetFeatureValue(CommonUsages.grip, out var grip);
                dev.TryGetFeatureValue(CommonUsages.trigger, out var trigger);
                dev.TryGetFeatureValue(CommonUsages.primaryButton, out var primaryButton);
                dev.TryGetFeatureValue(CommonUsages.secondaryButton, out var secondaryButton);
                dev.TryGetFeatureValue(CommonUsages.menuButton, out var menuButton);

                _sb.Append(",\"axisX\":");
                AppendJsonNumber(axis2D.x);
                _sb.Append(",\"axisY\":");
                AppendJsonNumber(axis2D.y);
                _sb.Append($",\"axisClick\":{BoolStr(axisClick)}");
                _sb.Append(",\"grip\":");
                AppendJsonNumber(grip);
                _sb.Append(",\"trigger\":");
                AppendJsonNumber(trigger);
                _sb.Append($",\"primaryButton\":{BoolStr(primaryButton)}");
                _sb.Append($",\"secondaryButton\":{BoolStr(secondaryButton)}");
                _sb.Append($",\"menuButton\":{BoolStr(menuButton)}");
            }

            _sb.Append('}');
        }

        // ── Hands ─────────────────────────────────────────

        private void AppendHands()
        {
            _sb.Append(",\"Hand\":{");
            AppendHand("leftHand", HandType.HandLeft);
            _sb.Append(',');
            AppendHand("rightHand", HandType.HandRight);
            _sb.Append('}');
        }

        private void AppendHand(string key, HandType handType)
        {
            HandJointLocations joints = default;
            bool ok = PXR_HandTracking.GetJointLocations(handType, ref joints);

            _sb.Append($"\"{key}\":{{\"isActive\":{BoolStr(ok && joints.isActive > 0)}");
            _sb.Append($",\"count\":{(ok ? joints.jointCount : 0)}");
            _sb.Append(",\"scale\":");
            AppendJsonNumber(ok ? joints.handScale : 1f);

            if (ok && joints.isActive > 0 && joints.jointLocations != null)
            {
                _sb.Append(",\"HandJointLocations\":[");
                for (int i = 0; i < joints.jointLocations.Length; i++)
                {
                    if (i > 0) _sb.Append(',');
                    var j = joints.jointLocations[i];
                    _sb.Append("{\"p\":\"");
                    AppendPose(
                        j.pose.Position.x, j.pose.Position.y, j.pose.Position.z,
                        j.pose.Orientation.x, j.pose.Orientation.y, j.pose.Orientation.z, j.pose.Orientation.w);
                    _sb.Append($"\",\"s\":{(ulong)j.locationStatus},\"r\":");
                    AppendJsonNumber(j.radius);
                    _sb.Append('}');
                }
                _sb.Append(']');
            }
            else
            {
                _sb.Append(",\"HandJointLocations\":[]");
            }

            _sb.Append('}');
        }

        // ── Body ──────────────────────────────────────────

        private void AppendBody()
        {
            bool supported = false;
            PXR_MotionTracking.GetBodyTrackingSupported(ref supported);

            int count = 0;
            BodyTrackingData data = default;

            if (supported && TrackingSignalStatus.HasValidSignal(TrackingSignalKind.Body))
            {
                BodyTrackingGetDataInfo getInfo = new BodyTrackingGetDataInfo { displayTime = 0 };
                data = new BodyTrackingData();
                data.roleDatas = new BodyTrackingRoleData[BodyJointCount];
                int result = PXR_MotionTracking.GetBodyTrackingData(ref getInfo, ref data);

                if (result == 0 && data.roleDatas != null)
                {
                    count = Mathf.Min(data.roleDatas.Length, BodyJointCount);
                }
            }

            _sb.Append(",\"Body\":{");
            _sb.Append("\"poseSpace\":\"pico_body_local\"");
            _sb.Append(",\"alignment\":\"none\"");
            _sb.Append(",\"joints\":[");

            for (int i = 0; i < count; i++)
            {
                if (i > 0) _sb.Append(',');
                var rd = data.roleDatas[i];
                _sb.Append("{\"p\":\"");
                AppendPose(
                    (float)rd.localPose.PosX, (float)rd.localPose.PosY, (float)rd.localPose.PosZ,
                    (float)rd.localPose.RotQx, (float)rd.localPose.RotQy, (float)rd.localPose.RotQz, (float)rd.localPose.RotQw);
                _sb.Append($"\",\"t\":{(int)rd.bodyAction}");
                AppendEmptyBodyVelocity();
                _sb.Append('}');
            }

            _sb.Append($"],\"len\":{count}}}");
        }

        private void AppendEmptyBodyVelocity()
        {
            // Body pose is the consumed signal; velocity fields stay present for protocol compatibility.
            _sb.Append(",\"va\":\"0,0,0,0,0,0\"");
            _sb.Append(",\"wva\":\"0,0,0,0,0,0\"");
        }

        // ── Motion Trackers ───────────────────────────────

        private void AppendMotion()
        {
            _sb.Append(",\"Motion\":{\"joints\":[");
            // Motion tracker enumeration requires runtime tracker IDs
            // Placeholder - will be populated when trackers are connected.
            _sb.Append("],\"len\":0}");
        }

        // ── helpers ───────────────────────────────────────

        private static Vector3 ToUnityPosition(float x, float y, float z)
        {
            return new Vector3(x, y, -z);
        }

        private static Quaternion ToUnityRotation(float x, float y, float z, float w)
        {
            return new Quaternion(x, y, -z, -w);
        }

        private void AppendPose(float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            AppendPoseComponent(x);
            _sb.Append(',');
            AppendPoseComponent(y);
            _sb.Append(',');
            AppendPoseComponent(z);
            _sb.Append(',');
            AppendPoseComponent(qx);
            _sb.Append(',');
            AppendPoseComponent(qy);
            _sb.Append(',');
            AppendPoseComponent(qz);
            _sb.Append(',');
            AppendPoseComponent(qw);
        }

        private void AppendPoseComponent(float value)
        {
            _sb.Append(JsonNumber(value, "F6"));
        }

        private void AppendJsonNumber(float value)
        {
            _sb.Append(JsonNumber(value, "F4"));
        }

        private static string JsonNumber(float value, string format)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "0";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string BoolStr(bool v) => v ? "true" : "false";
    }
}
