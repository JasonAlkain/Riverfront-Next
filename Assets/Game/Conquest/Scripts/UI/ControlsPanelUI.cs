using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Game.Conquest.Controls;

namespace Game.Conquest
{
    /// <summary>
    /// Keyboard and mouse rebinding, opened from the Settings panel (SettingsPanelUI.btn_Controls).
    /// Rows are RebindRow prefab instances in two columns, movement and combat; rebinds are saved at
    /// once by ConquestInput. A key used by two rows is shown in a warning colour. Pause stays on
    /// Escape so the menu can always be reached.
    /// </summary>
    public class ControlsPanelUI : MonoBehaviour
    {
        [Header("Rows")]
        public RebindRowUI rowPrefab;
        public Transform movementRows;
        public Transform combatRows;

        [Header("Buttons")]
        public Button btn_Reset;
        public Button btn_Back;

        [Header("Navigation")]
        [Tooltip("Panel shown again when this one closes.")]
        public GameObject returnPanel;

        private readonly List<RebindRowUI> m_Rows = new List<RebindRowUI>();
        private InputActionRebindingExtensions.RebindingOperation m_Rebind;

        private void Awake()
        {
            if (btn_Back != null) btn_Back.onClick.AddListener(Close);
            if (btn_Reset != null) btn_Reset.onClick.AddListener(ResetAll);
        }

        private void OnEnable()
        {
            if (m_Rows.Count == 0) BuildRows();
            RefreshRows();
        }

        private void OnDisable()
        {
            CancelRebind();
        }

        public void Open()
        {
            if (returnPanel != null) returnPanel.SetActive(false);
            gameObject.SetActive(true);
        }

        public void Close()
        {
            gameObject.SetActive(false);
            if (returnPanel != null) returnPanel.SetActive(true);
        }

        private void BuildRows()
        {
            if (rowPrefab == null || movementRows == null || combatRows == null) return;
            var g = ConquestInput.Gameplay;

            // Movement is a composite: one row per direction.
            AddCompositePart(movementRows, "Forward", g.Move, "up");
            AddCompositePart(movementRows, "Back", g.Move, "down");
            AddCompositePart(movementRows, "Left", g.Move, "left");
            AddCompositePart(movementRows, "Right", g.Move, "right");
            AddRow(movementRows, "Sprint", g.Sprint);
            AddRow(movementRows, "Jump", g.Jump);

            AddRow(combatRows, "Fire", g.Fire);
            AddRow(combatRows, "Aim / scope", g.Aim);
            AddRow(combatRows, "Reload", g.Reload);
            AddRow(combatRows, "Deploy station", g.Deploy);
            AddRow(combatRows, "Scoreboard", g.Scoreboard);
        }

        private void AddRow(Transform parent, string label, InputAction action)
        {
            int index = action.GetBindingIndex(group: ConquestInput.KeyboardMouseGroup);
            if (index >= 0) CreateRow(parent, label, action, index);
        }

        private void AddCompositePart(Transform parent, string label, InputAction action, string partName)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isPartOfComposite && bindings[i].name == partName &&
                    InputBinding.MaskByGroup(ConquestInput.KeyboardMouseGroup).Matches(bindings[i]))
                {
                    CreateRow(parent, label, action, i);
                    return;
                }
            }
        }

        private void CreateRow(Transform parent, string label, InputAction action, int bindingIndex)
        {
            RebindRowUI row = Instantiate(rowPrefab, parent);
            row.Setup(label, action, bindingIndex);
            row.Clicked += StartRebind;
            m_Rows.Add(row);
        }

        private void StartRebind(RebindRowUI row)
        {
            CancelRebind();
            row.ShowListening();
            m_Rebind = ConquestInput.StartRebind(row.Action, row.BindingIndex, () =>
            {
                m_Rebind = null;
                RefreshRows();
            });
        }

        private void CancelRebind()
        {
            if (m_Rebind != null)
            {
                var op = m_Rebind;
                m_Rebind = null;
                op.Cancel();
            }
        }

        private void ResetAll()
        {
            CancelRebind();
            ConquestInput.ResetAllBindings();
            RefreshRows();
        }

        private void RefreshRows()
        {
            for (int i = 0; i < m_Rows.Count; i++)
            {
                string path = m_Rows[i].EffectivePath;
                bool conflict = false;
                for (int j = 0; j < m_Rows.Count && !conflict; j++)
                {
                    conflict = j != i && string.Equals(m_Rows[j].EffectivePath, path, System.StringComparison.OrdinalIgnoreCase);
                }
                m_Rows[i].Refresh(conflict);
            }
        }
    }
}
