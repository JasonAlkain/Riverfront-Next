using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>
    /// One player in a lobby team list: a team-coloured edge, name, class, a host badge, a marker on
    /// your own card, and a ready badge. Only your own card's ready badge is clickable.
    /// </summary>
    public class PlayerCardUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txt_PlayerName;
        [Tooltip("Optional: the player's class under the name.")]
        [SerializeField] private TextMeshProUGUI txt_ClassName;
        [SerializeField] private Toggle tgl_Ready;
        [Tooltip("Optional: the ready badge's words, which say READY or NOT READY.")]
        [SerializeField] private TextMeshProUGUI txt_ReadyLabel;
        [Tooltip("Optional: the ready badge's background, tinted by state.")]
        [SerializeField] private Image img_ReadyBadge;
        [Tooltip("Optional: the coloured edge, tinted with the team colour.")]
        [SerializeField] private Image img_TeamStripe;
        [Tooltip("Optional: shown on the host's card.")]
        [SerializeField] private GameObject hostBadge;
        [Tooltip("Optional: shown on your own card.")]
        [SerializeField] private GameObject localMarker;

        [Header("Ready colours")]
        public Color readyColor = new Color(0.2f, 0.75f, 0.4f, 1f);
        public Color notReadyColor = new Color(0.2f, 0.25f, 0.32f, 1f);

        private Action m_OnReadyToggled;

        private void Awake()
        {
            if (tgl_Ready == null) return;
            // Cards start unready until the server says otherwise.
            tgl_Ready.SetIsOnWithoutNotify(false);
            tgl_Ready.onValueChanged.AddListener(_ => m_OnReadyToggled?.Invoke());
        }

        /// <param name="onReadyToggled">Set only on the local player's own card, which makes its toggle clickable.</param>
        public void SetPlayerInfo(string playerName, bool isReady, Action onReadyToggled = null)
        {
            SetPlayerInfo(playerName, "", false, onReadyToggled != null, isReady, Color.clear, onReadyToggled);
        }

        public void SetPlayerInfo(string playerName, string className, bool isHost, bool isLocal, bool isReady,
                                  Color teamColor, Action onReadyToggled)
        {
            txt_PlayerName.text = playerName;
            if (txt_ClassName != null)
            {
                txt_ClassName.text = string.IsNullOrEmpty(className) ? "" : className.ToUpperInvariant();
            }
            if (hostBadge != null) hostBadge.SetActive(isHost);
            if (localMarker != null) localMarker.SetActive(isLocal);
            if (img_TeamStripe != null && teamColor.a > 0f)
            {
                teamColor.a = 1f;
                img_TeamStripe.color = teamColor;
            }
            m_OnReadyToggled = onReadyToggled;

            // Ready state is server-owned: show it without raising the click callback, and let the
            // server's reply (which rebuilds the cards) be what actually flips it.
            if (tgl_Ready != null)
            {
                tgl_Ready.SetIsOnWithoutNotify(isReady);
                tgl_Ready.interactable = onReadyToggled != null;
            }
            if (txt_ReadyLabel != null) txt_ReadyLabel.text = isReady ? "READY" : "NOT READY";
            if (img_ReadyBadge != null) img_ReadyBadge.color = isReady ? readyColor : notReadyColor;
        }
    }
}
