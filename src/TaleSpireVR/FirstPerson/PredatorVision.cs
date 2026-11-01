using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace TaleSpireVR
{
    /// <summary>
    /// "Predator Vision" in first person, while both trackpads are held: the world goes almost grey, while every creature
    /// you can see keeps its full colour and gets a hazy blood-red glow around it (to show the players where everyone is).
    /// Not X-ray vision: walls still hide creatures. Built from shaders the game already has (it cannot load new ones):
    ///  1. before transparent things are drawn, the visible creature models are drawn white into a mask, depth-tested
    ///     against the board (so what is behind a wall stays out of it);
    ///  2. after post-processing, the finished picture is copied; the copy goes back into the eye grey (PPv2's own Uber
    ///     shader with a grey colour-grading lookup table); the mask, blurred by halving it a few times with bilinear
    ///     filtering, is laid over that in red; and the copy is laid back where the mask is (via its alpha) - so creatures
    ///     are in full colour and the red glow only shows around them.
    /// The stream view shows the left eye in first person, so the players see it too.
    /// </summary>
    [DefaultExecutionOrder(30040)]
    internal class PredatorVision : MonoBehaviour
    {
        /// <summary>How strong the effect is now (0 = off; it fades in and out over FadeTime).</summary>
        public static float Amount { get; private set; }
        /// <summary>Debug ("predator on|off"): show it without holding the trackpads (still only in first person).</summary>
        public static bool DebugForce;
        /// <summary>
        /// The grey pass draws PPv2's full-screen triangle straight into the eye, which (D3D11) needs its texture
        /// coordinates flipped; debug "predator flip" toggles it if a picture ever comes out upside down.
        /// </summary>
        public static bool FlipGrey = true;

        private const float FadeTime = 0.15f;
        /// <summary>How much colour the world loses, and how much darker it gets (so the creatures stand out).</summary>
        private const float Desaturate = 0.9f, Darken = 0.8f;
        private static readonly Color GlowColor = new Color(0.75f, 0.04f, 0.03f, 0.9f);
        /// <summary>The wide haze relative to the tight edge glow.</summary>
        private const float HazeStrength = 0.8f;

        private Camera _cam;
        private CommandBuffer _maskCb, _compositeCb;
        private bool _attached, _failed;
        private Material _maskMat, _greyMat, _glowMat, _hazeMat, _alphaOneMat, _alphaMulMat, _maskToAlphaMat, _restoreMat;
        private Texture2D _lut;
        private RenderTexture _mask;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private float _nextScan;
        private int _logs;

        private static readonly int ColorCopyId = Shader.PropertyToID("_TaleSpireVR_PredatorColour");
        private static readonly int[] BlurIds =
        {
            Shader.PropertyToID("_TaleSpireVR_PredatorBlur2"), Shader.PropertyToID("_TaleSpireVR_PredatorBlur4"),
            Shader.PropertyToID("_TaleSpireVR_PredatorBlur8"), Shader.PropertyToID("_TaleSpireVR_PredatorBlur16"),
            Shader.PropertyToID("_TaleSpireVR_PredatorBlur32"), Shader.PropertyToID("_TaleSpireVR_PredatorBlur64"),
        };
        private static readonly int GlowId = Shader.PropertyToID("_TaleSpireVR_PredatorGlow");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private float _bothFor;

        private void LateUpdate()
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            bool fp = FirstPersonMode.Active && !FirstPersonMode.Transitioning;
            // Both trackpads held. On controllers without trackpads the grips stand in for them - and a grip also shows
            // that hand's pointer - so there both have to stay squeezed for a moment first. Game Masters only.
            bool both = SteamVRInput.RotateModifier.Held && SteamVRInput.ElevateModifier.Held;
            _bothFor = both ? _bothFor + Time.unscaledDeltaTime : 0f;
            bool held = both && (ControllerProfiles.HasTrackpads || _bothFor >= 0.6f);
            bool want = fp && Roles.IsGm && (DebugForce || held);
            Amount = Mathf.MoveTowards(Amount, want ? 1f : 0f, Time.unscaledDeltaTime / FadeTime);
            if (!fp) Amount = 0f;
            var cam = rig != null && rig.OnBoard ? rig.GameCamera : null;
            if (Amount <= 0f || cam == null || _failed) { Detach(); return; }
            if (cam != _cam) { Detach(); _cam = cam; }
            try
            {
                if (!EnsureResources(cam)) { Detach(); return; }
                if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 0.5f; ScanCreatures(); }
                BuildBuffers();
                if (!_attached)
                {
                    cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _maskCb);
                    cam.AddCommandBuffer(CameraEvent.AfterImageEffects, _compositeCb);
                    _attached = true;
                    Plugin.Log.LogInfo($"Predator vision on: {_renderers.Count} creature renderers, eye {cam.pixelWidth}x{cam.pixelHeight}, mask shader '{_maskMat.shader.name}'");
                }
            }
            catch (System.Exception e)
            {
                _failed = true;
                Detach();
                Plugin.Log.LogError("Predator vision failed (off until restart): " + e);
            }
        }

        private void Detach()
        {
            if (_attached && _cam != null)
            {
                _cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _maskCb);
                _cam.RemoveCommandBuffer(CameraEvent.AfterImageEffects, _compositeCb);
                Plugin.Log.LogInfo("Predator vision off");
            }
            _attached = false;
        }

        /// <summary>The creatures' models (not their effects, and not the creature you are, which is hidden).</summary>
        private void ScanCreatures()
        {
            _renderers.Clear();
            var me = FirstPersonMode.Creature;
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>())
            {
                if (c == null || c == me) continue;
                foreach (var r in c.GetComponentsInChildren<Renderer>())
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (r.GetComponentInParent<ActionTimeline>() != null) continue;
                    _renderers.Add(r);
                }
            }
        }

        private void BuildBuffers()
        {
            // 1. The visible creatures, white, into the mask (our colour target with the camera's depth).
            _maskCb.Clear();
            _maskCb.SetRenderTarget(_mask, BuiltinRenderTextureType.CameraTarget);
            _maskCb.ClearRenderTarget(false, true, new Color(1f, 1f, 1f, 0f));
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                int subs = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.sharedMesh.subMeshCount
                         : r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                for (int s = 0; s < subs; s++) _maskCb.DrawRenderer(r, _maskMat, s, 0);
            }
            _maskCb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

            // 2. After post-processing: grey world, red glow, creatures back in colour.
            var cb = _compositeCb;
            cb.Clear();
            var target = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            cb.GetTemporaryRT(ColorCopyId, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            cb.Blit(target, ColorCopyId);

            _greyMat.SetVector("_UVTransform", FlipGrey ? new Vector4(1f, -1f, 0f, 1f) : new Vector4(1f, 1f, 0f, 0f));
            cb.SetGlobalTexture(MainTexId, ColorCopyId);
            cb.SetRenderTarget(target);
            cb.DrawMesh(RuntimeUtilities.fullscreenTriangle, Matrix4x4.identity, _greyMat, 0, 0);

            // The glow: the mask halved six times (each bilinear step averages 2 x 2). A tight, strong edge from the
            // 1/8 step (laid on twice), and a wide haze from the 1/64 step brought back up to 1/16 in steps (smooth).
            RenderTargetIdentifier prev = _mask;
            for (int i = 0; i < BlurIds.Length; i++)
            {
                cb.GetTemporaryRT(BlurIds[i], -(2 << i), -(2 << i), 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                cb.Blit(prev, BlurIds[i]);
                prev = BlurIds[i];
            }
            cb.GetTemporaryRT(GlowId, -16, -16, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cb.Blit(BlurIds[5], BlurIds[4]);
            cb.Blit(BlurIds[4], GlowId);
            var glow = GlowColor;
            glow.a *= Amount;
            _glowMat.color = glow;
            cb.Blit(BlurIds[2], target, _glowMat);
            cb.Blit(BlurIds[2], target, _glowMat);
            glow.a *= HazeStrength;
            _hazeMat.color = glow;
            cb.Blit(GlowId, target, _hazeMat);

            // Creatures (and, while fading, everything) back in colour: the copy's alpha = max(mask, 1 - amount).
            _alphaMulMat.color = new Color(1f, 1f, 1f, 1f - Amount);
            cb.Blit(Texture2D.whiteTexture, ColorCopyId, _alphaOneMat);
            cb.Blit(Texture2D.whiteTexture, ColorCopyId, _alphaMulMat);
            cb.Blit(_mask, ColorCopyId, _maskToAlphaMat);
            cb.Blit(ColorCopyId, target, _restoreMat);

            cb.ReleaseTemporaryRT(ColorCopyId);
            foreach (int id in BlurIds) cb.ReleaseTemporaryRT(id);
            cb.ReleaseTemporaryRT(GlowId);

            if (_logs++ < 3) Plugin.Log.LogInfo($"Predator vision buffers: {_renderers.Count} renderers, amount {Amount:F2}");
        }

        private bool EnsureResources(Camera cam)
        {
            int w = Mathf.Max(1, cam.pixelWidth), h = Mathf.Max(1, cam.pixelHeight);
            if (_mask == null || _mask.width != w || _mask.height != h)
            {
                if (_mask != null) _mask.Release();
                _mask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "TaleSpireVR_PredatorMask", filterMode = FilterMode.Bilinear };
                _mask.Create();
            }
            if (_maskCb != null) return true;

            var layer = cam.GetComponent<PostProcessLayer>();
            var res = layer != null ? AccessTools.Field(typeof(PostProcessLayer), "m_Resources")?.GetValue(layer) as PostProcessResources : null;
            var uber = res != null ? res.shaders.uber : null;
            var ui = Shader.Find("UI/Default");
            var colored = Shader.Find("Hidden/Internal-Colored");
            if (uber == null || ui == null || colored == null)
            {
                _failed = true;
                Plugin.Log.LogError($"Predator vision: shaders missing (uber={uber != null}, UI/Default={ui != null}, Internal-Colored={colored != null})");
                return false;
            }

            // The mask: plain white where the creature models are, depth-tested, without writing depth.
            var unlit = Shader.Find("Unlit/Color");
            _maskMat = unlit != null ? new Material(unlit) : new Material(ui);
            _maskMat.name = "TaleSpireVR_PredatorMask";
            _maskMat.color = Color.white;
            if (unlit == null) _maskMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);

            _lut = MakeGreyLut();
            _greyMat = new Material(uber) { name = "TaleSpireVR_PredatorGrey" };
            _greyMat.EnableKeyword("COLOR_GRADING_LDR_2D");
            _greyMat.SetTexture("_AutoExposureTex", Texture2D.whiteTexture);
            _greyMat.SetTexture("_Lut2D", _lut);
            _greyMat.SetVector("_Lut2D_Params", new Vector4(1f / _lut.width, 1f / _lut.height, _lut.height - 1f, 0f));
            _greyMat.SetFloat("_PostExposure", 1f);

            _glowMat = UiMaterial(ui, "TaleSpireVR_PredatorGlow", (int)ColorWriteMask.All);
            _hazeMat = UiMaterial(ui, "TaleSpireVR_PredatorHaze", (int)ColorWriteMask.All);
            _maskToAlphaMat = UiMaterial(ui, "TaleSpireVR_PredatorMaskToAlpha", (int)ColorWriteMask.Alpha);
            _restoreMat = UiMaterial(ui, "TaleSpireVR_PredatorRestore", (int)ColorWriteMask.All);
            _alphaOneMat = ColoredMaterial(colored, "TaleSpireVR_PredatorAlphaOne", BlendMode.One, BlendMode.One, new Color(0f, 0f, 0f, 1f));
            _alphaMulMat = ColoredMaterial(colored, "TaleSpireVR_PredatorAlphaMul", BlendMode.Zero, BlendMode.SrcColor, Color.white);

            _maskCb = new CommandBuffer { name = "TaleSpireVR Predator Vision mask" };
            _compositeCb = new CommandBuffer { name = "TaleSpireVR Predator Vision" };
            return true;
        }

        /// <summary>UI/Default for full-screen layers: no depth test, alpha blending, the given colour channels (ColorWriteMask).</summary>
        private static Material UiMaterial(Shader ui, string name, int colorMask)
        {
            var m = new Material(ui) { name = name, color = Color.white };
            m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            m.SetInt("_ColorMask", colorMask);
            return m;
        }

        private static Material ColoredMaterial(Shader colored, string name, BlendMode src, BlendMode dst, Color c)
        {
            var m = new Material(colored) { name = name, color = c };
            m.SetInt("_SrcBlend", (int)src);
            m.SetInt("_DstBlend", (int)dst);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)CompareFunction.Always);
            m.SetInt("_Cull", (int)CullMode.Off);
            return m;
        }

        /// <summary>
        /// PPv2's 2D colour-grading lookup table (32 slices of 32 x 32, blue across the slices; values in sRGB): every
        /// colour mostly to its grey (Desaturate) and a little darker.
        /// </summary>
        private static Texture2D MakeGreyLut()
        {
            const int n = 32;
            var tex = new Texture2D(n * n, n, TextureFormat.RGBA32, false, true) { name = "TaleSpireVR_PredatorLut", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n * n; x++)
                {
                    var c = new Color((x % n) / (n - 1f), y / (n - 1f), (x / n) / (n - 1f), 1f);
                    float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    var o = Color.Lerp(c, new Color(l, l, l, 1f), Desaturate) * Darken;
                    o.a = 1f;
                    px[y * n * n + x] = o;
                }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        private void OnDisable() { Detach(); }

        private void OnDestroy()
        {
            Detach();
            if (_mask != null) _mask.Release();
        }

        public static string Status() => $"predator={Amount:F2} force={DebugForce} flip={FlipGrey}";
    }
}
