using System;
using Unity.Netcode;
using UnityEngine;
using Game.Conquest.Classes;

namespace Game.Conquest.Combat
{
    // How a weapon keeps firing: magazines (blue) or heat (red). Pure maths and state, no Unity
    // objects, shared by the server (authority) and the shooting player's own peer (prediction).

    /// <summary>What is stopping a weapon from firing.</summary>
    public enum WeaponLock : byte
    {
        None = 0,
        Reload = 1,
        Overheat = 2
    }

    /// <summary>The numbers that drive one weapon's sustain, built from the class and the team's faction.</summary>
    public readonly struct SustainParams
    {
        public readonly ConquestWeaponModel Model;
        public readonly int Capacity;
        public readonly float ReloadSeconds;
        public readonly int ShotsToOverheat;
        public readonly float CoolDelay;
        public readonly float FullCoolSeconds;
        public readonly float LockoutSeconds;
        /// <summary>Share of the gauge still hot when an overheat lock ends.</summary>
        public readonly float HeatAfter;

        public SustainParams(ConquestWeaponModel model, int capacity, float reloadSeconds, int shotsToOverheat,
            float coolDelay, float fullCoolSeconds, float lockoutSeconds, float heatAfter)
        {
            Model = model;
            Capacity = Mathf.Max(1, capacity);
            ReloadSeconds = Mathf.Max(0f, reloadSeconds);
            ShotsToOverheat = Mathf.Max(1, shotsToOverheat);
            CoolDelay = Mathf.Max(0f, coolDelay);
            FullCoolSeconds = Mathf.Max(0f, fullCoolSeconds);
            LockoutSeconds = Mathf.Max(0f, lockoutSeconds);
            HeatAfter = Mathf.Clamp01(heatAfter);
        }

        public bool UsesHeat => Model == ConquestWeaponModel.Overheat;

        /// <summary>Shots' worth of heat lost per second once cooling has started.</summary>
        public float CoolPerSecond => FullCoolSeconds > 0f ? ShotsToOverheat / FullCoolSeconds : float.PositiveInfinity;
    }

    /// <summary>
    /// Heat is counted in shots (0 to ShotsToOverheat), so whole shots are exact and the server
    /// and the owner never drift on sustained fire. Cooling is evaluated lazily from the time it
    /// started, so nothing is ticked or sent per frame.
    /// </summary>
    public static class ConquestHeatModel
    {
        public static float HeatAt(float heat, double coolFrom, double now, float coolPerSecond)
        {
            if (heat <= 0f) return 0f;
            double dt = now - coolFrom;
            if (dt <= 0.0) return heat;
            if (float.IsPositiveInfinity(coolPerSecond)) return 0f;
            return Mathf.Max(0f, heat - coolPerSecond * (float)dt);
        }

        /// <summary>One shot: one shot of heat on top of what is left, and cooling waits CoolDelay again.</summary>
        public static void ApplyShot(ref float heat, ref double coolFrom, double now, in SustainParams p)
        {
            heat = Mathf.Min(p.ShotsToOverheat, HeatAt(heat, coolFrom, now, p.CoolPerSecond) + 1f);
            coolFrom = now + p.CoolDelay;
        }

        /// <summary>Gauge fill 0..1. While locked it falls from full to HeatAfter over the lockout.</summary>
        public static float Display01(float heat, double coolFrom, double now, in SustainParams p,
            WeaponLock weaponLock, double lockStart, double lockEnd)
        {
            if (weaponLock == WeaponLock.Overheat)
            {
                double span = lockEnd - lockStart;
                float t = span > 0.0 ? Mathf.Clamp01((float)((now - lockStart) / span)) : 1f;
                return Mathf.Lerp(1f, p.HeatAfter, t);
            }
            return Mathf.Clamp01(HeatAt(heat, coolFrom, now, p.CoolPerSecond) / p.ShotsToOverheat);
        }
    }

    /// <summary>
    /// What the server tells the shooting player about their own weapon (owner-only, 8 bytes),
    /// written only on events: a processed shot, a lock starting or ending, a reset.
    /// </summary>
    public struct WeaponOwnerState : INetworkSerializable, IEquatable<WeaponOwnerState>
    {
        private const byte OverheatedBit = 1;
        private const byte ReloadingBit = 2;

        /// <summary>Fire requests the server has processed (accepted or rejected).</summary>
        public ushort ackCount;
        /// <summary>Server heat in shots × 256.</summary>
        public ushort heatQ;
        /// <summary>Milliseconds left of the server's lock when this was written.</summary>
        public ushort lockMsLeft;
        public byte ammo;
        /// <summary>Bit 0 overheated, bit 1 reloading, bits 2-3 locks started (mod 4), bits 4-7 reset epoch.</summary>
        public byte flags;

