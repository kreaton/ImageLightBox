# ImageLightBox promo video

Two introductions to ImageLightBox, made with [Remotion](https://www.remotion.dev/) the same way as the [Audiofile Player](https://github.com/kreaton/AudiofilePlayer) promo. Both are 4K for YouTube:

| Composition | Format | Length | File |
|---|---|---|---|
| `Promo` | Landscape, 3840×2160 | 101 s | `out/imagelightbox-promo-4k.mp4` |
| `PromoShort` | Portrait, 2160×3840, for YouTube Shorts | 58 s | `out/imagelightbox-short-4k.mp4` |

The landscape video is cut to the music: every cut lands on a beat of the song (77 BPM), and each scene is four, eight or ten beats long. It goes from opening a folder to the EXIF panel, then the clean-up (a duplicate with its confirmation, then blurred, dark and duplicate shots one key each), moving the keepers, renaming, full screen, the map, movies, the keyboard, and ends with the open-source note, the title and the credits.

The Short stays under 60 seconds, because YouTube blocks a Short of over a minute if the music gets a copyright claim. To give its scenes time to be read, it leaves out the thumbnail sizes, rename, movies and the keyboard.

## Setup

The music isn't in the repository. Copy `Memory_Shelf.mp3` to `public/music.mp3`.

```bash
npm install
```

## Preview and render

```bash
npm run studio
```

```bash
npm run render
```

That renders both videos. `npm run render:landscape` and `npm run render:short` render one each. Like the Audiofile Player promo, `remotion.config.ts` has Chrome draw the frames on the GPU (ANGLE). Without a usable GPU, remove that line.

Remotion is 4.0.547.

## Files

| Path | Contents |
|---|---|
| `src/Promo.tsx` | The scenes and the timeline, which is the place to change wording or timing. |
| `src/parts.tsx` | Background, music visualizer, floating prints, app window with camera, captions and key caps. |
| `src/timing.ts` | Converts beats of the song to frames. |
| `src/beat.json` | Tempo and first beat, written by `scripts/analyze-beat.mjs`. Run that script again if you change the music. |
| `scripts/make-library.py` | Builds the demo photo library the screenshots were taken from. |
| `public/app/` | Screenshots of the real app, version 1.0.5. |

## About the screenshots

They show the real app (version 1.0.5) at 1426×993, browsing a demo library at `P:\Photos` (a `subst` drive). `scripts/make-library.py` builds that library with Pillow from two folders: public-domain paintings from Wikimedia Commons, and AI-generated village scenes. It gives the paintings phone-style names, EXIF from a fictional camera (the "Northwind Trail 12") and GPS positions of real places in Italy, and adds a few rejects (a blurred copy, a dark copy, duplicates) and one movie for the clean-up scenes:

```bash
python scripts/make-library.py <paintings> <images> P:\Photos [movie.mp4]
```

The screenshots record a single clean-up session, so after a delete, move or rename the files after it shift. To retake them, rebuild the library first and do the steps in the order the video shows them. The full-screen shots are 1920×1080 captures of the whole screen.

## Credits

- Music: Memory Shelf
- Pictures: public-domain paintings from Wikimedia Commons, and AI-generated images
- Map: Leaflet, map data © OpenStreetMap contributors
- App credits are the ones on the app's About card
