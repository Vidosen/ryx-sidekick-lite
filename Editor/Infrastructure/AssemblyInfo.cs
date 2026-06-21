// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.CompilerServices;

// Infrastructure-tier types include adapters and concrete implementations
// that the Presentation/Shell composition root and the test assembly need
// to reference. Sub-asmdefs (Providers) also need access to compose
// against these adapters.
[assembly: InternalsVisibleTo("Sidekick.Editor")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Presentation")]
[assembly: InternalsVisibleTo("Sidekick.Tests")]
[assembly: InternalsVisibleTo("Sidekick.Editor.Pro")]
[assembly: InternalsVisibleTo("Sidekick.Tests.Pro")]
// Dev-only publisher tooling (Assets/Editor/ReleaseTools) — not shipped to consumers.
[assembly: InternalsVisibleTo("Sidekick.ReleaseTools")]
