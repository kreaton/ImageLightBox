import {loadFont} from '@remotion/google-fonts/BricolageGrotesque';
import {useAudioData, visualizeAudio} from '@remotion/media-utils';
import {type CSSProperties, type ReactNode, useContext} from 'react';
import {AbsoluteFill, Img, Internals, interpolate, interpolateColors, random, spring, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';
import {b, beatPulse} from './timing';

export const {fontFamily} = loadFont('normal', {weights: ['400', '600', '800'], subsets: ['latin']});

/** The app's violet, and colours taken from the paintings in the demo library. */
export const ACCENT = {
	violet: '#8b5cf6',
	amber: '#e0a020',
	terracotta: '#e0703a',
	olive: '#86b04a',
	sea: '#2fa7c9',
	red: '#e5484d',
	rose: '#d65a9c',
};

// When the background colour changes, in beats.
const COLOR_TRACK: [number, string][] = [
	[0, ACCENT.violet],
	[8, ACCENT.amber],
	[12, ACCENT.violet],
	[20, ACCENT.terracotta],
	[28, ACCENT.sea],
	[36, ACCENT.red],
	[56, ACCENT.olive],
	[64, ACCENT.amber],
	[72, ACCENT.terracotta],
	[82, ACCENT.sea],
	[92, ACCENT.rose],
	[98, ACCENT.violet],
	[108, ACCENT.olive],
	[114, ACCENT.violet],
];

// The Short has the same story with fewer scenes.
const PHONE_COLOR_TRACK: [number, string][] = [
	[0, ACCENT.violet],
	[5, ACCENT.amber],
	[9, ACCENT.violet],
	[17, ACCENT.sea],
	[25, ACCENT.red],
	[43, ACCENT.olive],
	[51, ACCENT.terracotta],
	[59, ACCENT.sea],
	[66, ACCENT.violet],
];

/** True in the portrait composition. */
export const useIsPhone = () => {
	const {width, height} = useVideoConfig();
	return height > width;
};

export const useAccent = () => {
	// The colour track is in beats of the song, so it needs the frame of the whole video, not of the current scene.
	const sequence = useContext(Internals.SequenceContext);
	const frame = useCurrentFrame() + (sequence ? sequence.cumulatedFrom + sequence.relativeFrom : 0);
	const track = useIsPhone() ? PHONE_COLOR_TRACK : COLOR_TRACK;
	return interpolateColors(
		frame,
		track.flatMap(([beat], i) => (i === 0 ? [b(beat)] : [b(beat) - 8, b(beat)])),
		track.flatMap(([, color], i) => (i === 0 ? [color] : [track[i - 1][1], color])),
	);
};

/** The dark stage: two slow glows in the current accent colour that swell on the beat. */
export const Stage = () => {
	const frame = useCurrentFrame();
	const accent = useAccent();
	const pulse = beatPulse(frame);
	return (
		<AbsoluteFill style={{background: '#0b0a12', overflow: 'hidden'}}>
			<div
				style={{
					position: 'absolute',
					width: 1500,
					height: 1500,
					left: -520 + Math.sin(frame / 90) * 80,
					top: -760 + Math.cos(frame / 110) * 60,
					borderRadius: '50%',
					background: `radial-gradient(closest-side, ${accent}, transparent)`,
					opacity: 0.46 + pulse * 0.12,
				}}
			/>
			<div
				style={{
					position: 'absolute',
					width: 1300,
					height: 1300,
					right: -420 + Math.cos(frame / 80) * 70,
					bottom: -760 + Math.sin(frame / 100) * 50,
					borderRadius: '50%',
					background: `radial-gradient(closest-side, ${accent}, transparent)`,
					opacity: 0.24 + pulse * 0.08,
				}}
			/>
		</AbsoluteFill>
	);
};

/** Bars along the bottom, driven by the actual music. */
export const Visualizer = () => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const audioData = useAudioData(staticFile('music.mp3'));
	const accent = useAccent();
	if (!audioData) return null;

	const spectrum = visualizeAudio({fps, frame, audioData, numberOfSamples: 128});
	// Mirrored around the centre. Higher bins are much quieter, so they get more gain and the whole width moves.
	const half = spectrum.slice(0, 40).map((v, i) => Math.min(1, Math.pow(v, 0.8) * (1 + i * 0.3)));
	const bars = [...[...half].reverse(), ...half];
	return (
		<div style={{position: 'absolute', left: 0, right: 0, bottom: 0, height: 130, display: 'flex', alignItems: 'flex-end', gap: 6, padding: '0 6px', opacity: 0.6}}>
			{bars.map((v, i) => (
				<div key={i} style={{flex: 1, height: 5 + v * 120, background: accent, borderRadius: '4px 4px 0 0'}} />
			))}
		</div>
	);
};

