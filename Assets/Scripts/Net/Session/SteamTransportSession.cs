using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Pho.Net.Session
{
    /// <summary>
    /// Steam P2P over the Steam relay network, driving the community
    /// <c>FacepunchTransport</c> for Netcode for GameObjects.
    ///
    /// <b>Why every access here is reflective.</b> This class lives in the
    /// <c>Pho.Net</c> assembly, whose assembly definition lists exactly
    /// three references (Pho.Domain, Pho.Core, Unity.Netcode.Runtime) and is
    /// frozen by the architecture document -- an asmdef change is an
    /// architecture change and goes through the integration agent, not
    /// through here. So <c>FacepunchTransport</c> cannot be named at compile
    /// time from this assembly even when the package is installed.
    ///
    /// The upside is the property that actually matters for a game that must
    /// keep building: <b>this file compiles, and the game boots, whether or
    /// not the Steam transport package is present.</b> With the package
    /// missing, <see cref="IsAvailable"/> reports why and
    /// <see cref="NetworkSession"/> stays on UnityTransport. With Steam
    /// installed but not running, same. Nothing throws, nothing fails to
    /// compile, and no player ever sees a black screen because Steam was
    /// closed.
    ///
    /// Note the deliberate limit: this drives the *transport*. Creating and
    /// joining Steam *lobbies*, sending friends-list invites and reacting to
    /// the Steam overlay's "Join game" all need Facepunch's async lobby API
    /// (<c>Task&lt;Lobby?&gt;</c> returns and static events typed on
    /// Steamworks structs), which reflection cannot drive safely. That layer
    /// is the remaining Steam work and is written up in
    /// <c>docs/multiplayer-setup.md</c>.
    /// </summary>
    public sealed class SteamTransportSession : ISessionTransport
    {
        /// <summary>
        /// Spacewar. Valve's standard placeholder App ID, used by every
        /// Steamworks sample and tutorial: it lets Steam initialise, lobbies
        /// form and the relay carry traffic before your title has an App ID
        /// of its own. Replace it the day the real one is issued -- two
        /// different games both testing on 480 can see each other's lobbies.
        /// </summary>
        public const uint SpacewarAppId = 480;

        const string TransportTypeName = "Netcode.Transports.Facepunch.FacepunchTransport";
        const string SteamClientTypeName = "Steamworks.SteamClient";

        readonly uint _appId;

        public SteamTransportSession(uint appId = SpacewarAppId)
        {
            _appId = appId == 0 ? SpacewarAppId : appId;
        }

        public SessionTransportKind Kind => SessionTransportKind.Steam;

        public string DisplayName => $"Steam P2P relay (App ID {_appId})";

        /// <summary>
        /// Three things must be true, and each failure gets its own sentence
        /// because they need completely different fixes: the package must be
        /// installed (a developer fixes that), the Steamworks binding must
        /// be loadable (a build-settings problem), and Steam itself must be
        /// running and logged in (the player fixes that).
        /// </summary>
        public bool IsAvailable(out string reason)
        {
            if (FindType(TransportTypeName) == null)
            {
                reason = "The Facepunch Steam transport package is not installed. See docs/multiplayer-setup.md; the game will use LAN instead.";
                return false;
            }

            var steamClient = FindType(SteamClientTypeName);
            if (steamClient == null)
            {
                reason = "Facepunch.Steamworks was not loaded, so Steam networking is unavailable. The game will use LAN instead.";
                return false;
            }

            if (!IsSteamRunning(steamClient))
            {
                reason = "Steam is not running (or you are not signed in). Start Steam and try again -- the game will use LAN in the meantime.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool TryPrepareHost(NetworkManager networkManager, out string error) =>
            TryPrepare(networkManager, targetSteamId: 0UL, out error);

        public bool TryPrepareJoin(NetworkManager networkManager, string target, out string error)
        {
            if (!TryParseSteamId(target, out var steamId, out error)) return false;
            return TryPrepare(networkManager, steamId, out error);
        }

        bool TryPrepare(NetworkManager networkManager, ulong targetSteamId, out string error)
        {
            error = string.Empty;

            if (networkManager == null)
            {
                error = "No NetworkManager.";
                return false;
            }

            if (!IsAvailable(out error)) return false;

            var transportType = FindType(TransportTypeName);
            var component = networkManager.GetComponent(transportType) as NetworkTransport;
            if (component == null)
            {
                // FacepunchTransport derives from NetworkTransport, which
                // this assembly *can* name -- so once the component exists,
                // everything except its two configuration fields is typed.
                component = networkManager.gameObject.AddComponent(transportType) as NetworkTransport;
            }

            if (component == null)
            {
                error = $"Could not attach {TransportTypeName} to the NetworkManager.";
                return false;
            }

            if (!TrySetField(component, "steamAppId", _appId, out error)) return false;
            if (targetSteamId != 0UL && !TrySetField(component, "targetSteamId", targetSteamId, out error)) return false;

            SessionTransportUtil.MakeActive(networkManager, component);
            SessionTransportUtil.DisableOtherTransports(networkManager, component);
            return true;
        }

        /// <summary>
        /// The host's Steam ID -- what a friend needs in order to connect
        /// directly, and what the lobby layer would publish as lobby data
        /// once it exists.
        /// </summary>
        public string DescribeJoinTarget(NetworkManager networkManager)
        {
            var steamClient = FindType(SteamClientTypeName);
            if (steamClient == null) return string.Empty;

            try
            {
                var steamId = steamClient.GetProperty("SteamId", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (steamId == null) return string.Empty;

                // Steamworks.SteamId is a struct wrapping a ulong Value.
                var value = steamId.GetType().GetField("Value")?.GetValue(steamId);
                return value == null ? string.Empty : Convert.ToUInt64(value).ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SteamTransportSession] Could not read the local Steam ID: {ex.Message}");
                return string.Empty;
            }
        }

        public void Cleanup(NetworkManager networkManager)
        {
            // FacepunchTransport tears its relay socket down inside
            // NetworkManager.Shutdown(). Deliberately NOT calling
            // SteamClient.Shutdown() here: Steam is process-wide, and killing
            // it on leaving a session would break achievements, the overlay
            // and any subsequent session in the same run.
        }

        /// <summary>
        /// Reads the lobby id Steam passes when a player accepts an invite
        /// or clicks "Join game" while the game is closed: Steam relaunches
        /// it with <c>+connect_lobby &lt;id&gt;</c>.
        ///
        /// <b>This is half of the story and the other half is missing.</b>
        /// The value is a *lobby* id, and turning it into the host's Steam ID
        /// requires reading lobby data through Facepunch's lobby API -- the
        /// part that needs a compile-time reference. Surfaced here anyway so
        /// the integrator can see the launch argument arriving and log it,
        /// rather than wondering whether Steam is sending anything at all.
        /// </summary>
        public static bool TryGetLaunchLobbyId(out ulong lobbyId)
        {
            lobbyId = 0;

            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "+connect_lobby", StringComparison.OrdinalIgnoreCase)) continue;
                return ulong.TryParse(args[i + 1], out lobbyId) && lobbyId != 0;
            }

            return false;
        }

        internal static bool TryParseSteamId(string target, out ulong steamId, out string error)
        {
            steamId = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(target))
            {
                error = "No Steam ID to join. Pick a friend from the list, or paste their Steam ID.";
                return false;
            }

            if (!ulong.TryParse(target.Trim(), out steamId) || steamId == 0)
            {
                error = $"'{target}' is not a Steam ID. A Steam ID is a long number, e.g. 76561198000000000.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Asks Steam whether it is up, initialising the client if nobody
        /// has yet.
        ///
        /// The initialise attempt is wrapped because Facepunch throws when
        /// Steam is closed, and "Steam is closed" must be an answer, not an
        /// exception -- the player is allowed to launch the game outside
        /// Steam and play the LAN path.
        /// </summary>
        static bool IsSteamRunning(Type steamClientType)
        {
            try
            {
                var isValid = steamClientType.GetProperty("IsValid", BindingFlags.Public | BindingFlags.Static);
                if (isValid != null && isValid.GetValue(null) is bool valid && valid) return true;

                // FacepunchTransport.Initialize() also calls Init, but it does
                // so only once NGO has already committed to starting. We need
                // the answer before that, to decide whether to fall back.
                var init = steamClientType.GetMethod(
                    "Init",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(uint), typeof(bool) },
                    modifiers: null);

                init?.Invoke(null, new object[] { SpacewarAppId, false });

                return isValid != null && isValid.GetValue(null) is bool nowValid && nowValid;
            }
            catch (Exception ex)
            {
                Debug.Log($"[SteamTransportSession] Steam is unavailable: {ex.GetBaseException().Message}");
                return false;
            }
        }

        static bool TrySetField(object target, string fieldName, object value, out string error)
        {
            error = string.Empty;

            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
            {
                error = $"The installed Facepunch transport has no '{fieldName}' field -- it is a different version than this code expects. See docs/multiplayer-setup.md.";
                return false;
            }

            try
            {
                field.SetValue(target, Convert.ChangeType(value, field.FieldType));
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not set '{fieldName}' on the Facepunch transport: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Looks a type up across every loaded assembly.
        ///
        /// <c>Type.GetType</c> is not usable here: the transport's assembly
        /// is literally named "Facepunch Transport for Netcode for
        /// GameObjects", spaces and all, which makes assembly-qualified
        /// names fragile.
        /// </summary>
        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(fullName, throwOnError: false);
                }
                catch
                {
                    // A partially-loadable assembly is not worth aborting the
                    // whole scan for -- mirrors GameBootstrap's installer scan.
                    continue;
                }

                if (type != null) return type;
            }

            return null;
        }
    }
}
