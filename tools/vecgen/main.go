// Command vecgen generates the cross-language parity vectors used by the C#
// test suite. It runs the Go implementation (github.com/victorcondackdev/cnab-go)
// over the shared sample files in /testdata, over seeded random records,
// file mutations, remessa builder inputs and reconciliation scenarios, and
// writes the observed outputs as C# literals.
//
// Usage, from the repository root, with the Go module cloned next to it:
//
//	go -C tools/vecgen run . > tests/Cnab.Tests/Parity/GoVectors.g.cs
package main

import (
	"bytes"
	"crypto/sha256"
	"fmt"
	"math/rand"
	"os"
	"reflect"
	"strconv"
	"strings"
	"time"
	"unicode/utf16"
	"unicode/utf8"

	"github.com/victorcondackdev/cnab-go/cnab240"
	"github.com/victorcondackdev/cnab-go/cnab400"
	"github.com/victorcondackdev/cnab-go/cnab400/bradesco"
	"github.com/victorcondackdev/cnab-go/file"
	"github.com/victorcondackdev/cnab-go/fixedwidth"
	"github.com/victorcondackdev/cnab-go/reconcile"
)

const testdata = "../../testdata/"

var rng = rand.New(rand.NewSource(20261121))

// generatedAt is the fixed timestamp used for every remessa built here.
var generatedAt = time.Date(2026, 11, 20, 9, 30, 15, 0, time.UTC)

func read(name string) []byte {
	b, err := os.ReadFile(testdata + name)
	if err != nil {
		panic(err)
	}
	return b
}

// lit renders s as a C# string literal. Non-ASCII and control characters
// are written as \u escapes so the generated file is pure ASCII.
func lit(s string) string {
	if !utf8.ValidString(s) {
		panic(fmt.Sprintf("invalid UTF-8 in vector: %q", s))
	}
	var b strings.Builder
	b.WriteByte('"')
	for _, r := range s {
		switch {
		case r == '"':
			b.WriteString(`\"`)
		case r == '\\':
			b.WriteString(`\\`)
		case r >= 0x20 && r < 0x7F:
			b.WriteRune(r)
		default:
			for _, u := range utf16.Encode([]rune{r}) {
				fmt.Fprintf(&b, `\u%04X`, u)
			}
		}
	}
	b.WriteByte('"')
	return b.String()
}

func litArray(ss []string) string {
	if len(ss) == 0 {
		return "new string[0]"
	}
	parts := make([]string, len(ss))
	for i, s := range ss {
		parts[i] = lit(s)
	}
	return "new[] { " + strings.Join(parts, ", ") + " }"
}

func sha(b []byte) string { return fmt.Sprintf("%X", sha256.Sum256(b)) }

func errStr(err error) string { return "error|" + err.Error() }

type writer struct{ b bytes.Buffer }

func (w *writer) f(format string, args ...any) { fmt.Fprintf(&w.b, format, args...) }

func (w *writer) table(name, tupleType string, rows [][]string) {
	w.f("\n    public static readonly %s[] %s =\n    {\n", tupleType, name)
	for _, r := range rows {
		w.f("        (%s),\n", strings.Join(r, ", "))
	}
	w.f("    };\n")
}

func (w *writer) strings(name string, values []string) {
	w.f("\n    public static readonly string[] %s =\n    {\n", name)
	for _, v := range values {
		w.f("        %s,\n", lit(v))
	}
	w.f("    };\n")
}

// --- dumps -----------------------------------------------------------------

func fieldValue(v reflect.Value) string {
	switch v.Kind() {
	case reflect.String:
		return v.String()
	case reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64:
		return strconv.FormatInt(v.Int(), 10)
	case reflect.Uint, reflect.Uint8, reflect.Uint16, reflect.Uint32, reflect.Uint64:
		return strconv.FormatUint(v.Uint(), 10)
	case reflect.Struct:
		t := v.Interface().(time.Time)
		if t.IsZero() {
			return ""
		}
		return t.Format(time.DateOnly)
	}
	panic("unsupported kind " + v.Kind().String())
}

// dump renders every field of a record as Name=value, in position order.
func dump(v any) string {
	fs, err := fixedwidth.Fields(v)
	if err != nil {
		panic(err)
	}
	rv := reflect.ValueOf(v)
	if rv.Kind() == reflect.Pointer {
		rv = rv.Elem()
	}
	parts := make([]string, len(fs))
	for i, f := range fs {
		parts[i] = f.Name + "=" + fieldValue(rv.FieldByName(f.Name))
	}
	return strings.Join(parts, "|")
}

func segDump(s cnab240.Segment) string {
	switch s := s.(type) {
	case *cnab240.RawSegment:
		return "raw|" + s.Line
	default:
		return string(s.Code()) + "|" + dump(s)
	}
}

func fileDump(f *cnab240.File) string {
	lines := []string{"H|" + dump(&f.Header)}
	for _, b := range f.Batches {
		lines = append(lines, "BH|"+dump(&b.Header))
		for _, s := range b.Segments {
			lines = append(lines, segDump(s))
		}
		lines = append(lines, "BT|"+dump(&b.Trailer))
	}
	lines = append(lines, "FT|"+dump(&f.Trailer))
	return strings.Join(lines, "\n")
}

