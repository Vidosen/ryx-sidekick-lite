// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal enum AskUserQuestionDispatchChannel
    {
        ControlResponse,
        UserInputResponse,
        LocalOnly
    }

    internal sealed class AskUserQuestionDispatch
    {
        private AskUserQuestionDispatch(
            AskUserQuestionDispatchChannel channel,
            bool allow,
            JToken updatedInput,
            string message,
            JObject userInputResponse,
            AskUserQuestionModeTransition modeTransition,
            string localFollowupText)
        {
            Channel = channel;
            Allow = allow;
            UpdatedInput = updatedInput;
            Message = message;
            UserInputResponse = userInputResponse;
            ModeTransition = modeTransition;
            LocalFollowupText = localFollowupText;
        }

        public AskUserQuestionDispatchChannel Channel { get; }

        public bool Allow { get; }

        public JToken UpdatedInput { get; }

        public string Message { get; }

        public JObject UserInputResponse { get; }

        public AskUserQuestionModeTransition ModeTransition { get; }

        public string LocalFollowupText { get; }

        public static AskUserQuestionDispatch ForControlResponse(
            bool allow,
            JToken updatedInput = null,
            string message = null,
            AskUserQuestionModeTransition modeTransition = null)
        {
            return new AskUserQuestionDispatch(
                AskUserQuestionDispatchChannel.ControlResponse,
                allow,
                updatedInput,
                message,
                null,
                modeTransition,
                null);
        }

        public static AskUserQuestionDispatch ForUserInputResponse(
            JObject userInputResponse,
            AskUserQuestionModeTransition modeTransition = null)
        {
            return new AskUserQuestionDispatch(
                AskUserQuestionDispatchChannel.UserInputResponse,
                allow: true,
                updatedInput: null,
                message: null,
                userInputResponse: userInputResponse,
                modeTransition: modeTransition,
                localFollowupText: null);
        }

        public static AskUserQuestionDispatch ForLocalAction(
            string localFollowupText = null,
            AskUserQuestionModeTransition modeTransition = null)
        {
            return new AskUserQuestionDispatch(
                AskUserQuestionDispatchChannel.LocalOnly,
                allow: true,
                updatedInput: null,
                message: null,
                userInputResponse: null,
                modeTransition: modeTransition,
                localFollowupText: localFollowupText);
        }
    }
}
