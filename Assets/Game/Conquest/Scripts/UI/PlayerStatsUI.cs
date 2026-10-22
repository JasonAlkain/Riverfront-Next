using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>
    /// Bottom-left card: team and class, a health bar, a magazine bar (or a heat gauge for teams
    /// whose weapons overheat) with their numbers, and the action hints line. Every setter only touches the UI when its value changes, so the HUD can
    /// call them every frame without allocating strings.
    /// </summary>
    public class PlayerStatsUI : MonoBehaviour
    {
        [Header("Images")]
        public Image img_Background;
        [Tooltip("Image Type Filled (Horizontal): fillAmount is the health fraction.")]
        public Image img_HealthFill;
        [Tooltip("Image Type Filled (Horizontal): fillAmount is the magazine fraction.")]
        public Image img_AmmoFill;
        [Tooltip("Team shape, so the team doesn't depend on colour alone.")]
        public Image img_TeamIcon;

        [Header("Text Elements")]
        public TextMeshProUGUI txt_TeamClass;
        public TextMeshProUGUI txt_Health;
        public TextMeshProUGUI txt_Ammo;
        [Tooltip("Reload / station / scope key hints.")]
        public TextMeshProUGUI txt_Hints;

        [Header("Weapon model")]
        [Tooltip("Shown for magazine weapons (the ammo bar and its count).")]
        public GameObject[] magazineNodes;
        [Tooltip("Shown instead for weapons that overheat.")]
        public HeatGaugeUI heatGauge;

        [Header("Colors")]
        public Color healthyColor = new Color(0.3f, 0.85f, 0.4f);
        public Color lowHealthColor = new Color(0.95f, 0.25f, 0.2f);
        public Color ammoColor = new Color(0.95f, 0.8f, 0.3f);
        public Color reloadingColor = new Color(0.55f, 0.6f, 0.7f);

        private int m_Health = -1;
        private int m_MaxHealth = -1;
        private bool m_Low;
        private int m_Ammo = -1;
        private int m_Magazine = -1;
        private bool m_Reloading;
        private string m_AmmoLabel;
        private string m_TeamName;
        private string m_TeamClass;
        private string m_Hints;
        private int m_UsesHeat = -1;

        public void SetTeamColor(Color color)
        {
            if (img_Background != null && img_Background.color != color) img_Background.color = color;
        }

        /// <summary>"RED  |  Assault". Pass the same string instances each frame; only a change rebuilds it.</summary>
        public void SetTeamIcon(Sprite icon)
        {
            if (img_TeamIcon != null && img_TeamIcon.sprite != icon) img_TeamIcon.sprite = icon;
        }

        public void SetTeamClass(string teamName, string className)
        {
            if (ReferenceEquals(teamName, m_TeamName) && ReferenceEquals(className, m_TeamClass)) return;
            m_TeamName = teamName;
            m_TeamClass = className;
            if (txt_TeamClass != null) txt_TeamClass.text = string.IsNullOrEmpty(className) ? teamName : $"{teamName}  |  {className}";
        }

        public void SetHealth(int current, int max, bool low)
        {
            if (current == m_Health && max == m_MaxHealth && low == m_Low) return;
            m_Health = current;
            m_MaxHealth = max;
            m_Low = low;

            if (txt_Health != null) txt_Health.SetText("{0} / {1}", (float)current, (float)max);
            if (img_HealthFill != null)
            {
                img_HealthFill.fillAmount = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
                img_HealthFill.color = low ? lowHealthColor : healthyColor;
            }
        }

        public void SetAmmo(int current, int magazine, bool reloading)
        {
            if (current == m_Ammo && magazine == m_Magazine && reloading == m_Reloading) return;
            m_Ammo = current;
            m_Magazine = magazine;
            m_Reloading = reloading;

            if (txt_Ammo != null)
            {
                if (reloading) txt_Ammo.SetText("RELOADING");
                else txt_Ammo.SetText("{0} / {1}", (float)current, (float)magazine);
            }
            if (img_AmmoFill != null)
            {
                img_AmmoFill.fillAmount = magazine > 0 ? Mathf.Clamp01((float)current / magazine) : 0f;
                img_AmmoFill.color = reloading ? reloadingColor : ammoColor;
            }
        }

        /// <summary>A weapon without ammo (the repair tool): a full bar with a label instead of a count.</summary>
        public void SetAmmoLabel(string label)
        {
            if (m_Ammo == int.MinValue && ReferenceEquals(label, m_AmmoLabel)) return;
            m_Ammo = int.MinValue;
            m_AmmoLabel = label;
            if (txt_Ammo != null) txt_Ammo.text = label;
            if (img_AmmoFill != null)
            {
                img_AmmoFill.fillAmount = 1f;
                img_AmmoFill.color = ammoColor;
            }
        }

        /// <summary>Swaps the magazine bar for the heat gauge (or back). Only acts when the model changes.</summary>
        public void SetWeaponModel(bool usesHeat)
        {
            int state = usesHeat ? 1 : 0;
            if (state == m_UsesHeat) return;
            m_UsesHeat = state;
            if (magazineNodes != null)
            {
                for (int i = 0; i < magazineNodes.Length; i++)
                {
                    if (magazineNodes[i] != null) magazineNodes[i].SetActive(!usesHeat);
                }
            }
            if (heatGauge != null)
            {
                if (usesHeat) heatGauge.Show();
                else heatGauge.Hide();
            }
        }

        public void SetHeat(int percent, bool overheated)
        {
            if (heatGauge != null) heatGauge.SetHeat(percent, overheated);
        }

        public void SetHints(string text)
        {
            if (text == m_Hints) return;
            m_Hints = text;
            if (txt_Hints != null) txt_Hints.text = text;
        }

        // Show and hide the stats card
        public void Show() { if (!gameObject.activeSelf) gameObject.SetActive(true); }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
    }
}
