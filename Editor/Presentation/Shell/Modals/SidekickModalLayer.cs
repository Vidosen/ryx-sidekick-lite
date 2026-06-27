// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell.Modals
{
    internal enum SidekickModalDismissType
    {
        Manual,
        OutsideClick,
        Keyboard
    }

    internal readonly struct SidekickModalOptions
    {
        public SidekickModalOptions(bool outsideClickDismiss, bool keyboardDismiss, string contentWrapperClass = null)
        {
            OutsideClickDismiss = outsideClickDismiss;
            KeyboardDismiss = keyboardDismiss;
            ContentWrapperClass = contentWrapperClass;
        }

        public bool OutsideClickDismiss { get; }
        public bool KeyboardDismiss { get; }
        public string ContentWrapperClass { get; }
    }

    internal sealed class SidekickModalHandle
    {
        private readonly SidekickModalLayer _layer;
        private bool _dismissed;

        internal SidekickModalHandle(SidekickModalLayer layer, bool keyboardDismiss)
        {
            _layer = layer;
            KeyboardDismiss = keyboardDismiss;
        }

        internal event Action<SidekickModalDismissType> Dismissed;

        internal bool IsOpen => !_dismissed;

        internal bool KeyboardDismiss { get; }

        internal void Dismiss(SidekickModalDismissType type = SidekickModalDismissType.Manual)
        {
            if (_dismissed) return;
            _layer.DismissInternal(this, type);
        }

        /// <summary>
        /// Swaps the content of an already-open modal in place, without re-presenting it
        /// (no enter/exit animation). Lets a ViewModel re-render the same modal — e.g. after an
        /// async data refresh — without the entrance animation replaying.
        /// </summary>
        internal void ReplaceContent(VisualElement newContent)
        {
            if (_dismissed) return;
            _layer.ReplaceContentInternal(this, newContent);
        }

        internal void MarkDismissed(SidekickModalDismissType type)
        {
            if (_dismissed) return;
            _dismissed = true;
            Dismissed?.Invoke(type);
        }
    }

    /// <summary>
    /// Plain UI Toolkit modal layer. Replaces App UI <c>Modal</c> throughout the plugin.
    /// Hosts an absolute-positioned overlay root that stacks scrim+content pairs; supports
    /// outside-click and ESC dismissal per entry; works in any VisualElement host (chat window
    /// or Project Settings page) without requiring an App UI Panel.
    /// </summary>
    internal sealed class SidekickModalLayer : IDisposable
    {
        private readonly VisualElement _hostRoot;
        private readonly VisualElement _overlayRoot;
        private readonly EventCallback<KeyDownEvent> _keyDownHandler;

        private readonly struct Entry
        {
            public Entry(SidekickModalHandle handle, VisualElement scrim, EventCallback<PointerDownEvent> pointerCallback)
            {
                Handle = handle;
                Scrim = scrim;
                PointerCallback = pointerCallback;
            }

            public SidekickModalHandle Handle { get; }
            public VisualElement Scrim { get; }
            public EventCallback<PointerDownEvent> PointerCallback { get; }
        }

        private readonly List<Entry> _stack = new List<Entry>();
        private bool _disposed;

        public SidekickModalLayer(VisualElement hostRoot)
        {
            _hostRoot = hostRoot;

            _overlayRoot = new VisualElement();
            _overlayRoot.name = "sk-modal-layer-overlay";
            // Carries the design-token block (see SidekickWindow.uss `:root, .sk-modal-layer-overlay`)
            // so var(--sk-*) resolves even when this layer hosts in a non-Sidekick root (Settings),
            // where :root matches the editor window root — an ancestor outside this overlay's scope.
            _overlayRoot.AddToClassList("sk-modal-layer-overlay");
            _overlayRoot.focusable = true;
            _overlayRoot.pickingMode = PickingMode.Ignore;
            _overlayRoot.style.position = Position.Absolute;
            _overlayRoot.style.left = 0;
            _overlayRoot.style.top = 0;
            _overlayRoot.style.right = 0;
            _overlayRoot.style.bottom = 0;
            _overlayRoot.style.display = DisplayStyle.None;

            // Load all Sidekick stylesheets so .sk-* content renders correctly in any host
            // (especially Settings pages that don't inherit from SidekickWindow.uxml).
            LoadStyleSheets();

            _keyDownHandler = OnKeyDown;
            _overlayRoot.RegisterCallback(_keyDownHandler, TrickleDown.TrickleDown);

            hostRoot.Add(_overlayRoot);
        }

        private void LoadStyleSheets()
        {
            var mainUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                Ryx.Sidekick.Editor.Presentation.Constants.SidekickUiConstants.MainWindowUss);
            if (mainUss != null && !_overlayRoot.styleSheets.Contains(mainUss))
                _overlayRoot.styleSheets.Add(mainUss);

            foreach (var path in Ryx.Sidekick.Editor.Presentation.Constants.SidekickUiConstants.MainWindowPartialUssPaths)
            {
                var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (uss != null && !_overlayRoot.styleSheets.Contains(uss))
                    _overlayRoot.styleSheets.Add(uss);
            }
        }

        // Enter/exit animation duration; must match the USS transition on .sk-modal-scrim /
        // .sk-modal-content (0.18s). Drives the deferred scrim removal on exit.
        private const long AnimationMs = 180;

        // Animations only run against a live panel (the real editor window / settings page).
        // In EditMode tests the host has no panel, so Show/Dismiss act synchronously and the
        // synchronous contract (immediate scrim removal / overlay hide) the tests assert is kept.
        private bool IsLive => _overlayRoot.panel != null;

        internal SidekickModalHandle Show(VisualElement content, SidekickModalOptions options)
        {
            ThrowIfDisposed();

            // Self-heal: the host's children may have been cleared after this layer was
            // constructed (e.g. SidekickWindowView.TryCreate calls ContentContainer.Clear()
            // AFTER SidekickWindowPresenter created the layer over that same container, which
            // detaches _overlayRoot). Re-attach so the overlay is a live child before showing,
            // otherwise the scrim would be added to an off-tree element and never render.
            if (_overlayRoot.parent != _hostRoot)
            {
                _hostRoot.Add(_overlayRoot);
            }

            var handle = new SidekickModalHandle(this, options.KeyboardDismiss);

            var scrim = new VisualElement();
            scrim.AddToClassList("sk-modal-scrim");
            // Start in the closed (transparent) state; the enter animation removes it below.
            scrim.AddToClassList("sk-modal-scrim--closed");
            scrim.pickingMode = PickingMode.Position;

            var wrapper = new VisualElement();
            wrapper.AddToClassList("sk-modal-content");
            if (!string.IsNullOrEmpty(options.ContentWrapperClass))
                wrapper.AddToClassList(options.ContentWrapperClass);
            wrapper.Add(content);
            scrim.Add(wrapper);

            EventCallback<PointerDownEvent> pointerCallback = null;
            if (options.OutsideClickDismiss)
            {
                pointerCallback = evt =>
                {
                    if (evt.target == scrim)
                        handle.Dismiss(SidekickModalDismissType.OutsideClick);
                };
                scrim.RegisterCallback(pointerCallback);
            }

            _stack.Add(new Entry(handle, scrim, pointerCallback));
            _overlayRoot.Add(scrim);
            _overlayRoot.style.display = DisplayStyle.Flex;
            _overlayRoot.pickingMode = PickingMode.Position;

            // The overlay root may have been added to the host BEFORE other siblings
            // (e.g. the main window UXML cloned into the same container after layer
            // construction). In UI Toolkit later siblings paint on top, which would
            // render the scrim BEHIND the chat content. BringToFront makes the overlay
            // the topmost child of its host whenever a modal opens, regardless of when
            // other siblings were added.
            _overlayRoot.BringToFront();
            _overlayRoot.Focus();

            // Animate the backdrop/card in by clearing the --closed start state. On a live panel
            // defer one frame so the USS transition runs; in tests (no panel) settle immediately.
            if (IsLive)
                scrim.schedule.Execute(() => scrim.RemoveFromClassList("sk-modal-scrim--closed")).StartingIn(16);
            else
                scrim.RemoveFromClassList("sk-modal-scrim--closed");

            return handle;
        }

        internal void DismissInternal(SidekickModalHandle handle, SidekickModalDismissType type)
        {
            var index = -1;
            for (var i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Handle == handle)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0) return;

            var entry = _stack[index];
            _stack.RemoveAt(index);

            if (entry.PointerCallback != null)
                entry.Scrim.UnregisterCallback(entry.PointerCallback);

            if (IsLive)
            {
                // Animate the scrim out, then remove it. Re-check the stack in the deferred
                // callback so a modal opened during the fade-out keeps the overlay visible.
                var scrim = entry.Scrim;
                // Stop the dying scrim from intercepting input during its ~AnimationMs fade-out, so a
                // chained modal opened from a Dismissed handler (e.g. Confirm "Set Up Now" -> Onboarding)
                // receives clicks immediately. MarkDismissed now fires synchronously (below), so without
                // this the fading scrim would still be hit-testable on top of nothing for that window.
                scrim.pickingMode = PickingMode.Ignore;
                scrim.AddToClassList("sk-modal-scrim--closed");
                scrim.schedule.Execute(() =>
                {
                    scrim.RemoveFromHierarchy();
                    if (!_disposed && _stack.Count == 0)
                    {
                        _overlayRoot.style.display = DisplayStyle.None;
                        _overlayRoot.pickingMode = PickingMode.Ignore;
                    }
                }).StartingIn(AnimationMs);

                // Keep the overlay visible during the fade; only refocus when others remain.
                if (_stack.Count > 0)
                    _overlayRoot.Focus();
            }
            else
            {
                entry.Scrim.RemoveFromHierarchy();
                if (_stack.Count == 0)
                {
                    _overlayRoot.style.display = DisplayStyle.None;
                    _overlayRoot.pickingMode = PickingMode.Ignore;
                }
                else
                {
                    _overlayRoot.Focus();
                }
            }

            // Fire synchronously so handle.IsOpen / consumers update immediately; the scrim's
            // visual exit (live panel) completes asynchronously above.
            handle.MarkDismissed(type);
        }

        internal void ReplaceContentInternal(SidekickModalHandle handle, VisualElement newContent)
        {
            for (var i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Handle != handle)
                    continue;

                // The scrim's single child is the content wrapper (.sk-modal-content); swap its
                // content without touching the scrim, so no enter/exit transition is triggered.
                var wrapper = _stack[i].Scrim.Q(className: "sk-modal-content");
                if (wrapper != null)
                {
                    wrapper.Clear();
                    if (newContent != null)
                        wrapper.Add(newContent);
                }
                return;
            }
        }

        internal void DismissAll()
        {
            // Iterate a copy since DismissInternal modifies _stack.
            var copy = new List<Entry>(_stack);
            foreach (var entry in copy)
            {
                if (entry.Handle.IsOpen)
                    entry.Handle.Dismiss(SidekickModalDismissType.Manual);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            DismissAll();
            _overlayRoot.UnregisterCallback(_keyDownHandler, TrickleDown.TrickleDown);
            _overlayRoot.RemoveFromHierarchy();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_stack.Count == 0) return;
            var top = _stack[_stack.Count - 1];
            if (top.Handle.KeyboardDismiss)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    top.Handle.Dismiss(SidekickModalDismissType.Keyboard);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SidekickModalLayer));
        }
    }
}
