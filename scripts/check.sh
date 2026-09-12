#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# ComfortView has no game-independent behavior tests yet.
bun run scripts/validate-package.ts
