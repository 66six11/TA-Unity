# 屏幕空间 GI 实验：仅计算间接漫反射

小屋已进一步接入[卡通光照](Lighting.md)：启用 `PixelToonLighting` 时，GI 自动读取“直接光＋自发光”；关闭它才回到本页描述的自发光实验。下面关于 Light 不参与的说明适用于独立 GI 场景或关闭卡通光照的模式。`PixelToonLighting.View` 控制启用光照时的最终展示。

## 打开与运行

打开 `PixelGIDemo.unity`，或使用 `Tools > TA > Pixel Render > GI > Open GI Demo`。`Build and Verify GI Demo` 重建这个独立示例并输出验证报告，不重建原小屋场景。

此场景没有 Light 组件。红墙与蓝面板通过材质 `_EmissionColor` 提供线性 HDR 辐射输入，其他表面只用基本色接收一次反弹光。Game 视图默认仅显示 GI，**不叠加自发光源本身、直接光、环境光、SH 或描边**。发光面在 GI-only 中也可能是暗的，这不代表丢失了输入；切换 SourceRadiance 查看光源。

小屋示例 `PixelRenderDemo.unity` 已接入 GI。`GI Example Setup` 下有暖色、冷色庭院灯，各自包含自发光几何与可布置的 Point Light 组件。Game 只显示 GI；Light 的位置、颜色、强度暂不参与计算，GI 输入来自灯具材质的 `_EmissionColor`。移动灯具根节点可同时移动几何和光源。切换 `SourceRadiance` 查看发光面，`Off` 返回原基本色/描边输出。

`Tools > TA > Pixel Render > GI > Integrate Into Cottage Demo` 可重复接入，不重建小屋或修改相机参数。`Library/PixelGI-cottage-validation.txt` 验证所有 Light 关闭后 GI 像素保持一致。其他场景在 Pixel Camera 上添加 `PixelScreenSpaceGI` 即可使用；没有自发光或外部辐射输入时，GI 应为黑色。

## 管线与输入输出

```text
640×360 基本色 / 世界法线 / 正线性深度 / 自发光辐射
                         ↓
              160×90 单次漫反射射线步进
                         ↓
                  3×3 空间双边降噪
                         ↓
            320×180 GI-only 展示 → Point 放大
```

- `PixelCamera.Radiance`：新增第四张 MRT，RGBAHalf，RGB 来自材质 `_EmissionColor`，不依赖灯光。捕获路径要求至少四个 MRT。
- `externalRadiance`：可选线性 HDR 出射辐射颜色纹理，替换自发光输入。尺寸、相机、投影、UV 朝向必须与信息缓冲一致；尺寸不匹配时警告并退回自发光。不要输入基本色、已描边、色调映射或 GI 合成后的图。
- `RawGI`：RGBAHalf，RGB 为单次反弹的间接漫反射结果，已经乘接收表面的基本色；A 为几何命中比例。
- `IndirectDiffuse`：RGBAHalf，空间降噪后的结果。将来与直接光合成时不要重复乘接收端基本色。
- GI 有独立分辨率。`giHeight` 默认 90（160×90），示例用 32 条射线、64 步；组件默认 16 条射线、48 步。

## 算法

从深度重建视空间接收点，沿法线偏移以避免自交，再在法线半球中生成固定的余弦加权射线。沿每条射线投影到屏幕深度，检查第一次深度交叉；通过厚度和朝向测试后读取命中表面的辐射颜色。无辐射的几何同样会截断射线，防止直接穿过遮挡物采光。离屏、未命中和背面贡献为零。

Lambert 漫反射的 `albedo / π` 与余弦加权采样 PDF 抵消，最终为 `receiverAlbedo × mean(hitRadiance)`。这条估计不再额外乘命中法线余弦或距离平方衰减；表面积与距离影响由射线命中概率体现。距离上限截断为 radius。

3×3 空间滤波按深度和法线差异加权，避免直接跨表面平均。采样在每个 GI 像素上确定性分布，没有逐帧随机抖动、历史纹理或反馈。设置变动直接重算，无旧帧能量累积。

## 参数和调试

- `GIOnly`：仅显示过滤后的 GI。
- `SourceRadiance`：检查辐射输入。
- `HitRatio`：查看几何命中比例，不是发光面命中比例。
- `RawGI`：查看未降噪结果。
- `Off`：保留原 PixelCamera 的输出。
- `radius`：最大追踪距离；`normalBias`：法线方向起点偏移。
- `thickness`：屏幕深度表面的近似厚度，过大容易漏光，过小容易漏掉命中。
- `rayCount`、`raySteps`：质量 / 耗时权衡，未给出帧率保证。
- `spatialDenoise`、`filterDepthSigma`：空间降噪开关和深度权重范围。

## 已验证与限制

`Library/PixelGI-validation.txt` 记录 Shader 编译、输出尺寸、有限非负结果及红蓝反弹、外部全黑输入归零、材质发光归零（基本色不变）后的 GI 归零、确定性重复、尺寸重建和禁用释放。`Preview/GI` 提供输入、GI-only、原始 GI、命中比例 PNG，以及线性浮点 `IndirectDiffuse.exr`；PNG 会裁剪 HDR，仅供观察。

这是单层深度的屏幕空间一次反弹近似：不能处理离屏表面、完整背面或多层遮挡，没有多次反弹、天空后备、深度金字塔、时间重投影或运动矢量。相机/物体移动时仍会噪声跳动。降噪和低分辨率也可能造成细节丢失和局部漏光。当前不接像素光照分档、有限色板或艺术 AA。
