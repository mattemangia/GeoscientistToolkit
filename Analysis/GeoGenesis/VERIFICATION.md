# Prism.GeoGenesis — Verification against peer-reviewed literature

The thermodynamic / reactive-transport engine was ported from the Geoscientist's Toolkit and
then validated against published reference values. Validation exposed **four substantive bugs**
in the original code, all fixed here. The xUnit cases live in
`Prism.Tests/GeoGenesisLiteratureTests.cs` and `Prism.Tests/GeoGenesisMosaicTransportTests.cs`.

## Reference data (unchanged, confirmed correct)

| Quantity | Engine | Literature | Source |
|---|---|---|---|
| Calcite log Ksp (25 °C) | −8.48 | −8.48 | Plummer & Busenberg (1982) |
| Calcite ΔGf° | −1128.8 kJ/mol | −1128.8 | Robie & Hemingway (1995) |
| Ca²⁺ ΔGf° | −553.5 | −553.6 | Shock & Helgeson (1988) |
| HCO₃⁻ / CO₃²⁻ ΔGf° | −586.8 / −527.8 | −586.8 / −527.8 | Shock & Helgeson (1988) |

## Bugs found and fixed

1. **Dielectric constant of water had the wrong sign/magnitude.** The ported Bradley–Pitzer
   transcription returned εr ≈ **−1081** at 25 °C. A negative permittivity made the Debye–Hückel
   *B* parameter (∝ √εr) `NaN`, propagating `NaN` into every aqueous activity coefficient.
   → Replaced with the Malmberg & Maryott (1956) correlation (εr(25 °C)=78.3).

2. **Debye–Hückel A and B were dimensionally wrong.** Even with a correct εr, *A* evaluated to
   ≈ 0.009 (should be 0.509) and *B* was ≈ 30× too small, so activity coefficients collapsed to
   ≈ 1 at all ionic strengths. → Replaced with the standard closed forms
   `A = 1.8246e6·√ρ/(εrT)^{3/2}`, `B = 50.29·√ρ/(εrT)^{1/2}` (Helgeson & Kirkham 1974; Langmuir 1997),
   reproducing A = 0.509 and B = 0.328 Å⁻¹ at 25 °C.

3. **Species lookups used a notation the database never stored.** The reaction generator looked
   up `H^+`, `OH^-`, `CO3^2^-`, `Na^+`, … (caret notation) while the library stores `H+`, `OH-`,
   `CO32-`, `Na+`. All such lookups returned `null`, so the stoichiometric balancer had no
   H⁺/oxyanion to work with. → `CompoundLibrary.Find` now matches on a notation-agnostic
   canonical key (carets, Unicode sub/superscripts and case are normalised).

4. **Charge digits were parsed as element subscripts.** `ParseChemicalFormula("CO32-")` read
   O₃₂ (and `SO42-` → O₄₂), so every carbonate/sulfate dissolution reaction came out grossly
   unbalanced with absurd coefficients. → A charge-suffix stripper now removes the trailing
   charge, disambiguating monatomic ions (`Ca2+` → charge) from polyatomic subscripts
   (`NO3-`, `HCO3-` → subscript).

Two further robustness improvements in dissolution-reaction generation: the redundant OH⁻ was
removed from the balancing set (OH⁻ = H₂O − H⁺ made the element/charge matrix rank-deficient and
blew the SVD up), and product selection now preserves oxidation state (oxic minerals dissolve to
the oxyanion, e.g. gypsum → SO₄²⁻ rather than reduced H₂S).

## Post-fix results (all pass)

| Check | Result | Reference |
|---|---|---|
| γ(Na⁺), I=0.01 / 0.05 | 0.903 / 0.821 | Langmuir (1997) ≈0.90 / 0.82 |
| γ(Ca²⁺), I=0.01 / 0.05 | 0.668 / 0.463 | Langmuir (1997) |
| Debye–Hückel limiting law (Ca²⁺, I=1e-4) | log γ = −0.0201 | −A z²√I = −0.0204 |
| Calcite → Ca²⁺ + CO₃²⁻ | ΔG=47.5 kJ/mol, logK=−8.32 | consistent with Ksp=−8.48 |
| Calcite van't Hoff, 25→90 °C | logK −8.32 → −8.84 (retrograde) | Plummer & Busenberg (1982) |
| Gypsum → Ca²⁺ + SO₄²⁻ + 2H₂O | logK=−4.36 | Ksp=−4.58 |
| Halite → Na⁺ + Cl⁻ | logK=1.58 | Ksp=1.58 |
| Mosaic-seeded reactive transport | plume migrates down-gradient, mass-conservative, non-negative | advection–dispersion |

## References

- Plummer, L.N. & Busenberg, E. (1982). *Geochim. Cosmochim. Acta* 46, 1011–1040.
- Robie, R.A. & Hemingway, B.S. (1995). USGS Bulletin 2131.
- Shock, E.L. & Helgeson, H.C. (1988). *Geochim. Cosmochim. Acta* 52, 2009–2036.
- Helgeson, H.C. & Kirkham, D.H. (1974). *Am. J. Sci.* 274, 1199–1261.
- Malmberg, C.G. & Maryott, A.A. (1956). *J. Res. Natl. Bur. Stand.* 56, 1–8.
- Langmuir, D. (1997). *Aqueous Environmental Geochemistry*. Prentice Hall.
- Stumm, W. & Morgan, J.J. (1996). *Aquatic Chemistry*, 3rd ed. Wiley.
- Parkhurst, D.L. & Appelo, C.A.J. (2013). PHREEQC v3, USGS TM 6-A43.
