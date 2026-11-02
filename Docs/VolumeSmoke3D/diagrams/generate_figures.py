"""Rebuild the source-controlled SVG and PNG explanation figures.

Run with Python + matplotlib. Default font on Windows: Microsoft YaHei.
"""
from pathlib import Path
import argparse
import math

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch, Circle, Ellipse, Polygon

OUT = Path(__file__).resolve().parent.parent / "images"
PALETTE = {
    "ink": "#182A3A", "muted": "#566B7D", "line": "#C6D2DD",
    "source": "#2563A6", "source_bg": "#ECF4FC",
    "reconstruct": "#137F77", "reconstruct_bg": "#E8F5F1",
    "sim": "#A35C13", "sim_bg": "#FFF4E3",
    "render": "#7045A2", "render_bg": "#F3EDFA",
    "model": "#D6DEE5", "smoke": "#73B9D4", "danger": "#B84942",
}
FONT = None


class Diagram:
    def __init__(self, number, title, subtitle):
        self.fig, self.ax = plt.subplots(figsize=(16, 9.8), dpi=150)
        self.fig.subplots_adjust(0, 0, 1, 1)
        self.ax.set(xlim=(0, 1600), ylim=(980, 0))
        self.ax.axis("off")
        self.fig.patch.set_facecolor("white")
        self.labels = []
        self.text(55, 35, f"{number:02d}  {title}", 25, weight="bold")
        self.text(55, 96, subtitle, 14, color="muted")
        self.ax.plot([55, 1545], [145, 145], color=PALETTE["line"], lw=1)

    def text(self, x, y, value, size=14, color="ink", weight="normal", ha="left", va="top", bounds=None):
        artist = self.ax.text(x, y, value, fontsize=size, color=PALETTE.get(color, color),
                              fontproperties=FONT, weight=weight, ha=ha, va=va, linespacing=1.6)
        self.labels.append((artist, bounds))
        return artist

    def box(self, x, y, w, h, fill="white", stroke="line", radius=14, alpha=1):
        patch = FancyBboxPatch((x, y), w, h, boxstyle=f"round,pad=0,rounding_size={radius}",
                              facecolor=PALETTE.get(fill, fill), edgecolor=PALETTE.get(stroke, stroke),
                              lw=1.2, alpha=alpha)
        self.ax.add_patch(patch)
        return patch

    def card(self, x, y, w, h, title, body, kind="source", title_size=17, body_size=14):
        self.box(x, y, w, h, kind + "_bg")
        self.ax.plot([x+19, x+67], [y+24, y+24], color=PALETTE[kind], lw=4, solid_capstyle="round")
        self.text(x+19, y+42, title, title_size, kind, weight="bold", bounds=(x, y, x+w, y+h))
        self.text(x+19, y+96, body, body_size, bounds=(x, y, x+w, y+h))

    def arrow(self, a, b, label=None, color="muted", curved=0, dashed=False, label_pos=None, size=13):
        patch = FancyArrowPatch(a, b, arrowstyle="-|>", mutation_scale=15, lw=1.7,
                                color=PALETTE.get(color, color),
                                connectionstyle=f"arc3,rad={curved}", linestyle="--" if dashed else "-")
        self.ax.add_patch(patch)
        if label:
            x, y = label_pos or ((a[0]+b[0])/2, (a[1]+b[1])/2-30)
            self.text(x, y, label, size, color, ha="center")

    def line(self, points, color="line", width=1.2, dashed=False):
        self.ax.plot([p[0] for p in points], [p[1] for p in points], color=PALETTE.get(color,color),
                     lw=width, linestyle="--" if dashed else "-")

    def footer(self, text):
        self.text(55, 927, text, 12, "muted")

    def save(self, stem):
        self.fig.canvas.draw()
        renderer = self.fig.canvas.get_renderer()
        for artist, bounds in self.labels:
            bbox = artist.get_window_extent(renderer=renderer)
            left, bottom = self.ax.transData.inverted().transform((bbox.x0, bbox.y0))
            right, top = self.ax.transData.inverted().transform((bbox.x1, bbox.y1))
            if left < -1 or right > 1601 or top < -1 or bottom > 981:
                raise ValueError(f"Text outside figure: {artist.get_text()!r}")
            if bounds:
                x0, y0, x1, y1 = bounds
                if left < x0 or right > x1 or top < y0 or bottom > y1:
                    raise ValueError(f"Text outside card: {artist.get_text()!r}")
        OUT.mkdir(parents=True, exist_ok=True)
        self.fig.savefig(OUT / f"{stem}.png", dpi=150, facecolor="white")
        self.fig.savefig(OUT / f"{stem}.svg", facecolor="white")
        plt.close(self.fig)
        print(stem)


