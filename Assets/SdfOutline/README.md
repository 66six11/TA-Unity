# GPU 屏幕空间 SDF 描边

Unity 6000.5 / URP 17 RenderGraph 实现。运行时全程 GPU，无 CPU Mask 回读或轮廓连接。

## 使用

1. Unity 菜单 `Tools > TA > SDF Outline > Install on Default Renderer`。
2. 在 Renderer 的 `Screen Space SDF Outline` 中设置 `Target Layers`，目标物体及子物体放到该层。默认不选层，避免影响现有演示。
3. 设置 Width（屏幕像素）、Softness、Outline Color；Placement 支持 Outside / Inside / Centered（总宽度）。
4. Display 切换 Mask / SignedDistance 检查中间结果。蓝色为内部负距离，橙色为外部正距离，32 像素之外显示黑色。

如果相机使用自定义 Renderer，应在该 Renderer 上手动添加 SdfOutlineFeature，并指定本目录的三个 Shader 资源。

## 数据流

不透明目标 + 场景深度 → R8 可见区域 mask → GPU 四邻域边界种子 → JFA + 一次 step=1 修正 → R32 signed distance → 摄像机颜色合成。内部距离为负，外部为正，单位为当前摄像机渲染分辨率下的像素。相接目标合并为整体轮廓。JFA 是近似距离变换，非解析精确 SDF。

每相机每帧的临时纹理由 RenderGraph 管理，计算步骤显式声明读写，不使用原地 UAV 更新；分辨率变化时自动重新创建。资源不跨帧保留，空 mask 不会遗留上帧描边。SDF 暂为图内临时资源，后续效果可在 RecordRenderGraph 内直接消费。

## 限制与验证

目前支持普通非 XR 基础相机和 Scene View，不处理 Overlay 相机。仅采集不透明队列；override mask 材质不复现原材质的 alpha clip、顶点位移和自定义双面设置。透明遮挡物不进入深度判定。视口外区域未知，不在屏幕边缘制造边界；完全覆盖视口的目标没有可见轮廓。开启动态分辨率/上采样时，像素宽度依据渲染尺寸。

菜单 `Validate GPU Distance Field` 实际执行 32×32 矩形的 Seed/Jump/Distance，校验边界内外 ±0.5、中心 -7.5 以及空遮罩，并编译两个光栅 Shader；结果写入 `Library/SdfOutline-validation.txt`。此验证工具中的同步 GPU 读回仅用于编辑器校验。

## 示例场景

打开 Assets/SdfOutline/SdfOutlineDemo.unity，或使用菜单 Tools > TA > SDF Outline > Open Demo Scene。三组目标展示曲面、遮挡和相交物体的合并轮廓；独立 Renderer 位于 Demo/SdfOutlineRenderer.asset，可调整描边宽度、颜色、Placement 和 Display。场景使用第 31 层为目标、第 30 层为环境，默认 Renderer 不受影响。Demo/Preview.png 为实际相机输出。


## 运行时操作面板

示例相机已挂载 SdfOutlineControls。进入 Play Mode 后在 Game 视图右上角切换 Composite、Mask、Signed SDF、JFA Seeds、SDF Bands、Original。SDF 的蓝色为内部，橙色为外部；Seeds 的 R/G 显示最近轮廓点的屏幕坐标；等距线间隔为 4 像素。面板可调 SDF 显示范围、描边宽度、柔和度、位置和颜色，也可折叠或重置。参数只作用于该相机，不改写 Renderer 资源；编辑模式可在相机组件 Inspector 预设。


## 纸张粗糙噪声

内置 image_gen 生成资产：Resources/SdfPaperGrain.png。提示词：灰度冷压棉质水彩纸微距扫描、均匀照明、细纤维与凹坑、无文字和褶皱。原始生成提示保存在 Resources/PaperGrainPrompt.txt。线性采样、Mirror 镜像平铺、三线性过滤与 mipmap；以原图均值 0.6356 为中点，避免整体亮度偏移。噪声固定在屏幕像素空间，无随时间闪动。

