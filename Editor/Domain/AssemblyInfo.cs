// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.CompilerServices;

// Domain holds a few internal SPI types (e.g. IPersistentTurnStarter,
// ConversationListLoadState, ConversationHistoryLoadState) that the rest
// of the Sidekick package consumes. They are not part of the public
// package surface, so we grant the sibling assemblies access rather than
// promoting them all to `public`.
[assembly: InternalsVisibleTo("Sidekick.Editor")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Application")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Infrastructure")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Presentation")]
[assembly: InternalsVisibleTo("Sidekick.Tests")]
[assembly: InternalsVisibleTo("Sidekick.Tests.Application")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Pro")]
[assembly: InternalsVisibleTo("Sidekick.Tests.Pro")]
