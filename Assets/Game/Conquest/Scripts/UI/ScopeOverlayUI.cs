using UnityEngine;

namespace Game.Conquest
{
    /// <summary>Full-screen scope view (dark surround, lens ring and reticle) shown while aiming a scoped weapon.</summary>
    public class ScopeOverlayUI : MonoBehaviour
    {
        [Tooltip("Root of the overlay graphics; toggled on and off.")]
        public GameObject overlay;

        private bool m_Visible;

        private void Awake()
        {
            if (overlay != null) overlay.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (visible == m_Visible || overlay == null) return;
            m_Visible = visible;
            overlay.SetActive(visible);
        }
    }
}
