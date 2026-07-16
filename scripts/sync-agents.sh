#!/usr/bin/env bash
#
# sync-agents.sh — Distribute canonical agent definitions from .agents/ to
# every supported AI-coding platform directory.
#
# Canonical source of truth:  .agents/*.md
# Targets:
#   .claude/agents/   — Claude Code
#   .cursor/agents/   — Cursor
#
# Usage:
#   bash scripts/sync-agents.sh          # sync all
#   bash scripts/sync-agents.sh --check  # dry-run, show diffs only

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="${REPO_ROOT}/.agents"
DRY_RUN=false

[[ "${1:-}" == "--check" ]] && DRY_RUN=true

if [[ ! -d "$SOURCE" ]]; then
  echo "ERROR: canonical source $SOURCE does not exist." >&2
  exit 1
fi

TARGETS=(
  "${REPO_ROOT}/.claude/agents"
  "${REPO_ROOT}/.cursor/agents"
)

for target in "${TARGETS[@]}"; do
  echo "→ Syncing to ${target#${REPO_ROOT}/}"
  mkdir -p "$target"
  for f in "$SOURCE"/*.md; do
    name="$(basename "$f")"
    dest="${target}/${name}"
    if $DRY_RUN; then
      if [[ -f "$dest" ]] && diff -q "$f" "$dest" >/dev/null 2>&1; then
        echo "   ✓ ${name} (up to date)"
      else
        echo "   ~ ${name} (would update)"
      fi
    else
      cp "$f" "$dest"
      echo "   ✓ ${name}"
    fi
  done
done

echo ""
echo "Done. Canonical source: .agents/"
