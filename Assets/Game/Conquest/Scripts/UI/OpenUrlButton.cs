using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>Opens a web page in the player's browser when its button is clicked (credits licence links).</summary>
    [RequireComponent(typeof(Button))]
    public class OpenUrlButton : MonoBehaviour
    {
        [Tooltip("The page to open. Only https links are opened.")]
        public string url;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(Open);
        }

        private void Open()
        {
            if (!string.IsNullOrEmpty(url) && url.StartsWith("https://")) Application.OpenURL(url);
        }
    }
}
