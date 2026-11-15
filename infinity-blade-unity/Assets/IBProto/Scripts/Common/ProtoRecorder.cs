using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using Unity.Collections;
using UnityEditor.Media;
#endif

// プレイ動画の録画（Unityエディタで遊んでいるときだけ動く）。
// F9 で録画開始／停止。プロジェクトの Captures フォルダに mp4（音つき・30fps・横1280まで）で保存する。
// 音を拾うため、AudioListener の付いたカメラに付ける。
public class ProtoRecorder : MonoBehaviour
{
    public bool Recording { get; private set; }
    public string LastPath { get; private set; }

#if UNITY_EDITOR
    const int FPS = 30;
    MediaEncoder enc;
    Texture2D frame; RenderTexture rt;
    int W, H, sr, channels;
    readonly object lk = new object();
    readonly Queue<float> audio = new Queue<float>();
    readonly byte[] lut = new byte[256]; bool fixGamma;

    public void Toggle() { if (Recording) Stop(); else Begin(); }

    void Begin()
    {
        int sw = Screen.width, sh = Screen.height;
        float sc = Mathf.Min(1f, 1280f / sw);
        W = Mathf.Max(2, (int)(sw * sc)) & ~1; H = Mathf.Max(2, (int)(sh * sc)) & ~1;
        sr = AudioSettings.outputSampleRate;
        channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
        Directory.CreateDirectory(dir);
        LastPath = Path.Combine(dir, "play_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");

        var v = new VideoTrackAttributes { frameRate = new MediaRational(FPS), width = (uint)W, height = (uint)H, includeAlpha = false, bitRateMode = UnityEditor.VideoBitrateMode.High };
        var a = new AudioTrackAttributes { sampleRate = new MediaRational(sr), channelCount = (ushort)channels, language = "ja" };
        enc = new MediaEncoder(LastPath, v, a);
        frame = new Texture2D(W, H, TextureFormat.RGBA32, false);
        rt = new RenderTexture(W, H, 0);
        // リニア色空間だと取り込んだ絵が白っぽくなるので、色を戻す表を作っておく
        fixGamma = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (int i = 0; i < 256; i++) { float x = i / 255f; lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((x <= 0.04045f ? x / 12.92f : Mathf.Pow((x + 0.055f) / 1.055f, 2.4f)) * 255f), 0, 255); }
        lock (lk) audio.Clear();
        Recording = true;
        StartCoroutine(Loop());
        Debug.Log("[IBProto] 録画開始（F9で停止）: " + LastPath);
    }

    public void Stop()
    {
        if (!Recording) return;
        Recording = false;
        enc.Dispose(); enc = null;
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (frame != null) Destroy(frame);
        Debug.Log("[IBProto] 録画を保存しました: " + LastPath);
    }

    // 実際の時間に合わせて1秒30枚。重くて間に合わないコマは同じ絵を足して長さをそろえる
    IEnumerator Loop()
    {
        var wait = new WaitForEndOfFrame();
        double t0 = Time.realtimeSinceStartupAsDouble;
        long n = 0;
        while (Recording)
        {
            yield return wait;
            if (!Recording) break;
            long target = (long)((Time.realtimeSinceStartupAsDouble - t0) * FPS);
            if (target <= n) continue;
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            Graphics.Blit(shot, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            frame.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            if (fixGamma)
            {
                var px = frame.GetRawTextureData<Color32>();
                for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color32(lut[c.r], lut[c.g], lut[c.b], 255); }
            }
            frame.Apply();
            RenderTexture.active = prev;
            Destroy(shot);
            while (n < target && Recording) { enc.AddFrame(frame); AddAudio(); n++; }
        }
    }

    void AddAudio()
    {
        int need = sr / FPS * channels;
        using (var na = new NativeArray<float>(need, Allocator.Temp))
        {
            lock (lk)
            {
                int m = Mathf.Min(need, audio.Count);
                var tmp = na;
                for (int i = 0; i < m; i++) tmp[i] = audio.Dequeue();
            }
            enc.AddSamples(na);
        }
    }

    // 鳴っている音をそのまま写す（別スレッドから呼ばれる）
    void OnAudioFilterRead(float[] data, int ch)
    {
        if (!Recording) return;
        lock (lk)
        {
            for (int i = 0; i < data.Length; i += ch)
                for (int c = 0; c < channels; c++) audio.Enqueue(data[i + Mathf.Min(c, ch - 1)]);
            // 溜まりすぎたら古い分を捨てる（2秒以上のずれを防ぐ）
            while (audio.Count > sr * channels * 2) audio.Dequeue();
        }
    }

    void OnDisable() { Stop(); }
#else
    public void Toggle() { }
    public void Stop() { }
#endif
}
