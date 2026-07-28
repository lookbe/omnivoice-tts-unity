using UnityEditor;
using UnityEngine;

namespace OmniVoiceTTS.Editor
{
    [CustomEditor(typeof(OmniVoiceTTS))]
    public class OmniVoiceTTSEditor : UnityEditor.Editor
    {
        private SerializedProperty modelPath;
        private SerializedProperty codecPath;
        private SerializedProperty voiceMode;
        private SerializedProperty instruct;
        private SerializedProperty cloneWavPath;
        private SerializedProperty cloneTranscript;
        private SerializedProperty lang;

        private void OnEnable()
        {
            modelPath = serializedObject.FindProperty("modelPath");
            codecPath = serializedObject.FindProperty("codecPath");
            voiceMode = serializedObject.FindProperty("voiceMode");
            instruct = serializedObject.FindProperty("instruct");
            cloneWavPath = serializedObject.FindProperty("cloneWavPath");
            cloneTranscript = serializedObject.FindProperty("cloneTranscript");
            lang = serializedObject.FindProperty("lang");
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

            EditorGUILayout.PropertyField(lang);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
