using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Conquest.Classes;

namespace Game.Conquest
{
    /// <summary>
    /// The deploy screen's class sheet: name, health, description and loadout of the class the
    /// player will deploy as, all read from its ConquestClassData.
    /// </summary>
    public class ClassDetailsUI : MonoBehaviour
    {
        public TextMeshProUGUI txt_ClassName;
        public TextMeshProUGUI txt_Health;
        public TextMeshProUGUI txt_Description;
        public TextMeshProUGUI txt_PrimaryName;
        public TextMeshProUGUI txt_PrimaryInfo;
        public TextMeshProUGUI txt_SidearmName;
        public TextMeshProUGUI txt_GadgetName;
        [Tooltip("Optional: tinted with the class accent colour.")]
        public Image img_Accent;
        [Tooltip("Optional: the class portrait; the placeholder shows while a class has none.")]
        public Image img_Portrait;
        public GameObject portraitPlaceholder;
        [Tooltip("Optional: the primary weapon's icon.")]
        public Image img_PrimaryIcon;

        private ConquestClassData m_Shown;

        public void Show(ConquestClassData data)
        {
            if (data == null || data == m_Shown) return;
            m_Shown = data;
            Set(txt_ClassName, data.className.ToUpperInvariant());
            Set(txt_Health, $"{data.maxHealth} HP");
            Set(txt_Description, data.description);
            Set(txt_PrimaryName, data.weaponName);
            Set(txt_PrimaryInfo, PrimaryInfo(data));
            Set(txt_SidearmName, data.sidearm != null && data.sidearm.kind != SidearmKind.None ? data.sidearm.weaponName : "None");
            Set(txt_GadgetName, Gadget(data));
            if (img_Accent != null)
            {
                Color c = data.classAccentColor;
                img_Accent.color = new Color(c.r, c.g, c.b, img_Accent.color.a);
            }
            if (img_Portrait != null)
            {
                img_Portrait.sprite = data.portrait;
                img_Portrait.enabled = data.portrait != null;
            }
            if (portraitPlaceholder != null) portraitPlaceholder.SetActive(data.portrait == null);
            if (img_PrimaryIcon != null)
            {
                img_PrimaryIcon.sprite = data.weaponIcon;
                img_PrimaryIcon.enabled = data.weaponIcon != null;
            }
        }

        private static string PrimaryInfo(ConquestClassData d)
        {
            string fire = d.bulletsPerShot > 1 ? $"{d.bulletsPerShot} pellets" : d.isAutomatic ? "Automatic" : "Semi-automatic";
            string sight = d.hasScope ? ", scoped" : d.hasZoom ? ", can aim" : "";
            return $"{fire}, {d.damagePerShot} damage{sight}";
        }

        private static string Gadget(ConquestClassData d)
        {
            if (d.hasSupportStation) return "Support station";
            if (d.throwable != ThrowableKind.None && !string.IsNullOrEmpty(d.throwableName)) return $"{d.throwableName} x{d.throwableCount}";
            return "None";
        }

        private static void Set(TextMeshProUGUI text, string value)
        {
            if (text != null) text.text = value ?? "";
        }
    }
}
