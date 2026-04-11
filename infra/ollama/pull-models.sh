#!/bin/sh
set -e

echo "Pulling Ollama models..."
ollama pull gemma3
ollama pull qwen2.5
ollama pull phi3
echo "All models pulled successfully."
