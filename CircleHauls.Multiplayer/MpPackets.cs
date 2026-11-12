using MPAPI.Interfaces.Packets;

namespace CircleHauls.Multiplayer
{
    // LiteNetLib serialisiert öffentliche Properties automatisch (Primitive, string, Arrays von Primitiven).
    // Parameterloser Konstruktor nötig. Strings nie null senden → "".

    /// <summary>Host → Clients: Ablauf und Fracht pro Wagen eines Zugumlaufs (fürs Booklet).</summary>
    public class CircleHaulJobPacket : IPacket
    {
        public string JobId { get; set; } = "";
        public byte Variant { get; set; }
        public string StartTrackId { get; set; } = "";
        public string ReturnTrackId { get; set; } = "";
        public string FarUnloadMachineId { get; set; } = "";
        public string FarLoadMachineId { get; set; } = "";
        public string HomeMachineId { get; set; } = "";
        public int[] OutCargo { get; set; } = new int[0];
        public int[] BackCargo { get; set; } = new int[0];
        public int CarCount { get; set; }
        public bool BackRevealed { get; set; }
    }

    /// <summary>Host → Clients: Beladen in B fertig, fehlende Container dürfen jetzt angezeigt werden.</summary>
    public class CircleHaulRevealPacket : IPacket
    {
        public string JobId { get; set; } = "";
    }

    /// <summary>Host → Clients: Auftrag beendet/entfernt.</summary>
    public class CircleHaulRemovePacket : IPacket
    {
        public string JobId { get; set; } = "";
    }
}
