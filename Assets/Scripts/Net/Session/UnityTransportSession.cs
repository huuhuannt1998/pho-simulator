using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Pho.Net.Session
{
    /// <summary>
    /// The default transport: plain UDP over localhost and LAN, using the
    /// <c>UnityTransport</c> that ships inside Netcode for GameObjects.
    ///
    /// <b>Why this is the default and not Steam.</b> Steam is what players
    /// will use, but it is a terrible thing to depend on while building the
    /// game: it needs the Steam client running, an App ID, and two logged-in
    /// accounts to test four-player co-op properly. UnityTransport needs
    /// none of that -- two builds on one machine, no network connection, no
    /// external service. Keeping it the default means multiplayer never
    /// stops being testable because something outside the project broke,
    /// and it means the Steam path can be swapped in or out without any
    /// gameplay code noticing.
    ///
    /// The whole class is deliberately small. If it ever grows, that is a
    /// sign that something transport-specific has leaked out of
    /// <see cref="NetworkSession"/>.
    /// </summary>
    public sealed class UnityTransportSession : ISessionTransport
    {
        /// <summary>Arbitrary but conventional. Anything above 1024 that nothing else on the machine is using would do.</summary>
        public const ushort DefaultPort = 7777;

        readonly ushort _port;
        readonly string _listenAddress;
        readonly string _defaultJoinAddress;

        /// <param name="port">Port the host binds and clients dial.</param>
        /// <param name="listenAddress">
        /// What the host binds to. "0.0.0.0" accepts connections from the
        /// LAN as well as from this machine, which is what you want for a
        /// four-player couch/LAN test. "127.0.0.1" would restrict the host
        /// to this machine only.
        /// </param>
        /// <param name="defaultJoinAddress">
        /// Where a client goes when the player types nothing. Localhost, so
        /// the two-instance test needs zero typing.
        /// </param>
        public UnityTransportSession(
            ushort port = DefaultPort,
            string listenAddress = "0.0.0.0",
            string defaultJoinAddress = "127.0.0.1")
        {
            _port = port;
            _listenAddress = string.IsNullOrWhiteSpace(listenAddress) ? "0.0.0.0" : listenAddress;
            _defaultJoinAddress = string.IsNullOrWhiteSpace(defaultJoinAddress) ? "127.0.0.1" : defaultJoinAddress;
        }

        public SessionTransportKind Kind => SessionTransportKind.Unity;

        public string DisplayName => $"UnityTransport (localhost/LAN, port {_port})";

        /// <summary>Always available. That is the entire point of it being the default.</summary>
        public bool IsAvailable(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryPrepareHost(NetworkManager networkManager, out string error)
        {
            error = string.Empty;
            if (networkManager == null)
            {
                error = "No NetworkManager.";
                return false;
            }

            var utp = SessionTransportUtil.GetOrAdd<UnityTransport>(networkManager);

            // The first argument is the address clients are told to use; the
            // third is what the socket actually binds. Splitting them is what
            // lets one host serve both "the other window on this machine" and
            // "the laptop on the same wifi".
            utp.SetConnectionData(_defaultJoinAddress, _port, _listenAddress);

            SessionTransportUtil.MakeActive(networkManager, utp);
            SessionTransportUtil.DisableOtherTransports(networkManager, utp);
            return true;
        }

        public bool TryPrepareJoin(NetworkManager networkManager, string target, out string error)
        {
            error = string.Empty;
            if (networkManager == null)
            {
                error = "No NetworkManager.";
                return false;
            }

            if (!TryParseTarget(target, out var address, out var port, out error)) return false;

            var utp = SessionTransportUtil.GetOrAdd<UnityTransport>(networkManager);

            // The third argument is passed explicitly even though it is
            // optional. Without it the call is ambiguous enough that the
            // compiler has to consider SetConnectionData(NetworkEndpoint,
            // NetworkEndpoint), whose parameter type lives in
            // Unity.Networking.Transport -- an assembly Pho.Net does not
            // reference. Naming three arguments keeps overload resolution
            // inside the string overload and off that assembly.
            utp.SetConnectionData(address, port, null);

            SessionTransportUtil.MakeActive(networkManager, utp);
            SessionTransportUtil.DisableOtherTransports(networkManager, utp);
            return true;
        }

        public string DescribeJoinTarget(NetworkManager networkManager) => $"{_defaultJoinAddress}:{_port}";

        public void Cleanup(NetworkManager networkManager)
        {
            // UnityTransport releases its socket inside NetworkManager.Shutdown().
            // Nothing of ours outlives that.
        }

        /// <summary>
        /// Accepts "", "192.168.1.20", or "192.168.1.20:7777".
        ///
        /// Parsing is strict about the port and forgiving about everything
        /// else: a typo'd port silently falling back to the default would
        /// produce a connection timeout with no explanation, which is the
        /// worst possible failure to debug over a chat window.
        /// </summary>
        internal bool TryParseTarget(string target, out string address, out ushort port, out string error)
        {
            address = _defaultJoinAddress;
            port = _port;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(target)) return true;

            var text = target.Trim();
            var colon = text.LastIndexOf(':');
            if (colon < 0)
            {
                address = text;
                return true;
            }

            var host = text.Substring(0, colon);
            var portText = text.Substring(colon + 1);

            if (!ushort.TryParse(portText, out var parsedPort) || parsedPort == 0)
            {
                error = $"'{portText}' is not a valid port number. Expected something like 192.168.1.20:{DefaultPort}.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                error = $"'{target}' has no address. Expected something like 192.168.1.20:{DefaultPort}.";
                return false;
            }

            address = host;
            port = parsedPort;
            return true;
        }
    }
}
