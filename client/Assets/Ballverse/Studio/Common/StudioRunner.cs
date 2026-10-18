using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// Main-thread driver of the studio runtime: raises <see cref="Tick"/> every Unity frame (preview playback, autosave, export
    /// frame loop) and completes <see cref="NextFrame"/> awaiters. One per app (<see cref="StudioServices"/> creates it, survives
    /// scene loads). All studio work happens on the main thread except the audio mix and file writes, which the components move
    /// to the thread pool themselves.
    /// </summary>
    public sealed class StudioRunner : MonoBehaviour
    {
        readonly List<TaskCompletionSource<bool>> _waiters = new List<TaskCompletionSource<bool>>();
        readonly List<TaskCompletionSource<bool>> _firing = new List<TaskCompletionSource<bool>>();

        /// <summary>Every Update with the unscaled frame time in seconds.</summary>
        public event Action<float> Tick;

        /// <summary>Every LateUpdate (after UI layout, before rendering).</summary>
        public event Action LateTick;

        /// <summary>Frame counter of this runner.</summary>
        public int FrameCount { get; private set; }

        public static StudioRunner Create(string name = "BallverseStudio")
        {
            var go = new GameObject(name);
            DontDestroyOnLoad(go);
            return go.AddComponent<StudioRunner>();
        }

        /// <summary>Completes in the next Update (main thread).</summary>
        public Task NextFrame()
        {
            var tcs = new TaskCompletionSource<bool>();
            _waiters.Add(tcs);
            return tcs.Task;
        }

        void Update()
        {
            FrameCount++;
            if (_waiters.Count > 0)
            {
                _firing.AddRange(_waiters);
                _waiters.Clear();
                foreach (TaskCompletionSource<bool> t in _firing) t.TrySetResult(true);
                _firing.Clear();
            }
            try
            {
                Tick?.Invoke(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void LateUpdate()
        {
            try
            {
                LateTick?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OnDestroy()
        {
            foreach (TaskCompletionSource<bool> t in _waiters) t.TrySetCanceled();
            _waiters.Clear();
        }
    }
}
