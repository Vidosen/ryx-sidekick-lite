// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Dialogs
{
    /// <summary>
    /// Simple input dialog for the Unity Editor.
    /// </summary>
    internal class EditorInputDialog : EditorWindow
    {
        private string _title;
        private string _message;
        private string _value;
        private bool _confirmed;
        private bool _initialized;

        /// <summary>
        /// Shows a simple input dialog and returns the entered value, or null if cancelled.
        /// </summary>
        public static string Show(string title, string message, string defaultValue = "")
        {
            var window = CreateInstance<EditorInputDialog>();
            window._title = title;
            window._message = message;
            window._value = defaultValue;
            window._confirmed = false;
            window._initialized = true;
            
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(400, 120);
            window.maxSize = new Vector2(600, 150);
            
            // Center on screen
            var pos = window.position;
            pos.center = new Vector2(Screen.currentResolution.width / 2f, Screen.currentResolution.height / 2f);
            window.position = pos;
            
            window.ShowModalUtility();
            
            return window._confirmed ? window._value : null;
        }

        private void OnGUI()
        {
            if (!_initialized) return;
            
            EditorGUILayout.Space(10);
            
            // Message
            EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.Space(5);
            
            // Input field
            GUI.SetNextControlName("InputField");
            _value = EditorGUILayout.TextField(_value);
            
            // Focus input field on first frame
            if (Event.current.type == EventType.Repaint && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
            {
                EditorGUI.FocusTextInControl("InputField");
            }
            
            EditorGUILayout.Space(10);
            
            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                _confirmed = false;
                Close();
            }
            
            if (GUILayout.Button("OK", GUILayout.Width(80)) || 
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
            {
                _confirmed = true;
                Close();
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Handle escape key
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                _confirmed = false;
                Close();
            }
        }
    }
}



