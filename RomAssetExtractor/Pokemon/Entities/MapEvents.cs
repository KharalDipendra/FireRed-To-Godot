using RomAssetExtractor.GbaSystem;
using System.Runtime.InteropServices;

namespace RomAssetExtractor.Pokemon.Entities
{
    // ─── Raw ROM structs (for binary reading) ───

    [StructLayout(LayoutKind.Sequential)]
    public struct RawMapEventsHeader
    {
        public byte ObjectEventCount;
        public byte WarpCount;
        public byte CoordEventCount;
        public byte BgEventCount;
        public Pointer ObjectEvents;
        public Pointer Warps;
        public Pointer CoordEvents;
        public Pointer BgEvents;
    }

    /// <summary>
    /// Person / NPC object event (24 bytes).
    /// GBA struct: ObjectEventTemplate in pokefirered decomp.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawObjectEvent
    {
        public byte LocalId;
        public byte GraphicsId;
        public byte Kind;           // inConnection / OBJ_KIND_*
        public byte Filler0;
        public short X;
        public short Y;
        public byte Elevation;
        public byte MovementType;
        public byte RangeXY;        // lower nybble = rangeX, upper = rangeY
        public byte Filler1;
        public ushort TrainerType;
        public ushort TrainerRange;  // also berry tree ID
        public Pointer Script;
        public ushort FlagId;
        public ushort Filler2;
    }

    /// <summary>Warp event (8 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawWarpEvent
    {
        public short X;
        public short Y;
        public byte Elevation;
        public byte WarpId;
        public byte DestMapNum;
        public byte DestMapGroup;
    }

    /// <summary>Coordinate / trigger event (16 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawCoordEvent
    {
        public short X;
        public short Y;
        public byte Elevation;
        public byte Filler0;
        public ushort Trigger;
        public ushort Index;
        public ushort Filler1;
        public Pointer Script;
    }

    /// <summary>Background / signpost event (12 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RawBgEvent
    {
        public short X;
        public short Y;
        public byte Elevation;
        public byte Kind;          // BG_EVENT_* type
        public ushort Filler0;
        public Pointer Data;       // script pointer or hidden-item data
    }

    // ─── High-level model (for JSON export) ───

    /// <summary>
    /// Contains all parsed map events: NPCs, warps, triggers, signs.
    /// </summary>
    public class MapEvents
    {
        public RawObjectEvent[] ObjectEvents { get; set; }
        public RawWarpEvent[] Warps { get; set; }
        public RawCoordEvent[] CoordEvents { get; set; }
        public RawBgEvent[] BgEvents { get; set; }

        public static MapEvents ReadEvents(PokemonRomReader reader, Pointer eventsPointer)
        {
            if (eventsPointer.IsNull)
                return new MapEvents
                {
                    ObjectEvents = new RawObjectEvent[0],
                    Warps = new RawWarpEvent[0],
                    CoordEvents = new RawCoordEvent[0],
                    BgEvents = new RawBgEvent[0],
                };

            reader.GoToPointer(eventsPointer);
            var header = reader.Read<RawMapEventsHeader>();

            var events = new MapEvents();

            // Object events (NPCs)
            if (header.ObjectEventCount > 0 && !header.ObjectEvents.IsNull)
            {
                reader.GoToPointer(header.ObjectEvents);
                events.ObjectEvents = reader.ReadMany<RawObjectEvent>(header.ObjectEventCount);
            }
            else
            {
                events.ObjectEvents = new RawObjectEvent[0];
            }

            // Warps
            if (header.WarpCount > 0 && !header.Warps.IsNull)
            {
                reader.GoToPointer(header.Warps);
                events.Warps = reader.ReadMany<RawWarpEvent>(header.WarpCount);
            }
            else
            {
                events.Warps = new RawWarpEvent[0];
            }

            // Coordinate/trigger events
            if (header.CoordEventCount > 0 && !header.CoordEvents.IsNull)
            {
                reader.GoToPointer(header.CoordEvents);
                events.CoordEvents = reader.ReadMany<RawCoordEvent>(header.CoordEventCount);
            }
            else
            {
                events.CoordEvents = new RawCoordEvent[0];
            }

            // BG/sign events
            if (header.BgEventCount > 0 && !header.BgEvents.IsNull)
            {
                reader.GoToPointer(header.BgEvents);
                events.BgEvents = reader.ReadMany<RawBgEvent>(header.BgEventCount);
            }
            else
            {
                events.BgEvents = new RawBgEvent[0];
            }

            return events;
        }
    }
}
