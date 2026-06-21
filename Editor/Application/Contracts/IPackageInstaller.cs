// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IPackageInstaller
    {
        /// <summary>
        /// Stage a downloaded payload + manifest for the existing two-stage installer
        /// and trigger it (the installer self-activates on reload). payloadSourcePath is
        /// a local file the installer will import.
        /// </summary>
        void StageUpdate(string sku, string version, string[] packages, string payloadSourcePath);
    }
}
