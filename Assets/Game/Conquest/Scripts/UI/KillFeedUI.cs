using TMPro;
using UnityEngine;

namespace Game.Conquest
{
    /// <summary>The last few kills, newest on top. Each line clears itself after a few seconds.</summary>
    public class KillFeedUI : MonoBehaviour
    {
        [Header("Text Elements")]
        [Tooltip("Top line first.")]
        public TextMeshProUGUI[] lines;

        [Header("Timing")]
        public float lineLifetime = 6f;

        private float[] m_ExpireAt;

        private void Awake()
        {
            m_ExpireAt = new float[lines != null ? lines.Length : 0];
            for (int i = 0; i < m_ExpireAt.Length; i++) SetLine(i, "", 0f);
        }

        /// <summary>Add a line at the top; the rest move down and the oldest drops off.</summary>
        public void Push(string text)
        {
            if (lines == null || lines.Length == 0) return;
            for (int i = lines.Length - 1; i > 0; i--)
            {
                SetLine(i, lines[i - 1] != null ? lines[i - 1].text : "", m_ExpireAt[i - 1]);
            }
            SetLine(0, text, Time.time + lineLifetime);
        }

        private void Update()
        {
            for (int i = 0; i < m_ExpireAt.Length; i++)
            {
                if (m_ExpireAt[i] > 0f && Time.time >= m_ExpireAt[i]) SetLine(i, "", 0f);
            }
        }

        private void SetLine(int index, string text, float expireAt)
        {
            m_ExpireAt[index] = expireAt;
            var line = lines[index];
            if (line == null) return;
            line.text = text;
            line.enabled = !string.IsNullOrEmpty(text);
        }
    }
}
