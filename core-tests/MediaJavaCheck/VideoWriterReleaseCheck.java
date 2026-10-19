package com.ballverse.media;

import android.graphics.Bitmap;
import android.media.MediaCodec;
import android.media.MediaMuxer;
import android.opengl.EGL14;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.concurrent.CountDownLatch;

/**
 * Plain-JVM check of the REAL VideoWriter lifecycle against fake codec / muxer / surface classes (writerfakes/):
 * QA finding (MediaBridge follow-up): disposing a writer finished the native release in the background and deleted the
 * output BY PATH, so a redo at the same path could lose the new file while the old encoder was still held. Now each
 * writer owns one work file (C# picks a unique name) and releaseAsync reports only after the writer thread has exited
 * with the codec and muxer released and its own unfinished file deleted. Run by check.sh.
 *
 * Round 2 (QA): plain OpenWriter on a NEW path one frame after a Dispose (studio redo / §3.8 retry) must not fail on a
 * device with a single hardware encoder: open() waits (bounded) for writers that are stopping. Some cases also run on the
 * EGL renderer path (fake EGL display), so the EGL teardown inside the release is covered, not only lockHardwareCanvas.
 */
public final class VideoWriterReleaseCheck {
    private static final int W = 16;
    private static final int H = 16;
    private static final int FPS = 30;
    private static final long FRAME_US = (1000000L + FPS - 1) / FPS;
    private static int sCases;
    private static int sFailures;
    private static File sDir;
    /** A gate a case took out of MediaCodec but has not opened yet: opened after the case so a failure cannot strand a thread. */
    private static volatile CountDownLatch sHeldGate;

    /** Native state captured synchronously when a request is reported. */
    private static final class Snapshot {
        volatile boolean captured;
        volatile boolean fileExists;
        volatile int liveCodecs;
        volatile int liveMuxers;
        volatile int liveEgl;
        volatile boolean stopped;
    }

    public static void main(String[] args) throws Exception {
        sDir = new File(System.getProperty("java.io.tmpdir"), "writercheck-" + System.nanoTime());
        if (!sDir.mkdirs()) throw new IOException("cannot create " + sDir);
        try {
            run("release waits for a writer thread stuck in the codec", VideoWriterReleaseCheck::releaseWaitsForStuckThread);
            run("late release of an old writer never touches the output or a newer writer", VideoWriterReleaseCheck::oldWriterReleaseKeepsNewFile);
            run("finish then release keeps the file", VideoWriterReleaseCheck::finishThenRelease);
            run("finish without frames deletes only its own file", VideoWriterReleaseCheck::finishWithoutFrames);
            run("cancel during finish: cancelled, file gone before the release report", VideoWriterReleaseCheck::cancelDuringFinish);
            run("open failure before the thread starts", VideoWriterReleaseCheck::openFailureBeforeThread);
            run("open failure on the writer thread returns only after it stopped", VideoWriterReleaseCheck::openFailureOnThread);
            run("repeated releases all complete", VideoWriterReleaseCheck::repeatedRelease);
            run("open on a single-encoder device waits for a stopping writer's encoder", VideoWriterReleaseCheck::openWaitsForStoppingWriter);
            run("open does not wait for writers that are still open", VideoWriterReleaseCheck::openDoesNotWaitForOpenWriter);
            run("EGL path: release waits for a writer thread stuck in the codec", egl(VideoWriterReleaseCheck::releaseWaitsForStuckThread));
            run("EGL path: finish then release keeps the file", egl(VideoWriterReleaseCheck::finishThenRelease));
            run("EGL path: cancel during finish", egl(VideoWriterReleaseCheck::cancelDuringFinish));
            run("EGL path: open waits for a stopping writer's encoder", egl(VideoWriterReleaseCheck::openWaitsForStoppingWriter));
        } finally {
            deleteTree(sDir);
        }
        if (sFailures > 0) {
            System.err.println("VideoWriterReleaseCheck: " + sFailures + " of " + sCases + " cases FAILED");
            System.exit(1);
        }
        System.out.println("VideoWriterReleaseCheck: OK (" + sCases + " cases)");
    }

