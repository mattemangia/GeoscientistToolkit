#!/usr/bin/env bash
set -euo pipefail

# Synchronise GAIA's product-neutral GeoGenesis engine from the canonical PRISM copy.
# Usage: scripts/sync-geogenesis.sh [--apply|--check] [PRISM_ROOT]

mode="${1:---check}"
prism_root="${2:-../Prism}"
source_root="${prism_root}/Prism.GeoGenesis"
target_root="Analysis/GeoGenesis"
test_source_root="${prism_root}/Prism.Tests"
test_target_root="Tests/VerificationTests/GeoGenesis"

if [[ ! -d "${source_root}" ]]; then
  echo "PRISM GeoGenesis source not found: ${source_root}" >&2
  exit 2
fi

tmp_root="$(mktemp -d)"
legacy_material_root="${tmp_root}/__legacy_materials"
legacy_thermo_root="${tmp_root}/__legacy_thermodynamics"
trap 'rm -rf "${tmp_root}"' EXIT

for folder in Compute Contaminants Materials Multiphase Reactor Thermodynamics; do
  mkdir -p "${tmp_root}/${folder}"
  while IFS= read -r source_file; do
    relative="${source_file#${source_root}/}"
    destination="${tmp_root}/${relative}"
    mkdir -p "$(dirname "${destination}")"
    if rg -q '^namespace ' "${source_file}"; then
      sed 's/Prism\.GeoGenesis/GAIA.GeoGenesis/g' "${source_file}" > "${destination}"
    else
      {
        echo 'namespace GAIA.GeoGenesis.'"${folder}"';'
        sed 's/Prism\.GeoGenesis/GAIA.GeoGenesis/g' "${source_file}"
      } > "${destination}"
    fi
  done < <(find "${source_root}/${folder}" -maxdepth 1 -type f -name '*.cs' | sort)
done

mkdir -p "${legacy_thermo_root}"
for thermo_name in ActivityCoefficientCalculator ReactionGenerator ThermodynamicSolver; do
  sed -e 's/Prism\.GeoGenesis\.Materials/GAIA.Data.Materials/g' \
      -e 's/Prism\.GeoGenesis\.Compute/GAIA.Network/g' \
      -e 's/Prism\.GeoGenesis\.Thermodynamics/GAIA.Business.Thermodynamics/g' \
      -e 's/Prism\.GeoGenesis/GAIA.Util/g' \
      "${source_root}/Thermodynamics/${thermo_name}.cs" > "${legacy_thermo_root}/${thermo_name}.cs"
done

for source_file in "${source_root}/Logger.cs" "${source_root}/GeoGenesisModule.cs"; do
  destination="${tmp_root}/$(basename "${source_file}")"
  sed 's/Prism\.GeoGenesis/GAIA.GeoGenesis/g' "${source_file}" > "${destination}"
done
cp "${source_root}/VERIFICATION.md" "${tmp_root}/VERIFICATION.md"
{
  echo "source_repository=Prism"
  echo "source_revision=$(git -C "${prism_root}" rev-parse HEAD)"
  find "${source_root}" -type f \( -name '*.cs' -o -name 'VERIFICATION.md' \) \
    -not -path '*/obj/*' -not -path '*/bin/*' -print0 | sort -z |
    while IFS= read -r -d '' file; do
      printf '%s  %s\n' "$(sha256sum "${file}" | cut -d' ' -f1)" "${file#${source_root}/}"
    done
} > "${tmp_root}/SOURCE_MANIFEST.sha256"

mkdir -p "${tmp_root}/__tests"
for test_name in GeoGenesisLiteratureTests GeoGenesisRealReactionTests GeoGenesisReactionAndCouplingTests; do
  sed -e 's/Prism\.GeoGenesis/GAIA.GeoGenesis/g' \
      -e 's/namespace Prism\.Tests/namespace GAIA.VerificationTests/' \
      "${test_source_root}/${test_name}.cs" > "${tmp_root}/__tests/${test_name}.cs"
done

