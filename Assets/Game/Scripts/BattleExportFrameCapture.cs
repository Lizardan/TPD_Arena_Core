using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TPD.Arena
{
    /// <summary>
    /// Renders battle frames to an offscreen RenderTexture so Game View does not play back during export.
    /// </summary>
    public class BattleExportFrameCapture
    {
        private struct CanvasSnapshot
        {
            public Canvas canvas;
            public RenderMode renderMode;
            public Camera worldCamera;
            public float planeDistance;
            public Vector3 localScale;
            public bool overrideSorting;
            public int sortingOrder;
        }

        private readonly List<CanvasSnapshot> canvasSnapshots = new List<CanvasSnapshot>();
        private Camera camera;
        private RenderTexture renderTexture;
        private Texture2D readbackTexture;
        private RenderTexture previousCameraTarget;

        public bool Begin(Camera captureCamera, int width, int height)
        {
            if (captureCamera == null)
                return false;

            camera = captureCamera;
            renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            readbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled)
                    continue;

                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    continue;

                if (canvas.name == "CanvasDEBUG")
                    continue;

                RectTransform canvasRect = canvas.GetComponent<RectTransform>();
                Vector3 previousScale = canvasRect != null ? canvasRect.localScale : Vector3.one;

                canvasSnapshots.Add(new CanvasSnapshot
                {
                    canvas = canvas,
                    renderMode = canvas.renderMode,
                    worldCamera = canvas.worldCamera,
                    planeDistance = canvas.planeDistance,
                    localScale = previousScale,
                    overrideSorting = canvas.overrideSorting,
                    sortingOrder = canvas.sortingOrder
                });

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                if (canvas.planeDistance <= camera.nearClipPlane)
                    canvas.planeDistance = camera.nearClipPlane + 1f;

                // Overlay canvases often use (0,0,0) root scale; invisible in camera mode.
                if (canvasRect != null && canvasRect.localScale.sqrMagnitude < 0.01f)
                    canvasRect.localScale = Vector3.one;

                canvas.overrideSorting = true;
                if (canvas.sortingOrder < 100)
                    canvas.sortingOrder = 100;
            }

            previousCameraTarget = camera.targetTexture;
            camera.targetTexture = renderTexture;

            return true;
        }

        public Texture2D Capture()
        {
            Canvas.ForceUpdateCanvases();
            RenderToTexture();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            readbackTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            readbackTexture.Apply();
            RenderTexture.active = previousActive;

            return readbackTexture;
        }

        private void RenderToTexture()
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = renderTexture
            };

            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                return;
            }

            RenderTexture previousTarget = camera.targetTexture;
            bool restoreEnabled = camera.enabled;
            try
            {
                camera.enabled = true;
                camera.targetTexture = renderTexture;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.enabled = restoreEnabled;
            }
        }

        public void End()
        {
            if (camera != null)
                camera.targetTexture = previousCameraTarget;

            foreach (CanvasSnapshot snapshot in canvasSnapshots)
            {
                if (snapshot.canvas == null)
                    continue;

                snapshot.canvas.renderMode = snapshot.renderMode;
                snapshot.canvas.worldCamera = snapshot.worldCamera;
                snapshot.canvas.planeDistance = snapshot.planeDistance;
                snapshot.canvas.overrideSorting = snapshot.overrideSorting;
                snapshot.canvas.sortingOrder = snapshot.sortingOrder;

                RectTransform canvasRect = snapshot.canvas.GetComponent<RectTransform>();
                if (canvasRect != null)
                    canvasRect.localScale = snapshot.localScale;
            }

            canvasSnapshots.Clear();

            if (readbackTexture != null)
            {
                Object.Destroy(readbackTexture);
                readbackTexture = null;
            }

            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
                renderTexture = null;
            }

            camera = null;
        }
    }
}
