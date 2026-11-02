# 延迟卡通光照

## 连通性艺术 AA

`PixelToonLighting` 的 `Connectivity AA` 开关默认开启，`AA Threshold` 默认 0.875，`AA Strength` 默认 1。卡通直接光分档后，局部连通指标低于阈值且亮度高于第一亮档时，减去 `strength / (bands - 1)`。自发光不参与降档。修正后的直接光进入 GI；最终合成仍沿用原描边。此功能不处理 GI 水彩化，也不对最终画面做模糊。

算法参考 [SloanePixelartURP 的连通统计与着色修正](https://github.com/SL0ANE/SloanePixelartURP/tree/1738732ad202a4d79d575005d9b85c2f12876d8f/Assets/SloanePixelartRenderPipeline)。在每个最终像素对应的信息缓冲区域建立采样网格，用深度与法线的双向切平面预测误差判定四邻接，再从中心所属表面局部泛洪。统计连通样本，以及“连通或比中心更近”的样本比例；后一项供 AA 使用。`AAConnectivity` 为最终像素尺寸 RGBAHalf：R 连通比例，G 连通或更近比例，B 阈值命中，A 中心有效遮罩。

适配差异：网格按信息／输出尺寸比向上取整，每轴最多 8 点，支持非整数倍率；以实际样本数归一化，避免上游偶数尺度含端点窗口的计数超过 1。采样点落在信息 texel 中心，以最近中心样本作为泛洪种子；尺度过大时为有界稀疏近似，不是完整高分辨率拓扑恢复。几何法线目前使用已有 WorldNormal；示例没有法线贴图。1:1 分辨率只有一个样本，不产生这种边缘 AA。

统计纹理由光照组件管理，输出尺寸变化时重建，禁用时释放。关闭 AA 时跳过连通统计着色，清为全连通；每帧无 CPU 回读。局部泛洪增加 GPU 工作量，尚无帧率保证。

菜单 `Tools > TA > Pixel Render > Lighting > Enable and Verify Connectivity AA` 在预览场景验证 Shader、比例范围、边缘降档、零强度旁路、自发光不变和资源重建，再为当前小屋启用 AA，保留相机及光源设置。报告 `Library/PixelAA-validation.txt`，对照图 `Preview/AA/Off.png`、`On.png`。

小屋 `PixelRenderDemo.unity` 的 Pixel Camera 上添加并启用 `PixelToonLighting`。菜单 `Tools > TA > Pixel Render > Lighting > Integrate and Verify Toon Lighting` 接入现有场景并验证，不重建场景或改变相机参数。

流程：四张表面 MRT → 信息分辨率的卡通直接光与自发光 → 低分辨率 SSGI → 最终像素分辨率合成 → 连接式描边 → Point 放大。几何采集仍不计算光照。

`DirectRadiance` 是线性 RGBAHalf，RGB 为 `albedo × Σ(分档 N·L × 光源颜色 × 强度 × 衰减) + emission`，A 为覆盖。不含环境底色、GI、描边、曝光或色调映射。它自动成为 GI 输入；`External Radiance` 留空即可。显式外部纹理仍有更高优先级。GI 不反馈至自己的输入。

支持本场景中启用的 Directional、Point、Spot Light，最多 16 盏，按场景遍历顺序选取；运行时属性 CapturedLightCount/OmittedLightCount 提供数量。点光采用有限范围的平方平滑衰减与近距离上限，聚光额外乘内外锥插值。使用光源颜色、强度、色温、位置、方向、范围及 Culling Mask，逐像素按被捕获对象的 Layer 判断遮罩。强度为本示例的艺术控制值，不承诺匹配 URP 的物理曝光或单位。

- `Light Bands`：全局 2–6 档，默认 3 档，量化每盏灯的 N·L；距离与聚光边缘保持平滑。材质旧 `_Steps` 只用于 Scene 视图的前向 Pass。
- `Direct Intensity`：控制直接光强度，同时改变 GI 的入射源；不缩放材质自发光。
- `GI Intensity`：只调合成时 GI 的权重，不改变追踪输入。
- `Ambient Color/Intensity`：显示用环境底色，乘基本色，仅参与 Composite，不当作 GI 能量。
- `Exposure/Tone Mapping`：只影响显示；开启映射时使用 `c/(1+c)` 压缩 HDR。
- `View`：Composite 合成、DirectOnly 直接光与自发光、GIOnly 间接光、SourceRadiance 查看显示映射后的追踪源、Off 回到原 PixelCamera/GI 显示。关闭 GI 组件或将其 View 设为 Off 时合成的间接项归零。

启用光照时，以 `PixelToonLighting.View` 切换光照调试；原 `PixelCamera.View` 的基本色、法线、深度等调试仍优先显示。GI 组件负责计算，自己的展示 View 在光照组件接管输出时不参与最终呈现。

Composite = 直接光 + 自发光 + GI × GIIntensity + 基本色 × 环境底色。先在显示端曝光与压缩 HDR，再用已有 Connectivity 标志从近侧邻居取着色后的颜色生成远侧描边。只在 Composite 开启描边，其他光照视图便于检查数据。

限制：当前没有阴影贴图或屏幕空间直接光遮挡，因此灯光可穿过遮挡物；Light.Shadows 暂不参与计算。没有高光、反射、法线贴图、Cookie、IES、烘焙光照或区域光。GI 仍受单层屏幕深度和离屏信息缺失限制。GPU 资源由光照组件管理，信息分辨率变化时重建，禁用时释放；常规帧不执行 CPU 回读。

报告 `Library/PixelToonLighting-validation.txt` 覆盖 Shader 编译、无光时等于自发光、方向光/点光贡献、聚光锥方向、剔除遮罩、直接光注入 GI、无历史反馈、有限 HDR 值及重建/释放。预览在 `Preview/Lighting`。
