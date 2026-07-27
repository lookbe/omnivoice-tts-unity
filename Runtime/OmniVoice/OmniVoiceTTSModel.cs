using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace OmniVoiceTTS
{
    public class OmniVoiceTTSModel : BackgroundRunner
    {
        [Header("Model Paths")]
        public string modelPath = string.Empty;
        public string codecPath = string.Empty;

        [Header("Backend")]
        public bool useFa = true;
        public bool clampFp16 = false;

        [Header("Generation")]
        public bool streamAudio = true;
        public bool denoise = true;
        public bool preprocessPrompt = true;
        public bool postproc = true;
        public int TOverride = 0;
        public float chunkDurationSec = 15.0f;
        public float chunkThresholdSec = 30.0f;

        [Header("MaskGIT Sampler")]
        public int mgNumStep = 32;
        public float mgGuidanceScale = 2.0f;
        public float mgTShift = 0.1f;
        public float mgLayerPenaltyFactor = 5.0f;
        public float mgPositionTemperature = 5.0f;
        public float mgClassTemperature = 0.0f;
        public ulong mgSeed = 42;

        public delegate void StatusChangedDelegate(ModelStatus status);
        public event StatusChangedDelegate OnStatusChanged;

        public delegate void AudioChunkDelegate(float[] audioChunk);
        public event AudioChunkDelegate OnAudioChunkGenerated;

        public delegate void LogMessageDelegate(ov_log_level level, string message);
        public event LogMessageDelegate OnLogMessage;

        private ModelStatus _status = ModelStatus.Init;
        public ModelStatus status
        {
            get => _status;
            protected set
            {
                if (_status != value)
                {
                    _status = value;
                    OnStatusChanged?.Invoke(_status);
                }
            }
        }

        protected IntPtr _context = IntPtr.Zero;
        private static OmniVoiceTTSModel s_logTarget;
        private static readonly ov_log_cb s_nativeLogCallback = OnNativeLog;

        protected new void Awake()
        {
            base.Awake();
            SetupNativeLogging();
        }

        private void OnDestroy()
        {
            TeardownNativeLogging();
            BackgroundStopSync();
            FreeModel();
            Backend.Free();
        }

        public void InitModel()
        {
            if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(codecPath))
            {
                Debug.LogError("Model or codec path not set");
                return;
            }

            if (_status != ModelStatus.Init)
            {
                Debug.LogError("Invalid status for InitModel");
                return;
            }

            status = ModelStatus.Loading;
            Backend.Init();
            RunBackground(RunInitModel);
        }

        private void SetupNativeLogging()
        {
            s_logTarget = this;
            Native.ov_log_set(s_nativeLogCallback, IntPtr.Zero);
        }

        private void TeardownNativeLogging()
        {
            if (s_logTarget == this)
            {
                Native.ov_log_set(null, IntPtr.Zero);
                s_logTarget = null;
            }
        }

        [AOT.MonoPInvokeCallback(typeof(ov_log_cb))]
        private static void OnNativeLog(ov_log_level level, string message, IntPtr userData)
        {
            var target = s_logTarget;
            if (target == null)
            {
                return;
            }

            target.unityContext.Post(_ =>
            {
                target.OnLogMessage?.Invoke(level, message);

                switch (level)
                {
                    case ov_log_level.OV_LOG_ERROR:
                        Debug.LogError(message);
                        break;
                    case ov_log_level.OV_LOG_WARN:
                        Debug.LogWarning(message);
                        break;
                    default:
                        Debug.Log(message);
                        break;
                }
            }, null);
        }

        private void RunInitModel(CancellationToken token)
        {
            try
            {
                Native.ov_init_default_params(out var initParams);
                initParams.model_path = modelPath;
                initParams.codec_path = codecPath;
                initParams.use_fa = useFa;
                initParams.clamp_fp16 = clampFp16;

                _context = Native.ov_init(in initParams);
                if (_context == IntPtr.Zero)
                {
                    throw new Exception("Failed to initialize OmniVoice context: " + GetLastError());
                }

                unityContext.Post(_ => status = ModelStatus.Ready, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading model: {ex.Message}");
                FreeModel();
                unityContext.Post(_ => status = ModelStatus.Init, null);
            }
        }

        public void FreeModel()
        {
            if (_context != IntPtr.Zero)
            {
                Native.ov_free(_context);
                _context = IntPtr.Zero;
            }
        }

        private string GetLastError()
        {
            IntPtr errorPtr = Native.ov_last_error();
            return errorPtr == IntPtr.Zero ? "Unknown error" : Marshal.PtrToStringAnsi(errorPtr);
        }

        public void Synthesize(string text, string lang = "auto", string instruct = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_context == IntPtr.Zero)
            {
                Debug.LogError("Model not initialized");
                return;
            }

            if (status != ModelStatus.Ready)
            {
                Debug.LogError("Invalid status for Synthesize");
                return;
            }

            status = ModelStatus.Generate;
            var payload = new SynthPayload
            {
                Text = text,
                Lang = lang,
                Instruct = instruct,
            };
            RunBackground(payload, RunSynthesize);
        }

        private class SynthPayload : IBackgroundPayload
        {
            public string Text;
            public string Lang;
            public string Instruct;
        }

        [AOT.MonoPInvokeCallback(typeof(ov_audio_chunk_cb))]
        private static bool OnNativeAudioChunk(IntPtr samples, int n_samples, IntPtr userData)
        {
            var gch = GCHandle.FromIntPtr(userData);
            var self = (OmniVoiceTTSModel) gch.Target;

            float[] chunk = new float[n_samples];
            Marshal.Copy(samples, chunk, 0, n_samples);

            self.unityContext.Post(_ => self.OnAudioChunkGenerated?.Invoke(chunk), null);
            return true;
        }

        [AOT.MonoPInvokeCallback(typeof(ov_cancel_cb))]
        private static bool OnNativeCancel(IntPtr userData)
        {
            var gch = GCHandle.FromIntPtr(userData);
            var self = (OmniVoiceTTSModel) gch.Target;
            return self.cts != null && self.cts.IsCancellationRequested;
        }

        private void RunSynthesize(SynthPayload payload, CancellationToken token)
        {
            GCHandle gch = GCHandle.Alloc(this);
            ov_audio audio = default;
            try
            {
                Native.ov_tts_default_params(out var ttsParams);
                ttsParams.text = payload.Text;
                ttsParams.lang = payload.Lang;
                ttsParams.instruct = payload.Instruct;

                ttsParams.T_override = TOverride;
                ttsParams.chunk_duration_sec = chunkDurationSec;
                ttsParams.chunk_threshold_sec = chunkThresholdSec;
                ttsParams.denoise = denoise;
                ttsParams.preprocess_prompt = preprocessPrompt;
                ttsParams.mg_num_step = mgNumStep;
                ttsParams.mg_guidance_scale = mgGuidanceScale;
                ttsParams.mg_t_shift = mgTShift;
                ttsParams.mg_layer_penalty_factor = mgLayerPenaltyFactor;
                ttsParams.mg_position_temperature = mgPositionTemperature;
                ttsParams.mg_class_temperature = mgClassTemperature;
                ttsParams.mg_seed = mgSeed;
                ttsParams.postproc = postproc;

                if (streamAudio)
                {
                    ttsParams.on_chunk = OnNativeAudioChunk;
                    ttsParams.on_chunk_user_data = GCHandle.ToIntPtr(gch);
                }
                else
                {
                    ttsParams.on_chunk = null;
                    ttsParams.on_chunk_user_data = IntPtr.Zero;
                }

                ttsParams.cancel = OnNativeCancel;
                ttsParams.cancel_user_data = GCHandle.ToIntPtr(gch);

                ov_status result = Native.ov_synthesize(_context, in ttsParams, out audio);
                if (result != ov_status.OV_STATUS_OK)
                {
                    Debug.LogError($"Synthesis failed: {result} - {GetLastError()}");
                    if (!streamAudio)
                    {
                        Native.ov_audio_free(ref audio);
                    }
                    return;
                }

                if (!streamAudio && audio.samples != IntPtr.Zero && audio.n_samples > 0)
                {
                    float[] chunk = new float[audio.n_samples];
                    Marshal.Copy(audio.samples, chunk, 0, audio.n_samples);
                    unityContext.Post(_ => OnAudioChunkGenerated?.Invoke(chunk), null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in synthesis: {ex.Message}");
            }
            finally
            {
                if (!streamAudio)
                {
                    Native.ov_audio_free(ref audio);
                }

                gch.Free();
                unityContext.Post(_ => status = ModelStatus.Ready, null);
            }
        }

        public void Stop()
        {
            if (cts != null)
            {
                cts.Cancel();
            }
        }
    }
}
