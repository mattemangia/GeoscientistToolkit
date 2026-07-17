# GeoGenesis scientific core provenance

The C# sources below this directory are a namespace-adjusted mirror of
`RiderProjects/Prism/Prism.GeoGenesis`. PRISM is the canonical source; GAIA consumes the mirror so
the same thermodynamic, reactive-transport, compound, element, ion, and reactor implementation is
available in both products.

Synchronise with `scripts/sync-geogenesis.sh --apply` and verify parity with
`scripts/sync-geogenesis.sh --check`. The transformation is deliberately limited to replacing the
root namespace `Prism.GeoGenesis` with `GAIA.GeoGenesis`.

Scientific references and validation status are maintained in PRISM's `Prism.GeoGenesis/VERIFICATION.md`.
PHREEQC reference data may only be incorporated from a documented redistributable source; the
engine does not silently import a system PHREEQC installation.
