#!/usr/bin/env bash
# PostToolUse format hook for Shield.
# Reads Claude Code hook JSON from stdin, extracts the edited file path, and
# runs the appropriate formatter based on extension. Silent on missing tools.
#
# Triggered by .claude/settings.json hooks → see "Edit|Write" matcher.

set +e

# Extract file path from the hook payload (Python because jq isn't on Stoney's PATH).
FILE="$(python -c 'import json,sys
try:
    d = json.load(sys.stdin)
    p = (d.get("tool_input") or {}).get("file_path") or (d.get("tool_response") or {}).get("filePath") or ""
    print(p)
except Exception:
    print("")' 2>/dev/null)"

if [ -z "$FILE" ] || [ ! -f "$FILE" ]; then
    exit 0
fi

NAME="$(basename "$FILE")"
EXT="${FILE##*.}"

case "$EXT" in
    cs)
        if command -v csharpier >/dev/null 2>&1; then
            if csharpier format "$FILE" >/dev/null 2>&1; then
                echo "csharpier: $NAME"
            else
                echo "csharpier-skip: $NAME"
            fi
        fi
        ;;
    ts|tsx|vue|js|jsx|mjs|cjs|json|css|html|md|yml|yaml)
        # Only run prettier if the project actually wants it (any prettier or
        # eslint config marker, or a "prettier" entry in a nearby package.json).
        DIR="$(dirname "$FILE")"
        CONFIGURED=0
        while [ -n "$DIR" ] && [ "$DIR" != "/" ] && [ "$DIR" != "." ]; do
            if ls "$DIR"/.prettierrc* "$DIR"/prettier.config.* "$DIR"/eslint.config.* "$DIR"/.eslintrc* 2>/dev/null | grep -q .; then
                CONFIGURED=1
                break
            fi
            if [ -f "$DIR/package.json" ] && grep -qE '"(prettier|eslint)"\s*:' "$DIR/package.json" 2>/dev/null; then
                CONFIGURED=1
                break
            fi
            DIR="$(dirname "$DIR")"
        done

        if [ "$CONFIGURED" = "1" ]; then
            if (cd "$(dirname "$FILE")" && npx --no-install prettier --write --ignore-unknown "$FILE" >/dev/null 2>&1); then
                echo "prettier: $NAME"
            fi
        fi
        ;;
    *)
        :
        ;;
esac

exit 0
