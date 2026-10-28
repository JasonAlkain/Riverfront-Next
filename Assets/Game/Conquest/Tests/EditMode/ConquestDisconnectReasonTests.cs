using NUnit.Framework;
using Game.Conquest.Networking;

namespace Game.Conquest.Tests
{
    public class ConquestDisconnectReasonTests
    {
        private static string Reason(string raw, bool connecting) => ConquestSessionManager.FriendlyDisconnectReason(raw, connecting);

        [Test]
        public void AReasonFromTheServerIsKept()
        {
            Assert.AreEqual("Server full (16/16 players).", Reason("Server full (16/16 players).", true));
        }

        [Test]
        public void TheHostLeavingMidMatchReadsPlainly()
        {
            string raw = "[Disconnect Event][Client-1][TransportClientId-4294967296][ClosedByRemote] Connection was closed by remote endpoint.";
            Assert.AreEqual("The host ended the match or left.", Reason(raw, false));
        }

        [Test]
        public void AnUnreachableServerReadsPlainly()
        {
            string raw = "[Disconnect Event][Client-0][TransportClientId-4294967296][MaxConnectionAttempts] Connection closed due to maximum connection attempts reached.";
            Assert.AreEqual("Could not reach the server (timed out or refused).", Reason(raw, true));
        }

        [Test]
        public void NoReasonFallsBackByWhetherWeWereConnecting()
        {
            Assert.AreEqual("Could not reach the server (timed out or refused).", Reason("", true));
            Assert.AreEqual("Disconnected from the server.", Reason(null, false));
        }
    }
}