mkdir -p "${legacy_material_root}"
for material_name in CompoundLibrary CompoundLibraryExtensions CompoundLibraryMetamorphicExtensions \
  CompoundLibraryMultiphaseExtensions CompoundLibraryGeochemicalExpansionExtensions \
  CompoundLibrarySoilPollutantsExtensions Element; do
  sed -e 's/Prism\.GeoGenesis\.Materials/GAIA.Data.Materials/g' \
      -e 's/Prism\.GeoGenesis/GAIA.Util/g' \
      "${source_root}/Materials/${material_name}.cs" > "${legacy_material_root}/${material_name}.cs"
done

case "${mode}" in
  --apply)
    mkdir -p "${target_root}"
    find "${target_root}" -type f -name '*.cs' -delete
    mkdir -p "${test_target_root}"
    find "${test_target_root}" -type f -name '*.cs' -delete
    cp -R "${tmp_root}/__tests/." "${test_target_root}/"
    rm -rf "${tmp_root}/__tests"
    cp "${legacy_material_root}/CompoundLibrary.cs" Business/CompoundLibrary.cs
    cp "${legacy_material_root}/CompoundLibraryExtensions.cs" Business/CompoundLibraryExtensions.cs
    cp "${legacy_material_root}/CompoundLibraryMetamorphicExtensions.cs" Business/CompoundLibraryMetamorphicExtensions.cs
    cp "${legacy_material_root}/CompoundLibraryMultiphaseExtensions.cs" Business/CompoundLibraryMultiphaseExtensions.cs
    cp "${legacy_material_root}/CompoundLibraryGeochemicalExpansionExtensions.cs" Business/CompoundLibraryGeochemicalExpansionExtensions.cs
    cp "${legacy_material_root}/CompoundLibrarySoilPollutantsExtensions.cs" Business/CompoundLibrarySoilPollutantsExtensions.cs
    cp "${legacy_material_root}/Element.cs" Business/Element.cs
    rm -rf "${legacy_material_root}"
    for thermo_name in ActivityCoefficientCalculator ReactionGenerator ThermodynamicSolver; do
      cp "${legacy_thermo_root}/${thermo_name}.cs" "Analysis/Thermodynamic/${thermo_name}.cs"
    done
    rm -rf "${legacy_thermo_root}"
    cp -R "${tmp_root}/." "${target_root}/"
    # These two historical GAIA types intentionally live in the global namespace. Keep the
    # compatibility implementation numerically identical so older callers receive the same fixes.
    cp "${source_root}/Thermodynamics/WaterProperties.cs" "Analysis/Thermodynamic/WaterProperties.cs"
    ;;
  --check)
    if ! diff -q "${source_root}/Thermodynamics/WaterProperties.cs" "Analysis/Thermodynamic/WaterProperties.cs"; then
      echo "Legacy WaterProperties compatibility copy is out of sync." >&2
      exit 1
    fi
    if ! diff -qr "${tmp_root}/__tests" "${test_target_root}"; then
      echo "GeoGenesis verification tests are out of sync. Run: scripts/sync-geogenesis.sh --apply" >&2
      exit 1
    fi
    rm -rf "${tmp_root}/__tests"
    for material_name in CompoundLibrary CompoundLibraryExtensions CompoundLibraryMetamorphicExtensions \
      CompoundLibraryMultiphaseExtensions CompoundLibraryGeochemicalExpansionExtensions \
      CompoundLibrarySoilPollutantsExtensions Element; do
      if ! diff -q "${legacy_material_root}/${material_name}.cs" "Business/${material_name}.cs"; then
        echo "Legacy GAIA material library is out of sync: ${material_name}.cs" >&2
        exit 1
      fi
    done
    rm -rf "${legacy_material_root}"
    for thermo_name in ActivityCoefficientCalculator ReactionGenerator ThermodynamicSolver; do
      if ! diff -q "${legacy_thermo_root}/${thermo_name}.cs" "Analysis/Thermodynamic/${thermo_name}.cs"; then
        echo "Legacy GAIA thermodynamic API is out of sync: ${thermo_name}.cs" >&2
        exit 1
      fi
    done
    rm -rf "${legacy_thermo_root}"
    if ! diff -qr --exclude='PROVENANCE.md' "${tmp_root}" "${target_root}"; then
      echo "GeoGenesis copy is out of sync. Run: scripts/sync-geogenesis.sh --apply" >&2
      exit 1
    fi
    ;;
  *)
    echo "Unknown mode: ${mode}; expected --apply or --check" >&2
    exit 2
    ;;
esac