面板 Paper Texture 可控制开关、颗粒强度、纹理缩放和边缘扰动；Paper Grain 视图单独显示输入纹理。Composite 中叠加纸颗粒并扰动描边距离和覆盖率；Mask、SDF、Seeds 等调试数据保持原始数值。更换纹理时可在 Renderer Feature 指定 Paper Texture 与 Paper Midpoint。


## 环绕线纹

相机操作面板 Contour Lines 可开关线纹，设置 Outside / Inside / Both、间距、线宽、范围和不透明度。默认外侧、间距 9px、线宽 1px、范围 64px；线纹使用 SDF 的周期等距线，沿可见轮廓切向延伸，覆盖范围末端渐隐。Contour Lines 调试视图仅显示原始线纹覆盖率，不受合成开关影响。该版是屏幕空间轮廓线纹，不代表三维表面曲率或模型 UV；遮挡形成的可见边界也参与线纹，相邻目标按联合轮廓连接。内部中心附近可能出现距离场中轴分叉。




## 双分支方向投影虚线

已移除 CPU 弧长累计、Mask 异步回读和弧长纹理。每相机每帧在 GPU 归约整个目标 Mask 沿投影轴的最小/最大值。JFA 最近边界坐标投影得到 v；在最近边界采样 SDF 差分法线，以垂直轴上的法线符号区分两支，分别映射 u=v/2 与 u=1-v/2。对 u 做周期阈值产生虚线，SDF 仍控制描边宽度。

面板可调投影角度、整圈虚线数量、保留比例、相位；Projection U 视图显示由蓝到红的坐标。投影范围由全部目标共享，不能保证多个分离目标各自具有完整0–1；凹形、同一截线多次穿越轮廓、法线符号不稳定区域可能跳变。该坐标不等于弧长，虚线像素长度不均匀是预期限制。屏幕 Y 向下，角度正向沿该屏幕坐标系旋转。关闭 Dashes 恢复完整描边，Duty=0全空，Duty=1完整。


## 尖刺虚线笔触

Spike Shape 直接改变现有虚线笔触形状，不另叠加完整爆炸框。尖刺使用独立的数量、相位和随机参数生成连续形状，与虚线数量、占空比和相位无关；最后再用 DashCoverage 裁切。一个虚线段内可以有多个尖刺。关闭 Spike Shape 恢复原虚线。可调尖刺数量、相位、高度、外移、随机度和种子，沿用主描边颜色与宽度；尖刺形状位于物体外侧。Spike Dashes 调试视图展示变形后乘以虚线遮罩的覆盖率。

此版使用近似投影U，分支接缝、多目标共享投影范围、凹形区域仍可能出现折返或断口；UV中分段直线映射回屏幕后不保证严格直线。随机值由固定种子和虚线编号生成，无逐帧随机闪动。


Spike Shape 调试视图显示未乘虚线遮罩的完整尖刺形状，修改任意虚线参数不改变此视图；Spike Dashes 显示两者相乘的结果。

## 受限凸形稳定模式

Single Convex Target 默认只捕获相机组件指定的 Renderer，投影范围因此属于单个目标。示例自动指定球体，其他物体保持场景可见。可在相机 Inspector 指定其他 Renderer；要求完整可见、无遮挡的凸形或投影双分支轮廓。选中模式但未指定目标时 Mask 为空。关闭该模式恢复按层捕获，多个目标的投影仍共享范围，不提供稳定性保证。

法线改为半径3像素的双线性SDF差分；通过连续近似垂足投影得到共享U，尖刺与虚线使用同一套坐标；连续性检测使用圆周嵌入以正确识别0/1循环接缝。仍为屏幕空间近似，没有跨帧跟踪，不保证模型表面锚定、视口裁切、遮挡或轮廓拓扑变化时稳定。


绕视角验证：1280×720 正交相机，单球体，测试期间隐藏其他渲染物体，0–90°共10个角度；Projection U 图在有效带区域相对0°的RGB平均绝对差均值0.007541、最差0.010477（归一化到0–1）。该指标是对称球体截图差异，不代表任意凸形的跨帧稳定性，也不是几何UV误差或伪尖刺数量。原始输出见 Docs/Measurements/SdfConvexStability-2026-10-30.txt。

