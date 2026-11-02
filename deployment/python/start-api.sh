#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/api"
exec python3 -u main.py