func file400Dump(f *cnab400.File) string {
	lines := []string{"H|" + dump(&f.Header)}
	for _, d := range f.Details {
		lines = append(lines, "D|"+dump(&d))
		var det bradesco.DetalheRemessa
		if err := d.Decode(&det); err != nil {
			lines = append(lines, "B!"+err.Error())
		} else {
			lines = append(lines, "B|"+dump(&det))
		}
	}
	lines = append(lines, "T|"+dump(&f.Trailer))
	return strings.Join(lines, "\n")
}

func write240(f *cnab240.File, wr file.Writer) string {
	var buf bytes.Buffer
	if err := wr.Write(&buf, f); err != nil {
		return "write " + errStr(err)
	}
	return "ok|" + sha(buf.Bytes())
}

func write400(f *cnab400.File) string {
	var buf bytes.Buffer
	if err := file.Write400(&buf, f); err != nil {
		return "write " + errStr(err)
	}
	return "ok|" + sha(buf.Bytes())
}

func main() {
	w := &writer{}
	w.f("// <auto-generated>\n")
	w.f("// Generated by tools/vecgen from the Go implementation (github.com/victorcondackdev/cnab-go).\n")
	w.f("// Do not edit by hand; regenerate with: go -C tools/vecgen run . > tests/Cnab.Tests/Parity/GoVectors.g.cs\n")
	w.f("// </auto-generated>\n\n")
	w.f("namespace Cnab.Tests.Parity;\n\n")
	w.f("internal static class GoVectors\n{")

	genLayouts(w)
	genFiles(w)
	genBuilder(w)
	genSegments(w)
	genSegmentMutations(w)
	genParseMutations(w)
	genReconcile(w)
	genCodec(w)
	genFold(w)
	genKeys(w)

	w.f("}\n")
	if _, err := os.Stdout.Write(w.b.Bytes()); err != nil {
		panic(err)
	}
}

// --- layouts ---------------------------------------------------------------

func genLayouts(w *writer) {
	types := []struct {
		name string
		v    any
	}{
		{"FileHeader", cnab240.FileHeader{}}, {"BatchHeader", cnab240.BatchHeader{}},
		{"SegmentP", cnab240.SegmentP{}}, {"SegmentQ", cnab240.SegmentQ{}}, {"SegmentR", cnab240.SegmentR{}},
		{"SegmentT", cnab240.SegmentT{}}, {"SegmentU", cnab240.SegmentU{}},
		{"BatchTrailer", cnab240.BatchTrailer{}}, {"FileTrailer", cnab240.FileTrailer{}},
		{"Header400", cnab400.Header{}}, {"Detail400", cnab400.Detail{}}, {"Trailer400", cnab400.Trailer{}},
		{"DetalheRemessa", bradesco.DetalheRemessa{}},
	}
	var rows [][]string
	for _, t := range types {
		fs, err := fixedwidth.Fields(t.v)
		if err != nil {
			panic(err)
		}
		parts := make([]string, len(fs))
		for i, f := range fs {
			parts[i] = fmt.Sprintf("%s:%d-%d:%s", f.Name, f.Start, f.End, f.Format)
			if f.Const != "" {
				parts[i] += ":" + f.Const
			}
		}
		rows = append(rows, []string{lit(t.name), lit(strings.Join(parts, ","))})
	}
	w.table("Layouts", "(string Type, string Fields)", rows)
}

// --- sample files ----------------------------------------------------------

func genFiles(w *writer) {
	var rows [][]string
	for _, name := range []string{"remessa_341.rem", "retorno_341.ret"} {
		data := read(name)
		f, err := file.Parse(bytes.NewReader(data))
		if err != nil {
			panic(err)
		}
		crlf := write240(f, file.Writer{})
		lf := bytes.TrimSuffix(bytes.ReplaceAll(data, []byte("\r\n"), []byte("\n")), []byte("\n"))
		f2, err := file.Parse(bytes.NewReader(lf))
		if err != nil {
			panic(err)
		}
		lfOut := write240(f2, file.Writer{LineEnding: "\n", OmitFinalNewline: true})
		totals := "ok"
		if _, err := (file.Options{CheckTotals: true}).Parse(bytes.NewReader(data)); err != nil {
			totals = errStr(err)
		}
		rows = append(rows, []string{lit(name), lit(fileDump(f)), lit(crlf), lit(lfOut), lit(totals), lit("ok|" + sha(data))})
	}
	w.table("Files240", "(string Name, string Dump, string Write, string WriteLf, string Totals, string Input)", rows)

	data := read("remessa_237.rem")
	f, err := file.Parse400(bytes.NewReader(data))
	if err != nil {
		panic(err)
	}
	w.table("Files400", "(string Name, string Dump, string Write, string Input)",
		[][]string{{lit("remessa_237.rem"), lit(file400Dump(f)), lit(write400(f)), lit("ok|" + sha(data))}})

	// Entries of the sample retorno, as the reconciler sees them.
	rf, err := file.Parse(bytes.NewReader(read("retorno_341.ret")))
	if err != nil {
		panic(err)
	}
	var entries []string
	for _, e := range reconcile.EntriesFromFile(rf) {
		entries = append(entries, entryPipe(e))
	}
	w.strings("SampleEntries", entries)
}

// --- remessa builder -------------------------------------------------------

var sampleCompany = file.Company{Nome: "Acme Utilidades Ltda", Documento: "11.222.333/0001-81",
	Agencia: "01234", Conta: "000000056789", ContaDV: "0"}

