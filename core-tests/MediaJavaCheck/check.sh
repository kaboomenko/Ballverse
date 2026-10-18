#!/usr/bin/env bash
# Checks the Android Java sources of the MediaBridge plugin without Unity/Gradle:
#  1. compile: javac --release 8 (Java 8 library surface ~ Android libcore), Android framework classes from an
#     android-all.jar (API 34), and a stub of com.unity3d.player.UnityPlayer. -Xlint:all with -Werror: every lint
#     warning in our sources fails the check. Two categories are excluded because they are about the toolchain, not
#     the code: 'classfile' (android-all.jar references annotation classes that are not on the classpath) and
#     'options' (--release 8 is reported as obsolete by newer JDKs).
#  2. run YuvConverterCheck (plain JVM): the YUV→RGBA converter against an independent reference
#     (layouts × padding × crops × rotations).
#  3. run AacPcmEncoderCheck (plain JVM): the real AacPcmEncoder against a fake codec — audio is never queued past
#     the video end (VideoWriter / SegmentConcat driving patterns, PCM longer/equal/shorter than the video), plus a
#     static guard that no audio look-ahead constant came back into the callers.
#  4. run VideoWriterReleaseCheck (plain JVM): the real VideoWriter lifecycle against fake codec/muxer/surface/EGL classes
#     (writerfakes/) and a recording MediaBridge stand-in — releaseAsync reports only after the writer thread exited
#     with codec + muxer released and its own unfinished work file deleted; no other file (output path, a newer
#     writer's work file) is ever touched; open() failures return only after the thread stopped.
# Usage: core-tests/MediaJavaCheck/check.sh /path/to/android-all.jar
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
jar="${1:?usage: check.sh /path/to/android-all.jar}"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
javac --release 8 -Xlint:all -Xlint:-classfile -Xlint:-options -Werror -encoding UTF-8 -d "$out" -cp "$jar" \
  "$here"/stubs/com/unity3d/player/UnityPlayer.java \
  "$here"/YuvConverterCheck.java \
  "$repo"/client/Assets/Plugins/Android/BallverseMedia/*.java
echo "MediaJavaCheck: compile OK ($(ls "$repo"/client/Assets/Plugins/Android/BallverseMedia/*.java | wc -l) sources)"
java -cp "$out" com.ballverse.media.YuvConverterCheck
# 3. AacPcmEncoderCheck (plain JVM): the REAL AacPcmEncoder against a fake android.media codec (fakes/, compiled
#    first so they shadow the framework classes); org.json for MediaError still comes from android-all.jar.
aac="$out/aac"
mkdir -p "$aac"
javac --release 8 -Xlint:all -Xlint:-classfile -Xlint:-options -Werror -encoding UTF-8 -d "$aac" -cp "$jar" \
  "$here"/fakes/android/media/*.java \
  "$here"/AacPcmEncoderCheck.java \
  "$repo"/client/Assets/Plugins/Android/BallverseMedia/AacPcmEncoder.java \
  "$repo"/client/Assets/Plugins/Android/BallverseMedia/MediaError.java
java -cp "$aac:$jar" com.ballverse.media.AacPcmEncoderCheck
if grep -n 'AUDIO_LEAD' "$repo"/client/Assets/Plugins/Android/BallverseMedia/*.java; then
  echo "MediaJavaCheck: audio must not be queued ahead of the video (AUDIO_LEAD found)" >&2
  exit 1
fi
# 4. VideoWriterReleaseCheck (plain JVM): the REAL VideoWriter (+ AacPcmEncoder, MediaError, Json it links against)
#    with writerfakes/ shadowing the framework classes it executes; GLES20/EGLExt only compile against android-all.jar
#    (the fake EGL has no display, so the writer takes its lockHardwareCanvas path).
vw="$out/writer"
mkdir -p "$vw"
src="$repo"/client/Assets/Plugins/Android/BallverseMedia
javac --release 8 -Xlint:all -Xlint:-classfile -Xlint:-options -Werror -encoding UTF-8 -d "$vw" -cp "$jar" \
  $(find "$here"/writerfakes -name '*.java' | sort) \
  "$here"/VideoWriterReleaseCheck.java \
  "$src"/VideoWriter.java "$src"/AacPcmEncoder.java "$src"/MediaError.java "$src"/Json.java
java -cp "$vw:$jar" com.ballverse.media.VideoWriterReleaseCheck
