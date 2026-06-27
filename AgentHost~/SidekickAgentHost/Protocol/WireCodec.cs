// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;

namespace Ryx.Sidekick.AgentHost.Protocol
{
    /// <summary>
    /// A parsed client-&gt;daemon message. Every field is optional on the wire;
    /// the dispatcher reads only the ones relevant to <see cref="Type"/>.
    /// Parsing never throws on unknown/extra fields or wrong types — missing or
    /// mistyped values fall back to defaults so a malformed line cannot crash
    /// the connection loop (the dispatcher decides whether to ERROR or ignore).
    /// </summary>
    internal readonly struct IncomingMessage
    {
        public string Type { get; init; }
        public string? Token { get; init; }
        public int Proto { get; init; }
        public int OwnerPid { get; init; }
        public string? Handle { get; init; }
        public SpawnSpec? Spec { get; init; }
        public string? Data { get; init; }
        public bool AppendNewline { get; init; }
        public long AfterSeq { get; init; }
        public long SafeSeq { get; init; }

        public bool IsValid => !string.IsNullOrEmpty(Type);
    }

    /// <summary>
    /// JSON-lines codec. One JSON object per line, UTF-8. Outgoing objects are
    /// serialized with field names verbatim (no naming policy).
    /// </summary>
    internal static class WireCodec
    {
        private static readonly JsonSerializerOptions OutOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };

        public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, OutOptions);

        /// <summary>
        /// Parse a single received line into an <see cref="IncomingMessage"/>.
        /// Returns an invalid message (empty Type) for blank lines or
        /// unparseable JSON; the caller treats that as "skip".
        /// </summary>
        public static IncomingMessage ReadIncoming(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return default;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return default;

                return new IncomingMessage
                {
                    Type = GetString(root, "t") ?? "",
                    Token = GetString(root, "token"),
                    Proto = GetInt(root, "proto"),
                    OwnerPid = GetInt(root, "ownerPid"),
                    Handle = GetString(root, "handle"),
                    Spec = GetSpec(root),
                    Data = GetString(root, "data"),
                    AppendNewline = GetBool(root, "appendNewline"),
                    AfterSeq = GetLong(root, "afterSeq"),
                    SafeSeq = GetLong(root, "safeSeq"),
                };
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static SpawnSpec? GetSpec(JsonElement root)
        {
            if (!root.TryGetProperty("spec", out var spec) || spec.ValueKind != JsonValueKind.Object)
                return null;

            var result = new SpawnSpec
            {
                filename = GetString(spec, "filename") ?? "",
                commandLine = GetString(spec, "commandLine"),
                workingDir = GetString(spec, "workingDir"),
            };

            if (spec.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in args.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        result.args.Add(item.GetString() ?? "");
                }
            }

            if (spec.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in env.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        result.env[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            return result;
        }

        private static string? GetString(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int GetInt(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
                ? n
                : 0;

        private static long GetLong(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
                ? n
                : 0L;

        private static bool GetBool(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) &&
               (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();
    }
}