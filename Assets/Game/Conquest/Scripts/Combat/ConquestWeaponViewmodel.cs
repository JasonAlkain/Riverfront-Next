using UnityEngine;
using Game.Conquest.Classes;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Presentation only. It shows the model of the weapon in hand, first- and third-person, and
    /// on the switching player's own screen it lowers the arms while one weapon goes away and
    /// raises them as the next comes up. Timing comes from ConquestWeapon, which also blocks
    /// firing for the whole switch.
    /// </summary>
    [RequireComponent(typeof(ConquestWeapon))]
    public class ConquestWeaponViewmodel : MonoBehaviour
    {
        [Tooltip("First-person rig that drops out of view during a switch.")]
        [SerializeField] private Transform viewmodelRoot;
        [Tooltip("Placeholder: how far the rig drops, in its parent's space.")]
        [SerializeField] private Vector3 loweredOffset = new Vector3(0f, -0.3f, -0.05f);
        [Tooltip("Placeholder: degrees the rig tips forward as it drops.")]
        [SerializeField] private float loweredPitch = 40f;

        [Header("Models (first- and third-person)")]
        [SerializeField] private GameObject[] primaryModels;
        [SerializeField] private GameObject[] pistolModels;
        [SerializeField] private GameObject[] repairToolModels;

        private ConquestWeapon m_Weapon;
        private Vector3 m_RestPosition;
        private Quaternion m_RestRotation;
        private int m_ShownKey = -1;

        private void Awake()
        {
            m_Weapon = GetComponent<ConquestWeapon>();
            if (viewmodelRoot != null)
            {
                m_RestPosition = viewmodelRoot.localPosition;
                m_RestRotation = viewmodelRoot.localRotation;
            }
        }

        private void LateUpdate()
        {
            ConquestWeapon.WeaponSlot slot = m_Weapon.ShownSlot;
            SidearmKind kind = slot == ConquestWeapon.WeaponSlot.Sidearm ? m_Weapon.SidearmKind : SidearmKind.None;
            int key = (int)kind;
            if (key != m_ShownKey)
            {
                m_ShownKey = key;
                SetActive(primaryModels, kind == SidearmKind.None);
                SetActive(pistolModels, kind == SidearmKind.Blaster);
                SetActive(repairToolModels, kind == SidearmKind.RepairTool);
            }

            if (viewmodelRoot == null) return;
            float t = Mathf.SmoothStep(0f, 1f, m_Weapon.Lowered01);
            viewmodelRoot.localPosition = m_RestPosition + loweredOffset * t;
            viewmodelRoot.localRotation = m_RestRotation * Quaternion.Euler(loweredPitch * t, 0f, 0f);
        }

        private static void SetActive(GameObject[] models, bool active)
        {
            if (models == null) return;
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] != null && models[i].activeSelf != active) models[i].SetActive(active);
            }
        }
    }
}
