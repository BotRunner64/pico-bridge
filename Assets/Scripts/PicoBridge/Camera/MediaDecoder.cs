using UnityEngine;

namespace PicoBridge.Camera
{
    /// <summary>
    /// JNI wrapper for the native Android MediaDecoder (robotassistant_lib AAR).
    /// Receives H.264 Annex-B over TCP, decodes to a Unity Texture2D.
    /// </summary>
    public static class MediaDecoder
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _javaObj;

        private static AndroidJavaObject GetJavaObject()
        {
            if (_javaObj == null)
                _javaObj = new AndroidJavaObject("com.picovr.robotassistantlib.MediaDecoder");
            return _javaObj;
        }

        public static void Initialize(int nativeTexturePtr, int width, int height)
        {
            Debug.Log($"[MediaDecoder] init texture={nativeTexturePtr} {width}x{height}");
            GetJavaObject().Call("initialize", nativeTexturePtr, width, height);
        }

        public static void StartServer(int port, bool record = false)
        {
            Debug.Log($"[MediaDecoder] startServer port={port} record={record}");
            GetJavaObject().Call("startServer", port, record);
        }

        public static bool IsUpdateFrame()
        {
            return GetJavaObject().Call<bool>("isUpdateFrame");
        }

        public static void UpdateTexture()
        {
            GetJavaObject().Call("updateTexture");
        }

        public static void Release()
        {
            Debug.Log("[MediaDecoder] release");
            GetJavaObject().Call("release");
        }
#else
        // Editor stubs — no-op so the project compiles outside Android
        public static void Initialize(int nativeTexturePtr, int width, int height)
        {
            Debug.Log($"[MediaDecoder] (stub) init {width}x{height}");
        }

        public static void StartServer(int port, bool record = false)
        {
            Debug.Log($"[MediaDecoder] (stub) startServer port={port}");
        }

        public static bool IsUpdateFrame() => false;

        public static void UpdateTexture() { }

        public static void Release()
        {
            Debug.Log("[MediaDecoder] (stub) release");
        }
#endif
    }
}
