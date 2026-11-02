# 模型表面尘埃：Compute Shader + 自定义渲染

适用于本项目 Unity 6000.5.7f1 / URP 17.5，桌面端需要 Compute Shader 与 StructuredBuffer 支持。

## 运行

打开 `Assets/SurfaceDustGpu/SurfaceDustDemo.unity`，点击 Play。
也可以通过 **Tools > TA > Surface Dust GPU > Open Demo** 打开。
若场景尚未生成，执行 **Build and Verify Demo**；菜单会生成独立场景、材质、圆环模型和预览，并在独立的附加场景中验证，不替换当前场景。

原模型始终保留。约 3.3 万个粒子槽位在 GPU 中持续从圆环表面发射，受向外初速度、上升气流、解析旋涡场和阻力影响；粒子渐入、扩散、渐隐后重生。前 7 秒逐渐填满粒子池。运行时左上角可以调节发散速度、扰动、上升气流，暂停及重置。

## 换成自己的模型

1. 在独立 GameObject 上添加 `SurfaceDustEmitter`。
2. 把目标模型的 **MeshFilter** 拖到 `Source`，保留模型原有 MeshRenderer。
3. `Simulation` 指向 `Shaders/SurfaceDust.compute`。
4. `Dust Material` 指向 `Generated/Dust.mat`，或使用 `TA/Surface Dust GPU` Shader 创建材质。
5. 导入模型必须启用 **Read/Write**。源模型支持多个三角形子网格；忽略线和点拓扑，跳过退化三角形。
6. 根据模型世界尺寸调整 `Particle Size`、`Surface Offset`、`Outward Speed` 和 `Max Travel Distance`。

支持模型整体平移和旋转，新出生的粒子使用当前模型变换，已发射粒子保持世界空间运动。面积分布按初始化时的世界缩放构建；运行中改变非均匀缩放、网格或粒子槽位数量后，调用 `Initialize()` / 组件菜单 **Rebuild GPU particles**。法线使用逆转置矩阵变换。示例不处理骨骼或 BlendShape 变形。

## 数据与执行流程

```text
CPU 初始化一次：
    读 Mesh → 各三角形世界面积 → 累计概率 CDF → 上传三角形缓冲区
GPU Initialize：
    每个粒子独立随机状态 → 二分查找 CDF → 重心坐标采样 → 初始延迟
每帧 GPU Simulate：
    延迟结束/寿命结束 → 在当前模型表面重生
    否则 → 风力 + 解析 curl 流场 + 指数阻力 → 更新速度、位置
每帧自定义绘制：
    Graphics.RenderPrimitives → 顶点着色器读取同一粒子缓冲区
    → 每粒子 6 顶点组成朝相机的四边形 → 柔边尘埃片元
```

- `SurfaceDustEmitter.cs`：缓冲区创建/释放、网格 CDF、参数提交、Dispatch 和绘制。
- `Shaders/SurfaceDust.compute`：按面积在模型表面采样、粒子初始化/重生与运动。
- `Shaders/SurfaceDustParticle.hlsl`：共用的 48 字节粒子布局。
- `Shaders/SurfaceDust.shader`：随速度适度拉伸的 Billboard，寿命渐隐和预乘透明混合。
- `Editor/SurfaceDustDemoBuilder.cs`：生成场景与实际 GPU 验证。

运动更新中 CPU 不遍历粒子，不每帧上传粒子，也不每帧读回粒子。CPU 仍负责参数提交、Compute Dispatch 和 Draw 调度。
默认 32768 个粒子的状态缓冲约 **1.5 MiB**；额外网格缓冲为每个非退化三角形 96 字节。

固定粒子池在过期后立即重生，稳定发射速率近似为 `粒子数量 / 平均寿命`，不是独立的精确每秒发射计数器。
风参数在本例中是加速度；`Drag` 为速度衰减系数。流场为解析向量势的旋度，属于视觉运动，不是 SPH/PBF 流体求解器。

## 性能和边界

- 先用 8192/16384 个粒子调外观，再提高数量。透明像素重叠通常比粒子状态存储更值得关注。
- 绘制使用一个程序化提交，无需为每个粒子创建 GameObject。固定池中尚未出生的粒子仍有顶点开销。
- 使用 ZTest 遮挡原模型背后的尘埃，不写深度，不投影；当前材质为艺术化 Unlit 尘埃，不计算逐粒子光照。
- 透明粒子没有逐粒子排序，因此密集叠加存在近似；没有场景深度软交界、地面碰撞或粒子间碰撞。
- 粒子有最大累计行程，CPU 将源模型的历史包围盒扩张后用于裁剪，避免 GPU 回读。模型长距离移动会让包围盒保守增大，可重建粒子池清理历史。
- 大帧间隔最多补算 0.1 秒，并分为不超过 1/60 秒的子步，避免长时间失焦后一次追赶过多工作。
- 本例针对普通桌面摄像机；未实现 XR 单通道立体渲染适配。

## 验证

菜单 **Build and Verify Demo** 会验证：

- 面积 1:4 的两个三角形被采样的比例接近 20%:80%。
- 非 128 整倍数的粒子数量也正确初始化。
- 出生粒子位于圆环的偏移表面，GPU 运动、重生、寿命和行程范围有效。
- 尘埃开关的实际 URP 输出存在可见像素差异，原模型 Renderer 保持开启。
- 禁用组件释放缓冲区，再次初始化成功。

验证时允许显式 GPU 读回，正常运行路径不调用读回。结果写入 `Library/SurfaceDustGpu-validation.txt`，预览保存到 `Preview/`。
这些是正确性检查，不代表 GPU 性能基准；请使用目标设备的 Unity Profiler / GPU Profiler 评估预算。

参考：[Unity Graphics.RenderPrimitives](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderPrimitives.html)。
