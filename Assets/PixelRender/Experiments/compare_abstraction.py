"""Static study inspired by Gerstner et al. (2012), NOT an exact reproduction.

Uses palette-aware local SLIC assignment, 4-neighbor center smoothing and
deterministic weighted k-means. MCDA and the paper's bilateral step are omitted.
Dependencies: numpy, Pillow. Run with --help for input/output options.
"""
import argparse
import json
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


def to_lab(rgb):
    linear = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    xyz = linear @ np.array([[.4124564, .2126729, .0193339],
                             [.3575761, .7151522, .1191920],
                             [.1804375, .0721750, .9503041]])
    xyz /= np.array([.95047, 1., 1.08883])
    f = np.where(xyz > (6/29)**3, np.cbrt(xyz), xyz / (3*(6/29)**2) + 4/29)
    return np.stack([116*f[..., 1]-16, 500*(f[..., 0]-f[..., 1]), 200*(f[..., 1]-f[..., 2])], -1)


def to_rgb(lab):
    fy = (lab[..., 0]+16)/116
    f = np.stack([fy+lab[..., 1]/500, fy, fy-lab[..., 2]/200], -1)
    xyz = np.where(f > 6/29, f**3, 3*(6/29)**2*(f-4/29)) * [.95047, 1., 1.08883]
    linear = xyz @ np.array([[3.2404542, -.9692660, .0556434],
                             [-1.5371385, 1.8760108, -.2040259],
                             [-.4985314, .0415560, 1.0572252]])
    rgb = np.where(linear <= .0031308, 12.92*linear, 1.055*np.maximum(linear, 0)**(1/2.4)-.055)
    return np.uint8(np.clip(np.rint(rgb*255), 0, 255))


def quantize(values, k, weights=None, initial=None, iterations=30):
    """Deterministic farthest-point seeds followed by weighted Lloyd iterations."""
    weights = np.ones(len(values)) if weights is None else weights
    if initial is None:
        centers = [np.average(values, axis=0, weights=weights)]
        for _ in range(k-1):
            distance = np.min(np.sum((values[:, None]-np.array(centers))**2, axis=2), axis=1)
            centers.append(values[np.argmax(distance*weights)])
        centers = np.array(centers)
    else:
        centers = initial.copy()
    for _ in range(iterations):
        labels = np.argmin(np.sum((values[:, None]-centers)**2, axis=2), axis=1)
        new = centers.copy()
        for j in range(k):
            selected = labels == j
            if np.any(selected):
                new[j] = np.average(values[selected], axis=0, weights=weights[selected])
        delta = np.max(np.abs(new-centers))
        centers = new
        if delta < .001:
            break
    labels = np.argmin(np.sum((values[:, None]-centers)**2, axis=2), axis=1)
    return centers, labels


def joint(image, width, height, k, iterations=24, compactness=28.):
    # Use all 640x360 working pixels; output cells keep fixed rectangular IDs.
    rgb = np.asarray(image, dtype=float)/255
    lab = to_lab(rgb)
    h, w = lab.shape[:2]
    sy, sx = h/height, w/width
    oy, ox = np.mgrid[:height, :width]
    centers = np.stack([(ox+.5)*sx, (oy+.5)*sy], -1).reshape(-1, 2)
    means = to_lab(np.asarray(image.resize((width, height), Image.Resampling.BOX), dtype=float)/255).reshape(-1, 3)
    palette, labels = quantize(means, k)
    yy, xx = np.mgrid[:h, :w]
    bx = np.minimum((xx/sx).astype(int), width-1)
    by = np.minimum((yy/sy).astype(int), height-1)
    colors = lab.reshape(-1, 3)
    history = []
    for iteration in range(iterations):
        best = np.full((h, w), np.inf)
        assignment = np.zeros((h, w), dtype=np.int32)
        # Search the 3x3 neighborhood of the original cell, not the entire image.
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                cy, cx = by+dy, bx+dx
                valid = (cy >= 0) & (cy < height) & (cx >= 0) & (cx < width)
                index = np.clip(cy, 0, height-1)*width+np.clip(cx, 0, width-1)
                spatial = ((xx-centers[index, 0])/sx)**2 + ((yy-centers[index, 1])/sy)**2
                color = np.sum((lab-palette[labels[index]])**2, axis=-1)
                distance = np.sqrt(color) + compactness*np.sqrt(spatial)
                update = valid & (distance < best)
                best[update], assignment[update] = distance[update], index[update]
        ids = assignment.ravel()
        count = np.bincount(ids, minlength=width*height)
        occupied = count > 0
        for channel in range(3):
            means[occupied, channel] = np.bincount(ids, weights=colors[:, channel], minlength=len(count))[occupied]/count[occupied]
        for channel, coords in enumerate((xx, yy)):
            centers[occupied, channel] = np.bincount(ids, weights=coords.ravel(), minlength=len(count))[occupied]/count[occupied]
        grid = centers.reshape(height, width, 2)
        # Four-neighbor smoothing with degree normalization at image borders.
        total, degree = np.zeros_like(grid), np.zeros((height, width, 1))
        total[1:] += grid[:-1]; degree[1:] += 1
        total[:-1] += grid[1:]; degree[:-1] += 1
        total[:, 1:] += grid[:, :-1]; degree[:, 1:] += 1
        total[:, :-1] += grid[:, 1:]; degree[:, :-1] += 1
        centers = (.6*grid + .4*total/degree).reshape(-1, 2)
        # Uniform output-cell weighting; retain empty cells' prior means.
        palette, labels = quantize(means, k, initial=palette, iterations=8)
        history.append({"iteration": iteration+1, "empty_regions": int(np.sum(~occupied)),
                        "mean_assignment_cost": float(np.mean(best))})
    output = to_rgb(palette[labels]).reshape(height, width, 3)
    # Diagnostic: source regions filled using their corresponding output colors.
    regions = to_rgb(palette[labels[assignment]])
    return Image.fromarray(output), Image.fromarray(regions), history


