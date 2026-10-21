using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Classes;

namespace Game.Conquest
{
    public class RespawnPanelUI : MonoBehaviour
    {
        [Header("Text Elements")]
        public TextMeshProUGUI txt_Timer;

        [Header("Classes")]
        [Tooltip("Builds one button per class from the class list.")]
        public ClassPickerUI classPicker;

        [Header("Deploy")]
        [Tooltip("Spawns the player with the selected class at the selected post.")]
        public Button btn_Deploy;

        [Header("Spawn Post Buttons")]
        public SpawnPostButtonUI spawnPostButtonPrefab;
        public Transform spawnPostContainer;

        [Header("Spawn Map")]
        [Tooltip("Command map where clicking a friendly post picks it, same as its list button.")]
        public CommandMapUI commandMap;

        [Header("Colors")]
        public Color normalColor = new Color(0.25f, 0.30f, 0.40f);
        public Color selectedColor = new Color(0.2f, 0.8f, 0.35f);

        /// <summary>Raised with the class index when a class button is clicked.</summary>
        public event Action<int> ClassSelected;
        /// <summary>Raised with the post name when a spawn post button is clicked.</summary>
        public event Action<string> SpawnPostSelected;
        /// <summary>Raised when the deploy button is clicked.</summary>
        public event Action DeployClicked;

        private readonly List<SpawnPostButtonUI> m_SpawnPostButtons = new List<SpawnPostButtonUI>();

        private void Awake()
        {
            if (commandMap != null) commandMap.PostClicked += postName => SpawnPostSelected?.Invoke(postName);
            if (btn_Deploy != null) btn_Deploy.onClick.AddListener(() => DeployClicked?.Invoke());
            if (classPicker != null) classPicker.ClassSelected += index => ClassSelected?.Invoke(index);
        }

        public void SetTimer(string text)
        {
            if (txt_Timer != null) txt_Timer.text = text;
        }

        public void SetDeployInteractable(bool interactable)
        {
            if (btn_Deploy != null && btn_Deploy.interactable != interactable) btn_Deploy.interactable = interactable;
        }

        public void SetClasses(IReadOnlyList<ConquestClassData> classes, int selectedIndex)
        {
            if (classPicker == null) return;
            classPicker.SetClasses(classes);
            classPicker.SetSelected(selectedIndex);
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
