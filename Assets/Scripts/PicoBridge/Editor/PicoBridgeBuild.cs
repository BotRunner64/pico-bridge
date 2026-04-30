#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PicoBridge.Editor
{
    public static class PicoBridgeBuild
    {
        private const string DefaultAndroidApkPath = "/tmp/pico-bridge.apk";
        private const string BuildPathArg = "-picoBridgeBuildPath";

        public static void BuildAndroidApkFromCommandLine()
        {
            var outputPath = GetArgumentValue(BuildPathArg, DefaultAndroidApkPath);
            var preloadedAssets = PlayerSettings.GetPreloadedAssets();
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes in EditorBuildSettings.");

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(preloadedAssets);
            }

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");

            Debug.Log($"[PicoBridge] Android build succeeded: {outputPath}");
        }

        private static string GetArgumentValue(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1];
            }

            return fallback;
        }
    }
}
#endif
