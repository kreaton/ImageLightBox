"""Builds the demo photo library the promo's screenshots are taken from.

Usage: python scripts/make-library.py <paintings folder> <images folder> <output folder> [video]

The pictures are public-domain paintings from Wikimedia Commons and AI-generated images. They get phone-style names,
EXIF from a fictional camera (the "Northwind Trail 12") and GPS positions of real places in Italy, so the EXIF panel
and the map have something to show. A few deliberate rejects (a blurred shot, a dark one, duplicates) give the
clean-up scenes something to delete. The duplicates are byte-for-byte copies, one PNG is named .jpg, and the
museum folder's file dates are a later copy date, so the app's D, E and T checks each find something.
"""
import datetime
import os
import shutil
import sys

from PIL import Image, ImageEnhance, ImageFilter
from PIL.TiffImagePlugin import IFDRational as R

paintings, images, out = sys.argv[1:4]
video = sys.argv[4] if len(sys.argv) > 4 else None
MAX_SIDE = 2400

# name, source, place (lat, lon) or None, effect
TRIP = [
    ("IMG_2201.jpg", "florence", (43.7696, 11.2558), None),
    ("IMG_2202.jpg", "corot_getty", (42.2930, 12.4110), None),
    ("IMG_2203.jpg", "ciardi", (44.2040, 10.6160), None),
    ("IMG_2204.jpg", "lunde", (41.9630, 12.7980), None),
    ("IMG_2205.jpg", "holsoe", (44.1450, 10.6650), None),
    ("IMG_2206.jpg", "linnell", (44.0900, 11.2800), "blur"),
    ("IMG_2207.jpg", "linnell", (44.0900, 11.2800), None),
    ("IMG_2208.jpg", "livorno", (43.5485, 10.3106), None),
    ("IMG_2209.jpg", "marino", (43.0930, 11.7800), "dark"),
    ("IMG_2210.jpg", "marino", (43.0930, 11.7800), None),
    ("IMG_2211.jpg", "haimann", (44.0510, 9.8370), None),
    ("IMG_2212.jpg", "olives", (43.8060, 11.2930), None),
    ("IMG_2213.jpg", "faccioli", (44.4940, 11.3430), None),
    ("IMG_2214.jpg", "sonne", (41.9250, 13.0940), None),
    ("IMG_2215.jpg", "denittis", (44.0900, 11.2850), None),
    ("IMG_2216.jpg", "gioli", (43.7200, 10.4000), None),
    ("IMG_2217.jpg", "oliver", (43.8800, 10.7800), None),
    ("IMG_2218.jpg", "kyhn", (43.3180, 11.3310), "png"),
]

FROM_ANNA = [
    ("scan-001.jpg", "Peppoli.jpg"),
    ("scan-002.jpg", "Priest at door.jpg"),
    ("scan-003.jpg", "Priest teaching children.jpg"),
    ("scan-004.jpg", "Priest with poor family.jpg"),
    ("scan-005.jpg", "Vintage Village Portrait at the Church Door.png"),
    ("scan-006.jpg", "Chapel with Cassock and stick.jpg"),
    ("scan-007.jpg", "Chapel of Saints and Memories With Cassock and Stick.png"),
    ("scan-008.jpg", "Cardinal.jpg"),
]

MUSEUM = ["chierici", "curiosity", "dealbertis", "gamba", "nystrom", "randanini", "pearce", "palizzi",
          "stueckelberg", "tiratelli", "fattori_self", "cima", "subiaco", "bologna", "nanny", "oneil"]


def gps_dms(value):
    value = abs(value)
    d = int(value)
    m = int((value - d) * 60)
    s = round(((value - d) * 60 - m) * 60 * 100)
    return (R(d, 1), R(m, 1), R(s, 100))


def save(src, dst, when, place=None, effect=None, camera=True):
    im = Image.open(src).convert("RGB")
    im.thumbnail((MAX_SIDE, MAX_SIDE), Image.LANCZOS)
    if effect == "blur":
        im = im.filter(ImageFilter.GaussianBlur(radius=max(im.size) / 110))
    elif effect == "dark":
        im = ImageEnhance.Brightness(im).enhance(0.22)
    exif = Image.Exif()
    stamp = when.strftime("%Y:%m:%d %H:%M:%S")
    exif[0x0132] = stamp  # DateTime
    if camera:
        exif[0x010F] = "Northwind"  # Make
        exif[0x0110] = "Trail 12"  # Model
        sub = exif.get_ifd(0x8769)
        sub[0x9003] = stamp  # DateTimeOriginal
        sub[0x829A] = R(1, 400 if effect != "dark" else 2000)  # ExposureTime
        sub[0x829D] = R(36, 10)  # FNumber
        sub[0x8827] = 100  # ISO
        sub[0x920A] = R(52, 10)  # FocalLength
    if place:
        lat, lon = place
        gps = exif.get_ifd(0x8825)
        gps[1] = "N" if lat >= 0 else "S"
        gps[2] = gps_dms(lat)
        gps[3] = "E" if lon >= 0 else "W"
        gps[4] = gps_dms(lon)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if effect == "png":
        im.save(dst, format="PNG", exif=exif)  # A PNG with a .jpg name, for the wrong-extension tag.
    else:
        im.save(dst, quality=90, exif=exif)
    t = when.timestamp()
    os.utime(dst, (t, t))


if os.path.exists(out):
    shutil.rmtree(out)
start = datetime.datetime(2025, 9, 14, 9, 12, 5)
for i, (name, source, place, effect) in enumerate(TRIP):
    save(os.path.join(paintings, source + ".jpg"), os.path.join(out, name), start + datetime.timedelta(hours=i * 5, minutes=i * 7), place, effect)

for i, (name, source) in enumerate(FROM_ANNA):
    save(os.path.join(images, source), os.path.join(out, "Downloads", "from Anna", name), datetime.datetime(2025, 11, 2, 18, 40) + datetime.timedelta(minutes=i), camera=False)

for i, source in enumerate(MUSEUM):
    save(os.path.join(paintings, source + ".jpg"), os.path.join(out, "Museum visit", f"IMG_{2301 + i}.jpg"), datetime.datetime(2025, 9, 18, 11, 2) + datetime.timedelta(minutes=i * 4), (43.7678, 11.2553))

# Exact copies under other names, for the duplicate tag.
shutil.copy2(os.path.join(out, "IMG_2204.jpg"), os.path.join(out, "IMG_2204 copy.jpg"))
anna = os.path.join(out, "Downloads", "from Anna")
shutil.copy2(os.path.join(anna, "scan-003.jpg"), os.path.join(anna, "copy of scan-003.jpg"))

# The museum photos were copied off the card later, so their file dates no longer match the EXIF.
copied = datetime.datetime(2025, 10, 3, 20, 15).timestamp()
for name in os.listdir(os.path.join(out, "Museum visit")):
    os.utime(os.path.join(out, "Museum visit", name), (copied, copied))

if video:
    # A movie among the photos shows as an icon, and plays in the default player.
    shutil.copyfile(video, os.path.join(out, "Downloads", "from Anna", "MOV_0031.mp4"))

os.makedirs(os.path.join(out, "Keepers"), exist_ok=True)
print("Library written to", out)
