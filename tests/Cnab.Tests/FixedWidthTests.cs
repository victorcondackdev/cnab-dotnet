using Rec = Cnab.Tests.Parity.ParityTests.Rec;

namespace Cnab.Tests;

public class FixedWidthTests
{
    private const int RecLength = 47;

    private static readonly string Newline = ((char)10).ToString();

    public sealed record Gappy
    {
        [CnabField(1, 3, CnabFormat.Num)]
        public string A { get; set; } = string.Empty;

        [CnabField(5, 2, CnabFormat.Alfa)]
        public string B { get; set; } = string.Empty;

        [CnabField(6, 3, CnabFormat.Alfa)]
        public string C { get; set; } = string.Empty;
    }

    public sealed class BadAttributes
    {
        [CnabField(1, 0, CnabFormat.Num)]
        public string A { get; set; } = string.Empty;

        [CnabField(0, 1, CnabFormat.Num)]
        public string B { get; set; } = string.Empty;

        [CnabField(1, 1, CnabFormat.Num)]
        public bool C { get; set; }

        [CnabField(1, 10, CnabFormat.Num)]
        public int D { get; set; }

        [CnabField(1, 1, CnabFormat.Money)]
        public string E { get; set; } = string.Empty;

        [CnabField(1, 1, CnabFormat.Alfa)]
        public int F { get; set; }

        [CnabField(1, 8, CnabFormat.Date)]
        public string G { get; set; } = string.Empty;

        [CnabField(1, 7, CnabFormat.Date)]
        public DateOnly? H { get; set; }

        [CnabField(1, 1, CnabFormat.Const, Value = "1")]
        public int I { get; set; }

        [CnabField(1, 2, CnabFormat.Const, Value = "1")]
        public string J { get; set; } = string.Empty;

        [CnabField(1, 3, CnabFormat.Money)]
        public byte K { get; set; }

        [CnabField(1, 1, (CnabFormat)99)]
        public string L { get; set; } = string.Empty;

        [CnabField(1, 1, CnabFormat.Alfa)]
        public string M { get; } = string.Empty;

        [CnabField(1, 1, CnabFormat.Const)]
        public string N { get; set; } = string.Empty;

        [CnabField(1, 8, CnabFormat.Date)]
        public DateTime O { get; set; }
    }

    public sealed class NoFields
    {
        public string X { get; set; } = string.Empty;
    }

    public sealed class Leading
    {
        [CnabField(2, 2, CnabFormat.Num)]
        public string A { get; set; } = string.Empty;
    }

    public abstract record SharedPrefix
    {
        [CnabField(1, 2, CnabFormat.Num)]
        public string A { get; set; } = string.Empty;
    }

    public sealed record Embedded : SharedPrefix
    {
        [CnabField(3, 2, CnabFormat.Alfa)]
        public string B { get; set; } = string.Empty;
    }

    public enum Color
    {
        Red = 1,
        Blue = 42,
    }

    public sealed record WithEnum
    {
        [CnabField(1, 2, CnabFormat.Num)]
        public Color Color { get; set; }

        [CnabField(3, 4, CnabFormat.Num)]
        public short Short { get; set; }

        [CnabField(7, 3, CnabFormat.Num)]
        public int Init { get; init; }
    }

    [Fact]
    public void SerializeMatchesTheManualPositions()
    {
        var v = new Rec
        {
            Tipo = string.Empty, Codigo = "41", Qtd = 7, Nome = "São João", Data = TestData.D(2026, 12, 31),
            Data6 = TestData.D(2026, 1, 2), Valor = 12345, Livre = "ab", Small = 9,
        };
        Assert.Equal("3041007SAO JOAO  31122026020126000001234" + "5ab  09", FixedWidth.Serialize(v, RecLength));
    }

    [Fact]
    public void RoundTrip()
    {
        var want = new Rec
        {
            Tipo = "3", Codigo = "041", Qtd = 7, Nome = "SAO JOAO", Data = TestData.D(2026, 12, 31),
            Data6 = TestData.D(1999, 1, 2), Valor = 9999999999, Livre = "ab  ", Small = 99,
        };
        string line = FixedWidth.Serialize(want, RecLength);
        Assert.Equal(want, FixedWidth.Deserialize<Rec>(line));

        var target = new Rec();
        FixedWidth.Populate(line, target);
        Assert.Equal(want, target);
    }

    [Fact]
    public void BlanksAndZerosDecodeToEmptyValues()
    {
        string line = "3   " + "   " + new string(' ', 10) + "00000000" + "      " + new string(' ', 10) + "    " + "  ";
        Rec got = FixedWidth.Deserialize<Rec>(line);
        Assert.Equal(string.Empty, got.Codigo);
        Assert.Equal(0, got.Qtd);
        Assert.Null(got.Data);
        Assert.Null(got.Data6);
        Assert.Equal(0, got.Valor);
        Assert.Equal("    ", got.Livre);
    }

