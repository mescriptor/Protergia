using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace EnergyCorner
{
    /// <summary>Native-wall video playlist. The entire layer fades inside the wall composition.</summary>
    public class AttractVideoPlayer : MonoBehaviour
    {
        public EnergyCornerController controller;
        public VideoPlayer player;
        public RawImage target;
        public CanvasGroup layer;
        public Text noVideoLabel;

        public bool IsActive => active;
        public int PlaylistCount => files.Count;
        public string CurrentVideo => files.Count > 0 ? Path.GetFileName(files[index]) : "";
        public string Status { get; private set; } = "waiting";
        public bool HasVideo { get; private set; }

        static readonly string[] Extensions = { ".mp4", ".m4v", ".mov", ".webm", ".avi", ".wmv" };
        const float PrepareTimeoutSec = 15f;
        readonly List<string> files = new List<string>();
        int index, consecutiveErrors;
        RenderTexture renderTexture;
        Coroutine fade;
        bool active, preparing, started;
        float prepareStartedAt;

        void Awake()
        {
            var cfg = EnergyCornerConfig.Current;
            if (!player || !target || !layer)
            {
                Debug.LogError("[EC] Attract video requires VideoPlayer, RawImage and CanvasGroup references.");
                enabled = false;
                return;
            }
            // Compose video at wall-native size, before stretching the entire frame to the transport feed.
            renderTexture = new RenderTexture(cfg.videoWidth, cfg.videoHeight, 0)
            {
                name = "Energy Corner native video", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            renderTexture.Create();
            ClearTexture();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = renderTexture;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.audioOutputMode = cfg.videoAudio ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.loopPointReached += OnVideoFinished;
            player.errorReceived += OnVideoError;
            player.prepareCompleted += OnPrepared;
            player.frameReady += OnFirstFrame;
            target.texture = renderTexture;
            target.raycastTarget = false;
            LoadPlaylist(cfg);
            layer.alpha = 0f;
            layer.blocksRaycasts = false;
            ShowFallback(true);
        }

        void OnEnable()
        {
            if (!controller) return;
            controller.onEnterAttract.AddListener(EnterAttract);
            controller.onExitAttract.AddListener(ExitAttract);
            if (started && controller.State == CornerState.Attract) EnterAttract();
        }

        void Start()
        {
            started = true;
            // Also supports a later enable after the controller's startup event.
            if (controller && controller.State == CornerState.Attract) EnterAttract();
        }

        void OnDisable()
        {
            if (controller)
            {
                controller.onEnterAttract.RemoveListener(EnterAttract);
                controller.onExitAttract.RemoveListener(ExitAttract);
            }
            if (fade != null) StopCoroutine(fade);
            fade = null;
            active = preparing = false;
            if (player) player.Stop();
            HasVideo = false;
            if (layer) { layer.alpha = 0f; layer.blocksRaycasts = false; }
        }

        void OnDestroy()
        {
            if (player)
            {
                player.loopPointReached -= OnVideoFinished;
                player.errorReceived -= OnVideoError;
                player.prepareCompleted -= OnPrepared;
                player.frameReady -= OnFirstFrame;
                if (player.targetTexture == renderTexture) player.targetTexture = null;
            }
            if (target && target.texture == renderTexture) target.texture = null;
            if (renderTexture) { renderTexture.Release(); Destroy(renderTexture); }
        }

        void Update()
        {
            if (active && preparing && Time.unscaledTime - prepareStartedAt > PrepareTimeoutSec)
                OnVideoError(player, "No playable frame after " + PrepareTimeoutSec + " seconds");
        }

        void LoadPlaylist(EnergyCornerConfig cfg)
        {
            string folder = Path.IsPathRooted(cfg.videoFolder ?? "") ? cfg.videoFolder :
                Path.Combine(Application.streamingAssetsPath, cfg.videoFolder ?? "Videos");
            try
            {
                if (Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception e) { Debug.LogError("[EC] Cannot read video playlist: " + e.Message); }
            Debug.Log("[EC] " + files.Count + " attract video(s) in " + folder);
        }

        void EnterAttract()
        {
            if (active || !player || !renderTexture) return;
            active = true;
            consecutiveErrors = 0;
            PlayCurrent();
            FadeTo(1f, false);
        }

        void ExitAttract()
        {
            if (!active) return;
            active = false;
            preparing = false;
            FadeTo(0f, true);
            if (files.Count > 0) index = (index + 1) % files.Count;
        }

        void PlayCurrent()
        {
            player.Stop();
            HasVideo = false;
            preparing = false;
            ShowFallback(true);
            ClearTexture();
            if (files.Count == 0) { Status = "no videos (welcome screen)"; return; }
            player.isLooping = files.Count == 1;
            player.url = files[index];
            player.sendFrameReadyEvents = true;
            Status = "loading";
            preparing = true;
            prepareStartedAt = Time.unscaledTime;
            player.Prepare();
        }

        void OnPrepared(VideoPlayer p)
        {
            if (active && preparing) p.Play();
        }

        void OnFirstFrame(VideoPlayer p, long frame)
        {
            if (!active || !preparing) return;
            preparing = false;
            consecutiveErrors = 0;
            HasVideo = true;
            Status = "playing";
            ShowFallback(false);
            p.sendFrameReadyEvents = false;
        }

        void OnVideoFinished(VideoPlayer p)
        {
            if (!active || files.Count <= 1) return;
            index = (index + 1) % files.Count;
            PlayCurrent();
        }

        void OnVideoError(VideoPlayer p, string message)
        {
            if (!active) return;
            Debug.LogWarning("[EC] Video unavailable (" + CurrentVideo + "): " + message);
            preparing = false;
            HasVideo = false;
            p.Stop();
            ClearTexture();
            ShowFallback(true);
            if (files.Count == 0 || ++consecutiveErrors >= files.Count)
            {
                Status = "videos unavailable (welcome screen)";
                return;
            }
            index = (index + 1) % files.Count;
            PlayCurrent();
        }

        void ShowFallback(bool show)
        {
            if (target) target.enabled = !show;
            if (!noVideoLabel) return;
            noVideoLabel.gameObject.SetActive(show);
            noVideoLabel.text = "Ενεργειακή τομή κατοικίας\n\nΑνακάλυψε τις ενεργειακές λύσεις για το σπίτι σου";
        }

        void FadeTo(float alpha, bool stopWhenDone)
        {
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(FadeRoutine(alpha, stopWhenDone));
        }

        IEnumerator FadeRoutine(float alpha, bool stopWhenDone)
        {
            float duration = EnergyCornerConfig.Current.videoFadeSec;
            layer.blocksRaycasts = alpha > 0f;
            float start = layer.alpha;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                layer.alpha = Mathf.Lerp(start, alpha, t / duration);
                yield return null;
            }
            layer.alpha = alpha;
            if (stopWhenDone)
            {
                player.Stop();
                HasVideo = false;
                Status = "paused";
                ClearTexture();
            }
            fade = null;
        }

        void ClearTexture()
        {
            if (!renderTexture) return;
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }
    }
}
