"""Rebuild the explanatory figures with Python + Pillow; run from any directory."""
from pathlib import Path
import math
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = HERE / 'images'
OUT.mkdir(exist_ok=True)
FONT = 'C:/Windows/Fonts/msyh.ttc'
BOLD = 'C:/Windows/Fonts/msyhbd.ttc'
BG, INK, MUTED, BLUE, TEAL = '#f3f6fa', '#16283e', '#53657a', '#346bde', '#078c91'

def text(d, xy, s, size=24, color=INK, bold=False):
    d.text(xy, s, font=ImageFont.truetype(BOLD if bold else FONT, size), fill=color)

def arrow(d, points, color=BLUE, width=5):
    d.line(points, fill=color, width=width, joint='curve')
    x, y = points[-1]; px, py = points[-2]
    angle = math.atan2(y-py, x-px)
    d.polygon([(x,y), (x-16*math.cos(angle-.45), y-16*math.sin(angle-.45)),
               (x-16*math.cos(angle+.45), y-16*math.sin(angle+.45))], fill=color)

def box(d, rect, fill='white', outline='#d7e1ed'):
    d.rounded_rectangle(rect, radius=18, fill=fill, outline=outline, width=2)

def preview(im, name, rect):
    src = Image.open(ROOT / 'Assets/ScreenSpaceFluid/Preview' / name).convert('RGB')
    src.thumbnail((rect[2]-rect[0], rect[3]-rect[1]), Image.Resampling.LANCZOS)
    im.paste(src, (rect[0]+(rect[2]-rect[0]-src.width)//2, rect[1]))

im = Image.new('RGB', (1800, 1160), BG); d = ImageDraw.Draw(im)
text(d,(55,28),'从模型表面粒子，到屏幕上的液体',44,bold=True)
text(d,(55,91),'Screen Space Fluid · 当前项目实现 / 真实调试截图',24,MUTED)
box(d,(55,145,1745,285),'#e6eefc')
text(d,(80,164),'世界空间：模型三角形 → 按面积发射 → 流场更新 → GPU 粒子缓冲区',29,bold=True)
text(d,(80,216),'粒子只有位置、速度、半径等状态；不需要连接关系，也不生成液体三角网格。',25,MUTED)

def card(x,y,n,title,img,line1,line2):
    box(d,(x,y,x+510,y+305))
    text(d,(x+20,y+13),f'{n}  {title}',27,bold=True)
    preview(im,img,(x+18,y+61,x+492,y+237))
    text(d,(x+20,y+245),line1,20,MUTED)
    text(d,(x+20,y+273),line2,20,MUTED)

card(55,340,'01','球形粒子深度','ParticleDepth.png','每粒子画球形深度；Min 混合','保留每像素最近的前表面')
card(645,340,'02','双边平滑深度','SmoothDepth.png','默认 4 组 X/Y 滤波，共 8 Pass','平滑凹凸；空白像素仍为空白')
card(1235,340,'03','重建表面法线','Normals.png','深度 + 逆投影 → 观察空间位置','邻居位置差分 → 两条切线 → 法线')
arrow(d,[(565,485),(635,485)]); arrow(d,[(1155,485),(1225,485)])
arrow(d,[(310,285),(310,332)])
card(55,740,'04','粒子厚度累加','Thickness.png','球体弦长相加；按不透明深度裁剪','重叠部分重复计数，是近似厚度')
card(645,740,'05','不透明场景','SourceOnly.png','提供背景颜色与场景深度','用于折射取色、遮挡与厚度裁剪')
card(1235,740,'06','液体合成','Liquid.png','法线控制折射与反射；厚度控制吸收','最终输出当前相机的液体画面')
arrow(d,[(80,285),(30,285),(30,890),(47,890)],TEAL)
arrow(d,[(565,890),(603,890),(603,701),(1203,701),(1203,890),(1227,890)],TEAL)
text(d,(700,670),'厚度 + 背景颜色 / 场景深度',20,TEAL)
arrow(d,[(1155,920),(1227,920)])
arrow(d,[(1490,645),(1490,732)])
text(d,(1520,676),'法线 / 深度',20,MUTED)
text(d,(55,1090),'边界：仅当前视角最近的一层表面；没有压力/体积约束；深度平滑不能填孔。',27,bold=True)
im.save(OUT/'pipeline.png')

im = Image.new('RGB',(1800,1040),BG); d = ImageDraw.Draw(im)
text(d,(55,28),'为什么一堆粒子会看起来像连续水面？',42,bold=True)
text(d,(55,91),'几何剖面示意 · 下图是原理示意，不是实际粒子或滤波输出',24,MUTED)
for x,title in [(55,'A  小球投影产生深度'),(645,'B  只保留最近的球面'),(1235,'C  平滑深度后计算法线')]:
    box(d,(x,155,x+510,660)); text(d,(x+20,177),title,25,bold=True)
    arrow(d,[(x+42,250),(x+42,548)],MUTED,3)
    text(d,(x+60,240),'相机视线 / 深度增大 ↓',20,MUTED)

centers=[(120,430,84),(240,455,98),(365,425,84)]
for cx,cy,r in centers:
    d.ellipse((55+cx-r,cy-r,55+cx+r,cy+r),fill='#cce6f5',outline=BLUE,width=3)
arrow(d,[(565,420),(635,420)])
arrow(d,[(1155,420),(1227,420)])
raw=[]
for u in range(40,465):
    vals=[cy-math.sqrt(r*r-(u-cx)**2) for cx,cy,r in centers if abs(u-cx)<=r]
    if vals: raw.append((u,min(vals)))
d.line([(645+u,y) for u,y in raw],fill=BLUE,width=7)
for cx,cy,r in centers:
    d.arc((645+cx-r,cy-r,645+cx+r,cy+r),0,180,fill='#cbd7e5',width=2)
smooth=[]
for i,(u,y) in enumerate(raw):
    neighbors=raw[max(0,i-25):min(len(raw),i+26)]
    smooth.append((u,sum(v for _,v in neighbors)/len(neighbors)))
d.line([(1235+u,y) for u,y in raw],fill='#cbd7e5',width=3)
d.line([(1235+u,y) for u,y in smooth],fill=TEAL,width=7)
for k in [65,155,245,330]:
    u,y=smooth[k]; prev=smooth[k-3][1]; nex=smooth[k+3][1]
    slope=(nex-prev)/6; length=math.hypot(slope,1)
    arrow(d,[(1235+u,y),(1235+u+45*slope/length,y-45/length)],TEAL,3)
text(d,(75,580),'球面前侧：d = dc − R√(1 − u² − v²)',21,MUTED)
text(d,(665,580),'蓝线：每条视线上的最小深度',23,MUTED)
text(d,(1255,580),'绿线：平滑表面；箭头：表面法线',21,MUTED)
box(d,(55,710,1745,975),'#e7f3f1')
text(d,(80,732),'连续感来自“深度变平滑 → 法线变连续 → 光照 / 折射连续”。',31,bold=True)
text(d,(80,793),'双边滤波同时考虑屏幕距离与深度差，尽量保留前后表面的深度断层。',26)
text(d,(80,843),'轮廓外没有粒子的像素仍是无效深度：不会自动补洞，也不保证三维体积。',26)
text(d,(80,893),'厚度由另一条累加路径提供；此图只解释可见表面的重建。',26)
im.save(OUT/'reconstruction.png')
print('Wrote', OUT/'pipeline.png', 'and', OUT/'reconstruction.png')
