using System.Collections.Generic;
using UnityEngine;

namespace Game.Conquest
{
    public class CommandPostsRowUI : MonoBehaviour
    {
        [Header("Command Post Icon Prefab")]
        public CommandPostIconUI postIconPrefab;

        [Header("Container")]
        public Transform postContainer;

        private readonly List<CommandPostIconUI> m_Icons = new List<CommandPostIconUI>();

        /// <summary>Returns the icon for a post, creating icons from the prefab as more posts appear.</summary>
        public CommandPostIconUI GetIcon(int index)
        {
            if (postIconPrefab == null || postContainer == null) return null;

            while (m_Icons.Count <= index)
            {
                m_Icons.Add(Instantiate(postIconPrefab, postContainer));
            }
            return m_Icons[index];
        }

        // Hide icons left over from a map with more posts.
        public void SetVisibleCount(int count)
        {
            for (int i = 0; i < m_Icons.Count; i++)
            {
                m_Icons[i].gameObject.SetActive(i < count);
            }
        }

        // Show and hide the command posts row
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
