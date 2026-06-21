// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.CompilerServices;

// Application-tier contracts are intentionally declared `internal` because
// they are SPI for the Sidekick package only — they are not part of the
// public package surface. The main editor assembly (Sidekick.Editor)
// provides the concrete adapters (ProcessManager, ProviderScope, etc.) and
// the test assembly (Sidekick.Tests) needs to see them to verify wiring.
//
// Adding new InternalsVisibleTo grants here should be a deliberate act and
// reviewed with the asmdef boundary tests.
[assembly: InternalsVisibleTo("Sidekick.Editor")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Infrastructure")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Presentation")]
[assembly: InternalsVisibleTo("Sidekick.Tests")]
[assembly: InternalsVisibleTo("Sidekick.Tests.Application")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Pro")]
[assembly: InternalsVisibleTo("Sidekick.Tests.Pro")]
// Dev-only publisher tooling (Assets/Editor/ReleaseTools) — not shipped to consumers.
[assembly: InternalsVisibleTo("Sidekick.ReleaseTools")]
