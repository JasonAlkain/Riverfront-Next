using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Conquest
{
    /// <summary>A settings row: name, slider and the formatted value.</summary>
    public class SettingSliderUI : MonoBehaviour
    {
        public enum ValueFormat { Percent, Decimal, Degrees, Multiplier }

        [Header("Elements")]
        public TextMeshProUGUI txt_Label;
        public Slider slider;
        public TextMeshProUGUI txt_Value;

        [Header("Value")]
        public ValueFormat format = ValueFormat.Percent;

        /// <summary>Raised when the player drags the slider.</summary>
        public event Action<float> Changed;

        public float Value => slider != null ? slider.value : 0f;

        private void Awake()
        {
            if (slider != null) slider.onValueChanged.AddListener(v =>
            {
                RefreshLabel();
                Changed?.Invoke(v);
            });
        }

        public void SetValueWithoutNotify(float value)
        {
            if (slider != null) slider.SetValueWithoutNotify(value);
            RefreshLabel();
        }

        private void RefreshLabel()
        {
            if (txt_Value == null || slider == null) return;
            float v = slider.value;
            switch (format)
            {
                case ValueFormat.Percent: txt_Value.text = $"{Mathf.RoundToInt(v * 100)}%"; break;
                case ValueFormat.Decimal: txt_Value.text = v.ToString("F2"); break;
                case ValueFormat.Degrees: txt_Value.text = $"{Mathf.RoundToInt(v)}°"; break;
                case ValueFormat.Multiplier: txt_Value.text = $"{v:F2}x"; break;
            }
        }
    }
}