    [Theory]
    [InlineData(1, "4", "Tipo", CnabError.Const)]
    [InlineData(2, "4A1", "Codigo", CnabError.Digits)]
    [InlineData(5, "1 2", "Qtd", CnabError.Digits)]
    [InlineData(18, "31022026", "Data", CnabError.Date)]
    [InlineData(18, "3102202X", "Data", CnabError.Date)]
    [InlineData(26, "290223", "Data6", CnabError.Date)]
    [InlineData(32, "12345678-0", "Valor", CnabError.Digits)]
    public void DecodeErrorsNameTheField(int pos, string text, string field, CnabError error)
    {
        string line = TestData.Patch(FixedWidth.Serialize(new Rec(), RecLength), pos, text);
        CnabFieldException e = Assert.Throws<CnabFieldException>(() => FixedWidth.Deserialize<Rec>(line));
        Assert.Equal(error, e.Error);
        Assert.Equal(field, e.Field);
        Assert.Contains(field, e.Message, StringComparison.Ordinal);
        Assert.Equal(0, e.Line);
    }

    [Fact]
    public void LeapDaysAndByteLimitsDecode()
    {
        string line = TestData.Patch(TestData.Patch(FixedWidth.Serialize(new Rec(), RecLength), 26, "290200"), 46, "99");
        Rec got = FixedWidth.Deserialize<Rec>(line);
        Assert.Equal(TestData.D(2000, 2, 29), got.Data6);
        Assert.Equal(99, got.Small);
    }

    [Fact]
    public void WrongLineLengthIsReported()
    {
        string line = FixedWidth.Serialize(new Rec(), RecLength).Substring(0, 10);
        CnabFieldException e = Assert.Throws<CnabFieldException>(() => FixedWidth.Deserialize<Rec>(line));
        Assert.Equal(CnabError.LineLength, e.Error);
        Assert.Equal("cnab: field Rec (1-47): line has 10 bytes, want 47", e.Message);
    }

    [Fact]
    public void FieldExceptionMessageMatchesGo()
    {
        var e = new CnabFieldException(CnabError.Digits, "X", 1, 2, string.Empty, "bad", valueFromFile: true).WithLine(3);
        Assert.Equal("cnab: line 3: field X (1-2): bad", e.Message);
        Assert.Equal(3, e.Line);
        CnabFieldException withContext = e.WithContext("batch 1 header");
        Assert.Equal("batch 1 header: cnab: line 3: field X (1-2): bad", withContext.Message);
        Assert.Equal("batch 1 header", withContext.Context);
    }

    public static readonly TheoryData<string, CnabError> SerializeErrorCases = new()
    {
        { "const", CnabError.Const },
        { "digits", CnabError.Digits },
        { "num overflow", CnabError.Overflow },
        { "int overflow", CnabError.Overflow },
        { "negative", CnabError.Negative },
        { "alfa long", CnabError.Overflow },
        { "alfa charset", CnabError.Charset },
        { "alfa control", CnabError.Charset },
        { "raw newline", CnabError.Charset },
        { "raw non latin-1", CnabError.Charset },
        { "raw long", CnabError.Overflow },
        { "date6 year", CnabError.Date },
        { "money negative", CnabError.Negative },
        { "byte overflow", CnabError.Overflow },
    };

