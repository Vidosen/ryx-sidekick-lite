// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Account;
using Ryx.Sidekick.Editor.Infrastructure.Auth;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    /// <summary>
    /// Thin <see cref="ISidekickAccountService"/> wrapper over the process-singleton
    /// <see cref="SidekickAccountManager"/>. Register this in DI; tests inject the manager directly.
    /// </summary>
    internal sealed class SidekickAccountService : ISidekickAccountService, IDisposable
    {
        private readonly SidekickAccountManager _manager;

        public SidekickAccountService()
        {
            _manager = SidekickAccountManager.Instance;
        }

        public event Action<SidekickAccountStatus> OnStatusChanged
        {
            add => _manager.OnStatusChanged += value;
            remove => _manager.OnStatusChanged -= value;
        }

        public SidekickAccountStatus GetStatus() => _manager.GetStatus();

        public Task<SidekickAccountResult> StartLoginAsync(Action<string> openBrowser)
            => _manager.StartLoginAsync(openBrowser);

        public SidekickAccountResult CancelLogin() => _manager.CancelLogin();

        public SidekickAccountResult HandleManualCode(string code) => _manager.HandleManualCode(code);

        public Task<SidekickAccountResult> RefreshAsync() => _manager.RefreshAsync();

        public Task<bool> SignOutAsync() => _manager.SignOutAsync();

        public void Dispose() => _manager.CancelLogin();
    }
}
