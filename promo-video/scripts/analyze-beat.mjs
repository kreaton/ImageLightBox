// Estimates the tempo and first downbeat of public/music.mp3 and prints the loudness over time,
// so the video's cuts can land on the music. Usage: node scripts/analyze-beat.mjs
import {execFileSync} from 'node:child_process';
import {readFileSync, rmSync, writeFileSync} from 'node:fs';

const RATE = 11025;
const wavPath = 'music.tmp.wav';
execFileSync('npx', ['remotion', 'ffmpeg', '-y', '-loglevel', 'error', '-i', 'public/music.mp3', '-ac', '1', '-ar', String(RATE), '-c:a', 'pcm_s16le', wavPath], {shell: true, stdio: 'inherit'});
const bytes = readFileSync(wavPath);
rmSync(wavPath);
const dataAt = bytes.indexOf('data', 12, 'latin1') + 8;
const samples = new Float32Array(Math.floor((bytes.length - dataAt) / 2));
for (let i = 0; i < samples.length; i++) samples[i] = bytes.readInt16LE(dataAt + i * 2) / 32768;

// Energy per hop, then its positive rises (onsets).
const HOP = 256;
const hopRate = RATE / HOP;
const energy = new Float32Array(Math.floor(samples.length / HOP));
for (let i = 0; i < energy.length; i++) {
  let e = 0;
  for (let j = 0; j < HOP; j++) e += samples[i * HOP + j] ** 2;
  energy[i] = Math.sqrt(e / HOP);
}
const onset = new Float32Array(energy.length);
for (let i = 1; i < energy.length; i++) onset[i] = Math.max(0, energy[i] - energy[i - 1]);

// Tempo: best autocorrelation between 70 and 180 BPM.
let bestBpm = 120;
let bestScore = -1;
for (let bpm = 70; bpm <= 180; bpm += 0.05) {
  const lag = (60 / bpm) * hopRate;
  let score = 0;
  for (let i = 0; i + Math.ceil(lag) < onset.length; i++) score += onset[i] * onset[Math.round(i + lag)];
  if (score > bestScore) {
    bestScore = score;
    bestBpm = bpm;
  }
}

// Phase: the offset within one beat that collects the most onset energy.
const beatHops = (60 / bestBpm) * hopRate;
let bestPhase = 0;
let bestPhaseScore = -1;
for (let phase = 0; phase < beatHops; phase++) {
  let score = 0;
  for (let t = phase; t < onset.length; t += beatHops) score += onset[Math.round(t)] ?? 0;
  if (score > bestPhaseScore) {
    bestPhaseScore = score;
    bestPhase = phase;
  }
}

const seconds = samples.length / RATE;
const result = {bpm: Number(bestBpm.toFixed(2)), firstBeat: Number((bestPhase / hopRate).toFixed(3)), seconds: Number(seconds.toFixed(2))};
writeFileSync('src/beat.json', JSON.stringify(result, null, 2) + '\n');
console.log(result);

// Loudness per 4 seconds, to see the song's structure.
for (let s = 0; s < seconds; s += 4) {
  const from = Math.floor((s * RATE) / HOP);
  const to = Math.min(energy.length, Math.floor(((s + 4) * RATE) / HOP));
  let sum = 0;
  for (let i = from; i < to; i++) sum += energy[i];
  const mean = sum / Math.max(1, to - from);
  console.log(String(s).padStart(4) + 's ' + '#'.repeat(Math.round(mean * 200)));
}
