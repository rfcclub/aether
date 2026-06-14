#!/usr/bin/env bash
# generate-skills.sh — Copy LoomKit SKILL.md files to ~/.claude/skills/loomkit-*
#
# Usage: bash adapters/claude-code/generate-skills.sh
# Run from the project root (where skills/ directory lives).

set -euo pipefail

LOOMKIT_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SKILLS_DIR="$LOOMKIT_ROOT/skills"
CLAUDE_SKILLS_DIR="$HOME/.claude/skills"

if [ ! -d "$SKILLS_DIR" ]; then
  echo "ERROR: skills/ directory not found at $SKILLS_DIR"
  echo "Run this script from the project root."
  exit 1
fi

echo "Installing LoomKit skills to $CLAUDE_SKILLS_DIR..."

for phase_dir in "$SKILLS_DIR"/*/; do
  phase_name="$(basename "$phase_dir")"
  skill_name="loomkit-${phase_name}"
  target_dir="$CLAUDE_SKILLS_DIR/$skill_name"

  mkdir -p "$target_dir"
  cp "$phase_dir/SKILL.md" "$target_dir/SKILL.md"
  echo "  ✓ $skill_name → $target_dir/SKILL.md"
done

echo ""
echo "Done. ${#SKILL_DIRS[@]} skills installed."
echo "Claude Code will now load LoomKit workflow skills automatically."

# List installed skills
echo ""
echo "Installed skills:"
ls -1d "$CLAUDE_SKILLS_DIR"/loomkit-* 2>/dev/null || echo "  (none found)"
