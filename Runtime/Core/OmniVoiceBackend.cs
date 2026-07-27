using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace OmniVoiceTTS
{
    public static class NativeLibraryPath
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

        public static string GetDllPath(string dllName)
        {
            IntPtr hModule = GetModuleHandle(dllName);
            if (hModule == IntPtr.Zero)
            {
                return null;
            }

            var sb = new StringBuilder(1024);
            GetModuleFileName(hModule, sb, sb.Capacity);
            return System.IO.Path.GetDirectoryName(sb.ToString());
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        public static string GetAndroidNativeLibraryPath()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var info = activity.Call<AndroidJavaObject>("getApplicationInfo");
                return info.Get<string>("nativeLibraryDir");
            }
        }
#endif
    }

    public static class Backend
    {
        private static int count = 0;

        public static void Init()
        {
            if (count == 0)
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                string packagePath = NativeLibraryPath.GetDllPath("omnivoice.dll");
                if (!string.IsNullOrEmpty(packagePath))
                {
                    Native.ggml_backend_load_all_from_path(packagePath);
                }
                else
                {
                    Native.ggml_backend_load_all();
                }
#elif UNITY_ANDROID && !UNITY_EDITOR
                string packagePath = NativeLibraryPath.GetAndroidNativeLibraryPath();
                Native.ggml_backend_load_all_from_path(packagePath);
#else
                Native.ggml_backend_load_all();
#endif
            }

            count++;
        }

        public static void Free()
        {
            count--;
            if (count < 0)
            {
                count = 0;
            }
        }
    }
}
