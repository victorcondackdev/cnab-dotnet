using Cnab.Internal;

namespace Cnab.Tests;

public class MoneyAndDocIdTests
{
    [Theory]
    [InlineData(0L, "0.00", "R$ 0,00")]
    [InlineData(5L, "0.05", "R$ 0,05")]
    [InlineData(123456L, "1234.56", "R$ 1.234,56")]
    [InlineData(100000000L, "1000000.00", "R$ 1.000.000,00")]
    [InlineData(-1050L, "-10.50", "-R$ 10,50")]
    [InlineData(long.MinValue, "-92233720368547758.08", "-R$ 92.233.720.368.547.758,08")]
    public void Format(long cents, string dot, string brl)
    {
        Assert.Equal(dot, Money.Format(cents));
        Assert.Equal(brl, Money.FormatBrl(cents));
    }

    [Theory]
    [InlineData("1234", 123400L)]
    [InlineData("1234.5", 123450L)]
    [InlineData("1234.56", 123456L)]
    [InlineData("1234,56", 123456L)]
    [InlineData("1.234,56", 123456L)]
    [InlineData("1,234.56", 123456L)]
    [InlineData("R$ 10,00", 1000L)]
    [InlineData(",5", 50L)]
    [InlineData("0.01", 1L)]
    [InlineData("1.234", 123400L)]
    public void Parse(string text, long cents)
    {
        Assert.True(Money.TryParse(text, out long got));
        Assert.Equal(cents, got);
        Assert.Equal(cents, Money.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12.")]
    [InlineData("-5")]
    [InlineData("1.2.3a")]
    [InlineData("99999999999999999999")]
    public void ParseRejects(string text)
    {
        Assert.False(Money.TryParse(text, out _));
        Assert.Throws<FormatException>(() => Money.Parse(text));
    }

    [Fact]
    public void ParseRejectsNull() => Assert.False(Money.TryParse(null, out _));

    [Theory]
    [InlineData("52998224725", DocId.Cpf)]
    [InlineData("52998224724", DocId.Invalid)]
    [InlineData("11111111111", DocId.Invalid)]
    [InlineData("11222333000181", DocId.Cnpj)]
    [InlineData("11222333000182", DocId.Invalid)]
    [InlineData("00000000000000", DocId.Invalid)]
    [InlineData("1122233300018a", DocId.Invalid)]
    [InlineData("123", DocId.Invalid)]
    [InlineData("", DocId.Invalid)]
    [InlineData("11444777000161", DocId.Cnpj)]
    [InlineData("39053344705", DocId.Cpf)]
    public void Classify(string digits, int kind)
    {
        Assert.Equal(kind, DocId.Classify(digits));
    }
}
