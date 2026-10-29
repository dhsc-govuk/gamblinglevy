#!/usr/bin/env bash
# Fails when a Markdown file in DIR links to a relative path that does not exist
# in DIR. Run against the staged public tree, so a link to a file that is never
# published fails the publish instead of becoming a broken link on GitHub.
#
# Web links, mailto: links and same-page #anchors are not checked.
#
# Usage: scripts/open-source/check-links.sh DIR
set -euo pipefail

dir="$(cd "${1:?usage: check-links.sh DIR}" && pwd)"
broken=0

while IFS= read -r -d '' md; do
  base="$(dirname "$md")"
  while IFS= read -r link; do
    case "$link" in
      http://*|https://*|mailto:*|\#*|'') continue ;;
    esac
    path="${link%%#*}"
    path="${path%%\?*}"
    path="${path//%20/ }"
    [ -z "$path" ] && continue
    if [ "${path#/}" != "$path" ]; then resolved="$dir$path"; else resolved="$base/$path"; fi
    if [ ! -e "$resolved" ]; then
      echo "Broken link in ${md#"$dir"/}: $link"
      broken=1
    fi
  done < <(grep -oE '\]\([^)[:space:]]+' "$md" | sed 's/^](//' || true)
done < <(find "$dir" -name '*.md' -not -path '*/node_modules/*' -not -path '*/.git/*' -print0)

if [ "$broken" -ne 0 ]; then
  echo "Fix the links above, or add the files they point to to scripts/open-source/stage.sh."
  exit 1
fi
echo "All relative Markdown links resolve."
