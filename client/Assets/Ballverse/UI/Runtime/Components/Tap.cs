using System;
using Ballverse.UI.Accessibility;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    /// <summary>
    /// Tap handling for plain elements (buttons in our layouts are <see cref="VisualElement"/>s styled by USS, so the
    /// default theme's button styles never interfere). Uses the engine <see cref="Clickable"/> manipulator, which also
    /// drives the <c>:active</c> pseudo state used for pressed visuals.
    /// </summary>
    public static class Tap
    {
        /// <summary>Optional haptic tick for taps (wired by the app to the haptics service and setting).</summary>
        public static Action Haptic { get; set; }

        /// <param name="pressedStyle">Adds the pressed (scale) visual; off for whole rows that are a larger hit area.</param>
        public static Clickable On(VisualElement element, Action action, bool pressedStyle = true)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            var c = new Clickable(() =>
            {
                if (!element.enabledInHierarchy) return;
                Haptic?.Invoke();
                action?.Invoke();
            });
            element.AddManipulator(c);
            if (pressedStyle) element.AddToClassList("bv-tappable");
            // Screen readers announce and activate it like a button (spec §4.8).
            A11y.Button(element, () =>
            {
                if (element.enabledInHierarchy) action?.Invoke();
            });
            return c;
        }

        /// <summary><see cref="On"/> for a named child; throws when the layout lacks it.</summary>
        public static Clickable On(VisualElement root, string name, Action action)
        {
            return On(UiAssets.Q<VisualElement>(root, name), action);
        }

        public static void SetEnabled(VisualElement element, bool enabled)
        {
            element.SetEnabled(enabled);
        }
    }

    /// <summary>
    /// Tap and long press on one element (spec §4.6: long press on 🎲 and on cards). A press longer than
    /// <see cref="Duration"/> without moving more than <see cref="MoveTolerance"/> fires the long press and suppresses
    /// the tap.
    /// </summary>
    public sealed class TapOrLongPress : PointerManipulator
    {
        public const float Duration = 0.45f;
        public const float MoveTolerance = 12f;

        readonly Action _tap;
        readonly Action _longPress;
        IVisualElementScheduledItem _timer;
        int _pointerId = -1;
        Vector2 _start;
        bool _longFired;

        public TapOrLongPress(Action tap, Action longPress)
        {
            _tap = tap;
            _longPress = longPress;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerUpEvent>(OnUp);
            target.RegisterCallback<PointerCancelEvent>(OnCancel);
            target.RegisterCallback<PointerLeaveEvent>(OnLeave);
            target.AddToClassList("bv-tappable");
            // Screen readers activate the tap action; the long-press action stays a touch gesture.
            A11y.Button(target, () => _tap?.Invoke());
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<PointerCancelEvent>(OnCancel);
            target.UnregisterCallback<PointerLeaveEvent>(OnLeave);
        }

        void OnDown(PointerDownEvent e)
        {
            if (_pointerId >= 0 || !target.enabledInHierarchy) return;
            // No pointer capture and no StopPropagation: a parent ScrollView must still be able to start a touch
            // scroll from this element (cards in grids); moving beyond the tolerance cancels the press.
            _pointerId = e.pointerId;
            _start = e.position;
            _longFired = false;
            target.AddToClassList("bv-pressed");
            _timer = target.schedule.Execute(FireLong).StartingIn((long)(Duration * 1000));
        }

        void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != _pointerId) return;
            if (((Vector2)e.position - _start).sqrMagnitude > MoveTolerance * MoveTolerance) Reset();
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != _pointerId) return;
            bool inside = target.ContainsPoint(target.WorldToLocal(e.position));
            bool fireTap = !_longFired && inside;
            Reset();
            if (fireTap)
            {
                Tap.Haptic?.Invoke();
                _tap?.Invoke();
            }
        }

        void OnCancel(PointerCancelEvent e)
        {
            if (e.pointerId == _pointerId) Reset();
        }

        void OnLeave(PointerLeaveEvent e)
        {
            if (e.pointerId == _pointerId) Reset();
        }

        void FireLong()
        {
            if (_pointerId < 0) return;
            _longFired = true;
            target.RemoveFromClassList("bv-pressed");
            Tap.Haptic?.Invoke();
            _longPress?.Invoke();
        }

        void Reset()
        {
            _timer?.Pause();
            _timer = null;
            _pointerId = -1;
            target.RemoveFromClassList("bv-pressed");
        }
    }
}
