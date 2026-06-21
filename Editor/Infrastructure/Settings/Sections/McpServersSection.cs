// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Infrastructure.Mcp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>The "Servers" block: branded collapsible server cards, add/remove buttons, arg/env/header
    /// editors. Extracted from SidekickMcpSettingsProvider. Greyed out when UseCustomMcpConfig is on.</summary>
    internal sealed class McpServersSection : IMcpSettingsSection
    {
        private static readonly List<string> TransportChoices = new List<string> { "http", "stdio" };

        // Expanded-card state keyed by entry.id, preserved across ctx.RequestRebuild (full-page re-render).
        // internal so a test can verify persistence; static so it survives the content.Clear() on rebuild.
        internal static readonly HashSet<string> _expanded = new HashSet<string>();

        // JSON / Form mode toggle — internal so tests can set it directly.
        internal static bool _jsonMode;

        public string Id => "mcp-servers";
        public int Order => 20;

        // Initial-state class for the panel fade/slide-in transition (see SidekickSettings.Mcp.uss).
        private const string PanelEnterClass = "sk-mcpset-panel--enter";

        public VisualElement Build(McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;
            var root = new VisualElement();
            root.AddToClassList("sk-mcpset-group");

            root.Add(SidekickSettingsSectionBuilder.SectionHeader("Servers"));

            // Form | JSON segmented control. Switching only toggles panel visibility (no full-page
            // rebuild) so the swap can animate; only data changes (add/remove/Apply) rebuild the page.
            var tabBar = SidekickSettingsSectionBuilder.HorizontalRow();
            tabBar.AddToClassList("sk-settings-tabbar");

            // Sliding "pill" that morphs to the selected tab. Added first so it sits behind the
            // button labels; its position/size are driven from the selected tab's layout rect.
            var tabIndicator = new VisualElement { pickingMode = PickingMode.Ignore };
            tabIndicator.AddToClassList("sk-settings-tab-indicator");
            tabBar.Add(tabIndicator);

            var formTab = new Button { text = "Form" };
            formTab.AddToClassList("sk-settings-tab");
            var jsonTab = new Button { text = "JSON" };
            jsonTab.AddToClassList("sk-settings-tab");
            tabBar.Add(formTab);
            tabBar.Add(jsonTab);
            root.Add(tabBar);

            void UpdateTabIndicator()
            {
                var sel = _jsonMode ? jsonTab : formTab;
                var r = sel.layout;
                if (float.IsNaN(r.width) || r.width <= 0f) return; // layout not ready yet
                tabIndicator.style.left = r.x;
                tabIndicator.style.top = r.y;
                tabIndicator.style.width = r.width;
                tabIndicator.style.height = r.height;
                // Enable the slide transition only after the first placement, so the pill doesn't
                // animate in from (0,0) on the initial layout pass.
                if (!tabIndicator.ClassListContains("sk-settings-tab-indicator--anim"))
                    tabIndicator.schedule.Execute(
                        () => tabIndicator.AddToClassList("sk-settings-tab-indicator--anim")).StartingIn(40);
            }
            tabBar.RegisterCallback<GeometryChangedEvent>(_ => UpdateTabIndicator());

            // Both panels are built up-front and persist; only their visibility toggles on switch.
            var formPanel = new VisualElement();
            formPanel.AddToClassList("sk-mcpset-panel");
            BuildFormPanel(formPanel, ctx);
            root.Add(formPanel);

            var jsonPanel = new VisualElement();
            jsonPanel.AddToClassList("sk-mcpset-panel");
            var editor = BuildJsonEditor(jsonPanel, ctx);
            root.Add(jsonPanel);

            void SetMode(bool json, bool animate)
            {
                _jsonMode = json;
                formTab.EnableInClassList("sk-settings-tab--selected", !json);
                jsonTab.EnableInClassList("sk-settings-tab--selected", json);
                UpdateTabIndicator();

                var incoming = json ? jsonPanel : formPanel;
                var outgoing = json ? formPanel : jsonPanel;
                outgoing.style.display = DisplayStyle.None;
                incoming.style.display = DisplayStyle.Flex;

                // Refresh the JSON from the live list when entering JSON (the form may have edited it
                // since this section was built). Setting value re-runs the editor's validation callback.
                if (json)
                    editor.value = McpServerJsonCodec.ToJson(settings.GetMcpServers());

                if (animate)
                {
                    incoming.AddToClassList(PanelEnterClass);
                    incoming.schedule.Execute(() => incoming.RemoveFromClassList(PanelEnterClass)).StartingIn(20);
                }
            }

            formTab.clicked += () => { if (_jsonMode) SetMode(false, animate: true); };
            jsonTab.clicked += () => { if (!_jsonMode) SetMode(true, animate: true); };

            SetMode(_jsonMode, animate: false); // initial state — no entrance animation on (re)build

            if (settings.UseCustomMcpConfig)
                root.SetEnabled(false);

            return root;
        }

        private static void BuildFormPanel(VisualElement panel, McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;

            var serversContainer = new VisualElement();
            panel.Add(serversContainer);

            var addButton = new Button(() =>
            {
                settings.AddMcpServer();
                ctx.RequestRebuild();
            })
            {
                text = "Add Server"
            };
            var addRow = SidekickSettingsSectionBuilder.HorizontalRow();
            addRow.AddToClassList("sk-settings-actions");
            addRow.Add(addButton);
            panel.Add(addRow);

            RebuildServers(serversContainer, ctx);
        }

        private static TextField BuildJsonEditor(VisualElement root, McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;

            var editor = new TextField
            {
                value = McpServerJsonCodec.ToJson(settings.GetMcpServers()),
                multiline = true
            };
            editor.AddToClassList("sk-mcpset-json-editor");
            editor.style.minHeight = 160;
            // PreWrap (not Normal) preserves the formatted indentation/newlines like a code editor —
            // Normal collapses whitespace so the Newtonsoft indents disappear — while still wrapping long lines.
            editor.style.whiteSpace = WhiteSpace.PreWrap;
            root.Add(editor);

            // IDE-style auto-indent: on Enter, suppress the default newline and insert a newline that
            // keeps the current line's indentation (plus one level after a trailing '{' / '['). TrickleDown
            // so this runs before the inner text editor and StopPropagation prevents the default insertion.
            editor.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;

                var text = editor.value ?? string.Empty;
                var caret = Mathf.Clamp(editor.cursorIndex, 0, text.Length);
                var sel = Mathf.Clamp(editor.selectIndex, 0, text.Length);
                var start = Mathf.Min(caret, sel);
                var end = Mathf.Max(caret, sel);

                var insertion = "\n" + ComputeNewlineIndent(text, start);
                editor.value = text.Substring(0, start) + insertion + text.Substring(end);
                editor.cursorIndex = editor.selectIndex = start + insertion.Length;

                evt.StopPropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);

            var errorBox = SidekickSettingsSectionBuilder.Help(string.Empty, HelpBoxMessageType.Error);
            errorBox.style.display = DisplayStyle.None;
            root.Add(errorBox);

            var blankCount = settings.GetMcpServers().Count(s => s != null && string.IsNullOrWhiteSpace(s.name));
            if (blankCount > 0)
            {
                root.Add(SidekickSettingsSectionBuilder.Help(
                    $"{blankCount} unnamed server(s) are not shown here and will be removed if you Apply.",
                    HelpBoxMessageType.Info));
            }

            var applyBtn = new Button { text = "Apply" };
            applyBtn.AddToClassList("sk-mcpset-primary");
            var formatBtn = new Button(() =>
            {
                if (McpServerJsonCodec.TryParse(editor.value, out var parsed, out _))
                    editor.value = McpServerJsonCodec.ToJson(parsed);
            }) { text = "Format" };

            void Validate()
            {
                var ok = McpServerJsonCodec.TryParse(editor.value, out _, out var err);
                if (ok)
                {
                    errorBox.style.display = DisplayStyle.None;
                }
                else
                {
                    errorBox.text = err ?? "Invalid JSON.";
                    errorBox.style.display = DisplayStyle.Flex;
                }
                applyBtn.SetEnabled(ok);
            }

            editor.RegisterValueChangedCallback(_ => Validate());

            applyBtn.clicked += () =>
            {
                if (!McpServerJsonCodec.TryParse(editor.value, out var parsed, out _)) return;

                var current = settings.GetMcpServers();
                var currentNamed = current
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.name))
                    .Select(s => s.name.Trim())
                    .ToHashSet(StringComparer.Ordinal);
                var nextNamed = parsed
                    .Where(s => !string.IsNullOrWhiteSpace(s.name))
                    .Select(s => s.name.Trim())
                    .ToHashSet(StringComparer.Ordinal);
                var dropped = currentNamed.Where(n => !nextNamed.Contains(n)).ToList();
                var unnamed = current.Count(s => s != null && string.IsNullOrWhiteSpace(s.name));

                if (dropped.Count > 0 || unnamed > 0)
                {
                    var msg = $"Replace {currentNamed.Count + unnamed} server(s) with {nextNamed.Count}?";
                    if (dropped.Count > 0) msg += "\n\nRemoved: " + string.Join(", ", dropped);
                    if (unnamed > 0) msg += $"\n\n{unnamed} unnamed server(s) will also be removed.";
                    if (!EditorUtility.DisplayDialog("Replace MCP servers?", msg, "Apply", "Cancel")) return;
                }

                settings.mcpServers = parsed;
                settings.SaveSettings();
                ctx.RequestRebuild(); // stays in JSON mode; re-fills with canonical form
            };

            var actionsRow = SidekickSettingsSectionBuilder.HorizontalRow();
            actionsRow.AddToClassList("sk-settings-actions");
            actionsRow.Add(formatBtn);
            actionsRow.Add(applyBtn);
            root.Add(actionsRow);

            Validate(); // set initial enabled state
            return editor;
        }

        /// <summary>
        /// Whitespace to insert after a newline typed at <paramref name="caret"/>: the current line's
        /// leading whitespace, plus one indent level (two spaces) when the line ends with an opening
        /// '{' or '['. Pure and side-effect-free so it can be unit-tested without a live text field.
        /// </summary>
        internal static string ComputeNewlineIndent(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret <= 0)
                return string.Empty;
            if (caret > text.Length)
                caret = text.Length;

            var lineStart = text.LastIndexOf('\n', caret - 1);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            var indentLen = 0;
            while (lineStart + indentLen < caret)
            {
                var c = text[lineStart + indentLen];
                if (c == ' ' || c == '\t') indentLen++;
                else break;
            }
            var indent = text.Substring(lineStart, indentLen);

            var beforeTrimmed = text.Substring(lineStart, caret - lineStart).TrimEnd();
            if (beforeTrimmed.Length > 0)
            {
                var last = beforeTrimmed[beforeTrimmed.Length - 1];
                if (last == '{' || last == '[')
                    indent += "  ";
            }

            return indent;
        }

        private static void RebuildServers(VisualElement container, McpSettingsSectionContext ctx)
        {
            container.Clear();
            foreach (var entry in ctx.Settings.GetMcpServers())
            {
                container.Add(BuildServerCard(entry, ctx));
            }

            if (ctx.Settings.GetMcpServers().Count == 0)
            {
                var empty = new Label("No MCP servers. Click \"Add Server\" to create one.");
                empty.AddToClassList("sk-mcpset-empty");
                empty.style.unityTextAlign = TextAnchor.MiddleCenter; // layout → inline
                container.Add(empty);
            }
        }

        private static VisualElement BuildServerCard(SidekickSettings.McpServerEntry entry, McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;
            var id = entry.id ?? string.Empty;
            var isExpanded = !string.IsNullOrEmpty(id) && _expanded.Contains(id);

            var card = new VisualElement();
            card.AddToClassList("sk-mcpset-card");
            if (!entry.enabled) card.AddToClassList("sk-mcpset-card--disabled");

            // --- header (structural inline; cosmetics in USS) ---
            var header = new VisualElement();
            header.AddToClassList("sk-mcpset-card-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var chevron = new Label(isExpanded ? "▾" : "▸");
            chevron.AddToClassList("sk-mcpset-chevron");
            chevron.style.flexShrink = 0;
            chevron.style.width = 14; // structural → inline (survives unresolved USS in Project Settings)
            chevron.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(chevron);

            var nameLabel = new Label(string.IsNullOrWhiteSpace(entry.name) ? "(unnamed)" : entry.name);
            nameLabel.AddToClassList("sk-mcpset-card-name");
            nameLabel.style.flexGrow = 1;
            nameLabel.style.flexShrink = 1; // REQUIRED for text-overflow:ellipsis to engage
            header.Add(nameLabel);

            var transport = string.IsNullOrWhiteSpace(entry.transport) ? "http" : entry.transport;
            var badge = new Label(transport.ToUpperInvariant()); // "HTTP" / "STDIO"
            badge.AddToClassList("sk-mcpset-badge");
            badge.AddToClassList(string.Equals(transport, "stdio", StringComparison.Ordinal)
                ? "sk-mcpset-badge--stdio" : "sk-mcpset-badge--http");
            badge.style.flexShrink = 0;
            header.Add(badge);

            var enabledToggle = new Toggle { value = entry.enabled };
            enabledToggle.style.flexShrink = 0;
            enabledToggle.style.marginLeft = 6;
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                entry.enabled = evt.newValue;
                settings.SaveSettings();
                ctx.RequestRebuild(); // re-applies the --disabled dim class
            });
            // The toggle must not also toggle card expansion.
            enabledToggle.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            enabledToggle.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            header.Add(enabledToggle);
            card.Add(header);

            // --- body (the fields that used to be in the Foldout, minus Enabled which moved to header) ---
            var body = new VisualElement();
            body.AddToClassList("sk-mcpset-card-body");
            body.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            // Name
            var nameField = new TextField { value = entry.name };
            nameField.RegisterValueChangedCallback(evt => { entry.name = evt.newValue; settings.SaveSettings(); });
            body.Add(SidekickSettingsSectionBuilder.FieldRow("Name", nameField, "Server name (the key under mcpServers)."));

            // Transport
            var transportDropdown = new DropdownField(TransportChoices, Math.Max(0, TransportChoices.IndexOf(transport)));
            transportDropdown.RegisterValueChangedCallback(evt =>
            {
                entry.transport = evt.newValue;
                settings.SaveSettings();
                ctx.RequestRebuild();
            });
            body.Add(SidekickSettingsSectionBuilder.FieldRow("Transport", transportDropdown));

            if (string.Equals(transport, "stdio", StringComparison.Ordinal))
            {
                var command = new TextField { value = entry.command };
                command.RegisterValueChangedCallback(evt => { entry.command = evt.newValue; settings.SaveSettings(); });
                body.Add(SidekickSettingsSectionBuilder.FieldRow("Command", command));

                body.Add(SidekickSettingsSectionBuilder.SectionHeader("Args"));
                body.Add(BuildStringListEditor(entry.args, ctx));

                body.Add(SidekickSettingsSectionBuilder.SectionHeader("Env"));
                body.Add(BuildKeyValueEditor(entry.env, ctx));
            }
            else
            {
                var url = new TextField { value = entry.url };
                url.RegisterValueChangedCallback(evt => { entry.url = evt.newValue; settings.SaveSettings(); });
                body.Add(SidekickSettingsSectionBuilder.FieldRow("URL", url));

                body.Add(SidekickSettingsSectionBuilder.SectionHeader("Headers"));
                body.Add(BuildKeyValueEditor(entry.headers, ctx));
            }

            var remove = new Button(() => { settings.RemoveMcpServer(entry.id); ctx.RequestRebuild(); }) { text = "Remove Server" };
            var removeRow = SidekickSettingsSectionBuilder.HorizontalRow();
            removeRow.AddToClassList("sk-settings-actions");
            removeRow.style.marginTop = 4;
            removeRow.Add(remove);
            body.Add(removeRow);

            card.Add(body);

            // Clicking the header (chevron/name/badge region) toggles expansion; the toggle stopped propagation above.
            header.RegisterCallback<ClickEvent>(_ =>
            {
                var nowExpanded = body.style.display == DisplayStyle.None;
                body.style.display = nowExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.text = nowExpanded ? "▾" : "▸";
                if (string.IsNullOrEmpty(id)) return;
                if (nowExpanded) _expanded.Add(id); else _expanded.Remove(id);
            });

            return card;
        }

        private static VisualElement BuildStringListEditor(List<string> values, McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;
            var container = new VisualElement();

            for (var i = 0; i < values.Count; i++)
            {
                var index = i;
                var row = SidekickSettingsSectionBuilder.HorizontalRow();
                row.AddToClassList("sk-mcpset-list-row");

                var field = new TextField { value = values[index] };
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(evt => { values[index] = evt.newValue; settings.SaveSettings(); });
                row.Add(field);

                var removeBtn = new Button(() => { values.RemoveAt(index); settings.SaveSettings(); ctx.RequestRebuild(); })
                {
                    text = "−"
                };
                removeBtn.AddToClassList("sk-mcpset-remove-button");
                removeBtn.style.width = 24;
                row.Add(removeBtn);
                container.Add(row);
            }

            var add = new Button(() => { values.Add(string.Empty); settings.SaveSettings(); ctx.RequestRebuild(); })
            {
                text = "Add"
            };
            container.Add(add);
            return container;
        }

        private static VisualElement BuildKeyValueEditor(List<SidekickSettings.McpKeyValueEntry> entries, McpSettingsSectionContext ctx)
        {
            var settings = ctx.Settings;
            var container = new VisualElement();

            for (var i = 0; i < entries.Count; i++)
            {
                var index = i;
                var row = SidekickSettingsSectionBuilder.HorizontalRow();
                row.AddToClassList("sk-mcpset-kv-row");

                var key = new TextField { value = entries[index].key };
                key.style.flexGrow = 1;
                key.RegisterValueChangedCallback(evt => { entries[index].key = evt.newValue; settings.SaveSettings(); });
                row.Add(key);

                var value = new TextField { value = entries[index].value };
                value.style.flexGrow = 1;
                value.RegisterValueChangedCallback(evt => { entries[index].value = evt.newValue; settings.SaveSettings(); });
                row.Add(value);

                var removeBtn = new Button(() => { entries.RemoveAt(index); settings.SaveSettings(); ctx.RequestRebuild(); })
                {
                    text = "−"
                };
                removeBtn.AddToClassList("sk-mcpset-remove-button");
                removeBtn.style.width = 24;
                row.Add(removeBtn);
                container.Add(row);
            }

            var add = new Button(() =>
            {
                entries.Add(new SidekickSettings.McpKeyValueEntry());
                settings.SaveSettings();
                ctx.RequestRebuild();
            })
            {
                text = "Add"
            };
            container.Add(add);
            return container;
        }
    }
}