var sampleBank = file.Bank{Codigo: "341", Nome: "Banco Itaú S.A."}

func companyPipe(c file.Company) string {
	return strings.Join([]string{c.Nome, c.Documento, c.Convenio, c.Agencia, c.AgenciaDV, c.Conta, c.ContaDV, c.AgenciaContaDV}, "|")
}

func bankPipe(b file.Bank) string {
	return strings.Join([]string{b.Codigo, b.Nome, b.VersaoArquivo, b.VersaoLote}, "|")
}

func dateStr(t time.Time) string {
	if t.IsZero() {
		return ""
	}
	return t.Format(time.DateOnly)
}

func boletoPipe(b file.Boleto) string {
	aceite := "0"
	if b.Aceite {
		aceite = "1"
	}
	p := b.Pagador
	return strings.Join([]string{
		b.NossoNumero, b.NumeroDocumento, strconv.Itoa(b.Carteira), strconv.Itoa(int(b.Especie)), aceite,
		dateStr(b.Emissao), dateStr(b.Vencimento), strconv.FormatInt(b.Valor, 10), strconv.FormatInt(b.JurosPorDia, 10),
		strconv.FormatInt(b.Desconto, 10), dateStr(b.DescontoAte), strconv.FormatInt(b.Abatimento, 10),
		strconv.FormatInt(b.Multa, 10), strconv.Itoa(b.DiasProtesto), strconv.Itoa(b.DiasBaixa), b.UsoEmpresa, b.Mensagem,
		p.Nome, p.Documento, p.Endereco, p.Bairro, p.CEP, p.Cidade, p.UF,
	}, "|")
}

func d(y, m, dd int) time.Time { return time.Date(y, time.Month(m), dd, 0, 0, 0, 0, time.UTC) }

var (
	pagJoao = file.Pagador{Nome: "João da Silva Sauro", Documento: "529.982.247-25", Endereco: "Rua das Laranjeiras, 1500 apto 302",
		Bairro: "Laranjeiras", CEP: "22240-003", Cidade: "Rio de Janeiro", UF: "RJ"}
	pagPadaria = file.Pagador{Nome: "Padaria Pão Quente Ltda", Documento: "11222333000181", Endereco: "Av. Brasil, 2000",
		Bairro: "Penha", CEP: "21011-000", Cidade: "Rio de Janeiro", UF: "RJ"}
)

// sampleBoletos rebuilds testdata/remessa_341.rem.
func sampleBoletos() []file.Boleto {
	em := d(2026, 11, 20)
	return []file.Boleto{
		{NossoNumero: "00000001", NumeroDocumento: "NF-1001", Vencimento: d(2026, 12, 10), Valor: 150000, Pagador: pagJoao, JurosPorDia: 50, Emissao: em},
		{NossoNumero: "00000002", NumeroDocumento: "NF-1002", Vencimento: d(2026, 12, 15), Valor: 89990, Pagador: pagPadaria, JurosPorDia: 30, Multa: 1800, Mensagem: "Não receber após 30 dias do vencimento", Emissao: em, DiasProtesto: 5},
		{NossoNumero: "00000003", NumeroDocumento: "NF-1003", Vencimento: d(2026, 12, 20), Valor: 25000, Pagador: pagJoao, Desconto: 1000, DescontoAte: d(2026, 12, 10), Emissao: em},
		{NossoNumero: "00000004", NumeroDocumento: "NF-1004", Vencimento: d(2026, 12, 20), Valor: 42050, Pagador: pagPadaria, Desconto: 2050, Emissao: em},
		{NossoNumero: "00000005", NumeroDocumento: "NF-1005", Vencimento: d(2026, 12, 30), Valor: 10000, Pagador: pagJoao, Emissao: em},
	}
}

func build(c file.Company, bk file.Bank, nsa int, boletos []file.Boleto) string {
	r := file.NewRemessa(c, bk).WithNSA(nsa).WithGeneratedAt(generatedAt)
	for _, b := range boletos {
		r.AddBoleto(b)
	}
	var buf bytes.Buffer
	if _, err := r.WriteTo(&buf); err != nil {
		return errStr(err)
	}
	return "ok|" + sha(buf.Bytes())
}

var (
	textPool = []string{"Maria", "José", "Conceição", "Ana Paula", "Rua São João, nº 10", "Av. Atlântica 1702",
		"Ipanema", "Niterói", "São Gonçalo", "Belo Horizonte", "Ñandú Ölçü", "2ª via", "Padaria Pão Quente",
		"Comércio de Peças Ltda", "Centro", "Copacabana", "ação", "Avenida Presidente Juscelino Kubitschek de Oliveira 5000"}
	badText = []string{"€uro", "tab\there", "ctl\x01", "日本"}
	cpfs    = []string{"529.982.247-25", "111.444.777-35", "390.533.447-05", "11222333000181", "11.444.777/0001-61"}
	badDocs = []string{"529.982.247-24", "111.111.111-11", "123", "", "11222333000182", "abc.def.ghi-jk"}
	ceps    = []string{"22240-003", "20000000", "01310-100", "30130 010"}
	badCeps = []string{"2224000", "2224000A", "", "22.240-0033"}
	ufs     = []string{"RJ", "SP", "MG", "rj"}
)

func pick(pool []string) string { return pool[rng.Intn(len(pool))] }

