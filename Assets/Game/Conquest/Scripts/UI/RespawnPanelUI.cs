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
        public Button spawnPostButtonPrefab;
        public Transform spawnPostContainer;

        private readonly List<Button> m_SpawnPostButtons = new List<Button>();

        public void SetTimer(string text)
        {
            if (txt_Timer != null) txt_Timer.text = text;
        }

        /// <summary>Returns the spawn post button at index, creating buttons from the prefab as needed.</summary>
        public Button GetSpawnPostButton(int index)
        {
            if (spawnPostButtonPrefab == null || spawnPostContainer == null) return null;

            while (m_SpawnPostButtons.Count <= index)
            {
                m_SpawnPostButtons.Add(Instantiate(spawnPostButtonPrefab, spawnPostContainer));
            }
            return m_SpawnPostButtons[index];
        }

        public void SetVisibleSpawnPostCount(int count)
        {
            for (int i = 0; i < m_SpawnPostButtons.Count; i++)
            {
                m_SpawnPostButtons[i].gameObject.SetActive(i < count);
            }
        }

        // Show and hide the respawn panel
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