/** Slowly rising photo frames: small blank prints in the accent colour, deterministic per seed. Behind everything else. */
export const Prints = ({count = 22, clearCaption = true}: {count?: number; clearCaption?: boolean}) => {
	const frame = useCurrentFrame();
	const accent = useAccent();
	const {width, height} = useVideoConfig();
	const phone = height > width;
	const [clearFrom, clearTo] = phone ? [90, 700] : [90, 900];
	return (
		<AbsoluteFill style={{overflow: 'hidden'}}>
			{Array.from({length: count}, (_, i) => {
				const speed = 0.7 + random(`s${i}`) * 1.3;
				const size = 26 + random(`z${i}`) * 46;
				const y = height + 80 - ((random(`y${i}`) * (height + 200) + frame * speed) % (height + 200));
				const x = random(`x${i}`) * width + Math.sin(frame / 50 + i) * 22;
				// Prints fade out over the column where the captions are.
				const overCaption =
					clearCaption && (phone || x < 640)
						? Math.min(
								interpolate(y, [clearFrom, clearFrom + 80], [1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}) +
									interpolate(y, [clearTo - 80, clearTo], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}),
								1,
							)
						: 1;
				if (overCaption === 0) return null;
				const white = i % 4 === 0;
				return (
					<div
						key={i}
						style={{
							position: 'absolute',
							left: x,
							top: y,
							width: size,
							height: size * 0.78,
							boxSizing: 'border-box',
							border: `${Math.max(3, size * 0.09)}px solid ${white ? '#ffffff' : accent}`,
							borderBottomWidth: Math.max(6, size * 0.22),
							borderRadius: 3,
							opacity: (white ? 0.32 : 0.5) * overCaption,
							transform: `rotate(${(random(`r${i}`) - 0.5) * 50 + Math.sin(frame / 40 + i) * 8}deg)`,
						}}
					/>
				);
			})}
		</AbsoluteFill>
	);
};

const SHOT_W = 1426;
const SHOT_H = 993;

export type Focus = {x: number; y: number; z: number};
export const WHOLE: Focus = {x: 0.5, y: 0.5, z: 1};

type ShotProps = {
	name: string;
	/** Camera at the start and end of this shot; x and y are fractions of the screenshot. */
	from?: Focus;
	to?: Focus;
	/** Frames the camera takes; defaults to the whole sequence. */
	moveFrames?: number;
	width?: number;
	style?: CSSProperties;
	enter?: boolean;
	/** Camera in the portrait composition, where the window is cropped instead of shrunk. */
	phone?: {from?: Focus; to?: Focus};
};

// Where the app window sits in the portrait composition: under the caption, above YouTube's own buttons.
const PHONE_FRAME = {left: 40, top: 700, width: 1000, height: 860};

/** A screenshot of the real app in a window frame, with a camera that can move inside it. */
export const Shot = ({name, from: landscapeFrom = WHOLE, to: landscapeTo = landscapeFrom, moveFrames, width: landscapeWidth = 1240, style, enter = true, phone}: ShotProps) => {
	const frame = useCurrentFrame();
	const {fps, durationInFrames} = useVideoConfig();
	const isPhone = useIsPhone();
	const from = (isPhone && phone?.from) || landscapeFrom;
	const to = (isPhone && (phone?.to ?? phone?.from)) || landscapeTo;
	// The window's frame, and the screenshot's size inside it before any zoom.
	const frameW = isPhone ? PHONE_FRAME.width : landscapeWidth;
	const frameH = isPhone ? PHONE_FRAME.height : (landscapeWidth * SHOT_H) / SHOT_W;
	const width = (frameH * SHOT_W) / SHOT_H;
	const height = frameH;

	const t = spring({frame, fps, durationInFrames: moveFrames ?? 26, config: {damping: 200}});
	const linear = interpolate(frame, [0, moveFrames ?? durationInFrames], [0, 1], {extrapolateRight: 'clamp'});
	const k = moveFrames === undefined ? linear : t;
	const z = from.z + (to.z - from.z) * k;
	const fx = from.x + (to.x - from.x) * k;
	const fy = from.y + (to.y - from.y) * k;

	// Keep the picture covering the frame near its edges.
	const tx = Math.min(0, Math.max(frameW - width * z, frameW / 2 - fx * width * z));
	const ty = Math.min(0, Math.max(frameH - height * z, frameH / 2 - fy * height * z));

	const pop = enter ? spring({frame, fps, config: {damping: 16, stiffness: 170}}) : 1;
	return (
		<div
			style={{
				position: 'absolute',
				left: isPhone ? PHONE_FRAME.left : 1920 - frameW - 50,
				top: isPhone ? PHONE_FRAME.top : (1080 - frameH) / 2,
				width: frameW,
				height: frameH,
				borderRadius: 14,
				overflow: 'hidden',
				boxShadow: '0 40px 110px rgba(0,0,0,0.65), 0 0 0 1px rgba(255,255,255,0.12)',
				transform: `translateY(${(1 - pop) * 60}px) scale(${0.96 + pop * 0.04})`,
				opacity: Math.min(1, pop * 1.4),
				background: '#0b0a12',
				...style,
			}}
		>
			<Img
				src={staticFile(`app/${name}.jpg`)}
				style={{
					position: 'absolute',
					width,
					height,
					transformOrigin: '0 0',
					transform: `translate(${tx}px, ${ty}px) scale(${z})`,
				}}
			/>
		</div>
	);
};