def overview():
    d = Diagram(1, "屏幕表面输入，世界空间保存烟雾", "当前实现：动态表面捕获 + 固定世界空间三维网格；不是 Froxel 求解器。")
    d.card(55, 205, 435, 280, "捕获动态模型表面", "前表面：设备深度、法线 / Mask\n前表面：三维表面运动速度\n背表面：最远观察空间深度\n只由指定 Source Camera 捕获", "source", body_size=13)
    d.card(580, 205, 435, 280, "重建三维源区域", "体素投影 → 读取同一个像素\n逆 GPU VP → 三维表面位置\n法线偏移 → 平滑发射带\n前后深度 → 近似占据区间", "reconstruct", body_size=13)
    d.card(1110, 205, 435, 280, "每次捕获更新的 3D 输入", "Obstacle：近似固体\nSurfaceSource：法线 + 权重\nSurfaceMotion：世界表面速度\n模型移动时重建，不搬走旧烟", "reconstruct", body_size=13)
    d.arrow((492,345),(577,345),"读取",label_pos=(535,310))
    d.arrow((1017,345),(1107,345),"写入",label_pos=(1060,310))
    d.card(55, 625, 435, 250, "场景输入", "不透明场景颜色\n不透明场景深度\n观察摄像机的位置与射线", "render", body_size=14)
    d.card(580, 625, 435, 250, "体积光线步进合成", "射线与世界空间盒子求交\n读取密度 / 光照体积\n按场景深度裁剪并累积透射率", "render", body_size=13)
    d.card(1110, 625, 435, 250, "GPU 三维流体求解", "速度平流、外力、涡量约束\n压力投影、密度平流\n速度 / 密度 / 压力跨帧保留", "sim", body_size=13)
    d.arrow((1327,488),(1327,622),"下一次 Update 读取",label_pos=(1440,535),size=12)
    d.arrow((1107,750),(1018,750),"密度 / 光照",label_pos=(1060,710),size=11)
    d.arrow((492,750),(577,750),"颜色 / 深度",label_pos=(535,710),size=11)
    d.footer("资源归属：三维纹理由 VolumeSmokeSimulation 持有；屏幕捕获与相机合成目标由 RenderGraph 管理。")
    d.save("01-system-overview")