func maybe(p float64) bool { return rng.Float64() < p }

func randDate(y0, y1 int) time.Time {
	return d(y0+rng.Intn(y1-y0+1), 1+rng.Intn(12), 1+rng.Intn(28))
}

func randBoleto(i int) file.Boleto {
	b := file.Boleto{
		NossoNumero:     fmt.Sprintf("%0*d", 1+rng.Intn(10), rng.Intn(100000)),
		NumeroDocumento: fmt.Sprintf("NF-%d", 1000+i),
		Vencimento:      randDate(2026, 2027),
		Valor:           int64(1 + rng.Intn(5000000)),
		Pagador: file.Pagador{Nome: pick(textPool), Documento: pick(cpfs), Endereco: pick(textPool),
			Bairro: pick(textPool), CEP: pick(ceps), Cidade: pick(textPool), UF: pick(ufs)},
	}
	if maybe(0.3) {
		b.Carteira = 1 + rng.Intn(9)
	}
	if maybe(0.3) {
		b.Especie = cnab240.Especie([]int{2, 4, 12, 17, 99}[rng.Intn(5)])
	}
	b.Aceite = maybe(0.3)
	if maybe(0.5) {
		b.Emissao = randDate(2026, 2026)
	}
	if maybe(0.5) {
		b.JurosPorDia = int64(rng.Intn(500))
	}
	if maybe(0.4) {
		b.Desconto = int64(1 + rng.Intn(5000))
		if maybe(0.5) {
			b.DescontoAte = randDate(2026, 2027)
		}
	}
	if maybe(0.2) {
		b.Abatimento = int64(rng.Intn(1000))
	}
	if maybe(0.3) {
		b.Multa = int64(1 + rng.Intn(3000))
	}
	if maybe(0.3) {
		b.DiasProtesto = rng.Intn(100)
	}
	if maybe(0.3) {
		b.DiasBaixa = rng.Intn(1000)
	}
	if maybe(0.3) {
		b.UsoEmpresa = pick(textPool)
	}
	if maybe(0.3) {
		b.Mensagem = pick(textPool)
	}
	// One problem in roughly every fifth boleto.
	if maybe(0.2) {
		switch rng.Intn(14) {
		case 0:
			b.NossoNumero = ""
		case 1:
			b.NossoNumero = strings.Repeat("9", 21)
		case 2:
			b.Valor = -int64(rng.Intn(2))
		case 3:
			b.Vencimento = time.Time{}
		case 4:
			b.NumeroDocumento = strings.Repeat("X", 16)
		case 5:
			b.Pagador.Documento = pick(badDocs)
		case 6:
			b.Pagador.CEP = pick(badCeps)
		case 7:
			b.Pagador.Nome = pick(badText)
		case 8:
			b.Pagador.UF = "R"
		case 9:
			b.Mensagem = pick(badText)
		case 10:
			b.UsoEmpresa = pick(badText)
		case 11:
			b.DiasProtesto = 100
		case 12:
			b.Desconto = -1
		default:
			b.NumeroDocumento = pick(badText)
		}
	}
	return b
}

func genBuilder(w *writer) {
	r := file.NewRemessa(sampleCompany, sampleBank).WithNSA(42).WithGeneratedAt(generatedAt)
	for _, b := range sampleBoletos() {
		r.AddBoleto(b)
	}
	var buf bytes.Buffer
	if _, err := r.WriteTo(&buf); err != nil {
		panic(err)
	}
	if !bytes.Equal(buf.Bytes(), read("remessa_341.rem")) {
		panic("builder sample differs from testdata/remessa_341.rem")
	}
	w.f("\n    public const string BuilderSample =\n        %s;\n", lit(buf.String()))

	type scenario struct {
		c       file.Company
		bk      file.Bank
		nsa     int
		boletos []file.Boleto
	}
	var scenarios []scenario
	scenarios = append(scenarios, scenario{sampleCompany, sampleBank, 42, sampleBoletos()})
	for i := 0; i < 40; i++ {
		n := 1 + rng.Intn(5)
		var bs []file.Boleto
		for j := 0; j < n; j++ {
			bs = append(bs, randBoleto(j))
		}
		scenarios = append(scenarios, scenario{sampleCompany, sampleBank, 1 + rng.Intn(999999), bs})
	}
	ok := []file.Boleto{sampleBoletos()[0]}
	scenarios = append(scenarios,
		scenario{sampleCompany, sampleBank, 1, nil},
		scenario{file.Company{Nome: "X", Documento: "1"}, sampleBank, 1, ok},
		scenario{sampleCompany, file.Bank{Codigo: "34"}, 1, ok},
		scenario{file.Company{Documento: sampleCompany.Documento}, sampleBank, 1, ok},
		scenario{sampleCompany, file.Bank{Codigo: "341", Nome: "ctl\x01"}, 1, ok},
		scenario{file.Company{Nome: "X", Documento: sampleCompany.Documento, Agencia: "12a"}, sampleBank, 1, ok},
		scenario{file.Company{Nome: "Ação", Documento: "529.982.247-25", Agencia: "1", Conta: "2", Convenio: "conv 77"},
			file.Bank{Codigo: "001", Nome: "Banco do Brasil", VersaoArquivo: "084", VersaoLote: "042"}, 7, ok},
		scenario{sampleCompany, file.Bank{Codigo: "237"}, 1000000, ok},
	)
	var rows [][]string
	for _, s := range scenarios {
		var pipes []string
		for _, b := range s.boletos {
			pipes = append(pipes, boletoPipe(b))
		}
		rows = append(rows, []string{lit(companyPipe(s.c)), lit(bankPipe(s.bk)), strconv.Itoa(s.nsa), litArray(pipes), lit(build(s.c, s.bk, s.nsa, s.boletos))})
	}
	w.table("Builder", "(string Company, string Bank, int Nsa, string[] Boletos, string Result)", rows)
}

