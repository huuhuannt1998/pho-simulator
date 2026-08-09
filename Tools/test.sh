#!/usr/bin/env bash
# Fast domain-logic test loop -- no Unity process, no Library/ lock.
# Usage: Tools/test.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

export PATH="$HOME/.dotnet:$PATH"
dotnet test Tools/PhoDomain.Tests.csproj "$@"
