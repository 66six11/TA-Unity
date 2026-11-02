"""Arrange actual Unity debug captures; no synthetic simulation imagery."""
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--font', default='C:/Windows/Fonts/msyh.ttc')
args = parser.parse_args()
font = FontProperties(fname=args.font)
root = Path(__file__).resolve().parents[3]
folder = root / 'Assets/VolumeSmoke3D/Preview/Lighting'
views = [
    ('04-composite.png', '最终合成：圆环与烟雾受聚光灯照明'),
    ('View-DensityIntegral.png', '沿观察射线的累积密度'),
    ('View-ViewTransmittance.png', '观察透射率：白色可透过，黑色吸收'),
    ('View-Scattering.png', '积分散射光：剥离背景颜色'),
    ('View-IncidentLightSlice.png', '三维入射光切片：锥形与距离衰减'),
    ('View-LightTransmittanceSlice.png', '光路透射率切片：烟雾吸收光线'),
]
fig, axes = plt.subplots(2, 3, figsize=(18, 10), dpi=150, facecolor='#f4f6f9')
for ax, (file, title) in zip(axes.flat, views):
    ax.imshow(plt.imread(folder / file))
    ax.set_title(title, fontproperties=font, fontsize=12, pad=10, color='#203346')
    ax.axis('off')
fig.suptitle('实际 GPU 渲染与中间结果 · 64³ 烟雾 / 场景聚光灯', fontproperties=font, fontsize=22, color='#203346', y=.99)
fig.text(.5, .01, '切片为独立轴向诊断图；密度/光照采用可视化映射，透射率直接显示 0–1。', fontproperties=font, fontsize=11, ha='center', color='#586b7e')
fig.subplots_adjust(left=.03, right=.97, bottom=.07, top=.90, hspace=.30, wspace=.08)
output = Path(__file__).resolve().parents[1] / 'images/lighting-contact-sheet.png'
fig.savefig(output, facecolor=fig.get_facecolor())
print(output)
