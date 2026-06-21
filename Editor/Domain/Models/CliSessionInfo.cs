// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Domain.Models
{
    /// <summary>
    /// Lightweight session info for listing sessions without loading full history.
    /// </summary>
    internal class CliSessionInfo
    {
        public string SessionId;
        public string FilePath;
        public string Title;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
    }
}
