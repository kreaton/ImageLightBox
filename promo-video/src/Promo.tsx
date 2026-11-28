import type {ReactNode} from 'react';
import {AbsoluteFill, Html5Audio, Img, interpolate, Sequence, spring, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';
import {ACCENT, Caption, Flip, FullBleed, fontFamily, Key, PressKey, Prints, Shot, Stage, useAccent, useIsPhone, Visualizer, WHOLE, Words} from './parts';
import {b, beatPulse, SHORT_FRAMES, TOTAL_FRAMES} from './timing';

/** A sequence placed in beats of the song. */
const Beats = ({from, to, children}: {from: number; to: number; children: ReactNode}) => {
	const start = from === 0 ? 0 : b(from);
	return (
		<Sequence from={start} durationInFrames={b(to) - start}>
			{children}
		</Sequence>
	);
};

/** Frames from the start of a scene to its beat `n`. */
const useBeat = () => (n: number) => b(n) - b(0);

// ---------------------------------------------------------------- opening

const Logo = () => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const icon = spring({frame, fps, config: {damping: 11, stiffness: 120}});
	const pulse = beatPulse(frame);
	return (
		<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', fontFamily, paddingBottom: 60}}>
			<Img src={staticFile('icon.png')} style={{width: 280, height: 280, transform: `scale(${icon * (1 + pulse * 0.04)})`, filter: 'drop-shadow(0 30px 60px rgba(0,0,0,0.55))'}} />
			<div style={{fontSize: 150, fontWeight: 800, color: '#fff', letterSpacing: -3, marginTop: 26}}>
				<Words text="Image Lightbox" delay={14} step={6} />
			</div>
		</AbsoluteFill>
	);
};

const Hook = () => (
	<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', fontFamily, paddingBottom: 90, textAlign: 'center'}}>
		<div style={{fontSize: 140, fontWeight: 800, lineHeight: 1.04, color: '#fff', letterSpacing: -3, width: 1500}}>
			<Words text="Thousands of photos." step={4} />
			<br />
			<span style={{color: '#d8d8e4'}}>
				<Words text="One afternoon." delay={22} step={5} />
			</span>
		</div>
	</AbsoluteFill>
);

const THREE: [string, string][] = [
	['Look.', ACCENT.amber],
	['Decide.', ACCENT.terracotta],
	['Next.', '#c4b0ff'],
];

const ThreeWords = () => {
	const beat = useBeat();
	const phone = useIsPhone();
	return (
		<AbsoluteFill style={{justifyContent: 'center', paddingLeft: phone ? 80 : 190, paddingBottom: phone ? 240 : 90, fontFamily}}>
			{THREE.map(([text, color], i) => (
				<div key={text} style={{fontSize: phone ? 170 : 160, fontWeight: 800, lineHeight: 1.1, letterSpacing: -3, color}}>
					<Words text={text} delay={beat(i)} step={1} />
				</div>
			))}
		</AbsoluteFill>
	);
};

// ---------------------------------------------------------------- getting started

const OpenFolder = () => {
	const beat = useBeat();
	return (
		<>
			<Caption kicker="Start here" title="Point it at a folder" sub="Every file under it, subfolders too, sorted like Explorer. Or drop the folder on the window." subDelay={16} />
			<Flip
				shots={[
					{at: 0, node: <Shot name="start-empty" from={{x: 0.3, y: 0.05, z: 1.6}} phone={{from: {x: 0.3, y: 0.05, z: 2}}} />},
					{at: beat(1.5), node: <Shot name="start-typed" from={{x: 0.3, y: 0.05, z: 1.6}} enter={false} phone={{from: {x: 0.3, y: 0.05, z: 2}}} />},
					{at: beat(3), node: <Shot name="scanned" from={{x: 0.3, y: 0.05, z: 1.6}} to={WHOLE} moveFrames={30} enter={false} phone={{from: {x: 0.3, y: 0.05, z: 2}, to: {x: 0.6, y: 0.45, z: 1.15}}} />},
				]}
			/>
		</>
	);
};

