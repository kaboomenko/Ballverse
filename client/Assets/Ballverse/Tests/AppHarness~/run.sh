#!/usr/bin/env bash
# Ballverse.App / Ballverse.Sim checks under plain .NET 8 (no Unity):
#   1. CompileCheck - netstandard2.1 compile of App + Sim with the real sources they wire (warnings are errors);
#   2. Runtime      - the App EditMode tests (NUnit) against the real Core / Services / Studio storage with fake ports.
# Usage: ./run.sh [extra dotnet test args]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$here/CompileCheck/Ballverse.App.CompileCheck.csproj" -c Release
dotnet test "$here/Runtime/Ballverse.App.Tests.csproj" -c Release "$@"
