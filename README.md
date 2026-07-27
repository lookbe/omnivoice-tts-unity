# OmniVoice TTS Unity

OmniVoice TTS for Unity.

## Package Layout

- `Runtime/OmniVoiceTTS.cs` is the scene-facing component that owns playback.
- `Runtime/OmniVoice/OmniVoiceTTSModel.cs` handles native initialization and synthesis.
- `Runtime/Native/OmniVoiceNative.cs` contains the P/Invoke bindings.
- `Runtime/Core/*` contains the shared background runner, status enum, and backend loader.
- `Plugins/Windows/x86-64/` contains the native DLL payload used by the Unity package.

## Basic Usage

1. Add the `OmniVoiceTTS` component to a GameObject with an `AudioSource`.
2. Point `modelPath` at the OmniVoice GGUF model and `codecPath` at the codec GGUF.
3. Fill in `instruct` with the desired voice design text.
4. Call `InitModel()` and then `Synthesize(...)`.

## Model Download

Download GGUF model files from:

- [https://huggingface.co/Serveurperso/OmniVoice-GGUF](https://huggingface.co/Serveurperso/OmniVoice-GGUF)

## Installation

Add this package to your Unity project via Package Manager.

## Credits

This Unity plugin is built on top of the C++ inference engine from:

- [ServeurpersoCom/omnivoice.cpp](https://github.com/ServeurpersoCom/omnivoice.cpp)