const SIZES: [string, string][] = [
	['grid-small', 'Small'],
	['grid-medium', 'Medium'],
	['grid-large', 'Large'],
	['standard-select', 'Standard'],
];

const Sizes = () => {
	const beat = useBeat();
	return (
		<>
			<Caption title="Forty at a glance, or four up close" sub="Pick the thumbnail size. Standard always fits four, however big the window." subDelay={14} />
			<Flip
				shots={SIZES.map(([name], i) => ({
					at: beat(i * 2),
					node: <Shot name={name} from={{x: 0.5, y: 0.5, z: 1}} to={{x: 0.5, y: 0.5, z: 1.03}} enter={i === 0} phone={{from: {x: 0.62, y: 0.45, z: 1.2}}} />,
				}))}
			/>
		</>
	);
};

const Exif = () => (
	<>
		<Caption title="Everything about it, on the left" sub="Click a photo for its size, camera, exposure, date and GPS. For a movie, its length." subDelay={14} />
		<Shot name="exif-2201" from={WHOLE} to={{x: 0.18, y: 0.4, z: 1.75}} moveFrames={40} phone={{from: {x: 0.3, y: 0.4, z: 1.3}, to: {x: 0.15, y: 0.4, z: 2}}} />
	</>
);

// ---------------------------------------------------------------- problems and clean-up

const TAGS: [string, string, string][] = [
	['D', '#e5484d', 'Duplicate'],
	['E', '#f5a524', 'Wrong extension'],
	['T', '#3b82f6', 'Date differs'],
];

/** A round tag like the ones the app puts at the top right of a thumbnail. */
const Tag = ({letter, color, label, delay}: {letter: string; color: string; label: string; delay: number}) => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const phone = useIsPhone();
	const s = spring({frame: frame - delay, fps, config: {damping: 12, stiffness: 160}});
	const size = phone ? 54 : 58;
	return (
		<div style={{display: 'flex', alignItems: 'center', gap: phone ? 12 : 18, opacity: Math.min(1, s * 1.4), transform: `scale(${0.6 + s * 0.4})`, transformOrigin: 'left center'}}>
			<div style={{width: size, height: size, borderRadius: size, background: color, color: '#fff', fontFamily, fontWeight: 800, fontSize: size * 0.5, display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: `0 0 30px ${color}`, border: '3px solid #fff'}}>
				{letter}
			</div>
			<div style={{fontFamily, fontSize: phone ? 32 : 36, fontWeight: 600, color: '#fff', whiteSpace: 'nowrap'}}>{label}</div>
		</div>
	);
};

const Problems = () => {
	const {durationInFrames} = useVideoConfig();
	const phone = useIsPhone();
	const third = Math.round(durationInFrames / 3);
	return (
		<>
			<Caption kicker="New" title="It spots what's wrong" sub="Every file is checked while you look. A round tag marks each problem, and the bottom bar counts them." subDelay={14}>
				<div style={{marginTop: phone ? 30 : 44, display: 'flex', flexDirection: phone ? 'row' : 'column', gap: phone ? 26 : 20}}>
					{TAGS.map(([letter, color, label], i) => (
						<Tag key={letter} letter={letter} color={color} label={label} delay={i * third + 4} />
					))}
				</div>
			</Caption>
			<Flip
				shots={[
					{at: 0, node: <Shot name="tag-dup" from={WHOLE} to={{x: 0.8, y: 0.2, z: 1.55}} moveFrames={34} phone={{from: {x: 0.8, y: 0.2, z: 1.5}, to: {x: 0.8, y: 0.2, z: 2}}} />},
					{at: third, node: <Shot name="tag-ext" from={{x: 0.38, y: 0.8, z: 1.6}} to={{x: 0.38, y: 0.8, z: 1.75}} enter={false} phone={{from: {x: 0.36, y: 0.8, z: 2}}} />},
					{at: 2 * third, node: <Shot name="tag-date" from={{x: 0.62, y: 0.22, z: 1.4}} to={{x: 0.66, y: 0.35, z: 1.25}} moveFrames={Math.round(third * 0.9)} enter={false} phone={{from: {x: 0.62, y: 0.25, z: 1.8}, to: {x: 0.72, y: 0.3, z: 1.6}}} />},
				]}
			/>
		</>
	);
};

