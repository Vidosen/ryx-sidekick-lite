// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Presenters;
using Ryx.Sidekick.Editor.Presentation.Shell;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    internal sealed class SidekickWindow : EditorWindow
    {
        private const string WindowTitle = "Sidekick";

        [SerializeField] private string _hostToken;

        private SidekickEditorAppHost _appHost;
        private SidekickWindowPresenter _presenter;
        private ISidekickWindowHost _windowHost;

        [MenuItem(SidekickAppConstants.MenuItems.WindowSidekick)]
        public static void ShowWindow()
        {
            var window = GetWindow<SidekickWindow>();
            var windowIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(SidekickUiConstants.ToolbarIconPath);
            window.titleContent = new GUIContent(WindowTitle, windowIcon);
            window.minSize = new Vector2(450, 600);
        }

        private void OnEnable()
        {
            _appHost = new SidekickEditorAppHost(
                () => _hostToken,
                value => _hostToken = value);
            _windowHost = _appHost;
            SidekickWindowHostRegistry.Register(_windowHost);
            _presenter = new SidekickWindowPresenter(rootVisualElement, _appHost);
            _windowHost.Initialize();
        }

        private void CreateGUI()
        {
            _presenter?.CreateGUI();
        }

        private void OnDisable()
        {
            if (_windowHost != null)
            {
                DomainReloadAutoResume.SaveInputFieldState(_windowHost);
            }

            _presenter?.Dispose();
            _presenter = null;

            if (_windowHost != null)
            {
                SidekickWindowHostRegistry.Unregister(_windowHost);
            }

            _appHost?.Dispose();
            _windowHost = null;
            _appHost = null;
        }

        private void OnFocus()
        {
            _windowHost?.OnFocus();
        }
    }
}
