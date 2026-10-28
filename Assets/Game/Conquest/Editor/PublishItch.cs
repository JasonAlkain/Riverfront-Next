using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Conquest.Build
{
    /// <summary>
    /// Riverfront Next > Publish menu: pushes a build from Builds/ to itch.io by running Tools/Build/publish-itch.ps1
    /// in its own PowerShell window, so butler's progress (and any login prompt) stays visible and the
    /// Editor is not blocked while it uploads.
    /// </summary>
    public static class PublishItch
    {
        private const string ItchTarget = "allyn-david/riverfront-next";
        private const string ItchChannel = "windows";
        private const string ItchPageUrl = "https://allyn-david.itch.io/riverfront-next";
        private const string PublishScript = "Tools/Build/publish-itch.ps1";

        [MenuItem("Riverfront Next/Publish/Build Client and Push to itch.io...", priority = 0)]
        private static void BuildAndPush()
        {
            if (!EditorUtility.DisplayDialog("Build and push to itch.io",
                    $"Build the Windows client, then push it to {ItchTarget}:{ItchChannel}?\n\n" +
                    "Players with the itch app get it as an update.", "Build and push", "Cancel"))
            {
                return;
            }

            string output = ReleaseBuild.BuildFromMenu(client: true, server: false, reveal: false);
            if (output != null)
            {
                Push(Path.GetFileName(output), askFirst: false);
            }
        }

        [MenuItem("Riverfront Next/Publish/Push Latest Build to itch.io...", priority = 1)]
        private static void PushLatest()
        {
            DirectoryInfo latest = FindLatestBuild();
            if (latest == null)
            {
                EditorUtility.DisplayDialog("Nothing to push",
                    "No client build found under Builds/. Use Riverfront Next > Build > Windows Client first.", "OK");
                return;
            }
            Push(latest.Name, askFirst: true);
        }

        [MenuItem("Riverfront Next/Publish/Open itch.io Page", priority = 20)]
        private static void OpenPage() => Application.OpenURL(ItchPageUrl);

        private static void Push(string version, bool askFirst)
        {
            bool dirty = version.EndsWith("-dirty");
            if (askFirst && !EditorUtility.DisplayDialog("Push to itch.io",
                    $"Push build {version} to {ItchTarget}:{ItchChannel}?", "Push", "Cancel"))
            {
                return;
            }
            if (dirty && !EditorUtility.DisplayDialog("Uncommitted changes",
                    $"Build {version} was made with uncommitted changes, so its commit id does not fully describe it.\n\n" +
                    "Push it anyway?", "Push anyway", "Cancel"))
            {
                return;
            }

            string script = Path.GetFullPath(PublishScript);
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("Publish script missing", $"Expected {script}.", "OK");
                return;
            }

            // -NoExit keeps the window open so the result (or a butler login error) can be read.
            string args = $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{script}\" -Version \"{version}\"" +
                          (dirty ? " -AllowDirty" : "");
            Process.Start(new ProcessStartInfo("powershell.exe", args)
            {
                UseShellExecute = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            });
            UnityEngine.Debug.Log($"[PublishItch] Pushing {version} to {ItchTarget}:{ItchChannel} in a PowerShell window.");
        }

        private static DirectoryInfo FindLatestBuild()
        {
            var root = new DirectoryInfo("Builds");
            if (!root.Exists)
            {
                return null;
            }
            return root.GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "build-info.txt")) &&
                            Directory.Exists(Path.Combine(d.FullName, ReleaseBuild.ClientFolder)))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();
        }
    }
}
