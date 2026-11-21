using System.Text;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Reconciliation;
using Cnab.Remessa;

namespace Cnab.Tests;

/// <summary>
/// The .NET analogue of the Go fuzz targets: a deterministic stream of random
/// and mutated inputs that must never make the library throw anything other
/// than its documented <see cref="CnabException"/> family, and that must
/// preserve the invariants the Go fuzzers check (re-parsing written output
/// succeeds and writing is idempotent).
/// </summary>
public class RobustnessTests
{
    private const string Alphabet = "0123456789 ABPQRTUXZaz-./";

    // Non-ASCII, Latin-1, control characters, lone surrogates and an astral
    // character, built from code points so the source stays plain ASCII.
    private static readonly string[] Exotic =
        new[] { 0x00, 0x09, 0x0A, 0x0D, 0x1A, 0x7F, 0xA0, 0xE3, 0xFF, 0x20AC, 0xD800, 0xDC00, 0x2028, 0xFFFD }
            .Select(c => ((char)c).ToString())
            .Append(char.ConvertFromUtf32(0x1F642))
            .ToArray();

    private static readonly string[] Samples =
    {
        TestData.Text("remessa_341.rem"),
        TestData.Text("retorno_341.ret"),
        TestData.Text("remessa_237.rem"),
    };

    private static string RandomText(Random random, int maxLength)
    {
        var b = new StringBuilder();
        int n = random.Next(maxLength + 1);
        for (int i = 0; i < n; i++)
        {
            b.Append(random.Next(8) == 0 ? Exotic[random.Next(Exotic.Length)] : Alphabet[random.Next(Alphabet.Length)].ToString());
        }

        return b.ToString();
    }

    private static string Mutate(Random random, string s)
    {
        var b = new StringBuilder(s);
        int edits = 1 + random.Next(4);
        for (int i = 0; i < edits && b.Length > 0; i++)
        {
            int at = random.Next(b.Length);
            string insert = random.Next(6) == 0 ? Exotic[random.Next(Exotic.Length)] : Alphabet[random.Next(Alphabet.Length)].ToString();
            switch (random.Next(4))
            {
                case 0:
                    b.Remove(at, 1).Insert(at, insert);
                    break;
                case 1:
                    b.Insert(at, insert);
                    break;
                case 2:
                    b.Remove(at, 1);
                    break;
                default:
                    b.Remove(at, Math.Min(b.Length - at, 1 + random.Next(300)));
                    break;
            }
        }

        return b.ToString();
    }

