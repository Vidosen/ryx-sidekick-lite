// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class AskUserQuestionSchemaRegistry
    {
        private readonly List<IAskUserQuestionSchemaAdapter> _adapters;

        private AskUserQuestionSchemaRegistry(IEnumerable<IAskUserQuestionSchemaAdapter> adapters)
        {
            _adapters = adapters?.ToList() ?? throw new ArgumentNullException(nameof(adapters));
        }

        public IAskUserQuestionSchemaAdapter Resolve(PendingPermission permission)
        {
            return _adapters.FirstOrDefault(adapter => adapter.CanHandle(permission));
        }

        public static AskUserQuestionSchemaRegistry CreateDefault(
            ISettingsStore settingsStore = null,
            IProviderCatalog providerCatalog = null)
        {
            return new AskUserQuestionSchemaRegistry(new IAskUserQuestionSchemaAdapter[]
            {
                new ImplementPlanLocallyAdapter(settingsStore),
                new AcpExitPlanModeAdapter(),
                new ClaudeCodeUserQuestionSchemaAdapter(settingsStore, providerCatalog),
                new SessionAskQuestionAdapter(),
                new ClaudeControlRequestQuestionAdapter()
            });
        }
    }
}
