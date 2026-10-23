using System.Text;

namespace TipToy.Designer.Gme;

/// <summary>Aufgelöster Operand: Register-Nummer oder Konstante.</summary>
public readonly record struct ResValue(bool IsRegister, ushort Value);

public sealed record ResCondition(ResValue Left, byte[] Op, ResValue Right);

public sealed record ResCommand(ushort Reg, byte[] Op, ResValue Arg);

/// <summary>Eine Skriptzeile mit Indizes in die Audio-Tabelle.</summary>
public sealed record ResLine(List<ResCondition> Conditions, List<ResCommand> Commands, List<ushort> PlayList);

/// <summary>Inhalt einer GME-Datei (ohne Spiele und Timer).</summary>
public class GmeFile
{
    public uint ProductId { get; init; }
    public uint RawXor { get; init; } = 0x00000039;
    public byte AudioXor { get; init; } = 0xAD;
    public byte[] Comment { get; init; } = Array.Empty<byte>();
    public byte[] Date { get; init; } = Encoding.ASCII.GetBytes("19700101");
    public byte[] Language { get; init; } = Array.Empty<byte>();
    public List<List<ushort>> Welcome { get; init; } = new();
    public List<ushort> InitialRegisters { get; init; } = new();
    public SortedDictionary<ushort, List<ResLine>> Scripts { get; init; } = new();
    public List<byte[]> AudioFiles { get; init; } = new();
    public ushort Replay { get; init; }
    public ushort Stop { get; init; }
}

/// <summary>
/// Schreibt GME-Dateien. Byte-genaue Portierung von GMEWriter.hs aus tttool:
/// gleiche Reihenfolge der Tabellen, gleiche Offsets, Prüfsumme am Ende.
/// </summary>
public class GmeWriter
{
    readonly MemoryStream _ms = new();

    public static byte[] Write(GmeFile file)
    {
        var w = new GmeWriter();
        w.PutFile(file);
        var bytes = w._ms.ToArray();
        uint checksum = 0;
        foreach (var b in bytes)
            checksum += b;
        var result = new byte[bytes.Length + 4];
        bytes.CopyTo(result, 0);
        BitConverter.TryWriteBytes(result.AsSpan(bytes.Length), checksum);
        return result;
    }

    uint Pos => (uint)_ms.Position;

    void U8(byte v) => _ms.WriteByte(v);

    void U16(ushort v)
    {
        U8((byte)v);
        U8((byte)(v >> 8));
    }

    void U32(uint v)
    {
        U16((ushort)v);
        U16((ushort)(v >> 16));
    }

    void Bytes(byte[] b) => _ms.Write(b);

    /// <summary>Reserviert ein 32-Bit-Wort, das später mit <see cref="Patch"/> gefüllt wird.</summary>
    uint Reserve()
    {
        var p = Pos;
        U32(0);
        return p;
    }

    void Patch(uint at, uint value)
    {
        var save = _ms.Position;
        _ms.Position = at;
        U32(value);
        _ms.Position = save;
    }

    void Seek(uint to)
    {
        if (Pos > to)
            throw new InvalidOperationException($"Kopfdaten zu lang (0x{Pos:X} > 0x{to:X}). Kommentar oder Sprache kürzen.");
        while (Pos < to)
            U8(0);
    }

    void PutFile(GmeFile f)
    {
        var sto = Reserve();
        var mft = Reserve();
        U32(0x238b);
        var ast = Reserve();
        var gto = Reserve();
        U32(f.ProductId);
        var iro = Reserve();
        U32(f.RawXor);
        U8((byte)f.Comment.Length);
        Bytes(f.Comment);
        Bytes(f.Date);
        Bytes(f.Language);
        U8(0);
        Seek(0x0071);
        var ipllo = Reserve();
        Seek(0x0094);
        var sso = Reserve();
        Seek(0x0200);

        Patch(sto, Pos);
        PutScriptTable(f.Scripts);
        Patch(ast, Pos);
        U16(0); // keine zusätzliche Skripttabelle (Timer)
        Patch(gto, Pos);
        U32(0); // keine Spiele
        Patch(iro, Pos);
        U16((ushort)f.InitialRegisters.Count);
        foreach (var r in f.InitialRegisters)
            U16(r);
        Patch(mft, Pos);
        PutAudioTable(f.AudioXor, f.AudioFiles);
        Patch(ipllo, Pos);
        PutPlayListList(f.Welcome);
        Patch(sso, Pos);
        U16(f.Replay);
        U16(f.Stop);
        for (int i = 0; i < 20; i++)
            U16(0);
    }

    void PutScriptTable(SortedDictionary<ushort, List<ResLine>> scripts)
    {
        if (scripts.Count == 0)
            throw new InvalidOperationException("Das Projekt enthält keine Skripte.");
        ushort first = scripts.Keys.First(), last = scripts.Keys.Last();
        U32(last);
        U32(first);
        var slots = new List<(uint Slot, List<ResLine> Lines)>();
        for (int code = first; code <= last; code++)
        {
            if (scripts.TryGetValue((ushort)code, out var lines))
                slots.Add((Reserve(), lines));
            else
                U32(0xFFFFFFFF);
        }
        foreach (var (slot, lines) in slots)
        {
            Patch(slot, Pos);
            PutOffsets16(lines, PutLine);
        }
    }

    /// <summary>Anzahl (16 Bit), dann alle Offsets (32 Bit), dann die Einträge.</summary>
    void PutOffsets16<T>(List<T> items, Action<T> put)
    {
        U16((ushort)items.Count);
        var slots = items.Select(_ => Reserve()).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            Patch(slots[i], Pos);
            put(items[i]);
        }
    }

    void PutLine(ResLine line)
    {
        U16((ushort)line.Conditions.Count);
        foreach (var c in line.Conditions)
        {
            PutValue(c.Left);
            Bytes(c.Op);
            PutValue(c.Right);
        }
        U16((ushort)line.Commands.Count);
        foreach (var c in line.Commands)
        {
            U16(c.Reg);
            Bytes(c.Op);
            PutValue(c.Arg);
        }
        PutPlayList(line.PlayList);
    }

    void PutValue(ResValue v)
    {
        U8(v.IsRegister ? (byte)0 : (byte)1);
        U16(v.Value);
    }

    void PutPlayList(List<ushort> playList)
    {
        U16((ushort)playList.Count);
        foreach (var i in playList)
            U16(i);
    }

    void PutPlayListList(List<List<ushort>> lists) => PutOffsets16(lists, PutPlayList);

    void PutAudioTable(byte xor, List<byte[]> files)
    {
        var slots = new List<uint>();
        foreach (var f in files)
        {
            slots.Add(Reserve());
            U32((uint)f.Length);
        }
        for (int i = 0; i < files.Count; i++)
        {
            Patch(slots[i], Pos);
            Bytes(Cypher(xor, files[i]));
        }
    }

    /// <summary>XOR-Verschlüsselung der Audiodaten (0x00, 0xFF, x und x^0xFF bleiben unverändert).</summary>
    static byte[] Cypher(byte x, byte[] data)
    {
        var result = new byte[data.Length];
        byte xInv = (byte)(x ^ 0xFF);
        for (int i = 0; i < data.Length; i++)
        {
            byte n = data[i];
            result[i] = n == 0 || n == 0xFF || n == x || n == xInv ? n : (byte)(n ^ x);
        }
        return result;
    }
}
