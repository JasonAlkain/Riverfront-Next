using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class MultiplayerPanelUI : MonoBehaviour
    {
        [Header("Buttons")]
        public Button btn_Host;
        public Button btn_BrowseGames;
        public Button btn_Join;
        public Button btn_Back;

        // Show and hide the multiplayer panel
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