// --- random segments -------------------------------------------------------

const alfaChars = "ABCXYZ0189 -./"

func randomize(v any) {
	fs, err := fixedwidth.Fields(v)
	if err != nil {
		panic(err)
	}
	rv := reflect.ValueOf(v).Elem()
	for _, f := range fs {
		fv := rv.FieldByName(f.Name)
		width := f.End - f.Start + 1
		switch f.Format {
		case "const":
			continue
		case "alfa", "raw":
			n := rng.Intn(width + 1)
			var b strings.Builder
			for i := 0; i < n; i++ {
				b.WriteByte(alfaChars[rng.Intn(len(alfaChars))])
			}
			fv.SetString(b.String())
		case "num", "money":
			digits := width
			if fv.Kind() != reflect.String && digits > 15 {
				digits = 15
			}
			n := rng.Intn(digits + 1)
			var b strings.Builder
			for i := 0; i < n; i++ {
				b.WriteByte(byte('0' + rng.Intn(10)))
			}
			s := b.String()
			if fv.Kind() == reflect.String {
				fv.SetString(s)
			} else {
				x, _ := strconv.ParseInt("0"+s, 10, 64)
				fv.SetInt(x)
			}
		case "date":
			if maybe(0.2) {
				fv.Set(reflect.ValueOf(time.Time{}))
			} else {
				fv.Set(reflect.ValueOf(randDate(1990, 2060)))
			}
		}
	}
}

func genSegments(w *writer) {
	makers := []func() cnab240.Segment{
		func() cnab240.Segment { return new(cnab240.SegmentP) },
		func() cnab240.Segment { return new(cnab240.SegmentQ) },
		func() cnab240.Segment { return new(cnab240.SegmentR) },
		func() cnab240.Segment { return new(cnab240.SegmentT) },
		func() cnab240.Segment { return new(cnab240.SegmentU) },
	}
	var rows [][]string
	for i := 0; i < 75; i++ {
		s := makers[i%len(makers)]()
		randomize(s)
		line, err := cnab240.EncodeSegment(s)
		if err != nil {
			panic(err)
		}
		got, err := cnab240.DecodeSegment(line)
		if err != nil {
			panic(err)
		}
		if again, err := cnab240.EncodeSegment(got); err != nil || again != line {
			panic("segment round trip failed")
		}
		rows = append(rows, []string{lit(line), lit(segDump(got))})
	}
	w.table("Segments", "(string Line, string Dump)", rows)
}

// --- segment mutations -----------------------------------------------------

const mutationChars = "0123456789 AZaz-?"

func sampleLines(name string) []string {
	return strings.Split(strings.TrimSuffix(string(read(name)), "\r\n"), "\r\n")
}

func genSegmentMutations(w *writer) {
	var pool []string
	for _, name := range []string{"remessa_341.rem", "retorno_341.ret"} {
		for _, l := range sampleLines(name) {
			if l[7] == '3' {
				pool = append(pool, l)
			}
		}
	}
	var rows [][]string
	for i := 0; i < 120; i++ {
		b := []byte(pick(pool))
		for k := 0; k < 1+rng.Intn(2); k++ {
			b[rng.Intn(len(b))] = mutationChars[rng.Intn(len(mutationChars))]
		}
		line := string(b)
		decoded, reencoded := "", ""
		s, err := cnab240.DecodeSegment(line)
		if err != nil {
			decoded = errStr(err)
		} else {
			decoded = "ok|" + segDump(s)
			switch enc, err := cnab240.EncodeSegment(s); {
			case err != nil:
				reencoded = errStr(err)
			case enc == line:
				reencoded = "same"
			default:
				reencoded = "line|" + enc
			}
		}
		rows = append(rows, []string{lit(line), lit(decoded), lit(reencoded)})
	}
	w.table("SegmentMutations", "(string Line, string Decoded, string Reencoded)", rows)
}

// --- file mutations --------------------------------------------------------

// mutate applies one deterministic edit to a sample file. The C# suite
// applies the same edit (ParityTests.ApplyMutation) before parsing.
func mutate(data []byte, op string, line, pos int, text string) []byte {
	lines := strings.Split(strings.TrimSuffix(string(data), "\r\n"), "\r\n")
	eol, final := "\r\n", "\r\n"
	switch op {
	case "set":
		l := lines[line]
		lines[line] = l[:pos-1] + text + l[pos-1+len(text):]
	case "del":
		lines = append(lines[:line:line], lines[line+1:]...)
	case "dup":
		lines = append(lines[:line+1:line+1], lines[line:]...)
	case "swap":
		lines[line], lines[line+1] = lines[line+1], lines[line]
	case "trunc":
		lines[line] = lines[line][:pos]
	case "lf":
		eol, final = "\n", "\n"
	case "noeol":
		final = ""
	case "ctrlz":
		final = "\r\n\x1a"
	case "blank":
		final = "\r\n\r\n"
	}
	return []byte(strings.Join(lines, eol) + final)
}

