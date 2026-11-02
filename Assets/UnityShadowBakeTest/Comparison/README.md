# Unity 统一阴影颜色响应

完整算法、公式、讲解图和验证说明见 [Demo 技术文档](../../../Docs/ShadowChromaticDemo/TechnicalGuide.md)。文档与算法配图保存在项目级 Docs 目录。

场景：Assets/UnityShadowBakeTest/UnityShadowBakeTest.unity。
在 Main Camera 的 UnityMixedShadowComparison 中调整参数，或进入 Play Mode 使用左上角面板。

## 本次修正

艺术颜色直接参与主光源的明暗响应，不再通过边缘遮罩给最终画面叠色。
- 投射/自遮挡阴影：读取原生 PCF 可见度及烘焙 Shadowmask，在阴影强度和距离混合之前转换成 RGB 透光响应。
- 曲面自身明暗交界：对正向 N·L 的实际衰减使用同一条 RGB 曲线，不向背光面扩展光照。
- 两者相乘后替换主光源原有的 N·L × shadowAttenuation，继续使用 URP 原生 BRDF。
- GI、天空 SH、额外光源和自发光不经过这条颜色曲线。艺术处理本身是有意偏离物理的直接光响应。

曲线是三次 Bezier，固定 F(0)=0、F(1)=1，RGB 控制点有序，各通道单调。因此完全遮挡与完全可见端点保持原值，过渡内部连续改变颜色，不再添加独立色圈。
Surface falloff range 只控制自身明暗衰减的 N·L 范围；投影过渡宽度仍由原生 PCF/烘焙遮罩决定。
自身明暗交界另有两端衔接：归一化 N·L 的 0～0.35 范围用 smoothstep 缓慢引入直接光，让阴影底色逐渐进入色带；0.65～1 范围平滑接回原生余弦衰减，匹配亮端数值与斜率。暗端缓入随艺术强度增加，满强度时在零点斜率为零；不会给背光面额外补光。
Shadow/Light Edge Color 字段出于场景序列化兼容保留名称，含义已改为暗侧/亮侧响应控制色，不是最终叠加色。
Color strength 为 0，或关闭艺术处理，直接走原生 LitPassFragment。

## 代码

- Shaders/UnityMixedShadowReceiver.shader：原生 Lit 材质接口、初始化和诊断视图。
- Shaders/UnityArtisticLighting.hlsl：颜色曲线与主光源替换；PBR 入口基于本项目 URP 17.5 源码，升级 URP 时需要同步核对。
- UnityMixedShadowComparison.cs：统一参数、原生/自定义切换及运行时面板。
- Editor/MixedShadowReceiverTest.cs：离屏渲染对照和状态恢复。

复用原生 ShadowCaster、DepthOnly、DepthNormals、Meta、MotionVectors。已有 Lightmap 和 Shadowmask 不需重烘焙。
六个静态物体使用 Lightmap，额外小球使用环境 SH；场景尚无局部 Probe Group。
当前方案额外采样一次主光源阴影以取得混合前的覆盖率，尚未做 GPU 性能测量。

## 验证与图片

运行 Tools > TA > Mixed Shadow Receiver > Verify Current Scene。详见 Validation.txt。
包含原生像素对照、移除 Shadowmask/GI 的作用验证、烘焙/实时投影与自身明暗响应、直接光归零时 GI 不染色，以及强度归零恢复原生结果。

- ArtisticMixedShadow.png：统一艺术响应。
- MixedReceiver.png / NativeLit.png：关闭艺术响应的基线。
- ArtisticBakedShadow.png：关闭实时投影，只用烘焙遮罩。
- ArtisticSelfShadow.png：投影遮罩全白，只观察自身明暗衰减。
- ArtTransitionMask.png / SelfShadowTransitionMask.png：响应发生变化的诊断区域，未用于叠色。

测试平台是 Unity 6000.5.7f1、URP 17.5、Windows DX12、不透明材质。原生烘焙仍是低采样测试档。
