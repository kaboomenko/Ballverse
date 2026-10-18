#!/usr/bin/env bash
# Compile-checks the Android Java sources of the MediaBridge plugin without Unity/Gradle:
#   javac --release 8 (Java 8 library surface ~ Android libcore), Android framework classes from an
#   android-all.jar (API 34), and a stub of com.unity3d.player.UnityPlayer.
# -Xlint:all with -Werror: every lint warning in our sources fails the check. Two categories are excluded because
# they are about the toolchain, not the code: 'classfile' (android-all.jar references annotation classes that are not
# on the classpath) and 'options' (--release 8 is reported as obsolete by newer JDKs).
# Usage: core-tests/MediaJavaCheck/check.sh /path/to/android-all.jar
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
jar="${1:?usage: check.sh /path/to/android-all.jar}"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
javac --release 8 -Xlint:all -Xlint:-classfile -Xlint:-options -Werror -encoding UTF-8 -d "$out" -cp "$jar" \
  "$here"/stubs/com/unity3d/player/UnityPlayer.java \
  "$repo"/client/Assets/Plugins/Android/BallverseMedia/*.java
echo "MediaJavaCheck: OK ($(ls "$repo"/client/Assets/Plugins/Android/BallverseMedia/*.java | wc -l) sources)"
