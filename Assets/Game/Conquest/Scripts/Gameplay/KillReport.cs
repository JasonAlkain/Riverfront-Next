using Unity.Collections;
using Unity.Netcode;

namespace Game.Conquest.Gameplay
{
    /// <summary>One kill-feed line, sent by the server to every peer when someone dies.</summary>
    public struct KillReport : INetworkSerializable
    {
        public FixedString64Bytes victimName;
        public Team victimTeam;
        /// <summary>False for falls and other deaths with nobody to credit.</summary>
        public bool hasKiller;
        public FixedString64Bytes killerName;
        public Team killerTeam;
        public FixedString64Bytes weaponName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref victimName);
            serializer.SerializeValue(ref victimTeam);
            serializer.SerializeValue(ref hasKiller);
            if (hasKiller)
            {
                serializer.SerializeValue(ref killerName);
                serializer.SerializeValue(ref killerTeam);
                serializer.SerializeValue(ref weaponName);
            }
        }
    }
}
