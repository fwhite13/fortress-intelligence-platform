#!/usr/bin/env bash
# WI #9138 — regenerates the dev-only Plaud mock fixture audio from the *.script dialogues.
# Each script line is "S|text" (Samantha voice, speaker "Sarah Mitchell") or "D|text" (Daniel voice,
# speaker "David Chen"). Output per script: <name>.m4a (16 kHz mono AAC) and <name>.transcript.tsv
# ("#duration_ms<TAB>N" header, then start_ms<TAB>end_ms<TAB>speaker<TAB>text per line), both embedded
# in firm-web and served by PlaudMockController. macOS only: needs say, ffmpeg and ffprobe.
# Usage: ./generate.sh   (run from anywhere; writes next to this script)
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
for tool in say ffmpeg ffprobe; do
  command -v "$tool" >/dev/null || { echo "missing $tool" >&2; exit 1; }
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
gap=0.35

ffmpeg -loglevel error -y -f lavfi -i anullsrc=r=16000:cl=mono -t "$gap" -c:a pcm_s16le "$work/gap.wav"

dur_ms() {
  ffprobe -v error -show_entries format=duration -of default=nw=1:nk=1 "$1" | awk '{ printf "%d", $1 * 1000 }'
}

for script in "$here"/*.script; do
  name="$(basename "$script" .script)"
  list="$work/$name.list"
  tsv="$work/$name.tsv"
  : > "$list"
  : > "$tsv"
  pos=0
  gap_ms="$(dur_ms "$work/gap.wav")"
  i=0
  while IFS='|' read -r who text || [ -n "$who" ]; do
    [ -z "$who" ] && continue
    case "$who" in
      S) voice=Samantha; speaker="Sarah Mitchell" ;;
      D) voice=Daniel; speaker="David Chen" ;;
      *) echo "bad speaker '$who' in $script" >&2; exit 1 ;;
    esac
    i=$((i + 1))
    say -v "$voice" -o "$work/$name-$i.aiff" "$text"
    ffmpeg -loglevel error -y -i "$work/$name-$i.aiff" -ar 16000 -ac 1 -c:a pcm_s16le "$work/$name-$i.wav"
    len="$(dur_ms "$work/$name-$i.wav")"
    printf '%d\t%d\t%s\t%s\n' "$pos" "$((pos + len))" "$speaker" "$text" >> "$tsv"
    printf "file '%s'\nfile '%s'\n" "$work/$name-$i.wav" "$work/gap.wav" >> "$list"
    pos=$((pos + len + gap_ms))
  done < "$script"

  ffmpeg -loglevel error -y -f concat -safe 0 -i "$list" -c:a aac -b:a 32k -ar 16000 -ac 1 \
    -movflags +faststart "$here/$name.m4a"
  total="$(dur_ms "$here/$name.m4a")"
  { printf '#duration_ms\t%d\n' "$total"; cat "$tsv"; } > "$here/$name.transcript.tsv"
  echo "$name: ${total} ms, $(wc -c < "$here/$name.m4a" | tr -d ' ') bytes"
done
