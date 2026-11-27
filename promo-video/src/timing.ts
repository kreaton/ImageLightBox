import beat from './beat.json';

export const FPS = 30;
const SECONDS_PER_BEAT = 60 / beat.bpm;

/** The frame of a beat of the song; every cut in the video is placed with this. */
export const b = (n: number) => Math.round((beat.firstBeat + n * SECONDS_PER_BEAT) * FPS);

/** Length of the landscape video, about 100 seconds: the credits end with a fade. */
export const TOTAL_FRAMES = b(130);

/** Length of the portrait Short: under a minute, so a claim on the music can't get it blocked. */
export const SHORT_FRAMES = b(74);

/** 1 right on a beat, decaying towards 0 before the next one. */
export const beatPulse = (frame: number) => {
	const position = (frame / FPS - beat.firstBeat) / SECONDS_PER_BEAT;
	if (position < 0) return 0;
	return Math.exp(-3.5 * (position % 1));
};
