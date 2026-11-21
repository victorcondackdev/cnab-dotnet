using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Cnab.Cnab240;

namespace Cnab.Benchmarks;

/// <summary>Entry point: <c>dotnet run -c Release --project benchmarks/Cnab.Benchmarks -- --filter '*'</c>.</summary>
public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>Parsing and writing a 10,000-line CNAB 240 retorno (4,998 T/U pairs), as in the Go benchmarks.</summary>
[MemoryDiagnoser]
public class FileBenchmarks
{
    private string _text = string.Empty;
    private Cnab240File _file = new();

    [GlobalSetup]
    public void Setup()
    {
        _file = BuildRetorno(4998);
        _text = CnabWriter.Default.WriteToString(_file);
    }

    [Benchmark]
    public int Parse10k() => CnabFile.Parse(new StringReader(_text)).Trailer.QuantidadeRegistros;

    [Benchmark]
    public int Write10k()
    {
        var writer = new CountingWriter();
        CnabWriter.Default.Write(_file, writer);
        return writer.Count;
    }

    [Benchmark]
    public long DecodeSegmentT() => ((SegmentT)Segment.Decode(_text.Substring(2 * 242, 240))).Valor;

    internal static Cnab240File BuildRetorno(int pairs)
    {
        var batch = new Batch
        {
            Header = new BatchHeader { Banco = "341", Lote = 1, Operacao = CobrancaCodes.OperacaoRetorno, Servico = CobrancaCodes.ServicoCobranca, VersaoLayout = "060" },
        };
        for (int i = 0; i < pairs; i++)
        {
            batch.Segments.Add(new SegmentT
            {
                Banco = "341",
                Movimento = MovimentoRetorno.Liquidacao,
                Agencia = "01234",
                Conta = "000000056789",
                NossoNumero = (i + 1).ToString("D8", CultureInfo.InvariantCulture),
                Carteira = 1,
                NumeroDocumento = "NF-" + (i + 1).ToString(CultureInfo.InvariantCulture),
                Vencimento = new DateOnly(2026, 12, 10),
                Valor = 150000,
                BancoCobrador = "341",
                AgenciaCobradora = "00500",
                Moeda = CobrancaCodes.MoedaReal,
                TipoInscricao = CobrancaCodes.InscricaoCpf,
                NumeroInscricao = "000052998224725",
                Nome = "JOAO DA SILVA",
                NumeroContrato = "0000000000",
                Tarifa = 350,
            });
            batch.Segments.Add(new SegmentU
            {
                Banco = "341",
                Movimento = MovimentoRetorno.Liquidacao,
                ValorPago = 150000,
                ValorLiquido = 149650,
                DataOcorrencia = new DateOnly(2026, 12, 10),
                DataCredito = new DateOnly(2026, 12, 11),
                BancoCorrespondente = "000",
            });
        }

        var file = new Cnab240File
        {
            Header = new FileHeader
            {
                Banco = "341",
                TipoInscricao = CobrancaCodes.InscricaoCnpj,
                NumeroInscricao = "11222333000181",
                Agencia = "01234",
                Conta = "000000056789",
                NomeEmpresa = "ACME UTILIDADES LTDA",
                NomeBanco = "BANCO ITAU S.A.",
                CodigoRemessaRetorno = CobrancaCodes.CodigoRetorno,
                DataGeracao = new DateOnly(2026, 12, 17),
                HoraGeracao = "080000",
                NSA = 1077,
                VersaoLayout = "103",
            },
        };
        file.Batches.Add(batch);
        return file;
    }

    private sealed class CountingWriter : TextWriter
    {
        public int Count { get; private set; }

        public override System.Text.Encoding Encoding => System.Text.Encoding.Latin1;

        public override void Write(char value) => Count++;

        public override void Write(string? value) => Count += value?.Length ?? 0;

        public override void Write(ReadOnlySpan<char> buffer) => Count += buffer.Length;
    }
}

/// <summary>The attribute-driven codec on a single record.</summary>
[MemoryDiagnoser]
public class CodecBenchmarks
{
    private readonly SegmentP _segment = new()
    {
        Banco = "341",
        Lote = 1,
        Sequencial = 1,
        Movimento = MovimentoRemessa.Entrada,
        Agencia = "01234",
        Conta = "000000056789",
        NossoNumero = "00000001",
        Carteira = 1,
        NumeroDocumento = "NF-1001",
        Vencimento = new DateOnly(2026, 12, 10),
        Valor = 150000,
        Especie = Especie.DuplicataMercantil,
        Aceite = "N",
        Moeda = CobrancaCodes.MoedaReal,
    };

    private string _line = string.Empty;

    [GlobalSetup]
    public void Setup() => _line = _segment.Encode();

    [Benchmark]
    public string Serialize() => FixedWidth.Serialize(_segment, Layout240.LineLength);

    [Benchmark]
    public long Deserialize() => FixedWidth.Deserialize<SegmentP>(_line).Valor;

    [Benchmark]
    public string Fold() => AsciiFolding.Fold("Avenida São João, nº 1500 - Conceição");
}