def reconstruction():
    d = Diagram(2, "从一个捕获像素恢复三维发射位置", "图中是一张穿过相机与模型的示意截面；真正的发射权重存储在三维网格中。")
    # Camera and capture plane.
    d.ax.add_patch(Polygon([(105,455),(180,420),(180,490)],closed=True,facecolor=PALETTE["source_bg"],edgecolor=PALETTE["source"],lw=2))
    d.text(88,535,"Source Camera",14,"source")
    d.box(440,240,58,470,"source_bg","source",radius=4)
    d.text(372,187,"捕获纹理平面",15,"source")
    d.ax.add_patch(FancyBboxPatch((441,437),56,36,boxstyle="square,pad=0",facecolor=PALETTE["source"],edgecolor="none"))
    d.text(375,720,"像素中心 UV + 设备深度",13,"source")
    d.line([(180,455),(498,260),(1430,220)],"line",dashed=True)
    d.line([(180,455),(498,690),(1430,750)],"line",dashed=True)
    # Domain / model cross section.
    d.box(810,235,685,490,"white","line",radius=4)
    for x in range(850,1495,70):d.line([(x,235),(x,725)],"line",width=0.5)
    for y in range(270,725,70):d.line([(810,y),(1495,y)],"line",width=0.5)
    d.text(1040,181,"世界空间三维网格（截面）",16,"sim")
    d.ax.add_patch(Ellipse((1250,465),300,365,facecolor=PALETTE["model"],edgecolor=PALETTE["muted"],lw=1.5))
    d.ax.add_patch(Circle((1052,455),72,facecolor=PALETTE["reconstruct_bg"],edgecolor=PALETTE["reconstruct"],lw=1.3,alpha=0.9))
    d.arrow((180,455),(1450,455),color="muted")
    d.ax.add_patch(Circle((1100,455),7,facecolor=PALETTE["source"],edgecolor="white",lw=1))
    d.ax.add_patch(Circle((1400,455),7,facecolor=PALETTE["muted"],edgecolor="white",lw=1))
    d.box(1016,420,70,70,"reconstruct_bg","reconstruct",radius=0)
    d.ax.add_patch(Circle((1051,455),5,facecolor=PALETTE["reconstruct"],edgecolor="none"))
    d.arrow((1090,425),(1000,425),"外向法线 N",color="reconstruct",label_pos=(1050,380),size=12)
    d.text(1065,550,"前表面 P_s",14,"source")
    d.text(1335,550,"后表面深度",14,"muted")
    d.text(825,610,"候选体素 P_i",13,"reconstruct")
    d.arrow((955,614),(1040,488),color="reconstruct")
    d.text(1180,625,"前 / 后深度之间\n近似为固体",14,"sim")
    d.box(55,795,1490,104,"source_bg",radius=10)
    d.text(78,817,"重建：像素中心 → NDC（处理 Y / 深度约定）→ 逆 GPU VP → P_s",15,"source")
    d.text(78,857,"发射：P_t = P_s + 0.75hN；按体素到 P_t 的距离生成平滑权重。h 为体素世界尺寸。",13)
    d.footer("只有前表面参与发射；一个前后深度区间不能准确表达所有凹形或多层交叠几何。")
    d.save("02-source-reconstruction")


def motion_and_frames():
    d=Diagram(3,"物体移动，旧烟留在世界空间","图示区分源模型的位姿变化、三维烟雾历史，以及当前代码的一帧输入延迟。")
    for x,label in [(55,"时刻 n：从旧位置发烟"),(850,"时刻 n+1：源向右移动")]:
        d.box(x,185,695,265,"white")
        d.text(x+22,207,label,17,"source",weight="bold")
        d.line([(x+28,408),(x+665,408)],"line")
        # Keep the same old smoke world location in both panels.
        for j in range(8):
            d.ax.add_patch(Ellipse((x+190+math.sin(j*0.9)*24,360-j*13),82-j*4,48,
                                  facecolor=PALETTE["smoke"],edgecolor="none",alpha=0.12+j*0.025))
        model_x=x+(245 if x==55 else 450)
        d.ax.add_patch(Circle((model_x,355),42,facecolor=PALETTE["model"],edgecolor=PALETTE["muted"],lw=1.4))
        d.ax.add_patch(Circle((model_x-40,355),12,facecolor=PALETTE["reconstruct"],edgecolor="none"))
        if x==850:
            d.arrow((x+275,345),(x+385,345),"位姿变化",label_pos=(x+330,305),size=12)
            d.text(x+35,257,"旧烟继续平流 / 衰减",13,"muted")
            d.text(x+458,292,"新的发射带",13,"reconstruct")
            d.arrow((x+498,323),(model_x-42,344),color="reconstruct")
        else:
            d.text(x+332,335,"表面发射",14,"reconstruct")
    d.text(55,493,"同一帧的实际顺序",18,weight="bold")
    widths=[430,430,430];xs=[55,585,1115]
    d.card(xs[0],550,widths[0],235,"① Update：推进模拟", "读取上一帧源 E[n−1]\n更新三维状态 S[n]\n旧烟不随源模型整体平移", "sim", body_size=13)
    d.card(xs[1],550,widths[1],235,"② URP：捕获与重建", "捕获当前模型位姿 M[n]\n生成当前源 E[n]\n供下一次 Update 使用", "source", body_size=13)
    d.card(xs[2],550,widths[2],235,"③ URP：显示当前烟雾", "读取三维状态 S[n]\n按当前摄像机做体积渲染\n转动视角不重置 S[n]", "render", body_size=13)
    d.arrow((488,665),(582,665),"随后",label_pos=(535,630))
    d.arrow((1018,665),(1112,665),"随后",label_pos=(1065,630))
    d.line([(800,790),(800,856),(270,856),(270,790)],"reconstruct",dashed=True)
    d.arrow((270,825),(270,790),color="reconstruct")
    d.text(450,827,"E[n] → 下一帧 Update",13,"reconstruct",ha="center",va="center")
    d.footer("摄像机改变时只更新捕获与观察；固定世界空间状态保留。动态源输入约有一帧延迟。")
    d.save("03-motion-and-frame-order")