const KeepThis = () => {
	const beat = useBeat();
	return (
		<>
			<Caption kicker="Clean-up time" title="Same photo twice? Keep one." sub="Twins are found by their content, whatever they're called. K keeps this one and sends the rest to the Recycle Bin." subDelay={18}>
				<div style={{marginTop: 44, display: 'flex', alignItems: 'center', gap: 14, height: 90}}>
					<PressKey at={[beat(3)]}>K</PressKey>
				</div>
			</Caption>
			<Flip
				shots={[
					{at: 0, node: <Shot name="dup-selected" from={WHOLE} to={{x: 0.72, y: 0.22, z: 1.4}} moveFrames={30} phone={{from: {x: 0.72, y: 0.22, z: 1.5}, to: {x: 0.75, y: 0.2, z: 1.9}}} />},
					{at: beat(3), node: <Shot name="dup-confirm" from={{x: 0.62, y: 0.45, z: 1.35}} to={{x: 0.63, y: 0.5, z: 1.6}} moveFrames={20} enter={false} phone={{from: {x: 0.63, y: 0.5, z: 2}}} />},
					{at: beat(6), node: <Shot name="dup-gone" from={{x: 0.72, y: 0.22, z: 1.4}} to={{x: 0.55, y: 0.45, z: 1.1}} moveFrames={30} enter={false} phone={{from: {x: 0.75, y: 0.2, z: 1.9}, to: {x: 0.66, y: 0.3, z: 1.4}}} />},
				]}
			/>
		</>
	);
};

const RAPID = ['blur-selected', 'blur-gone', 'dark-selected', 'dark-gone', 'copy-selected', 'clean-done'];

const Rapid = () => {
	const beat = useBeat();
	const presses = [1, 3, 5].map((i) => beat(1.25 * i + 1.25));
	return (
		<>
			<Caption title="Blurred. Too dark. Seen it." sub="Turn off the question, and it's one key per photo: Del deletes, K clears out a twin." subDelay={14}>
				<div style={{marginTop: 44, display: 'flex', alignItems: 'center', gap: 22, height: 90}}>
					<PressKey at={presses.slice(0, 2)}>Del</PressKey>
					<PressKey at={presses.slice(2)}>K</PressKey>
				</div>
			</Caption>
			<Flip
				shots={RAPID.map((name, i) => ({
					at: i === 0 ? 0 : beat(1.25 * i + 1.25),
					node: <Shot name={name} from={{x: 0.6, y: 0.45, z: 1.08}} enter={i === 0} phone={{from: {x: 0.62, y: 0.5, z: 1.25}}} />,
				}))}
			/>
		</>
	);
};

const Fixes = () => {
	const beat = useBeat();
	return (
		<>
			<Caption title="Wrong name? Wrong date? Fixed." sub="X gives a PNG called .jpg its real extension. T sets the file date to the date taken. Or fix them all from the bottom bar." subDelay={14}>
				<div style={{marginTop: 44, display: 'flex', alignItems: 'center', gap: 22, height: 90}}>
					<PressKey at={[beat(2)]}>X</PressKey>
					<PressKey at={[]}>T</PressKey>
				</div>
			</Caption>
			<Flip
				shots={[
					{at: 0, node: <Shot name="ext-menu" from={{x: 0.7, y: 0.65, z: 1.15}} to={{x: 0.76, y: 0.78, z: 1.5}} moveFrames={30} phone={{from: {x: 0.76, y: 0.8, z: 1.75}}} />},
					{at: beat(2), node: <Shot name="ext-done" from={{x: 0.66, y: 0.6, z: 1.6}} to={{x: 0.6, y: 0.55, z: 1.35}} moveFrames={24} enter={false} phone={{from: {x: 0.66, y: 0.6, z: 1.9}}} />},
					{at: beat(4), node: <Shot name="dates-confirm" from={{x: 0.25, y: 0.9, z: 1.6}} to={{x: 0.62, y: 0.5, z: 1.45}} moveFrames={26} enter={false} phone={{from: {x: 0.62, y: 0.5, z: 1.9}}} />},
					{at: beat(6), node: <Shot name="dates-done" from={{x: 0.6, y: 0.35, z: 1.4}} to={{x: 0.55, y: 0.4, z: 1.15}} moveFrames={30} enter={false} phone={{from: {x: 0.62, y: 0.35, z: 1.6}}} />},
				]}
			/>
		</>
	);
};

