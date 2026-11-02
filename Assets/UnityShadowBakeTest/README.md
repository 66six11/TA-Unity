# Unity 原生 Shadowmask 烘焙测试

打开 UnityShadowBakeTest.unity。场景包含地面、墙面、方块、球体、圆柱和悬空横梁。
原生烘焙基线使用 Universal Render Pipeline/Lit。当前保存场景已接入自定义混合阴影接收材质与艺术颜色响应，仍使用 Unity 原生阴影和烘焙数据。

算法、公式和讲解图见 [统一风格化阴影技术文档](../../Docs/ShadowChromaticDemo/TechnicalGuide.md)。

## 烘焙配置

- Unity Progressive Lightmapper（CPU）。
- Baked Global Illumination 开启，Realtime GI 关闭。
- 主方向光：Mixed，Lighting Mode：Shadowmask。
- 静态几何：Contribute GI，Receive GI：Lightmaps。
- Lightmap Resolution：12 texels/unit，最大图集 1024。
- Direct / Indirect / Environment Samples：各 64，2 次反弹。
- 测试档关闭降噪/过滤，因此阴影边缘可能有采样噪声；最终质量需提高采样和分辨率并启用过滤。

Unity 的 Shadowmask 存储静态光源遮挡值，配合 lightmap UV 使用；它不是光源视角的深度 Shadow Map。
URP 的实时 Shadow Map 仍由 Unity 自动生成。此场景验证的是 Unity 原生烘焙阴影能否被标准 Lit 正确接收。

## 已完成的验证

1. 原生烘焙输出两组 Lightmap / Shadowmask 和 LightingData.asset。
2. 通过 bakeCompleted 回调确认完成，再渲染测试。
3. 临时设置 Shadowmask 质量模式，并关闭所有 MeshRenderer 的实时投影。
4. 渲染实际烘焙遮罩，再保留相同 Lightmap、只把 Shadowmask 替换成全白并再次渲染。
5. 检查测试几何确实可见，并确认两张图片存在显著差异。
6. 恢复原遮罩、投影设置和质量选项，保存场景。

Preview/UnityBakedShadowmask.png：关闭实时几何投影后的烘焙阴影。
Preview/WhiteShadowmaskControl.png：同样条件下替换为全白遮罩的对照。
Validation.txt：实际检查结果和生成资源路径。

## 重复操作

Tools > TA > Unity Shadow Bake > Verify Baked Shadowmask：检查当前测试场景。
Tools > TA > Unity Shadow Bake > Build Scene and Bake：重建本测试场景并重新烘焙；使用前保存当前场景的修改。

日常打开场景时，Unity 会按当前质量设置组合实时和烘焙阴影；验证过程中的临时关闭操作不会保存到场景。

## 混合阴影接收材质更新

当前场景已在相同烘焙资源上接入自定义接收材质，对照与接入说明见 Comparison/README.md。
进入 Play Mode 可用左上角切换原生 URP Lit 和自定义接收材质，并查看 GI/SH、最终阴影可见度及 Shadowmask。
原生烘焙测试报告在 Validation.txt；接收材质对照与数据读取验证在 Comparison/Validation.txt。
