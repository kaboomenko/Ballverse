package com.ballverse.media;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.IntBuffer;

/**
 * YUV 4:2:0 (any plane layout: I420, NV12, NV21, P010 — given as three planes with row/pixel strides, like
 * android.media.Image YUV_420_888 / YCBCR_P010) to top-down RGBA32 with rotation, crop and downscaling in one pass.
 *
 * Sampling: nearest neighbour, or a 2x2 luma box filter when downscaling by 1.5x or more; chroma nearest.
 * Colour: BT.601 / BT.709 / BT.2020 matrices, limited or full range (8-bit fixed-point tables). 10/16-bit samples
 * (MSB-aligned, little-endian) are reduced to 8 bits for SDR; HDR (PQ / HLG) is tone-mapped to SDR BT.709 in float:
 * EOTF LUT → BT.2020→709 gamut matrix → max-RGB extended Reinhard (reference white 203 nits, peak 1000 nits) →
 * BT.709 gamma 1/2.4 LUT. This is the fallback when the platform decoder cannot tone-map (Android < 13 or no
 * vendor support).
 *
 * Pure java.nio: no Android dependencies (unit-testable on a desktop JVM). Not thread-safe; one per reader.
 */
final class YuvConverter {
    static final int STANDARD_BT601 = 0;
    static final int STANDARD_BT709 = 1;
    static final int STANDARD_BT2020 = 2;
    static final int TRANSFER_SDR = 0;
    static final int TRANSFER_PQ = 1;
    static final int TRANSFER_HLG = 2;

    static final double REFERENCE_WHITE_NITS = 203.0;
    static final double HDR_PEAK_NITS = 1000.0;

    /** One image plane: buffer + strides in bytes. */
    static final class Plane {
        final ByteBuffer buffer;
        final int rowStride;
        final int pixelStride;

        Plane(ByteBuffer buffer, int rowStride, int pixelStride) {
            this.buffer = buffer;
            this.rowStride = rowStride;
            this.pixelStride = pixelStride;
        }
    }

    private final int mOutWidth;
    private final int mOutHeight;
    private final int mRotation;

    // Colour setup.
    private int mStandard = -1;
    private boolean mFullRange;
    private int mTransfer = -1;
    private final int[] mYTable = new int[256];
    private final int[] mRv = new int[256];
    private final int[] mGu = new int[256];
    private final int[] mGv = new int[256];
    private final int[] mBu = new int[256];
    // HDR float path.
    private float mKy, mOffY, mKc;
    private float mCrv, mCgu, mCgv, mCbu;
    private float[] mEotf;      // 4096 entries: signal [0,1] -> linear, units of reference white
    private byte[] mOetf;       // 4096 entries: linear [0,1] -> 8-bit BT.709

    // Geometry cache (rebuilt when strides or crop change).
    private int mKeyYRow = -1, mKeyYPix, mKeyURow, mKeyUPix, mKeyVRow, mKeyVPix, mKeyCropX, mKeyCropY, mKeyCropW, mKeyCropH;
    private boolean mBox;
    private int[] mLumaX, mLumaY, mUX, mUY, mVX, mVY;
    private int[] mRow;

    YuvConverter(int outWidth, int outHeight, int rotationDegrees) {
        if (rotationDegrees != 0 && rotationDegrees != 90 && rotationDegrees != 180 && rotationDegrees != 270) {
            throw new IllegalArgumentException("rotation must be 0/90/180/270");
        }
        mOutWidth = outWidth;
        mOutHeight = outHeight;
        mRotation = rotationDegrees;
        mRow = new int[outWidth];
    }

    /** Output size rule shared with C# VideoReaderOptions.ComputeOutputSize: fit, never upscale, even, >= 2. */
    static int[] outputSize(int displayWidth, int displayHeight, int maxWidth, int maxHeight) {
        double scale = 1.0;
        if (maxWidth > 0) scale = Math.min(scale, (double) maxWidth / displayWidth);
        if (maxHeight > 0) scale = Math.min(scale, (double) maxHeight / displayHeight);
        int w = Math.max(2, (int) Math.floor(displayWidth * scale + 0.5) & ~1);
        int h = Math.max(2, (int) Math.floor(displayHeight * scale + 0.5) & ~1);
        return new int[] {w, h};
    }

