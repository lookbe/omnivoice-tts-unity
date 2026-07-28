using System.Collections.Generic;
using UnityEngine;

namespace OmniVoiceTTS
{
    [RequireComponent(typeof(AudioSource))]
    public class OmniVoiceTTS : MonoBehaviour
    {
        [Header("Model")]
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

        [Header("Language")]
        public string lang = "auto";

        private AudioSource audioSource;
        private OmniVoiceTTSModel model;
        private readonly Queue<float> audioQueue = new Queue<float>();
        private const int SampleRate = 24000;
        private const int Channels = 1;

        public delegate void StatusChangedDelegate(ModelStatus status);
        public event StatusChangedDelegate OnStatusChanged;

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

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            model = GetComponentInChildren<OmniVoiceTTSModel>();

            if (audioSource != null)
            {
                audioSource.clip = AudioClip.Create("OmniVoiceStreamingClip", SampleRate * 60, Channels, SampleRate, true, OnAudioRead);
                audioSource.loop = true;
                audioSource.Play();
            }
        }

        private void OnEnable()
        {
            if (model != null)
            {
                model.OnStatusChanged += OnModelStatusChanged;
                model.OnAudioChunkGenerated += OnAudioChunkGenerated;
            }
        }

        private void OnDisable()
        {
            if (model != null)
            {
                model.OnStatusChanged -= OnModelStatusChanged;
                model.OnAudioChunkGenerated -= OnAudioChunkGenerated;
            }
        }

        public void InitModel()
        {
            if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(codecPath) || model == null)
            {
                return;
            }

            if (status != ModelStatus.Init)
            {
                Debug.LogError("invalid status");
                return;
            }

            status = ModelStatus.Loading;
            StartCoroutine(RunInitModel());
        }

        private System.Collections.IEnumerator RunInitModel()
        {
            Debug.Log("Load OmniVoice model");

            model.modelPath = modelPath;
            model.codecPath = codecPath;
            model.ApplyVoiceSettings(voiceMode, instruct, cloneWavPath, cloneTranscript);
            model.InitModel();

            yield return new WaitWhile(() => model.status == ModelStatus.Loading);

            if (model.status != ModelStatus.Ready)
            {
                Debug.LogError("failed to load OmniVoice model");
                status = ModelStatus.Error;
                yield break;
            }

            Debug.Log("Load model done");
            status = ModelStatus.Ready;
        }

        public void Synthesize(string text, string language = null, string voiceInstruct = null)
        {
            if (string.IsNullOrEmpty(text) || model == null)
            {
                return;
            }

            if (status != ModelStatus.Ready)
            {
                Debug.LogError("invalid status");
                return;
            }

            status = ModelStatus.Generate;
            model.ApplyVoiceSettings(voiceMode, voiceInstruct ?? instruct, cloneWavPath, cloneTranscript);
            model.Synthesize(text, language ?? lang, voiceMode, voiceInstruct ?? instruct, cloneWavPath, cloneTranscript);
            StartCoroutine(WaitForGenerationAndPlaybackDone());
        }

        private System.Collections.IEnumerator WaitForGenerationAndPlaybackDone()
        {
            yield return new WaitUntil(() => model.status == ModelStatus.Ready);
            yield return new WaitUntil(IsAudioQueueEmpty);
            status = ModelStatus.Ready;
        }

        private bool IsAudioQueueEmpty()
        {
            lock (audioQueue)
            {
                return audioQueue.Count == 0;
            }
        }

        public void Stop()
        {
            if (status != ModelStatus.Generate)
            {
                Debug.Log("already stopped");
                return;
            }

            if (model != null)
            {
                model.Stop();
            }

            lock (audioQueue)
            {
                audioQueue.Clear();
            }
        }

        private void OnModelStatusChanged(ModelStatus status)
        {
            if (status == ModelStatus.Error)
            {
                StopAllCoroutines();
                lock (audioQueue)
                {
                    audioQueue.Clear();
                }
            }

            this.status = status;
        }

        private void OnAudioChunkGenerated(float[] audioChunk)
        {
            lock (audioQueue)
            {
                foreach (var s in audioChunk)
                {
                    audioQueue.Enqueue(s);
                }
            }
        }

        private void OnAudioRead(float[] data)
        {
            lock (audioQueue)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (audioQueue.Count > 0)
                    {
                        data[i] = audioQueue.Dequeue();
                    }
                    else
                    {
                        data[i] = 0f;
                    }
                }
            }
        }
    }
}
