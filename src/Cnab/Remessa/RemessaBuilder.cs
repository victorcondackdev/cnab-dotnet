using System.Text;
using Cnab.Cnab240;
using Cnab.Internal;

namespace Cnab.Remessa;

/// <summary>
/// Builds a CNAB 240 cobrança remessa with a single batch. Problems found by
/// <see cref="AddBoleto"/> are collected and reported together by
/// <see cref="Build"/>.
/// </summary>
/// <example>
/// <code>
/// Cnab240File file = new RemessaBuilder(company, bank)
///     .WithNsa(42)
///     .AddBoleto(new Boleto { NossoNumero = "00000001", NumeroDocumento = "NF-1001", Vencimento = new DateOnly(2026, 12, 10), Valor = 150000, Pagador = pagador })
///     .Build();
/// </code>
/// </example>
public sealed class RemessaBuilder
{
    private readonly Company _company;
    private readonly Bank _bank;
    private readonly List<Titulo> _titles = new();
    private readonly List<string> _errors = new();
    private int _nsa = 1;
    private DateTime? _generatedAt;

    /// <summary>Starts a remessa for <paramref name="company"/> at <paramref name="bank"/>.</summary>
    /// <param name="company">The beneficiary.</param>
    /// <param name="bank">The bank.</param>
    public RemessaBuilder(Company company, Bank bank)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(bank);
        _company = company;
        _bank = bank;
    }

    /// <summary>The number of boletos accepted so far.</summary>
    public int Count => _titles.Count;

    /// <summary>
    /// Sets the file sequence number ("número sequencial do arquivo"), 1 by
    /// default; banks require it to increase with every file sent.
    /// </summary>
    /// <param name="nsa">The sequence number.</param>
    /// <returns>This builder.</returns>
    public RemessaBuilder WithNsa(int nsa)
    {
        _nsa = nsa;
        return this;
    }

    /// <summary>Sets the generation timestamp written in the headers; the current local time by default.</summary>
    /// <param name="generatedAt">The timestamp.</param>
    /// <returns>This builder.</returns>
    public RemessaBuilder WithGeneratedAt(DateTime generatedAt)
    {
        _generatedAt = generatedAt;
        return this;
    }

    /// <summary>Validates <paramref name="boleto"/> and appends its P, Q and (when needed) R segments.</summary>
    /// <param name="boleto">The title to register.</param>
    /// <returns>This builder.</returns>
    public RemessaBuilder AddBoleto(Boleto boleto)
    {
        ArgumentNullException.ThrowIfNull(boleto);
        int index = _titles.Count + _errors.Count + 1;
        string? problem = Validate(boleto, out Titulo? title);
        if (problem is not null || title is null)
        {
            _errors.Add("boleto " + GoText.Str(index) + " (nosso número " + GoText.Quote(boleto.NossoNumero) + "): " + problem);
        }
        else
        {
            _titles.Add(title);
        }

        return this;
    }

    /// <summary>Adds every boleto of <paramref name="boletos"/>.</summary>
    /// <param name="boletos">The titles to register.</param>
    /// <returns>This builder.</returns>
    public RemessaBuilder AddBoletos(IEnumerable<Boleto> boletos)
    {
        ArgumentNullException.ThrowIfNull(boletos);
        foreach (Boleto b in boletos)
        {
            AddBoleto(b);
        }

        return this;
    }

    /// <summary>
    /// Validates the company and bank data and returns the remessa. Control
    /// fields (batch numbers, sequences, counts) are set, so the result is
    /// consistent in memory; <see cref="CnabWriter"/> regenerates them anyway.
    /// </summary>
    /// <returns>The remessa file.</returns>
    /// <exception cref="RemessaException">Any boleto, the company or the bank is invalid; every problem is listed.</exception>
    public Cnab240File Build()
    {
        var errors = new List<string>(_errors);
        Company c = _company;
        Bank bk = _bank;
        string doc = Digits(c.Documento);
        int kind = DocId.Classify(doc);
        if (kind == DocId.Invalid)
        {
            errors.Add("company documento " + GoText.Quote(c.Documento) + " is not a valid CPF or CNPJ");
        }

        if (bk.Codigo.Length != 3 || !Codec.AllDigits(bk.Codigo))
        {
            errors.Add("bank code " + GoText.Quote(bk.Codigo) + " must have 3 digits");
        }

        string? nomeProblem = Text(c.Nome, 30, out string nome);
        if (nomeProblem is not null || nome.Length == 0)
        {
            errors.Add("company nome is required ASCII text");
        }

        string? bancoProblem = Text(bk.Nome, 30, out string nomeBanco);
        if (bancoProblem is not null)
        {
            errors.Add("bank nome: " + bancoProblem);
        }

        if (_titles.Count == 0 && _errors.Count == 0)
        {
            errors.Add("no boletos added");
        }

        if (errors.Count > 0)
        {
            throw new RemessaException(errors);
        }

        DateTime at = _generatedAt ?? DateTime.Now;
        DateOnly day = DateOnly.FromDateTime(at);
        string versaoArquivo = bk.VersaoArquivo.Length == 0 ? "103" : bk.VersaoArquivo;
        string versaoLote = bk.VersaoLote.Length == 0 ? "060" : bk.VersaoLote;
        var file = new Cnab240File
        {
            Header = new FileHeader
            {
                Banco = bk.Codigo,
                TipoInscricao = kind,
                NumeroInscricao = doc,
                Convenio = c.Convenio,
                Agencia = c.Agencia,
                AgenciaDV = c.AgenciaDV,
                Conta = c.Conta,
                ContaDV = c.ContaDV,
                AgenciaContaDV = c.AgenciaContaDV,
                NomeEmpresa = nome,
                NomeBanco = nomeBanco,
                CodigoRemessaRetorno = CobrancaCodes.CodigoRemessa,
                DataGeracao = day,
                HoraGeracao = at.ToString("HHmmss", GoText.Inv),
                NSA = _nsa,
                VersaoLayout = versaoArquivo,
            },
        };
        var batch = new Batch
        {
            Header = new BatchHeader
            {
                Banco = bk.Codigo,
                Lote = 1,
                Operacao = CobrancaCodes.OperacaoRemessa,
                Servico = CobrancaCodes.ServicoCobranca,
                VersaoLayout = versaoLote,
                TipoInscricao = kind,
                NumeroInscricao = doc,
                Convenio = c.Convenio,
                Agencia = c.Agencia,
                AgenciaDV = c.AgenciaDV,
                Conta = c.Conta,
                ContaDV = c.ContaDV,
                AgenciaContaDV = c.AgenciaContaDV,
                NomeEmpresa = nome,
                NumeroRemessa = _nsa,
                DataGravacao = day,
            },
        };
        int seq = 0;
        foreach (Titulo t in _titles)
        {
            batch.Segments.Add(t.P with { Banco = bk.Codigo, Lote = 1, Sequencial = ++seq, DataEmissao = t.P.DataEmissao ?? day });
            if (t.Q is not null)
            {
                batch.Segments.Add(t.Q with { Banco = bk.Codigo, Lote = 1, Sequencial = ++seq });
            }

            if (t.R is not null)
            {
                batch.Segments.Add(t.R with { Banco = bk.Codigo, Lote = 1, Sequencial = ++seq });
            }
        }

        batch.Trailer = new BatchTrailer { Banco = bk.Codigo, Lote = 1, QuantidadeRegistros = seq + 2 };
        file.Batches.Add(batch);
        file.Trailer = new FileTrailer { Banco = bk.Codigo, Lote = 9999, QuantidadeLotes = 1, QuantidadeRegistros = seq + 4 };

        // Encode every record once so field errors (for example a non-digit
        // agência) surface here rather than when the file is written.
        try
        {
            CnabWriter.Default.Write(file, TextWriter.Null);
        }
        catch (CnabException e)
        {
            throw new RemessaException(new[] { e.Message }, e);
        }

        return file;
    }

    /// <summary>Builds the remessa and writes it with <see cref="CnabWriter.Default"/>.</summary>
    /// <param name="writer">The destination.</param>
    /// <exception cref="RemessaException">The input is invalid.</exception>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        CnabWriter.Default.Write(Build(), writer);
    }

    /// <summary>Builds the remessa and writes it to a stream (Latin-1) with <see cref="CnabWriter.Default"/>.</summary>
    /// <param name="stream">The destination. It is flushed, not closed.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="RemessaException">The input is invalid.</exception>
    public long WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] bytes = CnabFile.TextEncoding.GetBytes(CnabWriter.Default.WriteToString(Build()));
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        return bytes.Length;
    }

    /// <summary>Builds the remessa and returns it as text.</summary>
    /// <returns>The file content, CRLF-terminated.</returns>
    /// <exception cref="RemessaException">The input is invalid.</exception>
    public string BuildText() => CnabWriter.Default.WriteToString(Build());

    private string? Validate(Boleto b, out Titulo? title)
    {
        title = null;
        string? nnProblem = Text(b.NossoNumero, 20, out string nn);
        if (nnProblem is not null || nn.Length == 0)
        {
            return "nosso número is required ASCII text";
        }

        if (Encoding.UTF8.GetByteCount(b.NossoNumero.Trim()) > 20)
        {
            return "nosso número longer than 20 characters";
        }

        if (b.Valor <= 0)
        {
            return "valor must be positive";
        }

        if (b.Vencimento is not DateOnly vencimento)
        {
            return "vencimento is required";
        }

        if (vencimento == DateOnly.MaxValue)
        {
            return "vencimento out of range";
        }

        if (b.NumeroDocumento.Length == 0 || Encoding.UTF8.GetByteCount(b.NumeroDocumento) > 15)
        {
            return "número do documento must have 1 to 15 characters";
        }

        if (b.JurosPorDia < 0 || b.Desconto < 0 || b.Abatimento < 0 || b.Multa < 0)
        {
            return "amounts must not be negative";
        }

        if (b.DiasProtesto < 0 || b.DiasProtesto > 99 || b.DiasBaixa < 0 || b.DiasBaixa > 999)
        {
            return "dias de protesto/baixa out of range";
        }

        Pagador pagador = b.Pagador;
        string doc = Digits(pagador.Documento);
        int kind = DocId.Classify(doc);
        if (kind == DocId.Invalid)
        {
            return "pagador documento " + GoText.Quote(pagador.Documento) + " is not a valid CPF or CNPJ";
        }

        string cep = Digits(pagador.CEP);
        if (cep.Length != 8 || cep.Contains('x', StringComparison.Ordinal))
        {
            return "pagador CEP " + GoText.Quote(pagador.CEP) + " must have 8 digits";
        }

        string? docProblem = Text(b.NumeroDocumento, 15, out string numeroDocumento);
        if (docProblem is not null)
        {
            return "número do documento: " + docProblem;
        }

        string endereco = string.Empty;
        string bairro = string.Empty;
        string cidade = string.Empty;
        string uf = string.Empty;
        string? pagadorProblem = Text(pagador.Nome, 40, out string nome);
        pagadorProblem ??= Text(pagador.Endereco, 40, out endereco);
        pagadorProblem ??= Text(pagador.Bairro, 15, out bairro);
        pagadorProblem ??= Text(pagador.Cidade, 15, out cidade);
        pagadorProblem ??= Text(pagador.UF, 2, out uf);
        if (pagadorProblem is not null)
        {
            return "pagador: " + pagadorProblem;
        }

        if (nome.Length == 0 || uf.Length != 2)
        {
            return "pagador nome and a 2-letter UF are required";
        }

        var q = new SegmentQ
        {
            Movimento = MovimentoRemessa.Entrada,
            TipoInscricao = kind,
            NumeroInscricao = doc,
            Nome = nome,
            Endereco = endereco,
            Bairro = bairro,
            Cidade = cidade,
            UF = uf,
            CEP = cep.Substring(0, 5),
            SufixoCEP = cep.Substring(5),
        };

        Company c = _company;
        var p = new SegmentP
        {
            Movimento = MovimentoRemessa.Entrada,
            Agencia = c.Agencia,
            AgenciaDV = c.AgenciaDV,
            Conta = c.Conta,
            ContaDV = c.ContaDV,
            AgenciaContaDV = c.AgenciaContaDV,
            NossoNumero = nn,
            Carteira = b.Carteira == 0 ? 1 : b.Carteira,
            FormaCadastramento = 1, // com cadastramento (registrada)
            TipoDocumento = "1", // tradicional
            EmissaoBoleto = 2, // cliente emite
            DistribuicaoBoleto = "2", // cliente distribui
            NumeroDocumento = numeroDocumento,
            Vencimento = vencimento,
            Valor = b.Valor,
            Especie = b.Especie == 0 ? Especie.DuplicataMercantil : b.Especie,
            Aceite = b.Aceite ? "A" : "N",
            DataEmissao = b.Emissao,
            CodigoJuros = CobrancaCodes.JurosIsento,
            Abatimento = b.Abatimento,
            CodigoProtesto = CobrancaCodes.NaoProtestar,
            Moeda = CobrancaCodes.MoedaReal,
        };
        if (b.JurosPorDia > 0)
        {
            p.CodigoJuros = CobrancaCodes.JurosValorDiario;
            p.DataJuros = vencimento.AddDays(1);
            p.Juros = b.JurosPorDia;
        }

        if (b.Desconto > 0)
        {
            p.CodigoDesconto = CobrancaCodes.DescontoValorFixo;
            p.Desconto = b.Desconto;
            p.DataDesconto = b.DescontoAte ?? vencimento;
        }

        if (b.DiasProtesto > 0)
        {
            p.CodigoProtesto = CobrancaCodes.ProtestarDiasCorridos;
            p.DiasProtesto = b.DiasProtesto;
        }

        if (b.DiasBaixa > 0)
        {
            p.CodigoBaixa = CobrancaCodes.BaixarDevolver;
            p.DiasBaixa = b.DiasBaixa.ToString("D3", GoText.Inv);
        }

        string? usoProblem = Text(b.UsoEmpresa, 25, out string usoEmpresa);
        if (usoProblem is not null)
        {
            return "uso da empresa: " + usoProblem;
        }

        p.UsoEmpresa = usoEmpresa;
        SegmentR? r = null;
        if (b.Multa > 0 || b.Mensagem.Length != 0)
        {
            r = new SegmentR { Movimento = MovimentoRemessa.Entrada };
            if (b.Multa > 0)
            {
                r.CodigoMulta = CobrancaCodes.MultaValorFixo;
                r.DataMulta = vencimento.AddDays(1);
                r.Multa = b.Multa;
            }

            string? msgProblem = Text(b.Mensagem, 40, out string mensagem);
            if (msgProblem is not null)
            {
                return "mensagem: " + msgProblem;
            }

            r.Mensagem3 = mensagem;
        }

        title = new Titulo(p, q, r);
        return null;
    }

    /// <summary>
    /// Keeps digits, drops the usual punctuation (<c>. - /</c> and spaces) and
    /// turns anything else into <c>x</c>, so invalid input stays invalid.
    /// </summary>
    private static string Digits(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9')
            {
                b.Append(c);
            }
            else if (c != '.' && c != '-' && c != '/' && c != ' ' && !char.IsLowSurrogate(c))
            {
                b.Append('x');
            }
        }

        return b.ToString();
    }

    /// <summary>Folds <paramref name="s"/> to CNAB alfa and truncates it to <paramref name="n"/> characters.</summary>
    private static string? Text(string s, int n, out string result)
    {
        string? problem = AsciiFolding.FoldCore(s.Trim(), out string folded);
        if (problem is not null)
        {
            result = string.Empty;
            return problem;
        }

        result = folded.Length > n ? folded.Substring(0, n).TrimEnd(' ') : folded;
        return null;
    }
}
