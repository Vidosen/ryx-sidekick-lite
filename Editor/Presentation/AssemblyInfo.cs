// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.CompilerServices;

// Presentation owns the shell composition root (SidekickEditorAppHost,
// SidekickWindow + partials, SidekickServiceRegistry, SidekickArchitecture).
// The test assembly needs access to internal composition surfaces.
[assembly: InternalsVisibleTo("Sidekick.Editor")]
[assembly: InternalsVisibleTo("Sidekick.Tests")]
[assembly: InternalsVisibleTo("Sidekick.Tests.Pro")]
