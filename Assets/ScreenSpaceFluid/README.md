# 屏幕空间流体示例（SSF）

完整原理、讲解图和代码入口见 [技术文档](../../Docs/ScreenSpaceFluid/TechnicalGuide.md)。

打开 `Assets/ScreenSpaceFluid/ScreenSpaceFluidDemo.unity` 后运行。菜单为 **Tools > TA > Screen Space Fluid > Open Demo**。

本例保留金属圆环，从模型表面不断发射 GPU 粒子，把粒子渲染成连续、透明的液体表面。背景条纹和棋盘格用于观察折射。左上角可以切换 Liquid、Raw depth、Smooth depth、Normals、Thickness，调节平滑半径、折射强度、流场强度，以及暂停/重置。

## “屏幕空间”指哪一步

粒子位置和速度仍在三维世界空间中由 Compute Shader 更新。**水面重建、深度平滑、法线计算和液体着色在屏幕空间完成**，不生成 Marching Cubes 网格。

这实现了前面讨论的 Screen Space Fluid Rendering。它不包含 SPH/PBF 压力、体积约束、黏性或表面张力求解，也不是在二维屏幕网格上求解速度/压力的 Stable Fluids。现有解析流场提供视觉运动，SSF 提供液体外观；两者是独立层。

## 渲染链路

```text
模型三角形 → GPU 按面积发射 → 世界空间流场更新
                                   ↓
                           同一个粒子缓冲区
                            ↙            ↘
                 球形粒子前表面深度      球体弦长累加
                       ↓                  ↓
                  双边深度平滑           厚度图
                       ↓                  ↓
                 位置与法线重建 ───────→ 液体着色
                                            ↑
                              不透明场景颜色与深度
```

1. **球面深度**：每粒子绘制 6 顶点四边形，用圆内坐标计算球面深度 `-centerVS.z - radius * sqrt(1-r²)`。R32F 目标以远处哨兵值清空，Min 混合保留最近的液体表面。
2. **厚度**：加法累积粒子的球体弦长 `2 * radius * sqrt(1-r²)`，并用不透明场景深度裁剪弦长。R16F 厚度是重叠粒子的艺术近似，不是精确体积并集厚度。
3. **平滑**：默认四组水平/垂直双边滤波，每方向 9 个采样。空间权重和观察空间深度差权重相乘，保留深度断层；无效像素不向外扩展。分离滤波是二维双边滤波的近似。
4. **法线**：用平滑深度和逆投影矩阵恢复观察空间位置，以相邻像素差分计算法线。轮廓处选择较连续的一侧，孤立像素使用朝向相机的回退法线。
5. **着色**：法线偏移背景采样形成近似折射；厚度通过 `exp(-absorption * thickness)` 控制吸收；Schlick Fresnel 混合程序化天空反射。反射天空是示例预设，不是场景反射探针或 SSR。
6. **遮挡**：最终比较平滑流体深度与场景深度，原模型和其他不透明物体会遮挡后方流体。折射采样落到流体前方物体时退回原 UV，减少前景拉伸。

所有流体贴图均由 URP RenderGraph 管理，随当前摄像机目标尺寸创建，不保存跨帧纹理，不每帧读回 CPU。

## Unity 接入

本例针对 **Unity 6000.5.7f1 / URP 17.5 / RenderGraph / 桌面 DX12**。相机关闭 MSAA。

- `ScreenSpaceFluidFeature.cs`：注册 sphere depth、thickness、bilateral、composite 渲染 Pass。
- `ScreenSpaceFluidView.cs`：摄像机上的流体源引用和调试/外观参数。
- `Shaders/ScreenSpaceFluid.shader`：四个 Shader Pass；法线在最终合成中计算。
- `Editor/ScreenSpaceFluidDemoBuilder.cs`：生成场景、安装独立 Renderer、编译和渲染验证。
- 粒子模拟复用 `Assets/SurfaceDustGpu/` 的 Compute Shader 和 Emitter；`drawBillboards=false`，不走尘埃 Billboard 的绘制路径。

生成菜单会复制一个独立的 `Generated/FluidRenderer.asset`，添加流体 Feature，并将它追加到当前 URP Asset 的 Renderer List。**原来的默认 Renderer 索引和内容保持原样**，示例相机显式选择新 Renderer。

当前 PC 管线已注册该 Renderer。如果切换到另一 Quality 的 URP Asset，需要在该 Quality 下重新执行生成菜单。普通场景相机没有 `ScreenSpaceFluidView` 时，不会执行流体 Pass。

换模型时，把 `GPU fluid particles` 上的 `Source` 换成目标 MeshFilter，保留原 Renderer，并启用导入模型的 Read/Write。支持整体移动/旋转；骨骼、BlendShape 变形不在本例范围。使用自定义相机时，选择 FluidRenderer，并添加 `ScreenSpaceFluidView` 引用 Emitter。每个视图目前渲染一个 Emitter。

## 参数与开销

- `particleSize` 在此作为球半径，默认 0.045–0.075 世界单位，粒子槽位 12288。
- `filterRadius` 为每方向的屏幕像素采样范围，默认 6；`depthSigma` 为深度相似性尺度，默认 0.2 世界单位。
- `smoothingIterations` 默认 4，即 8 次全屏平滑 Pass。增加它主要增加纹理带宽和采样开销。
- `thicknessScale` 修正粒子重叠造成的厚度高估；`absorption` 按 RGB 通道控制吸收；`refraction` 是 UV 偏移强度，不是严格的折射率。
- 本例使用全分辨率流体贴图，以便观察算法。生产版本可考虑降分辨率后深度感知上采样、限制流体屏幕包围区域和自适应滤波。
- 背景场景、粒子重叠和全屏滤波共同决定性能；通过 GPU Profiler 测量，不能只按粒子数推断帧率。

## 限制

只有当前视角最近的一层流体表面，没有完整的内部几何、多次折射或真实焦散；无法从画面外获取折射信息。稀疏粒子的轮廓仍然会显示水滴或孔洞，平滑不能替代合理的粒子密度和流体运动。

示例在不透明物体之后、其他透明物体之前合成，**不把流体深度写回摄像机深度附件**，因此与后续透明物体的前后关系是有限近似。未实现其他透明物体的正确折射、XR、相机堆叠与动态分辨率适配；当前仅针对普通完整视口的透视/正交摄像机。常规摄像机缩放分辨率变化已做渲染检查。

现有 GPU 运动没有实体碰撞，粒子可能穿过模型或地面；场景深度只负责渲染遮挡。需要真实碰撞或保持液体体积时，要在模拟层另加 SDF 碰撞或流体约束。

## 验证

**Build and Verify Demo** 编译所有 Shader Pass，并实际通过 URP 渲染验证：

- 没有活粒子时，合成保持不透明场景颜色。
- 液体存在时产生可见变化，并保留流体轮廓外的背景。
- 原始深度与平滑深度存在变化；导出法线和厚度调试图。
- 1280×800、960×540 两种尺寸可以渲染。
- 原始模型 Renderer 保持开启，尘埃 Billboard 绘制关闭。

结果见 `Library/ScreenSpaceFluid-validation.txt`；图片见 `Preview/`。验证是正确性检查，不是性能基准。

参考：[NVIDIA Screen Space Fluid Rendering](https://developer.download.nvidia.com/presentations/2010/gdc/Direct3D_Effects.pdf)、[Unity RenderGraph Blit](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-blit.html)。