    private interface Case {
        void run() throws Exception;
    }

    private static void run(String name, Case c) {
        sCases++;
        MediaCodec.resetAll();
        EGL14.reset();
        VideoWriter.sOpenWaitForStoppingMs = 3000;
        MediaBridge.clear();
        MediaBridge.sListener = null;
        try {
            c.run();
            if (MediaCodec.liveCount() != 0) throw new AssertionError("codec leaked: " + MediaCodec.liveCount());
            if (MediaMuxer.liveCount() != 0) throw new AssertionError("muxer leaked: " + MediaMuxer.liveCount());
            if (EGL14.liveCount() != 0) throw new AssertionError("EGL context/surface leaked: " + EGL14.liveCount());
            if (VideoWriter.stoppingCount() != 0) throw new AssertionError("writer still registered as stopping: " + VideoWriter.stoppingCount());
            System.out.println("  ok   " + name);
        } catch (Throwable t) {
            sFailures++;
            System.err.println("  FAIL " + name + ": " + t);
            t.printStackTrace();
        } finally {
            CountDownLatch g = MediaCodec.frameGate;
            if (g != null) g.countDown();
            g = MediaCodec.eosGate;
            if (g != null) g.countDown();
            g = sHeldGate;
            sHeldGate = null;
            if (g != null) g.countDown();
            VideoWriter.waitForStoppingWriters(5000); // writers stranded by a failed case must not leak into the next one
        }
    }

    // ------------------------------------------------------------------ cases

    private static void releaseWaitsForStuckThread() throws Exception {
        File output = touch("seg.mp4", "previous result");
        File newer = touch("seg.mp4.bvNEWER.part.mp4", "newer writer");
        File work = new File(sDir, "seg.mp4.bvOLD.part.mp4");
        VideoWriter w = open(work);
        MediaCodec.frameGate = new CountDownLatch(1);
        append(w, 0);
        awaitBlocked(1);
        Snapshot snap = captureOn(100, w, work);
        w.releaseAsync(100);
        String early = MediaBridge.poll(300);
        check(early == null, "release reported while the writer thread was still inside the codec: " + early);
        check(!w.isStopped(), "writer reported stopped while its thread is blocked");
        check(MediaCodec.liveCount() == 1, "codec released while the thread still uses it");
        MediaCodec.releaseDelayMs = 200; // a slow teardown must still finish before the report
        MediaCodec.frameGate.countDown();
        expect("100 ok", 5000);
        checkStopped(snap, "release");
        check(!work.exists(), "unfinished work file not deleted");
        check(output.exists() && newer.exists(), "release deleted a file it does not own");
    }

