"""
Resizes and converts the images downloaded from the old WordPress site into the small WebP files the new site serves.
Run once (and again only if the sources change):  python scripts/prepare-images.py <folder-with-downloaded-originals> <folder-with-the-two-portraits>
The originals are named YEAR__MONTH__file as they were saved from the old site's uploads folder. Nothing is cropped except the logo's empty margin.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

SRC = Path(sys.argv[1])
PORTRAITS = Path(sys.argv[2])
OUT = Path(__file__).resolve().parent.parent / "public" / "images"
APP = Path(__file__).resolve().parent.parent / "src" / "app"
NAVY = (11, 31, 68)

for sub in ("", "gallery", "partners"):
    (OUT / sub).mkdir(parents=True, exist_ok=True)


def load(name):
    return Image.open(SRC / name).convert("RGBA" if name.endswith(".png") else "RGB")


def save(im, dest, max_side, quality=74):
    im = im.copy()
    im.thumbnail((max_side, max_side), Image.LANCZOS)
    if im.mode == "P":
        im = im.convert("RGBA")
    im.save(OUT / dest, "WEBP", quality=quality, method=6)
    print(f"{dest:40} {im.size[0]}x{im.size[1]} {(OUT / dest).stat().st_size // 1024} KB")


# ---- hero slides, page header, bands
save(load("2024__11__16-scaled.jpg"), "hero-1.webp", 1920)
save(load("2024__11__4.jpeg"), "hero-2.webp", 1200)
save(load("2025__01__calgary-scaled.jpeg"), "hero-3.webp", 1920)
save(load("2024__07__slide2.jpg"), "page-header.webp", 1920)
save(load("2024__11__1-scaled.jpg"), "band.webp", 1920)
save(load("2024__07__nichotan-header.jpg"), "footer-bg.webp", 1600, 60)

# ---- home photo strip
for out, src in (("home-1", "2024__10__Predepature-.jpg"), ("home-2", "2024__10__Okanagan-College-Grad.jpg"),
                 ("home-3", "2025__01__Thompson-Rivers-2025-Winter-Predepature-scaled.jpg"),
                 ("home-4", "2025__01__NICHO-TAN-Team-TRU-Predepature-scaled.jpg")):
    save(load(src), f"{out}.webp", 1000)

# ---- service accordion images (order matches the five services)
for i, src in enumerate(("2024__11__2-scaled.jpg", "2024__11__17.jpg", "2024__11__6.jpg", "2024__11__1-scaled.jpg", "2024__11__12.jpg"), 1):
    save(load(src), f"service-{i}.webp", 1000)

# ---- gallery: a small thumbnail and a larger version for the lightbox, in the old site's order
gallery = ["2024__11__1-1-scaled.jpg", "2024__11__2-1-scaled.jpg", "2024__11__3-1-scaled.jpg", "2024__11__7.jpg", "2024__11__5.jpg",
           "2024__11__4-1.jpeg", "2024__11__6-1.jpg", "2024__11__9.jpg", "2024__11__8.jpg", "2024__11__12-1.jpg", "2024__11__11.jpg",
           "2024__11__13.jpg", "2024__11__17-1.jpg", "2024__11__16-1-scaled.jpg", "2024__11__15.jpg", "2024__11__19.jpg",
           "2024__11__18.jpg", "2024__10__Okanagan-College-Grad.jpg"]
for i, src in enumerate(gallery, 1):
    im = load(src)
    save(im, f"gallery/{i:02d}-thumb.webp", 640, 70)
    save(im, f"gallery/{i:02d}.webp", 1600, 76)

# ---- leadership portraits
for name, f in (("jonas", "jonas.jpg"), ("chingombe", "chin.jpg")):
    im = Image.open(PORTRAITS / f).convert("RGB")
    im.save(OUT / f"{name}.webp", "WEBP", quality=85, method=6)
    print(name, im.size)

# ---- partner logos (these are small already: keep their size, only change the format)
# Partners the website no longer lists (MacEwan 4, Acadia 9, UNB 11, Waterloo 15) are skipped.
for i in (1, 2, 3, 5, 6, 7, 8, 10, 12, 13, 14, 16):
    save(load(f"2024__07__partner-{i}.png"), f"partners/partner-{i}.webp", 400, 90)
for name in ("vancouver.jpeg", "windsor.png", "download-2.png", "simon-fraser.png", "northen_b_columbia.png"):
    save(load(f"2024__10__{name}"), f"partners/{Path(name).stem}.webp", 600, 90)

# ---- accreditation badges
save(load("2026__11__ICEF-ACCREDITATION.png"), "icef-accreditation.webp", 1000, 82)
save(load("2026__11__Borderpass-Verified-Agent-Certificate-scaled.png"), "borderpass-certificate.webp", 1400, 82)
save(load("2026__11__Screenshot-2026-11-23-at-11.42.38.png"), "british-council-certificate.webp", 900, 82)

# ---- logo (white + lime crest on transparency: only readable on navy) and the favicon/app icon (crest on a navy tile)
logo = Image.open(SRC / "2024__07__Logo-Color.png").convert("RGBA")
logo = logo.crop(logo.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox())
save(logo, "logo.webp", 520, 90)
logo.thumbnail((520, 520), Image.LANCZOS)
logo.save(OUT / "logo.png")


def tile(size):
    t = Image.new("RGBA", (size, size), NAVY + (255,))
    c = logo.copy()
    c.thumbnail((int(size * 0.7), int(size * 0.7)), Image.LANCZOS)
    t.paste(c, ((size - c.width) // 2, (size - c.height) // 2), c)
    return t


tile(512).save(APP / "icon.png")
tile(180).save(APP / "apple-icon.png")
og = Image.open(OUT / "hero-1.webp").convert("RGB")
og = og.resize((1200, int(1200 * og.height / og.width)), Image.LANCZOS).crop((0, 0, 1200, 630))
og.save(OUT / "og.jpg", quality=80)
print("done")
