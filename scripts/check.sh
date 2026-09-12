#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# ComfortView has no game-independent behavior tests yet.
python3 scripts/validate-package.py
