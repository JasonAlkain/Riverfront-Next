using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Conquest.Build
{
    /// <summary>
    /// Release builds for the Windows client and the Windows dedicated server, callable from the Editor
    /// menu or from batchmode (see Tools/Build/build.ps1):
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -buildTarget Win64
    ///     -executeMethod Game.Conquest.Build.ReleaseBuild.BuildFromCommandLine
    ///     -buildVersion 0.1.0+abc1234 -buildOutput Builds/0.1.0+abc1234 -buildTargets client,server
    ///
    /// Output layout: {output}/windows-client/Riverfront Next.exe and {output}/windows-server/Riverfront Next.exe.
    /// Debug-symbol folders Unity writes next to the player are moved to {output}/symbols/ so the
    /// client folder can be pushed to itch.io as-is.
    ///
    /// Uses the global scene list and Player Settings rather than the build profiles in
    /// Assets/Settings/Build Profiles: the "Windows Client" profile still carries template Player Settings
    /// overrides (productName "FPSSample2"), which would ship the wrong exe name.
    /// </summary>
    public static class ReleaseBuild
    {
        public const string ClientFolder = "windows-client";
        public const string ServerFolder = "windows-server";
        public const string SymbolsFolder = "symbols";

        private const string DefaultOutputRoot = "Builds";

        [MenuItem("Rivetfront/Build/Windows Client + Server")]
        private static void MenuBuildAll() => BuildFromMenu(client: true, server: true);

        [MenuItem("Rivetfront/Build/Windows Client")]
        private static void MenuBuildClient() => BuildFromMenu(client: true, server: false);

        [MenuItem("Rivetfront/Build/Windows Dedicated Server")]
        private static void MenuBuildServer() => BuildFromMenu(client: false, server: true);

        private static void BuildFromMenu(bool client, bool server)
        {
            string version = PlayerSettings.bundleVersion;
            string output = Path.Combine(DefaultOutputRoot, version);
            bool ok = Run(version, output, client, server, development: false);
            EditorUtility.RevealInFinder(output);
            if (!ok)
            {
                EditorUtility.DisplayDialog("Build failed", "See the Console for details.", "OK");
            }
        }

        /// <summary>Batchmode entry point. Exits with code 0 on success, 1 on failure.</summary>
        public static void BuildFromCommandLine()
        {
            bool ok;
            try
            {
                string version = GetArg("-buildVersion") ?? PlayerSettings.bundleVersion;
                string output = GetArg("-buildOutput") ?? Path.Combine(DefaultOutputRoot, version);
                string targets = (GetArg("-buildTargets") ?? "client,server").ToLowerInvariant();
                bool client = targets.Contains("client");
                bool server = targets.Contains("server");
                bool development = HasArg("-developmentBuild");
                if (!client && !server)
                {
                    throw new ArgumentException($"-buildTargets '{targets}' names neither client nor server.");
                }
                ok = Run(version, output, client, server, development);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        private static bool Run(string version, string outputRoot, bool client, bool server, bool development)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[ReleaseBuild] No enabled scenes in Build Settings.");
                return false;
            }

            // Application.version is part of the connection-approval handshake (ConquestSessionManager.BuildVersion),
            // so the client and server built here must carry the same string. Set it for the build only and
            // restore it so a batchmode run does not leave ProjectSettings.asset modified.
            string originalVersion = PlayerSettings.bundleVersion;
            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            StandaloneBuildSubtarget originalSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            PlayerSettings.bundleVersion = version;
            try
            {
                Debug.Log($"[ReleaseBuild] Version {version} -> {Path.GetFullPath(outputRoot)}");
                if (client && !BuildOne(scenes, outputRoot, ClientFolder, StandaloneBuildSubtarget.Player, development))
                {
                    return false;
                }
                if (server && !BuildOne(scenes, outputRoot, ServerFolder, StandaloneBuildSubtarget.Server, development))
                {
                    return false;
                }
                WriteBuildInfo(outputRoot, version, client, server, development);
                return true;
            }
            finally
            {
                PlayerSettings.bundleVersion = originalVersion;
                if (!Application.isBatchMode)
                {
                    // Building the server switches the active subtarget; put the Editor back how it was.
                    EditorUserBuildSettings.standaloneBuildSubtarget = originalSubtarget;
                    if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
                    {
                        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(originalTarget), originalTarget);
                    }
                }
            }
        }

        private static bool BuildOne(string[] scenes, string outputRoot, string folder, StandaloneBuildSubtarget subtarget, bool development)
        {
            string dir = Path.Combine(outputRoot, folder);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
            Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(dir, PlayerSettings.productName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)subtarget,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log($"[ReleaseBuild] Building {folder} ({subtarget})...");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[ReleaseBuild] {folder} failed: {summary.result}, {summary.totalErrors} error(s).");
                return false;
            }

            MoveSymbolFolders(dir, Path.Combine(outputRoot, SymbolsFolder, folder));
            Debug.Log($"[ReleaseBuild] {folder} OK: {summary.totalSize / (1024f * 1024f):F1} MB in {summary.totalTime.TotalSeconds:F0}s.");
            return true;
        }

        /// <summary>Burst/IL2CPP debug folders must not ship; keep them beside the build for crash symbolication.</summary>
        private static void MoveSymbolFolders(string buildDir, string symbolsDir)
        {
            var doNotShip = Directory.GetDirectories(buildDir)
                .Where(d => d.EndsWith("_DoNotShip", StringComparison.Ordinal) ||
                            d.EndsWith("_ButDontShipItWithYourGame", StringComparison.Ordinal))
                .ToList();
            if (doNotShip.Count == 0)
            {
                return;
            }
            if (Directory.Exists(symbolsDir))
            {
                Directory.Delete(symbolsDir, recursive: true);
            }
            Directory.CreateDirectory(symbolsDir);
            foreach (string d in doNotShip)
            {
                Directory.Move(d, Path.Combine(symbolsDir, Path.GetFileName(d)));
            }
        }

        private static void WriteBuildInfo(string outputRoot, string version, bool client, bool server, bool development)
        {
            var lines = new List<string>
            {
                $"product={PlayerSettings.productName}",
                $"version={version}",
                $"unity={Application.unityVersion}",
                $"built_utc={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}",
                $"development={development.ToString().ToLowerInvariant()}",
                $"targets={(client ? ClientFolder + " " : "")}{(server ? ServerFolder : "")}".TrimEnd(),
            };
            File.WriteAllLines(Path.Combine(outputRoot, "build-info.txt"), lines);
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static bool HasArg(string name) =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