    private static void oldWriterReleaseKeepsNewFile() throws Exception {
        // The old writer hangs: the next open() waits for its encoder only this long, then proceeds.
        VideoWriter.sOpenWaitForStoppingMs = 300;
        File output = new File(sDir, "redo.mp4");
        File oldWork = new File(sDir, "redo.mp4.bvA.part.mp4");
        File newWork = new File(sDir, "redo.mp4.bvB.part.mp4");
        VideoWriter a = open(oldWork);
        append(a, 0);
        append(a, FRAME_US);
        // The old writer is slow to stop while the redo writer for the same output already runs.
        MediaCodec.frameGate = new CountDownLatch(1);
        append(a, 2 * FRAME_US);
        awaitBlocked(1);
        Snapshot snapA = captureOn(200, a, oldWork);
        a.releaseAsync(200);
        CountDownLatch gate = MediaCodec.frameGate;
        sHeldGate = gate;
        MediaCodec.frameGate = null;
        long t0 = System.nanoTime();
        VideoWriter b = open(newWork);
        long waitedMs = (System.nanoTime() - t0) / 1000000L;
        check(waitedMs >= 250, "open() did not wait for the stopping writer's encoder (" + waitedMs + " ms)");
        check(!a.isStopped(), "the old writer must still be stuck here");
        for (int i = 0; i < 5; i++) append(b, i * FRAME_US);
        check(MediaBridge.poll(200) == null, "the old writer reported its release while still stuck");
        gate.countDown();
        expect("200 ok", 5000);
        check(snapA.captured && snapA.stopped && !snapA.fileExists, "old writer reported before it stopped and deleted its file");
        check(!oldWork.exists(), "old work file not deleted");
        check(newWork.exists(), "the old writer's release deleted the new writer's file");
        b.finishAsync(201);
        expect("201 ok", 5000);
        b.releaseAsync(202);
        expect("202 ok", 5000);
        check(newWork.exists() && newWork.length() > 8, "finished file missing or empty");
        check(!output.exists(), "the writer must never create the output path itself (C# renames on success)");
    }

    private static void finishThenRelease() throws Exception {
        File work = new File(sDir, "fin.mp4.bv1.part.mp4");
        VideoWriter w = open(work);
        for (int i = 0; i < 3; i++) append(w, i * FRAME_US);
        w.finishAsync(300);
        expect("300 ok", 5000);
        Snapshot snap = captureOn(301, w, work);
        w.releaseAsync(301);
        expect("301 ok", 5000);
        check(snap.captured && snap.stopped && snap.liveCodecs == 0 && snap.liveMuxers == 0 && snap.liveEgl == 0,
                "finished writer not released at report time");
        check(snap.fileExists, "finished file missing at release time");
        check(work.exists(), "release after a successful finish deleted the file");
    }

    private static void finishWithoutFrames() throws Exception {
        File other = touch("empty.mp4", "unrelated");
        File work = new File(sDir, "empty.mp4.bv1.part.mp4");
        VideoWriter w = open(work);
        w.finishAsync(400);
        String r = MediaBridge.poll(5000);
        check(r != null && r.startsWith("400 fail:INVALID_STATE"), "expected INVALID_STATE failure, got " + r);
        Snapshot snap = captureOn(401, w, work);
        w.releaseAsync(401);
        expect("401 ok", 5000);
        checkStopped(snap, "release after failed finish");
        check(!work.exists(), "failed writer kept its file");
        check(other.exists(), "failed writer deleted another file");
    }

    private static void cancelDuringFinish() throws Exception {
        File work = new File(sDir, "cancel.mp4.bv1.part.mp4");
        VideoWriter w = open(work);
        for (int i = 0; i < 3; i++) append(w, i * FRAME_US);
        MediaCodec.eosGate = new CountDownLatch(1);
        w.finishAsync(500);
        awaitBlocked(1);
        w.cancel();
        Snapshot snap = captureOn(501, w, work);
        w.releaseAsync(501);
        check(MediaBridge.poll(200) == null, "a result arrived while the finish was blocked in the codec");
        MediaCodec.eosGate.countDown();
        expect("500 cancelled", 5000);
        expect("501 ok", 5000);
        checkStopped(snap, "release after cancelled finish");
    }

    private static void openFailureBeforeThread() throws Exception {
        File work = new File(sDir, "bad.mp4.bv1.part.mp4");
        MediaCodec.failConfigure = true;
        VideoWriter w = new VideoWriter(work.getPath(), W, H, FPS, 1000000, 2, "", 192000);
        check(!w.open(), "open should fail");
        check(w.lastError() != null && w.lastError().startsWith("ENCODER_FAILED"), "error: " + w.lastError());
        check(w.isStopped(), "no thread expected");
        check(!work.exists(), "file left behind");
        w.releaseAsync(600);
        expect("600 ok", 1000);
    }

