package com.ballverse.media;

import android.media.MediaCodec;
import android.media.MediaFormat;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;

/**
 * Plain-JVM check of the REAL AacPcmEncoder against a fake codec (fakes/android/media): audio is never queued past
 * the video end, whatever the PCM length (QA round 2: a 0.5 s look-ahead used to leave AAC past the last frame).
 * Drives the encoder exactly like VideoWriter (pump(pts + 1/fps) after each frame, finish(lastPts + 1/fps)) and like
 * SegmentConcat (pump(outPts) before each sample, finish(frames·10⁶/fps)). Run by check.sh.
 */
public final class AacPcmEncoderCheck {
    private static final long ONE_SAMPLE_US = 21; // 1 / 48 kHz rounded up
    private static int sFailures;
    private static int sCases;

    public static void main(String[] args) throws Exception {
        File dir = new File(System.getProperty("java.io.tmpdir"), "aaccheck-" + System.nanoTime());
        if (!dir.mkdirs()) throw new IOException("cannot create " + dir);
        try {
            int[] fpsList = {30, 60};
            double[] pcmSeconds = {12.0, 10.5, 10.0, 10.0 + 1.0 / 96.0, 9.99, 3.0, 0.01};
            for (int fps : fpsList) {
                for (double seconds : pcmSeconds) {
                    run(dir, fps, 10 * fps, seconds, false);
                    run(dir, fps, 10 * fps, seconds, true);
                }
                run(dir, fps, 1, 2.0, false);
                run(dir, fps, 1, 2.0, true);
            }
        } finally {
            File[] files = dir.listFiles();
            if (files != null) for (File f : files) if (!f.delete()) System.err.println("could not delete " + f);
            if (!dir.delete()) System.err.println("could not delete " + dir);
        }
        if (sFailures > 0) {
            System.err.println("AacPcmEncoderCheck: " + sFailures + " of " + sCases + " cases FAILED");
            System.exit(1);
        }
        System.out.println("AacPcmEncoderCheck: OK (" + sCases + " cases)");
    }

    private static void run(File dir, int fps, int frames, double pcmSeconds, boolean concatStyle) throws Exception {
        sCases++;
        long pcmFrames = Math.round(pcmSeconds * AacPcmEncoder.SAMPLE_RATE);
        File pcm = new File(dir, "mix_" + sCases + ".f32");
        try (FileOutputStream out = new FileOutputStream(pcm)) {
            out.write(new byte[(int) (pcmFrames * 4 * AacPcmEncoder.CHANNELS)]);
        }
        final long[] lastOutPts = {-1};
        final int[] formats = {0};
        AacPcmEncoder encoder = new AacPcmEncoder(pcm.getPath(), 192000, new AacPcmEncoder.Sink() {
            @Override
            public void onFormat(MediaFormat format) {
                formats[0]++;
            }

            @Override
            public void onSample(ByteBuffer data, MediaCodec.BufferInfo info) {
                lastOutPts[0] = Math.max(lastOutPts[0], info.presentationTimeUs);
            }

            @Override
            public boolean cancelled() {
                return false;
            }
        });
        String name = (concatStyle ? "concat" : "writer") + " fps=" + fps + " frames=" + frames + " pcm=" + pcmSeconds + "s";
        String error = null;
        try {
            encoder.open();
            MediaCodec codec = MediaCodec.last;
            long frameUs = VideoWriterTiming.frameDurationUs(fps);
            long lastPts = 0;
            for (int i = 0; i < frames && error == null; i++) {
                long pts = (i * 1000000L + fps - 1) / fps; // export-plan frame times
                long until = concatStyle ? pts : pts + frameUs;
                encoder.pump(until, false);
                lastPts = pts;
                if (codec.lastInputEndUs > until + ONE_SAMPLE_US) {
                    error = "frame " + i + ": audio queued to " + codec.lastInputEndUs + " us, ahead of the video at " + until + " us";
                }
            }
            long endUs = concatStyle ? (frames * 1000000L + fps - 1) / fps : lastPts + frameUs;
            if (error == null) {
                encoder.finish(endUs);
                long pcmEndUs = AacPcmEncoder.ptsUs(pcmFrames);
                long expectedEnd = Math.min(pcmEndUs, endUs);
                if (!codec.eosQueued) error = "no end of stream";
                else if (codec.lastInputEndUs > endUs + ONE_SAMPLE_US) {
                    error = "audio ends at " + codec.lastInputEndUs + " us, past the video end " + endUs + " us";
                } else if (codec.lastInputEndUs < expectedEnd - ONE_SAMPLE_US) {
                    error = "audio ends at " + codec.lastInputEndUs + " us, expected " + expectedEnd + " us (lost samples)";
                } else if (codec.framesIn != Math.min(pcmFrames, AacPcmEncoder.framesUntil(endUs))) {
                    error = "queued " + codec.framesIn + " frames, expected " + Math.min(pcmFrames, AacPcmEncoder.framesUntil(endUs));
                } else if (formats[0] != 1) {
                    error = "output format reported " + formats[0] + " times";
                } else if (lastOutPts[0] < 0 || lastOutPts[0] >= endUs) {
                    error = "last encoded sample pts " + lastOutPts[0] + " not inside [0, " + endUs + ")";
                }
            }
        } finally {
            encoder.release();
        }
        if (error != null) {
            sFailures++;
            System.err.println("FAIL " + name + ": " + error);
        }
    }

    /** Mirrors VideoWriter.frameDurationUs (VideoWriter itself needs EGL/GLES, which this JVM check does not fake). */
    private static final class VideoWriterTiming {
        static long frameDurationUs(int fps) {
            return (1000000L + fps - 1) / fps;
        }
    }
}
