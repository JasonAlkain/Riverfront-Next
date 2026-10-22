using NUnit.Framework;
using Game.Conquest.Objectives;

namespace Game.Conquest.Tests
{
    public class ConquestCaptureRulesTests
    {
        private const float Falloff = 0.75f;
        private const int Crowd = 4;
        private const float Tail = 0.1f;

        private static float Multiplier(int players) => ConquestCaptureRules.CrowdMultiplier(players, Falloff, Crowd, Tail);

        [Test]
        public void NobodyOnThePostCapturesNothing()
        {
            Assert.AreEqual(0f, Multiplier(0));
        }

        [Test]
        public void OnePlayerCapturesAtTheBaseRate()
        {
            Assert.AreEqual(1f, Multiplier(1), 1e-5f);
        }

        [Test]
        public void TheFirstFourEachAddLessThanTheOneBefore()
        {
            Assert.AreEqual(1.75f, Multiplier(2), 1e-4f);
            Assert.AreEqual(2.3125f, Multiplier(3), 1e-4f);
            Assert.AreEqual(2.734375f, Multiplier(4), 1e-4f);
        }

        [Test]
        public void PlayersPastTheCrowdAddAFlatSmallGain()
        {
            Assert.AreEqual(Multiplier(4) + 0.1f, Multiplier(5), 1e-4f);
            Assert.AreEqual(Multiplier(4) + 1.2f, Multiplier(16), 1e-4f);
            Assert.Less(Multiplier(5) - Multiplier(4), Multiplier(4) - Multiplier(3), "the fifth adds less than the fourth");
        }

        [Test]
        public void ThereIsNoCap()
        {
            Assert.Greater(Multiplier(32), Multiplier(16));
        }

        [Test]
        public void NoFalloffCountsEveryoneInTheCrowdFully()
        {
            Assert.AreEqual(4f, ConquestCaptureRules.CrowdMultiplier(4, 1f, 4, 0f), 1e-5f);
            Assert.AreEqual(4f, ConquestCaptureRules.CrowdMultiplier(8, 1f, 4, 0f), 1e-5f);
        }
    }
}
