using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using UnityEngine;

namespace OmniVoiceTTS
{
    public enum TtsGenerationMode
    {
        Design = 0,
        Clone = 1,
    }

    public class OmniVoiceTTSModel : BackgroundRunner
    {
        [Header("Model Paths")]
        public string modelPath = string.Empty;
        public string codecPath = string.Empty;

        [Header("Voice Mode")]
        public TtsGenerationMode voiceMode = TtsGenerationMode.Design;

        [Header("Voice Design")]
        [TextArea(3, 3)]
        public string instruct = string.Empty;

        [Header("Voice Clone")]
        public string cloneWavPath = string.Empty;
        [TextArea(3, 3)]
        [Tooltip("Enter transcript text directly, or provide a path to a .txt file.")]
        public string cloneTranscript = string.Empty;

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
        private float[] cloneReferenceSamples;
        private string resolvedCloneTranscript = string.Empty;
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

        public void ApplyVoiceSettings(TtsGenerationMode mode, string design, string wavPath, string transcript)
        {
            voiceMode = mode;
            instruct = design ?? string.Empty;
            cloneWavPath = wavPath ?? string.Empty;
            cloneTranscript = transcript ?? string.Empty;

            if (status == ModelStatus.Ready && voiceMode == TtsGenerationMode.Clone)
            {
                if (!PrepareCloneInputs(out var clonePrepError))
                {
                    Debug.LogError(clonePrepError);
                    FreeModel();
                    status = ModelStatus.Error;
                }
            }
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

                if (!PrepareCloneInputs(out var clonePrepError))
                {
                    Debug.LogError(clonePrepError);
                    FreeModel();
                    unityContext.Post(_ => status = ModelStatus.Error, null);
                    return;
                }

                unityContext.Post(_ => status = ModelStatus.Ready, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading model: {ex.Message}");
                FreeModel();
                unityContext.Post(_ => status = ModelStatus.Error, null);
            }
        }

        public void FreeModel()
        {
            if (_context != IntPtr.Zero)
            {
                Native.ov_free(_context);
                _context = IntPtr.Zero;
            }

            cloneReferenceSamples = null;
            resolvedCloneTranscript = string.Empty;
        }

        private string GetLastError()
        {
            IntPtr errorPtr = Native.ov_last_error();
            return errorPtr == IntPtr.Zero ? "Unknown error" : Marshal.PtrToStringAnsi(errorPtr);
        }

