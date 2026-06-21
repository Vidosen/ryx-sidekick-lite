// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    internal interface IComposerAttachmentSource
    {
        IReadOnlyList<ImageAttachment> Pending { get; }

        IReadOnlyList<IContextAttachment> Context { get; }

        event Action Changed;
    }
}
