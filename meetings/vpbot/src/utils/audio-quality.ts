import * as fs from 'fs';

/**
 * Recording dropout detector.
 *
 * Before 2026-11-11 the bot ran at 1 vCPU and the browser's audio output starved for CPU, so recordings carry
 * hundreds of 20-40 ms holes per minute (they sound choppy and hurt transcription and voiceprints). Clean
 * recordings sit around 0-5 per minute. This counts short low-energy dips that sit between loud speech
 * frames - the signature of those holes - so a bad recording is flagged in the bot log the moment it ends.
 *
 * Reads a 16-bit PCM WAV (the bot's ffmpeg output) in chunks; never throws.
 */
export interface AudioQuality {
  durationSec: number;
  dropouts: number;
  dropoutsPerMin: number;
  level: 'ok' | 'degraded' | 'bad' | 'unknown';
}

const FRAME_MS = 5;
const LOUD_RMS = 300;
const LOW_RMS = 30;
const MAX_DIP_MS = 120;
const CONTEXT_FRAMES = 6; // loud audio must be within 30 ms on both sides

export const DEGRADED_PER_MIN = 15;
export const BAD_PER_MIN = 60;

export async function analyzeRecordingQuality(wavPath: string, sampleRate = 16000): Promise<AudioQuality> {
  const unknown: AudioQuality = { durationSec: 0, dropouts: 0, dropoutsPerMin: 0, level: 'unknown' };
  let handle: fs.promises.FileHandle | null = null;
  try {
    const size = (await fs.promises.stat(wavPath)).size;
    if (size < 44 + sampleRate * 2 * 5 || size > 600 * 1024 * 1024) return unknown; // <5 s or absurdly large
    handle = await fs.promises.open(wavPath, 'r');

    const frameSamples = Math.floor((sampleRate * FRAME_MS) / 1000);
    const frameBytes = frameSamples * 2;
    const chunk = Buffer.alloc(frameBytes * 4000); // ~20 s per read
    const frames: number[] = [];
    let pos = 44; // standard 44-byte header from ffmpeg's WAV muxer
    for (;;) {
      const { bytesRead } = await handle.read(chunk, 0, chunk.length, pos);
      if (bytesRead < frameBytes) break;
      const usable = bytesRead - (bytesRead % frameBytes);
      for (let off = 0; off < usable; off += frameBytes) {
        let sq = 0;
        for (let i = 0; i < frameBytes; i += 2) {
          const v = chunk.readInt16LE(off + i);
          sq += v * v;
        }
        frames.push(Math.sqrt(sq / frameSamples));
      }
      pos += usable;
      if (bytesRead < chunk.length) break;
    }

    const m = frames.length;
    const durationSec = (m * FRAME_MS) / 1000;
    if (durationSec < 5) return unknown;

    const anyLoud = (a: number, b: number): boolean => {
      for (let f = Math.max(0, a); f < Math.min(m, b); f++) if (frames[f] > LOUD_RMS) return true;
      return false;
    };
    let dropouts = 0;
    for (let f = 1; f < m - 1; ) {
      if (frames[f] < LOW_RMS && anyLoud(f - CONTEXT_FRAMES, f)) {
        let j = f;
        while (j < m && frames[j] < LOW_RMS) j++;
        if ((j - f) * FRAME_MS <= MAX_DIP_MS && anyLoud(j, j + CONTEXT_FRAMES)) dropouts++;
        f = j;
      } else {
        f++;
      }
    }
    const dropoutsPerMin = dropouts / (durationSec / 60);
    const level = dropoutsPerMin >= BAD_PER_MIN ? 'bad' : dropoutsPerMin >= DEGRADED_PER_MIN ? 'degraded' : 'ok';
    return { durationSec, dropouts, dropoutsPerMin, level };
  } catch (err: any) {
    console.warn(`[Bot] Audio quality check failed (non-fatal): ${err?.message ?? err}`);
    return unknown;
  } finally {
    await handle?.close().catch(() => undefined);
  }
}

export function logAudioQuality(q: AudioQuality): void {
  if (q.level === 'unknown') {
    console.log('[Bot] Audio quality: not assessed (recording too short or unreadable)');
    return;
  }
  const line = `[Bot] Audio quality: ${q.dropouts} dropouts in ${q.durationSec.toFixed(0)}s (${q.dropoutsPerMin.toFixed(1)}/min) -> ${q.level.toUpperCase()}`;
  if (q.level === 'ok') console.log(line);
  else console.warn(`${line} - recording has audible holes (CPU-starved capture?); transcription and voiceprints from it will be degraded`);
}
