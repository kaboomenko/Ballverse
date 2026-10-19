using System;
using UnityEngine;

namespace Ballverse.Services
{
    /// <summary><see cref="IServiceLog"/> over the Unity console. Info lines are dropped in release builds.</summary>
    public sealed class UnityServiceLog : IServiceLog
    {
        readonly bool _verbose;

        public UnityServiceLog(bool verbose)
        {
            _verbose = verbose;
        }

        public void Info(string tag, string message)
        {
            if (_verbose) Debug.Log("[Ballverse." + tag + "] " + message);
        }

        public void Warning(string tag, string message)
        {
            Debug.LogWarning("[Ballverse." + tag + "] " + message);
        }

        public void Error(string tag, string message, Exception exception = null)
        {
            Debug.LogError("[Ballverse." + tag + "] " + message);
            if (exception != null) Debug.LogException(exception);
        }
    }
}
