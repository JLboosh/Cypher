#!/bin/bash
# Fetches the large files that are not stored in git: Whisper speech model, Piper venv + voice.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

WHISPER="$ROOT/CypherWorkshop/Assets/StreamingAssets/Whisper/ggml-base.en.bin"
if [ ! -f "$WHISPER" ]; then
  mkdir -p "$(dirname "$WHISPER")"
  curl -L -o "$WHISPER" https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin
fi

if [ ! -d "$ROOT/tools/piper-venv" ]; then
  python3 -m venv "$ROOT/tools/piper-venv"
  "$ROOT/tools/piper-venv/bin/pip" install piper-tts==1.8.0
fi

mkdir -p "$ROOT/tools/piper-voices"
if [ ! -f "$ROOT/tools/piper-voices/en_GB-alan-medium.onnx" ]; then
  "$ROOT/tools/piper-venv/bin/python" -m piper.download_voices --data-dir "$ROOT/tools/piper-voices" en_GB-alan-medium
fi
echo "Setup complete."
