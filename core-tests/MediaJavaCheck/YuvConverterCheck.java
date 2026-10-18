package com.ballverse.media;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Random;

/**
 * Plain-JVM regression check of {@link YuvConverter} (pure java.nio), run by check.sh after the compile check.
 * Compares the converter against an independent reference: BT.709 limited-range floating-point conversion with
 * nearest-neighbour sampling and the clockwise display rotations 0/90/180/270, for I420, NV12 and NV21 plane layouts,
 * with and without row padding, for several crop rectangles. Tolerance: 1 code value per channel (fixed-point
 * rounding). A control case (90° output vs. 270° reference) must fail, proving the comparison detects errors.
 * Exit code 1 on any mismatch. Never packaged.
 */
public final class YuvConverterCheck {
    private static final int W = 10;
    private static final int H = 6;

    private YuvConverterCheck() {
    }

    private static final class Source {
        final int[][] y = new int[H][W];
        final int[][] u = new int[H / 2][W / 2];
        final int[][] v = new int[H / 2][W / 2];
    }

    public static void main(String[] args) {
        Random random = new Random(7);
        Source src = new Source();
        for (int r = 0; r < H; r++) for (int c = 0; c < W; c++) src.y[r][c] = 16 + random.nextInt(220);
        for (int r = 0; r < H / 2; r++) {
            for (int c = 0; c < W / 2; c++) {
                src.u[r][c] = 16 + random.nextInt(225);
                src.v[r][c] = 16 + random.nextInt(225);
            }
        }
        int[][] crops = {{0, 0, W, H}, {2, 2, 6, 4}, {4, 0, 6, 2}};
        String[] layouts = {"I420", "NV12", "NV21"};
        int cases = 0;
        int failures = 0;
        for (String layout : layouts) {
            for (int padding : new int[] {0, 16}) {
                for (int[] crop : crops) {
                    for (int rotation : new int[] {0, 90, 180, 270}) {
                        cases++;
                        int diff = maxDiff(src, layout, padding, crop, rotation, rotation);
                        if (diff > 1) {
                            failures++;
                            System.out.println("MISMATCH " + layout + " pad " + padding + " crop " + crop[0] + "," + crop[1] + " "
                                    + crop[2] + "x" + crop[3] + " rot " + rotation + ": max diff " + diff);
                        }
                    }
                }
            }
        }
        int control = maxDiff(src, "I420", 0, crops[0], 90, 270);
        if (control <= 1) {
            System.out.println("CONTROL FAILED: 90 deg output matched the 270 deg reference (diff " + control + ")");
            failures++;
        }
        if (failures > 0) {
            System.out.println("YuvConverterCheck: FAILED (" + failures + " of " + cases + " cases)");
            System.exit(1);
        }
        System.out.println("YuvConverterCheck: OK (" + cases + " cases, control diff " + control + ")");
    }

    /** Converts with YuvConverter at `rotation` and returns the max channel difference to the reference at `refRotation`. */
    private static int maxDiff(Source s, String layout, int padding, int[] crop, int rotation, int refRotation) {
        int cx = crop[0], cy = crop[1], cw = crop[2], ch = crop[3];
        boolean swap = rotation == 90 || rotation == 270;
        int outW = swap ? ch : cw;
        int outH = swap ? cw : ch;
        boolean refSwap = refRotation == 90 || refRotation == 270;
        if ((refSwap ? ch : cw) != outW) return 255; // shapes differ: certainly not equal

        int yStride = W + padding;
        ByteBuffer yb = ByteBuffer.allocateDirect(yStride * H);
        for (int r = 0; r < H; r++) for (int c = 0; c < W; c++) yb.put(r * yStride + c, (byte) s.y[r][c]);
        YuvConverter.Plane yPlane = new YuvConverter.Plane(yb, yStride, 1);
        YuvConverter.Plane uPlane;
        YuvConverter.Plane vPlane;
        if (layout.equals("I420")) {
            int cs = W / 2 + padding;
            ByteBuffer ub = ByteBuffer.allocateDirect(cs * H / 2);
            ByteBuffer vb = ByteBuffer.allocateDirect(cs * H / 2);
            for (int r = 0; r < H / 2; r++) {
                for (int c = 0; c < W / 2; c++) {
                    ub.put(r * cs + c, (byte) s.u[r][c]);
                    vb.put(r * cs + c, (byte) s.v[r][c]);
                }
            }
            uPlane = new YuvConverter.Plane(ub, cs, 1);
            vPlane = new YuvConverter.Plane(vb, cs, 1);
        } else {
            int cs = W + padding;
            ByteBuffer uv = ByteBuffer.allocateDirect(cs * H / 2 + 1);
            boolean nv12 = layout.equals("NV12");
            for (int r = 0; r < H / 2; r++) {
                for (int c = 0; c < W / 2; c++) {
                    uv.put(r * cs + 2 * c + (nv12 ? 0 : 1), (byte) s.u[r][c]);
                    uv.put(r * cs + 2 * c + (nv12 ? 1 : 0), (byte) s.v[r][c]);
                }
            }
            ByteBuffer first = uv.duplicate();
            ByteBuffer second = uv.duplicate();
            second.position(1);
            ByteBuffer secondSlice = second.slice();
            uPlane = new YuvConverter.Plane(nv12 ? first : secondSlice, cs, 2);
            vPlane = new YuvConverter.Plane(nv12 ? secondSlice : first, cs, 2);
        }

        YuvConverter converter = new YuvConverter(outW, outH, rotation);
        converter.setColor(YuvConverter.STANDARD_BT709, false, YuvConverter.TRANSFER_SDR);
        ByteBuffer out = ByteBuffer.allocateDirect(outW * outH * 4).order(ByteOrder.LITTLE_ENDIAN);
        converter.convert(yPlane, uPlane, vPlane, 1, cx, cy, cw, ch, out);

        int max = 0;
        for (int dy = 0; dy < outH; dy++) {
            for (int dx = 0; dx < outW; dx++) {
                int sx, sy;
                switch (refRotation) {
                    case 90:
                        sx = cx + dy;
                        sy = cy + ch - 1 - dx;
                        break;
                    case 180:
                        sx = cx + cw - 1 - dx;
                        sy = cy + ch - 1 - dy;
                        break;
                    case 270:
                        sx = cx + cw - 1 - dy;
                        sy = cy + dx;
                        break;
                    default:
                        sx = cx + dx;
                        sy = cy + dy;
                        break;
                }
                double yy = 1.164383561643836 * (s.y[sy][sx] - 16);
                double uu = s.u[sy >> 1][sx >> 1] - 128;
                double vv = s.v[sy >> 1][sx >> 1] - 128;
                int[] ref = {
                    clamp(yy + 1.792741071428571 * vv),
                    clamp(yy - 0.213248614273730 * uu - 0.532909328559444 * vv),
                    clamp(yy + 2.112401785714286 * uu),
                    255,
                };
                int at = (dy * outW + dx) * 4;
                for (int k = 0; k < 4; k++) max = Math.max(max, Math.abs((out.get(at + k) & 0xFF) - ref[k]));
            }
        }
        return max;
    }

    private static int clamp(double v) {
        long r = Math.round(v);
        return (int) (r < 0 ? 0 : (r > 255 ? 255 : r));
    }
}
