using System;
using System.Runtime.InteropServices;

namespace OmniVoiceTTS
{
    public enum ov_status
    {
        OV_STATUS_OK = 0,
        OV_STATUS_INVALID_PARAMS = -1,
        OV_STATUS_INSTRUCT_INVALID = -2,
        OV_STATUS_GENERATE_FAILED = -3,
        OV_STATUS_OOM = -4,
        OV_STATUS_CANCELLED = -5,
    }

    public enum ov_log_level
    {
        OV_LOG_DEBUG = 0,
        OV_LOG_INFO = 1,
        OV_LOG_WARN = 2,
        OV_LOG_ERROR = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ov_audio
    {
        public IntPtr samples;
        public int n_samples;
        public int sample_rate;
        public int channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ov_init_params
    {
        public int abi_version;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string model_path;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string codec_path;
        [MarshalAs(UnmanagedType.I1)] public bool use_fa;
        [MarshalAs(UnmanagedType.I1)] public bool clamp_fp16;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ov_voice_ref
    {
        public IntPtr ref_codes;
        public int ref_T;
        public int num_codebooks;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool ov_cancel_cb(IntPtr user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool ov_audio_chunk_cb(IntPtr samples, int n_samples, IntPtr user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ov_log_cb(ov_log_level level, [MarshalAs(UnmanagedType.LPUTF8Str)] string msg, IntPtr user_data);

    [StructLayout(LayoutKind.Sequential)]
    public struct ov_tts_params
    {
        public int abi_version;

        [MarshalAs(UnmanagedType.LPUTF8Str)] public string text;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string lang;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string instruct;

        public int T_override;
        public float chunk_duration_sec;
        public float chunk_threshold_sec;

        [MarshalAs(UnmanagedType.I1)] public bool denoise;
        [MarshalAs(UnmanagedType.I1)] public bool preprocess_prompt;

        public int mg_num_step;
        public float mg_guidance_scale;
        public float mg_t_shift;
        public float mg_layer_penalty_factor;
        public float mg_position_temperature;
        public float mg_class_temperature;
        public ulong mg_seed;

        public IntPtr ref_audio_tokens;
        public int ref_T;
        public IntPtr ref_audio_24k;
        public int ref_n_samples;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string ref_text;

        [MarshalAs(UnmanagedType.LPUTF8Str)] public string dump_dir;

        public ov_cancel_cb cancel;
        public IntPtr cancel_user_data;

        public ov_audio_chunk_cb on_chunk;
        public IntPtr on_chunk_user_data;

        [MarshalAs(UnmanagedType.I1)] public bool postproc;
    }

    public static class Native
    {
        private const string GgmlDll = "ggml";
        private const string LibName = "omnivoice";

        [DllImport(GgmlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ggml_backend_load_all();

        [DllImport(GgmlDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ggml_backend_load_all_from_path([MarshalAs(UnmanagedType.LPUTF8Str)] string dir_path);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ov_version();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ov_last_error();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ov_audio_free(ref ov_audio a);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ov_init_default_params(out ov_init_params p);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ov_init(in ov_init_params params_init);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ov_free(IntPtr ov);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ov_log_set(ov_log_cb cb, IntPtr user_data);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ov_tts_default_params(out ov_tts_params p);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ov_status ov_synthesize(IntPtr ov, in ov_tts_params params_tts, out ov_audio out_audio);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ov_duration_sec_to_tokens(IntPtr ov, float duration_sec);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ov_num_codebooks(IntPtr ov);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ov_status ov_extract_voice_ref(IntPtr ov, IntPtr ref_audio_24k, int ref_n_samples, out ov_voice_ref out_ref);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ov_voice_ref_free(ref ov_voice_ref ref_voice);
    }
}
