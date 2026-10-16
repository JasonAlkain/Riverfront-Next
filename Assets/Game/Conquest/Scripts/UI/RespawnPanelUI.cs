using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class RespawnPanelUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Timer;

        [Header("Class Buttons")]
        [Tooltip("Class select buttons, ordered by class index (Assault, Heavy, Engineer, Recon).")]
        public Button[] btn_Classes;

        [Header("Spawn Post Buttons")]
        public SpawnPostButtonUI spawnPostButtonPrefab;
        public Transform spawnPostContainer;

        [Header("Colors")]
        public Color normalColor = new Color(0.25f, 0.30f, 0.40f);
        public Color selectedColor = new Color(0.2f, 0.8f, 0.35f);

        /// <summary>Raised with the class index when a class button is clicked.</summary>
        public event Action<int> ClassSelected;
        /// <summary>Raised with the post name when a spawn post button is clicked.</summary>
        public event Action<string> SpawnPostSelected;

        private readonly List<SpawnPostButtonUI> m_SpawnPostButtons = new List<SpawnPostButtonUI>();
        private int m_SelectedClass = -1;

        private void Awake()
        {
            if (btn_Classes == null) return;
            for (int i = 0; i < btn_Classes.Length; i++)
            {
                if (btn_Classes[i] == null) continue;
                int classIndex = i;
                btn_Classes[i].onClick.AddListener(() => ClassSelected?.Invoke(classIndex));
            }
        }

        public void SetTimer(string text)
        {
            if (txt_Timer != null) txt_Timer.text = text;
        }

        public void SetSelectedClass(int classIndex)
        {
            if (classIndex == m_SelectedClass || btn_Classes == null) return;
            m_SelectedClass = classIndex;

            for (int i = 0; i < btn_Classes.Length; i++)
            {
                if (btn_Classes[i] != null && btn_Classes[i].targetGraphic != null)
                {
                    btn_Classes[i].targetGraphic.color = i == classIndex ? selectedColor : normalColor;
                }
            }
        }

        /// <summary>Shows one button per friendly post, creating buttons from the prefab as needed.</summary>
        public void SetSpawnPosts(IReadOnlyList<string> postNames, string selectedPost)
        {
            if (spawnPostButtonPrefab == null || spawnPostContainer == null) return;

            while (m_SpawnPostButtons.Count < postNames.Count)
            {
                var button = Instantiate(spawnPostButtonPrefab, spawnPostContainer);
                button.Clicked += postName => SpawnPostSelected?.Invoke(postName);
                m_SpawnPostButtons.Add(button);
            }

            for (int i = 0; i < m_SpawnPostButtons.Count; i++)
            {
                bool visible = i < postNames.Count;
                if (m_SpawnPostButtons[i].gameObject.activeSelf != visible) m_SpawnPostButtons[i].gameObject.SetActive(visible);
                if (visible) m_SpawnPostButtons[i].SetPost(postNames[i], postNames[i] == selectedPost, normalColor, selectedColor);
            }
        }

        // Show and hide the respawn panel
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
