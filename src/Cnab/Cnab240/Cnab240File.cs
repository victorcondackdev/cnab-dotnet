namespace Cnab.Cnab240;

/// <summary>A parsed CNAB 240 file: header, batches ("lotes") and trailer.</summary>
public sealed class Cnab240File
{
    /// <summary>The file header (record type 0).</summary>
    public FileHeader Header { get; set; } = new();

    /// <summary>The batches in file order.</summary>
    public List<Batch> Batches { get; set; } = new();

    /// <summary>The file trailer (record type 9).</summary>
    public FileTrailer Trailer { get; set; } = new();

    /// <summary>Returns the titles (P/Q/R groups) of every batch, in file order.</summary>
    /// <returns>The titles.</returns>
    public IReadOnlyList<Titulo> Titulos() => Batches.SelectMany(b => b.Titulos()).ToList();

    /// <summary>Returns the occurrences (T/U pairs) of every batch, in file order.</summary>
    /// <returns>The occurrences.</returns>
    public IReadOnlyList<Ocorrencia> Ocorrencias() => Batches.SelectMany(b => b.Ocorrencias()).ToList();
}

/// <summary>One batch ("lote"): its header, records in file order and trailer.</summary>
public sealed class Batch
{
    /// <summary>The batch header (record type 1).</summary>
    public BatchHeader Header { get; set; } = new();

    /// <summary>The batch records (types 2, 3 and 4) in file order.</summary>
    public List<Segment> Segments { get; set; } = new();

    /// <summary>The batch trailer (record type 5).</summary>
    public BatchTrailer Trailer { get; set; } = new();

    /// <summary>
    /// Groups the batch's P/Q/R segments into titles. A Q or R not preceded
    /// by a P is ignored; a second Q or R for the same P is ignored.
    /// </summary>
    /// <returns>The titles.</returns>
    public IReadOnlyList<Titulo> Titulos()
    {
        var titles = new List<Titulo>();
        foreach (Segment s in Segments)
        {
            switch (s)
            {
                case SegmentP p:
                    titles.Add(new Titulo(p, null, null));
                    break;
                case SegmentQ q when titles.Count > 0 && titles[^1].Q is null:
                    titles[^1] = titles[^1] with { Q = q };
                    break;
                case SegmentR r when titles.Count > 0 && titles[^1].R is null:
                    titles[^1] = titles[^1] with { R = r };
                    break;
            }
        }

        return titles;
    }

    /// <summary>
    /// Groups the batch's T/U segments into occurrences. A U not preceded by
    /// a T is ignored.
    /// </summary>
    /// <returns>The occurrences.</returns>
    public IReadOnlyList<Ocorrencia> Ocorrencias()
    {
        var occurrences = new List<Ocorrencia>();
        foreach (Segment s in Segments)
        {
            switch (s)
            {
                case SegmentT t:
                    occurrences.Add(new Ocorrencia(t, null));
                    break;
                case SegmentU u when occurrences.Count > 0 && occurrences[^1].U is null:
                    occurrences[^1] = occurrences[^1] with { U = u };
                    break;
            }
        }

        return occurrences;
    }
}

/// <summary>One title of a remessa: segment P plus the Q and R that follow it.</summary>
/// <param name="P">The title data.</param>
/// <param name="Q">The payer data, or <see langword="null"/> when absent.</param>
/// <param name="R">The optional data, or <see langword="null"/> when absent.</param>
public sealed record Titulo(SegmentP P, SegmentQ? Q, SegmentR? R);

/// <summary>One occurrence of a retorno: segment T plus the U that follows it.</summary>
/// <param name="T">The title data.</param>
/// <param name="U">The amounts and dates, or <see langword="null"/> when absent.</param>
public sealed record Ocorrencia(SegmentT T, SegmentU? U);
