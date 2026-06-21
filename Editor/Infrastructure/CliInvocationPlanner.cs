// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor
{
    internal sealed class StagedAttachmentFiles : IDisposable
    {
        private readonly List<string> _filePaths = new();

        public IReadOnlyList<string> FilePaths => _filePaths;

        public static StagedAttachmentFiles Create(IReadOnlyList<ImageAttachment> attachments)
        {
            var staged = new StagedAttachmentFiles();

            if (attachments == null)
            {
                return staged;
            }

            foreach (var attachment in attachments)
            {
                if (attachment == null)
                {
                    continue;
                }

                var normalized = ControlRequestHandler.NormalizeBase64Data(attachment.Data);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(normalized);
                }
                catch (FormatException)
                {
                    continue;
                }

                var extension = GetExtension(attachment);
                var fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? $"sidekick-image{extension}"
                    : Path.GetFileNameWithoutExtension(attachment.FileName) + extension;
                var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{fileName}");

                File.WriteAllBytes(tempPath, bytes);
                staged._filePaths.Add(tempPath);
            }

            return staged;
        }

        public void Dispose()
        {
            foreach (var filePath in _filePaths)
            {
                AttachmentUtils.TryDeleteFile(filePath);
            }

            _filePaths.Clear();
        }

        private static string GetExtension(ImageAttachment attachment)
        {
            if (!string.IsNullOrWhiteSpace(attachment?.FileName))
            {
                var fileExtension = Path.GetExtension(attachment.FileName);
                if (!string.IsNullOrWhiteSpace(fileExtension))
                {
                    return fileExtension;
                }
            }

            return attachment?.MediaType?.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".png"
            };
        }
    }

    internal sealed class CliInvocationPlan : IDisposable
    {
        private readonly StagedAttachmentFiles _stagedAttachmentFiles;

        public CliInvocationPlan(
            string arguments,
            string stdinPayload,
            PromptTransportMode promptTransportMode,
            StagedAttachmentFiles stagedAttachmentFiles)
        {
            Arguments = arguments;
            StdinPayload = stdinPayload;
            PromptTransportMode = promptTransportMode;
            _stagedAttachmentFiles = stagedAttachmentFiles;
        }

        public string Arguments { get; }
        public string StdinPayload { get; }
        public PromptTransportMode PromptTransportMode { get; }
        public bool CloseStdinAfterPrompt => PromptTransportMode == PromptTransportMode.PlainTextStdin;
        public IReadOnlyList<string> ImageAttachmentPaths => _stagedAttachmentFiles?.FilePaths ?? Array.Empty<string>();

        public void Dispose()
        {
            _stagedAttachmentFiles?.Dispose();
        }
    }

    internal static class CliInvocationPlanner
    {
        public static CliInvocationPlan Build(
            SidekickSettings settings,
            string prompt,
            string sessionId,
            string mcpArgs,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments,
            BuildPromptContextUseCase promptContextUseCase = null)
        {
            var provider = settings.ActiveProvider;
            var transportMode = provider.PromptTransportMode;
            var stagedAttachmentFiles = provider.UsesImageAttachmentFilePaths
                ? StagedAttachmentFiles.Create(attachments)
                : null;

            var uc = promptContextUseCase ?? new BuildPromptContextUseCase();
            var promptForArgumentsOrPlainText = transportMode == PromptTransportMode.StreamJsonStdin
                ? prompt
                : uc.Execute(prompt, contextAttachments);

            var arguments = settings.BuildArguments(
                prompt: transportMode == PromptTransportMode.Argument ? promptForArgumentsOrPlainText : null,
                printMode: true,
                continueSession: false,
                sessionId: sessionId,
                promptTransportMode: transportMode,
                includePrompt: transportMode == PromptTransportMode.Argument,
                imageAttachmentPaths: stagedAttachmentFiles?.FilePaths);

            if (!string.IsNullOrEmpty(mcpArgs) && provider.SupportsMcpConfig)
            {
                arguments = $"{arguments} {mcpArgs}".Trim();
            }

            var stdinPayload = transportMode switch
            {
                PromptTransportMode.StreamJsonStdin => provider.BuildPromptInput(prompt, attachments, contextAttachments),
                PromptTransportMode.PlainTextStdin => provider.BuildPromptInput(promptForArgumentsOrPlainText, attachments, contextAttachments),
                _ => null
            };

            return new CliInvocationPlan(arguments, stdinPayload, transportMode, stagedAttachmentFiles);
        }
    }
}
