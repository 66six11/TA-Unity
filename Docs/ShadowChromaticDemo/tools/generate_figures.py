from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib import font_manager

OUT = Path(__file__).resolve().parent.parent / 'images'
OUT.mkdir(parents=True, exist_ok=True)
font_manager.fontManager.addfont('C:/Windows/Fonts/msyh.ttc')
plt.rcParams.update({'font.family':'Microsoft YaHei', 'font.size':12,
    'axes.spines.top':False,'axes.spines.right':False,
    'axes.labelcolor':'#344359','text.color':'#172b43',
    'xtick.color':'#526175','ytick.color':'#526175','axes.edgecolor':'#aeb9c6',
    'figure.facecolor':'#f8fafc','axes.facecolor':'#f8fafc','axes.titleweight':'bold'})

def linear(c):
    c=np.asarray(c)
    return np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)

def display(c):
    c=np.clip(c,0,1)
    return np.where(c<=.0031308,12.92*c,1.055*c**(1/2.4)-.055)

def smooth(a,b,t):
    q=np.clip((t-a)/(b-a),0,1)
    return q*q*(3-2*q)

def F(t):
    t=np.clip(np.asarray(t),0,1)[...,None]
    p1=.5*linear([.55,.2,.78])
    p2=.5+.5*linear([1,.48,.12])
    return 3*(1-t)**2*t*p1+3*(1-t)*t*t*p2+t**3

def S(t):
    t=np.maximum(np.asarray(t),0)
    f=F(t)*smooth(0,.35,t)[...,None]
    blend=smooth(.65,1,t)[...,None]
    return np.where((t>=1)[...,None],t[...,None],f*(1-blend)+t[...,None]*blend)

GI=np.array([.018,.031,.065])
DIRECT=np.array([.65,.46,.28])
colors=['#d64652','#2b9565','#416fda']

def strip(ax,data,label):
    ax.imshow(np.tile(display(data)[None,:,:],(28,1,1)),extent=(0,1,0,1),aspect='auto')
    ax.set_yticks([])
    ax.set_xticks([])
    ax.set_ylabel(label,rotation=0,ha='right',va='center',labelpad=15)
    for sp in ax.spines.values(): sp.set_visible(False)

t=np.linspace(0,1,800)
fig=plt.figure(figsize=(11,7.6),layout='constrained')
gs=fig.add_gridspec(4,1,height_ratios=[4,.55,.55,.22])
ax=fig.add_subplot(gs[0])
ax.plot(t,t,'--',color='#7e8998',lw=1.8,label='原始可见度：RGB 同步恢复')
for i,name in enumerate(['R 通道','G 通道','B 通道']):
    ax.plot(t,F(t)[:,i],lw=2.5,color=colors[i],label=name)
ax.set(xlim=(0,1),ylim=(0,1.04),xlabel='PCF 过滤后可见度 V',ylabel='直接光 RGB 响应 F(V)')
ax.set_title('① 投射阴影：颜色发生在 PCF 的过渡内部',loc='left',pad=20)
ax.grid(alpha=.17)
ax.legend(loc='upper left',frameon=False,fontsize=10)
ax.annotate('完全遮挡\nF(0) = 0',xy=(0,0),xytext=(.12,.15),arrowprops={'arrowstyle':'->','color':'#526175'},fontsize=10)
ax.annotate('完全可见\nF(1) = 1',xy=(1,1),xytext=(.76,.79),arrowprops={'arrowstyle':'->','color':'#526175'},fontsize=10)
strip(fig.add_subplot(gs[1]),GI+DIRECT*t[:,None],'原始光照')
strip(fig.add_subplot(gs[2]),GI+DIRECT*F(t),'艺术响应')
foot=fig.add_subplot(gs[3]);foot.axis('off')
foot.text(0,.5,'阴影底色 ←                    半影中的颜色变化                    → 完整直接光',fontsize=11)
fig.suptitle('示意：紫 / 橙控制色 · 艺术强度 1 · 固定冷色 GI + 暖色直接光',fontsize=11,color='#526175')
fig.savefig(OUT/'shadow-pcf-response.png',dpi=150)
plt.close(fig)

q=np.linspace(-.15,1.25,1000)
u=np.maximum(q,0)
old=np.where((u>=1)[:,None],u[:,None],F(u))
new=S(q)
fig=plt.figure(figsize=(11,8.4),layout='constrained')
gs=fig.add_gridspec(4,1,height_ratios=[4,.6,.6,.35])
ax=fig.add_subplot(gs[0])
for lo,hi,c,label in [(-.15,0,'#e4e9f3','背光区'),(0,.35,'#e9e1f7','阴影 → 色带'),(.35,.65,'#f4e5e5','色彩响应'),(.65,1,'#fbefd9','色带 → 受光'),(1,1.25,'#e6efe8','正常受光')]:
    ax.axvspan(lo,hi,color=c,alpha=.75)
    ax.text((lo+hi)/2,1.18,label,ha='center',fontsize=10)
ax.plot(q,u,'--',color='#8d98a6',lw=1.6,label='原始 N·L 衰减')
ax.plot(q,old[:,2],color='#9a7dc2',lw=2,ls=(0,(4,3)),label='旧响应：仅使用颜色曲线（B 通道）')
ax.plot(q,new[:,2],color='#315bb7',lw=3,label='新响应：暗端缓入 + 亮端接回（B 通道）')
for v in [0,.35,.65,1]: ax.axvline(v,color='#8794a5',alpha=.35,lw=.8)
ax.set(xlim=(-.15,1.25),ylim=(-.03,1.27),xlabel='归一化法线受光量 N·L / w（负值为背光）',ylabel='自身明暗响应 S / w')
ax.set_title('② 自身明暗交界：阴影与色带平滑衔接',loc='left',pad=20)
ax.legend(loc='upper left',bbox_to_anchor=(0,.90),frameon=False,fontsize=10)
ax.annotate('暗端缓慢起步',xy=(.08,S([.08])[0,2]),xytext=(.20,.22),arrowprops={'arrowstyle':'->','color':'#315bb7'},fontsize=10)
ax.annotate('亮端匹配原始斜率',xy=(1,1),xytext=(.72,.67),arrowprops={'arrowstyle':'->','color':'#315bb7'},fontsize=10)
# Color strips use w=0.6 and span the same t interval as the plot.
def surface_strip(ax,data,label):
    ax.imshow(np.tile(display(GI+DIRECT*.6*data)[None,:,:],(30,1,1)),extent=(-.15,1.25,0,1),aspect='auto')
    ax.set(xlim=(-.15,1.25),yticks=[],xticks=[])
    ax.set_ylabel(label,rotation=0,ha='right',va='center',labelpad=15)
    for v in [0,.35,.65,1]: ax.axvline(v,color='white',alpha=.5,lw=.8)
    for sp in ax.spines.values(): sp.set_visible(False)
surface_strip(fig.add_subplot(gs[1]),old,'旧响应')
surface_strip(fig.add_subplot(gs[2]),new,'平滑衔接后')
foot=fig.add_subplot(gs[3]);foot.axis('off')
foot.text(0,.55,'色条为完整 RGB 合成；曲线仅放大展示 B 通道。w = 0.6，艺术强度 = 1。',fontsize=11)
fig.suptitle('算法示意，非 Unity 截图；位置轴表示受光量，不代表屏幕上的固定距离',fontsize=11,color='#526175')
fig.savefig(OUT/'shadow-surface-blend.png',dpi=150)
plt.close(fig)
print('Created shadow-pcf-response.png and shadow-surface-blend.png')