        public bool Overheated => (flags & OverheatedBit) != 0;
        public bool Reloading => (flags & ReloadingBit) != 0;
        public bool Locked => (flags & (OverheatedBit | ReloadingBit)) != 0;
        public byte Epoch => (byte)(flags >> 4);
        public byte LockCount => (byte)((flags >> 2) & 0x03);
        public float Heat => heatQ / 256f;

        public static WeaponOwnerState Pack(ushort ackCount, float heat, double lockSecondsLeft, int ammo,
            WeaponLock weaponLock, byte lockCount, byte epoch)
        {
            byte f = (byte)(((epoch & 0x0F) << 4) | ((lockCount & 0x03) << 2));
            if (weaponLock == WeaponLock.Overheat) f |= OverheatedBit;
            if (weaponLock == WeaponLock.Reload) f |= ReloadingBit;
            return new WeaponOwnerState
            {
                ackCount = ackCount,
                heatQ = (ushort)Mathf.Clamp(Mathf.RoundToInt(heat * 256f), 0, ushort.MaxValue),
                lockMsLeft = (ushort)Mathf.Clamp((int)Math.Ceiling(Math.Max(0.0, lockSecondsLeft) * 1000.0), 0, ushort.MaxValue),
                ammo = (byte)Mathf.Clamp(ammo, 0, 255),
                flags = f
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ackCount);
            serializer.SerializeValue(ref heatQ);
            serializer.SerializeValue(ref lockMsLeft);
            serializer.SerializeValue(ref ammo);
            serializer.SerializeValue(ref flags);
        }

        public bool Equals(WeaponOwnerState other)
        {
            return ackCount == other.ackCount && heatQ == other.heatQ && lockMsLeft == other.lockMsLeft &&
                   ammo == other.ammo && flags == other.flags;
        }

