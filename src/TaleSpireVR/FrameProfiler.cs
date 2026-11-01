using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Developer aid for the status file: rough main-thread split of a frame (scripts vs. camera rendering, per
    /// camera) and Unity's render counters. Averages are smoothed over roughly the last second.
    /// </summary>
    internal class FrameProfiler : MonoBehaviour
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _frameStart, _renderStart = -1;
        private static float _scriptsMs, _renderMs;

        private class CamStat { public double Start; public float FrameSum; public float Avg; public int Renders; }
        private static readonly Dictionary<Camera, CamStat> Cams = new Dictionary<Camera, CamStat>();

        private ProfilerRecorder _draws, _setPass, _casters, _batches, _tris;
        private static FrameProfiler _instance;

        public static void MarkFrameStart()
        {
            _frameStart = Clock.Elapsed.TotalMilliseconds;
            _renderStart = -1;
        }

        private void OnEnable()
        {
            _instance = this;
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
            StartCoroutine(EndOfFrame());
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
            _draws.Dispose(); _setPass.Dispose(); _casters.Dispose(); _batches.Dispose(); _tris.Dispose();
        }

        private static void OnPreCull(Camera c)
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            if (_renderStart < 0) _renderStart = now;
            if (!Cams.TryGetValue(c, out var s)) Cams[c] = s = new CamStat();
            s.Start = now;
        }

        private static void OnPostRender(Camera c)
        {
            if (Cams.TryGetValue(c, out var s) && s.Start > 0)
            {
                s.FrameSum += (float)(Clock.Elapsed.TotalMilliseconds - s.Start);
                s.Renders++;
                s.Start = 0;
            }
        }

        private IEnumerator EndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                double now = Clock.Elapsed.TotalMilliseconds;
                if (_renderStart >= 0)
                {
                    _scriptsMs = Mathf.Lerp(_scriptsMs, (float)(_renderStart - _frameStart), 0.05f);
                    _renderMs = Mathf.Lerp(_renderMs, (float)(now - _renderStart), 0.05f);
                }
                foreach (var s in Cams.Values)
                {
                    s.Avg = Mathf.Lerp(s.Avg, s.FrameSum, 0.05f);
                    s.FrameSum = 0f;
                }
            }
        }

        public static string Status()
        {
            var sb = new StringBuilder();
            sb.Append($"cpu frame={Time.unscaledDeltaTime * 1000f:F1}ms scripts={_scriptsMs:F1}ms cameras={_renderMs:F1}ms [");
            foreach (var kv in Cams)
            {
                if (kv.Key == null || kv.Value.Avg < 0.05f) continue;
                sb.Append($"{kv.Key.name}={kv.Value.Avg:F2} ");
            }
            sb.Append("]");
            var p = _instance;
            if (p != null)
                sb.Append($" draws={p._draws.LastValue} setpass={p._setPass.LastValue} batches={p._batches.LastValue} shadowCasters={p._casters.LastValue} tris={p._tris.LastValue / 1000}k");
            return sb.ToString();
        }
    }
}