        public void Synthesize(string text, string lang = "auto", TtsGenerationMode mode = TtsGenerationMode.Design, string design = null, string wavPath = null, string transcript = null)
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
                Mode = mode,
                Instruct = design ?? string.Empty,
            };
            RunBackground(payload, RunSynthesize);
        }

        private static bool ResolveTranscriptInput(string transcriptInput, out string transcriptText, out string error)
        {
            transcriptText = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(transcriptInput))
            {
                error = "Clone mode requires a reference transcript text or a .txt file path";
                return false;
            }

            var trimmedInput = transcriptInput.Trim();
            if (File.Exists(trimmedInput))
            {
                try
                {
                    transcriptText = File.ReadAllText(trimmedInput);
                }
                catch (Exception ex)
                {
                    error = $"Failed to read reference transcript file '{trimmedInput}': {ex.Message}";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(transcriptText))
                {
                    error = $"Reference transcript file is empty: {trimmedInput}";
                    return false;
                }

                return true;
            }

            transcriptText = transcriptInput;
            if (string.IsNullOrWhiteSpace(transcriptText))
            {
                error = "Clone mode requires a reference transcript text or a .txt file path";
                return false;
            }

            return true;
        }

        private bool PrepareCloneInputs(out string error)
        {
            error = null;
            cloneReferenceSamples = null;
            resolvedCloneTranscript = string.Empty;

            if (voiceMode != TtsGenerationMode.Clone)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(cloneWavPath))
            {
                error = "Clone mode requires a reference WAV path";
                return false;
            }

            if (!File.Exists(cloneWavPath))
            {
                error = $"Reference WAV not found: {cloneWavPath}";
                return false;
            }

            if (!ResolveTranscriptInput(cloneTranscript, out resolvedCloneTranscript, out error))
            {
                return false;
            }

            if (!TryLoadWavAsMono24k(cloneWavPath, out cloneReferenceSamples, out error))
            {
                cloneReferenceSamples = null;
                resolvedCloneTranscript = string.Empty;
                return false;
            }

            return true;
        }

        private class SynthPayload : IBackgroundPayload
        {
            public string Text;
            public string Lang;
            public TtsGenerationMode Mode;
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
            ov_voice_ref voiceRef = default;
            bool hasVoiceRef = false;
            try
            {
                Native.ov_tts_default_params(out var ttsParams);
                ttsParams.text = payload.Text;
                ttsParams.lang = payload.Lang;

                if (payload.Mode == TtsGenerationMode.Clone)
                {
                    if (cloneReferenceSamples == null || cloneReferenceSamples.Length == 0)
                    {
                        Debug.LogError("Clone voice reference was not prepared during init");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(resolvedCloneTranscript))
                    {
                        Debug.LogError("Clone voice transcript was not prepared during init");
                        return;
                    }

                    GCHandle audioHandle = GCHandle.Alloc(cloneReferenceSamples, GCHandleType.Pinned);
                    try
                    {
                        ov_status extractStatus = Native.ov_extract_voice_ref(
                            _context,
                            audioHandle.AddrOfPinnedObject(),
                            cloneReferenceSamples.Length,
                            out voiceRef);

                        if (extractStatus != ov_status.OV_STATUS_OK)
                        {
                            Debug.LogError($"Voice reference extraction failed: {extractStatus} - {GetLastError()}");
                            return;
                        }

                        hasVoiceRef = true;
                    }
                    finally
                    {
                        audioHandle.Free();
                    }

                    ttsParams.instruct = null;
                    ttsParams.ref_audio_tokens = voiceRef.ref_codes;
                    ttsParams.ref_T = voiceRef.ref_T;
                    ttsParams.ref_audio_24k = IntPtr.Zero;
                    ttsParams.ref_n_samples = 0;
                    ttsParams.ref_text = resolvedCloneTranscript;
                }
                else
                {
                    ttsParams.instruct = payload.Instruct;
                    ttsParams.ref_audio_tokens = IntPtr.Zero;
                    ttsParams.ref_T = 0;
                    ttsParams.ref_audio_24k = IntPtr.Zero;
                    ttsParams.ref_n_samples = 0;
                    ttsParams.ref_text = null;
                }

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

                if (hasVoiceRef)
                {
                    Native.ov_voice_ref_free(ref voiceRef);
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

        private static bool TryLoadWavAsMono24k(string path, out float[] samples, out string error)
        {
            samples = null;
            error = null;

            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    if (ReadFourCC(br) != "RIFF")
                    {
                        error = $"Invalid WAV file (missing RIFF header): {path}";
                        return false;
                    }

                    br.ReadInt32();

                    if (ReadFourCC(br) != "WAVE")
                    {
                        error = $"Invalid WAV file (missing WAVE header): {path}";
                        return false;
                    }

                    ushort audioFormat = 0;
                    ushort channels = 0;
                    int sampleRate = 0;
                    ushort bitsPerSample = 0;
                    byte[] dataChunk = null;

                    while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                    {
                        string chunkId = ReadFourCC(br);
                        int chunkSize = br.ReadInt32();
                        long chunkEnd = br.BaseStream.Position + chunkSize + (chunkSize & 1);

                        if (chunkId == "fmt ")
                        {
                            audioFormat = br.ReadUInt16();
                            channels = br.ReadUInt16();
                            sampleRate = br.ReadInt32();
                            br.ReadInt32();
                            br.ReadUInt16();
                            bitsPerSample = br.ReadUInt16();
                        }
                        else if (chunkId == "data")
                        {
                            dataChunk = br.ReadBytes(chunkSize);
                        }

                        br.BaseStream.Position = chunkEnd;
                    }

                    if (dataChunk == null)
                    {
                        error = $"Invalid WAV file (missing data chunk): {path}";
                        return false;
                    }

                    if (channels == 0 || sampleRate <= 0 || bitsPerSample == 0)
                    {
                        error = $"Invalid WAV file (missing format metadata): {path}";
                        return false;
                    }

                    float[] mono = ConvertToMonoFloat(dataChunk, audioFormat, channels, bitsPerSample, out var convertError);
                    if (mono == null)
                    {
                        error = convertError;
                        return false;
                    }

                    samples = sampleRate == 24000 ? mono : ResampleLinear(mono, sampleRate, 24000);
                    if (samples == null || samples.Length == 0)
                    {
                        error = $"Failed to resample reference WAV: {path}";
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to load reference WAV '{path}': {ex.Message}";
                return false;
            }
        }

        private static float[] ConvertToMonoFloat(byte[] data, ushort audioFormat, ushort channels, ushort bitsPerSample, out string error)
        {
            error = null;

            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample <= 0 || channels == 0)
            {
                error = "Unsupported WAV format.";
                return null;
            }

            int frameSize = bytesPerSample * channels;
            if (frameSize <= 0)
            {
                error = "Unsupported WAV frame size.";
                return null;
            }

            int frameCount = data.Length / frameSize;
            if (frameCount <= 0)
            {
                error = "Reference WAV contains no samples.";
                return null;
            }

            float[] mono = new float[frameCount];
            int offset = 0;

            for (int i = 0; i < frameCount; i++)
            {
                double sum = 0.0;

                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleOffset = offset + (ch * bytesPerSample);
                    sum += ReadSample(data, sampleOffset, audioFormat, bitsPerSample, out error);
                    if (error != null)
                    {
                        return null;
                    }
                }

                mono[i] = (float)(sum / channels);
                offset += frameSize;
            }

            return mono;
        }

        private static float ReadSample(byte[] data, int offset, ushort audioFormat, ushort bitsPerSample, out string error)
        {
            error = null;

            if (audioFormat == 3 && bitsPerSample == 32)
            {
                return BitConverter.ToSingle(data, offset);
            }

            if (audioFormat != 1)
            {
                error = $"Unsupported WAV audio format: {audioFormat}";
                return 0f;
            }

            switch (bitsPerSample)
            {
                case 8:
                    return (data[offset] - 128) / 128f;
                case 16:
                    return BitConverter.ToInt16(data, offset) / 32768f;
                case 24:
                    {
                        int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                        if ((value & 0x800000) != 0)
                        {
                            value |= unchecked((int)0xFF000000);
                        }

                        return value / 8388608f;
                    }
                case 32:
                    return BitConverter.ToInt32(data, offset) / 2147483648f;
                default:
                    error = $"Unsupported WAV bit depth: {bitsPerSample}";
                    return 0f;
            }
        }

        private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
        {
            if (input == null || input.Length == 0 || sourceRate <= 0 || targetRate <= 0)
            {
                return input;
            }

            if (sourceRate == targetRate)
            {
                return input;
            }

            if (input.Length == 1)
            {
                return new[] { input[0] };
            }

            int outputLength = Math.Max(1, (int)Math.Round(input.Length * (double)targetRate / sourceRate));
            if (outputLength == 1)
            {
                return new[] { input[0] };
            }

            float[] output = new float[outputLength];
            double scale = (double)(input.Length - 1) / (outputLength - 1);

            for (int i = 0; i < outputLength; i++)
            {
                double srcPos = i * scale;
                int left = (int)srcPos;
                int right = Math.Min(left + 1, input.Length - 1);
                double frac = srcPos - left;
                output[i] = (float)(input[left] + ((input[right] - input[left]) * frac));
            }

            return output;
        }

        private static string ReadFourCC(BinaryReader br)
        {
            return Encoding.ASCII.GetString(br.ReadBytes(4));
        }
    }
}