        public override bool Equals(object obj) => obj is WeaponOwnerState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ackCount, heatQ, lockMsLeft, ammo, flags);
    }

    /// <summary>
    /// The shooting player's own copy of their magazine or heat, run ahead of the server so the
    /// HUD and the trigger respond at once. It counts shots the server hasn't acknowledged yet and
    /// corrects itself from <see cref="WeaponOwnerState"/>. The stricter side always wins: a lock
    /// this peer predicted is never cut short by the server, and a lock the server started first
    /// is adopted.
    /// </summary>
    public class ConquestSustainPredictor
    {
        // Snapshots older than this many unacknowledged shots are treated as this many (a burst).
        private const int MaxUnacked = 64;

        private int m_Ammo;
        private float m_Heat;
        private double m_CoolFrom;
        private WeaponLock m_Lock;
        private double m_LockStart;
        private double m_LockUntil;
        private byte m_Epoch;
        private bool m_HasEpoch;
        // Locks started here (mod 4), compared with the server's so a lock this peer already
        // predicted isn't adopted a second time from a late snapshot.
        private byte m_LockCount;

        /// <summary>Fire requests sent so far; the next one's number.</summary>
        public ushort Sent { get; private set; }
        public int Ammo => m_Ammo;
        public WeaponLock Lock => m_Lock;
        public bool IsReloading => m_Lock == WeaponLock.Reload;
        public bool IsOverheated => m_Lock == WeaponLock.Overheat;
        /// <summary>Predicted heat in shots right now (for tests and diagnostics).</summary>
        public float HeatShots(in SustainParams p, double now) => ConquestHeatModel.HeatAt(m_Heat, m_CoolFrom, now, p.CoolPerSecond);

        /// <summary>Full magazine, cold weapon, no lock. The shot count is kept.</summary>
        public void Reset(in SustainParams p)
        {
            m_Ammo = p.Capacity;
            m_Heat = 0f;
            m_CoolFrom = 0.0;
            m_Lock = WeaponLock.None;
        }

        public bool CanFire(in SustainParams p, double now)
        {
            Tick(p, now);
            return m_Lock == WeaponLock.None && (p.UsesHeat || m_Ammo > 0);
        }

        /// <summary>A shot fired on this peer. Returns its number, which the server checks and seeds the spread with.</summary>
        public ushort OnShot(in SustainParams p, double now)
        {
            ushort seq = Sent;
            Sent = (ushort)(Sent + 1);

            if (p.UsesHeat)
            {
                ConquestHeatModel.ApplyShot(ref m_Heat, ref m_CoolFrom, now, p);
                if (m_Heat >= p.ShotsToOverheat) StartPredictedLock(WeaponLock.Overheat, now, now + p.LockoutSeconds);
            }
            else
            {
                m_Ammo = Mathf.Max(0, m_Ammo - 1);
                if (m_Ammo == 0) StartPredictedLock(WeaponLock.Reload, now, now + p.ReloadSeconds);
            }
            return seq;
        }

        /// <summary>The player pressed reload. Returns false when there is nothing to reload.</summary>
        public bool OnReloadRequested(in SustainParams p, double now)
        {
            Tick(p, now);
            if (p.UsesHeat || m_Lock != WeaponLock.None || m_Ammo >= p.Capacity) return false;
            StartPredictedLock(WeaponLock.Reload, now, now + p.ReloadSeconds);
            return true;
        }

        public void Tick(in SustainParams p, double now)
        {
            if (m_Lock != WeaponLock.None && now >= m_LockUntil) FinishLock(p, now);
        }

        /// <summary>The server's numbering differs (it rejected and resynced): continue from its count.</summary>
        public void ResyncSeq(ushort serverCount)
        {
            Sent = serverCount;
        }

        public float Heat01(in SustainParams p, double now)
        {
            return ConquestHeatModel.Display01(m_Heat, m_CoolFrom, now, p, m_Lock, m_LockStart, m_LockUntil);
        }

        /// <param name="oneWaySeconds">Half the round trip to the server: how old the snapshot is.</param>
        public void OnSnapshot(in SustainParams p, in WeaponOwnerState s, double now, float oneWaySeconds)
        {
            int unacked = Mathf.Clamp((ushort)(Sent - s.ackCount), 0, MaxUnacked);
            double serverLockUntil = now + s.lockMsLeft / 1000.0 - oneWaySeconds;
            WeaponLock serverLock = s.Overheated ? WeaponLock.Overheat : (s.Reloading ? WeaponLock.Reload : WeaponLock.None);

            if (!m_HasEpoch || s.Epoch != m_Epoch)
            {
                // The server reset the weapon (death, respawn, class or team change, a station):
                // take its state whole, plus the shots it hasn't seen yet.
                m_HasEpoch = true;
                m_Epoch = s.Epoch;
                m_Ammo = Mathf.Max(0, s.ammo - unacked);
                m_Heat = Mathf.Min(p.ShotsToOverheat, s.Heat + unacked);
                m_CoolFrom = unacked > 0 ? now + p.CoolDelay : now;
                m_Lock = WeaponLock.None;
                m_LockCount = s.LockCount;
                if (serverLock != WeaponLock.None && serverLockUntil > now) StartLock(serverLock, now, serverLockUntil);
                return;
            }

            if (serverLock == WeaponLock.None && m_Lock == WeaponLock.None)
            {
                // Both idle: the lock counts must agree (realigns after a lock only one side made).
                m_LockCount = s.LockCount;
            }
            else if (serverLock != WeaponLock.None && m_Lock == WeaponLock.None && s.LockCount != m_LockCount)
            {
                // The server started a lock this peer didn't predict (it overheated a shot sooner,
                // or auto-reloaded): adopt it. One this peer predicted and already finished isn't.
                m_LockCount = s.LockCount;
                if (serverLockUntil > now) StartLock(serverLock, now, serverLockUntil);
            }

            if (!p.UsesHeat)
            {
                // Mid-reload the server's count means nothing; otherwise it is exact once the
                // unacknowledged shots are taken off.
                if (serverLock != WeaponLock.Reload && m_Lock != WeaponLock.Reload) m_Ammo = Mathf.Max(0, s.ammo - unacked);
            }
            else if (m_Lock == WeaponLock.None && serverLock == WeaponLock.None)
            {
                // Arrival jitter can leave the server a little hotter; follow it upward only.
                float serverHeat = Mathf.Min(p.ShotsToOverheat, s.Heat + unacked);
                if (serverHeat > HeatShots(p, now) + 0.5f)
                {
                    m_Heat = serverHeat;
                    m_CoolFrom = now + p.CoolDelay;
                }
            }
        }

        private void StartPredictedLock(WeaponLock weaponLock, double now, double until)
        {
            m_LockCount = (byte)((m_LockCount + 1) & 0x03);
            StartLock(weaponLock, now, until);
        }

        private void StartLock(WeaponLock weaponLock, double now, double until)
        {
            m_Lock = weaponLock;
            m_LockStart = now;
            m_LockUntil = until;
        }

        private void FinishLock(in SustainParams p, double now)
        {
            if (m_Lock == WeaponLock.Reload)
            {
                m_Ammo = p.Capacity;
            }
            else if (m_Lock == WeaponLock.Overheat)
            {
                m_Heat = p.HeatAfter * p.ShotsToOverheat;
                m_CoolFrom = now;
            }
            m_Lock = WeaponLock.None;
        }
    }
}
