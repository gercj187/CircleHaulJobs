using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;

namespace CircleHauls.Multiplayer
{
    /// <summary>
    /// Multiplayer-Anbindung (eigene DLL, nur geladen wenn dv-multiplayer aktiv ist – siehe MpCompat).
    /// Kennt von CircleHauls nur IMpBridge, CircleHaulWire und MpHooks.
    /// </summary>
    public class MpBridge : IMpBridge
    {
        private bool _hooked;

        public void Init(string modId)
        {
            if (_hooked || !MultiplayerAPI.IsMultiplayerLoaded) return;
            _hooked = true;

            // Muss auf Host und Clients installiert sein (eigener Job-Typ, Booklets, Pakete)
            MultiplayerAPI.Instance.SetModCompatibility(modId, MultiplayerCompatibility.All);

            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ClientStarted += OnClientStarted;

            // Falls Server/Client schon laufen (Ladereihenfolge nicht garantiert)
            if (MultiplayerAPI.Server != null) OnServerStarted(MultiplayerAPI.Server);
            if (MultiplayerAPI.Client != null) OnClientStarted(MultiplayerAPI.Client);
        }

        public bool IsClientOnly() =>
            MultiplayerAPI.IsMultiplayerLoaded && MultiplayerAPI.Instance.IsConnected && !MultiplayerAPI.Instance.IsHost;

        private static bool IsHostWithServer() =>
            MultiplayerAPI.IsMultiplayerLoaded && MultiplayerAPI.Instance.IsHost && MultiplayerAPI.Server != null;

        // ------------------------------------------------------------------ Host

        private static void OnServerStarted(IServer server)
        {
            // Spieler, die später dazukommen, bekommen alle laufenden Zugumläufe
            server.OnPlayerReady += player =>
            {
                foreach (var w in MpHooks.GetAllJobs())
                    server.SendPacketToPlayer(ToPacket(w), player);
            };
        }

        public void BroadcastJob(CircleHaulWire job)
        {
            if (!IsHostWithServer() || string.IsNullOrEmpty(job?.JobId)) return;
            MultiplayerAPI.Server.SendPacketToAll(ToPacket(job), true, true);
        }

        public void BroadcastReveal(string jobId)
        {
            if (!IsHostWithServer()) return;
            MultiplayerAPI.Server.SendPacketToAll(new CircleHaulRevealPacket { JobId = jobId }, true, true);
        }

        public void BroadcastRemove(string jobId)
        {
            if (!IsHostWithServer()) return;
            MultiplayerAPI.Server.SendPacketToAll(new CircleHaulRemovePacket { JobId = jobId }, true, true);
        }

        private static CircleHaulJobPacket ToPacket(CircleHaulWire w) => new CircleHaulJobPacket
        {
            JobId = w.JobId ?? "",
            Variant = w.Variant,
            StartTrackId = w.StartTrackId ?? "",
            ReturnTrackId = w.ReturnTrackId ?? "",
            FarUnloadMachineId = w.FarUnloadMachineId ?? "",
            FarLoadMachineId = w.FarLoadMachineId ?? "",
            HomeMachineId = w.HomeMachineId ?? "",
            OutCargo = w.OutCargo ?? new int[0],
            BackCargo = w.BackCargo ?? new int[0],
            CarCount = w.CarCount,
            BackRevealed = w.BackRevealed,
        };

        // ------------------------------------------------------------------ Client

        private static void OnClientStarted(IClient client)
        {
            client.RegisterPacket<CircleHaulJobPacket>(OnJobPacket);
            client.RegisterPacket<CircleHaulRevealPacket>(p => { if (!MultiplayerAPI.Instance.IsHost) MpHooks.OnRemoteReveal(p.JobId); });
            client.RegisterPacket<CircleHaulRemovePacket>(p => { if (!MultiplayerAPI.Instance.IsHost) MpHooks.OnRemoteRemove(p.JobId); });
        }

        private static void OnJobPacket(CircleHaulJobPacket p)
        {
            if (MultiplayerAPI.Instance.IsHost) return;
            MpHooks.OnRemoteJob(new CircleHaulWire
            {
                JobId = p.JobId,
                Variant = p.Variant,
                StartTrackId = p.StartTrackId,
                ReturnTrackId = p.ReturnTrackId,
                FarUnloadMachineId = p.FarUnloadMachineId,
                FarLoadMachineId = p.FarLoadMachineId,
                HomeMachineId = p.HomeMachineId,
                OutCargo = p.OutCargo,
                BackCargo = p.BackCargo,
                CarCount = p.CarCount,
                BackRevealed = p.BackRevealed,
            });
        }
    }
}
