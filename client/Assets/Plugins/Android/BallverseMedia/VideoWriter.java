package com.ballverse.media;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLExt;
import android.opengl.EGLSurface;
import android.opengl.GLES20;
import android.os.Build;
import android.util.Log;
import android.view.Surface;

import java.io.File;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * H.264 + AAC MP4 writer (spec §3.8): MediaCodec H.264 High (Baseline fallback) with an INPUT SURFACE, MediaCodec
 * AAC-LC 48 kHz stereo from a raw f32le PCM file, MediaMuxer MP4 (C# moves moov to the front afterwards).
 *
 * Frames: C# copies an RGBA32 frame into one of 3 direct "slot" buffers (acquireSlot / slot / submit). The encoder
 * thread uploads the slot into a GL texture and draws it into the encoder's input surface through EGL with the exact
 * presentation time (eglPresentationTimeANDROID); the platform converts RGBA to YUV (robust across vendors, no
 * colour-format guessing). If EGL cannot be set up, it falls back to Surface.lockHardwareCanvas() + Bitmap, in which
 * case encoder timestamps are replaced by the submitted ones in output order (B-frames are disabled for that reason).
 * A slot is free again as soon as its pixels are uploaded, so at most 3 frames are in flight.
 *
 * Audio ({@link AacPcmEncoder}) is encoded on the same thread, kept ~0.5 s ahead of the video so MediaMuxer can
 * interleave; it is trimmed to the video end (last pts + 1 / fps). A segment of the segmented export has no audio
 * (empty audioPcmPath); {@link SegmentConcat} encodes the mix once when joining the segments.
 *
 * Threading: the public methods are called from the Unity main thread; everything else runs on the writer thread.
 */
public final class VideoWriter {
    private static final String TAG = MediaBridge.TAG;
    private static final String MIME_AVC = MediaFormat.MIMETYPE_VIDEO_AVC;
    private static final int SLOT_COUNT = 3;
    private static final long AUDIO_LEAD_US = 500000;
    private static final long DRAIN_TIMEOUT_US = 10000;
    private static final long STALL_LIMIT_NS = 10000000000L;
    private static final int EGL_RECORDABLE_ANDROID = 0x3142;

    private static final int JOB_FRAME = 1;
    private static final int JOB_FINISH = 2;
    private static final int JOB_CANCEL = 3;

    private static final class Job {
        final int type;
        final int slot;
        final long value;

        Job(int type, int slot, long value) {
            this.type = type;
            this.slot = slot;
            this.value = value;
        }
    }

    private static final class PendingSample {
        final int track;
        final ByteBuffer data;
        final MediaCodec.BufferInfo info;

        PendingSample(int track, ByteBuffer data, MediaCodec.BufferInfo info) {
            this.track = track;
            this.data = data;
            this.info = info;
        }
    }

    // Configuration
    private final String mPath;
    private final int mWidth;
    private final int mHeight;
    private final int mFps;
    private final int mBitrate;
    private final int mKeyframeSeconds;
    private final String mAudioPath;
    private final int mAudioBitrate;

    // Shared with the C# thread
    private final ByteBuffer[] mSlots = new ByteBuffer[SLOT_COUNT];
    private final ArrayBlockingQueue<Integer> mFreeSlots = new ArrayBlockingQueue<>(SLOT_COUNT);
    private final LinkedBlockingQueue<Job> mJobs = new LinkedBlockingQueue<>();
    private volatile String mError;
    private volatile boolean mFailed;
    private volatile boolean mCancelled;
    private volatile boolean mDone;
    private Thread mThread;

    // Writer thread state
    private MediaCodec mVideo;
    private AacPcmEncoder mAudio;
    private MediaMuxer mMuxer;
    private Surface mInputSurface;
    private EglRenderer mEgl;
    private CanvasRenderer mCanvas;
    private String mProfile = "baseline";
    private int mVideoTrack = -1;
    private int mAudioTrack = -1;
    private boolean mMuxerStarted;
    private final List<PendingSample> mPending = new ArrayList<>();
    private final ArrayDeque<Long> mPtsFifo = new ArrayDeque<>();
    private final MediaCodec.BufferInfo mVideoInfo = new MediaCodec.BufferInfo();
    private boolean mHasAudio;
    private long mLastPts = -1;
    private int mFramesWritten;
    private int mFramesSubmitted;

    public VideoWriter(String path, int width, int height, int fps, int bitrate, int keyframeSeconds, String audioPcmPath, int audioBitrate) {
        mPath = path;
        mWidth = width;
        mHeight = height;
        mFps = fps;
        mBitrate = bitrate;
        mKeyframeSeconds = keyframeSeconds;
        mAudioPath = audioPcmPath == null || audioPcmPath.length() == 0 ? null : audioPcmPath;
        mAudioBitrate = audioBitrate;
    }

    // ================================================================== public API (Unity main thread)

    public String lastError() {
        return mError;
    }

    /** "high" or "baseline" (after open). */
    public String profile() {
        return mProfile;
    }

    /** Configures codecs and muxer and starts the writer thread. On failure see lastError(). */
    public boolean open() {
        try {
            File out = new File(mPath);
            File dir = out.getParentFile();
            if (dir != null && !dir.isDirectory() && !dir.mkdirs()) throw new MediaError(MediaError.IO, "cannot create " + dir);
            if (out.exists() && !out.delete()) throw new MediaError(MediaError.IO, "cannot replace " + out);
            mHasAudio = AacPcmEncoder.frameCount(mAudioPath) > 0;
            configureVideo();
            mMuxer = new MediaMuxer(mPath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            for (int i = 0; i < SLOT_COUNT; i++) {
                mSlots[i] = ByteBuffer.allocateDirect(mWidth * mHeight * 4).order(ByteOrder.LITTLE_ENDIAN);
                mFreeSlots.add(i);
            }
            mVideo.start();
            if (mHasAudio) {
                mAudio = new AacPcmEncoder(mAudioPath, mAudioBitrate, new AudioSink());
                mAudio.open();
            }

            final CountDownLatch ready = new CountDownLatch(1);
            mThread = new Thread(new Runnable() {
                @Override
                public void run() {
                    threadMain(ready);
                }
            }, "BallverseVideoWriter");
            mThread.start();
            if (!ready.await(10, TimeUnit.SECONDS)) throw new MediaError(MediaError.TIMEOUT, "writer thread did not start");
            if (mFailed) throw errorFromString(mError);
            return true;
        } catch (MediaError e) {
            mError = e.toNative();
        } catch (Throwable t) {
            mError = MediaError.wrap(MediaError.ENCODER_FAILED, t).toNative();
        }
        Log.e(TAG, "VideoWriter.open failed: " + mError);
        mFailed = true;
        if (mThread == null) {
            releaseCodecs();
            deleteOutput();
        } else {
            mJobs.offer(new Job(JOB_CANCEL, -1, 0));
        }
        return false;
    }

    public int slotCount() {
        return SLOT_COUNT;
    }

    public ByteBuffer slot(int index) {
        return mSlots[index];
    }

    /** A free slot index, -1 if none freed within timeoutMs (0 = do not wait), -2 if the writer failed. */
    public int acquireSlot(int timeoutMs) {
        if (mFailed || mDone) return -2;
        Integer slot;
        try {
            slot = timeoutMs > 0 ? mFreeSlots.poll(timeoutMs, TimeUnit.MILLISECONDS) : mFreeSlots.poll();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return -1;
        }
        if (mFailed) {
            if (slot != null) mFreeSlots.offer(slot);
            return -2;
        }
        return slot == null ? -1 : slot;
    }

    /** Queues the filled slot for encoding at ptsUs. False if the writer failed (see lastError()). */
    public boolean submit(int slot, long ptsUs) {
        if (mFailed || mDone) {
            mFreeSlots.offer(slot);
            if (mError == null) mError = MediaError.INVALID_STATE + "|writer is closed";
            return false;
        }
        mJobs.offer(new Job(JOB_FRAME, slot, ptsUs));
        return true;
    }

    public int framesInFlight() {
        return SLOT_COUNT - mFreeSlots.size();
    }

    /** Flushes and closes the file; result via MediaBridge callback: {"frames","durationUs"}. */
    public void finishAsync(long requestId) {
        if (mThread == null) {
            MediaBridge.fail(requestId, new MediaError(MediaError.INVALID_STATE, "writer not open"));
            return;
        }
        mJobs.offer(new Job(JOB_FINISH, -1, requestId));
    }

    /** Aborts encoding and deletes the partial file (asynchronously on the writer thread). */
    public void cancel() {
        mCancelled = true;
        if (mThread != null) mJobs.offer(new Job(JOB_CANCEL, -1, 0));
    }

    /** Releases everything; aborts (and deletes the file) if not finished. Safe to call more than once. */
    public void release() {
        if (!mDone) cancel();
    }

    // ================================================================== writer thread

    private void threadMain(CountDownLatch ready) {
        try {
            try {
                mEgl = new EglRenderer();
                mEgl.init(mInputSurface, mWidth, mHeight);
            } catch (Throwable t) {
                Log.w(TAG, "EGL encoder path unavailable, using lockHardwareCanvas: " + t);
                if (mEgl != null) mEgl.release();
                mEgl = null;
                mCanvas = new CanvasRenderer(mInputSurface, mWidth, mHeight);
            }
        } catch (Throwable t) {
            fail(MediaError.wrap(MediaError.ENCODER_FAILED, t));
        } finally {
            ready.countDown();
        }

        while (true) {
            Job job;
            try {
                job = mJobs.take();
            } catch (InterruptedException e) {
                job = new Job(JOB_CANCEL, -1, 0);
            }
            if (job.type == JOB_CANCEL) {
                if (!mDone) abort();
                return;
            }
            if (job.type == JOB_FRAME) {
                if (mFailed || mCancelled) {
                    mFreeSlots.offer(job.slot);
                    continue;
                }
                try {
                    encodeFrame(job.slot, job.value);
                } catch (Throwable t) {
                    mFreeSlots.offer(job.slot);
                    fail(MediaError.wrap(MediaError.ENCODER_FAILED, t));
                }
                continue;
            }
            // JOB_FINISH
            long requestId = job.value;
            if (mFailed) {
                MediaBridge.fail(requestId, errorFromString(mError));
                abort();
                return;
            }
            try {
                String json = finish();
                mDone = true;
                MediaBridge.succeed(requestId, json);
            } catch (Throwable t) {
                MediaError e = MediaError.wrap(MediaError.MUXER_FAILED, t);
                fail(e);
                abort();
                if (mCancelled) MediaBridge.cancelled(requestId);
                else MediaBridge.fail(requestId, e);
            }
            return;
        }
    }

    private void encodeFrame(int slot, long ptsUs) throws MediaError, IOException {
        drainVideo(false);
        ByteBuffer pixels = mSlots[slot];
        if (mEgl != null) {
            mEgl.upload(pixels);
            mFreeSlots.offer(slot);
            mEgl.drawAndSwap(ptsUs);
        } else {
            mCanvas.upload(pixels);
            mFreeSlots.offer(slot);
            mPtsFifo.add(ptsUs);
            mCanvas.draw();
        }
        mLastPts = ptsUs;
        mFramesSubmitted++;
        drainVideo(false);
        if (mAudio != null) mAudio.pump(ptsUs + AUDIO_LEAD_US, false);
    }

    private String finish() throws MediaError, IOException {
        if (mFramesSubmitted == 0) throw new MediaError(MediaError.INVALID_STATE, "no frames");
        mVideo.signalEndOfInputStream();
        drainVideo(true);
        long endUs = mLastPts + (1000000L + mFps - 1) / mFps;
        if (mAudio != null) mAudio.finish(endUs);
        if (!mMuxerStarted) throw new MediaError(MediaError.MUXER_FAILED, "muxer never started (no encoder output)");
        mMuxer.stop();
        releaseCodecs();
        if (mFramesWritten != mFramesSubmitted) {
            Log.w(TAG, "frames written " + mFramesWritten + " != submitted " + mFramesSubmitted);
        }
        return new Json().put("frames", mFramesWritten).put("durationUs", endUs).put("profile", mProfile).toString();
    }

    private void fail(MediaError e) {
        if (!mFailed) {
            mError = e.toNative();
            mFailed = true;
            Log.e(TAG, "VideoWriter failed: " + mError);
        }
    }

    private static MediaError errorFromString(String codeAndMessage) {
        if (codeAndMessage == null) return new MediaError(MediaError.ENCODER_FAILED, "unknown");
        int bar = codeAndMessage.indexOf('|');
        if (bar < 0) return new MediaError(MediaError.ENCODER_FAILED, codeAndMessage);
        return new MediaError(codeAndMessage.substring(0, bar), codeAndMessage.substring(bar + 1));
    }

    private void abort() {
        mDone = true;
        releaseCodecs();
        deleteOutput();
    }

    private void deleteOutput() {
        File f = new File(mPath);
        if (f.exists() && !f.delete()) Log.w(TAG, "cannot delete " + mPath);
    }

    // ------------------------------------------------------------------ video

    private MediaFormat videoFormat(boolean high, int highLevel, boolean vbr) {
        MediaFormat f = MediaFormat.createVideoFormat(MIME_AVC, mWidth, mHeight);
        f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
        f.setInteger(MediaFormat.KEY_BIT_RATE, mBitrate);
        f.setInteger(MediaFormat.KEY_FRAME_RATE, mFps);
        f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, mKeyframeSeconds);
        f.setInteger("max-bframes", 0); // MediaFormat.KEY_MAX_B_FRAMES (API 29): output order == input order
        if (vbr) f.setInteger(MediaFormat.KEY_BITRATE_MODE, MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_VBR);
        if (Build.VERSION.SDK_INT >= 24) {
            f.setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT709);
            f.setInteger(MediaFormat.KEY_COLOR_RANGE, MediaFormat.COLOR_RANGE_LIMITED);
            f.setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        }
        if (high) {
            f.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileHigh);
            if (highLevel > 0) f.setInteger(MediaFormat.KEY_LEVEL, highLevel);
        }
        return f;
    }

    private void configureVideo() throws MediaError, IOException {
        MediaFormat probe = videoFormat(false, 0, false);
        String name = null;
        try {
            name = new MediaCodecList(MediaCodecList.REGULAR_CODECS).findEncoderForFormat(probe);
        } catch (RuntimeException ignored) {
            // fall through to createEncoderByType
        }
        mVideo = name != null ? MediaCodec.createByCodecName(name) : MediaCodec.createEncoderByType(MIME_AVC);
        MediaCodecInfo.CodecCapabilities caps = mVideo.getCodecInfo().getCapabilitiesForType(MIME_AVC);
        boolean vbr = false;
        MediaCodecInfo.EncoderCapabilities enc = caps.getEncoderCapabilities();
        if (enc != null) vbr = enc.isBitrateModeSupported(MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_VBR);
        int highMaxLevel = 0;
        boolean high = false;
        for (MediaCodecInfo.CodecProfileLevel pl : caps.profileLevels) {
            if (pl.profile == MediaCodecInfo.CodecProfileLevel.AVCProfileHigh) {
                high = true;
                highMaxLevel = Math.max(highMaxLevel, pl.level);
            }
        }
        int level = highMaxLevel >= MediaCodecInfo.CodecProfileLevel.AVCLevel42 ? MediaCodecInfo.CodecProfileLevel.AVCLevel42 : 0;
        boolean configured = false;
        if (high) {
            try {
                mVideo.configure(videoFormat(true, level, vbr), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                mProfile = "high";
                configured = true;
            } catch (RuntimeException e) {
                Log.w(TAG, "H.264 High rejected, falling back to Baseline: " + e);
                mVideo.reset();
            }
        }
        if (!configured) {
            try {
                mVideo.configure(videoFormat(false, 0, vbr), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            } catch (RuntimeException e) {
                throw new MediaError(MediaError.ENCODER_FAILED, "H.264 encoder rejected " + mWidth + "x" + mHeight + "@" + mFps, e);
            }
            mProfile = "baseline";
        }
        mInputSurface = mVideo.createInputSurface();
    }

    private void drainVideo(boolean endOfStream) throws MediaError {
        long stallStart = System.nanoTime();
        while (true) {
            if (mCancelled) throw new MediaError(MediaError.ENCODER_FAILED, "cancelled");
            int index = mVideo.dequeueOutputBuffer(mVideoInfo, endOfStream ? DRAIN_TIMEOUT_US : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) return;
                if (System.nanoTime() - stallStart > STALL_LIMIT_NS) throw new MediaError(MediaError.TIMEOUT, "video encoder did not finish");
                continue;
            }
            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (mVideoTrack >= 0) throw new MediaError(MediaError.MUXER_FAILED, "video format changed twice");
                mVideoTrack = mMuxer.addTrack(mVideo.getOutputFormat());
                maybeStartMuxer();
                continue;
            }
            if (index < 0) continue;
            stallStart = System.nanoTime();
            ByteBuffer data = mVideo.getOutputBuffer(index);
            if ((mVideoInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) mVideoInfo.size = 0;
            if (mVideoInfo.size > 0 && data != null) {
                if (mCanvas != null) {
                    Long pts = mPtsFifo.poll();
                    if (pts != null) mVideoInfo.presentationTimeUs = pts;
                }
                writeSample(mVideoTrack, data, mVideoInfo);
                mFramesWritten++;
            }
            boolean eos = (mVideoInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            mVideo.releaseOutputBuffer(index, false);
            if (eos) return;
        }
    }

    // ------------------------------------------------------------------ audio

    /** Routes AAC output into the muxer's audio track. Writer thread. */
    private final class AudioSink implements AacPcmEncoder.Sink {
        @Override
        public void onFormat(MediaFormat format) throws MediaError {
            if (mAudioTrack >= 0) throw new MediaError(MediaError.MUXER_FAILED, "audio format changed twice");
            mAudioTrack = mMuxer.addTrack(format);
            maybeStartMuxer();
        }

        @Override
        public void onSample(ByteBuffer data, MediaCodec.BufferInfo info) {
            writeSample(mAudioTrack, data, info);
        }

        @Override
        public boolean cancelled() {
            return mCancelled;
        }
    }

    // ------------------------------------------------------------------ muxer

    private void maybeStartMuxer() {
        if (mMuxerStarted || mVideoTrack < 0 || (mHasAudio && mAudioTrack < 0)) return;
        mMuxer.start();
        mMuxerStarted = true;
        for (PendingSample s : mPending) mMuxer.writeSampleData(s.track, s.data, s.info);
        mPending.clear();
    }

    private void writeSample(int track, ByteBuffer data, MediaCodec.BufferInfo info) {
        data.position(info.offset);
        data.limit(info.offset + info.size);
        if (!mMuxerStarted) {
            ByteBuffer copy = ByteBuffer.allocate(info.size);
            copy.put(data);
            copy.flip();
            MediaCodec.BufferInfo bi = new MediaCodec.BufferInfo();
            bi.set(0, info.size, info.presentationTimeUs, info.flags);
            mPending.add(new PendingSample(track, copy, bi));
            return;
        }
        mMuxer.writeSampleData(track, data, info);
    }

    // ------------------------------------------------------------------ cleanup

    private void releaseCodecs() {
        if (mEgl != null) {
            mEgl.release();
            mEgl = null;
        }
        mCanvas = null;
        if (mVideo != null) {
            try {
                mVideo.stop();
            } catch (RuntimeException ignored) {
                // not started or already failed
            }
            mVideo.release();
            mVideo = null;
        }
        if (mInputSurface != null) {
            mInputSurface.release();
            mInputSurface = null;
        }
        if (mAudio != null) {
            mAudio.release();
            mAudio = null;
        }
        if (mMuxer != null) {
            try {
                mMuxer.release();
            } catch (RuntimeException ignored) {
                // release() of a started-but-not-stopped muxer may throw; the file is deleted anyway
            }
            mMuxer = null;
        }
        mPending.clear();
    }

    // ================================================================== renderers

    /** Draws RGBA texture into the encoder surface via EGL (RECORDABLE config) with explicit presentation times. */
    private static final class EglRenderer {
        private static final String VERTEX =
                "attribute vec2 aPos;\n"
                + "attribute vec2 aTex;\n"
                + "varying vec2 vTex;\n"
                + "void main() { gl_Position = vec4(aPos, 0.0, 1.0); vTex = aTex; }\n";
        private static final String FRAGMENT =
                "#ifdef GL_FRAGMENT_PRECISION_HIGH\nprecision highp float;\n#else\nprecision mediump float;\n#endif\n"
                + "varying vec2 vTex;\n"
                + "uniform sampler2D uTex;\n"
                + "void main() { gl_FragColor = vec4(texture2D(uTex, vTex).rgb, 1.0); }\n";

        private EGLDisplay mDisplay = EGL14.EGL_NO_DISPLAY;
        private EGLContext mContext = EGL14.EGL_NO_CONTEXT;
        private EGLSurface mSurface = EGL14.EGL_NO_SURFACE;
        private int mProgram;
        private int mTexture;
        private int mPosLoc;
        private int mTexLoc;
        private FloatBuffer mQuad;
        private int mWidth;
        private int mHeight;

        void init(Surface surface, int width, int height) throws MediaError {
            mWidth = width;
            mHeight = height;
            mDisplay = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);
            if (mDisplay == EGL14.EGL_NO_DISPLAY) throw eglError("eglGetDisplay");
            int[] version = new int[2];
            if (!EGL14.eglInitialize(mDisplay, version, 0, version, 1)) throw eglError("eglInitialize");
            int[] attribs = {
                EGL14.EGL_RED_SIZE, 8, EGL14.EGL_GREEN_SIZE, 8, EGL14.EGL_BLUE_SIZE, 8, EGL14.EGL_ALPHA_SIZE, 8,
                EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT, EGL_RECORDABLE_ANDROID, 1, EGL14.EGL_NONE
            };
            EGLConfig[] configs = new EGLConfig[1];
            int[] count = new int[1];
            if (!EGL14.eglChooseConfig(mDisplay, attribs, 0, configs, 0, 1, count, 0) || count[0] == 0) throw eglError("eglChooseConfig");
            mContext = EGL14.eglCreateContext(mDisplay, configs[0], EGL14.EGL_NO_CONTEXT,
                    new int[] {EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE}, 0);
            if (mContext == null || mContext == EGL14.EGL_NO_CONTEXT) throw eglError("eglCreateContext");
            mSurface = EGL14.eglCreateWindowSurface(mDisplay, configs[0], surface, new int[] {EGL14.EGL_NONE}, 0);
            if (mSurface == null || mSurface == EGL14.EGL_NO_SURFACE) throw eglError("eglCreateWindowSurface");
            if (!EGL14.eglMakeCurrent(mDisplay, mSurface, mSurface, mContext)) throw eglError("eglMakeCurrent");

            mProgram = buildProgram();
            mPosLoc = GLES20.glGetAttribLocation(mProgram, "aPos");
            mTexLoc = GLES20.glGetAttribLocation(mProgram, "aTex");
            int[] tex = new int[1];
            GLES20.glGenTextures(1, tex, 0);
            mTexture = tex[0];
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, mTexture);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_NEAREST);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_NEAREST);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glPixelStorei(GLES20.GL_UNPACK_ALIGNMENT, 4);
            GLES20.glTexImage2D(GLES20.GL_TEXTURE_2D, 0, GLES20.GL_RGBA, width, height, 0, GLES20.GL_RGBA, GLES20.GL_UNSIGNED_BYTE, null);
            // Row 0 of the uploaded data is the TOP of the frame, so the top of the viewport samples t = 0.
            float[] quad = {
                -1f, -1f, 0f, 1f,
                1f, -1f, 1f, 1f,
                -1f, 1f, 0f, 0f,
                1f, 1f, 1f, 0f,
            };
            mQuad = ByteBuffer.allocateDirect(quad.length * 4).order(ByteOrder.nativeOrder()).asFloatBuffer();
            mQuad.put(quad).position(0);
            checkGl("init");
        }

        void upload(ByteBuffer rgba) throws MediaError {
            rgba.position(0);
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, mTexture);
            GLES20.glTexSubImage2D(GLES20.GL_TEXTURE_2D, 0, 0, 0, mWidth, mHeight, GLES20.GL_RGBA, GLES20.GL_UNSIGNED_BYTE, rgba);
            checkGl("glTexSubImage2D");
        }

        void drawAndSwap(long ptsUs) throws MediaError {
            GLES20.glViewport(0, 0, mWidth, mHeight);
            GLES20.glUseProgram(mProgram);
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0);
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, mTexture);
            mQuad.position(0);
            GLES20.glVertexAttribPointer(mPosLoc, 2, GLES20.GL_FLOAT, false, 16, mQuad);
            GLES20.glEnableVertexAttribArray(mPosLoc);
            mQuad.position(2);
            GLES20.glVertexAttribPointer(mTexLoc, 2, GLES20.GL_FLOAT, false, 16, mQuad);
            GLES20.glEnableVertexAttribArray(mTexLoc);
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);
            checkGl("draw");
            EGLExt.eglPresentationTimeANDROID(mDisplay, mSurface, ptsUs * 1000L);
            if (!EGL14.eglSwapBuffers(mDisplay, mSurface)) throw eglError("eglSwapBuffers");
        }

        /** Releases context and surface; the display is NOT terminated (it is shared with Unity's renderer). */
        void release() {
            if (mDisplay != EGL14.EGL_NO_DISPLAY) {
                EGL14.eglMakeCurrent(mDisplay, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT);
                if (mSurface != EGL14.EGL_NO_SURFACE) EGL14.eglDestroySurface(mDisplay, mSurface);
                if (mContext != EGL14.EGL_NO_CONTEXT) EGL14.eglDestroyContext(mDisplay, mContext);
                EGL14.eglReleaseThread();
            }
            mDisplay = EGL14.EGL_NO_DISPLAY;
            mContext = EGL14.EGL_NO_CONTEXT;
            mSurface = EGL14.EGL_NO_SURFACE;
        }

        private int buildProgram() throws MediaError {
            int vs = compile(GLES20.GL_VERTEX_SHADER, VERTEX);
            int fs = compile(GLES20.GL_FRAGMENT_SHADER, FRAGMENT);
            int program = GLES20.glCreateProgram();
            GLES20.glAttachShader(program, vs);
            GLES20.glAttachShader(program, fs);
            GLES20.glLinkProgram(program);
            int[] status = new int[1];
            GLES20.glGetProgramiv(program, GLES20.GL_LINK_STATUS, status, 0);
            if (status[0] == 0) throw new MediaError(MediaError.ENCODER_FAILED, "GL link: " + GLES20.glGetProgramInfoLog(program));
            return program;
        }

        private static int compile(int type, String source) throws MediaError {
            int shader = GLES20.glCreateShader(type);
            GLES20.glShaderSource(shader, source);
            GLES20.glCompileShader(shader);
            int[] status = new int[1];
            GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, status, 0);
            if (status[0] == 0) throw new MediaError(MediaError.ENCODER_FAILED, "GL compile: " + GLES20.glGetShaderInfoLog(shader));
            return shader;
        }

        private static void checkGl(String op) throws MediaError {
            int error = GLES20.glGetError();
            if (error != GLES20.GL_NO_ERROR) throw new MediaError(MediaError.ENCODER_FAILED, op + ": GL error 0x" + Integer.toHexString(error));
        }

        private static MediaError eglError(String op) {
            return new MediaError(MediaError.ENCODER_FAILED, op + ": EGL error 0x" + Integer.toHexString(EGL14.eglGetError()));
        }
    }

    /** Fallback: Surface.lockHardwareCanvas() + Bitmap (timestamps restored from the submit order). */
    private static final class CanvasRenderer {
        private final Surface mSurface;
        private final Bitmap mBitmap;

        CanvasRenderer(Surface surface, int width, int height) {
            mSurface = surface;
            mBitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
            mBitmap.setHasAlpha(false);
        }

        void upload(ByteBuffer rgba) {
            rgba.position(0);
            mBitmap.copyPixelsFromBuffer(rgba); // ARGB_8888 memory order is R, G, B, A = our RGBA32
        }

        void draw() {
            Canvas canvas = mSurface.lockHardwareCanvas();
            try {
                canvas.drawBitmap(mBitmap, 0f, 0f, null);
            } finally {
                mSurface.unlockCanvasAndPost(canvas);
            }
        }
    }
}
