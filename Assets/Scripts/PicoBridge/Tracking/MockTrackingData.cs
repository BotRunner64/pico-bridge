using System.Globalization;
using UnityEngine;

namespace PicoBridge.Tracking
{
    /// <summary>
    /// Generates fake tracking JSON for Editor Play mode testing.
    /// Head bobs gently, controllers stay at sides.
    /// </summary>
    public static class MockTrackingData
    {
        public static string GenerateJson(float time)
        {
            // Simulate gentle head movement
            float hx = Mathf.Sin(time * 0.5f) * 0.1f;
            float hy = 1.6f + Mathf.Sin(time * 0.3f) * 0.02f;
            float hz = Mathf.Cos(time * 0.4f) * 0.1f;
            float hry = Mathf.Sin(time * 0.2f) * 0.1f;

            long tsNs = (long)(Time.realtimeSinceStartupAsDouble * 1_000_000_000);
            string hxText = hx.ToString("F6", CultureInfo.InvariantCulture);
            string hyText = hy.ToString("F6", CultureInfo.InvariantCulture);
            string hzText = hz.ToString("F6", CultureInfo.InvariantCulture);
            string hryText = hry.ToString("F6", CultureInfo.InvariantCulture);

            return $"{{\"predictTime\":16000,"
                + $"\"appState\":{{\"focus\":true}},"
                + $"\"Head\":{{\"pose\":\"{hxText},{hyText},{hzText},0.000000,{hryText},0.000000,1.000000\",\"status\":3}},"
                + $"\"Controller\":{{\"left\":{{\"pose\":\"-0.200000,1.000000,-0.300000,0,0,0,1\",\"trigger\":0.0000,\"grip\":0.0000}},"
                + $"\"right\":{{\"pose\":\"0.200000,1.000000,-0.300000,0,0,0,1\",\"trigger\":0.0000,\"grip\":0.0000}}}},"
                + $"\"Hand\":{{\"leftHand\":{{\"isActive\":false,\"count\":0}},\"rightHand\":{{\"isActive\":false,\"count\":0}}}},"
                + $"\"Body\":{{\"joints\":[],\"len\":0}},"
                + $"\"Motion\":{{\"joints\":[],\"len\":0}},"
                + $"\"Input\":0,"
                + $"\"timeStampNs\":{tsNs}}}";
        }
    }
}
