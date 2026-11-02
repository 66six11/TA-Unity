# TA Unity 技术美术实验

记录完整 Unity 项目的源码、场景、资源、项目配置和实验文档。

## 环境

- Unity `6000.5.7f1`
- Universal Render Pipeline `17.5.0`
- 依赖版本见 `Packages/manifest.json` 和 `Packages/packages-lock.json`。

通过 Unity Hub 添加仓库根目录并使用对应版本打开，等待 Package Manager 恢复依赖和资源导入。Unity 缓存、日志、本地 Python 环境和 IDE 生成文件不入库。

## 实验目录

- [像素渲染](Assets/PixelRender/README.md)：表面信息缓冲、连接式描边、延迟卡通光照、屏幕空间单次反弹 GI、连通性艺术 AA。
- [像素光照与 AA](Assets/PixelRender/Lighting.md)
- [屏幕空间 GI](Assets/PixelRender/GI.md)
- `Assets/ScreenGridSmoke`：屏幕网格烟雾实验。
- `Assets/ScreenSpaceFluid`：屏幕空间流体实验。
- `Assets/ShadowChromaticDemo`：阴影颜色实验。
- `Assets/SdfOutline`：SDF 描边实验。
- `Assets/SurfaceDustGpu`：GPU 表面尘埃实验。
- `Assets/UnityShadowBakeTest`：阴影烘焙与体积烟雾实验。
- `Docs`：项目文档与原理图。

像素渲染主示例为 `Assets/PixelRender/PixelRenderDemo.unity`，独立 GI 示例为 `Assets/PixelRender/PixelGIDemo.unity`。编辑器验证入口位于 `Tools > TA`。

当前已实现连通性 AA；GI 的水彩、油画色块处理仍处于方案讨论阶段。

## 版本记录

使用 Git 提交记录后续实验变化。首次提交保存当前磁盘快照；Unity 中未保存的编辑应先保存到场景或资源再提交。

第三方代码及资源沿用各自许可。像素描边参考项目的许可见 [LICENSE.Sloane.txt](Assets/PixelRender/LICENSE.Sloane.txt)。
