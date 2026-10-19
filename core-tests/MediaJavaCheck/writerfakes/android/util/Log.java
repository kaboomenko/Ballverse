package android.util;

/** JVM fake (android-all's Log is native). Not the Android class. */
public final class Log {
    private Log() {
    }

    public static int d(String tag, String msg) {
        return print("D", tag, msg);
    }

    public static int i(String tag, String msg) {
        return print("I", tag, msg);
    }

    public static int w(String tag, String msg) {
        return print("W", tag, msg);
    }

    public static int w(String tag, String msg, Throwable t) {
        return print("W", tag, msg + " " + t);
    }

    public static int e(String tag, String msg) {
        return print("E", tag, msg);
    }

    public static int e(String tag, String msg, Throwable t) {
        return print("E", tag, msg + " " + t);
    }

    private static int print(String level, String tag, String msg) {
        System.out.println("  [" + level + "/" + tag + "] " + msg);
        return 0;
    }
}
