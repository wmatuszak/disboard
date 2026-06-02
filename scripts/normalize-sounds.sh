#!/usr/bin/env bash
set -euo pipefail

SOUNDS_DIR="${1:-/games/soundboard/sounds}"
BACKUP_DIR="${BACKUP_DIR:-/games/soundboard/sound-backups}"
LOUDNESS_TARGET="${LOUDNESS_TARGET:--16}"
LOUDNESS_RANGE_TARGET="${LOUDNESS_RANGE_TARGET:-11}"
TRUE_PEAK_LIMIT="${TRUE_PEAK_LIMIT:--1.5}"

if [[ ! -d "$SOUNDS_DIR" ]]; then
  echo "Sound directory not found: $SOUNDS_DIR" >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"
shopt -s nullglob

for file in "$SOUNDS_DIR"/*.mp3 "$SOUNDS_DIR"/*.wav; do
  [[ -f "$file" ]] || continue

  temp_file="${file%.*}.normalized.${file##*.}"
  timestamp="$(date -u +%Y%m%d%H%M%S)"
  backup_file="${BACKUP_DIR}/$(basename "${file%.*}").${timestamp}.${file##*.}"
  echo "Normalizing $(basename "$file")"

  cp "$file" "$backup_file"

  ffmpeg -y \
    -i "$file" \
    -vn \
    -af "loudnorm=I=${LOUDNESS_TARGET}:LRA=${LOUDNESS_RANGE_TARGET}:TP=${TRUE_PEAK_LIMIT}" \
    -ac 2 \
    -ar 48000 \
    "$temp_file"

  mv "$temp_file" "$file"
done

echo "Normalization complete for $SOUNDS_DIR"
