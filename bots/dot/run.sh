#!/usr/bin/env bash
set -euo pipefail
BOT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd -- "$BOT_DIR/../.." && pwd)
python3 "$BOT_DIR/check_config.py" "$BOT_DIR/config.json"
if [[ "${1:-}" == --check ]]; then exit 0; fi
if [[ $# != 0 ]]; then echo 'Usage: bash bots/dot/run.sh [--check]' >&2; exit 2; fi
if ! command -v dotnet >/dev/null; then
  source "$REPO/../tooling/env.sh"
fi
exec dotnet "$REPO/src/Chief.Bridge/bin/Debug/net8.0/Chief.Bridge.dll" --config "$BOT_DIR/config.json"
