// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal static class AskUserQuestionTraceFormatter
    {
        private const string QuestionsProperty = "questions";
        private const string AnswersProperty = "answers";
        private const string SummaryProperty = "summary";
        private const string QuestionCountProperty = "questionCount";
        private const string CancelledProperty = "cancelled";

        public static JObject BuildTraceInput(JToken rawInput)
        {
            var questions = NormalizeQuestions(rawInput);
            return new JObject
            {
                [QuestionsProperty] = questions,
                [SummaryProperty] = new JObject
                {
                    [QuestionCountProperty] = questions.Count
                }
            };
        }

        public static JObject ApplyAnswers(JToken existingInput, JObject response)
        {
            var traceInput = EnsureTraceInput(existingInput);
            var answers = ExtractAnswers(response);

            traceInput[AnswersProperty] = answers;
            traceInput[CancelledProperty] = answers.Count == 0;

            return traceInput;
        }

        public static JObject ParseOutputAnswers(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            try
            {
                return ExtractAnswers(JObject.Parse(output));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static JObject BuildDynamicToolCallResponse(JObject response)
        {
            var payload = response ?? new JObject
            {
                [AnswersProperty] = new JObject()
            };

            return new JObject
            {
                ["success"] = true,
                ["contentItems"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "inputText",
                        ["text"] = payload.ToString(Formatting.None)
                    }
                }
            };
        }

        public static JObject GetAnswers(JToken traceInput)
        {
            return traceInput?[AnswersProperty] as JObject;
        }

        public static string GetTraceAnswerKey(AskUserQuestionItem question)
        {
            if (question == null) return null;
            return !string.IsNullOrEmpty(question.Id) ? question.Id : question.Question;
        }

        public static JObject BuildTraceAnswersPayload(AskUserQuestionInput input, AskUserQuestionSessionState state)
        {
            var answers = new JObject();

            if (input?.Questions != null)
            {
                for (int i = 0; i < input.Questions.Count; i++)
                {
                    var question = input.Questions[i];
                    var key = GetTraceAnswerKey(question);
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    if (state != null && !state.IsQuestionAnswered(i))
                    {
                        continue;
                    }

                    var resolved = state?.GetResolvedLabels(i);
                    if (resolved == null || resolved.Count == 0)
                    {
                        continue;
                    }

                    answers[key] = new JObject
                    {
                        [AnswersProperty] = new JArray(resolved.ToArray())
                    };
                }
            }

            return new JObject
            {
                [AnswersProperty] = answers
            };
        }

        /// <summary>
        /// Converts a Claude-flat-format answers object {"question_text": "answer_text | A, B, C"}
        /// to trace format {answers: {traceKey: {answers: [labels...]}}}
        /// used for reload path from Claude JSONL history.
        /// </summary>
        public static JObject BuildTraceAnswersPayloadFromClaudeFlat(
            AskUserQuestionInput input,
            JObject flatAnswers)
        {
            var answers = new JObject();

            if (input?.Questions != null && flatAnswers != null)
            {
                foreach (var question in input.Questions)
                {
                    var key = GetTraceAnswerKey(question);
                    if (string.IsNullOrEmpty(key)) continue;

                    // Look up by question text first, then by header as fallback
                    JToken answerToken = null;
                    if (!string.IsNullOrEmpty(question.Question))
                    {
                        answerToken = flatAnswers[question.Question];
                    }
                    if (answerToken == null && !string.IsNullOrEmpty(question.Header))
                    {
                        answerToken = flatAnswers[question.Header];
                    }
                    var answerStr = answerToken?.Value<string>();
                    if (string.IsNullOrEmpty(answerStr)) continue;

                    string[] labels;
                    if (question.MultiSelect)
                    {
                        labels = answerStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    }
                    else
                    {
                        labels = new[] { answerStr };
                    }

                    if (labels.Length == 0) continue;

                    answers[key] = new JObject
                    {
                        [AnswersProperty] = new JArray(labels)
                    };
                }
            }

            return new JObject
            {
                [AnswersProperty] = answers
            };
        }

        public static bool IsCancelled(JToken traceInput)
        {
            return traceInput?[CancelledProperty]?.Value<bool>() == true;
        }

        public static int GetQuestionCount(JToken traceInput)
        {
            var summaryCount = traceInput?[SummaryProperty]?[QuestionCountProperty]?.Value<int?>();
            if (summaryCount.HasValue)
            {
                return summaryCount.Value;
            }

            return GetQuestions(traceInput)?.Questions?.Count ?? 0;
        }

        public static AskUserQuestionInput GetQuestions(JToken traceInput)
        {
            return AskUserQuestionInput.FromJToken(traceInput);
        }

        private static JObject EnsureTraceInput(JToken existingInput)
        {
            if (existingInput is JObject existingObject)
            {
                var cloned = (JObject)existingObject.DeepClone();
                if (cloned[QuestionsProperty] is JArray)
                {
                    if (cloned[SummaryProperty] == null)
                    {
                        cloned[SummaryProperty] = new JObject
                        {
                            [QuestionCountProperty] = GetQuestionCount(cloned)
                        };
                    }

                    return cloned;
                }
            }

            return BuildTraceInput(existingInput);
        }

        private static JObject ExtractAnswers(JObject response)
        {
            return response?[AnswersProperty] is JObject answers
                ? (JObject)answers.DeepClone()
                : new JObject();
        }

        private static JArray NormalizeQuestions(JToken rawInput)
        {
            var questionsToken = rawInput;
            if (questionsToken is JObject questionObject && questionObject[QuestionsProperty] != null)
            {
                questionsToken = questionObject[QuestionsProperty];
            }

            if (questionsToken is not JArray rawQuestions)
            {
                return new JArray();
            }

            var normalized = new JArray();
            foreach (var questionToken in rawQuestions.OfType<JObject>())
            {
                normalized.Add(NormalizeQuestion(questionToken));
            }

            return normalized;
        }

        private static JObject NormalizeQuestion(JObject question)
        {
            var options = new JArray();
            if (question["options"] is JArray optionArray)
            {
                foreach (var optionToken in optionArray.OfType<JObject>())
                {
                    options.Add(new JObject
                    {
                        ["label"] = optionToken["label"]?.Value<string>() ?? string.Empty,
                        ["description"] = optionToken["description"]?.Value<string>() ?? string.Empty
                    });
                }
            }

            var isOther = question["isOther"]?.Value<bool>() == true;
            if (isOther && !options.OfType<JObject>().Any(option => string.Equals(option["label"]?.Value<string>(), "Other", StringComparison.Ordinal)))
            {
                options.Add(new JObject
                {
                    ["label"] = "Other",
                    ["description"] = "Provide a custom answer."
                });
            }

            if (options.Count == 0)
            {
                options.Add(new JObject
                {
                    ["label"] = "Other",
                    ["description"] = "Provide a custom answer."
                });
                isOther = true;
            }

            return new JObject
            {
                ["id"] = question["id"]?.Value<string>(),
                ["header"] = question["header"]?.Value<string>(),
                ["question"] = question["question"]?.Value<string>(),
                ["options"] = options,
                ["multiSelect"] = question["multiSelect"]?.Value<bool>() == true,
                ["isOther"] = isOther,
                ["isSecret"] = question["isSecret"]?.Value<bool>() == true
            };
        }
    }
}