const patchChars = "0123456789 ABPQRTUXZaz-"

func genParseMutations(w *writer) {
	var rows [][]string
	for _, name := range []string{"remessa_341.rem", "retorno_341.ret", "remessa_237.rem"} {
		data := read(name)
		lines := sampleLines(name)
		count := 80
		if name == "remessa_237.rem" {
			count = 40
		}
		for i := 0; i < count; i++ {
			op, line, pos, text := "set", rng.Intn(len(lines)), 0, ""
			switch r := rng.Intn(21); {
			case r < 12:
				n := 1 + rng.Intn(3)
				pos = 1 + rng.Intn(len(lines[line])-n+1)
				var b strings.Builder
				for k := 0; k < n; k++ {
					b.WriteByte(patchChars[rng.Intn(len(patchChars))])
				}
				text = b.String()
			case r < 14:
				op = "del"
			case r < 15:
				op = "dup"
			case r < 16:
				op = "swap"
				if line == len(lines)-1 {
					line--
				}
			case r < 17:
				op = "trunc"
				pos = rng.Intn(len(lines[line]))
			default:
				op = []string{"lf", "noeol", "ctrlz", "blank"}[r-17]
				line = 0
			}
			in := mutate(data, op, line, pos, text)
			var result string
			if name == "remessa_237.rem" {
				f, err := file.Parse400(bytes.NewReader(in))
				if err != nil {
					result = errStr(err)
				} else {
					result = "ok|" + file400Dump(f) + "\n" + write400(f)
				}
			} else {
				f, err := file.Parse(bytes.NewReader(in))
				if err != nil {
					result = errStr(err)
				} else {
					result = "ok|" + write240(f, file.Writer{})
				}
				if _, err := (file.Options{CheckTotals: true}).Parse(bytes.NewReader(in)); err != nil {
					result += "\ntotals " + errStr(err)
				} else {
					result += "\ntotals ok"
				}
			}
			rows = append(rows, []string{lit(name), lit(op), strconv.Itoa(line), strconv.Itoa(pos), lit(text), lit(result)})
		}
	}
	w.table("ParseMutations", "(string File, string Op, int Line, int Pos, string Text, string Result)", rows)
}

// --- reconciliation --------------------------------------------------------

func entryPipe(e reconcile.Entry) string {
	return strings.Join([]string{
		e.NossoNumero, e.NumeroDocumento, strconv.Itoa(int(e.Movimento)), strings.Join(e.Motivos, ","), dateStr(e.Vencimento),
		strconv.FormatInt(e.Valor, 10), strconv.FormatInt(e.ValorPago, 10), strconv.FormatInt(e.ValorLiquido, 10),
		strconv.FormatInt(e.JurosMulta, 10), strconv.FormatInt(e.Desconto, 10), strconv.FormatInt(e.Abatimento, 10),
		strconv.FormatInt(e.Tarifa, 10), dateStr(e.DataOcorrencia), dateStr(e.DataCredito),
	}, "|")
}

func expectedPipe(e reconcile.Expected) string {
	return strings.Join([]string{e.NossoNumero, strconv.FormatInt(e.Valor, 10), dateStr(e.Vencimento), e.Referencia}, "|")
}

func resultsText(rep *reconcile.Report) string {
	lines := make([]string, len(rep.Results))
	for i, r := range rep.Results {
		lines[i] = fmt.Sprintf("%s|%s|%d|%s", r.Status, r.NossoNumero, r.Difference, r.Explanation)
	}
	return strings.Join(lines, "\n")
}

func dec(dd int) time.Time { return d(2026, 12, dd) }

func liq(nn string, pago int64, on time.Time) reconcile.Entry {
	return reconcile.Entry{NossoNumero: nn, Movimento: cnab240.RetornoLiquidacao, ValorPago: pago, ValorLiquido: pago, DataOcorrencia: on}
}

// unitScenario is the scenario of the Go unit test TestReconcile.
func unitScenario() ([]reconcile.Expected, []reconcile.Entry) {
	var expected []reconcile.Expected
	for _, nn := range []string{"0001", "2", "3", "4", "5", "6", "7", "8"} {
		expected = append(expected, reconcile.Expected{NossoNumero: nn, Valor: 1000, Vencimento: dec(10)})
	}
	adj := liq("2", 900, dec(9))
	adj.Desconto = 100
	late := liq("3", 1050, dec(12))
	late.JurosMulta = 50
	div := liq("4", 1200, dec(10))
	div.JurosMulta = 50
	nominal := liq("7", 1000, dec(10))
	nominal.Valor = 999
	creditOnly := liq("8", 1000, time.Time{})
	creditOnly.DataCredito = dec(10)
	entries := []reconcile.Entry{
		{NossoNumero: "00000001  ", Movimento: cnab240.RetornoEntradaConfirmada},
		liq("1", 1000, dec(10)), adj, late, div, liq("1", 1000, dec(11)), liq("99", 500, dec(10)),
		{NossoNumero: "5", Movimento: cnab240.RetornoEntradaRejeitada, Motivos: []string{"08"}},
		{NossoNumero: "77", Movimento: cnab240.RetornoInstrucaoRejeitada},
		liq("6", 1000, dec(11)), nominal, creditOnly,
		{NossoNumero: "2", Movimento: cnab240.RetornoDebitoTarifas, Tarifa: 350},
	}
	return expected, entries
}