def raymarch():
    d=Diagram(4,"沿三维体积采样，并在场景表面前停止","图中为一条屏幕像素射线的示意截面；体积密度本身并不绑定这条射线。")
    d.box(320,225,1170,430,"white",radius=4)
    d.text(340,180,"世界空间烟雾体积 AABB",16,"sim")
    for x,y,w,h in [(570,410,300,250),(760,430,320,280),(940,410,280,230)]:
        d.ax.add_patch(Ellipse((x,y),w,h,facecolor=PALETTE["smoke"],edgecolor="none",alpha=0.15))
    for x,y,w,h in [(1220,410,230,240),(1380,420,170,200)]:
        d.ax.add_patch(Ellipse((x,y),w,h,facecolor=PALETTE["model"],edgecolor="none",alpha=0.45))
    d.ax.add_patch(Polygon([(85,420),(165,386),(165,454)],closed=True,facecolor=PALETTE["source_bg"],edgecolor=PALETTE["source"],lw=2))
    d.text(65,482,"观察相机",14,"source")
    d.arrow((167,420),(1095,420),color="source")
    d.line([(1095,420),(1480,420)],"line",dashed=True)
    for j,x in enumerate(range(400,1050,65)):
        d.ax.add_patch(Circle((x,420),7,facecolor=PALETTE["source"],edgecolor="white",lw=1))
    d.box(1090,280,68,325,"model","muted",radius=3)
    d.text(1060,236,"不透明物体",15)
    d.line([(320,235),(320,650)],"sim",dashed=True)
    d.line([(1090,300),(1090,650)],"danger",dashed=True)
    d.text(340,555,"入口 t_enter",14,"sim")
    d.text(945,660,"终点 min(t_exit, t_scene)",14,"danger")
    d.text(1200,560,"后方体积仍存在\n此像素不采样它",13,"muted")
    d.box(55,740,720,150,"render_bg")
    d.text(78,765,"逐步累积",17,"render",weight="bold")
    d.text(78,810,"T_step = exp(−ρ × extinction × Δs)\nC += T × (1−T_step) × smokeColor × lighting",13)
    d.box(825,740,720,150,"sim_bg")
    d.text(848,765,"当前实现",17,"sim",weight="bold")
    d.text(848,810,"默认最多 128 步；T < 0.01 时提前结束\n场景灯光体积：默认每灯 12 次光路采样",13)
    d.footer("前方烟雾可覆盖模型；场景深度只裁剪当前像素射线，不等于模型与烟雾的完整物理碰撞。")
    d.save("04-volume-raymarch")


def main():
    global FONT
    parser=argparse.ArgumentParser()
    parser.add_argument("--font",default="C:/Windows/Fonts/msyh.ttc")
    args=parser.parse_args()
    FONT=font_manager.FontProperties(fname=args.font)
    matplotlib.rcParams["svg.fonttype"]="none"
    matplotlib.rcParams["axes.unicode_minus"]=False
    overview();reconstruction();motion_and_frames();raymarch()


if __name__=="__main__":main()
