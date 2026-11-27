import {Config} from '@remotion/cli/config';

Config.setEntryPoint('src/index.ts');
// Draw the frames on the GPU (ANGLE on Direct3D) instead of Chrome's CPU renderer. On the
// Radeon 780M a 4K render runs about 2.7x faster; x264 encoding runs alongside and costs little.
Config.setChromiumOpenGlRenderer('angle');
Config.setVideoImageFormat('jpeg');
Config.setJpegQuality(95);
Config.setCrf(16);
Config.setColorSpace('bt709');
Config.setAudioBitrate('320k');
