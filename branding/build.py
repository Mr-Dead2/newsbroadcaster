#!/usr/bin/env python3
"""Builds the logo pack from logo.svg (the icon, drawn by hand).

    pip install fonttools          # text is turned into outlines, so the SVGs need no font
    python3 branding/build.py      # needs fonts-roboto-unhinted, node + playwright (for the PNGs)

Writes wordmark.svg, wordmark-on-light.svg, banner.svg, the PNG exports next to them
and site/favicon.svg (a copy of the icon).
"""
import os
import shutil
import subprocess

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = '/usr/share/fonts/truetype/roboto/unhinted/RobotoCondensed-Bold.ttf'

INK = '#f4f6f9'
INK_ON_LIGHT = '#12141a'
MUTED = '#8b93a3'
MUTED_ON_LIGHT = '#5b6472'
ACCENT = '#5cb5ff'
TAGLINE = 'IN-GAME NEWS FOR RUST SERVERS'

font = TTFont(FONT)
glyphs = font.getGlyphSet()
cmap = font.getBestCmap()
units = font['head'].unitsPerEm
cap_height = font['OS/2'].sCapHeight / units


def outline(text, size, tracking=0.0):
    """SVG path data for text with its baseline at (0, 0), and its width."""
    scale = size / units
    parts, x = [], 0.0
    for ch in text:
        glyph = glyphs[cmap[ord(ch)]]
        pen = SVGPathPen(glyphs)
        glyph.draw(TransformPen(pen, (scale, 0, 0, -scale, x, 0)))
        if pen.getCommands():
            parts.append(pen.getCommands())
        x += glyph.width * scale + tracking
    return ' '.join(parts), x - tracking


icon = open(os.path.join(HERE, 'logo.svg')).read()
defs = icon[icon.index('<defs>') + 6:icon.index('</defs>')]
art = icon[icon.index('</defs>') + 7:icon.rindex('</svg>')]


def name_block(size, ink, muted, tag_size, tag_tracking, tag_drop):
    """NEWS + BROADCASTER with the tagline under it; returns (svg, width)."""
    news, news_width = outline('NEWS', size, size / 60)
    caster, caster_width = outline('BROADCASTER', size, size / 60)
    tag, _ = outline(TAGLINE, tag_size, tag_tracking)
    svg = (f'<path d="{news}" fill="{ink}"/>'
           f'<path d="{caster}" fill="{ACCENT}" transform="translate({news_width + size / 60:.1f} 0)"/>'
           f'<path d="{tag}" fill="{muted}" transform="translate({size / 30:.1f} {tag_drop})"/>')
    return svg, news_width + caster_width + size / 60


def wordmark(ink, muted):
    height, text_x = 256, 300
    block, width = name_block(120, ink, muted, 30, 5.2, 54)
    baseline = height / 2 + 120 * cap_height / 2 - 22
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {text_x + width + 12:.0f} {height}" role="img" aria-label="NewsBroadcaster">
  <defs>{defs}</defs>
  <g transform="scale(0.5)">{art}</g>
  <g transform="translate({text_x} {baseline:.1f})">{block}</g>
</svg>
'''


def banner():
    width, height, icon_size, between = 1600, 800, 300, 64
    block, block_width = name_block(112, INK, MUTED, 31, 5.6, 58)
    left = (width - (icon_size + between + block_width)) / 2
    icon_top = 190
    cx, cy = left + icon_size / 2, icon_top + icon_size / 2

    chips, x = '', 0.0
    for word in ('POPUPS', 'POLLS', 'COUNTDOWNS', 'ARCHIVE', 'DISCORD', 'THEMES'):
        path, w = outline(word, 26, 2.5)
        chips += (f'<g transform="translate({x:.1f} 0)"><rect width="{w + 44:.1f}" height="54" rx="12" fill="#ffffff" fill-opacity="0.06" '
                  f'stroke="#ffffff" stroke-opacity="0.10" stroke-width="2"/><path d="{path}" fill="#c9d1dc" transform="translate(22 36.5)"/></g>')
        x += w + 44 + 14
    rings = ''.join(f'<circle cx="{cx:.0f}" cy="{cy:.0f}" r="{r}"/>' for r in (280, 420, 560, 700, 840))

    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" role="img" aria-label="NewsBroadcaster - in-game news for Rust servers">
  <defs>{defs}
    <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#161b27"/><stop offset="1" stop-color="#090b10"/></linearGradient>
    <radialGradient id="halo" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#5cb5ff" stop-opacity="0.22"/><stop offset="1" stop-color="#5cb5ff" stop-opacity="0"/></radialGradient>
    <radialGradient id="ember" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#ff7a33" stop-opacity="0.16"/><stop offset="1" stop-color="#ff7a33" stop-opacity="0"/></radialGradient>
  </defs>
  <rect width="{width}" height="{height}" fill="url(#sky)"/>
  <ellipse cx="{cx:.0f}" cy="{cy:.0f}" rx="620" ry="460" fill="url(#halo)"/>
  <ellipse cx="{width - 260}" cy="{height - 120}" rx="620" ry="380" fill="url(#ember)"/>
  <g fill="none" stroke="#ffffff" stroke-opacity="0.035" stroke-width="2">{rings}</g>
  <g transform="translate({left:.1f} {icon_top}) scale({icon_size / 512:.4f})">{art}</g>
  <g transform="translate({left + icon_size + between:.1f} {cy + 112 * cap_height / 2 - 26:.1f})">{block}</g>
  <g transform="translate({(width - (x - 14)) / 2:.1f} 610)">{chips}</g>
</svg>
'''


def write(name, svg):
    with open(os.path.join(HERE, name), 'w') as f:
        f.write(svg)


def render(name, out, width, height):
    script = ("const{chromium}=require('playwright');const fs=require('fs');(async()=>{const[i,o,w,h]=process.argv.slice(1);"
              "const b=await chromium.launch();const p=await b.newPage({viewport:{width:+w,height:+h}});"
              "await p.setContent('<body style=\"margin:0;background:transparent\">'+fs.readFileSync(i,'utf8').replace('<svg ','<svg width=\"'+w+'\" height=\"'+h+'\" ')+'</body>');"
              "await p.screenshot({path:o,omitBackground:true});await b.close()})()")
    subprocess.run(['node', '-e', script, os.path.join(HERE, name), os.path.join(HERE, out), str(width), str(height)], check=True)


write('wordmark.svg', wordmark(INK, MUTED))
write('wordmark-on-light.svg', wordmark(INK_ON_LIGHT, MUTED_ON_LIGHT))
write('banner.svg', banner())
shutil.copyfile(os.path.join(HERE, 'logo.svg'), os.path.join(HERE, '..', 'site', 'favicon.svg'))

wordmark_width = int(open(os.path.join(HERE, 'wordmark.svg')).read().split('viewBox="0 0 ')[1].split(' ')[0])
for size in (1024, 512, 256, 128, 64):
    render('logo.svg', f'logo-{size}.png', size, size)
render('wordmark.svg', 'wordmark.png', wordmark_width, 256)
render('wordmark-on-light.svg', 'wordmark-on-light.png', wordmark_width, 256)
render('banner.svg', 'banner.png', 1600, 800)
print('logo pack written to', HERE)
