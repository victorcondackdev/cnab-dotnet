using System.Diagnostics.CodeAnalysis;
using System.Text;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Internal;

namespace Cnab;

/// <summary>
/// Writes CNAB files, regenerating every control field so callers never
/// maintain them by hand. The default instance writes CRLF line endings with
/// a final terminator, which is what bank systems expect.
/// </summary>
/// <remarks>
/// The file model passed in is never modified. Writing to a stream uses
/// Latin-1 and rejects characters outside it instead of replacing them. On
/// error, output already written is not rolled back.
/// </remarks>
public sealed class CnabWriter
{
    private static readonly Encoding StrictLatin1 =
        Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>CRLF line endings with a final terminator.</summary>
    public static CnabWriter Default { get; } = new();

    /// <summary>The line terminator; <c>\r\n</c> by default (an empty value also means <c>\r\n</c>).</summary>
    public string LineEnding { get; init; } = "\r\n";

    /// <summary>Drops the terminator after the last record.</summary>
    public bool OmitFinalNewline { get; init; }

    private string Eol => string.IsNullOrEmpty(LineEnding) ? "\r\n" : LineEnding;

    /// <summary>
    /// Writes <paramref name="file"/> as CNAB 240.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>every record gets the file header's bank code;</item>
    /// <item>batches are numbered 1, 2, ... and every record of a batch carries its number; the file header and trailer get 0000 and 9999;</item>
    /// <item>type 3 records are numbered 1, 2, ... within their batch;</item>
    /// <item>batch and file trailer record counts and the batch count are set.</item>
    /// </list>
    /// Parsing a valid file and writing it back reproduces it byte for byte
    /// when the line endings match and every alfa field was already upper-case ASCII.
    /// </remarks>
    /// <param name="file">The file to write.</param>
    /// <param name="writer">The destination.</param>
    /// <exception cref="CnabFieldException">A record cannot be encoded; <see cref="CnabFieldException.Context"/> names it.</exception>
    /// <exception cref="CnabException">Too many batches or records (<see cref="CnabError.Count"/>), or a raw segment of the wrong length.</exception>
    public void Write(Cnab240File file, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(writer);
        if (file.Batches.Count > 9998)
        {
            throw CountError(string.Create(GoText.Inv, $"{file.Batches.Count} batches, at most 9998 fit"));
        }

        long records = 2;
        for (int i = 0; i < file.Batches.Count; i++)
        {
            int count = file.Batches[i].Segments.Count;
            if (count > 99999)
            {
                throw CountError(string.Create(GoText.Inv, $"batch {i + 1} has {count} records, at most 99999 fit"));
            }

            records += count + 2;
        }

        if (records > 999999)
        {
            throw CountError(string.Create(GoText.Inv, $"{records} records, at most 999999 fit"));
        }

        string eol = Eol;
        int total = 0;
        void Emit(ReadOnlySpan<char> record)
        {
            if (total > 0)
            {
                writer.Write(eol);
            }

            writer.Write(record);
            total++;
        }

        string line = Encode(file.Header with { Lote = 0 }, Layout240.LineLength, "file header");
        string bank = line.Substring(0, 3);
        Emit(line);
        char[] scratch = new char[Layout240.LineLength];
        for (int bi = 0; bi < file.Batches.Count; bi++)
        {
            Batch b = file.Batches[bi];
            int lote = bi + 1;
            string context = "batch " + GoText.Str(lote);
            Emit(Encode(b.Header with { Banco = bank, Lote = lote }, Layout240.LineLength, context + " header"));
            int seq = 0;
            for (int si = 0; si < b.Segments.Count; si++)
            {
                string encoded;
                try
                {
                    encoded = b.Segments[si].Encode();
                }
                catch (CnabFieldException e)
                {
                    throw e.WithContext(context + " record " + GoText.Str(si + 1));
                }
                catch (CnabException e)
                {
                    throw new CnabException(e.Error, context + " record " + GoText.Str(si + 1) + ": " + e.Message, e);
                }

                encoded.CopyTo(0, scratch, 0, Layout240.LineLength);
                bank.CopyTo(0, scratch, 0, 3);
                Codec.PutDigits(scratch.AsSpan(3, 4), lote);
                if (scratch[7] == RecordType.Detalhe)
                {
                    seq++;
                    Codec.PutDigits(scratch.AsSpan(8, 5), seq);
                }

                Emit(scratch);
            }

            BatchTrailer trailer = b.Trailer with { Banco = bank, Lote = lote, QuantidadeRegistros = b.Segments.Count + 2 };
            Emit(Encode(trailer, Layout240.LineLength, context + " trailer"));
        }

        FileTrailer fileTrailer = file.Trailer with
        {
            Banco = bank,
            Lote = 9999,
            QuantidadeLotes = file.Batches.Count,
            QuantidadeRegistros = total + 1,
        };
        Emit(Encode(fileTrailer, Layout240.LineLength, "file trailer"));
        if (!OmitFinalNewline)
        {
            writer.Write(eol);
        }
    }

