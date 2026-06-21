// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    /// <summary>
    /// Resolves an ImageAttachment to its decoded Texture2D, typically from a presentation-side
    /// texture cache. Used by ImageOverlayPresenter so it does not own the cache directly.
    /// </summary>
    internal interface IImageTextureResolver
    {
        Texture2D Resolve(ImageAttachment attachment);
    }
}
