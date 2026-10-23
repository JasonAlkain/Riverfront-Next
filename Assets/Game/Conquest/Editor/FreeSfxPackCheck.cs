using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Conquest.Editor
{
    /// <summary>
    /// The Free SFX Pack is not in the repository (its Asset Store licence forbids publishing the
    /// raw files). Without it the game still runs, using fallback clips or silence for the pack's
    /// sounds; this says so once per editor session and points at the setup steps in the README.
    /// </summary>
    [InitializeOnLoad]
    internal static class FreeSfxPackCheck
    {
        private const string PackFolder = "Assets/Free SFX Pack";
        private const string SessionKey = "Riverfront.FreeSfxPackChecked";

        static FreeSfxPackCheck()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            if (!Directory.Exists(PackFolder))
            {
                Debug.LogWarning("[Riverfront] Optional sounds missing: import the Asset Store package \"Free Sound Effects Pack\" " +
                                 "into Assets/Free SFX Pack (see README, \"Sound effects\"). Until then those sounds use fallbacks or stay silent.");
            }
        }
    }
}
