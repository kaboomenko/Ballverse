package com.ballverse.media;

import android.media.AudioFormat;
import android.media.MediaCodec;
import android.media.MediaExtractor;
import android.media.MediaFormat;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.channels.FileChannel;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Decodes the first decodable audio track to raw f32le interleaved PCM at the decoder's output sample rate and
 * channel count (C# then downmixes/resamples to 48 kHz stereo). The output starts exactly at startUs: leading gaps are
 * filled with silence, samples before startUs are dropped, and decoding stops after durationUs.
 */
final class AudioDecoder {
    private static final long TIMEOUT_US = 10000;
    private static final long STALL_LIMIT_NS = 10_000_000_000L;

    private AudioDecoder() {
    }

    static Json decode(String path, String outPath, long startUs, long durationUs, AtomicBoolean cancel) throws MediaError {
        File out = new File(outPath);
        MediaExtractor extractor = new MediaExtractor();
        MediaCodec codec = null;
        FileOutputStream stream = null;
        boolean ok = false;
        try {
            try {
                extractor.setDataSource(path);
            } catch (IOException e) {
                throw new MediaError(MediaError.CORRUPT, "cannot parse the file", e);
            }
            MediaProbe.checkDrm(extractor);
            int track = -1;
            MediaFormat format = null;
            String decoderName = null;
            for (int i = 0; i < extractor.getTrackCount(); i++) {
                MediaFormat f = extractor.getTrackFormat(i);
                String mime = f.getString(MediaFormat.KEY_MIME);
                if (mime == null || !mime.startsWith("audio/")) continue;
                decoderName = MediaProbe.findDecoder(f);
                if (decoderName == null) continue;
                track = i;
                format = f;
                break;
            }
            if (track < 0) {
                ok = true;
                return new Json().put("path", outPath).put("sampleRate", 0).put("channels", 0).put("frames", 0L);
            }
            extractor.selectTrack(track);
            if (startUs > 0) extractor.seekTo(startUs, MediaExtractor.SEEK_TO_PREVIOUS_SYNC);
            codec = MediaCodec.createByCodecName(decoderName);
            codec.configure(format, null, null, 0);
            codec.start();

            stream = new FileOutputStream(out);
            FileChannel channel = stream.getChannel();
            ByteBuffer floatBytes = ByteBuffer.allocateDirect(1 << 16).order(ByteOrder.LITTLE_ENDIAN);

            int sampleRate = MediaProbe.intOr(format, MediaFormat.KEY_SAMPLE_RATE, 0);
            int channels = MediaProbe.intOr(format, MediaFormat.KEY_CHANNEL_COUNT, 0);
            boolean floatPcm = false;
            long endUs = durationUs > 0 ? startUs + durationUs : Long.MAX_VALUE;
            long maxFrames = -1;   // computed once the output format is known
            long written = 0;      // frames written so far (including leading silence)
            boolean inputDone = false;
            boolean outputDone = false;
            boolean started = false;
            long lastProgress = System.nanoTime();
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();

            while (!outputDone) {
                if (cancel.get()) return null;
                if (!inputDone) {
                    int in = codec.dequeueInputBuffer(TIMEOUT_US);
                    if (in >= 0) {
                        ByteBuffer buf = codec.getInputBuffer(in);
                        int size = buf == null ? -1 : extractor.readSampleData(buf, 0);
                        long t = extractor.getSampleTime();
                        if (size < 0 || t > endUs) {
                            codec.queueInputBuffer(in, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                            inputDone = true;
                        } else {
                            codec.queueInputBuffer(in, 0, size, t, 0);
                            extractor.advance();
                        }
                        lastProgress = System.nanoTime();
                    }
                }
                int outIndex = codec.dequeueOutputBuffer(info, TIMEOUT_US);
                if (outIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    MediaFormat of = codec.getOutputFormat();
                    sampleRate = MediaProbe.intOr(of, MediaFormat.KEY_SAMPLE_RATE, sampleRate);
                    channels = MediaProbe.intOr(of, MediaFormat.KEY_CHANNEL_COUNT, channels);
                    floatPcm = MediaProbe.intOr(of, "pcm-encoding", AudioFormat.ENCODING_PCM_16BIT) == AudioFormat.ENCODING_PCM_FLOAT;
                    continue;
                }
                if (outIndex < 0) {
                    if (System.nanoTime() - lastProgress > STALL_LIMIT_NS) throw new MediaError(MediaError.TIMEOUT, "audio decoder stalled");
                    continue;
                }
                lastProgress = System.nanoTime();
                if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) outputDone = true;
                ByteBuffer data = codec.getOutputBuffer(outIndex);
                if (data != null && info.size > 0 && sampleRate > 0 && channels > 0) {
                    if (maxFrames < 0) maxFrames = durationUs > 0 ? (durationUs * sampleRate + 999999) / 1000000 : Long.MAX_VALUE;
                    data.position(info.offset);
                    data.limit(info.offset + info.size);
                    ByteBuffer pcm = data.slice().order(ByteOrder.nativeOrder());
                    int bytesPerSample = floatPcm ? 4 : 2;
                    int frames = info.size / (bytesPerSample * channels);
                    long bufferStartFrame = Math.round((info.presentationTimeUs - startUs) * (double) sampleRate / 1e6);
                    int skip = 0;
                    if (!started) {
                        if (bufferStartFrame > 0) {
                            long silence = Math.min(bufferStartFrame, maxFrames);
                            writeSilence(channel, floatBytes, silence * channels);
                            written += silence;
                        } else {
                            skip = (int) Math.min(frames, -bufferStartFrame);
                        }
                        if (skip < frames) started = true;
                    }
                    if (started) {
                        long take = Math.min(frames - skip, maxFrames - written);
                        if (take > 0) {
                            writeSamples(channel, floatBytes, pcm, floatPcm, skip * channels, (int) take * channels);
                            written += take;
                        }
                        if (written >= maxFrames) outputDone = true;
                    }
                }
                codec.releaseOutputBuffer(outIndex, false);
            }
            channel.force(false);
            ok = true;
            return new Json().put("path", outPath).put("sampleRate", sampleRate).put("channels", channels).put("frames", written);
        } catch (IOException | RuntimeException e) {
            throw MediaError.wrap(MediaError.DECODER_FAILED, e);
        } finally {
            if (codec != null) {
                try {
                    codec.stop();
                } catch (RuntimeException ignored) {
                    // already stopped / failed
                }
                codec.release();
            }
            extractor.release();
            if (stream != null) {
                try {
                    stream.close();
                } catch (IOException ignored) {
                    // nothing to do
                }
            }
            if (!ok && out.exists() && !out.delete()) out.deleteOnExit();
        }
    }

    private static void writeSilence(FileChannel channel, ByteBuffer scratch, long samples) throws IOException {
        long left = samples;
        while (left > 0) {
            scratch.clear();
            int n = (int) Math.min(left, scratch.capacity() / 4);
            for (int i = 0; i < n; i++) scratch.putFloat(0f);
            scratch.flip();
            while (scratch.hasRemaining()) channel.write(scratch);
            left -= n;
        }
    }

    private static void writeSamples(FileChannel channel, ByteBuffer scratch, ByteBuffer pcm, boolean floatPcm, int firstSample, int count) throws IOException {
        int i = firstSample;
        int end = firstSample + count;
        while (i < end) {
            scratch.clear();
            int n = Math.min(end - i, scratch.capacity() / 4);
            if (floatPcm) {
                for (int k = 0; k < n; k++) scratch.putFloat(pcm.getFloat((i + k) * 4));
            } else {
                for (int k = 0; k < n; k++) scratch.putFloat(pcm.getShort((i + k) * 2) / 32768f);
            }
            scratch.flip();
            while (scratch.hasRemaining()) channel.write(scratch);
            i += n;
        }
    }
}
