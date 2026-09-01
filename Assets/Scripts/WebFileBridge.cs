using System.IO;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// Thin wrapper over Assets/Plugins/WebGL/FileBridge.jslib. On WebGL it hands the
// browser a real download / file picker; everywhere else it degrades to a
// persistentDataPath write / no-op, so callers don't each need their own #if
// (they still guard the upload path, which has no sensible desktop fallback).
public static class WebFileBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BMeshDownloadFile(string filename, string text, string mime);
    [DllImport("__Internal")] private static extern void BMeshDownloadBytes(string filename, byte[] data, int length, string mime);
    [DllImport("__Internal")] private static extern void BMeshUploadFile(string gameObjectName, string callbackName, string accept);
    [DllImport("__Internal")] private static extern void BMeshPreventCanvasScroll();
#endif

    // Hand `text` to the user as a downloaded file.
    public static void Download(string filename, string text, string mime = "application/octet-stream")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        BMeshDownloadFile(filename, text, mime);
#else
        string path = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllText(path, text);
        Debug.Log($"WebFileBridge.Download (non-WebGL fallback): wrote {path}");
#endif
    }

    // Hand raw bytes (e.g. a .glb) to the user as a downloaded file.
    public static void DownloadBytes(string filename, byte[] data, string mime = "application/octet-stream")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        BMeshDownloadBytes(filename, data, data.Length, mime);
#else
        string path = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllBytes(path, data);
        Debug.Log($"WebFileBridge.DownloadBytes (non-WebGL fallback): wrote {path}");
#endif
    }

    // Opens the browser file picker. The picked file's text content is delivered
    // via SendMessage(gameObjectName, callbackName, <text>) -- empty if cancelled.
    public static void RequestUpload(string gameObjectName, string callbackName, string accept = "")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        BMeshUploadFile(gameObjectName, callbackName, accept);
#else
        Debug.LogWarning("WebFileBridge.RequestUpload is only available in a WebGL build.");
#endif
    }

    // Prevents the browser page from scrolling when the wheel is used over the
    // Unity canvas. Safe to call more than once.
    public static void PreventCanvasScroll()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        BMeshPreventCanvasScroll();
#endif
    }
}
