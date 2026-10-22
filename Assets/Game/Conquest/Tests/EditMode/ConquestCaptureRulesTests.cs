using NUnit.Framework;
using Game.Conquest.Objectives;

namespace Game.Conquest.Tests
{
    public class ConquestCaptureRulesTests
    {
        [Test]
        public void NobodyOnThePostCapturesNothing()
        {
            Assert.AreEqual(0f, ConquestCaptureRules.CrowdMultiplier(0, 0.6f));
        }

        [Test]
        public void OnePlayerCapturesAtTheBaseRate()
        {
            Assert.AreEqual(1f, ConquestCaptureRules.CrowdMultiplier(1, 0.6f), 1e-5f);
            Assert.AreEqual(1f, ConquestCaptureRules.CrowdMultiplier(8, 0f), 1e-5f);
        }

        [Test]
        public void EachExtraPlayerAddsLessThanTheOneBefore()
        {
            Assert.AreEqual(1.6f, ConquestCaptureRules.CrowdMultiplier(2, 0.6f), 1e-4f);
            Assert.AreEqual(2.176f, ConquestCaptureRules.CrowdMultiplier(4, 0.6f), 1e-4f);

            float previousGain = float.MaxValue;
            for (int n = 2; n <= 16; n++)
            {
                float gain = ConquestCaptureRules.CrowdMultiplier(n, 0.6f) - ConquestCaptureRules.CrowdMultiplier(n - 1, 0.6f);
                Assert.Greater(gain, 0f, $"player {n} still helps");
                Assert.Less(gain, previousGain, $"player {n} adds less than player {n - 1}");
                previousGain = gain;
            }
        }

        [Test]
        public void NoCrowdReachesTheLimit()
        {
            Assert.Less(ConquestCaptureRules.CrowdMultiplier(32, 0.6f), 2.5f);
        }
    }
}
