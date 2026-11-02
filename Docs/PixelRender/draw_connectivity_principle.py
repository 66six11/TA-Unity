"""Draw the implemented connectivity outline principle (schematic, not a render)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

S = 2
image = Image.new('RGB', (1600*S, 1330*S), '#f6f8fc')
d = ImageDraw.Draw(image)
font_path = 'C:/Windows/Fonts/msyh.ttc'
fonts = {n: ImageFont.truetype(font_path, n*S) for n in (19, 22, 25, 28, 34, 44)}
ink, muted, blue, orange, green = '#203246', '#52667c', '#367abb', '#cc642c', '#287d67'


def text(x, y, value, size=25, fill=ink):
    d.text((x*S, y*S), value, font=fonts[size], fill=fill)


def line(points, fill=ink, width=3):
    d.line([(x*S, y*S) for x, y in points], fill=fill, width=width*S)


def dash(a, b, fill=muted, width=2):
    length = math.dist(a, b)
    for i in range(0, int(length), 16):
        t, u = i/length, min(i+8, length)/length
        line([(a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t),
              (a[0]+(b[0]-a[0])*u, a[1]+(b[1]-a[1])*u)], fill, width)


def arrow(a, b, fill=ink, width=3):
    line([a, b], fill, width)
    angle = math.atan2(b[1]-a[1], b[0]-a[0])
    points = [b, (b[0]-13*math.cos(angle-.45), b[1]-13*math.sin(angle-.45)),
              (b[0]-13*math.cos(angle+.45), b[1]-13*math.sin(angle+.45))]
    d.polygon([(x*S, y*S) for x, y in points], fill=fill)


def dot(x, y, label, fill=blue, dx=12, dy=8):
    d.ellipse(((x-7)*S, (y-7)*S, (x+7)*S, (y+7)*S), fill=fill)
    text(x+dx, y+dy, label, 25, fill)


def box(x, y, w, h, fill, border=None):
    d.rectangle((x*S, y*S, (x+w)*S, (y+h)*S), fill=fill, outline=border, width=2*S)


text(64, 38, '连接式像素描边：先判断表面连续，再决定在哪一侧画线', 44)
text(64, 106, '当前 PixelRender 实现 · 参考 SloanePixelartURP 的连接判定与远侧合成', 22, muted)
text(64, 174, '① 侧视：法线用于预测深度，不直接按法线差描线', 34)

# Same tangent plane: both samples lie on the prediction.
text(106, 237, '同一斜面 → 连通', 28, green)
arrow((78, 300), (78, 475), muted, 2)
text(91, 428, '深度增大', 19, muted)
line([(196, 325), (599, 471)], blue, 7)
dash((180, 319), (650, 489), blue)
dot(293, 360, 'P', dx=-24, dy=12)
dot(493, 432, 'Q', dx=12, dy=8)
arrow((293, 360), (327, 266), blue)
arrow((493, 432), (527, 338), blue)
text(342, 282, '法线 Np', 22, blue)
text(539, 351, 'Nq', 22, blue)
text(150, 510, '深度虽不同，但切平面预测吻合', 25)
text(150, 551, '至少一种预测误差 ≤ 阈值 → 保持连接', 22, green)

# Two parallel surfaces at different eye depths.
text(886, 237, '遮挡 / 断层 → 不连通', 28, orange)
line([(874, 337), (1105, 337)], blue, 7)
line([(1211, 441), (1486, 441)], green, 7)
dot(1002, 337, 'P：近', dx=-55, dy=15)
dot(1333, 441, 'Q：远', fill=green, dx=12, dy=10)
arrow((1002, 337), (1002, 284), blue)
arrow((1333, 441), (1333, 388), green)
dash((1002, 337), (1430, 337), blue)
dash((930, 441), (1333, 441), green)
arrow((1380, 337), (1380, 441), orange, 2)
arrow((952, 441), (952, 337), orange, 2)
text(1392, 372, '误差', 19, orange)
text(876, 375, '误差', 19, orange)
text(889, 510, '两端的切平面预测都无法解释深度跳变', 25)
text(889, 551, '两种预测误差都 > 阈值 → 断开连接', 22, orange)

line([(64, 611), (1536, 611)], '#d4dce6', 2)
text(64, 646, '② 屏幕像素：只在“不连通边界”的远侧落笔', 34)
text(108, 707, '近侧物体 P', 25, blue)
text(812, 707, '远侧表面 / 背景 Q', 25, green)
tile, x0, y0 = 85, 108, 759
for j in range(14):
    box(x0+j*tile, y0, tile, 85, '#deebf7' if j<6 else '#e4eee9', '#aebcca')
    if j<6:
        text(x0+j*tile+32, y0+24, 'P', 25, blue)
    elif j==6:
        box(x0+j*tile+1, y0+1, tile-2, 83, ink)
        text(x0+j*tile+14, y0+27, '描边', 22, '#ffffff')
    else:
        text(x0+j*tile+31, y0+24, 'Q', 25, green)

arrow((550, 882), (602, 851), blue)
text(108, 879, 'P 看向 Q：不连通，但 P 更近', 25)
text(108, 920, '→ P 不画线，保留物体颜色', 22, blue)
arrow((1005, 882), (660, 851), orange)
text(812, 879, 'Q 看向 P：不连通，且 Q 更远', 25)
text(812, 920, '→ Q 画线，线色来自近侧 P', 22, orange)

text(64, 992, '③ 实际处理顺序', 34)
labels = [('表面信息', '深度 · 法线 · 覆盖'), ('逐段检查', '沿高分辨率路径'),
          ('四方向记录', '连通位 + 近远位'), ('远侧合成', '取近侧颜色着线')]
for i, (title, subtitle) in enumerate(labels):
    x=64+i*385
    box(x, 1054, 322, 104, '#e9eef5')
    text(x+20, 1068, title, 28)
    text(x+20, 1111, subtitle, 22, muted)
    if i<3: arrow((x+331, 1106), (x+372, 1106), muted, 2)
text(64, 1200, '相邻输出像素之间含多个信息采样点；任意一段断开，该方向即不连通。', 25)
text(64, 1250, '示意图只解释连接与描边归属；当前版本没有轮廓路径整理或艺术 AA。', 22, muted)

path = Path(__file__).with_name('ConnectivityOutlinePrinciple.png')
image.resize((1600, 1330), Image.Resampling.LANCZOS).save(path)
print(path)
