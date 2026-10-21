using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Conquest.Gameplay;
using Game.Conquest.Objectives;
using Game.Conquest.Player;

namespace Game.Conquest
{
    /// <summary>
    /// Screen-space markers over world objects: one per command post (always shown, kept inside the
    /// screen edge) and a nameplate over each nearby living teammate. Enemies get no nameplate, so a
    /// name over someone's head means "friendly". Markers are pooled prefab instances.
    /// </summary>
    public class WorldMarkersUI : MonoBehaviour
    {
        [Header("Prefabs")]
        public PostMarkerUI postMarkerPrefab;
        public NameplateUI nameplatePrefab;

        [Header("Containers")]
        public RectTransform postContainer;
        public RectTransform nameplateContainer;

        [Header("Theme")]
        [Tooltip("Owner shapes for the post markers.")]
        public Game.Conquest.UI.ConquestUITheme theme;

        [Header("Placement")]
        [Tooltip("Placeholder: metres above a post's centre where its marker sits.")]
        public float postMarkerHeight = 6f;
        [Tooltip("Metres above a player's feet where the nameplate sits.")]
        public float nameplateHeight = 2.3f;
        [Tooltip("Placeholder: teammates further away than this get no nameplate.")]
        public float nameplateRange = 40f;
        [Tooltip("Canvas pixels kept clear at the screen edge for post markers.")]
        public float edgeMargin = 40f;

        private readonly List<PostMarkerUI> m_PostMarkers = new List<PostMarkerUI>();
        private readonly List<NameplateUI> m_Nameplates = new List<NameplateUI>();

        /// <summary>Hides every marker (no camera, dead, menu open).</summary>
        public void HideAll()
        {
            SetActiveCount(m_PostMarkers, 0);
            SetActiveCount(m_Nameplates, 0);
        }

        /// <param name="postColor">Owner (or contested) colour for a post.</param>
        /// <param name="captureColor">Colour of the capture ring for a post.</param>
        public void Tick(Camera cam, ConquestPlayer viewer, IReadOnlyList<ConquestCommandPost> posts,
            Func<ConquestCommandPost, Color> postColor, Func<ConquestCommandPost, Color> captureColor, Color friendlyColor)
        {
            if (cam == null || viewer == null)
            {
                HideAll();
                return;
            }

            var canvasRect = (RectTransform)transform;
            Vector3 viewerPos = viewer.transform.position;

            int shown = 0;
            if (postMarkerPrefab != null && postContainer != null && posts != null)
            {
                for (int i = 0; i < posts.Count; i++)
                {
                    var post = posts[i];
                    if (post == null) continue;

                    var marker = Get(m_PostMarkers, shown, postMarkerPrefab, postContainer);
                    shown++;
                    Vector3 world = post.transform.position + Vector3.up * postMarkerHeight;
                    marker.Rect.anchoredPosition = ToCanvas(cam, canvasRect, world, true);
                    int meters = Mathf.RoundToInt(Vector3.Distance(viewerPos, post.transform.position));
                    marker.SetPost(post.PostName, postColor(post), theme != null ? theme.TeamIcon(post.CurrentOwner) : null, meters, post.CaptureProgress, captureColor(post));
                }
            }
            SetActiveCount(m_PostMarkers, shown);

            shown = 0;
            if (nameplatePrefab != null && nameplateContainer != null)
            {
                var players = ConquestPlayer.All;
                float rangeSqr = nameplateRange * nameplateRange;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p == viewer || p.CurrentTeam != viewer.CurrentTeam || p.Health == null || p.Health.IsDead) continue;

                    Vector3 world = p.transform.position + Vector3.up * nameplateHeight;
                    float distSqr = (p.transform.position - viewerPos).sqrMagnitude;
                    if (distSqr > rangeSqr) continue;
                    // Behind the camera there's nothing to label.
                    if (cam.WorldToScreenPoint(world).z <= 0f) continue;

                    var plate = Get(m_Nameplates, shown, nameplatePrefab, nameplateContainer);
                    shown++;
                    plate.Rect.anchoredPosition = ToCanvas(cam, canvasRect, world, false);
                    int max = p.Health.MaxHealth;
                    float health = max > 0 ? Mathf.Clamp01((float)p.Health.CurrentHealth / max) : 0f;
                    // Fade over the last quarter of the range.
                    float alpha = Mathf.Clamp01((1f - Mathf.Sqrt(distSqr) / nameplateRange) * 4f);
                    plate.SetPlayer(p.playerName.Value, health, friendlyColor, alpha);
                }
            }
            SetActiveCount(m_Nameplates, shown);
        }

        // Canvas-local position of a world point. Post markers stay on screen: points behind the
        // camera are mirrored and pushed to the edge so the marker still shows which way to turn.
        private Vector2 ToCanvas(Camera cam, RectTransform canvasRect, Vector3 world, bool clampToEdge)
        {
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (clampToEdge)
            {
                if (screen.z < 0f)
                {
                    screen.x = Screen.width - screen.x;
                    screen.y = 0f;
                }
                float margin = edgeMargin * (canvasRect.rect.width > 0f ? Screen.width / canvasRect.rect.width : 1f);
                screen.x = Mathf.Clamp(screen.x, margin, Screen.width - margin);
                screen.y = Mathf.Clamp(screen.y, margin, Screen.height - margin);
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            return local;
        }

        private static T Get<T>(List<T> pool, int index, T prefab, Transform parent) where T : Component
        {
            while (pool.Count <= index) pool.Add(Instantiate(prefab, parent));
            var item = pool[index];
            if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);
            return item;
        }

        private static void SetActiveCount<T>(List<T> pool, int count) where T : Component
        {
            for (int i = count; i < pool.Count; i++)
            {
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
            }
        }
    }
}