    private static void Expect(List<string> failures, string input, Action action)
    {
        try
        {
            action();
        }
        catch (CnabException)
        {
            // Documented.
        }
        catch (Exception e)
        {
            failures.Add(e.GetType().Name + ": " + e.Message + " for input of length " + input.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void MutatedFilesOnlyThrowDocumentedExceptions()
    {
        var random = new Random(20261121);
        var failures = new List<string>();
        for (int i = 0; i < 3000 && failures.Count < 20; i++)
        {
            string input = Mutate(random, Samples[random.Next(Samples.Length)]);
            Expect(failures, input, () => CheckFile240(input));
            Expect(failures, input, () => CheckFile400(input));
            Expect(failures, input, () => CnabFile.Detect(input));
        }

        Assert.Empty(failures);
    }

    private static void CheckFile240(string input)
    {
        Cnab240File file = CnabFile.Parse(new StringReader(input), new ParseOptions { CheckTotals = input.Length % 2 == 0 });
        string first;
        try
        {
            first = CnabWriter.Default.WriteToString(file);
        }
        catch (CnabException)
        {
            return; // for example, non-ASCII alfa text the encoder rejects
        }

        Cnab240File again = CnabFile.Parse(new StringReader(first));
        string second = CnabWriter.Default.WriteToString(again);
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("write is not idempotent");
        }

        foreach (RetornoEntry entry in Reconciler.EntriesFrom(file))
        {
            _ = Reconciler.Key(entry.NossoNumero);
        }

        _ = Reconciler.Reconcile(Array.Empty<ExpectedBoleto>(), Reconciler.EntriesFrom(file)).Summary();
    }

    private static void CheckFile400(string input)
    {
        Cnab400File file = CnabFile.Parse400(new StringReader(input));
        string written;
        try
        {
            written = CnabWriter.Default.WriteToString(file);
        }
        catch (CnabException)
        {
            return;
        }

        _ = CnabFile.Parse400(new StringReader(written));
        foreach (Detail detail in file.Details)
        {
            try
            {
                _ = detail.Decode<Cnab400.Bradesco.DetalheRemessa>();
            }
            catch (CnabException)
            {
                // Documented.
            }
        }
    }

    [Fact]
    public void RandomSegmentLinesOnlyThrowDocumentedExceptions()
    {
        var random = new Random(4242);
        var failures = new List<string>();
        List<string> lines = TestData.Lines("remessa_341.rem").Concat(TestData.Lines("retorno_341.ret")).ToList();
        for (int i = 0; i < 5000 && failures.Count < 20; i++)
        {
            string line = random.Next(3) == 0 ? RandomText(random, 260) : Mutate(random, lines[random.Next(lines.Count)]);
            if (random.Next(2) == 0 && line.Length > 0)
            {
                line = line.PadRight(240).Substring(0, 240);
            }

            Expect(failures, line, () =>
            {
                Segment s = Segment.Decode(line);
                string encoded;
                try
                {
                    encoded = s.Encode();
                }
                catch (CnabException)
                {
                    return;
                }

                Segment again = Segment.Decode(encoded);
                if (!string.Equals(again.Encode(), encoded, StringComparison.Ordinal) || !again.Equals(Segment.Decode(encoded)))
                {
                    throw new InvalidOperationException("segment encoding is not idempotent");
                }
            });
            Expect(failures, line, () => FixedWidth.Deserialize<FileHeader>(line));
            Expect(failures, line, () => FixedWidth.Deserialize<BatchTrailer>(line));
            Expect(failures, line, () => AsciiFolding.Fold(line));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void RandomBuilderInputOnlyThrowsDocumentedExceptions()
    {
        var random = new Random(777);
        var failures = new List<string>();
        for (int i = 0; i < 1500 && failures.Count < 20; i++)
        {
            var company = new Company
            {
                Nome = RandomText(random, 35),
                Documento = random.Next(2) == 0 ? "11.222.333/0001-81" : RandomText(random, 18),
                Agencia = random.Next(2) == 0 ? "01234" : RandomText(random, 6),
                Conta = random.Next(2) == 0 ? "000000056789" : RandomText(random, 13),
                Convenio = RandomText(random, 22),
            };
            var bank = new Bank { Codigo = random.Next(2) == 0 ? "341" : RandomText(random, 4), Nome = RandomText(random, 32) };
            var boleto = new Boleto
            {
                NossoNumero = RandomText(random, 22),
                NumeroDocumento = RandomText(random, 17),
                Carteira = random.Next(-2, 12),
                Especie = (Especie)random.Next(-1, 120),
                Vencimento = random.Next(5) == 0 ? null : new DateOnly(1 + random.Next(9999), 1 + random.Next(12), 1 + random.Next(28)),
                Emissao = random.Next(2) == 0 ? null : new DateOnly(1 + random.Next(9999), 1 + random.Next(12), 1 + random.Next(28)),
                Valor = random.NextInt64(-10, long.MaxValue),
                JurosPorDia = random.NextInt64(-10, long.MaxValue),
                Desconto = random.Next(-10, 100000),
                Multa = random.Next(-10, 100000),
                DiasProtesto = random.Next(-5, 120),
                DiasBaixa = random.Next(-5, 1200),
                UsoEmpresa = RandomText(random, 30),
                Mensagem = RandomText(random, 45),
                Pagador = new Pagador
                {
                    Nome = RandomText(random, 45),
                    Documento = random.Next(2) == 0 ? "529.982.247-25" : RandomText(random, 15),
                    Endereco = RandomText(random, 45),
                    Bairro = RandomText(random, 20),
                    CEP = random.Next(2) == 0 ? "22240-003" : RandomText(random, 10),
                    Cidade = RandomText(random, 20),
                    UF = random.Next(2) == 0 ? "RJ" : RandomText(random, 3),
                },
            };
            Expect(failures, boleto.NossoNumero, () =>
            {
                string text = new RemessaBuilder(company, bank)
                    .WithNsa(random.Next(-1, 2000000))
                    .WithGeneratedAt(new DateTime(2026, 11, 20, 9, 30, 15))
                    .AddBoleto(boleto)
                    .BuildText();
                _ = CnabFile.Parse(new StringReader(text));
            });
        }

        Assert.Empty(failures);
    }
}