    /** Configures the colour conversion (cheap to call per frame; tables are rebuilt only on change). */
    void setColor(int standard, boolean fullRange, int transfer) {
        if (standard == mStandard && fullRange == mFullRange && transfer == mTransfer) return;
        mStandard = standard;
        mFullRange = fullRange;
        mTransfer = transfer;
        double kr, kb;
        switch (standard) {
            case STANDARD_BT601:
                kr = 0.299;
                kb = 0.114;
                break;
            case STANDARD_BT2020:
                kr = 0.2627;
                kb = 0.0593;
                break;
            default:
                kr = 0.2126;
                kb = 0.0722;
                break;
        }
        double kg = 1.0 - kr - kb;
        double rv = 2.0 * (1.0 - kr);
        double bu = 2.0 * (1.0 - kb);
        double gu = -2.0 * kb * (1.0 - kb) / kg;
        double gv = -2.0 * kr * (1.0 - kr) / kg;
        double yScale = fullRange ? 1.0 : 255.0 / 219.0;
        double cScale = fullRange ? 1.0 : 255.0 / 224.0;
        int yOffset = fullRange ? 0 : 16;
        for (int i = 0; i < 256; i++) {
            mYTable[i] = (int) Math.round((i - yOffset) * yScale * 65536.0);
            double c = (i - 128) * cScale;
            mRv[i] = (int) Math.round(rv * c * 65536.0);
            mGu[i] = (int) Math.round(gu * c * 65536.0);
            mGv[i] = (int) Math.round(gv * c * 65536.0);
            mBu[i] = (int) Math.round(bu * c * 65536.0);
        }
        // Float coefficients for 10-bit HDR (applied to normalised Y' in [0,1] and C in [-0.5,0.5]).
        mKy = (float) (fullRange ? 1.0 / 1023.0 : 1.0 / 876.0);
        mOffY = fullRange ? 0f : 64f;
        mKc = (float) (fullRange ? 1.0 / 1023.0 : 1.0 / 896.0);
        mCrv = (float) rv;
        mCgu = (float) gu;
        mCgv = (float) gv;
        mCbu = (float) bu;
        if (transfer == TRANSFER_PQ || transfer == TRANSFER_HLG) buildHdrTables(transfer);
    }

    private void buildHdrTables(int transfer) {
        mEotf = new float[4096];
        for (int i = 0; i < 4096; i++) {
            double e = i / 4095.0;
            double nits;
            if (transfer == TRANSFER_PQ) {
                final double m1 = 0.1593017578125, m2 = 78.84375, c1 = 0.8359375, c2 = 18.8515625, c3 = 18.6875;
                double p = Math.pow(e, 1.0 / m2);
                nits = 10000.0 * Math.pow(Math.max(p - c1, 0.0) / (c2 - c3 * p), 1.0 / m1);
            } else {
                final double a = 0.17883277, b = 0.28466892, c = 0.55991073;
                double scene = e <= 0.5 ? e * e / 3.0 : (Math.exp((e - c) / a) + b) / 12.0;
                nits = HDR_PEAK_NITS * Math.pow(scene, 1.2); // HLG OOTF, per channel approximation
            }
            mEotf[i] = (float) (nits / REFERENCE_WHITE_NITS);
        }
        mOetf = new byte[4096];
        for (int i = 0; i < 4096; i++) {
            double v = Math.pow(i / 4095.0, 1.0 / 2.4);
            mOetf[i] = (byte) Math.round(Math.min(1.0, v) * 255.0);
        }
    }

