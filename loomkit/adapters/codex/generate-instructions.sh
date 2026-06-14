#!/bin/bash
# Generate Codex-compatible instructions from LoomKit SKILL.md files
# Usage: ./generate-instructions.sh

SKILLS_DIR="../../skills"
OUTPUT_DIR="./generated"

mkdir -p "$OUTPUT_DIR"

for skill_dir in "$SKILLS_DIR"/*/; do
  skill_name=$(basename "$skill_dir")
  skill_file="$skill_dir/SKILL.md"

  if [ -f "$skill_file" ]; then
    output_file="$OUTPUT_DIR/loomkit-${skill_name}.md"

    # Extract description
    description=$(head -5 "$skill_file" | grep "^description:" | sed 's/description: //')

    # Format for Codex
    echo "# LoomKit — ${skill_name^}" > "$output_file"
    echo "" >> "$output_file"
    echo "**Trigger:** $description" >> "$output_file"
    echo "" >> "$output_file"
    echo "---" >> "$output_file"
    echo "" >> "$output_file"

    # Extract instructions (skip frontmatter and overview)
    in_body=false
    while IFS= read -r line; do
      if [[ "$line" == "## Instructions" ]]; then
        in_body=true
      fi
      if $in_body; then
        echo "$line" >> "$output_file"
      fi
    done < "$skill_file"

    echo "Generated $output_file"
  fi
done

echo "Done. Instructions in $OUTPUT_DIR/"
