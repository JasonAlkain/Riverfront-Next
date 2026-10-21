using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Objectives;
using Game.Conquest.Player;

namespace Game.Conquest
{
    /// <summary>
    /// North-up command map: command posts in their owner colour, living teammates as dots and the
    /// viewer as an arrow. There is no map art or camera; the area shown is the box around every
    /// post plus padding, so any map works without setup. Used small on the HUD and large on the
    /// deploy screen, where clicking a friendly post picks it as the spawn.
    /// </summary>
    public class CommandMapUI : MonoBehaviour
    {
        [Header("Prefabs")]
        public CommandMapPostUI postPrefab;
        public Image teammateDotPrefab;

        [Header("Layout")]
        [Tooltip("Area the map is drawn in; posts and dots are placed inside its rect.")]
        public RectTransform mapArea;
        [Tooltip("Arrow for the viewer. Rotates with their facing.")]
        public RectTransform viewerArrow;

        [Header("Bounds")]
        [Tooltip("Placeholder: metres of padding around the outermost posts.")]
        public float boundsPadding = 40f;

        [Header("Options")]
        public bool showTeammates = true;

        /// <summary>Raised with a post name when a selectable post is clicked.</summary>
        public event Action<string> PostClicked;

        private readonly List<CommandMapPostUI> m_Posts = new List<CommandMapPostUI>();
        private readonly List<Image> m_Dots = new List<Image>();
        private Vector2 m_Min;
        private float m_Size;
        private int m_BoundsPostCount = -1;

        /// <param name="selectable">Posts the viewer may deploy at; null means none are clickable.</param>
        public void Tick(IReadOnlyList<ConquestCommandPost> posts, ConquestPlayer viewer, Func<ConquestCommandPost, Color> postColor,
            Color teammateColor, List<string> selectable = null, string selected = null)
        {
            if (mapArea == null || posts == null) return;
            if (posts.Count != m_BoundsPostCount) ComputeBounds(posts);

            int shown = 0;
            if (postPrefab != null)
            {
                for (int i = 0; i < posts.Count; i++)
                {
                    var post = posts[i];
                    if (post == null) continue;
                    while (m_Posts.Count <= shown)
                    {
                        var item = Instantiate(postPrefab, mapArea);
                        item.Clicked += name => PostClicked?.Invoke(name);
                        m_Posts.Add(item);
                    }
                    var ui = m_Posts[shown++];
                    if (!ui.gameObject.activeSelf) ui.gameObject.SetActive(true);
                    ui.Rect.anchoredPosition = ToMap(post.transform.position);
                    string postName = post.PostName;
                    bool canPick = selectable != null && selectable.Contains(postName);
                    ui.SetPost(postName, postColor(post), canPick, canPick && postName == selected);
                }
            }
            for (int i = shown; i < m_Posts.Count; i++) m_Posts[i].gameObject.SetActive(false);

            shown = 0;
            if (showTeammates && teammateDotPrefab != null && viewer != null)
            {
                var players = ConquestPlayer.All;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p == viewer || p.CurrentTeam != viewer.CurrentTeam || p.Health == null || p.Health.IsDead) continue;
                    while (m_Dots.Count <= shown) m_Dots.Add(Instantiate(teammateDotPrefab, mapArea));
                    var dot = m_Dots[shown++];
                    if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
                    dot.rectTransform.anchoredPosition = ToMap(p.transform.position);
                    if (dot.color != teammateColor) dot.color = teammateColor;
                }
            }
            for (int i = shown; i < m_Dots.Count; i++)
            {
                if (m_Dots[i].gameObject.activeSelf) m_Dots[i].gameObject.SetActive(false);
            }

            if (viewerArrow != null)
            {
                bool alive = viewer != null && viewer.Health != null && !viewer.Health.IsDead;
                if (viewerArrow.gameObject.activeSelf != alive) viewerArrow.gameObject.SetActive(alive);
                if (alive)
                {
                    viewerArrow.anchoredPosition = ToMap(viewer.transform.position);
                    viewerArrow.localRotation = Quaternion.Euler(0f, 0f, -viewer.transform.eulerAngles.y);
                    viewerArrow.SetAsLastSibling();
                }
            }
        }

        // A square around every post so the map keeps its proportions on any panel size.
        private void ComputeBounds(IReadOnlyList<ConquestCommandPost> posts)
        {
            m_BoundsPostCount = posts.Count;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < posts.Count; i++)
            {
                if (posts[i] == null) continue;
                Vector3 p = posts[i].transform.position;
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            if (min.x > max.x)
            {
                min = Vector2.zero;
                max = Vector2.zero;
            }
            Vector2 centre = (min + max) * 0.5f;
            m_Size = Mathf.Max(max.x - min.x, max.y - min.y) + boundsPadding * 2f;
            if (m_Size < 1f) m_Size = 1f;
            m_Min = centre - Vector2.one * (m_Size * 0.5f);
        }

        // World XZ to a position in mapArea, measured from its centre (children anchor at the centre).
        private Vector2 ToMap(Vector3 world)
        {
            Rect r = mapArea.rect;
            float side = Mathf.Min(r.width, r.height);
            float u = Mathf.Clamp01((world.x - m_Min.x) / m_Size) - 0.5f;
            float v = Mathf.Clamp01((world.z - m_Min.y) / m_Size) - 0.5f;
            return new Vector2(u * side, v * side);
        }
    }
}