    private static void openFailureOnThread() throws Exception {
        File work = new File(sDir, "badthread.mp4.bv1.part.mp4");
        Bitmap.failCreate = true;
        MediaCodec.releaseDelayMs = 300; // the writer thread's teardown is slow: open() must still wait for it
        VideoWriter w = new VideoWriter(work.getPath(), W, H, FPS, 1000000, 2, "", 192000);
        check(!w.open(), "open should fail");
        // open() returns only once the writer thread has released the encoder and deleted its file.
        check(w.isStopped(), "open() returned before the writer thread stopped");
        check(MediaCodec.liveCount() == 0, "encoder still held after a failed open");
        check(!work.exists(), "file left behind after a failed open");
        w.releaseAsync(700);
        expect("700 ok", 1000);
    }

    private static void repeatedRelease() throws Exception {
        File work = new File(sDir, "twice.mp4.bv1.part.mp4");
        VideoWriter w = open(work);
        append(w, 0);
        w.releaseAsync(800);
        w.releaseAsync(801);
        String r1 = MediaBridge.poll(5000);
        String r2 = MediaBridge.poll(5000);
        check(r1 != null && r2 != null, "both releases must complete: " + r1 + ", " + r2);
        check(("800 ok".equals(r1) && "801 ok".equals(r2)) || ("801 ok".equals(r1) && "800 ok".equals(r2)), r1 + ", " + r2);
        w.releaseAsync(802);
        expect("802 ok", 1000);
        check(!work.exists(), "file left behind");
    }

    // Round 2 QA: the studio retries a failed segment on a NEW path with plain OpenWriter one frame after Dispose. On a
    // phone with one hardware AVC encoder the old writer thread may still own it: open() must wait until it is freed.
    private static void openWaitsForStoppingWriter() throws Exception {
        MediaCodec.maxLive = 1;
        File oldWork = new File(sDir, "seg_000_a0.mp4.bv1.part.mp4");
        final File newWork = new File(sDir, "seg_000_a1.mp4.bv2.part.mp4");
        VideoWriter a = open(oldWork);
        append(a, 0);
        MediaCodec.frameGate = new CountDownLatch(1);
        append(a, FRAME_US);
        awaitBlocked(1);
        a.releaseAsync(900);
        check(VideoWriter.stoppingCount() == 1, "released writer not registered as stopping");
        final VideoWriter b = new VideoWriter(newWork.getPath(), W, H, FPS, 1000000, 2, "", 192000);
        final boolean[] opened = new boolean[1];
        final boolean[] oldStoppedAtOpen = new boolean[1];
        final VideoWriter old = a;
        Thread opener = new Thread(new Runnable() {
            @Override
            public void run() {
                opened[0] = b.open();
                oldStoppedAtOpen[0] = old.isStopped();
            }
        }, "opener");
        opener.start();
        opener.join(300);
        check(opener.isAlive(), "open() returned while the old writer still held the only encoder");
        CountDownLatch gate = MediaCodec.frameGate;
        sHeldGate = gate;
        MediaCodec.frameGate = null;
        gate.countDown();
        opener.join(5000);
        check(!opener.isAlive(), "open() never returned after the old writer stopped");
        check(opened[0], "open failed: " + b.lastError());
        check(oldStoppedAtOpen[0], "the new encoder was configured before the old writer thread exited");
        expect("900 ok", 5000);
        check(!oldWork.exists(), "old work file not deleted");
        for (int i = 0; i < 3; i++) append(b, i * FRAME_US);
        b.finishAsync(901);
        expect("901 ok", 5000);
        b.releaseAsync(902);
        expect("902 ok", 5000);
        check(newWork.exists() && newWork.length() > 8, "retry output missing");
    }

