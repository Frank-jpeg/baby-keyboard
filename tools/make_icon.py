"""Build the app's small original keycap icon; Pillow is only a development tool."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

root = Path(__file__).resolve().parents[1]
size = 512
image = Image.new('RGBA', (size, size))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((12, 12, 500, 500), radius=112, fill='#101c2b', outline='#294c55', width=8)
glow = Image.new('RGBA', image.size)
g = ImageDraw.Draw(glow)
g.rounded_rectangle((83, 69, 429, 413), radius=75, fill='#42b9aa')
glow = glow.filter(ImageFilter.GaussianBlur(48))
image = Image.alpha_composite(image, glow)
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((77, 59, 435, 417), radius=79, fill='#163333', outline='#8ae7cd', width=6)
font = ImageFont.truetype('C:/Windows/Fonts/seguisb.ttf', 257)
draw.text((256, 235), 'B', font=font, anchor='mm', fill='#c8fff0')
draw.rounded_rectangle((169, 451, 343, 465), radius=7, fill='#77dbbe')
target = root / 'src/BabyKeyboard.App/Assets'
target.mkdir(parents=True, exist_ok=True)
image.save(target / 'BabyKeyboard.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(target / 'BabyKeyboard.ico')
