/**
 * Audio conversion utilities using ffmpeg
 */

import { spawn } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

/**
 * Convert audio file to WAV format suitable for Amazon Transcribe
 * 
 * Amazon Transcribe requirements:
 * - Sample rate: 8000-48000 Hz (16000 recommended for phone calls)
 * - Channels: 1 or 2
 * - Bit depth: 16-bit PCM
 */
export async function convertToWav(inputPath: string): Promise<string> {
  const outputPath = inputPath.replace(/\.[^/.]+$/, '.wav');
  
  // Skip if already WAV
  if (inputPath.toLowerCase().endsWith('.wav')) {
    return inputPath;
  }

  // Check if input file exists
  if (!fs.existsSync(inputPath)) {
    throw new Error(`Input file not found: ${inputPath}`);
  }

  console.log(`[Audio] Converting ${path.basename(inputPath)} to WAV...`);

  return new Promise((resolve, reject) => {
    const ffmpeg = spawn('ffmpeg', [
      '-i', inputPath,
      '-vn',                    // No video
      '-acodec', 'pcm_s16le',   // 16-bit PCM
      '-ar', '16000',           // 16kHz sample rate
      '-ac', '1',               // Mono
      '-y',                     // Overwrite output
      outputPath,
    ]);

    let stderr = '';

    ffmpeg.stderr.on('data', (data) => {
      stderr += data.toString();
    });

    ffmpeg.on('close', (code) => {
      if (code === 0) {
        console.log(`[Audio] Conversion complete: ${path.basename(outputPath)}`);
        resolve(outputPath);
      } else {
        console.error(`[Audio] ffmpeg stderr: ${stderr}`);
        reject(new Error(`ffmpeg exited with code ${code}`));
      }
    });

    ffmpeg.on('error', (err) => {
      reject(new Error(`Failed to start ffmpeg: ${err.message}`));
    });
  });
}

/**
 * Get audio file duration in seconds
 */
export async function getAudioDuration(filePath: string): Promise<number> {
  return new Promise((resolve, reject) => {
    const ffprobe = spawn('ffprobe', [
      '-v', 'error',
      '-show_entries', 'format=duration',
      '-of', 'default=noprint_wrappers=1:nokey=1',
      filePath,
    ]);

    let stdout = '';
    let stderr = '';

    ffprobe.stdout.on('data', (data) => {
      stdout += data.toString();
    });

    ffprobe.stderr.on('data', (data) => {
      stderr += data.toString();
    });

    ffprobe.on('close', (code) => {
      if (code === 0 && stdout.trim()) {
        resolve(parseFloat(stdout.trim()));
      } else {
        reject(new Error(`Failed to get duration: ${stderr}`));
      }
    });

    ffprobe.on('error', (err) => {
      reject(new Error(`Failed to start ffprobe: ${err.message}`));
    });
  });
}

/**
 * Extract audio from video file
 */
export async function extractAudio(videoPath: string): Promise<string> {
  const outputPath = videoPath.replace(/\.[^/.]+$/, '.webm');
  
  return new Promise((resolve, reject) => {
    const ffmpeg = spawn('ffmpeg', [
      '-i', videoPath,
      '-vn',                    // No video
      '-acodec', 'libopus',     // Opus codec
      '-b:a', '64k',            // 64kbps bitrate
      '-y',                     // Overwrite output
      outputPath,
    ]);

    ffmpeg.on('close', (code) => {
      if (code === 0) {
        resolve(outputPath);
      } else {
        reject(new Error(`ffmpeg exited with code ${code}`));
      }
    });

    ffmpeg.on('error', (err) => {
      reject(new Error(`Failed to start ffmpeg: ${err.message}`));
    });
  });
}

/**
 * Split audio file into chunks
 * Useful for very long recordings that exceed Transcribe limits
 */
export async function splitAudio(
  inputPath: string,
  chunkDurationSeconds: number = 3600
): Promise<string[]> {
  const duration = await getAudioDuration(inputPath);
  const numChunks = Math.ceil(duration / chunkDurationSeconds);
  
  if (numChunks <= 1) {
    return [inputPath];
  }

  const chunks: string[] = [];
  const baseName = inputPath.replace(/\.[^/.]+$/, '');
  const extension = path.extname(inputPath);

  for (let i = 0; i < numChunks; i++) {
    const startTime = i * chunkDurationSeconds;
    const chunkPath = `${baseName}_chunk${i}${extension}`;

    await new Promise<void>((resolve, reject) => {
      const ffmpeg = spawn('ffmpeg', [
        '-ss', startTime.toString(),
        '-i', inputPath,
        '-t', chunkDurationSeconds.toString(),
        '-c', 'copy',
        '-y',
        chunkPath,
      ]);

      ffmpeg.on('close', (code) => {
        if (code === 0) {
          chunks.push(chunkPath);
          resolve();
        } else {
          reject(new Error(`Failed to split audio chunk ${i}`));
        }
      });

      ffmpeg.on('error', reject);
    });
  }

  return chunks;
}

/**
 * Check if ffmpeg is available
 */
export async function checkFfmpegAvailable(): Promise<boolean> {
  return new Promise((resolve) => {
    const ffmpeg = spawn('ffmpeg', ['-version']);
    
    ffmpeg.on('close', (code) => {
      resolve(code === 0);
    });

    ffmpeg.on('error', () => {
      resolve(false);
    });
  });
}
