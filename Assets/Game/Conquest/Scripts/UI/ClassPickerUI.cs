using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Conquest.Classes;

namespace Game.Conquest
{
    /// <summary>
    /// A row of ClassButton prefabs built from the class list, used by the lobby and the deploy
    /// screen. Adding or editing a class asset changes both without touching the UI.
    /// </summary>
    public class ClassPickerUI : MonoBehaviour
    {
        [Header("Buttons")]
        public ClassButtonUI classButtonPrefab;
        public Transform container;

        [Header("Colors")]
        public Color normalColor = new Color(0.25f, 0.30f, 0.40f);
        public Color selectedColor = new Color(0.2f, 0.8f, 0.35f);

        /// <summary>Raised with the class index when a class button is clicked.</summary>
        public event Action<int> ClassSelected;

        private readonly List<ClassButtonUI> m_Buttons = new List<ClassButtonUI>();
        private IReadOnlyList<ConquestClassData> m_Classes;
        private int m_Selected = int.MinValue;

        /// <summary>Builds one button per class. Does nothing if the same list is already shown.</summary>
        public void SetClasses(IReadOnlyList<ConquestClassData> classes)
        {
            if (classes == m_Classes || classButtonPrefab == null || container == null) return;
            m_Classes = classes;
            int count = classes != null ? classes.Count : 0;

            while (m_Buttons.Count < count)
            {
                var button = Instantiate(classButtonPrefab, container);
                button.Clicked += index => ClassSelected?.Invoke(index);
                m_Buttons.Add(button);
            }
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                bool used = i < count;
                m_Buttons[i].gameObject.SetActive(used);
                if (used) m_Buttons[i].SetClass(i, classes[i]);
            }
            int selected = m_Selected;
            m_Selected = int.MinValue;
            SetSelected(selected);
        }

        public void SetSelected(int classIndex)
        {
            if (classIndex == m_Selected) return;
            m_Selected = classIndex;
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                m_Buttons[i].SetSelected(i == classIndex, normalColor, selectedColor);
            }
        }
    }
}
