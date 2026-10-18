using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    public enum Ease
    {
        Linear = 0,
        OutCubic = 1,
        InCubic = 2,
        /// <summary>Spring-like ease with a small overshoot (spec §4.4: spring transitions 250–350 ms).</summary>
        Spring = 3,
    }

    /// <summary>
    /// Minimal per-frame tween on an element's scheduler. Under "Reduce motion" (spec §4.8) every tween completes
    /// instantly. Independent of USS transitions so durations, interruption and completion are deterministic.
    /// </summary>
    public sealed class Tween
    {
        /// <summary>Global switch set by the shell from the resolved appearance.</summary>
        public static bool ReduceMotion { get; set; }

        public const float DefaultDuration = 0.3f;

        IVisualElementScheduledItem _item;
        readonly Action<float> _step;
        readonly Action _done;
        readonly float _duration;
        readonly Ease _ease;
        float _start;
        bool _finished;

        Tween(Action<float> step, Action done, float duration, Ease ease)
        {
            _step = step;
            _done = done;
            _duration = duration;
            _ease = ease;
        }

        public bool IsRunning => !_finished;

        /// <summary>Runs <paramref name="step"/> with eased progress 0→1 every frame, then <paramref name="done"/>.</summary>
        public static Tween Run(VisualElement owner, float duration, Ease ease, Action<float> step, Action done = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var t = new Tween(step, done, duration, ease);
            if (ReduceMotion || duration <= 0f || owner.panel == null)
            {
                t.Finish();
                return t;
            }
            t._start = Time.unscaledTime;
            t._step?.Invoke(0f);
            t._item = owner.schedule.Execute(t.Update).Every(0);
            return t;
        }

        /// <summary>Stops without calling the completion callback.</summary>
        public void Cancel()
        {
            _finished = true;
            _item?.Pause();
        }

        /// <summary>Jumps to the end and completes.</summary>
        public void Complete()
        {
            if (_finished) return;
            _item?.Pause();
            Finish();
        }

        void Update(TimerState state)
        {
            if (_finished) return;
            float t = Mathf.Clamp01((Time.unscaledTime - _start) / _duration);
            if (t >= 1f)
            {
                _item.Pause();
                Finish();
                return;
            }
            _step?.Invoke(Evaluate(_ease, t));
        }

        void Finish()
        {
            _finished = true;
            _step?.Invoke(1f);
            _done?.Invoke();
        }

        public static float Evaluate(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.OutCubic:
                {
                    float u = 1f - t;
                    return 1f - u * u * u;
                }
                case Ease.InCubic:
                    return t * t * t;
                case Ease.Spring:
                {
                    const float c1 = 1.2f;
                    const float c3 = c1 + 1f;
                    float u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }
                default:
                    return t;
            }
        }
    }
}
