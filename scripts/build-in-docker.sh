#!/usr/bin/env bash
# scripts/package.sh in the .NET SDK container, for machines without .NET. Output lands in artifacts/.
set -euo pipefail
cd "$(dirname "$0")/.."
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c 'command -v python3 >/dev/null || (apt-get update -qq && apt-get install -y -qq python3 >/dev/null); scripts/package.sh "$0"' "${1:-0.0.0.0}"
