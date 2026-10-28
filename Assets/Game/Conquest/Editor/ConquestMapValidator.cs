using System.Collections.Generic;
using System.Linq;
using Game.Conquest.Gameplay;
using Game.Conquest.Lobby;
using Game.Conquest.Maps;
using Game.Conquest.Networking;
using Game.Conquest.Objectives;
using Game.Conquest.Scenes;
using Game.Conquest.UI;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Game.Conquest.Build
{
    /// <summary>
    /// Checks every registered map scene, and the menu scene that hosts the lobby, for the wiring a
    /// match needs: posts with spawn points, home bases, one of each manager, baked NavMesh stored
    /// outside the scene, spawns on the NavMesh, and map data that matches the scene.
    ///
    /// Menu: Rivetfront → Validate Maps. Batchmode (exit code 1 on any error):
    ///     Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod Game.Conquest.Build.ConquestMapValidator.ValidateFromCommandLine
    /// </summary>
    public static class ConquestMapValidator
    {
        private const string BootstrapScenePath = "Assets/Game/Conquest/Scenes/Bootstrap.unity";
        private const string MenuScenePath = "Assets/Game/Conquest/Scenes/MainMenu.unity";
        // How far a spawn point may sit from the NavMesh and still count as on it (bots path from there).
        private const float NavMeshTolerance = 2f;

        public sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public int MapsChecked;

            public void Error(string where, string message) => Errors.Add($"{where}: {message}");
            public void Warn(string where, string message) => Warnings.Add($"{where}: {message}");
        }

        [MenuItem("Rivetfront/Validate Maps")]
        public static void ValidateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Report report;
            try { report = Run(); }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            Log(report);
            EditorUtility.DisplayDialog("Validate Maps",
                report.Errors.Count == 0
                    ? $"{report.MapsChecked} maps passed ({report.Warnings.Count} warnings). Details are in the Console."
                    : $"{report.Errors.Count} errors in {report.MapsChecked} maps. Details are in the Console.",
                "OK");
        }

        public static void ValidateFromCommandLine()
        {
            Report report = Run();
            Log(report);
            EditorApplication.Exit(report.Errors.Count == 0 ? 0 : 1);
        }

        public static Report Run()
        {
            var report = new Report();
            var registry = FindRegistry(report);
            ValidateMenuScene(report, registry);
            if (registry == null) return report;

            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            var seenIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var map in registry.RegisteredMaps)
            {
                if (map == null) { report.Error(AssetDatabase.GetAssetPath(registry), "has an empty map slot"); continue; }
                string where = $"Map '{map.mapId}'";
                if (!seenIds.Add(map.mapId)) report.Error(where, "map id is used by more than one map");

                string scenePath = buildScenes.FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == map.sceneName);
                if (scenePath == null)
                {
                    report.Error(where, $"scene '{map.sceneName}' is not an enabled scene in the build list");
                    continue;
                }
                ValidateMapScene(report, map, scenePath);
                report.MapsChecked++;
            }
            return report;
        }

        private static ConquestMapRegistry FindRegistry(Report report)
        {
            var guids = AssetDatabase.FindAssets("t:ConquestMapRegistry");
            if (guids.Length == 0) { report.Error("Project", "no ConquestMapRegistry asset"); return null; }
            if (guids.Length > 1) report.Warn("Project", $"{guids.Length} ConquestMapRegistry assets; checking the first");
            return AssetDatabase.LoadAssetAtPath<ConquestMapRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void ValidateMenuScene(Report report, ConquestMapRegistry registry)
        {
            // The session manager and scene transitions live in Bootstrap and persist; the lobby and
            // the NetworkManager live in the main menu.
            var scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            string where = scene.name;
            var session = One<ConquestSessionManager>(report, where, scene);
            if (session != null) RequireRef(report, where, session, "networkPrefabs", null);
            One<ConquestSceneTransitionManager>(report, where, scene);

            scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            where = scene.name;
            var lobby = One<ConquestLobbyManager>(report, where, scene);
            if (lobby != null) RequireRef(report, where, lobby, "mapRegistry", registry);
            One<NetworkManager>(report, where, scene);
        }

        private static void ValidateMapScene(Report report, ConquestMapData map, string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string where = scene.name;

            One<ConquestMatchManager>(report, where, scene);
            var bots = One<ConquestBotManager>(report, where, scene);
            if (bots != null) RequireRef(report, where, bots, "botPrefab", null);
            One<ConquestNetworkManager>(report, where, scene);
            One<ConquestHUD>(report, where, scene);
            One<ConquestPauseMenu>(report, where, scene);

            var posts = All<ConquestCommandPost>(scene);
            // The menus show commandPostsCount as the number of posts to fight over, so home bases don't count.
            int capturable = posts.Count(p => !new SerializedObject(p).FindProperty("isHomeBase").boolValue);
            if (capturable != map.commandPostsCount)
                report.Error(where, $"has {capturable} capturable command posts but map data '{map.mapId}' says {map.commandPostsCount}");

            var names = new HashSet<string>();
            var spawnPoints = new List<(string label, Vector3 position)>();
            bool redHome = false, blueHome = false, redStart = false, blueStart = false;
            foreach (var post in posts)
            {
                var so = new SerializedObject(post);
                string postName = so.FindProperty("postName").stringValue;
                var owner = (Team)so.FindProperty("initialOwner").intValue;
                bool home = so.FindProperty("isHomeBase").boolValue;
                string label = $"{where}/post '{postName}'";

                if (string.IsNullOrWhiteSpace(postName)) report.Error(label, "has no name");
                else if (!names.Add(postName)) report.Error(label, "name is used by more than one post");
                if (owner == Team.Red) redStart = true;
                if (owner == Team.Blue) blueStart = true;
                if (home && owner == Team.Red) redHome = true;
                if (home && owner == Team.Blue) blueHome = true;
                if (home && owner == Team.Neutral) report.Error(label, "is a home base with no owning team");

                // A post with no spawn points spawns around its centre (ConquestCommandPost.GetSpawnPoint),
                // so the centre then has to be a valid spawn.
                var spawns = so.FindProperty("spawnPoints");
                if (spawns.arraySize == 0) spawnPoints.Add(($"{label} (centre, no spawn points)", post.transform.position));
                for (int i = 0; i < spawns.arraySize; i++)
                {
                    var t = spawns.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (t == null) { report.Error(label, $"spawn point {i} is empty"); continue; }
                    spawnPoints.Add(($"{label}/{t.name}", t.position));
                }
            }
            // Both teams need somewhere to spawn when the match starts.
            if (!redStart) report.Error(where, "no post starts owned by Red");
            if (!blueStart) report.Error(where, "no post starts owned by Blue");
            if (!redHome) report.Warn(where, "Red has no home base");
            if (!blueHome) report.Warn(where, "Blue has no home base");

            ValidateNavMesh(report, where, scene, spawnPoints);
            ValidateFallLimit(report, where, scene, spawnPoints);
        }

        private static void ValidateNavMesh(Report report, string where, Scene scene, List<(string label, Vector3 position)> spawnPoints)
        {
            var surfaces = All<NavMeshSurface>(scene);
            if (surfaces.Count == 0) { report.Error(where, "has no NavMeshSurface (bots can't move)"); return; }
            foreach (var surface in surfaces)
            {
                if (surface.navMeshData == null) { report.Error(where, $"NavMeshSurface on '{surface.name}' has no baked data"); continue; }
                string dataPath = AssetDatabase.GetAssetPath(surface.navMeshData);
                if (string.IsNullOrEmpty(dataPath) || dataPath == scene.path)
                    report.Error(where, $"NavMesh data on '{surface.name}' is stored inside the scene; bake it to a separate asset");
            }
            Physics.SyncTransforms();
            foreach (var (label, position) in spawnPoints)
            {
                // The game drops a spawn onto the ground below it before placing the player; do the same.
                Vector3 ground = Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 15f)
                    ? hit.point : position;
                if (NavMesh.SamplePosition(ground, out _, NavMeshTolerance, NavMesh.AllAreas)) continue;
                string distance = NavMesh.SamplePosition(ground, out NavMeshHit near, 50f, NavMesh.AllAreas)
                    ? $"{Vector3.Distance(ground, near.position):F1} m (up {near.position.y - ground.y:F1} m; ground hit '{(hit.collider != null ? hit.collider.name : "none")}')" : "over 50 m";
                report.Error(label, $"is {distance} from the NavMesh (allowed {NavMeshTolerance} m), so bots spawning there can't path");
            }
        }

        private static void ValidateFallLimit(Report report, string where, Scene scene, List<(string label, Vector3 position)> spawnPoints)
        {
            var match = All<ConquestMatchManager>(scene).FirstOrDefault();
            if (match == null || spawnPoints.Count == 0) return;
            float fallLimit = new SerializedObject(match).FindProperty("fallLimitY").floatValue;
            foreach (var (label, position) in spawnPoints)
            {
                if (position.y <= fallLimit) report.Error(label, $"is below the fall limit ({fallLimit})");
            }
        }

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        private static T One<T>(Report report, string where, Scene scene) where T : Component
        {
            var all = All<T>(scene);
            if (all.Count == 0) report.Error(where, $"has no {typeof(T).Name}");
            else if (all.Count > 1) report.Error(where, $"has {all.Count} {typeof(T).Name} components (expected 1)");
            return all.Count > 0 ? all[0] : null;
        }

        // expected == null means "any non-empty reference".
        private static void RequireRef(Report report, string where, Component component, string field, Object expected)
        {
            var prop = new SerializedObject(component).FindProperty(field);
            if (prop == null) { report.Error(where, $"{component.GetType().Name}.{field} no longer exists; update the validator"); return; }
            if (prop.objectReferenceValue == null) report.Error(where, $"{component.GetType().Name}.{field} is not assigned");
            else if (expected != null && prop.objectReferenceValue != expected)
                report.Error(where, $"{component.GetType().Name}.{field} points at '{prop.objectReferenceValue.name}', not '{expected.name}'");
        }

        private static void Log(Report report)
        {
            foreach (var w in report.Warnings) Debug.LogWarning("[MapValidator] " + w);
            foreach (var e in report.Errors) Debug.LogError("[MapValidator] " + e);
            Debug.Log($"[MapValidator] {report.MapsChecked} maps checked: {report.Errors.Count} errors, {report.Warnings.Count} warnings.");
        }
    }
}