    /**
     * Converts one frame. sampleBytes: 1 for 8-bit planes, 2 for 16-bit little-endian MSB-aligned samples (P010).
     * crop: visible rectangle in luma pixels. dst: direct buffer of outWidth*outHeight*4 bytes (written from 0).
     */
    void convert(Plane y, Plane u, Plane v, int sampleBytes, int cropX, int cropY, int cropW, int cropH, ByteBuffer dst) {
        prepareGeometry(y, u, v, cropX, cropY, cropW, cropH);
        dst.clear();
        IntBuffer out = dst.order(ByteOrder.LITTLE_ENDIAN).asIntBuffer();
        if (sampleBytes == 1) {
            convert8(y.buffer, u.buffer, v.buffer, out);
        } else if (mTransfer == TRANSFER_PQ || mTransfer == TRANSFER_HLG) {
            convertHdr(le(y.buffer), le(u.buffer), le(v.buffer), out);
        } else {
            convert16As8(le(y.buffer), le(u.buffer), le(v.buffer), out);
        }
    }

    private static ByteBuffer le(ByteBuffer b) {
        return b.duplicate().order(ByteOrder.LITTLE_ENDIAN);
    }

    private void prepareGeometry(Plane y, Plane u, Plane v, int cropX, int cropY, int cropW, int cropH) {
        if (mLumaX != null && mKeyYRow == y.rowStride && mKeyYPix == y.pixelStride && mKeyURow == u.rowStride
                && mKeyUPix == u.pixelStride && mKeyVRow == v.rowStride && mKeyVPix == v.pixelStride
                && mKeyCropX == cropX && mKeyCropY == cropY && mKeyCropW == cropW && mKeyCropH == cropH) {
            return;
        }
        mKeyYRow = y.rowStride;
        mKeyYPix = y.pixelStride;
        mKeyURow = u.rowStride;
        mKeyUPix = u.pixelStride;
        mKeyVRow = v.rowStride;
        mKeyVPix = v.pixelStride;
        mKeyCropX = cropX;
        mKeyCropY = cropY;
        mKeyCropW = cropW;
        mKeyCropH = cropH;

        boolean swap = mRotation == 90 || mRotation == 270;
        int dispW = swap ? cropH : cropW;
        int dispH = swap ? cropW : cropH;
        mBox = (double) dispW / mOutWidth >= 1.5 || (double) dispH / mOutHeight >= 1.5;
        mLumaX = new int[mOutWidth * 2];
        mUX = new int[mOutWidth];
        mVX = new int[mOutWidth];
        mLumaY = new int[mOutHeight * 2];
        mUY = new int[mOutHeight];
        mVY = new int[mOutHeight];
        int[] taps = new int[2];
        for (int ox = 0; ox < mOutWidth; ox++) {
            displayTaps(ox, mOutWidth, dispW, taps);
            for (int t = 0; t < 2; t++) {
                mLumaX[2 * ox + t] = columnTerm(taps[t], y.rowStride, y.pixelStride, cropX, cropY, cropW, cropH, false);
            }
            mUX[ox] = columnTerm(taps[0], u.rowStride, u.pixelStride, cropX, cropY, cropW, cropH, true);
            mVX[ox] = columnTerm(taps[0], v.rowStride, v.pixelStride, cropX, cropY, cropW, cropH, true);
        }
        for (int oy = 0; oy < mOutHeight; oy++) {
            displayTaps(oy, mOutHeight, dispH, taps);
            for (int t = 0; t < 2; t++) {
                mLumaY[2 * oy + t] = rowTerm(taps[t], y.rowStride, y.pixelStride, cropX, cropY, cropW, cropH, false);
            }
            mUY[oy] = rowTerm(taps[0], u.rowStride, u.pixelStride, cropX, cropY, cropW, cropH, true);
            mVY[oy] = rowTerm(taps[0], v.rowStride, v.pixelStride, cropX, cropY, cropW, cropH, true);
        }
    }

    /** Display-space sample indices for output index o (two taps for the box filter, else equal). */
    private void displayTaps(int o, int outSize, int dispSize, int[] taps) {
        double centre = (o + 0.5) * dispSize / outSize;
        if (mBox) {
            int t0 = (int) Math.floor(centre - 0.5);
            t0 = Math.max(0, Math.min(dispSize - 1, t0));
            taps[0] = t0;
            taps[1] = Math.min(dispSize - 1, t0 + 1);
        } else {
            int t = Math.max(0, Math.min(dispSize - 1, (int) centre));
            taps[0] = t;
            taps[1] = t;
        }
    }

