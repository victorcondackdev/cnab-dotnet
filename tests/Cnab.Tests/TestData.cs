using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Cnab400.Bradesco;

namespace Cnab.Tests;

/// <summary>Access to the shared byte-exact samples in /testdata, and dump helpers.</summary>
internal static class TestData
{
    public static byte[] Bytes(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    /// <summary>The file decoded as Latin-1: one character per byte.</summary>
    public static string Text(string name) => Encoding.Latin1.GetString(Bytes(name));

    /// <summary>The CRLF-separated lines, without the final terminator.</summary>
    public static List<string> Lines(string name)
    {
        string text = Text(name);
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - 2);
        }

        return text.Split("\r\n").ToList();
    }

    public static Cnab240File Parse(string name) => CnabFile.Parse(new StringReader(Text(name)));

    public static string Join(IEnumerable<string> lines) => string.Join("\r\n", lines) + "\r\n";

    public static StringReader Reader(IEnumerable<string> lines) => new(Join(lines));

    public static string Patch(string line, int pos, string text) =>
        line.Substring(0, pos - 1) + text + line.Substring(pos - 1 + text.Length);

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(text)));

    public static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>Renders every field as Name=value in position order, exactly like the Go generator.</summary>
    public static string Dump<T>(T value)
        where T : class
    {
        var parts = new List<string>();
        foreach (CnabFieldInfo f in FixedWidth.Fields<T>())
        {
            object? v = typeof(T).GetProperty(f.Name)!.GetValue(value);
            parts.Add(f.Name + "=" + Format(v));
        }

        return string.Join("|", parts);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IConvertible c => Convert.ToInt64(c, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    public static string SegmentDump(Segment s) => s switch
    {
        RawSegment r => "raw|" + r.Line,
        SegmentP p => "P|" + Dump(p),
        SegmentQ q => "Q|" + Dump(q),
        SegmentR r => "R|" + Dump(r),
        SegmentT t => "T|" + Dump(t),
        SegmentU u => "U|" + Dump(u),
        _ => throw new InvalidOperationException("unexpected segment " + s.GetType().Name),
    };

    public static string FileDump(Cnab240File f)
    {
        var lines = new List<string> { "H|" + Dump(f.Header) };
        foreach (Batch b in f.Batches)
        {
            lines.Add("BH|" + Dump(b.Header));
            lines.AddRange(b.Segments.Select(SegmentDump));
            lines.Add("BT|" + Dump(b.Trailer));
        }

        lines.Add("FT|" + Dump(f.Trailer));
        return string.Join("\n", lines);
    }

    public static string File400Dump(Cnab400File f)
    {
        var lines = new List<string> { "H|" + Dump(f.Header) };
        foreach (Detail d in f.Details)
        {
            lines.Add("D|" + Dump(d));
            try
            {
                lines.Add("B|" + Dump(d.Decode<DetalheRemessa>()));
            }
            catch (CnabException e)
            {
                lines.Add("B!" + e.Message);
            }
        }

        lines.Add("T|" + Dump(f.Trailer));
        return string.Join("\n", lines);
    }
}
