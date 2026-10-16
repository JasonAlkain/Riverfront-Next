using Game.Conquest.Gameplay;
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

        public void SetPlayerInfo(string playerName, bool isReady)
        {
            txt_PlayerName.text = playerName;
            tgl_Ready.isOn = isReady;
            // Display only: ready state is server-owned and changed through the lobby's Ready button.
            tgl_Ready.interactable = false;
        }
    }

    
}
