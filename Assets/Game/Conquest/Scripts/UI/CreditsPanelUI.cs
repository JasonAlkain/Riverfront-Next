using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    public class CreditsPanelUI : MonoBehaviour
    {
        public Button btn_Back;

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
