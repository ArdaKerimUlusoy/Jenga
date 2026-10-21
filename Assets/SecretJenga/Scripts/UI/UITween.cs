using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    /// <summary>Tiny unscaled-time tween helper for UI (fade, scale pop, slide) - no external packages.</summary>
    public class UITween : MonoBehaviour
    {
        static UITween _runner;
        readonly Dictionary<int, Coroutine> _running = new Dictionary<int, Coroutine>();

        static UITween Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("UITweenRunner");
                    DontDestroyOnLoad(go);
                    _runner = go.AddComponent<UITween>();
                }
                return _runner;
            }
        }

        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }

        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3);

        static void Run(UnityEngine.Object key, IEnumerator routine)
        {
            var r = Runner;
            int id = key.GetInstanceID();
            if (r._running.TryGetValue(id, out var c) && c != null) r.StopCoroutine(c);
            r._running[id] = r.StartCoroutine(routine);
        }

        public static void Fade(CanvasGroup g, float to, float duration, Action done = null)
        {
            if (g == null) return;
            Run(g, FadeRoutine(g, to, duration, done));
        }

        static IEnumerator FadeRoutine(CanvasGroup g, float to, float duration, Action done)
        {
            float from = g.alpha, t = 0f;
            while (t < duration && g != null)
            {
                t += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, EaseOutCubic(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            if (g != null) g.alpha = to;
            done?.Invoke();
        }

        public static void Scale(Transform tr, Vector3 from, Vector3 to, float duration, bool back = true)
        {
            if (tr == null) return;
            Run(tr, ScaleRoutine(tr, from, to, duration, back));
        }

        static IEnumerator ScaleRoutine(Transform tr, Vector3 from, Vector3 to, float duration, bool back)
        {
            float t = 0f;
            while (t < duration && tr != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                tr.localScale = Vector3.LerpUnclamped(from, to, back ? EaseOutBack(k) : EaseOutCubic(k));
                yield return null;
            }
            if (tr != null) tr.localScale = to;
        }

        public static void Pop(Transform tr, float amount = 1.15f, float duration = 0.25f) => Scale(tr, Vector3.one * amount, Vector3.one, duration);

        public static void SlideIn(RectTransform rt, Vector2 offset, float duration)
        {
            if (rt == null) return;
            var end = rt.anchoredPosition;
            Run(rt, SlideRoutine(rt, end + offset, end, duration));
        }

        static IEnumerator SlideRoutine(RectTransform rt, Vector2 from, Vector2 to, float duration)
        {
            float t = 0f;
            while (t < duration && rt != null)
            {
                t += Time.unscaledDeltaTime;
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, EaseOutBack(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = to;
        }

        public static void Delay(float seconds, Action a) => Runner.StartCoroutine(DelayRoutine(seconds, a));

        static IEnumerator DelayRoutine(float s, Action a)
        {
            yield return new WaitForSecondsRealtime(s);
            a?.Invoke();
        }
    }

    /// <summary>Hover/click scale feedback + UI sounds for any selectable.</summary>
    public class UIButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float scaleOnHover = 1.05f;
        Selectable _sel;

        void Awake() => _sel = GetComponent<Selectable>();

        bool Usable => _sel == null || _sel.IsInteractable();

        public void OnPointerEnter(PointerEventData e)
        {
            if (!Usable) return;
            if (scaleOnHover > 1f) UITween.Scale(transform, transform.localScale, Vector3.one * scaleOnHover, 0.15f, false);
            AudioManager.Instance?.Play(AudioManager.Sfx.UIHover, 0.5f);
        }

        public void OnPointerExit(PointerEventData e)
        {
            UITween.Scale(transform, transform.localScale, Vector3.one, 0.15f, false);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Usable) return;
            UITween.Scale(transform, transform.localScale, Vector3.one * 0.95f, 0.08f, false);
            AudioManager.Instance?.Play(AudioManager.Sfx.UIClick, 0.8f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            UITween.Scale(transform, transform.localScale, Vector3.one * (Usable ? scaleOnHover : 1f), 0.2f);
        }

        void OnDisable() => transform.localScale = Vector3.one;
    }
}