    [Theory]
    [MemberData(nameof(SerializeErrorCases))]
    public void SerializeErrors(string name, CnabError error)
    {
        Rec v = name switch
        {
            "const" => new Rec { Tipo = "X" },
            "digits" => new Rec { Codigo = "4a" },
            "num overflow" => new Rec { Codigo = "1234" },
            "int overflow" => new Rec { Qtd = 1000 },
            "negative" => new Rec { Qtd = -1 },
            "alfa long" => new Rec { Nome = "ABCDEFGHIJK" },
            "alfa charset" => new Rec { Nome = "日本" },
            "alfa control" => new Rec { Nome = "A" + (char)9 + "B" },
            "raw newline" => new Rec { Livre = "a" + Newline + "b" },
            "raw non latin-1" => new Rec { Livre = "€" },
            "raw long" => new Rec { Livre = "abcde" },
            "date6 year" => new Rec { Data6 = TestData.D(2070, 1, 1) },
            "money negative" => new Rec { Valor = -5 },
            "byte overflow" => new Rec { Small = 100 },
            _ => throw new InvalidOperationException(name),
        };
        CnabFieldException e = Assert.Throws<CnabFieldException>(() => FixedWidth.Serialize(v, RecLength));
        Assert.Equal(error, e.Error);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => FixedWidth.Serialize<Rec>(null!, RecLength));
        Assert.Throws<ArgumentNullException>(() => FixedWidth.Deserialize<Rec>(null!));
        Assert.Throws<ArgumentNullException>(() => FixedWidth.Populate<Rec>("x", null!));
    }

    [Fact]
    public void LayoutErrorsAreReportedTogether()
    {
        CnabLayoutException e = Assert.Throws<CnabLayoutException>(() => FixedWidth.Check<Gappy>(8));
        Assert.Equal(CnabError.Layout, e.Error);
        Assert.Contains("gap at 4-4 between A and B", e.Message, StringComparison.Ordinal);
        Assert.Contains("overlap: B (5-6) and C (6-8)", e.Message, StringComparison.Ordinal);

        e = Assert.Throws<CnabLayoutException>(() => FixedWidth.Check<BadAttributes>(1));
        Assert.True(e.Problems.Count >= 15, string.Join("; ", e.Problems));
        foreach (string name in new[] { "A:", "B:", "C:", "D:", "E:", "F:", "G:", "H:", "I:", "J:", "K:", "L:", "M:", "N:", "O:" })
        {
            Assert.Contains(name, e.Message, StringComparison.Ordinal);
        }

        Assert.Contains("D: num width 10 too wide for int (max 9); use string", e.Problems);
        Assert.Contains("M: property must have a public getter and setter", e.Problems);
        Assert.Contains("O: date needs a DateOnly? property, have DateTime", e.Problems);

        Assert.Throws<CnabLayoutException>(() => FixedWidth.Check<NoFields>(1));
        Assert.Contains("positions 48-50 not covered", Assert.Throws<CnabLayoutException>(() => FixedWidth.Check<Rec>(50)).Message, StringComparison.Ordinal);
        Assert.Contains("Valor (32-41) exceeds line length 40", Assert.Throws<CnabLayoutException>(() => FixedWidth.Check<Rec>(40)).Message, StringComparison.Ordinal);
        Assert.Throws<CnabLayoutException>(() => FixedWidth.Serialize(new Rec(), 40));
        Assert.Throws<CnabLayoutException>(() => FixedWidth.Serialize(new Gappy(), 8));
        Assert.Throws<CnabLayoutException>(() => FixedWidth.Deserialize<Gappy>("12345678"));
        Assert.Throws<CnabLayoutException>(() => FixedWidth.Width<Gappy>());
        Assert.Throws<CnabLayoutException>(() => FixedWidth.Fields<Gappy>());
        Assert.Equal("gap at 1-1 before A", Assert.Single(FixedWidth.Validate<Leading>(3)));
        Assert.Empty(FixedWidth.Validate<Rec>(RecLength));
    }

    [Fact]
    public void InheritedFieldsAndIntrospection()
    {
        FixedWidth.Check<Embedded>(4);
        Assert.Equal("07X ", FixedWidth.Serialize(new Embedded { A = "7", B = "x" }, 4));
        Assert.Equal(RecLength, FixedWidth.Width<Rec>());
        IReadOnlyList<CnabFieldInfo> fields = FixedWidth.Fields<Rec>();
        Assert.Equal(9, fields.Count);
        Assert.Equal(new CnabFieldInfo("Tipo", 1, 1, CnabFormat.Const, "3"), fields[0]);
        Assert.Equal(3, fields[1].Length);
    }

    [Fact]
    public void EnumsShortsAndInitAccessorsAreSupported()
    {
        var v = new WithEnum { Color = Color.Blue, Short = 1234, Init = 7 };
        string line = FixedWidth.Serialize(v, 9);
        Assert.Equal("421234007", line);
        Assert.Equal(v, FixedWidth.Deserialize<WithEnum>(line));
        Assert.Equal((Color)77, FixedWidth.Deserialize<WithEnum>("770000000").Color);
    }

    [Fact]
    public void Fold()
    {
        var cases = new Dictionary<string, string>
        {
            [string.Empty] = string.Empty,
            ["ABC 123"] = "ABC 123",
            ["ação"] = "ACAO",
            ["Avenida São João"] = "AVENIDA SAO JOAO",
            ["Nº 10, 2ª via"] = "NO 10, 2A VIA",
            ["Ñandú Ölçü"] = "NANDU OLCU",
            ["áàâãäéèêëíìîïóòôõöúùûüçñ"] = "AAAAAEEEEIIIIOOOOOUUUUCN",
        };
        foreach ((string input, string want) in cases)
        {
            Assert.Equal(want, AsciiFolding.Fold(input));
            Assert.True(AsciiFolding.TryFold(input, out string? folded));
            Assert.Equal(want, folded);
        }

        foreach (string bad in new[] { ((char)0xD800).ToString(), "a" + (char)0, "€", "x" + (char)0x7F })
        {
            Assert.Equal(CnabError.Charset, Assert.Throws<CnabException>(() => AsciiFolding.Fold(bad)).Error);
            Assert.False(AsciiFolding.TryFold(bad, out _));
        }

        Assert.Equal("rune '€' has no ASCII equivalent", Assert.Throws<CnabException>(() => AsciiFolding.Fold("€")).Message);
    }
}