    // A display column (dx) maps to a source column (rotation 0/180) or a source row (90/270); a display row (dy) to the other.
    private int columnTerm(int dx, int rowStride, int pixelStride, int cropX, int cropY, int cropW, int cropH, boolean chroma) {
        switch (mRotation) {
            case 90:
                return srcRow(cropY + cropH - 1 - dx, rowStride, chroma);
            case 180:
                return srcCol(cropX + cropW - 1 - dx, pixelStride, chroma);
            case 270:
                return srcRow(cropY + dx, rowStride, chroma);
            default:
                return srcCol(cropX + dx, pixelStride, chroma);
        }
    }

    private int rowTerm(int dy, int rowStride, int pixelStride, int cropX, int cropY, int cropW, int cropH, boolean chroma) {
        switch (mRotation) {
            case 90:
                return srcCol(cropX + dy, pixelStride, chroma);
            case 180:
                return srcRow(cropY + cropH - 1 - dy, rowStride, chroma);
            case 270:
                return srcCol(cropX + cropW - 1 - dy, pixelStride, chroma);
            default:
                return srcRow(cropY + dy, rowStride, chroma);
        }
    }

    private static int srcRow(int y, int rowStride, boolean chroma) {
        return (chroma ? y >> 1 : y) * rowStride;
    }

    private static int srcCol(int x, int pixelStride, boolean chroma) {
        return (chroma ? x >> 1 : x) * pixelStride;
    }

    private void convert8(ByteBuffer yb, ByteBuffer ub, ByteBuffer vb, IntBuffer out) {
        final int w = mOutWidth;
        final int[] lx = mLumaX, ux = mUX, vx = mVX, row = mRow;
        final int[] yt = mYTable, rvT = mRv, guT = mGu, gvT = mGv, buT = mBu;
        for (int oy = 0; oy < mOutHeight; oy++) {
            final int r0 = mLumaY[2 * oy], r1 = mLumaY[2 * oy + 1], ru = mUY[oy], rv = mVY[oy];
            for (int ox = 0; ox < w; ox++) {
                int yy;
                if (mBox) {
                    int c0 = lx[2 * ox], c1 = lx[2 * ox + 1];
                    yy = ((yb.get(c0 + r0) & 0xFF) + (yb.get(c1 + r0) & 0xFF) + (yb.get(c0 + r1) & 0xFF) + (yb.get(c1 + r1) & 0xFF) + 2) >> 2;
                } else {
                    yy = yb.get(lx[2 * ox] + r0) & 0xFF;
                }
                int uu = ub.get(ux[ox] + ru) & 0xFF;
                int vv = vb.get(vx[ox] + rv) & 0xFF;
                row[ox] = pack(yt[yy] + rvT[vv], yt[yy] + guT[uu] + gvT[vv], yt[yy] + buT[uu]);
            }
            out.put(row, 0, w);
        }
    }

    private void convert16As8(ByteBuffer yb, ByteBuffer ub, ByteBuffer vb, IntBuffer out) {
        final int w = mOutWidth;
        final int[] lx = mLumaX, ux = mUX, vx = mVX, row = mRow;
        final int[] yt = mYTable, rvT = mRv, guT = mGu, gvT = mGv, buT = mBu;
        for (int oy = 0; oy < mOutHeight; oy++) {
            final int r0 = mLumaY[2 * oy], r1 = mLumaY[2 * oy + 1], ru = mUY[oy], rv = mVY[oy];
            for (int ox = 0; ox < w; ox++) {
                int yy;
                if (mBox) {
                    int c0 = lx[2 * ox], c1 = lx[2 * ox + 1];
                    yy = ((yb.getShort(c0 + r0) & 0xFFFF) + (yb.getShort(c1 + r0) & 0xFFFF)
                            + (yb.getShort(c0 + r1) & 0xFFFF) + (yb.getShort(c1 + r1) & 0xFFFF)) >> 10;
                } else {
                    yy = (yb.getShort(lx[2 * ox] + r0) & 0xFFFF) >> 8;
                }
                int uu = (ub.getShort(ux[ox] + ru) & 0xFFFF) >> 8;
                int vv = (vb.getShort(vx[ox] + rv) & 0xFFFF) >> 8;
                row[ox] = pack(yt[yy] + rvT[vv], yt[yy] + guT[uu] + gvT[vv], yt[yy] + buT[uu]);
            }
            out.put(row, 0, w);
        }
    }

