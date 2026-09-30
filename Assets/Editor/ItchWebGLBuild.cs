using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ItchTools.Editor
{
    /// <summary>
    /// WebGL build for itch.io: gzip + decompression fallback (works without special server headers),
    /// output in Builds/WebGL and a ready-to-upload Builds/&lt;name&gt;_WebGL.zip.
    /// Batch: -executeMethod ItchTools.Editor.ItchWebGLBuild.Build [-webglSize 1280x720]
    /// </summary>
    public static class ItchWebGLBuild
    {
        [MenuItem("Tools/itch.io/Build WebGL")]
        public static void BuildMenu() => Run(false);

        public static void Build() => Run(Application.isBatchMode);

        private static void Run(bool exit)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { Fail("No scenes in Build Settings", exit); return; }

            var (w, h) = ParseSize();
            PlayerSettings.defaultWebScreenWidth = w;
            PlayerSettings.defaultWebScreenHeight = h;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.runInBackground = true;

            var root = Path.GetDirectoryName(Application.dataPath);
            var outDir = Path.Combine(root, "Builds", "WebGL");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

            var previous = EditorUserBuildSettings.activeBuildTarget;
            var previousGroup = BuildPipeline.GetBuildTargetGroup(previous);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                Fail($"WebGL build failed: {report.summary.result}, {report.summary.totalErrors} errors", exit);
                return;
            }

            foreach (var dir in Directory.GetDirectories(outDir, "*_DoNotShip"))
                Directory.Delete(dir, true);

            var zip = Path.Combine(root, "Builds", $"{PlayerSettings.productName.Replace(' ', '_')}_WebGL.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(outDir, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log($"[ItchWebGLBuild] OK {w}x{h} → {zip} ({new FileInfo(zip).Length / 1048576f:F1} MB)");

            if (previous != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previous);
            if (exit) EditorApplication.Exit(0);
        }

        private static (int, int) ParseSize()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-webglSize");
            if (i >= 0 && i + 1 < args.Length)
            {
                var p = args[i + 1].Split('x');
                if (p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h)) return (w, h);
            }
            return (1280, 720);
        }

        private static void Fail(string message, bool exit)
        {
            Debug.LogError("[ItchWebGLBuild] " + message);
            if (exit) EditorApplication.Exit(1);
        }
    }
}
