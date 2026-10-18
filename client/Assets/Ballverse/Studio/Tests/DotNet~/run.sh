#!/usr/bin/env bash
# Runs the Ballverse.Studio .NET harness (no Unity): the pure-C# parts of the studio runtime (storage, clip library, PCM source,
# audio mixer, export pipeline) against the Media fake backend, plus a compile-only check of the whole Studio assembly against
# Unity API stand-ins. Usage: ./run.sh [extra dotnet test args]   (needs the .NET 8 SDK)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$here/CompileCheck/Ballverse.Studio.CompileCheck.csproj" -c Release
dotnet test "$here/Ballverse.Studio.DotNetTests.csproj" -c Release "$@"
