#!/usr/bin/env bash
# Copies everything this repository publishes to the public GitHub repository
# (dhsc-govuk/gamblinglevy) into TARGET, replacing what was published before.
#
# This list is the whole definition of "what is public". Anything not named here
# never leaves Bitbucket. Every path named here is deleted from TARGET and copied
# fresh, so a file removed here is removed there on the next publish.
#
# Usage: scripts/open-source/stage.sh TARGET
set -euo pipefail

target="${1:?usage: stage.sh TARGET}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

# Folders, each with the build output and local-only files it must not carry.
publish_dir() {
  local dir="$1"; shift
  rm -rf "${target:?}/$dir"
  mkdir -p "$target/$dir"
  rsync -a "$@" "$dir/" "$target/$dir/"
}

publish_dir client \
  --exclude='node_modules/' \
  --exclude='dist/' \
  --exclude='build/' \
  --exclude='.vite/' \
  --exclude='test-results/' \
  --exclude='coverage/'

# appsettings.Development.json carries the dev-tier CIAM client secret.
publish_dir api \
  --exclude='bin/' \
  --exclude='obj/' \
  --exclude='publish/' \
  --exclude='TestResults/' \
  --exclude='.vs/' \
  --exclude='src/Api/appsettings.Development.json'

# Proposals for unreleased work stay private until the team agrees to publish them.
publish_dir design_docs \
  --exclude='Organisation-Groups-Proposal.*' \
  --exclude='docs/OrgGroups_*'

# The publish scripts themselves, which the published pipeline configuration calls.
publish_dir scripts

# Single files at the repository root.
for file in README.md LICENSE docker-compose.yml bitbucket-pipelines.yml; do
  cp "$file" "$target/$file"
done
