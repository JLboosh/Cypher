using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// A two-choice holographic question, answerable by click or by voice. Cypher also asks it
    /// out loud. Used for "keep my placement?" and "also delete it in Google/Notion?".
    /// Questions asked while one is open wait in line.
    /// </summary>
    public class HoloPrompt : MonoBehaviour
    {
        const float W = 540f, H = 200f, UnitsToMeters = 0.001f;

        class Request
        {
            public string Question, OptionA, OptionB, Spoken, ReplyA, ReplyB;
            public Regex VoiceA, VoiceB;
            public Action<bool> OnAnswer; // true = option A
        }

        static HoloPrompt current;
        static readonly Queue<Request> waiting = new Queue<Request>();
        Request request;

        public static bool IsOpen => current != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            waiting.Clear();
        }

        /// <param name="voiceA">Regex (on lower-case, punctuation-free speech) that means option A.</param>
        public static void Show(string question, string optionA, string optionB, Action<bool> onAnswer,
            string voiceA, string voiceB, string spoken, string replyA, string replyB)
        {
            var r = new Request
            {
                Question = question, OptionA = optionA, OptionB = optionB, OnAnswer = onAnswer,
                VoiceA = new Regex(voiceA), VoiceB = new Regex(voiceB), Spoken = spoken, ReplyA = replyA, ReplyB = replyB,
            };
            if (current != null) waiting.Enqueue(r);
            else Open(r);
        }

        /// <summary>For voice: answers the open question if the speech matches one option.</summary>
        public static bool TryVoiceAnswer(string speech, out string reply)
        {
            reply = null;
            if (current == null) return false;
            string t = NaturalDate.Normalize(speech);
            var r = current.request;
            bool a = r.VoiceA.IsMatch(t), b = r.VoiceB.IsMatch(t);
            if (a == b) return false; // neither, or ambiguous
            reply = a ? r.ReplyA : r.ReplyB;
            current.Close(a);
            return true;
        }

        public static string CurrentSpokenQuestion => current?.request.Spoken;
        public static string CurrentOptions => current == null ? "" : $"Say {current.request.OptionA.ToLowerInvariant()}, or {current.request.OptionB.ToLowerInvariant()}.";

        static void Open(Request r)
        {
            var editPanel = EditPanelController.Instance;
            if (editPanel == null || editPanel.UI == null || Camera.main == null)
            {
                r.OnAnswer?.Invoke(false);
                return;
            }
            current = new GameObject("Holo Prompt").AddComponent<HoloPrompt>();
            current.request = r;
            current.Build(editPanel.UI);
            InputLock.Acquire(current);
            CypherAudio.Play(Sfx.Open, current.transform.position, 0.6f);
            if (VoiceAssistant.Instance != null) VoiceAssistant.Instance.AskOpenPrompt();
        }

        void Build(HoloUI ui)
        {
            var cam = Camera.main.transform;
            transform.SetPositionAndRotation(cam.position + cam.forward * 0.7f - cam.up * 0.05f, Quaternion.LookRotation(cam.forward, cam.up));

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 20;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(W, H);
            rect.localScale = Vector3.one * UnitsToMeters;

            ui.Glass(rect, W, H, 18);
            ui.Frame("Frame", rect, 0, 0, W, H, new Color(0.4f, 0.9f, 1f, 1f));
            var text = ui.Label("Question", rect, 24, 20, W - 48, 84, request.Question, 18, TextAlignmentOptions.Center, HoloUI.TextColor);
            text.textWrappingMode = TextWrappingModes.Normal;
            ui.Button("A", rect, 24, 120, 230, 50, request.OptionA, 16, () => Close(true));
            ui.Button("B", rect, W - 254, 120, 230, 50, request.OptionB, 16, () => Close(false));
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close(false); // Esc = the safe option (B)
        }

        void Close(bool optionA)
        {
            if (current != this) return;
            InputLock.Release(this);
            current = null;
            CypherAudio.Play(Sfx.Close, transform.position, 0.6f);
            Destroy(gameObject);
            try { request.OnAnswer?.Invoke(optionA); }
            catch (Exception e) { Debug.LogException(e); }
            if (waiting.Count > 0) Open(waiting.Dequeue());
        }

        void OnDestroy()
        {
            InputLock.Release(this);
            if (current == this) current = null;
        }
    }
}
