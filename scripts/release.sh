#!/usr/bin/env bash
# release.sh — bump version, tag, push → GitHub Actions สร้าง release เอง
#
# Usage:
#   ./scripts/release.sh patch    # 1.0.0 → 1.0.1  (bug fix)
#   ./scripts/release.sh minor    # 1.0.0 → 1.1.0  (feature)
#   ./scripts/release.sh major    # 1.0.0 → 2.0.0  (breaking)

set -euo pipefail

BUMP="${1:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CSPROJ="$ROOT/CargoFit/CargoFit.csproj"

# ── Validate ──────────────────────────────────────────────────────────────────
if [[ -z "$BUMP" || ! "$BUMP" =~ ^(patch|minor|major)$ ]]; then
    echo "Usage: $0 [patch|minor|major]"
    echo ""
    echo "  patch  →  bug fixes       (1.0.0 → 1.0.1)"
    echo "  minor  →  new features    (1.0.0 → 1.1.0)"
    echo "  major  →  breaking change (1.0.0 → 2.0.0)"
    exit 1
fi

# ── Read current version from .csproj ─────────────────────────────────────────
CURRENT=$(grep -oE '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>' "$CSPROJ" \
    | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')

if [[ -z "$CURRENT" ]]; then
    echo "❌  ไม่พบ <Version> ใน $CSPROJ"
    exit 1
fi

IFS='.' read -ra V <<< "$CURRENT"
MAJOR="${V[0]}"; MINOR="${V[1]}"; PATCH="${V[2]}"

case "$BUMP" in
    major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
    minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
    patch) PATCH=$((PATCH + 1)) ;;
esac

NEW="$MAJOR.$MINOR.$PATCH"
TAG="v$NEW"

echo "🔖  $CURRENT  →  $NEW  (tag: $TAG)"

# ── Confirm ───────────────────────────────────────────────────────────────────
read -r -p "ดำเนินการต่อ? [y/N] " CONFIRM
if [[ ! "$CONFIRM" =~ ^[Yy]$ ]]; then
    echo "ยกเลิก"
    exit 0
fi

# ── Check working tree is clean ───────────────────────────────────────────────
if ! git diff --quiet || ! git diff --cached --quiet; then
    echo "❌  มี uncommitted changes — commit หรือ stash ก่อน"
    exit 1
fi

# ── Update <Version> in .csproj (portable: perl -i works on macOS + Linux) ────
perl -i -pe "s|<Version>$CURRENT</Version>|<Version>$NEW</Version>|" "$CSPROJ"

# ── Commit + Tag + Push ───────────────────────────────────────────────────────
git add "$CSPROJ"
git commit -m "chore: release $TAG"
git tag "$TAG"
git push origin HEAD
git push origin "$TAG"

echo ""
echo "✅  Tagged $TAG และ push แล้ว"
echo "   GitHub Actions กำลัง build → ดูได้ที่:"
echo "   https://github.com/PingSunn/cargofit/actions"