const Move = () => {
	const beat = useBeat();
	return (
		<>
			<Caption kicker="Keepers" title="The good ones go to their own folder" sub="M moves, C copies. Nothing is ever overwritten: a clash becomes name (1).jpg." subDelay={16}>
				<div style={{marginTop: 44, display: 'flex', alignItems: 'center', gap: 22, height: 90}}>
					<PressKey at={[beat(2)]}>M</PressKey>
					<PressKey at={[]}>C</PressKey>
				</div>
			</Caption>
			<Flip
				shots={[
					{at: 0, node: <Shot name="move-selected" from={WHOLE} to={{x: 0.8, y: 0.4, z: 1.3}} moveFrames={30} phone={{from: {x: 0.8, y: 0.4, z: 1.5}}} />},
					{at: beat(2), node: <Shot name="move-confirm" from={{x: 0.56, y: 0.52, z: 1.6}} enter={false} phone={{from: {x: 0.56, y: 0.52, z: 1.9}}} />},
					{at: beat(5), node: <Shot name="move-done" from={{x: 0.12, y: 0.98, z: 1.9}} to={{x: 0.5, y: 0.5, z: 1}} moveFrames={40} enter={false} phone={{from: {x: 0.12, y: 0.98, z: 2.2}, to: {x: 0.62, y: 0.45, z: 1.25}}} />},
				]}
			/>
		</>
	);
};

const Rename = () => {
	const beat = useBeat();
	return (
		<>
			<Caption title="Give them a name" sub="F2 renames. Right-click a photo for every action, each with its own key." subDelay={14}>
				<div style={{marginTop: 44, display: 'flex', alignItems: 'center', gap: 14, height: 90}}>
					<PressKey at={[beat(3)]}>F2</PressKey>
				</div>
			</Caption>
			<Flip
				shots={[
					{at: 0, node: <Shot name="context-menu" from={WHOLE} to={{x: 0.38, y: 0.56, z: 1.45}} moveFrames={30} phone={{from: {x: 0.38, y: 0.56, z: 1.7}}} />},
					{at: beat(3), node: <Shot name="rename-typed" from={{x: 0.49, y: 0.49, z: 1.7}} enter={false} phone={{from: {x: 0.49, y: 0.49, z: 2}}} />},
					{at: beat(6), node: <Shot name="rename-done" from={{x: 0.88, y: 0.4, z: 1.7}} to={{x: 0.7, y: 0.42, z: 1.3}} moveFrames={30} enter={false} phone={{from: {x: 0.88, y: 0.4, z: 1.9}}} />},
				]}
			/>
		</>
	);
};

// ---------------------------------------------------------------- looking closer

const FullScreen = () => {
	const beat = useBeat();
	const phone = useIsPhone();
	const accent = useAccent();
	return (
		<>
			<Flip
				shots={[
					{at: 0, node: <FullBleed name="fullscreen-1" />},
					{at: beat(3.5), node: <FullBleed name="fullscreen-2" />},
					{at: beat(7), node: <FullBleed name="fullscreen-3" />},
				]}
			/>
			<AbsoluteFill style={{background: phone ? 'linear-gradient(rgba(0,0,0,0.85), rgba(0,0,0,0) 45%)' : 'linear-gradient(90deg, rgba(0,0,0,0.82), rgba(0,0,0,0) 55%)'}} />
			<div style={{position: 'absolute', left: phone ? 60 : 90, top: phone ? 170 : 0, bottom: phone ? undefined : 120, width: phone ? 900 : 760, display: 'flex', flexDirection: 'column', justifyContent: 'center', fontFamily}}>
				<div style={{fontSize: phone ? 32 : 28, fontWeight: 800, letterSpacing: 5, color: '#fff', marginBottom: 18, textTransform: 'uppercase', textShadow: `0 0 26px ${accent}`}}>
					<Words text="Look closer" />
				</div>
				<div style={{fontSize: phone ? 88 : 84, fontWeight: 800, lineHeight: 1.04, color: '#fff', letterSpacing: -1.5}}>
					<Words text="Enter for full screen" delay={3} />
				</div>
				<div style={{fontSize: phone ? 40 : 36, lineHeight: 1.3, color: '#e4e4ee', marginTop: 24}}>
					<Words text="The arrow keys step through. Delete, M, C and L still work." delay={14} step={1} />
				</div>
				<div style={{marginTop: 40, display: 'flex', gap: 14}}>
					<PressKey at={[0]}>Enter</PressKey>
					<PressKey at={[beat(3.5), beat(7)]}>→</PressKey>
				</div>
			</div>
		</>
	);
};

