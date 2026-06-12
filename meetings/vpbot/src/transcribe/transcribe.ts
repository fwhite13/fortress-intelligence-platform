/**
 * Whisper transcription service using faster-whisper (Python subprocess)
 * Replaces AWS Transcribe for firm-vpbot
 */

import { spawn } from 'child_process';
import * as fs from 'fs';
import { Transcript, TranscriptSegment } from '../types.js';

export interface WhisperConfig {
  region: string;
  s3Bucket: string;
  modelSize?: string;   // default: medium (pre-baked in Docker image as of ADO#1489; large-v3 requires image rebuild with BAKE_MODEL=large-v3)
  language?: string;    // default: en
  device?: string;      // default: cpu
  computeType?: string; // default: int8
  initialPrompt?: string; // optional initial prompt for Whisper (ADO#1809)
}

export interface WhisperSegment {
  speakerLabel: string;
  speakerName: string;
  text: string;
  startTimeMs: number;
  endTimeMs: number;
}

// Python inline script for faster-whisper transcription + optional pyannote diarization
const WHISPER_SCRIPT = `
import sys
import json
import os
from faster_whisper import WhisperModel

audio_path = sys.argv[1]
model_size = sys.argv[2] if len(sys.argv) > 2 else "medium"
language = sys.argv[3] if len(sys.argv) > 3 else "en"
initial_prompt = sys.argv[4] if len(sys.argv) > 4 else None
hf_token = os.environ.get("HF_TOKEN", "")

model = WhisperModel(model_size, device="cpu", compute_type="int8")
segments_raw, info = model.transcribe(
    audio_path,
    language=language,
    beam_size=5,
    word_timestamps=True,
    vad_filter=True,
    initial_prompt=initial_prompt if initial_prompt else None,
    verbose=False  # suppress progress output to stdout
)
whisper_segments = [
    {"text": seg.text.strip(), "start": seg.start, "end": seg.end}
    for seg in segments_raw
]

# Diarization
diarization_map = {}  # seg_start -> speaker_label
if hf_token:
    try:
        from pyannote.audio import Pipeline
        print("[Diarization] Loading pyannote pipeline...", file=sys.stderr)
        pipeline = Pipeline.from_pretrained(
            "pyannote/speaker-diarization-3.1",
            token=hf_token
        )
        print("[Diarization] Pipeline loaded. Running diarization...", file=sys.stderr)
        diarization = pipeline(audio_path)
        # Build list of (start, end, speaker) turns
        turns = [(turn.start, turn.end, speaker) for turn, _, speaker in diarization.itertracks(yield_label=True)]
        # Assign speaker to each whisper segment by midpoint
        for seg in whisper_segments:
            mid = (seg["start"] + seg["end"]) / 2
            speaker = "SPEAKER_00"
            for (t_start, t_end, t_speaker) in turns:
                if t_start <= mid <= t_end:
                    speaker = t_speaker
                    break
            diarization_map[seg["start"]] = speaker
        print(f"[Diarization] Complete. {len(turns)} turns, {len(set(s for _,_,s in turns))} speakers.", file=sys.stderr)
    except Exception as e:
        print(f"[Diarization] Failed: {e}", file=sys.stderr)
else:
    print("[Diarization] HF_TOKEN not set — skipping diarization.", file=sys.stderr)

result = []
for seg in whisper_segments:
    speaker = diarization_map.get(seg["start"], "SPEAKER_00")
    result.append({
        "speakerLabel": speaker,
        "speakerName": speaker,
        "text": seg["text"],
        "startTimeMs": int(seg["start"] * 1000),
        "endTimeMs": int(seg["end"] * 1000)
    })

print(json.dumps(result))
`;

export class WhisperService {
  private modelSize: string;
  private language: string;
  private initialPrompt: string;

  constructor(config: WhisperConfig) {
    this.modelSize = process.env.WHISPER_MODEL || config.modelSize || 'medium';
    this.language = config.language || 'en';
    this.initialPrompt = config.initialPrompt || '';
  }

  /**
   * Transcribe an audio file using faster-whisper (Python subprocess)
   */
  async transcribe(audioPath: string, meetingId: string, initialPrompt?: string): Promise<Transcript> {
    console.log(`[Whisper] Starting transcription: ${audioPath} (model=${this.modelSize})`);

    // Write the Python script to a temp file
    const scriptPath = `/tmp/whisper-${meetingId}.py`;
    fs.writeFileSync(scriptPath, WHISPER_SCRIPT.trim());

    return new Promise<Transcript>((resolve, reject) => {
      const proc = spawn('python3', [scriptPath, audioPath, this.modelSize, this.language, initialPrompt ?? ''], {
        stdio: ['pipe', 'pipe', 'pipe'],
      });

      let stdout = '';
      let stderr = '';

      proc.stdout.on('data', (data: Buffer) => {
        stdout += data.toString();
      });

      proc.stderr.on('data', (data: Buffer) => {
        const line = data.toString().trim();
        if (line) {
          console.log(`[Whisper] ${line}`);
          stderr += line + '\n';
        }
      });

      proc.on('exit', (code) => {
        // Cleanup temp script
        try { fs.unlinkSync(scriptPath); } catch { /* ignore */ }

        if (code !== 0) {
          reject(new Error(`Whisper process exited with code ${code}. stderr: ${stderr}`));
          return;
        }

        try {
          // Find the last '[' in stdout — the JSON array is always the final print statement
          // Any preceding stdout lines are whisper/tqdm progress output
          const jsonStart = stdout.lastIndexOf('[');
          if (jsonStart === -1) {
            throw new Error('No JSON array found in output');
          }
          const jsonStr = stdout.substring(jsonStart);
          const segments: WhisperSegment[] = JSON.parse(jsonStr);
          const transcript = this.convertToTranscript(segments);
          console.log(`[Whisper] Transcription complete: ${segments.length} segments`);
          resolve(transcript);
        } catch (err) {
          reject(new Error(`Failed to parse Whisper output: ${err}. stdout: ${stdout.substring(0, 500)}`));
        }
      });

      proc.on('error', (err) => {
        reject(new Error(`Failed to spawn python3: ${err.message}`));
      });
    });
  }

  /**
   * Convert Whisper segments to our Transcript type
   */
  private convertToTranscript(segments: WhisperSegment[]): Transcript {
    const transcriptSegments: TranscriptSegment[] = segments.map(seg => ({
      speakerLabel: seg.speakerLabel,
      startTime: seg.startTimeMs / 1000,
      endTime: seg.endTimeMs / 1000,
      content: seg.text,
      confidence: 0.95,
    }));

    const speakers = [...new Set(segments.map(s => s.speakerLabel))];
    const duration = segments.length > 0
      ? segments[segments.length - 1].endTimeMs / 1000
      : 0;

    const fullText = transcriptSegments
      .map(s => `${s.speakerLabel}: ${s.content}`)
      .join('\n\n');

    return {
      segments: transcriptSegments,
      fullText,
      speakers,
      duration,
    };
  }
}

// Re-export as TranscribeService alias for backward compat with index.ts
export { WhisperService as TranscribeService };
