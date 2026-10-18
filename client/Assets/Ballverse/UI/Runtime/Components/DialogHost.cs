using System;
using System.Collections.Generic;
using Ballverse.UI.Localization;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    public enum DialogButtonStyle
    {
        Primary = 0,
        Secondary = 1,
        Destructive = 2,
        /// <summary>Text-only button (e.g. "Отмена").</summary>
        Plain = 3,
    }

    public sealed class DialogButton
    {
        public LocalizedText Label { get; }
        public DialogButtonStyle Style { get; }
        /// <summary>Invoked after the dialog closed. With a text field the entered text is passed.</summary>
        public Action<string> Action { get; }
        /// <summary>Keep the dialog open after the action (e.g. validation failed); the action closes it itself.</summary>
        public bool KeepOpen { get; }

        public DialogButton(LocalizedText label, DialogButtonStyle style, Action<string> action, bool keepOpen = false)
        {
            Label = label;
            Style = style;
            Action = action;
            KeepOpen = keepOpen;
        }

        public static DialogButton Key(string key, DialogButtonStyle style, Action action)
        {
            return new DialogButton(LocalizedText.FromKey(key), style, _ => action?.Invoke());
        }
    }

    public sealed class DialogSpec
    {
        public LocalizedText Title;
        public LocalizedText Body;
        public IconKind Icon = IconKind.None;
        public readonly List<DialogButton> Buttons = new List<DialogButton>();
        /// <summary>Back / scrim tap closes and invokes <see cref="Cancelled"/>.</summary>
        public bool Cancellable = true;
        public Action Cancelled;
        /// <summary>Shows a single-line text field when not null (initial value).</summary>
        public string TextFieldValue;
        public int TextFieldMaxLength = 32;
        /// <summary>Extra content under the body (e.g. a spinner row).</summary>
        public VisualElement Extra;
    }

    /// <summary>Open dialog; <see cref="Close"/> removes it without invoking callbacks.</summary>
    public sealed class DialogHandle
    {
        internal VisualElement Root;
        internal DialogSpec Spec;
        internal DialogHost Host;
        internal TextField Field;

        public bool IsOpen => Root != null && Root.parent != null;
        public string Text => Field?.value;

        public void Close()
        {
            Host?.Remove(this);
        }

        /// <summary>Enables or disables all buttons (e.g. while waiting for an ad).</summary>
        public void SetButtonsEnabled(bool enabled)
        {
            Root?.Query<VisualElement>(className: "bv-dialog__button").ForEach(b => b.SetEnabled(enabled));
        }
    }

    /// <summary>Modal dialogs on a solid surface (spec §4.5: prices and purchase buttons are never on glass).</summary>
    public sealed class DialogHost
    {
        readonly UiContext _ctx;
        readonly VisualElement _layer;
        readonly List<DialogHandle> _open = new List<DialogHandle>();

        public DialogHost(UiContext ctx, VisualElement layer)
        {
            _ctx = ctx;
            _layer = layer;
            _layer.pickingMode = PickingMode.Ignore;
        }

        public bool AnyOpen => _open.Count > 0;

        public DialogHandle Show(DialogSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            var handle = new DialogHandle { Spec = spec, Host = this };
            var root = new VisualElement();
            root.AddToClassList("bv-dialog-root");
            root.AddToClassList("bv-fill");
            var scrim = new VisualElement();
            scrim.AddToClassList("bv-dialog-scrim");
            scrim.AddToClassList("bv-fill");
            scrim.RegisterCallback<PointerDownEvent>(e =>
            {
                if (spec.Cancellable) Cancel(handle);
            });
            root.Add(scrim);

            var card = new VisualElement();
            card.AddToClassList("bv-dialog");
            card.AddToClassList("bv-surface-solid");
            if (spec.Icon != IconKind.None) card.Add(Icons.Create(spec.Icon, "bv-dialog__icon"));
            if (!spec.Title.IsEmpty) card.Add(UiContext.TextLabel(_ctx.L(spec.Title), "bv-dialog__title"));
            if (!spec.Body.IsEmpty) card.Add(UiContext.TextLabel(_ctx.L(spec.Body), "bv-dialog__body"));
            if (spec.TextFieldValue != null)
            {
                var field = new TextField { value = spec.TextFieldValue, maxLength = spec.TextFieldMaxLength, isDelayed = false };
                field.AddToClassList("bv-textfield");
                card.Add(field);
                handle.Field = field;
            }
            if (spec.Extra != null) card.Add(spec.Extra);

            var buttons = new VisualElement();
            buttons.AddToClassList("bv-dialog__buttons");
            foreach (var b in spec.Buttons)
            {
                var button = new VisualElement();
                button.AddToClassList("bv-dialog__button");
                button.AddToClassList(ButtonClass(b.Style));
                button.Add(UiContext.TextLabel(_ctx.L(b.Label), "bv-btn__label"));
                var captured = b;
                Tap.On(button, () =>
                {
                    string text = handle.Field?.value;
                    if (!captured.KeepOpen) Remove(handle);
                    captured.Action?.Invoke(text);
                });
                buttons.Add(button);
            }
            card.Add(buttons);
            root.Add(card);
            handle.Root = root;
            _ctx.Prepare(root);
            _layer.Add(root);
            _open.Add(handle);
            root.style.opacity = 0f;
            Tween.Run(root, 0.18f, Ease.OutCubic, k => root.style.opacity = k);
            return handle;
        }

        /// <summary>Simple confirmation: title, body, confirm and cancel buttons.</summary>
        public DialogHandle Confirm(string titleKey, string bodyKey, string confirmKey, bool destructive, Action onConfirm,
            Action onCancel = null)
        {
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey(titleKey),
                Body = bodyKey != null ? LocalizedText.FromKey(bodyKey) : default,
                Cancelled = onCancel,
            };
            spec.Buttons.Add(DialogButton.Key(confirmKey, destructive ? DialogButtonStyle.Destructive : DialogButtonStyle.Primary, onConfirm));
            spec.Buttons.Add(DialogButton.Key("common.cancel", DialogButtonStyle.Plain, onCancel));
            return Show(spec);
        }

        /// <summary>Closes the top cancellable dialog (system back). Returns true when a dialog consumed the back action.</summary>
        public bool CloseTop()
        {
            if (_open.Count == 0) return false;
            var top = _open[_open.Count - 1];
            if (top.Spec.Cancellable) Cancel(top);
            return true;
        }

        void Cancel(DialogHandle handle)
        {
            Remove(handle);
            handle.Spec.Cancelled?.Invoke();
        }

        internal void Remove(DialogHandle handle)
        {
            if (!_open.Remove(handle)) return;
            handle.Root?.RemoveFromHierarchy();
        }

        static string ButtonClass(DialogButtonStyle style)
        {
            switch (style)
            {
                case DialogButtonStyle.Primary: return "bv-btn--primary";
                case DialogButtonStyle.Destructive: return "bv-btn--destructive";
                case DialogButtonStyle.Plain: return "bv-btn--plain";
                default: return "bv-btn--secondary";
            }
        }
    }

    /// <summary>Short messages at the bottom of the screen, one at a time.</summary>
    public sealed class ToastHost
    {
        const float DefaultSeconds = 2.5f;
        /// <summary>Spec §5.3.2 IN8: a short "Ad" plaque instead of a countdown before an interstitial.</summary>
        public const float AdNoticeSeconds = 0.5f;

        readonly VisualElement _layer;
        readonly Queue<KeyValuePair<string, float>> _queue = new Queue<KeyValuePair<string, float>>();
        VisualElement _current;

        public ToastHost(VisualElement layer)
        {
            _layer = layer;
            _layer.pickingMode = PickingMode.Ignore;
        }

        public void Show(string text, float seconds = DefaultSeconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            _queue.Enqueue(new KeyValuePair<string, float>(text, seconds));
            if (_current == null) Next();
        }

        /// <summary>Centered plaque shown right before an interstitial; <paramref name="shown"/> fires when it is gone.</summary>
        public void ShowAdNotice(string text, Action shown)
        {
            var plaque = new VisualElement();
            plaque.AddToClassList("bv-ad-notice");
            plaque.pickingMode = PickingMode.Ignore;
            plaque.Add(UiContext.TextLabel(text, "bv-ad-notice__label"));
            _layer.Add(plaque);
            plaque.schedule.Execute(() =>
            {
                plaque.RemoveFromHierarchy();
                shown?.Invoke();
            }).StartingIn((long)(AdNoticeSeconds * 1000));
        }

        void Next()
        {
            if (_queue.Count == 0)
            {
                _current = null;
                return;
            }
            var item = _queue.Dequeue();
            var toast = new VisualElement();
            toast.AddToClassList("bv-toast");
            toast.pickingMode = PickingMode.Ignore;
            toast.Add(UiContext.TextLabel(item.Key, "bv-toast__label"));
            _layer.Add(toast);
            _current = toast;
            toast.style.opacity = 0f;
            Tween.Run(toast, 0.2f, Ease.OutCubic, k => toast.style.opacity = k);
            toast.schedule.Execute(() =>
            {
                Tween.Run(toast, 0.2f, Ease.InCubic, k => toast.style.opacity = 1f - k, () =>
                {
                    toast.RemoveFromHierarchy();
                    Next();
                });
            }).StartingIn((long)(item.Value * 1000));
        }
    }
}
