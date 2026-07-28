using UnityEditor;
using UnityEngine;

namespace OmniVoiceTTS.Editor
{
    [CustomEditor(typeof(OmniVoiceTTSModel))]
    public class OmniVoiceTTSModelEditor : UnityEditor.Editor
    {
        private SerializedProperty modelPath;
        private SerializedProperty codecPath;
        private SerializedProperty voiceMode;
        private SerializedProperty instruct;
        private SerializedProperty cloneWavPath;
        private SerializedProperty cloneTranscript;
        private SerializedProperty useFa;
        private SerializedProperty clampFp16;
        private SerializedProperty streamAudio;
        private SerializedProperty denoise;
        private SerializedProperty preprocessPrompt;
        private SerializedProperty postproc;
        private SerializedProperty TOverride;
        private SerializedProperty chunkDurationSec;
        private SerializedProperty chunkThresholdSec;
        private SerializedProperty mgNumStep;
        private SerializedProperty mgGuidanceScale;
        private SerializedProperty mgTShift;
        private SerializedProperty mgLayerPenaltyFactor;
        private SerializedProperty mgPositionTemperature;
        private SerializedProperty mgClassTemperature;
        private SerializedProperty mgSeed;

        private void OnEnable()
        {
            modelPath = serializedObject.FindProperty("modelPath");
            codecPath = serializedObject.FindProperty("codecPath");
            voiceMode = serializedObject.FindProperty("voiceMode");
            instruct = serializedObject.FindProperty("instruct");
            cloneWavPath = serializedObject.FindProperty("cloneWavPath");
            cloneTranscript = serializedObject.FindProperty("cloneTranscript");
            useFa = serializedObject.FindProperty("useFa");
            clampFp16 = serializedObject.FindProperty("clampFp16");
            streamAudio = serializedObject.FindProperty("streamAudio");
            denoise = serializedObject.FindProperty("denoise");
            preprocessPrompt = serializedObject.FindProperty("preprocessPrompt");
            postproc = serializedObject.FindProperty("postproc");
            TOverride = serializedObject.FindProperty("TOverride");
            chunkDurationSec = serializedObject.FindProperty("chunkDurationSec");
            chunkThresholdSec = serializedObject.FindProperty("chunkThresholdSec");
            mgNumStep = serializedObject.FindProperty("mgNumStep");
            mgGuidanceScale = serializedObject.FindProperty("mgGuidanceScale");
            mgTShift = serializedObject.FindProperty("mgTShift");
            mgLayerPenaltyFactor = serializedObject.FindProperty("mgLayerPenaltyFactor");
            mgPositionTemperature = serializedObject.FindProperty("mgPositionTemperature");
            mgClassTemperature = serializedObject.FindProperty("mgClassTemperature");
            mgSeed = serializedObject.FindProperty("mgSeed");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(modelPath);
            EditorGUILayout.PropertyField(codecPath);
            EditorGUILayout.PropertyField(voiceMode);

            var mode = (TtsGenerationMode) voiceMode.enumValueIndex;
            if (mode == TtsGenerationMode.Design)
            {
                EditorGUILayout.PropertyField(instruct);
            }
            else
            {
                EditorGUILayout.PropertyField(cloneWavPath);
                EditorGUILayout.PropertyField(cloneTranscript, new GUIContent("Clone Transcript", "Enter transcript text directly, or provide a path to a .txt file."));
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(useFa);
            EditorGUILayout.PropertyField(clampFp16);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(streamAudio);
            EditorGUILayout.PropertyField(denoise);
            EditorGUILayout.PropertyField(preprocessPrompt);
            EditorGUILayout.PropertyField(postproc);
            EditorGUILayout.PropertyField(TOverride);
            EditorGUILayout.PropertyField(chunkDurationSec);
            EditorGUILayout.PropertyField(chunkThresholdSec);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(mgNumStep);
            EditorGUILayout.PropertyField(mgGuidanceScale);
            EditorGUILayout.PropertyField(mgTShift);
            EditorGUILayout.PropertyField(mgLayerPenaltyFactor);
            EditorGUILayout.PropertyField(mgPositionTemperature);
            EditorGUILayout.PropertyField(mgClassTemperature);
            EditorGUILayout.PropertyField(mgSeed);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