    /// <summary>Writes <paramref name="file"/> as CNAB 240 to a stream, encoded as Latin-1.</summary>
    /// <param name="file">The file to write.</param>
    /// <param name="stream">The destination. It is flushed, not closed.</param>
    /// <exception cref="CnabException">The file cannot be encoded; see <see cref="Write(Cnab240File, TextWriter)"/>.</exception>
    public void Write(Cnab240File file, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, StrictLatin1, 64 * 1024, leaveOpen: true);
        Write(file, writer);
    }

    /// <summary>Writes <paramref name="file"/> as CNAB 240 to a string.</summary>
    /// <param name="file">The file to write.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="CnabException">The file cannot be encoded; see <see cref="Write(Cnab240File, TextWriter)"/>.</exception>
    public string WriteToString(Cnab240File file)
    {
        using var writer = new StringWriter(GoText.Inv);
        Write(file, writer);
        return writer.ToString();
    }

    /// <summary>Writes <paramref name="file"/> as CNAB 400, regenerating the sequence numbers (395-400).</summary>
    /// <param name="file">The file to write.</param>
    /// <param name="writer">The destination.</param>
    /// <exception cref="CnabFieldException">A record cannot be encoded; <see cref="CnabFieldException.Context"/> names it.</exception>
    public void Write(Cnab400File file, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(writer);
        string eol = Eol;
        int seq = 0;
        void Emit(string record)
        {
            if (seq > 1)
            {
                writer.Write(eol);
            }

            writer.Write(record);
        }

        seq++;
        Emit(Encode(file.Header with { Sequencial = 1 }, Layout400.LineLength, "record 1"));
        foreach (Detail detail in file.Details)
        {
            seq++;
            Emit(Encode(detail with { Sequencial = seq }, Layout400.LineLength, "record " + GoText.Str(seq)));
        }

        seq++;
        Emit(Encode(file.Trailer with { Sequencial = seq }, Layout400.LineLength, "record " + GoText.Str(seq)));
        if (!OmitFinalNewline)
        {
            writer.Write(eol);
        }
    }

    /// <summary>Writes <paramref name="file"/> as CNAB 400 to a stream, encoded as Latin-1.</summary>
    /// <param name="file">The file to write.</param>
    /// <param name="stream">The destination. It is flushed, not closed.</param>
    /// <exception cref="CnabException">The file cannot be encoded.</exception>
    public void Write(Cnab400File file, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, StrictLatin1, 64 * 1024, leaveOpen: true);
        Write(file, writer);
    }

    /// <summary>Writes <paramref name="file"/> as CNAB 400 to a string.</summary>
    /// <param name="file">The file to write.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="CnabException">The file cannot be encoded.</exception>
    public string WriteToString(Cnab400File file)
    {
        using var writer = new StringWriter(GoText.Inv);
        Write(file, writer);
        return writer.ToString();
    }

    private static CnabException CountError(string detail) =>
        new(CnabError.Count, CnabException.Describe(CnabError.Count) + ": " + detail);

    private static string Encode<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T record, int lineLength, string context)
        where T : class
    {
        try
        {
            return FixedWidth.Serialize(record, lineLength);
        }
        catch (CnabFieldException e)
        {
            throw e.WithContext(context);
        }
    }
}
