#!/bin/bash
# Vesta Migration Script: Gemini to Antigravity

echo "🔥 Starting Vesta Migration..."

# 1. Create Antigravity config dir if missing
mkdir -p /home/thoor/.antigravity

# 2. Port Identity
if [ -f "/home/thoor/.gemini/GEMINI.md" ]; then
    echo "📜 Porting Vesta Identity to ANTIGRAVITY.md..."
    cp "/home/thoor/.gemini/GEMINI.md" "/home/thoor/.antigravity/ANTIGRAVITY.md"
else
    echo "⚠️ Warning: .gemini/GEMINI.md not found."
fi

# 3. Import Plugins
echo "🧩 Importing plugins from Gemini CLI..."
agy plugin import gemini-cli

# 4. Success
echo "✅ Migration files prepared."
echo "👉 Action required: Run 'agy' in your terminal to complete OAuth authentication."
