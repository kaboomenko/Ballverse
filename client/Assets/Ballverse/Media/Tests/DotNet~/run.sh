#!/usr/bin/env bash
# Runs the Ballverse.Media EditMode tests under plain .NET 8 (no Unity). Usage: ./run.sh [extra dotnet test args]
# First a compile-only check that the Unity stand-ins shared with other harnesses (UnityStubs.cs, EditMode/TestSupport.cs)
# build without any test framework (see StubsCompileCheck/).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$here/StubsCompileCheck/Ballverse.Media.StubsCompileCheck.csproj" -c Release
dotnet test "$here/Ballverse.Media.DotNetTests.csproj" -c Release "$@"
