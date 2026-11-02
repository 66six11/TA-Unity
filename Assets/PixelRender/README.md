# 3 渲 2 像素场景

小屋现已接入延迟卡通光照与 GI 合成，支持方向光、点光、聚光；详见 [Lighting.md](Lighting.md)。

独立 SSGI 实验：打开 `PixelGIDemo.unity`；只显示自发光输入产生的一次反弹 GI，不执行直接光照计算。详见 [GI.md](GI.md)。

新增论文启发的静帧对照实验：见 `Experiments/README.md`，结果在 `Experiments/Output/index.html`。比较相同尺寸、16 色预算下的最近邻、面积缩小与联合优化近似。

打开 `PixelRenderDemo.unity`，在 Game 视图查看（编辑状态和 Play 均可）。
菜单：`Tools > TA > Pixel Render > Open Demo`；`Build Demo` 重建场景及预览，会覆盖本目录的生成资源。

当前 Game 管线：场景 → 指定尺寸的表面信息 MRT → 延迟卡通直接光 → SSGI → 合成与四方向连接式描边 → 整数倍放大。默认信息尺寸为 640×360，最终像素尺寸为 320×180。窗口小于原始分辨率时缩小适配。固定 16:9，关闭 MSAA。

选中 Pixel Camera，在 PixelCamera 组件中设置 `informationHeight`（信息缓冲高度）、`verticalResolution`（最终像素高度）和 `view`（调试通道）。Outlined 为描边合成；Albedo、WorldNormal、LinearDepth、Coverage 查看原始表面信息；OutlineMask 查看二值线条；Connectivity 查看连通比例（红）和参与描边的邻居比例（绿）。区域优化尚未接入。Scene 视图及旧静帧实验仍使用原 Toon 光照，不能拿它们当作当前信息缓冲。

## Sloane 连接式描边

参考仓库已更名为 [SloanePixelartURP](https://github.com/SL0ANE/SloanePixelartURP)。本次适配的是 `Connectivity.hlsl`、`ConnectivityResult.compute` 和 `OutlinePass.hlsl` 中由描边使用的四方向连接/近远判定与颜色合成。原作者 MIT 许可见本目录 `LICENSE.Sloane.txt`。上一版直接深度边缘与法线差描边已移除。

1. 每个最终像素向右、左、上、下连接，在相邻输出像素之间沿高分辨率信息图逐步检查。
2. 每一步根据两个端点的法线建立切平面、预测深度变化；只有两个端点的预测误差都超过阈值才认为不连通。几何/背景交界不连通，背景内部连通。
3. 保存连接状态与中心是否较近。只有不连通且中心较远时，才在中心像素画线；因此轮廓落在远侧/背景。
4. 线色取相邻近侧表面的基本色乘 `outlineTint`。多个方向同时贡献时平均线色；也可启用 `solidOutlineColor` 使用纯色。

参数：`outlineEnabled` 为合成开关，`connectivityThreshold` 为端点深度预测容差（世界单位，默认 0.05，越大越容易连通、线越少）。这不是法线夹角阈值。原始信息纹理与描边采样均显式对齐信息 texel 中心，避免偶数倍率下点采样落在 texel 边界。

`Connectivity` 为最终像素尺寸的 RGBAHalf 纹理，R/G/B/A 分别代表右/左/上/下，每方向为整数 `connected + 2 * closerOrEqual`，值域 0..3。后续可直接消费这张连接图，无需重新识别颜色边缘。

与上游的区别：使用现有正线性深度和覆盖遮罩、通用逆投影；逐段推进时使用当前两个端点的深度，修正上游循环中未推进 previous/端点深度的行为；法线 z 分母设下限避免掠射面除零；边界访问有保护。步数按信息/输出尺寸比自动选择（最多 64），支持非整数倍率。未移植泛洪、形状优先级、PaletteProperty 的逐对象描边开关、调色板系统或整套渲染器；它们不是当前 OutlinePass 所消费的四方向边界位的必要输入。这不是完整仓库复现。

当前仍为硬边，不含 AA、像素路径追踪或时序约束。小于输出像素的细节仍可能丢失；相邻轮廓、转角不保证严格一像素。启用卡通光照后描边取着色后的近侧颜色；关闭光照时取无光照基本色。

运行 `Validate Surface Buffers` 导出 `Preview/Buffers/Outlined.png`、`OutlineMask.png`、`Connectivity.png`。检查连接整数位、倾斜平面连通、前景/背景交界的近远标志与仅远侧出线、关闭描边一致性、空场景和纹理释放。

## 信息缓冲约定

| 属性 | 格式 | 数据 |
|---|---|---|
| Albedo | RGBAHalf，线性 | RGB 为未受光的材质 `_BaseColor`；A 为有效像素遮罩 |
| WorldNormal | RGBAHalf，线性 | RGB 为归一化的世界法线，保留负值；A 为遮罩 |
| LinearDepth | RGBAFloat，线性 | R 为正的视空间轴向深度（世界单位，非径向距离、非硬件深度）；G 为对象 Layer（0–31）；A 为遮罩 |
| Radiance | RGBAHalf，线性 HDR | RGB 为材质 `_EmissionColor` 提供的出射辐射颜色；A 为遮罩 |

背景四个缓冲均清零。法线调试映射为 `N*0.5+0.5`；深度调试按相机 near/far 映射灰度。下游应先检查覆盖遮罩，再解释法线与深度。深度重建位置还需要相机矩阵和投影约定。

`PixelSurface` Pass 一次几何绘制同时写入四张纹理，共享硬件深度测试。当前采集支持本示例 Toon 材质及普通 MeshRenderer，不采集纹理、透明、裁剪或自定义顶点变形；缺少该 Pass 的材质会跳过，`UnsupportedMaterials` 计数可供诊断。按组件的 `captureLayers`（默认第 29 层）和所属场景筛选对象；相机自身 Culling Mask 在启用期间为 Nothing，避免前向着色重复绘制。后续接区域处理时应保留表面边界，不能直接跨物体平均属性。

这是独立示例采集路径：相机只驱动帧时序，在相机完成后执行自有 CommandBuffer；不切换项目 URP 渲染路径。每帧枚举本场景的 Renderer，后续规模化时应缓存和更新绘制列表。常规帧不进行 CPU 回读。

## 验证与导出

菜单 `Tools > TA > Pixel Render > Validate Surface Buffers` 在独立预览场景验证 Shader、法线长度、覆盖一致性、深度范围、光源变化不影响基本色，以及尺寸变化、释放和重新启用。报告为 `Library/PixelBuffers-validation.txt`。`Preview/Buffers` 输出四张可视化 PNG 和三张线性浮点 EXR；PNG 仅作调试，后续计算读取运行时缓冲或 EXR。

RenderTexture 与 CommandBuffer 由 PixelCamera 创建和释放，禁用时恢复相机原目标和剔除层。生成场景使用第 29 层隔离几何。`Preview/PixelRender.png` 是旧 Toon 光照预览；当前信息预览以 `Preview/Buffers` 为准。

