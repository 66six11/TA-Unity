# 静帧像素抽象实验

研究来源：[Pixelated Image Abstraction (Gerstner et al., NPAR 2012)](https://cragl.cs.gmu.edu/pixelate/)。这是受其思想启发的工程近似，不是论文完整复现。

## 运行

1. Unity 菜单 `Tools > TA > Pixel Render > Export Abstraction Source` 导出已保存的 `PixelRenderDemo.unity`。在独立预览场景中渲染，不保存或重建当前编辑场景。输出 `Output/Source.png`，1280×720。
2. 在项目根目录执行：

```powershell
python -m venv .pixelrender-venv
./.pixelrender-venv/Scripts/python.exe -m pip install -r Assets/PixelRender/Experiments/requirements.txt
./.pixelrender-venv/Scripts/python.exe Assets/PixelRender/Experiments/compare_abstraction.py
```

3. 打开 `Output/index.html` 查看原图、80×45 和 128×72 对照、区域诊断图；`validation.json` 记录色数、耗时与空区域数。

## 方法与控制变量

- 左：1280×720 原图最近邻缩小 + 最多 16 色；不限色版本另存为 `Unrestricted.png`。
- 中：面积缩小，在 CIELAB 空间使用确定性最远点初始化 + k-means，最多 16 色。
- 右：640×360 工作图上局部 3×3 候选区域分配，颜色距离使用当前区域的色板颜色；中心使用 40% 四邻域平滑，交替更新区域均值与 16 色 k-means，共 24 轮。
- 三组输出尺寸及色数上限相同，各自学习色板，不是共享固定色板。全部展示图放大均为最近邻。

与论文差异：以 k-means 代替 MCDA；没有温度调度、色簇分裂、区域代表色双边滤波、重要性图和最终增饱和；候选区域限制在初始网格邻域。空区域保留之前的均值。没有显式轮廓、语义或拓扑约束，因此可能产生轮廓变形或丢失细杆。

该脚本是 CPU 离线实验，不接入实时相机；没有时序约束。观察门窗、树干、石阶、栅栏及轮廓连贯性，不能仅凭均方误差判断美术质量；场景没有人物，不能从本实验推断人物识别效果。

验证包括：Lab/RGB 往返误差、输出尺寸及颜色数上限。当前生成图需结合视觉检查判断，不宣称优于基线。

## 本轮观察

已完成 Unity 1280×720 独立场景导出，两组共六张结果均为 16 色；Lab 往返与尺寸断言通过。当前机器每组计算约 5 秒（含图像输出，非性能基准）。

80×45 时面积缩小的部分边缘出现混合色；联合近似使部分色块更集中，但窗框与细杆仍有形变。128×72 时门窗更易辨认，但联合近似没有显示出一致优于最近邻限色的结果。现有场景已经使用分档光照及平涂材质，复杂照片上的论文收益不能直接外推到该输入。下一轮应补充人物样本，并对照测试论文的双边步骤与 MCDA；实时使用还需另行设计时序约束。