func sampleExpected() []reconcile.Expected {
	return []reconcile.Expected{
		{NossoNumero: "00000001", Valor: 150000, Vencimento: dec(10), Referencia: "NF-1001"},
		{NossoNumero: "00000002", Valor: 89990, Vencimento: dec(15), Referencia: "NF-1002"},
		{NossoNumero: "00000003", Valor: 25000, Vencimento: dec(20), Referencia: "NF-1003"},
		{NossoNumero: "00000004", Valor: 42050, Vencimento: dec(20), Referencia: "NF-1004"},
		{NossoNumero: "00000005", Valor: 10000, Vencimento: dec(30), Referencia: "NF-1005"},
	}
}

func randomScenario() ([]reconcile.Expected, []reconcile.Entry) {
	pad := func(n int) string { return fmt.Sprintf("%0*d", 1+rng.Intn(9), n) }
	var expected []reconcile.Expected
	used := map[int]bool{}
	for i := 0; i < rng.Intn(9); i++ {
		n := 1 + rng.Intn(12)
		if used[n] {
			continue
		}
		used[n] = true
		e := reconcile.Expected{NossoNumero: pad(n), Valor: int64(100 + rng.Intn(100000)), Referencia: fmt.Sprintf("REF-%d", n)}
		if !maybe(0.1) {
			e.Vencimento = dec(1 + rng.Intn(28))
		}
		expected = append(expected, e)
	}
	movs := []cnab240.MovimentoRetorno{2, 3, 6, 6, 6, 6, 9, 17, 26, 28, 30, 44}
	var entries []reconcile.Entry
	for i := 0; i < rng.Intn(13); i++ {
		n := 1 + rng.Intn(15)
		e := reconcile.Entry{NossoNumero: pad(n), NumeroDocumento: fmt.Sprintf("NF-%d", n), Movimento: movs[rng.Intn(len(movs))]}
		if maybe(0.3) {
			e.NossoNumero += "  "
		}
		if maybe(0.8) {
			e.Vencimento = dec(1 + rng.Intn(28))
		}
		if maybe(0.5) {
			e.Valor = int64(100 + rng.Intn(100000))
		}
		if e.Movimento.IsLiquidacao() {
			base := int64(100 + rng.Intn(100000))
			for _, x := range expected {
				if reconcile.Key(x.NossoNumero) == reconcile.Key(e.NossoNumero) && maybe(0.7) {
					base = x.Valor
				}
			}
			switch rng.Intn(5) {
			case 1:
				e.JurosMulta = int64(rng.Intn(500))
			case 2:
				e.Desconto = int64(rng.Intn(500))
			case 3:
				e.Abatimento = int64(rng.Intn(500))
			}
			e.ValorPago = base + e.JurosMulta - e.Desconto - e.Abatimento
			if maybe(0.15) {
				e.ValorPago += int64(1 + rng.Intn(100))
			}
			e.ValorLiquido = e.ValorPago - int64(rng.Intn(400))
			if !maybe(0.1) {
				e.DataOcorrencia = dec(1 + rng.Intn(28))
			}
			if maybe(0.7) {
				e.DataCredito = dec(1 + rng.Intn(28))
			}
		}
		if maybe(0.3) {
			e.Tarifa = int64(rng.Intn(1000))
		}
		if e.Movimento.IsRejeicao() && maybe(0.6) {
			e.Motivos = [][]string{{"08"}, {"08", "46"}, {"A1", "B"}}[rng.Intn(3)]
		}
		entries = append(entries, e)
	}
	return expected, entries
}

func genReconcile(w *writer) {
	type scenario struct {
		name     string
		grace    int
		expected []reconcile.Expected
		entries  []reconcile.Entry
	}
	rf, err := file.Parse(bytes.NewReader(read("retorno_341.ret")))
	if err != nil {
		panic(err)
	}
	ue, un := unitScenario()
	scenarios := []scenario{
		{"sample", 0, sampleExpected(), reconcile.EntriesFromFile(rf)},
		{"sample grace 3", 3, sampleExpected(), reconcile.EntriesFromFile(rf)},
		{"unit", 0, ue, un},
		{"unit grace 2", 2, ue, un},
		{"duplicate expected", 0, []reconcile.Expected{{NossoNumero: "01"}, {NossoNumero: "1 "}}, nil},
		{"empty", 0, nil, nil},
		{"clean", 0, []reconcile.Expected{{NossoNumero: "1", Valor: 10}}, []reconcile.Entry{liq("1", 10, time.Time{})}},
	}
	for i := 0; i < 40; i++ {
		e, n := randomScenario()
		scenarios = append(scenarios, scenario{fmt.Sprintf("random %d", i), rng.Intn(3), e, n})
	}
	var rows [][]string
	for _, s := range scenarios {
		var ep, np []string
		for _, e := range s.expected {
			ep = append(ep, expectedPipe(e))
		}
		for _, e := range s.entries {
			np = append(np, entryPipe(e))
		}
		summary, results := "", ""
		rep, err := reconcile.Options{GraceDays: s.grace}.Reconcile(s.expected, s.entries)
		if err != nil {
			summary = errStr(err)
		} else {
			summary = rep.Summary()
			results = resultsText(rep)
			results += fmt.Sprintf("\nattention=%t expected=%d paid=%d credited=%d tariffs=%d",
				rep.NeedsAttention(), rep.ExpectedTotal, rep.PaidTotal, rep.CreditedTotal, rep.TariffTotal)
		}
		rows = append(rows, []string{lit(s.name), strconv.Itoa(s.grace), litArray(ep), litArray(np), lit(summary), lit(results)})
	}
	w.table("Reconcile", "(string Name, int GraceDays, string[] Expected, string[] Entries, string Summary, string Results)", rows)
}

