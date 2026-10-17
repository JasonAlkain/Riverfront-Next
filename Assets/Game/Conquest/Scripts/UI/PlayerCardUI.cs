using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class PlayerCardUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txt_PlayerName;
        [SerializeField] private Toggle tgl_Ready;

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
            txt_PlayerName.text = playerName;
            m_OnReadyToggled = onReadyToggled;

            // Ready state is server-owned: show it without raising the click callback, and let the
            // server's reply (which rebuilds the cards) be what actually flips it.
            tgl_Ready.SetIsOnWithoutNotify(isReady);
            tgl_Ready.interactable = onReadyToggled != null;
        }
    }
}