/** Shots of one scene shown one after the other; `at` is each one's start in frames of the scene. */
export const Flip = ({shots}: {shots: {at: number; node: ReactNode}[]}) => {
	const frame = useCurrentFrame();
	let current = shots[0];
	for (const s of shots) if (frame >= s.at) current = s;
	return <>{current.node}</>;
};

const Word = ({children, delay}: {children: string; delay: number}) => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const s = spring({frame: frame - delay, fps, config: {damping: 15, stiffness: 190}});
	return (
		<span style={{display: 'inline-block', marginRight: '0.26em', opacity: Math.min(1, s * 1.5), transform: `translateY(${(1 - s) * 34}px)`}}>
			{children}
		</span>
	);
};

export const Words = ({text, delay = 0, step = 2}: {text: string; delay?: number; step?: number}) => (
	<>
		{text.split(' ').map((w, i) => (
			<Word key={i} delay={delay + i * step}>
				{w}
			</Word>
		))}
	</>
);

/** The left column next to a Shot: a chapter label, a headline and one supporting line. */
export const Caption = ({kicker, title, sub, subDelay = 12, children}: {kicker?: string; title: string; sub?: string; subDelay?: number; children?: ReactNode}) => {
	const accent = useAccent();
	const phone = useIsPhone();
	const place: CSSProperties = phone
		? {left: 60, top: 150, height: 520, width: 900, justifyContent: 'flex-end'}
		: {left: 56, top: 0, bottom: 90, width: 540, justifyContent: 'center'};
	return (
		<div style={{position: 'absolute', ...place, display: 'flex', flexDirection: 'column', fontFamily}}>
			{kicker ? (
				<div style={{fontSize: phone ? 32 : 28, fontWeight: 800, letterSpacing: 5, color: '#fff', marginBottom: 18, textTransform: 'uppercase', textShadow: `0 0 26px ${accent}`}}>
					<Words text={kicker} />
				</div>
			) : null}
			<div style={{fontSize: phone ? 84 : 74, fontWeight: 800, lineHeight: 1.04, color: '#fff', letterSpacing: -1.5}}>
				<Words text={title} delay={3} />
			</div>
			{sub ? (
				<div style={{fontSize: phone ? 40 : 34, fontWeight: 400, lineHeight: 1.3, color: '#d8d8e4', marginTop: phone ? 18 : 24}}>
					<Words text={sub} delay={subDelay} step={1} />
				</div>
			) : null}
			{children}
		</div>
	);
};

export const Key = ({children, size = 44, pressed = false}: {children: ReactNode; size?: number; pressed?: boolean}) => (
	<span
		style={{
			display: 'inline-flex',
			alignItems: 'center',
			justifyContent: 'center',
			minWidth: size * 1.7,
			height: size * 1.7,
			padding: `0 ${size * 0.45}px`,
			boxSizing: 'border-box',
			borderRadius: size * 0.3,
			background: 'linear-gradient(#f7f7fb, #d5d5e0)',
			boxShadow: pressed ? `0 ${size * 0.04}px 0 #8e8ea0, 0 ${size * 0.1}px ${size * 0.3}px rgba(0,0,0,0.5)` : `0 ${size * 0.14}px 0 #8e8ea0, 0 ${size * 0.3}px ${size * 0.5}px rgba(0,0,0,0.5)`,
			transform: pressed ? `translateY(${size * 0.1}px)` : undefined,
			color: '#16161f',
			fontFamily,
			fontWeight: 800,
			fontSize: size,
			lineHeight: 1,
		}}
	>
		{children}
	</span>
);

/** A key that goes down on the given frames of the scene. */
export const PressKey = ({children, at, size}: {children: ReactNode; at: number[]; size?: number}) => {
	const frame = useCurrentFrame();
	const pressed = at.some((f) => frame >= f && frame < f + 5);
	return (
		<Key size={size} pressed={pressed}>
			{children}
		</Key>
	);
};

/** A full-screen capture filling the frame, drifting slowly, for the full-screen view of the app. */
export const FullBleed = ({name, zoom = [1, 1.06]}: {name: string; zoom?: [number, number]}) => {
	const frame = useCurrentFrame();
	const {durationInFrames} = useVideoConfig();
	const z = interpolate(frame, [0, durationInFrames], zoom);
	return (
		<AbsoluteFill style={{background: '#000', overflow: 'hidden'}}>
			<Img src={staticFile(`app/${name}.jpg`)} style={{width: '100%', height: '100%', objectFit: 'cover', transform: `scale(${z})`}} />
		</AbsoluteFill>
	);
};
