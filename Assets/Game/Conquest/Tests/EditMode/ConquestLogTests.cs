using Game.Conquest.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Conquest.Tests
{
    public class ConquestLogTests
    {
        private ConquestLogLevel m_Saved;

        [SetUp] public void SetUp() => m_Saved = ConquestLog.MinLevel;
        [TearDown] public void TearDown() => ConquestLog.MinLevel = m_Saved;

        [TestCase("verbose", ConquestLogLevel.Verbose)]
        [TestCase("DEBUG", ConquestLogLevel.Verbose)]
        [TestCase("info", ConquestLogLevel.Info)]
        [TestCase("warn", ConquestLogLevel.Warning)]
        [TestCase(" Error ", ConquestLogLevel.Error)]
        [TestCase("off", ConquestLogLevel.Off)]
        public void ParseLevel_KnownNames(string value, ConquestLogLevel expected)
        {
            Assert.AreEqual(expected, ConquestLog.ParseLevel(value));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("loud")]
        public void ParseLevel_UnknownIsNull(string value)
        {
            Assert.IsNull(ConquestLog.ParseLevel(value));
        }

        [Test]
        public void Format_KeepsTheTagShapeTheNettestToolsSearchFor()
        {
            Assert.AreEqual("[ConquestNetworkManager] Client 3 connected.", ConquestLog.Format("ConquestNetworkManager", "Client 3 connected."));
            Assert.AreEqual("no tag", ConquestLog.Format(null, "no tag"));
        }

        [Test]
        public void MinLevel_FiltersLowerLevels()
        {
            ConquestLog.MinLevel = ConquestLogLevel.Info;
            Assert.IsFalse(ConquestLog.IsEnabled(ConquestLogLevel.Verbose));
            Assert.IsTrue(ConquestLog.IsEnabled(ConquestLogLevel.Info));
            Assert.IsTrue(ConquestLog.IsEnabled(ConquestLogLevel.Error));

            ConquestLog.Verbose("Test", "hidden");
            LogAssert.NoUnexpectedReceived();

            ConquestLog.Info("Test", "shown");
            LogAssert.Expect(LogType.Log, "[Test] shown");
        }

        [Test]
        public void Off_SilencesEverything()
        {
            ConquestLog.MinLevel = ConquestLogLevel.Off;
            Assert.IsFalse(ConquestLog.IsEnabled(ConquestLogLevel.Error));
            Assert.IsFalse(ConquestLog.IsEnabled(ConquestLogLevel.Off));
        }
    }
}