// --- codec -----------------------------------------------------------------

// Rec mirrors the record of the Go fixedwidth unit tests; the C# suite
// declares the same layout (ParityTests.Rec).
type Rec struct {
	Tipo   string    `cnab:"1,1,const=3"`
	Codigo string    `cnab:"2-4,num"`
	Qtd    int       `cnab:"5,3,num"`
	Nome   string    `cnab:"8,10,alfa"`
	Data   time.Time `cnab:"18,8,date"`
	Data6  time.Time `cnab:"26,6,date"`
	Valor  int64     `cnab:"32,10,money"`
	Livre  string    `cnab:"42,4,raw"`
	Small  uint8     `cnab:"46,2,num"`
}

func genCodec(w *writer) {
	names := []string{"", "Ana", "José", "são joão", "ABCDEFGHIJ", "ABCDEFGHIJK", "€", "a\tb", "Nº 1", "Conceição"}
	livres := []string{"", "ab", "a\nb", "abcd", "abcde", "x y ", "\r"}
	codigos := []string{"", "1", "041", "999", "1234", "4a"}
	var rows [][]string
	for i := 0; i < 80; i++ {
		v := Rec{
			Codigo: pick(codigos),
			Qtd:    rng.Intn(1002) - 1,
			Nome:   pick(names),
			Valor:  rng.Int63n(10000000001) - int64(rng.Intn(2)),
			Livre:  pick(livres),
			Small:  uint8(rng.Intn(121)),
		}
		if maybe(0.8) {
			v.Data = randDate(1900, 2100)
		}
		if maybe(0.8) {
			v.Data6 = randDate(1960, 2075)
		}
		if maybe(0.05) {
			v.Tipo = "X"
		}
		input := strings.Join([]string{v.Tipo, v.Codigo, strconv.Itoa(v.Qtd), v.Nome, dateStr(v.Data), dateStr(v.Data6),
			strconv.FormatInt(v.Valor, 10), v.Livre, strconv.Itoa(int(v.Small))}, "|")
		var result string
		line, err := fixedwidth.Marshal(v, 47)
		if err != nil {
			result = errStr(err)
		} else {
			var got Rec
			if err := fixedwidth.Unmarshal(line, &got); err != nil {
				panic(err)
			}
			result = "ok|" + line + "\n" + dump(&got)
		}
		rows = append(rows, []string{lit(input), lit(result)})
	}
	// Decoding of raw lines, including blanks, bad digits and bad dates.
	bases := []string{
		"3041007SAO JOAO  31122026020126000001234" + "5ab  09",
		"3" + strings.Repeat(" ", 46),
		"3" + strings.Repeat("0", 46),
	}
	for i := 0; i < 60; i++ {
		b := []byte(pick(bases))
		for k := 0; k < 1+rng.Intn(2); k++ {
			b[rng.Intn(len(b))] = mutationChars[rng.Intn(len(mutationChars))]
		}
		if maybe(0.1) {
			b = b[:rng.Intn(len(b))]
		}
		line := string(b)
		var got Rec
		var result string
		if err := fixedwidth.Unmarshal(line, &got); err != nil {
			result = errStr(err)
		} else {
			result = "ok|" + dump(&got)
		}
		rows = append(rows, []string{lit("decode|" + line), lit(result)})
	}
	w.table("Codec", "(string Input, string Result)", rows)
}

func genFold(w *writer) {
	pool := []rune("aAzZ09 ,.-áÁãçÇñºª°ÿÝüÖ€\t\x01\x7f日")
	inputs := []string{"", "ABC 123", "ação", "Avenida São João", "Nº 10, 2ª via", "Ñandú Ölçü", "áàâãäéèêëíìîïóòôõöúùûüçñ", "€", "a\x00", "x\x7f", "🙂"}
	for i := 0; i < 60; i++ {
		n := rng.Intn(9)
		r := make([]rune, n)
		for k := range r {
			r[k] = pool[rng.Intn(len(pool))]
		}
		inputs = append(inputs, string(r))
	}
	var rows [][]string
	for _, in := range inputs {
		out, err := fixedwidth.Fold(in)
		result := "ok|" + out
		if err != nil {
			result = "error|" + err.Error()
		}
		rows = append(rows, []string{lit(in), lit(result)})
	}
	w.table("Fold", "(string Input, string Result)", rows)
}

func genKeys(w *writer) {
	inputs := []string{"", "   ", "0", "0000", "000123  ", "12 34", "A0", " 0 0 ", "00000001  ", "10", "0A", "001 0"}
	var rows [][]string
	for _, in := range inputs {
		rows = append(rows, []string{lit(in), lit(reconcile.Key(in))})
	}
	w.table("Keys", "(string Input, string Key)", rows)
}
