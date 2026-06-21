// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Attachments
{
    internal sealed class AddContextAttachmentUseCase
    {
        private readonly AttachmentSessionState _state;

        public AddContextAttachmentUseCase(AttachmentSessionState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Adds <paramref name="context"/> to the pending list.
        /// Returns <c>true</c> when appended, <c>false</c> when null or a duplicate.
        /// </summary>
        public bool Execute(IContextAttachment context)
        {
            switch (context)
            {
                case null:
                    return false;
                // FileContext: skip if duplicate FilePath
                case FileContextAttachment fileCtx:
                {
                    foreach (var existing in _state.PendingContexts)
                    {
                        if (existing is FileContextAttachment existingFile &&
                            existingFile.FilePath == fileCtx.FilePath)
                        {
                            return false;
                        }
                    }

                    break;
                }
                // GameObjectContext: skip if duplicate InstanceId
                case GameObjectContextAttachment goCtx:
                {
                    foreach (var existing in _state.PendingContexts)
                    {
                        if (existing is GameObjectContextAttachment existingGo &&
                            existingGo.InstanceId == goCtx.InstanceId)
                        {
                            return false;
                        }
                    }

                    break;
                }
            }

            // ScreenshotContext: always append (no dedup)

            _state.AppendContext(context);
            return true;
        }
    }
}
