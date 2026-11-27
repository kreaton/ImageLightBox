import {Composition} from 'remotion';
import {Promo, PromoShort} from './Promo';
import {FPS, SHORT_FRAMES, TOTAL_FRAMES} from './timing';

export const RemotionRoot = () => (
	<>
		{/* Designed at half size; `--scale=2` renders both at 4K with sharp text and graphics. */}
		<Composition id="Promo" component={Promo} durationInFrames={TOTAL_FRAMES} fps={FPS} width={1920} height={1080} />
		<Composition id="PromoShort" component={PromoShort} durationInFrames={SHORT_FRAMES} fps={FPS} width={1080} height={1920} />
	</>
);
