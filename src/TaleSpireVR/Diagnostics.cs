using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace TaleSpireVR
{
    internal static class Diagnostics
    {
        public static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        public static void Dump(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== TaleSpireVR diagnostics ({reason}) frame={Time.frameCount} =====");
            sb.AppendLine($"XR: enabled={XRSettings.enabled} active={XRSettings.isDeviceActive} device='{XRSettings.loadedDeviceName}' eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} screen={Screen.width}x{Screen.height} fullscreen={Screen.fullScreenMode}");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine($"Scene[{i}] '{s.name}' loaded={s.isLoaded} roots={s.rootCount}");
            }

            foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (cam.gameObject.scene.name == null) continue; // prefab assets
                var comps = string.Join(",", cam.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name).ToArray());
                sb.AppendLine($"CAM '{PathOf(cam.transform)}' scene='{cam.gameObject.scene.name}' active={cam.isActiveAndEnabled} depth={cam.depth} tex={(cam.targetTexture ? cam.targetTexture.name + "(" + cam.targetTexture.width + "x" + cam.targetTexture.height + ")" : "none")} eye={cam.stereoTargetEye} stereo={cam.stereoEnabled} mask=0x{cam.cullingMask:X8} clear={cam.clearFlags} path={cam.actualRenderingPath} near={cam.nearClipPlane} far={cam.farClipPlane} fov={cam.fieldOfView:F1} pos={cam.transform.position} cmdBufs={cam.commandBufferCount} comps=[{comps}]");
            }

            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas.gameObject.scene.name == null || !canvas.isRootCanvas) continue;
                var scaler = canvas.GetComponent<CanvasScaler>();
                sb.AppendLine($"CANVAS '{PathOf(canvas.transform)}' scene='{canvas.gameObject.scene.name}' active={canvas.isActiveAndEnabled} mode={canvas.renderMode} cam={(canvas.worldCamera ? canvas.worldCamera.name : "none")} order={canvas.sortingOrder} layer={LayerMask.LayerToName(canvas.gameObject.layer)}({canvas.gameObject.layer}) planeDist={canvas.planeDistance} pixelRect={canvas.pixelRect} scaler={(scaler ? scaler.uiScaleMode + "/" + scaler.scaleFactor + "/" + scaler.referenceResolution : "none")} raycaster={(canvas.GetComponent<GraphicRaycaster>() != null)} children={canvas.transform.childCount}");
            }

            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                int visible = 0;
                var names = new StringBuilder();
                foreach (var g in canvas.GetComponentsInChildren<Graphic>(false))
                {
                    if (!g.enabled || g.color.a <= 0.01f || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                    visible++;
                    if (visible <= 12) names.Append(g.name).Append(g is Text || g.GetType().Name.Contains("TextMesh") ? "(text)" : "").Append(", ");
                }
                sb.AppendLine($"VISIBLE-UI '{canvas.name}' graphics={visible} e.g. [{names}]");
            }

            var es = EventSystem.current;
            sb.AppendLine($"EventSystem={(es ? PathOf(es.transform) : "none")} module={(es && es.currentInputModule ? es.currentInputModule.GetType().FullName : "none")}");
            var mouse = UnityEngine.InputSystem.Mouse.current;
            sb.AppendLine($"Mouse.current={(mouse != null ? mouse.name + " pos=" + mouse.position.ReadValue() : "none")} devices={string.Join(",", UnityEngine.InputSystem.InputSystem.devices.Select(d => d.name).ToArray())}");
            for (int l = 0; l < 32; l++)
            {
                var n = LayerMask.LayerToName(l);
                sb.Append($"{l}:{(string.IsNullOrEmpty(n) ? "-" : n)} ");
            }
            sb.AppendLine();
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
