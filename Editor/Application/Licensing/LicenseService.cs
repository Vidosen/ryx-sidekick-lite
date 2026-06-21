// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Licensing;
using Ryx.Sidekick.Editor.UseCases.Contracts;
// IClock lives in the Ryx.Sidekick.Editor namespace (Application/Contracts/IClock.cs, same asmdef)
using Ryx.Sidekick.Editor;

namespace Ryx.Sidekick.Editor.UseCases.Licensing
{
    internal enum LicenseState { None, Active, Expired }

    internal enum LicenseActivationOutcome
    {
        Success, NotFound, Revoked, SeatLimit, BadRequest, InvalidToken, NetworkError, ServerError
    }

    internal readonly struct LicenseStatus
    {
        public readonly LicenseState State;
        public readonly string Sku;
        public readonly long ExpiresAt;
        public readonly int EditionYear;
        public readonly long SupportUntil;
        public LicenseStatus(LicenseState state, string sku, long expiresAt, int editionYear = 0, long supportUntil = 0)
        { State = state; Sku = sku; ExpiresAt = expiresAt; EditionYear = editionYear; SupportUntil = supportUntil; }
        public static LicenseStatus None => new LicenseStatus(LicenseState.None, null, 0);
    }

    internal readonly struct LicenseActivationResult
    {
        public readonly LicenseActivationOutcome Outcome;
        public readonly LicenseStatus Status;
        public LicenseActivationResult(LicenseActivationOutcome outcome, LicenseStatus status)
        { Outcome = outcome; Status = status; }
    }

    internal interface ILicenseService
    {
        Task<LicenseActivationResult> ActivateAsync(string key, string editorVersion, string os);
        Task<LicenseActivationResult> RefreshAsync();
        LicenseStatus GetStatus();
    }

    internal sealed class LicenseService : ILicenseService
    {
        private const int TimeoutSeconds = 15;
        private readonly IHttpClient _http;
        private readonly IEntitlementVerifier _verifier;
        private readonly ICredentialStore _creds;
        private readonly IEntitlementCache _cache;
        private readonly IMachineIdProvider _machine;
        private readonly IClock _clock;
        private readonly string _validateUrl;

        public LicenseService(IHttpClient http, IEntitlementVerifier verifier, ICredentialStore creds,
            IEntitlementCache cache, IMachineIdProvider machine, IClock clock, string validateLicenseUrl)
        {
            _http = http; _verifier = verifier; _creds = creds; _cache = cache;
            _machine = machine; _clock = clock; _validateUrl = validateLicenseUrl;
        }

        public async Task<LicenseActivationResult> ActivateAsync(string key, string editorVersion, string os)
        {
            if (string.IsNullOrWhiteSpace(key))
                return new LicenseActivationResult(LicenseActivationOutcome.BadRequest, LicenseStatus.None);

            var body = new JObject
            {
                ["key"] = key,
                ["machineId"] = _machine.GetMachineId(),
                ["editorVersion"] = editorVersion,
                ["os"] = os,
            }.ToString(Newtonsoft.Json.Formatting.None);

            HttpResponse resp;
            try { resp = await _http.PostJsonAsync(_validateUrl, body, TimeoutSeconds); }
            catch { return new LicenseActivationResult(LicenseActivationOutcome.NetworkError, LicenseStatus.None); }

            if (resp.Status == 0)
                return new LicenseActivationResult(LicenseActivationOutcome.NetworkError, LicenseStatus.None);

            var result = ParseAndApply(resp, persistKeyOnSuccess: key);
            return result;
        }

        public async Task<LicenseActivationResult> RefreshAsync()
        {
            var key = _creds.ReadLicenseKey();
            if (string.IsNullOrEmpty(key))
                return new LicenseActivationResult(LicenseActivationOutcome.NotFound, LicenseStatus.None);
            return await ActivateAsync(key, null, null);
        }

        private LicenseActivationResult ParseAndApply(HttpResponse resp, string persistKeyOnSuccess)
        {
            JObject json = null;
            try { if (!string.IsNullOrEmpty(resp.Body)) json = JObject.Parse(resp.Body); } catch { }

            if (!resp.Ok)
            {
                var code = json?["error"]?.ToString();
                return new LicenseActivationResult(MapError(code), LicenseStatus.None);
            }

            var token = json?["entitlementToken"]?.ToString();
            if (string.IsNullOrEmpty(token))
                return new LicenseActivationResult(LicenseActivationOutcome.ServerError, LicenseStatus.None);

            var verification = _verifier.Verify(token);
            if (!verification.Valid)
                return new LicenseActivationResult(LicenseActivationOutcome.InvalidToken, LicenseStatus.None);

            _cache.Write(token);
            if (persistKeyOnSuccess != null) _creds.WriteLicenseKey(persistKeyOnSuccess);
            return new LicenseActivationResult(LicenseActivationOutcome.Success, StatusFrom(verification.Payload));
        }

        private static LicenseActivationOutcome MapError(string code)
        {
            switch (code)
            {
                case "not_found": return LicenseActivationOutcome.NotFound;
                case "revoked": return LicenseActivationOutcome.Revoked;
                case "seat_limit": return LicenseActivationOutcome.SeatLimit;
                case "bad_request": return LicenseActivationOutcome.BadRequest;
                default: return LicenseActivationOutcome.ServerError;
            }
        }

        public LicenseStatus GetStatus()
        {
            var token = _cache.Read();
            if (string.IsNullOrEmpty(token)) return LicenseStatus.None;
            var v = _verifier.Verify(token);
            if (!v.Valid) return LicenseStatus.None;
            return StatusFrom(v.Payload);
        }

        private LicenseStatus StatusFrom(EntitlementPayload p)
        {
            // IClock.Now is LOCAL time; convert to UTC before epoch math (else off by the
            // machine's UTC offset). Do NOT subtract a UTC epoch from a local Now directly.
            long now = (long)(_clock.Now.ToUniversalTime()
                - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            // State (Active/Expired) is based on expiresAt only — token freshness/refresh cadence.
            // supportUntil governs download eligibility (enforced server-side); Pro features are perpetual.
            var state = now <= p.ExpiresAt ? LicenseState.Active : LicenseState.Expired;
            return new LicenseStatus(state, p.Sku, p.ExpiresAt, p.EditionYear, p.SupportUntil);
        }
    }
}
