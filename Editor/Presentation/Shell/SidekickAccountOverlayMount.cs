// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Views;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Static factory that instantiates the Sidekick Account overlay UXML fragment,
    /// appends it to a parent <see cref="VisualElement"/>, queries the named elements,
    /// and returns a ready-to-use <see cref="SidekickAccountLoginView"/>.
    ///
    /// The caller is responsible for binding the view to a
    /// <see cref="Controllers.SidekickAccountController"/> and for disposing the view on teardown.
    ///
    /// Mirror: <c>SidekickWindowView.TryCreate</c> clones <c>LoginOverlay.uxml</c> into the named
    /// <c>login-overlay-container</c> element then queries child refs manually. This helper does
    /// the same for the account overlay, appending a new container to <paramref name="parent"/>.
    /// </summary>
    internal static class SidekickAccountOverlayMount
    {
        internal const string ContainerName = "sidekick-account-overlay-container";

        /// <summary>
        /// Loads the account overlay UXML via <c>AssetDatabase</c> (the same loader used for
        /// <c>LoginOverlay.uxml</c>), appends a named container to <paramref name="parent"/>,
        /// clones the template into it, queries the named child elements, and returns the view.
        /// Returns <c>null</c> if the template asset cannot be found.
        /// </summary>
        internal static SidekickAccountLoginView Mount(VisualElement parent)
        {
            if (parent == null)
            {
                return null;
            }

            var path = SidekickUiConstants.AccountOverlayUxmlPath;
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (template == null)
            {
                UnityEngine.Debug.LogWarning($"[SidekickAccount] Could not load overlay template at {path}");
                return null;
            }

            var container = new VisualElement { name = ContainerName };
            template.CloneTree(container);
            parent.Add(container);

            return QueryView(container);
        }

        private static SidekickAccountLoginView QueryView(VisualElement container)
        {
            var overlay = container.Q<VisualElement>("account-overlay");
            var signedOutScreen = overlay?.Q<VisualElement>("account-signedout-screen");
            var signingInScreen = overlay?.Q<VisualElement>("account-signingin-screen");
            var signedInScreen = overlay?.Q<VisualElement>("account-signedin-screen");

            var signInBtn = overlay?.Q<Button>("account-signin-btn");
            var codeInput = overlay?.Q<TextField>("account-code-input");
            var codeSubmitBtn = overlay?.Q<Button>("account-code-submit-btn");
            var cancelBtn = overlay?.Q<Button>("account-cancel-btn");
            var manualCodeInput = overlay?.Q<TextField>("account-manual-code-input");
            var signingInSubmitBtn = overlay?.Q<Button>("account-signingin-submit-btn");
            var signingInCancelBtn = overlay?.Q<Button>("account-signingin-cancel-btn");
            var emailLabel = overlay?.Q<Label>("account-email-label");
            var planLabel = overlay?.Q<Label>("account-plan-label");
            var seatsLabel = overlay?.Q<Label>("account-seats-label");
            var signOutBtn = overlay?.Q<Button>("account-signout-btn");

            return new SidekickAccountLoginView(
                overlay,
                signedOutScreen,
                signingInScreen,
                signedInScreen,
                signInBtn,
                codeInput,
                codeSubmitBtn,
                cancelBtn,
                manualCodeInput,
                signingInSubmitBtn,
                signingInCancelBtn,
                emailLabel,
                planLabel,
                seatsLabel,
                signOutBtn);
        }
    }
}
