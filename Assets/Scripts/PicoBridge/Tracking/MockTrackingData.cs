using System.Globalization;
using System.Text;
using UnityEngine;

namespace PicoBridge.Tracking
{
    /// <summary>
    /// Generates fake tracking JSON for Editor Play mode testing.
    /// Sends every tracking family so the PC receiver and visualizer can be tested without a PICO device.
    /// </summary>
    public static class MockTrackingData
    {
        private const int HandJointCount = 21;
        private const int BodyJointCount = 24;
        private const int MotionJointCount = 3;

        public static string GenerateJson(float time)
        {
            var sb = new StringBuilder(8192);
            long tsNs = (long)(Time.realtimeSinceStartupAsDouble * 1_000_000_000);

            sb.Append('{');
            sb.Append("\"predictTime\":16000");
            sb.Append(",\"appState\":{\"focus\":true}");
            AppendHead(sb, time);
            AppendControllers(sb, time);
            AppendHands(sb, time);
            AppendBody(sb, time);
            AppendMotion(sb, time);
            sb.Append(",\"Input\":0");
            sb.Append($",\"timeStampNs\":{tsNs}");
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendHead(StringBuilder sb, float time)
        {
            float hx = Mathf.Sin(time * 0.5f) * 0.1f;
            float hy = 1.6f + Mathf.Sin(time * 0.3f) * 0.02f;
            float hz = Mathf.Cos(time * 0.4f) * 0.1f;
            float hry = Mathf.Sin(time * 0.2f) * 0.1f;

            sb.Append(",\"Head\":{\"pose\":\"");
            AppendPose(sb, hx, hy, hz, 0f, hry, 0f, 1f);
            sb.Append("\",\"status\":3}");
        }

        private static void AppendControllers(StringBuilder sb, float time)
        {
            float triggerPulse = 0.5f + 0.5f * Mathf.Sin(time * 1.3f);
            float gripPulse = 0.5f + 0.5f * Mathf.Cos(time * 1.1f);

            sb.Append(",\"Controller\":{");
            AppendController(sb, "left", -0.24f, 1.08f, -0.38f, -0.35f, 0.15f, triggerPulse, gripPulse, true, false);
            sb.Append(',');
            AppendController(sb, "right", 0.24f, 1.08f, -0.38f, 0.35f, -0.15f, gripPulse, triggerPulse, false, true);
            sb.Append('}');
        }

        private static void AppendController(
            StringBuilder sb,
            string side,
            float x,
            float y,
            float z,
            float axisX,
            float axisY,
            float trigger,
            float grip,
            bool primaryButton,
            bool secondaryButton)
        {
            sb.Append($"\"{side}\":{{\"pose\":\"");
            AppendPose(sb, x, y, z, 0f, 0f, 0f, 1f);
            sb.Append("\",\"axisX\":");
            AppendJsonNumber(sb, axisX);
            sb.Append(",\"axisY\":");
            AppendJsonNumber(sb, axisY);
            sb.Append(",\"axisClick\":true,\"grip\":");
            AppendJsonNumber(sb, grip);
            sb.Append(",\"trigger\":");
            AppendJsonNumber(sb, trigger);
            sb.Append($",\"primaryButton\":{BoolStr(primaryButton)}");
            sb.Append($",\"secondaryButton\":{BoolStr(secondaryButton)}");
            sb.Append(",\"menuButton\":false}");
        }

        private static void AppendHands(StringBuilder sb, float time)
        {
            sb.Append(",\"Hand\":{");
            AppendHand(sb, "leftHand", -1f, time);
            sb.Append(',');
            AppendHand(sb, "rightHand", 1f, time + 0.7f);
            sb.Append('}');
        }

        private static void AppendHand(StringBuilder sb, string key, float side, float time)
        {
            sb.Append($"\"{key}\":{{\"isActive\":true,\"count\":{HandJointCount},\"scale\":1.0000,\"HandJointLocations\":[");

            for (int i = 0; i < HandJointCount; i++)
            {
                if (i > 0)
                    sb.Append(',');

                Vector3 pos = GetMockHandJoint(side, i, time);
                sb.Append("{\"p\":\"");
                AppendPose(sb, pos.x, pos.y, pos.z, 0f, 0f, 0f, 1f);
                sb.Append("\",\"s\":3,\"r\":0.0100}");
            }

            sb.Append("]}");
        }

        private static Vector3 GetMockHandJoint(float side, int index, float time)
        {
            float originX = side * 0.28f;
            float wave = Mathf.Sin(time * 2.0f + index * 0.35f) * 0.012f;
            float palmY = 1.12f;
            float palmZ = -0.45f;

            if (index == 0)
                return new Vector3(originX, palmY, palmZ);

            if (index < 5)
            {
                float step = index;
                return new Vector3(originX + side * (0.025f + step * 0.018f), palmY + step * 0.020f, palmZ + 0.015f + wave);
            }

            int finger = (index - 5) / 4;
            int segment = ((index - 5) % 4) + 1;
            float[] spread = { side * 0.030f, side * 0.010f, -side * 0.010f, -side * 0.030f };
            float[] length = { 0.032f, 0.038f, 0.035f, 0.028f };
            return new Vector3(
                originX + spread[finger],
                palmY + 0.025f + segment * length[finger],
                palmZ - segment * 0.006f + wave);
        }

        private static void AppendBody(StringBuilder sb, float time)
        {
            Vector3[] joints =
            {
                new Vector3(0.00f, 0.95f, -0.08f),
                new Vector3(0.00f, 1.12f, -0.08f),
                new Vector3(0.00f, 1.30f, -0.08f),
                new Vector3(0.00f, 1.48f, -0.08f),
                new Vector3(0.00f, 1.62f, -0.08f),
                new Vector3(-0.18f, 1.42f, -0.08f),
                new Vector3(-0.36f, 1.25f, -0.10f),
                new Vector3(-0.42f, 1.05f, -0.12f),
                new Vector3(0.18f, 1.42f, -0.08f),
                new Vector3(0.36f, 1.25f, -0.10f),
                new Vector3(0.42f, 1.05f, -0.12f),
                new Vector3(-0.10f, 0.92f, -0.08f),
                new Vector3(-0.12f, 0.55f, -0.06f),
                new Vector3(-0.12f, 0.18f, -0.03f),
                new Vector3(-0.12f, 0.05f, -0.17f),
                new Vector3(0.10f, 0.92f, -0.08f),
                new Vector3(0.12f, 0.55f, -0.06f),
                new Vector3(0.12f, 0.18f, -0.03f),
                new Vector3(0.12f, 0.05f, -0.17f),
                new Vector3(-0.23f, 1.38f, -0.07f),
                new Vector3(0.23f, 1.38f, -0.07f),
                new Vector3(-0.08f, 1.57f, -0.05f),
                new Vector3(0.08f, 1.57f, -0.05f),
                new Vector3(0.00f, 1.72f, -0.07f),
            };

            sb.Append(",\"Body\":{\"joints\":[");
            for (int i = 0; i < BodyJointCount; i++)
            {
                if (i > 0)
                    sb.Append(',');

                Vector3 p = joints[i];
                p.x += Mathf.Sin(time * 0.9f + i) * 0.015f;
                p.z += Mathf.Cos(time * 0.7f + i) * 0.010f;
                sb.Append("{\"p\":\"");
                AppendPose(sb, p.x, p.y, p.z, 0f, 0f, 0f, 1f);
                sb.Append($"\",\"t\":{i},\"va\":\"0,0,0,0,0,0\",\"wva\":\"0,0,0,0,0,0\"}}");
            }

            sb.Append($"],\"len\":{BodyJointCount}}}");
        }

        private static void AppendMotion(StringBuilder sb, float time)
        {
            sb.Append(",\"Motion\":{\"joints\":[");

            for (int i = 0; i < MotionJointCount; i++)
            {
                if (i > 0)
                    sb.Append(',');

                float angle = time + i * 2.1f;
                float x = -0.45f + i * 0.45f;
                float y = 0.72f + Mathf.Sin(angle) * 0.08f;
                float z = -0.62f + Mathf.Cos(angle) * 0.05f;
                sb.Append($"{{\"id\":{i},\"p\":\"");
                AppendPose(sb, x, y, z, 0f, 0f, 0f, 1f);
                sb.Append($"\",\"t\":{i},\"va\":\"0,0,0,0,0,0\",\"wva\":\"0,0,0,0,0,0\"}}");
            }

            sb.Append($"],\"len\":{MotionJointCount}}}");
        }

        private static void AppendPose(StringBuilder sb, float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            AppendPoseComponent(sb, x);
            sb.Append(',');
            AppendPoseComponent(sb, y);
            sb.Append(',');
            AppendPoseComponent(sb, z);
            sb.Append(',');
            AppendPoseComponent(sb, qx);
            sb.Append(',');
            AppendPoseComponent(sb, qy);
            sb.Append(',');
            AppendPoseComponent(sb, qz);
            sb.Append(',');
            AppendPoseComponent(sb, qw);
        }

        private static void AppendPoseComponent(StringBuilder sb, float value)
        {
            sb.Append(JsonNumber(value, "F6"));
        }

        private static void AppendJsonNumber(StringBuilder sb, float value)
        {
            sb.Append(JsonNumber(value, "F4"));
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
