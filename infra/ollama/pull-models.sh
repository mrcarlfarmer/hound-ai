#!/bin/sh
set -e

MODELS="qwen3:14b qwen3.5:9b"

installed=$(ollama list 2>/dev/null | tail -n +2 | awk '{print $1}')

for model in $MODELS; do
  if echo "$installed" | grep -qx "$model"; then
    echo "Already available: $model"
  else
    echo "Pulling $model..."
    ollama pull "$model"
  fi
done

echo "All models ready."
