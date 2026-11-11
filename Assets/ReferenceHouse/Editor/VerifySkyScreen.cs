using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

[InitializeOnLoad]
internal static class VerifySkyScreen
{
    const string Request = "Temp/SkyScreenVerify.request";
    const string Result = "Temp/SkyScreenVerify.result";
    const string House = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
    const int Width = 640, Height = 360;
    static double started, lastProbe;
    static bool playing;
    static string waiting;
    static PreviewRenderUtility preview;
    static VideoPlayer player;
    static RenderTexture output;
    static Color32[] first;
    static long firstFrame, lastFrame;

    static VerifySkyScreen()
    {
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        EditorApplication.quitting += Cleanup;
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (started == 0)
            {
                started = now;
                waiting = "Waiting for the active scene";
                if (File.Exists(Result)) File.Delete(Result);
            }
            if (now - started > 120)
                throw new TimeoutException(waiting + (player ? $" (prepared={player.isPrepared}, playing={player.isPlaying}, frame={player.frame})" : ""));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("The checker requires Edit mode.");
            if (!player)
            {
                if (now - lastProbe < 0.5) return;
                lastProbe = now;
                var scene = SceneManager.GetActiveScene();
                if (scene.path != House) return;
                var tv = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(item => item.name == "Living / TV mass");
                waiting = "Waiting for TV with imported clip in the active scene";
                var source = tv ? tv.GetComponentInChildren<VideoPlayer>(true) : null;
                if (!source || !source.clip || !source.targetMaterialRenderer) return;
                if (!(source.enabled && source.gameObject.activeInHierarchy && source.playOnAwake && source.isLooping))
                    throw new InvalidOperationException("The TV must be active with autoplay and loop enabled.");
                if (source.renderMode != VideoRenderMode.MaterialOverride ||
                    !source.targetMaterialRenderer.sharedMaterial.HasProperty(source.targetMaterialProperty))
                    throw new InvalidOperationException("The TV renderer has no matching material override.");
                MakePreview(tv);
                return;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            if (!player.isPrepared) return;
            if (!playing)
            {
                playing = true;
                waiting = "Waiting for movie playback";
                player.Play();
                return;
            }
            long frame = player.frame;
            if (!player.texture || frame < 20 || Math.Abs(frame - lastFrame) < (first == null ? 5 : 35)) return;
            lastFrame = frame;
            var pixels = Capture(first == null ? 1 : 2);
            if (first == null)
            {
                if (ColorRange(pixels) < 24) return;
                first = pixels;
                firstFrame = lastFrame = frame;
                waiting = "Waiting for a visually distinct second frame";
                return;
            }
            double total = 0;
            for (int i = 0; i < pixels.Length; i += 11)
            {
                var a = first[i];
                var b = pixels[i];
                total += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
            }
            double mean = total / (3 * Math.Ceiling(pixels.Length / 11.0));
            if (mean < 3 || ColorRange(pixels) < 24) return;
            Finish($"PASS\n{player.clip.name} ({player.clip.width}x{player.clip.height} @ {player.clip.frameRate:F2} fps)\n" +
                $"Frames {firstFrame} and {frame}; mean RGB change {mean:F2}/255.\n" +
                $"Renderer: {player.targetMaterialRenderer.name}; material property: {player.targetMaterialProperty}. Autoplay and loop enabled.");
        }
        catch (Exception ex) { Finish("FAIL\n" + ex); }
    }

    static void MakePreview(Transform tv)
    {
        preview = new PreviewRenderUtility();
        var camera = preview.camera;
        var clone = UnityEngine.Object.Instantiate(tv.gameObject, new InstantiateParameters { scene = camera.gameObject.scene });
        clone.transform.SetPositionAndRotation(Vector3.zero, tv.rotation);
        clone.transform.localScale = tv.lossyScale;
        player = clone.GetComponentInChildren<VideoPlayer>(true);
        var renderer = player.targetMaterialRenderer;
        if (!renderer || renderer.gameObject.scene != clone.scene)
            throw new InvalidOperationException("The material target does not belong to the TV clone.");
        var quad = renderer.transform;
        var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
        var center = quad.TransformPoint(mesh.bounds.center);
        var up = quad.TransformVector(Vector3.up);
        var right = quad.TransformVector(Vector3.right);
        var forward = Vector3.Cross(right, up).normalized;
        float width = mesh.bounds.size.x * right.magnitude;
        float height = mesh.bounds.size.y * up.magnitude;
        float distance = Mathf.Max(width, height, .1f);
        camera.transform.SetPositionAndRotation(center - forward * distance, Quaternion.LookRotation(forward, up));
        camera.nearClipPlane = distance * .01f;
        camera.farClipPlane = distance * 2f;
        camera.orthographic = true;
        camera.aspect = (float)Width / Height;
        camera.orthographicSize = Mathf.Max(height, width / camera.aspect) * .5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.cullingMask = ~0;
        camera.scene = clone.scene;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(clone.scene);
        output = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        output.Create();
        camera.targetTexture = output;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        player.errorReceived += VideoError;
        waiting = "Waiting for the decoder to prepare";
        player.Prepare();
        EditorApplication.QueuePlayerLoopUpdate();
    }

    static void VideoError(VideoPlayer _, string reason)
    {
        // Leave the callback before releasing the native decoder.
        EditorApplication.delayCall += () => { if (player) Finish("FAIL\nVideoPlayer: " + reason); };
    }

    static Color32[] Capture(int index)
    {
        var camera = preview.camera;
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = output };
        if (RenderPipeline.SupportsRenderRequest(camera, request))
            RenderPipeline.SubmitRenderRequest(camera, request);
        else camera.Render();
        var previous = RenderTexture.active;
        var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = output;
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            File.WriteAllBytes($"Temp/SkyScreenVerify-{index}.jpg", image.EncodeToJPG(85));
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    static int ColorRange(Color32[] pixels)
    {
        int min = 255, max = 0;
        for (int i = 0; i < pixels.Length; i += 11)
        {
            int value = (pixels[i].r + pixels[i].g + pixels[i].b) / 3;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        return max - min;
    }

    static void Finish(string message)
    {
        try { Cleanup(); }
        finally
        {
            File.Delete(Request);
            File.WriteAllText(Result, message);
        }
    }

    static void Cleanup()
    {
        if (player)
        {
            player.errorReceived -= VideoError;
            player.Stop();
        }
        if (preview != null)
        {
            preview.camera.targetTexture = null;
            preview.Cleanup();
        }
        if (output)
        {
            output.Release();
            UnityEngine.Object.DestroyImmediate(output);
        }
        preview = null;
        player = null;
        output = null;
        first = null;
        firstFrame = lastFrame = -1;
        started = lastProbe = 0;
        playing = false;
    }
}