    private void convertHdr(ByteBuffer yb, ByteBuffer ub, ByteBuffer vb, IntBuffer out) {
        final int w = mOutWidth;
        final int[] lx = mLumaX, ux = mUX, vx = mVX, row = mRow;
        final float[] eotf = mEotf;
        final byte[] oetf = mOetf;
        final float ky = mKy, offY = mOffY, kc = mKc, crv = mCrv, cgu = mCgu, cgv = mCgv, cbu = mCbu;
        final float peak = (float) (HDR_PEAK_NITS / REFERENCE_WHITE_NITS);
        final float invPeak2 = 1f / (peak * peak);
        for (int oy = 0; oy < mOutHeight; oy++) {
            final int r0 = mLumaY[2 * oy], r1 = mLumaY[2 * oy + 1], ru = mUY[oy], rv = mVY[oy];
            for (int ox = 0; ox < w; ox++) {
                int y10;
                if (mBox) {
                    int c0 = lx[2 * ox], c1 = lx[2 * ox + 1];
                    y10 = ((yb.getShort(c0 + r0) & 0xFFFF) + (yb.getShort(c1 + r0) & 0xFFFF)
                            + (yb.getShort(c0 + r1) & 0xFFFF) + (yb.getShort(c1 + r1) & 0xFFFF)) >> 8;
                } else {
                    y10 = (yb.getShort(lx[2 * ox] + r0) & 0xFFFF) >> 6;
                }
                int u10 = (ub.getShort(ux[ox] + ru) & 0xFFFF) >> 6;
                int v10 = (vb.getShort(vx[ox] + rv) & 0xFFFF) >> 6;
                float yn = (y10 - offY) * ky;
                float cb = (u10 - 512) * kc;
                float cr = (v10 - 512) * kc;
                float rr = eotf[lut(yn + crv * cr)];
                float gg = eotf[lut(yn + cgu * cb + cgv * cr)];
                float bb = eotf[lut(yn + cbu * cb)];
                // BT.2020 -> BT.709 primaries (linear light).
                float r = 1.6605f * rr - 0.5876f * gg - 0.0728f * bb;
                float g = -0.1246f * rr + 1.1329f * gg - 0.0083f * bb;
                float b = -0.0182f * rr - 0.1006f * gg + 1.1187f * bb;
                if (r < 0) r = 0;
                if (g < 0) g = 0;
                if (b < 0) b = 0;
                float m = Math.max(r, Math.max(g, b));
                if (m > 1e-6f) {
                    float mapped = m * (1f + m * invPeak2) / (1f + m);
                    float s = mapped / m;
                    r *= s;
                    g *= s;
                    b *= s;
                }
                row[ox] = 0xFF000000 | ((oetf[lut(b)] & 0xFF) << 16) | ((oetf[lut(g)] & 0xFF) << 8) | (oetf[lut(r)] & 0xFF);
            }
            out.put(row, 0, w);
        }
    }

    private static int lut(float v) {
        int i = (int) (v * 4095f + 0.5f);
        return i < 0 ? 0 : (i > 4095 ? 4095 : i);
    }

    private static int pack(int r, int g, int b) {
        r = (r + 32768) >> 16;
        g = (g + 32768) >> 16;
        b = (b + 32768) >> 16;
        r = r < 0 ? 0 : (r > 255 ? 255 : r);
        g = g < 0 ? 0 : (g > 255 ? 255 : g);
        b = b < 0 ? 0 : (b > 255 ? 255 : b);
        return 0xFF000000 | (b << 16) | (g << 8) | r;
    }
}
