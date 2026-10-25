using UnityEngine;

namespace Game.Conquest.Profile
{
    /// <summary>How a match turns into experience, and how much experience each level needs. All Placeholder.</summary>
    [CreateAssetMenu(fileName = "ProfileRules", menuName = "Conquest/Profile Rules")]
    public class ProfileRules : ScriptableObject
    {
        [Tooltip("Placeholder: experience for finishing a match.")]
        public int perMatch = 20;
        [Tooltip("Placeholder: experience per kill.")]
        public int perKill = 10;
        [Tooltip("Placeholder: experience per command post captured or neutralised.")]
        public int perCapture = 25;
        [Tooltip("Placeholder: bonus experience for a win.")]
        public int perWin = 50;
        [Tooltip("Placeholder: experience needed for level 2; each level after needs this much more.")]
        public int firstLevel = 500;
        [Tooltip("Placeholder: extra experience each further level needs.")]
        public int levelStep = 100;

        public int ExperienceFor(int kills, int captures, bool won)
        {
            return perMatch + kills * perKill + captures * perCapture + (won ? perWin : 0);
        }

        /// <summary>Level (from 1) for a total, plus experience into it and what the next level needs.</summary>
        public int LevelFor(int experience, out int intoLevel, out int needed)
        {
            int level = 1;
            int remaining = Mathf.Max(0, experience);
            needed = Mathf.Max(1, firstLevel);
            while (remaining >= needed && level < 999)
            {
                remaining -= needed;
                level++;
                needed = Mathf.Max(1, firstLevel + (level - 1) * levelStep);
            }
            intoLevel = remaining;
            return level;
        }
    }
}