def run(source, destination):
    destination.mkdir(parents=True, exist_ok=True)
    source_image = Image.open(source).convert('RGB')
    if source_image.size != (1280, 720):
        raise ValueError('Expected an actual 1280x720 scene capture, not an upscaled low-resolution preview')
    working = source_image.resize((640, 360), Image.Resampling.BOX)
    # Check conversions independently of the comparison algorithms.
    probes = np.array([[0, 0, 0], [1, 1, 1], [1, 0, 0], [.2, .5, .8]])
    assert np.max(np.abs(to_rgb(to_lab(probes)).astype(float)-np.rint(probes*255))) <= 1
    report = {"source": str(source), "source_size": source_image.size,
              "working_size": working.size, "colors": 16, "runs": [],
              "method": "Palette-aware local SLIC + deterministic k-means; no MCDA, bilateral filter, importance map or temporal coherence"}
    for width, height in ((80, 45), (128, 72)):
        start = time.perf_counter()
        nearest = source_image.resize((width, height), Image.Resampling.NEAREST)
        nearest.save(destination/f'{width}x{height}_Unrestricted.png')
        near_palette, near_labels = quantize(to_lab(np.asarray(nearest, dtype=float)/255).reshape(-1, 3), 16)
        nearest = Image.fromarray(to_rgb(near_palette[near_labels]).reshape(height, width, 3))
        box = source_image.resize((width, height), Image.Resampling.BOX)
        baseline_palette, baseline_labels = quantize(to_lab(np.asarray(box, dtype=float)/255).reshape(-1, 3), 16)
        limited = Image.fromarray(to_rgb(baseline_palette[baseline_labels]).reshape(height, width, 3))
        optimized, regions, history = joint(working, width, height, 16)
        variants = [('Nearest + 16 colors', nearest), ('Area + 16 colors', limited), ('Joint approx. / 16 colors', optimized)]
        counts = {}
        for name, result in variants:
            key = name.split()[0]
            counts[key] = len(np.unique(np.asarray(result).reshape(-1, 3), axis=0))
            result.save(destination/f'{width}x{height}_{key}.png')
        assert all(value <= 16 for value in counts.values())
        assert optimized.size == (width, height)
        regions.save(destination/f'{width}x{height}_Regions.png')
        # All methods have the same K, but learn their own palettes.
        scale = 5 if width == 80 else 4
        panel_w, panel_h = width*scale, height*scale
        sheet = Image.new('RGB', (3*panel_w+48, panel_h+100), '#19212d')
        draw = ImageDraw.Draw(sheet)
        for j, (name, result) in enumerate(variants):
            x = 12+j*(panel_w+12)
            sheet.paste(result.resize((panel_w, panel_h), Image.Resampling.NEAREST), (x, 48))
            draw.text((x, 12), name, fill='white')
            draw.text((x, panel_h+62), f'{width}x{height} | {counts[name.split()[0]]} unique colors', fill='#b9c9dc')
        sheet.save(destination/f'Comparison_{width}x{height}.png')
        report['runs'].append({"size": [width, height], "unique_colors": counts,
                               "seconds": round(time.perf_counter()-start, 2), "history": history})
    (destination/'validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    html = '''<!doctype html><meta charset="utf-8"><title>Pixel abstraction study</title>
<style>body{background:#19212d;color:#ddd;font:16px system-ui;margin:30px}img{max-width:100%;image-rendering:pixelated}p{max-width:1000px}</style>
<h1>像素区域与色板联合优化：静帧实验</h1>
<p>同一场景。左：最近邻缩小 + 16 色；中：面积缩小 + 16 色；右：区域与色板联合优化近似（16 色）。三种方法具有相同颜色预算，各自学习调色板。当前近似采用 k-means，未复现论文的 MCDA 和双边滤波，不代表论文完整结果，也不保证优于基线。不限制颜色的最近邻图另存为 Unrestricted.png。</p>
<img src="Comparison_80x45.png"><img src="Comparison_128x72.png">
<h2>1280×720 源图</h2><img src="Source.png">
<h2>区域映射诊断（80×45）</h2><p>此图显示高分辨率输入的区域分配，非最终像素网格。</p><img src="80x45_Regions.png">
<p>静态实验，无帧间稳定性约束。用门窗、树干、屋顶与栅栏检查结构；没有人物样本，不能据此得出人物识别结论。</p>'''
    (destination/'index.html').write_text(html, encoding='utf-8')
    print(json.dumps({"output": str(destination.resolve()), "runs": [{k:v for k,v in r.items() if k!='history'} for r in report['runs']]}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    default = Path(__file__).resolve().parent/'Output'
    parser.add_argument('--source', type=Path, default=default/'Source.png')
    parser.add_argument('--output', type=Path, default=default)
    args = parser.parse_args()
    run(args.source, args.output)