const MapScene = () => {
	const beat = useBeat();
	return (
		<>
			<Caption title="Where was this taken?" sub="L shows the spot on OpenStreetMap. The map follows you to the next photo." subDelay={14}>
				<div style={{marginTop: 44, display: 'flex', alignItems: 'center', gap: 14, height: 90}}>
					<PressKey at={[0]}>L</PressKey>
				</div>
			</Caption>
			<Flip
				shots={[
					{at: 0, node: <Shot name="map-1" from={{x: 0.5, y: 0.5, z: 1.05}} to={{x: 0.5, y: 0.5, z: 1.3}} moveFrames={beat(5)} phone={{from: {x: 0.5, y: 0.5, z: 1.55}}} />},
					{at: beat(5), node: <Shot name="map-2" from={{x: 0.5, y: 0.5, z: 1.3}} to={{x: 0.5, y: 0.5, z: 1.4}} enter={false} phone={{from: {x: 0.5, y: 0.5, z: 1.55}}} />},
				]}
			/>
		</>
	);
};

const Movies = () => (
	<>
		<Caption title="Movies in the pile, too" sub="JPEG, HEIC, PNG, GIF, TIFF and WebP show as pictures. Anything else gets an icon, and O opens it in its own program." subDelay={14} />
		<Shot name="video-selected" from={WHOLE} to={{x: 0.42, y: 0.45, z: 1.3}} moveFrames={40} phone={{from: {x: 0.67, y: 0.6, z: 1.5}}} />
	</>
);

const Shortcut = ({keys, label, delay}: {keys: ReactNode[]; label: string; delay: number}) => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const s = spring({frame: frame - delay, fps, config: {damping: 15, stiffness: 150}});
	return (
		<div style={{display: 'flex', alignItems: 'center', gap: 30, opacity: Math.min(1, s * 1.4), transform: `translateY(${(1 - s) * 30}px)`, height: 84}}>
			<div style={{display: 'flex', gap: 12, width: 250, justifyContent: 'flex-end'}}>
				{keys.map((k, i) => (
					<Key key={i} size={34}>
						{k}
					</Key>
				))}
			</div>
			<div style={{fontFamily, color: '#fff', fontSize: 40, fontWeight: 600}}>{label}</div>
		</div>
	);
};

const Keyboard = () => {
	const beat = useBeat();
	const rows: [ReactNode[], string][] = [
		[['PgUp', 'PgDn'], 'Previous, next page'],
		[['←', '→'], 'Move the selection'],
		[['Del'], 'Delete'],
		[['M', 'C'], 'Move or copy'],
		[['F2'], 'Rename'],
		[['Enter'], 'Full screen, or play'],
		[['L'], 'Show on the map'],
		[['K'], 'Keep this, delete its twins'],
		[['X'], 'Fix the extension'],
		[['T'], 'Date to date taken'],
		[['O'], 'Open in its program'],
		[['E'], 'Show in Explorer'],
	];
	return (
		<>
			<Shot name="scanned" from={{x: 0.6, y: 0.5, z: 1.1}} style={{opacity: 0.16, filter: 'blur(4px)', left: 340}} enter={false} />
			<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', paddingBottom: 120, fontFamily}}>
				<div style={{fontSize: 104, fontWeight: 800, color: '#fff', letterSpacing: -2, marginBottom: 44}}>
					<Words text="One key per decision" />
				</div>
				<div style={{display: 'grid', gridTemplateColumns: 'auto auto', columnGap: 50, rowGap: 16}}>
					{rows.map(([keys, label], i) => (
						<Shortcut key={label} keys={keys} label={label} delay={6 + i * beat(0.45)} />
					))}
				</div>
			</AbsoluteFill>
		</>
	);
};

