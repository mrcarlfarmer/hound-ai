#!/bin/sh
set -e

MODELS="qwen3:14b qwen3.5:9b"
API_URL="${HOUND_API_URL:-http://hound-api:5000}/api/activity"

notify() {
  severity="$1"
  message="$2"
  curl -sf -X POST "$API_URL" \
    -H "Content-Type: application/json" \
    -d "{
      \"packId\": \"infrastructure\",
      \"houndId\": \"ollama-init\",
      \"houndName\": \"Ollama Model Manager\",
      \"message\": \"$message\",
      \"severity\": \"$severity\",
      \"timestamp\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"
    }" --max-time 5 2>/dev/null || true
}

installed=$(ollama list 2>/dev/null | tail -n +2 | awk '{print $1}')

notify "Info" "Model pull check started"

for model in $MODELS; do
  if echo "$installed" | grep -qx "$model"; then
    echo "Already available: $model"
    notify "Info" "Model $model already available — skipping download"
  else
    echo "Pulling $model..."
    notify "Info" "Downloading model $model..."
    ollama pull "$model"
    notify "Success" "Model $model pulled successfully"
  fi
done

notify "Success" "All models ready"
echo "All models ready."
