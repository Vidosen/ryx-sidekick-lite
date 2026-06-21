// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.Infrastructure.Providers.Claude
{
    /// <summary>
    /// Data-transfer object returned by <see cref="ClaudeInitializeResponseParser.Parse"/>.
    /// </summary>
    internal sealed class ClaudeCliCapabilities
    {
        public List<ModelDescriptor> Models { get; set; } = new List<ModelDescriptor>();
        public List<SlashCommand> Commands { get; set; } = new List<SlashCommand>();
        public ProviderAccountInfo Account { get; set; }
    }

    /// <summary>
    /// Parses the JSON line of a Claude CLI <c>control_response / initialize</c> event
    /// into a <see cref="ClaudeCliCapabilities"/> DTO.
    /// Malformed input, missing nodes, wrong types, or a non-success subtype all return
    /// an empty DTO — this method never throws.
    /// </summary>
    internal static class ClaudeInitializeResponseParser
    {
        // Regex matching " (word)" plugin prefix at the start of a description, e.g. "(superpowers) "
        private static readonly Regex PluginPrefixRegex =
            new Regex(@"^\(([A-Za-z0-9_\-]+)\)\s+", RegexOptions.Compiled);

        /// <summary>
        /// Parses a single JSON line that contains a <c>control_response</c> initialize response.
        /// Returns an empty <see cref="ClaudeCliCapabilities"/> on any parse failure.
        /// </summary>
        public static ClaudeCliCapabilities Parse(string jsonLine)
        {
            var empty = new ClaudeCliCapabilities();

            if (string.IsNullOrWhiteSpace(jsonLine))
                return empty;

            try
            {
                var root = JObject.Parse(jsonLine);

                // Validate outer structure
                if (root["type"]?.ToString() != "control_response")
                    return empty;

                var outerResponse = root["response"] as JObject;
                if (outerResponse == null)
                    return empty;

                if (outerResponse["subtype"]?.ToString() != "success")
                    return empty;

                var inner = outerResponse["response"] as JObject;
                if (inner == null)
                    return empty;

                return new ClaudeCliCapabilities
                {
                    Models = ParseModels(inner["models"] as JArray),
                    Commands = ParseCommands(inner["commands"] as JArray),
                    Account = ParseAccount(inner["account"] as JObject),
                };
            }
            catch
            {
                return empty;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Models

        private static List<ModelDescriptor> ParseModels(JArray modelsArray)
        {
            var result = new List<ModelDescriptor>();
            if (modelsArray == null)
                return result;

            foreach (var token in modelsArray)
            {
                if (token is not JObject obj)
                    continue;

                try
                {
                    var id = obj["value"]?.ToString() ?? string.Empty;
                    var displayName = obj["displayName"]?.ToString() ?? id;
                    var modelDescription = obj["description"]?.ToString() ?? string.Empty;
                    var isDefault = string.Equals(id, "default", StringComparison.Ordinal);

                    var efforts = new List<ReasoningEffortDescriptor>();
                    if (obj["supportsEffort"]?.ToObject<bool>() == true
                        && obj["supportedEffortLevels"] is JArray effortArray)
                    {
                        foreach (var effortToken in effortArray)
                        {
                            var effortValue = effortToken?.ToString();
                            if (!string.IsNullOrEmpty(effortValue))
                                efforts.Add(new ReasoningEffortDescriptor(effortValue, string.Empty));
                        }
                    }

                    result.Add(new ModelDescriptor(
                        id: id,
                        displayName: displayName,
                        isDefault: isDefault,
                        supportedReasoningEfforts: efforts,
                        defaultReasoningEffort: string.Empty,
                        description: modelDescription));
                }
                catch
                {
                    // Skip malformed model entries
                }
            }

            return result;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Commands

        private static List<SlashCommand> ParseCommands(JArray commandsArray)
        {
            var result = new List<SlashCommand>();
            if (commandsArray == null)
                return result;

            foreach (var token in commandsArray)
            {
                if (token is not JObject obj)
                    continue;

                try
                {
                    var name = obj["name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var description = obj["description"]?.ToString() ?? string.Empty;
                    var argumentHint = obj["argumentHint"]?.ToString() ?? string.Empty;

                    var (origin, cleanedDescription) = ClassifyOrigin(name, description);

                    result.Add(new SlashCommand
                    {
                        Name = name,
                        Description = cleanedDescription,
                        ArgumentHint = argumentHint,
                        AcceptsArguments = !string.IsNullOrEmpty(argumentHint),
                        Origin = origin,
                    });
                }
                catch
                {
                    // Skip malformed command entries
                }
            }

            return result;
        }

        /// <summary>
        /// Classifies the <see cref="SlashCommandOrigin"/> for a command and strips
        /// any origin marker from the description.
        /// </summary>
        internal static (SlashCommandOrigin origin, string cleanedDescription) ClassifyOrigin(
            string name, string description)
        {
            // ── Suffix markers (check before colon / prefix logic) ──────────

            if (description.EndsWith(" (user)", StringComparison.Ordinal))
                return (SlashCommandOrigin.User,
                    description.Substring(0, description.Length - " (user)".Length));

            if (description.EndsWith(" (project)", StringComparison.Ordinal))
                return (SlashCommandOrigin.Project,
                    description.Substring(0, description.Length - " (project)".Length));

            if (description.EndsWith(" (dynamic workflow)", StringComparison.Ordinal))
                return (SlashCommandOrigin.Workflow,
                    description.Substring(0, description.Length - " (dynamic workflow)".Length));

            // ── Colon in name → Plugin (e.g. "codex:review") ─────────────

            if (name.Contains(':'))
                return (SlashCommandOrigin.Plugin, description);

            // ── "(word) " prefix in description → Plugin ──────────────────

            var prefixMatch = PluginPrefixRegex.Match(description);
            if (prefixMatch.Success)
            {
                var cleaned = description.Substring(prefixMatch.Length);
                return (SlashCommandOrigin.Plugin, cleaned);
            }

            // ── Default ───────────────────────────────────────────────────

            return (SlashCommandOrigin.Builtin, description);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Account

        private static ProviderAccountInfo ParseAccount(JObject accountObj)
        {
            if (accountObj == null)
                return null;

            try
            {
                return new ProviderAccountInfo
                {
                    ProviderId = "claude",
                    Email = accountObj["email"]?.ToString() ?? string.Empty,
                    Organization = accountObj["organization"]?.ToString() ?? string.Empty,
                    SubscriptionType = accountObj["subscriptionType"]?.ToString() ?? string.Empty,
                    ApiProvider = accountObj["apiProvider"]?.ToString() ?? string.Empty,
                    AccountType = string.Empty,
                    RequiresAuth = false,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
