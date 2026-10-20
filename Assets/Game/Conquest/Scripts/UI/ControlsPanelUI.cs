using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Game.Conquest.Controls;

namespace Game.Conquest
{
    /// <summary>
    /// Keyboard and mouse rebinding, opened from the Settings panel (SettingsPanelUI.btn_Controls). Rows are RebindRow prefab
    /// instances, one per rebindable binding; rebinds are saved at once by ConquestInput.
    /// Pause stays on Escape so the menu can always be reached.
    /// </summary>
    public class ControlsPanelUI : MonoBehaviour
    {
        [Header("Rows")]
        public RebindRowUI rowPrefab;
        public Transform rowsParent;

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
            if (rowPrefab == null || rowsParent == null) return;
            var g = ConquestInput.Gameplay;

            // Movement is a composite: one row per direction.
            AddCompositePart("Move forward", g.Move, "up");
            AddCompositePart("Move back", g.Move, "down");
            AddCompositePart("Move left", g.Move, "left");
            AddCompositePart("Move right", g.Move, "right");
            AddRow("Fire", g.Fire);
            AddRow("Aim / scope", g.Aim);
            AddRow("Reload", g.Reload);
            AddRow("Sprint", g.Sprint);
            AddRow("Jump", g.Jump);
            AddRow("Deploy station", g.Deploy);
            AddRow("Scoreboard", g.Scoreboard);
        }

        private void AddRow(string label, InputAction action)
        {
            int index = action.GetBindingIndex(group: ConquestInput.KeyboardMouseGroup);
            if (index >= 0) CreateRow(label, action, index);
        }

        private void AddCompositePart(string label, InputAction action, string partName)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isPartOfComposite && bindings[i].name == partName &&
                    InputBinding.MaskByGroup(ConquestInput.KeyboardMouseGroup).Matches(bindings[i]))
                {
                    CreateRow(label, action, i);
                    return;
                }
            }
        }

        private void CreateRow(string label, InputAction action, int bindingIndex)
        {
            RebindRowUI row = Instantiate(rowPrefab, rowsParent);
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
            for (int i = 0; i < m_Rows.Count; i++) m_Rows[i].Refresh();
        }
    }
}
