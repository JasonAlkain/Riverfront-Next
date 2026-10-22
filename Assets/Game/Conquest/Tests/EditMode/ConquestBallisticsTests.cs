using NUnit.Framework;
using UnityEngine;
using Game.Conquest.Combat;

namespace Game.Conquest.Tests
{
    public class ConquestBallisticsTests
    {
        [Test]
        public void StationaryTargetIsAimedAtDirectly()
        {
            var p = new Vector3(0, 0, 50);
            Assert.AreEqual(p, ConquestBallistics.InterceptPoint(Vector3.zero, p, Vector3.zero, 100f, 5f));
        }

        [Test]
        public void CrossingTargetIsMetAtTheBoltsDistance()
        {
            var origin = Vector3.zero;
            var target = new Vector3(0, 0, 60);
            var velocity = new Vector3(6, 0, 0);
            const float speed = 120f;
            Vector3 aim = ConquestBallistics.InterceptPoint(origin, target, velocity, speed, 5f);
            float t = (aim - target).magnitude / velocity.magnitude;
            Assert.AreEqual(speed * t, (aim - origin).magnitude, 1e-2f);
            Assert.Greater(aim.x, 0f);
        }

        [Test]
        public void RecedingTargetLeadsFurther()
        {
            var target = new Vector3(0, 0, 60);
            Vector3 aim = ConquestBallistics.InterceptPoint(Vector3.zero, target, new Vector3(0, 0, 8), 120f, 5f);
            Assert.Greater(aim.z, target.z);
            float t = (aim.z - target.z) / 8f;
            Assert.AreEqual(120f * t, aim.z, 1e-2f);
        }

        [Test]
        public void TargetFasterThanTheBoltFallsBackToPlainFlightTime()
        {
            var target = new Vector3(0, 0, 60);
            Vector3 aim = ConquestBallistics.InterceptPoint(Vector3.zero, target, new Vector3(0, 0, 200), 100f, 10f);
            // Plain flight time 0.6 s.
            Assert.AreEqual(60f + 200f * 0.6f, aim.z, 1e-2f);
        }

        [Test]
        public void LeadIsClampedToMaxTime()
        {
            var target = new Vector3(0, 0, 600);
            Vector3 aim = ConquestBallistics.InterceptPoint(Vector3.zero, target, new Vector3(10, 0, 0), 100f, 1f);
            Assert.AreEqual(10f, aim.x, 1e-3f);
        }

        [Test]
        public void TargetAsFastAsTheBoltApproachingSolvesLinearly()
        {
            var target = new Vector3(0, 0, 100);
            Vector3 aim = ConquestBallistics.InterceptPoint(Vector3.zero, target, new Vector3(0, 0, -100), 100f, 10f);
            Assert.AreEqual(50f, aim.z, 1e-2f);
        }

        [Test]
        public void SameShotGivesTheSamePelletsAndNeighboursDiffer()
        {
            uint a = ConquestBallistics.Seed(17, 300);
            uint b = ConquestBallistics.Seed(17, 300);
            uint c = ConquestBallistics.Seed(17, 301);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(ConquestBallistics.Pellet(Vector3.forward, 4f, a, i), ConquestBallistics.Pellet(Vector3.forward, 4f, b, i));
            }
            Assert.AreNotEqual(ConquestBallistics.Pellet(Vector3.forward, 4f, a, 0), ConquestBallistics.Pellet(Vector3.forward, 4f, c, 0));
        }

        [Test]
        public void PelletsStayInsideTheSpreadCone()
        {
            for (ushort seq = 0; seq < 200; seq++)
            {
                uint seed = ConquestBallistics.Seed(5, seq);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 d = ConquestBallistics.Pellet(Vector3.forward, 3f, seed, i);
                    // A square cone of ±3° on each axis is inside a circular cone of 3·√2.
                    Assert.LessOrEqual(Vector3.Angle(Vector3.forward, d), 3f * 1.4143f + 1e-3f);
                }
            }
        }

        [Test]
        public void NoSpreadReturnsForward()
        {
            Assert.AreEqual(Vector3.forward, ConquestBallistics.Pellet(Vector3.forward, 0f, 123u, 3));
        }
    }
}
