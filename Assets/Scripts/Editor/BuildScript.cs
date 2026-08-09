using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Pho.EditorTools
{
    /// <summary>
    /// Produces the standalone playable build ("the beta").
    ///
    /// Invoked headlessly:
    ///   Unity -batchmode -nographics -projectPath . \
    ///     -executeMethod Pho.EditorTools.BuildScript.BuildMacOS -quit
    ///
    /// Output goes to Build/ (gitignored) -- a build artifact is not source
    /// and must never be committed.
    ///
    /// ORDERING: run the content generator, the prefab builder and the scene
    /// builder BEFORE this, or the build will ship whatever stale generated
    /// assets happen to be on disk. BuildMacOS re-asserts the scene is
    /// registered in EditorBuildSettings rather than assuming it.
    /// </summary>
    public static class BuildScript
    {
        const string BootScenePath = "Assets/Scenes/Boot.unity";
        const string OutputDirectory = "Build";
        const string ProductName = "PhoSimulator";

        [MenuItem("Pho/Build/macOS Player")]
        public static void BuildMacOS()
        {
            var target = BuildTarget.StandaloneOSX;
            var outputPath = Path.Combine(OutputDirectory, ProductName + ".app");

            Directory.CreateDirectory(OutputDirectory);

            if (!File.Exists(BootScenePath))
            {
                Fail($"Boot scene '{BootScenePath}' does not exist -- run SceneBuilder.BuildBootScene first.");
                return;
            }

            // Don't trust EditorBuildSettings to already be correct; a build
            // with zero scenes "succeeds" and produces an unplayable black
            // window, which is exactly the kind of silent failure this
            // project has been bitten by before.
            var scenes = new[] { BootScenePath };
            EditorBuildSettings.scenes = scenes
                .Select(s => new EditorBuildSettingsScene(s, true))
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build {summary.result}: {summary.totalErrors} error(s). Output: {outputPath}");
                return;
            }

            Debug.Log($"[BuildScript] Build SUCCEEDED -> {outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalWarnings} warning(s)).");
        }

        static void Fail(string message)
        {
            Debug.LogError($"[BuildScript] {message}");

            // In batchmode a LogError alone still exits 0, which would let a
            // broken build masquerade as a good one in any script that only
            // checks the exit code.
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