// ---------------------------------------------------------------- ending

const REPO = 'github.com/kreaton/ImageLightBox';

const OpenSource = () => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const accent = useAccent();
	const url = spring({frame: frame - 20, fps, config: {damping: 15, stiffness: 140}});
	return (
		<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', fontFamily, textAlign: 'center', paddingBottom: 110}}>
			<div style={{fontSize: 30, fontWeight: 800, letterSpacing: 6, textTransform: 'uppercase', color: '#d8d8e4', marginBottom: 18}}>
				<Words text="Free, and open source" step={3} />
			</div>
			<div style={{fontSize: 120, fontWeight: 800, color: '#fff', letterSpacing: -3, lineHeight: 1.05}}>
				<Words text="MIT licensed" delay={6} step={4} />
			</div>
			<div
				style={{
					marginTop: 54,
					padding: '22px 46px',
					borderRadius: 999,
					background: 'rgba(255,255,255,0.08)',
					border: `2px solid ${accent}`,
					boxShadow: `0 0 50px ${accent}`,
					fontSize: 64,
					fontWeight: 600,
					color: '#fff',
					opacity: Math.min(1, url * 1.4),
					transform: `translateY(${(1 - url) * 30}px) scale(${0.9 + url * 0.1})`,
				}}
			>
				{REPO}
			</div>
			<div style={{fontSize: 36, fontWeight: 400, color: '#d8d8e4', marginTop: 34, opacity: interpolate(frame, [36, 50], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'})}}>
				No account, no cloud. Your photos stay on your PC.
			</div>
		</AbsoluteFill>
	);
};

const Finale = () => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const icon = spring({frame, fps, config: {damping: 11, stiffness: 120}});
	const pulse = beatPulse(frame);
	return (
		<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', fontFamily, textAlign: 'center', paddingBottom: 80}}>
			<Img src={staticFile('icon.png')} style={{width: 220, height: 220, transform: `scale(${icon * (1 + pulse * 0.04)})`, filter: 'drop-shadow(0 30px 60px rgba(0,0,0,0.55))'}} />
			<div style={{fontSize: 130, fontWeight: 800, color: '#fff', letterSpacing: -3, marginTop: 20}}>
				<Words text="Image Lightbox" delay={6} step={4} />
			</div>
			<div style={{fontSize: 62, fontWeight: 600, color: '#fff', marginTop: 16}}>
				<Words text="Look. Decide. Next." delay={18} step={8} />
			</div>
			<div style={{fontSize: 36, fontWeight: 400, color: '#d8d8e4', marginTop: 26}}>
				<Words text="For Windows" delay={46} step={3} />
			</div>
		</AbsoluteFill>
	);
};

const CREDIT_ROWS: [string, string][] = [
	['Music', 'Memory Shelf'],
	['Made by', 'Per Hejndorf, with Claude (Anthropic)'],
	['Pictures', 'Public-domain paintings from Wikimedia Commons, and AI-generated village scenes'],
	['Metadata', 'MetadataExtractor by Drew Noakes (Apache-2.0)'],
	['Map', 'Leaflet (BSD-2-Clause) · © OpenStreetMap contributors'],
];

const Credit = ({label, value, delay}: {label: string; value: string; delay: number}) => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const phone = useIsPhone();
	const s = spring({frame: frame - delay, fps, config: {damping: 200}});
	return (
		<div style={{opacity: s, transform: `translateY(${(1 - s) * 16}px)`, marginBottom: phone ? 30 : 22}}>
			<div style={{fontSize: phone ? 26 : 22, fontWeight: 800, letterSpacing: 3, textTransform: 'uppercase', color: '#c9c9d8'}}>{label}</div>
			<div style={{fontSize: phone ? 42 : 36, fontWeight: 600, color: '#fff', lineHeight: 1.2}}>{value}</div>
		</div>
	);
};

