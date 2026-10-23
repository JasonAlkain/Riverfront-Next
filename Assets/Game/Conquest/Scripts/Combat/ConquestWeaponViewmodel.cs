using UnityEngine;
using Game.Conquest.Classes;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Presentation only. It shows the model of the weapon in hand, first- and third-person. On
    /// the player's own screen it also lowers the arms while one weapon goes away and raises them
    /// as the next comes up, and brings the weapon's sight to the eye while aiming down sights.
    /// Timing comes from ConquestWeapon, which also blocks firing for the whole switch.
    /// </summary>
    [RequireComponent(typeof(ConquestWeapon))]
    public class ConquestWeaponViewmodel : MonoBehaviour
    {
        [Tooltip("First-person rig that drops out of view during a switch and moves to the eye while aiming.")]
        [SerializeField] private Transform viewmodelRoot;
        [Tooltip("Placeholder: how far the rig drops, in its parent's space.")]
        [SerializeField] private Vector3 loweredOffset = new Vector3(0f, -0.3f, -0.05f);
        [Tooltip("Placeholder: degrees the rig tips forward as it drops.")]
        [SerializeField] private float loweredPitch = 40f;

        [Header("Models (first- and third-person)")]
        [SerializeField] private GameObject[] primaryModels;
        [SerializeField] private GameObject[] pistolModels;
        [SerializeField] private GameObject[] repairToolModels;

        [Header("Aim Down Sights")]
        [Tooltip("Where the eye looks along each first-person model: forward down the barrel, up out of the sight. " +
                 "Aiming puts it on the screen centre line. Empty: that weapon keeps its hip pose while aiming.")]
        [SerializeField] private Transform primarySight;
        [SerializeField] private Transform pistolSight;
        [Tooltip("Placeholder: metres between the eye and the sight while fully aimed.")]
        [SerializeField] private float eyeRelief = 0.2f;
        [Tooltip("Seconds after a shot before the hip pose is measured again, so recoil doesn't move the aim pose.")]
        [SerializeField] private float settleSeconds = 0.3f;

        private ConquestWeapon m_Weapon;
        private Vector3 m_RestPosition;
        private Quaternion m_RestRotation;
        private int m_ShownKey = -1;

        // The rig's pose (in its parent's space, which is the camera's) that puts the shown sight on
        // the eye line. Measured from the animated hip pose; see MeasureAimPose.
        private Transform m_Sight;
        private bool m_HasAimPose;
        private Vector3 m_AimPosition;
        private Quaternion m_AimRotation = Quaternion.identity;

        private Renderer[] m_FirstPersonRenderers;
        private bool m_HiddenForScope;

        private void Awake()
        {
            m_Weapon = GetComponent<ConquestWeapon>();
            if (viewmodelRoot != null)
            {
                m_RestPosition = viewmodelRoot.localPosition;
                m_RestRotation = viewmodelRoot.localRotation;
                m_FirstPersonRenderers = viewmodelRoot.GetComponentsInChildren<Renderer>(true);
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
                m_Sight = kind == SidearmKind.None ? primarySight : kind == SidearmKind.Blaster ? pistolSight : null;
                m_HasAimPose = false;
            }

            if (viewmodelRoot == null) return;

            float aim = Mathf.SmoothStep(0f, 1f, m_Weapon.Aim01);
            float lowered = Mathf.SmoothStep(0f, 1f, m_Weapon.Lowered01);
            // Measured at the hip and at rest (the Animator has posed the bones for this frame by now).
            if (m_Sight != null && aim <= 0f && lowered <= 0f && Time.time - m_Weapon.LastLocalShotTime > settleSeconds)
            {
                MeasureAimPose();
            }

            Vector3 position = m_RestPosition;
            Quaternion rotation = m_RestRotation;
            if (m_HasAimPose && aim > 0f)
            {
                position = Vector3.Lerp(m_RestPosition, m_AimPosition + m_Weapon.ShownAdsOffset, aim);
                rotation = Quaternion.Slerp(m_RestRotation, m_AimRotation, aim);
            }
            viewmodelRoot.localPosition = position + loweredOffset * lowered;
            viewmodelRoot.localRotation = rotation * Quaternion.Euler(loweredPitch * lowered, 0f, 0f);

            // A scope draws its own view: the rifle would sit in front of the lens.
            SetHiddenForScope(m_Weapon.IsScoped);
        }

        // The sight's pose relative to the rig root comes from the animation alone, so it can be read
        // whatever pose the root itself has. The aim pose turns the root so the sight faces straight
        // down the camera's forward axis, and moves it so the sight sits eyeRelief in front of the eye.
        private void MeasureAimPose()
        {
            Vector3 sightInRoot = viewmodelRoot.InverseTransformPoint(m_Sight.position);
            Quaternion sightRotInRoot = Quaternion.Inverse(viewmodelRoot.rotation) * m_Sight.rotation;
            m_AimRotation = Quaternion.Inverse(sightRotInRoot);
            m_AimPosition = new Vector3(0f, 0f, eyeRelief) - m_AimRotation * Vector3.Scale(viewmodelRoot.localScale, sightInRoot);
            m_HasAimPose = true;
        }

        private void SetHiddenForScope(bool hidden)
        {
            if (hidden == m_HiddenForScope || m_FirstPersonRenderers == null) return;
            m_HiddenForScope = hidden;
            for (int i = 0; i < m_FirstPersonRenderers.Length; i++)
            {
                if (m_FirstPersonRenderers[i] != null) m_FirstPersonRenderers[i].enabled = !hidden;
            }
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