    private static void openDoesNotWaitForOpenWriter() throws Exception {
        File first = new File(sDir, "live.mp4.bv1.part.mp4");
        File second = new File(sDir, "live2.mp4.bv2.part.mp4");
        VideoWriter a = open(first);
        append(a, 0);
        long t0 = System.nanoTime();
        VideoWriter b = open(second);
        long ms = (System.nanoTime() - t0) / 1000000L;
        check(ms < 1000, "open() waited " + ms + " ms for a writer that is still open");
        a.releaseAsync(1000);
        b.releaseAsync(1001);
        String r1 = MediaBridge.poll(5000);
        String r2 = MediaBridge.poll(5000);
        check(r1 != null && r2 != null, "both releases must complete: " + r1 + ", " + r2);
        check(!first.exists() && !second.exists(), "unfinished files left behind");
    }

    /** Runs a case on the EGL renderer path (fake display available) and checks that path was really taken. */
    private static Case egl(final Case c) {
        return new Case() {
            @Override
            public void run() throws Exception {
                EGL14.available = true;
                c.run();
                check(EGL14.swaps.get() > 0, "the writer did not use the EGL renderer");
            }
        };
    }

    // ------------------------------------------------------------------ helpers

    private static VideoWriter open(File work) {
        VideoWriter w = new VideoWriter(work.getPath(), W, H, FPS, 1000000, 2, "", 192000);
        if (!w.open()) throw new AssertionError("open failed: " + w.lastError());
        check(work.exists(), "open did not create the work file");
        return w;
    }

    private static void append(VideoWriter w, long ptsUs) {
        int slot = w.acquireSlot(5000);
        if (slot < 0) throw new AssertionError("no slot: " + slot + " " + w.lastError());
        if (!w.submit(slot, ptsUs)) throw new AssertionError("submit failed: " + w.lastError());
    }

    /** Captures the native state at the moment request {@code id} is reported (on the reporting thread). */
    private static Snapshot captureOn(final long id, final VideoWriter w, final File work) {
        final Snapshot snap = new Snapshot();
        MediaBridge.sListener = new MediaBridge.Listener() {
            @Override
            public void onResult(long reported, String kind) {
                if (reported != id) return;
                snap.fileExists = work.exists();
                snap.liveCodecs = MediaCodec.liveCount();
                snap.liveMuxers = MediaMuxer.liveCount();
                snap.liveEgl = EGL14.liveCount();
                snap.stopped = w.isStopped();
                snap.captured = true;
            }
        };
        return snap;
    }

    /** The report came after the writer stopped: thread exited, encoder and muxer released, unfinished file deleted. */
    private static void checkStopped(Snapshot snap, String what) {
        check(snap.captured, what + ": no report captured");
        check(snap.stopped, what + " reported before the writer thread exited");
        check(snap.liveCodecs == 0, what + " reported while the encoder was still held");
        check(snap.liveMuxers == 0, what + " reported while the muxer was still open");
        check(snap.liveEgl == 0, what + " reported before the EGL context/surface were destroyed");
        check(!snap.fileExists, what + " reported before the writer deleted its unfinished file");
    }

    /** Waits until a writer thread is parked at a fake codec gate. */
    private static void awaitBlocked(int count) throws InterruptedException {
        long deadline = System.currentTimeMillis() + 5000;
        while (MediaCodec.blocked.get() < count) {
            if (System.currentTimeMillis() > deadline) throw new AssertionError("writer thread never reached the codec gate");
            Thread.sleep(5);
        }
    }

    private static void expect(String line, long timeoutMs) throws InterruptedException {
        String r = MediaBridge.poll(timeoutMs);
        check(line.equals(r), "expected '" + line + "', got '" + r + "'");
    }

    private static File touch(String name, String content) throws IOException {
        File f = new File(sDir, name);
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(content.getBytes("UTF-8"));
        }
        return f;
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    private static void deleteTree(File f) {
        File[] children = f.listFiles();
        if (children != null) for (File c : children) deleteTree(c);
        if (!f.delete()) System.err.println("could not delete " + f);
    }
}
