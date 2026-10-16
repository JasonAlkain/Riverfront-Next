using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Game.Conquest.Networking
{
    /// <summary>
    /// Keeps exactly one NetworkManager alive. MainMenu and every map scene carry their own
    /// NetworkManager + UnityTransport; NGO makes the first one the persistent singleton and
    /// leaves later copies idle in DontDestroyOnLoad, piling up with each scene change.
    /// Duplicates are disabled before their OnEnable runs (so they never move their
    /// GameObject, which may hold scene components, into DontDestroyOnLoad) and removed.
    /// </summary>
    public static class ConquestNetworkManagerGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            NetworkManager.OnInstantiated -= HandleInstantiated;
            NetworkManager.OnInstantiated += HandleInstantiated;
        }

        // Invoked from the new instance's Awake.
        private static void HandleInstantiated(NetworkManager instance)
        {
            var existing = NetworkManager.Singleton;
            if (existing == null || existing == instance) return;

            instance.enabled = false;
            var transport = instance.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.enabled = false;
                Object.Destroy(transport);
            }
            Object.Destroy(instance);
        }
    }
}
