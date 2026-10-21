using UnityEngine;

namespace Game.Conquest.Combat
{
    /// <summary>
    /// Pure bolt maths shared by the server, the shooter and observers: deterministic pellet
    /// spread from a server-owned shot number, and where to aim to hit a moving target.
    /// </summary>
    public static class ConquestBallistics
    {
        /// <summary>Integer hash (lowbias32). Same result on every peer and platform.</summary>
        public static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>Seed for one trigger pull of one weapon. The shot number is counted by the server, so a client can't pick it.</summary>
        public static uint Seed(ulong objectId, ushort shotSeq)
        {
            unchecked
            {
                return Hash((uint)objectId ^ Hash((uint)(objectId >> 32) ^ (shotSeq * 0x9E3779B9u)));
            }
        }

        /// <summary>
        /// Direction of pellet <paramref name="pellet"/> of a shot: a square cone of
        /// ±<paramref name="angle"/> degrees around <paramref name="forward"/>, from the seed.
        /// </summary>
        public static Vector3 Pellet(Vector3 forward, float angle, uint seed, int pellet)
        {
            if (angle <= 0.01f) return forward;
            uint h;
            unchecked { h = Hash(seed + (uint)pellet * 0x85EBCA6Bu); }
            float u = (h & 0xFFFF) * (2f / 65535f) - 1f;
            float v = (h >> 16) * (2f / 65535f) - 1f;
            return Quaternion.LookRotation(forward) * Quaternion.Euler(u * angle, v * angle, 0f) * Vector3.forward;
        }

        /// <summary>
        /// Where to aim from <paramref name="origin"/> so a bolt of speed <paramref name="speed"/>
        /// meets a target at <paramref name="target"/> moving at constant <paramref name="velocity"/>.
        /// Bolts fly straight, so the intercept is the smallest positive root of
        /// |target + velocity·t − origin| = speed·t. Falls back to the plain flight time when there
        /// is no solution (a target faster than the bolt moving away), and never leads further
        /// than <paramref name="maxTime"/> seconds.
        /// </summary>
        public static Vector3 InterceptPoint(Vector3 origin, Vector3 target, Vector3 velocity, float speed, float maxTime)
        {
            Vector3 d = target - origin;
            if (speed <= 0f) return target;
            float a = Vector3.Dot(velocity, velocity) - speed * speed;
            float b = 2f * Vector3.Dot(d, velocity);
            float c = Vector3.Dot(d, d);
            float plain = Mathf.Sqrt(c) / speed;
            float t;

            if (Mathf.Abs(a) < 1e-4f)
            {
                // Target as fast as the bolt: the equation is linear.
                t = b < 0f ? -c / b : plain;
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc < 0f)
                {
                    t = plain;
                }
                else
                {
                    float q = Mathf.Sqrt(disc);
                    float t1 = (-b - q) / (2f * a);
                    float t2 = (-b + q) / (2f * a);
                    float lo = Mathf.Min(t1, t2), hi = Mathf.Max(t1, t2);
                    t = lo > 0f ? lo : (hi > 0f ? hi : plain);
                }
            }

            t = Mathf.Clamp(t, 0f, Mathf.Max(0f, maxTime));
            return target + velocity * t;
        }
    }
}
