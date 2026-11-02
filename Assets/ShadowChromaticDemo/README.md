# 暖色阳光 + 冷色天空光的阴影颜色过渡

本文描述早期解析光照案例。当前 Unity 原生混合阴影与统一艺术响应案例见 [Demo 技术文档](../../Docs/ShadowChromaticDemo/TechnicalGuide.md)，其中包含算法公式、自身阴影衔接图与 PCF 颜色曲线图。

打开 ChromaticShadowDemo.unity 并运行，或使用 Tools > TA > Chromatic Shadows > Open Demo。

当前版本移除了边缘颜色、RGB 偏移和非线性阴影颜色曲线。PCF 输出标量 V，颜色来自光照贡献的线性叠加。

## 模型

采用 Lambert 漫反射 BRDF = albedo / PI：

    Lo = albedo / PI * (E_environment + V * E_sun * max(N dot L, 0))

环境定义为上下两个半球，各自具有均匀辐亮度。在不考虑环境遮挡时，其解析辐照度为：

    E_environment = PI * (L_sky * (1+Ny)/2 + L_ground * (1-Ny)/2)

直射阳光为暖色，天空为冷色。全阴影区域只剩天空和地面环境的贡献；半影随 V 增加逐步混入阳光。
不应期待额外出现独立的饱和彩边。改变阳光/天空能量比会自然改变过渡颜色。
颜色在线性空间计算。普通 Color 控件按 sRGB 选色，天空参数显式转换为线性辐亮度。
强度为相对尺度，未做真实 lux 标定。

## 控制与查看

- Sun irradiance scale：主方向光强度。
- Sky radiance scale：天空/地面环境辐亮度的统一缩放。
- PCF filter radius：阴影贴图过滤半径，单位为世界尺度。
- Combined / Sun / Sky / PCF：分别查看总光照、直射项、环境项和标量可见度。
- Neutral lights：保持各光源线性亮度的中性颜色对照。
- 在示例相机的 ChromaticShadowControls 中可编辑太阳、天空和地面环境颜色。

## 准确性范围

光照合成遵循 Lambert 模型，但这是受控的近似示例，不是完整的物理天空或全局光照求解器。
天空环境不计算物体遮挡，地面环境是预设的下半球辐亮度，不是实时反弹光。
PCF 为固定世界尺度的 64 点过滤，不是真正面积光源积分或接触硬化 PCSS；远近半影宽度不由光源角直径自动计算。
主接收地面使用上述解析模型，标准 URP Lit 投影物体使用项目的环境照明路径，二者不保证数值完全一致。
完整生产实现应对接项目统一的天空探针/SH、反射与 GI，并为真实软阴影引入面积光可见度近似。
只覆盖主方向光、不透明接收表面；大核级联边界和复杂曲面自阴影需另行处理。
案例相机只显示第 30 层，以隔离同时加载的原场景。

## 输出

- Preview/WarmSunCoolSky.png：暖阳光 + 冷天空光。
- Preview/NeutralLights.png：同等亮度中性光照对照。
- Preview/ScalarPCF.png：标量 PCF 调试图，接收地面为灰度。

菜单 Build and Verify Demo 会重建本案例场景和预览图。
Library/ChromaticShadowDemo-validation.txt 记录编译、光照颜色对照，以及零阳光/零天空的分项验证。
