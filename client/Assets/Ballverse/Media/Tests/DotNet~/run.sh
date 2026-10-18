#!/usr/bin/env bash
# Runs the Ballverse.Media EditMode tests under plain .NET 8 (no Unity). Usage: ./run.sh [extra dotnet test args]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dotnet test "$here/Ballverse.Media.DotNetTests.csproj" -c Release "$@"
