using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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

        [Header("Click guard")]
        [Tooltip("Placeholder: seconds after the panel opens before its buttons take clicks, so a shot fired as you die can't pick a class or post.")]
        public float clickGuardSeconds = 0.6f;

        [Header("Class sheet")]
        [Tooltip("Optional: health, description and loadout of the chosen class.")]
        public ClassDetailsUI classDetails;

        [Header("Selected spawn")]
        [Tooltip("Optional: the post the player will deploy at.")]
        public TextMeshProUGUI txt_SelectedPost;
        [Tooltip("Optional: a line under it on what that choice means.")]
        public TextMeshProUGUI txt_SelectedPostInfo;
        [Tooltip("Optional: picture of the battlefield beside the spawn choice (the map's preview).")]
        public Image img_SpawnPreview;
        public GameObject spawnPreviewPlaceholder;

        [Header("Change class at a post")]
        [Tooltip("Hidden while changing class at a post: they are only about where to deploy.")]
        public GameObject[] hiddenWhenChangingClass;
        [Tooltip("The deploy button's label, which reads CHANGE CLASS in that mode.")]
        public TextMeshProUGUI txt_DeployLabel;

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
        private CanvasGroup m_CanvasGroup;
        private float m_GuardUntil;
        private bool m_WaitForRelease;

        private void Awake()
        {
            m_CanvasGroup = GetComponent<CanvasGroup>();
            if (m_CanvasGroup == null) m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
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

        private string m_ShownPost;
        private bool m_ShownHasPosts;

        public void SetSpawnPreview(Sprite preview)
        {
            if (img_SpawnPreview != null && img_SpawnPreview.sprite != preview)
            {
                img_SpawnPreview.sprite = preview;
                img_SpawnPreview.enabled = preview != null;
            }
            if (spawnPreviewPlaceholder != null && spawnPreviewPlaceholder.activeSelf != (preview == null)) spawnPreviewPlaceholder.SetActive(preview == null);
        }

        private bool m_ChangingClass;
        private string m_DeployLabel;

        /// <summary>
        /// Switches between deploying (dead) and changing class at a friendly post (alive). Called
        /// every frame while shown; the label is re-applied because a localized label can refresh
        /// itself after the panel is enabled.
        /// </summary>
        public void SetChangeClassMode(bool changing, string postName = "")
        {
            if (changing != m_ChangingClass)
            {
                m_ChangingClass = changing;
                if (hiddenWhenChangingClass != null)
                {
                    foreach (var go in hiddenWhenChangingClass)
                    {
                        if (go != null) go.SetActive(!changing);
                    }
                }
                m_ShownPost = null;
            }
            if (txt_DeployLabel != null)
            {
                if (m_DeployLabel == null && !changing) m_DeployLabel = txt_DeployLabel.text;
                string label = changing ? "CHANGE CLASS" : m_DeployLabel;
                if (label != null && txt_DeployLabel.text != label) txt_DeployLabel.text = label;
            }
            if (!changing) return;
            postName = postName ?? "";
            if (postName == m_ShownPost) return;
            m_ShownPost = postName;
            if (txt_SelectedPost != null) txt_SelectedPost.text = $"AT {postName.ToUpperInvariant()}";
            if (txt_SelectedPostInfo != null) txt_SelectedPostInfo.text = "Your new class starts at once, at full health with a fresh loadout.";
        }

        public void SetClassDetails(ConquestClassData data)
        {
            if (classDetails != null) classDetails.Show(data);
        }

        /// <summary>Names the chosen post; an empty name means the server picks (or the team base).</summary>
        public void SetSelectedPost(string postName, bool hasFriendlyPosts)
        {
            if (m_ChangingClass) return;
            // Called every frame by the HUD; only rebuild the text when the choice changes.
            postName = postName ?? "";
            if (postName == m_ShownPost && hasFriendlyPosts == m_ShownHasPosts) return;
            m_ShownPost = postName;
            m_ShownHasPosts = hasFriendlyPosts;
            if (txt_SelectedPost != null)
            {
                txt_SelectedPost.text = !string.IsNullOrEmpty(postName) ? postName.ToUpperInvariant()
                    : hasFriendlyPosts ? "ANY FRIENDLY POST" : "TEAM BASE";
            }
            if (txt_SelectedPostInfo != null)
            {
                txt_SelectedPostInfo.text = !string.IsNullOrEmpty(postName) ? "Deploy at this command post."
                    : hasFriendlyPosts ? "Pick a post on the map, or deploy and the nearest safe post is chosen."
                    : "No friendly post is safe: you deploy at your team's base.";
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

        // Buttons stay locked (and greyed) for the guard time and until every mouse button held
        // when the panel opened is let go: a held trigger never turns into a click on a class.
        private void Update()
        {
            if (m_WaitForRelease)
            {
                Mouse mouse = Mouse.current;
                m_WaitForRelease = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
            }
            bool open = !m_WaitForRelease && Time.unscaledTime >= m_GuardUntil;
            if (m_CanvasGroup.interactable != open) m_CanvasGroup.interactable = open;
            if (m_CanvasGroup.blocksRaycasts != open) m_CanvasGroup.blocksRaycasts = open;
        }

        // Show and hide the respawn panel
        public void Show()
        {
            if (gameObject.activeSelf) return;
            m_GuardUntil = Time.unscaledTime + clickGuardSeconds;
            m_WaitForRelease = true;
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.interactable = false;
                m_CanvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(true);
        }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
