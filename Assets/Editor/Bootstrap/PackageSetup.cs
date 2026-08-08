using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>
/// One-shot headless package installer for Phase 0 bootstrap.
/// Run via: Unity -batchmode -nographics -projectPath . -executeMethod PhoBootstrap.PackageSetup.InstallAll -quit
/// Resolves each package against the registry rather than hand-guessing
/// version strings in Packages/manifest.json.
/// </summary>
public static class PackageSetup
{
    static readonly string[] PackagesToInstall =
    {
        "com.unity.render-pipelines.universal",
        "com.unity.ai.navigation",
        "com.unity.nuget.newtonsoft-json",
        "com.unity.inputsystem",
    };

    public static void InstallAll()
    {
        foreach (var pkg in PackagesToInstall)
        {
            Debug.Log($"[PackageSetup] Adding {pkg} ...");
            var request = Client.Add(pkg);
            WaitForCompletion(request);

            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[PackageSetup] OK: {request.Result.packageId}");
            }
            else if (request.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[PackageSetup] FAILED {pkg}: {request.Error.message}");
            }
        }

        Debug.Log("[PackageSetup] Done.");
    }

    static void WaitForCompletion(Request request)
    {
        var timeoutAt = System.DateTime.UtcNow.AddMinutes(5);
        while (!request.IsCompleted)
        {
            if (System.DateTime.UtcNow > timeoutAt)
            {
                Debug.LogError("[PackageSetup] Timed out waiting for package request.");
                break;
            }
            Thread.Sleep(250);
        }
    }
}
