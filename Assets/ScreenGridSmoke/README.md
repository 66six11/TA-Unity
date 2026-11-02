# 屏幕网格墨水 / 烟雾流体

运行 `Assets/ScreenGridSmoke/ScreenGridSmokeDemo.unity`。菜单：**Tools > TA > Screen Grid Smoke > Open Demo**。

原模型保留；其屏幕轮廓持续释放蓝色染料，二维速度场带动染料卷动、扩散、上升。此例没有 GPU 粒子，也没有用粒子深度重建水面。

## 和之前的 SSF 水面示例有什么不同

这里**模拟状态本身保存在屏幕纹理里**：速度、压力、染料浓度都有跨帧历史。模型网格只用于生成投影遮罩。

移动/旋转游戏摄像机、修改投影或改变网格尺寸/视口宽高比时，默认清空历史。界面的 **Rotate camera +15 degrees** 可以直接观察重置和烟雾重新长出的过程。Scene 窗口里的编辑器摄像机不是游戏摄像机，单独转动 Scene 视图不会改变这个模拟。

关闭 `Reset On Camera Change` 可以观察不重投影的后果：旧烟雾会留在原来的屏幕位置，无法保持世界空间连续性。

## 求解步骤

每次相机渲染，URP RenderGraph 依次执行：

1. **投影模型**：绘制 MeshFilter 网格，生成低分辨率二值轮廓遮罩；投影内部作为固体障碍。
2. **速度平流**：半拉格朗日回溯 `uv - velocity * dt / gridSize`，双线性采样旧速度。
3. **施加外力**：轮廓外侧窄带的向外作用、风力、基于染料密度的浮力，以及模型两侧的高斯局部旋转力。
4. **涡量约束**：计算二维旋度 `ω = ∂v/∂x - ∂u/∂y`，根据 `grad(abs(ω))` 施加旋转补偿，恢复半拉格朗日平流中丢失的细节。
5. **压力投影**：计算散度，使用默认 50 次 Jacobi 迭代近似求解 `Laplacian(p) = divergence(u)`，再减去压力梯度。
6. **染料平流**：使用投影后的速度搬运上一帧 RGB 染料和浓度，再从模型轮廓外侧注入新染料，并做指数衰减。
7. **合成**：用浓度计算 `opacity = 1-exp(-density*exposure)`，将染料颜色合成到原场景，密度梯度提供艺术化的明暗细节。

散度使用前向差分，压力梯度使用匹配的后向差分，从而与 Jacobi 的最近邻五点 Laplacian 一致，避免中心差分组合形成跨两格的投影误差。障碍与屏幕边界的法向速度设零，压力采用相邻固体处的 Neumann 近似边界。

这是二维不可压缩流的视觉近似。有限次压力迭代会保留残余散度；并非精确的零散度求解。本例没有显式黏性扩散，平流自身有数值耗散，另有可控速度阻力和染料衰减。速度使用共享二维纹理采样，并未实现高阶 MAC 平流或物理量纲标定。

## 控制

- **Vorticity confinement**：细小卷曲强度。默认 4，过强会产生颗粒状或格子尺度的扰动。
- **Vortex force**：两侧较大旋涡的外力强度，默认 1.5；与涡量约束相互独立。
- **Emission**：轮廓处染料注入速率。
- **Pause / Reset**：暂停保留纹理，重置清空状态。
- **Rotate camera**：转动游戏摄像机，触发历史重置。
- 调试模式：Smoke、Density、Velocity、Pressure、Vorticity、Source mask。

Inspector 里还可以调节网格高度、压力迭代次数、浮力、风、速度阻力、染料衰减和颜色。速度单位是网格格数/秒；外力参数按网格高度转换，减轻分辨率变化对运动尺度的影响。时间步限制在不超过 1/30 秒，低帧率时会减慢模拟。

## 接入自己的模型

1. 在游戏摄像机上添加 `ScreenGridSmokeView`，将目标物体的 **MeshFilter** 拖入 `Source`。保留原 MeshRenderer。
2. 摄像机选择 `Generated/SmokeRenderer.asset` 对应的 Renderer，关闭 MSAA。
3. 该 Renderer 上的 `ScreenGridSmokeFeature` 引用 `Shaders/ScreenGridSmoke.compute` 和 `Shaders/ScreenGridSmoke.shader`。

模型不需要开启 Read/Write，因为此例直接绘制网格生成投影遮罩，不在 CPU 读取顶点。不处理 SkinnedMeshRenderer/BlendShape，默认单相机、单个静态网格发射源；支持网格物体整体变换，快速移动时可能留下屏幕历史伪影。

生成菜单会复制一个独立的 URP Renderer 并追加到当前管线 Renderer List，保留原默认 Renderer。只有带 `ScreenGridSmokeView` 的相机才执行模拟。切换到另一 Quality 的管线后，需要在该 Quality 下重新生成/注册 Renderer。

## 状态、资源和开销

- 速度：两张 RG32F。
- 染料：两张 RGBA16F，RGB 为染料累积量，A 为浓度。
- 压力：两张 R32F。
- 旋度、投影前散度、投影后散度：三张 R32F。

九张持久纹理由相机组件拥有；禁用、销毁或尺寸变化时释放。RenderGraph 导入这些纹理以声明计算和合成的依赖，不接管其生命周期。投影遮罩及最终合成目标由 RenderGraph 管理。

默认网格约为 `464×288`（1280×800 视口），持久纹理约 **6.63 MiB**，不含相机颜色、深度和临时遮罩。每步包含默认 50 次压力迭代；这通常比单纯的粒子运动多出许多 Compute Dispatch。实际性能需要在目标设备测量。

正常运行没有 GPU→CPU 状态读回。编辑器验证允许显式读回染料和散度；`validationDeltaTime` 只用于离线验证，保存的场景中为 0。

## 适用范围与限制

适用于本项目 Unity 6000.5.7f1、URP 17.5、RenderGraph、普通桌面 DX12 透视摄像机。未适配 XR、相机堆叠、动态分辨率或正交摄像机深度合成。

它是二维屏幕特效，不是三维体积烟雾。不能从另一个视角看到同一团烟的真实侧面，也不包含体积光、真实阴影和三维碰撞。源模型投影是二维障碍；其他不透明物体仅通过一张位于发射源中心深度附近的平面进行近似合成遮挡，不参与速度/压力边界求解。后续透明物体排序仍是近似。

## 验证与预览

**Build and Verify Demo** 实际运行 180 个 GPU 求解步，保存 30/90/180 步及调试模式图片，并验证：

- 空网格完整保留场景和模型。
- 染料浓度非负、有限且总量非零。
- 压力投影显著降低投影前后的散度 RMS。
- 暂停保持染料历史；转动摄像机清空速度/压力/染料。
- 改变视口宽高比触发纹理重建与清空。
- 禁用组件释放持久纹理，原模型 Renderer 保持开启。

结果保存在 `Library/ScreenGridSmoke-validation.txt`，图像在 `Preview/`。这些是正确性检查，不是性能基准。

算法参考：[NVIDIA GPU Gems，Fast Fluid Dynamics Simulation on the GPU](https://developer.nvidia.com/gpugems/gpugems/part-vi-beyond-triangles/chapter-38-fast-fluid-dynamics-simulation-gpu)。
