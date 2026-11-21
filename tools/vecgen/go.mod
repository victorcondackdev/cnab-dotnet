module github.com/victorcondackdev/cnab-dotnet/tools/vecgen

go 1.22

// The generator runs against a local checkout of the Go implementation,
// cloned next to this repository (../cnab-go relative to the repository root).
replace github.com/victorcondackdev/cnab-go => ../../../cnab-go

require github.com/victorcondackdev/cnab-go v0.0.0-00010101000000-000000000000
