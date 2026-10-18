using System;

namespace Ballverse.App
{
    /// <summary>Raises events so that one throwing subscriber is reported and does not stop the others.</summary>
    static class AppEvents
    {
        public static void Raise(Action handler, Action<string, Exception> reportError, string context)
        {
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception e)
                {
                    reportError(context, e);
                }
            }
        }

        public static void Raise<T>(Action<T> handler, T arg, Action<string, Exception> reportError, string context)
        {
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(arg);
                }
                catch (Exception e)
                {
                    reportError(context, e);
                }
            }
        }
    }
}
