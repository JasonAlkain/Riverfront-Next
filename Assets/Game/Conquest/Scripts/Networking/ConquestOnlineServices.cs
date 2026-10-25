using System;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Game.Conquest.Networking
{
    /// <summary>
    /// Unity Gaming Services for online play: signs the player in (anonymously for now; Steam can
    /// be linked to the same player later) and gets Relay allocations, so a host needs no public IP
    /// or open port. Everything here is async and only touches the transport's settings; the
    /// session manager starts and stops Netcode as before.
    /// </summary>
    public static class ConquestOnlineServices
    {
        // DTLS: encrypted UDP through the relay, the right choice for desktop builds.
        private const string RelayConnectionType = "dtls";

        // Relay caps one allocation at 100 connections; the host is not one of them.
        private const int MaxRelayConnections = 100;

        public static bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

        /// <summary>The signed-in player's id (stable per install for anonymous sign-in), or empty.</summary>
        public static string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : "";

        private static Task s_SignIn;

        /// <summary>Initializes Unity Services and signs in anonymously if needed. Throws on failure.</summary>
        public static Task EnsureSignedInAsync()
        {
            // Hosting and the match list can both ask at once; they share one sign-in.
            if (IsSignedIn) return Task.CompletedTask;
            if (s_SignIn == null || s_SignIn.IsFaulted || s_SignIn.IsCanceled) s_SignIn = SignInAsync();
            return s_SignIn;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSignIn() => s_SignIn = null;

        private static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                await UnityServices.InitializeAsync();
            }
            else
            {
                // Another caller is already initializing: wait for it rather than starting twice.
                while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            }
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                throw new InvalidOperationException("Unity Services did not initialize.");
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>
        /// Host: reserve a relay for up to <paramref name="maxClients"/> other players, point the
        /// transport at it and return the join code players type in.
        /// </summary>
        public static async Task<string> CreateRelayAsync(UnityTransport transport, int maxClients)
        {
            await EnsureSignedInAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Clamp(maxClients, 1, MaxRelayConnections));
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            transport.SetRelayServerData(allocation.ToRelayServerData(RelayConnectionType));
            return joinCode;
        }

        /// <summary>Client: join the relay behind <paramref name="joinCode"/> and point the transport at it.</summary>
        public static async Task JoinRelayAsync(UnityTransport transport, string joinCode)
        {
            await EnsureSignedInAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            transport.SetRelayServerData(allocation.ToRelayServerData(RelayConnectionType));
        }

        /// <summary>A Relay join code as players type it: trimmed and upper case.</summary>
        public static string NormalizeJoinCode(string raw)
        {
            return string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// True if <paramref name="text"/> reads as a Relay join code rather than a LAN address:
        /// six letters and digits, with no dot or colon (an IP or "host:port" always has one).
        /// </summary>
        public static bool LooksLikeJoinCode(string text)
        {
            string code = NormalizeJoinCode(text);
            if (code.Length != 6) return false;
            foreach (char c in code)
            {
                if (!char.IsLetterOrDigit(c)) return false;
            }
            return true;
        }

        /// <summary>A readable reason for a failed online call, for the status badge.</summary>
        public static string Describe(Exception e)
        {
            if (e is RelayServiceException relay)
            {
                if (relay.Reason == RelayExceptionReason.JoinCodeNotFound || relay.Reason == RelayExceptionReason.InvalidRequest)
                {
                    return "No online match uses that join code.";
                }
                return "Relay error: " + relay.Message;
            }
            if (e is AuthenticationException || e is RequestFailedException)
            {
                return "Couldn't reach Unity's online services. Check your internet connection.";
            }
            return e.Message;
        }
    }
}