const Credits = () => {
	const frame = useCurrentFrame();
	const accent = useAccent();
	const {durationInFrames} = useVideoConfig();
	const fade = interpolate(frame, [durationInFrames - 30, durationInFrames - 2], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
	return (
		<>
			<Shot name="about" from={{x: 0.5, y: 0.52, z: 1.75}} width={900} style={{left: 60, right: 'auto'}} />
			<div style={{position: 'absolute', left: 1020, top: 0, bottom: 110, width: 840, display: 'flex', flexDirection: 'column', justifyContent: 'center', fontFamily}}>
				<div style={{fontSize: 74, fontWeight: 800, color: '#fff', letterSpacing: -1.5, marginBottom: 30, textShadow: `0 0 34px ${accent}`}}>
					<Words text="Credits" />
				</div>
				{CREDIT_ROWS.map(([label, value], i) => (
					<Credit key={label} label={label} value={value} delay={6 + i * 6} />
				))}
				<Credit label="Source code (MIT)" value={REPO} delay={6 + CREDIT_ROWS.length * 6} />
				<div style={{fontSize: 28, fontWeight: 400, color: '#d8d8e4', marginTop: 8, opacity: interpolate(frame, [40, 60], [0, 1], {extrapolateRight: 'clamp'})}}>
					© 2026 Per Hejndorf · Version 1.0.9
				</div>
			</div>
			<AbsoluteFill style={{background: '#000', opacity: fade}} />
		</>
	);
};

// ---------------------------------------------------------------- timeline, in beats of the song

const musicVolume = (end: number, fadeFrom: number) => (frame: number) =>
	Math.min(interpolate(frame, [0, 6], [0, 1], {extrapolateRight: 'clamp'}), interpolate(frame, [fadeFrom, end - 4], [1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'}));

export const Promo = () => (
	<AbsoluteFill style={{background: '#0b0a12'}}>
		<Html5Audio src={staticFile('music.mp3')} volume={musicVolume(TOTAL_FRAMES, b(143))} />
		<Stage />
		<Beats from={0} to={12}>
			<Prints count={16} clearCaption={false} />
		</Beats>
		<Beats from={12} to={90}>
			<Prints />
		</Beats>
		<Beats from={100} to={148}>
			<Prints count={16} />
		</Beats>
		<Visualizer />

		<Beats from={0} to={4}><Logo /></Beats>
		<Beats from={4} to={8}><Hook /></Beats>
		<Beats from={8} to={12}><ThreeWords /></Beats>

		<Beats from={12} to={20}><OpenFolder /></Beats>
		<Beats from={20} to={28}><Sizes /></Beats>
		<Beats from={28} to={36}><Exif /></Beats>

		<Beats from={36} to={46}><Problems /></Beats>
		<Beats from={46} to={56}><KeepThis /></Beats>
		<Beats from={56} to={66}><Rapid /></Beats>
		<Beats from={66} to={74}><Fixes /></Beats>
		<Beats from={74} to={82}><Move /></Beats>
		<Beats from={82} to={90}><Rename /></Beats>

		<Beats from={90} to={100}><FullScreen /></Beats>
		<Beats from={100} to={110}><MapScene /></Beats>
		<Beats from={110} to={116}><Movies /></Beats>
		<Beats from={116} to={126}><Keyboard /></Beats>

		<Beats from={126} to={132}><OpenSource /></Beats>
		<Beats from={132} to={138}><Finale /></Beats>
		<Beats from={138} to={148}><Credits /></Beats>
	</AbsoluteFill>
);

// ---------------------------------------------------------------- the Short: portrait and under a minute

const PhoneHook = () => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	const icon = spring({frame, fps, config: {damping: 11, stiffness: 120}});
	const pulse = beatPulse(frame);
	return (
		<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center', fontFamily, textAlign: 'center', paddingBottom: 240}}>
			<Img src={staticFile('icon.png')} style={{width: 240, height: 240, transform: `scale(${icon * (1 + pulse * 0.04)})`, filter: 'drop-shadow(0 30px 60px rgba(0,0,0,0.55))'}} />
			<div style={{fontSize: 104, fontWeight: 800, color: '#fff', letterSpacing: -2, marginTop: 30}}>
				<Words text="Image Lightbox" delay={6} step={4} />
			</div>
			<div style={{fontSize: 120, fontWeight: 800, lineHeight: 1.04, color: '#fff', letterSpacing: -3, width: 960, marginTop: 70}}>
				<Words text="Thousands of photos." delay={beatsToFrames(1.5)} step={3} />
				<br />
				<span style={{color: '#d8d8e4'}}>
					<Words text="One afternoon." delay={beatsToFrames(3)} step={4} />
				</span>
			</div>
		</AbsoluteFill>
	);
};

const beatsToFrames = (n: number) => b(n) - b(0);

/** The finale and the credits on one screen, because a Short has no time for both. */
const PhoneEnding = () => {
	const frame = useCurrentFrame();
	const {fps, durationInFrames} = useVideoConfig();
	const icon = spring({frame, fps, config: {damping: 11, stiffness: 120}});
	const pulse = beatPulse(frame);
	const fade = interpolate(frame, [durationInFrames - 16, durationInFrames - 2], [0, 1], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
	return (
		<>
			<div style={{position: 'absolute', left: 60, top: 170, width: 900, fontFamily}}>
				<div style={{display: 'flex', alignItems: 'center', gap: 30}}>
					<Img src={staticFile('icon.png')} style={{width: 140, height: 140, transform: `scale(${icon * (1 + pulse * 0.04)})`}} />
					<div style={{fontSize: 88, fontWeight: 800, color: '#fff', letterSpacing: -3}}>
						<Words text="Image Lightbox" delay={3} step={3} />
					</div>
				</div>
				<div style={{fontSize: 64, fontWeight: 600, color: '#fff', margin: '26px 0 56px'}}>
					<Words text="Look. Decide. Next." delay={8} step={4} />
				</div>
				{CREDIT_ROWS.map(([label, value], i) => (
					<Credit key={label} label={label} value={value} delay={10 + i * 3} />
				))}
				<Credit label="Free and open source, MIT" value={REPO} delay={10 + CREDIT_ROWS.length * 3} />
				<div style={{fontSize: 32, fontWeight: 400, color: '#d8d8e4', marginTop: 10, opacity: interpolate(frame, [24, 36], [0, 1], {extrapolateRight: 'clamp'})}}>
					© 2026 Per Hejndorf · Version 1.0.9 · For Windows
				</div>
			</div>
			<AbsoluteFill style={{background: '#000', opacity: fade}} />
		</>
	);
};

export const PromoShort = () => (
	<AbsoluteFill style={{background: '#0b0a12'}}>
		<Html5Audio src={staticFile('music.mp3')} volume={musicVolume(SHORT_FRAMES, b(71))} />
		<Stage />
		<Beats from={0} to={9}>
			<Prints count={16} clearCaption={false} />
		</Beats>
		<Beats from={9} to={49}>
			<Prints />
		</Beats>
		<Visualizer />

		<Beats from={0} to={5}><PhoneHook /></Beats>
		<Beats from={5} to={9}><ThreeWords /></Beats>

		<Beats from={9} to={17}><OpenFolder /></Beats>

		{/* A Short has to stay under a minute, so the sizes, EXIF, move, rename, movies and keyboard scenes are left out. */}
		<Beats from={17} to={25}><Problems /></Beats>
		<Beats from={25} to={33}><KeepThis /></Beats>
		<Beats from={33} to={41}><Rapid /></Beats>
		<Beats from={41} to={49}><Fixes /></Beats>
		<Beats from={49} to={57}><FullScreen /></Beats>
		<Beats from={57} to={64}><MapScene /></Beats>

		<Sequence from={b(64)} durationInFrames={SHORT_FRAMES - b(64)}><PhoneEnding /></Sequence>
	</AbsoluteFill>
);
