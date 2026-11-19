# AirGlance 基础编辑与导出 — v1 交互视觉稿

> 状态：v1 已实现（`feature/basic-edit-export` / `d25cdec`）。  
> 原型：[edit-export-v1.html](edit-export-v1.html)  
> 静态稿：[入口与 dock](edit-export-v1-closed.png) · [编辑面板](edit-export-v1-edit.png) · [裁剪](edit-export-v1-crop.png) · [导出](edit-export-v1-export.png) · [代理图禁用](edit-export-v1-proxy.png)

## 目标

在不破坏现有“本地、非破坏、以查看为中心”定位的前提下，增加一组
足够常用的编辑和导出能力：

- 90° 旋转；
- 水平/垂直翻转；
- 自由裁剪和常用比例裁剪；
- 导出 JPEG / PNG / WebP 副本；
- 导出时可调整质量、尺寸、文件名，并选择是否保留元数据。

原文件永远不写入。

## 已确认的 v1 决策

- 只做 rotate / flip / crop + export，不做曝光和色彩调整。
- 只有代理图的大 TIFF：禁止编辑，提示需要完整分辨率。
- 双画布：v1 禁止编辑，只支持单画布。
- 元数据：默认保留。
- 不做 `Ctrl+Z`，只用 Reset + Cancel。
- Edit 放 dock 全屏按钮左边、分隔符右边，快捷键 `Ctrl+E`。

---

# 一、入口与状态

## 入口

在底部 dock 增加一个铅笔图标：

```text
Edit
```

快捷键：`Ctrl+E`。

Edit 放在底部 dock 中 **全屏按钮左侧、分隔符右侧**：

```text
… | rotate  info  filmstrip  dual  slideshow | Edit  Fullscreen  Settings
```

原型的 dock 已按这个顺序绘制；Edit 激活时使用陶土红。

双画布中 v1 禁止编辑，只支持单画布。

## 进入编辑模式

右侧滑入 350 px 编辑面板：

```text
Image
├─ Edit panel (right, 350 px)
├─ Crop overlay (only when crop is active)
└─ Export modal (on demand)
```

编辑面板打开时：

- 图片仍保持当前缩放和位置；
- 不改变原文件；
- 不阻塞查看、缩放和平移；
- 右下角显示 “Original stays unchanged”。

## 退出

- 面板 `Cancel` / 右上角 ×；
- `Esc`；
- 编辑面板关闭时丢弃本会话编辑；
- 如果已有编辑，切换图片或关闭窗口时提示：

```text
Discard edits?
[Keep editing] [Discard]
```

v1 不做 undo/redo，只做每个 section 的 Reset 和整体 Cancel。

---

# 二、编辑面板

## Transform

四个按钮：

```text
[ Rotate left ] [ Rotate right ] [ Flip H ] [ Flip V ]
```

行为：

- 90° 步进旋转；
- 旋转后同步更新裁剪矩形和显示尺寸；
- 所有操作只影响当前编辑会话；
- 右上角 `Reset` 恢复全部 transform 和 crop。

## Crop

比例 chips：

```text
Free   Original   1:1   4:3   16:9
```

行为：

- 选中 Crop 后显示裁剪框；
- 8 个拖拽手柄；
- 框外变暗；
- 显示三分线；
- 框下方显示 `1980 × 1224 · Free`；
- 可选比例会锁定宽高比；
- 裁剪框不能超出原图；
- 最小尺寸建议 32 × 32；
- `Reset crop` 恢复整图；
- `Crop overlay` 开关用于临时隐藏/显示裁剪框，不丢弃裁剪区域。

裁剪框使用源图像坐标，旋转/翻转后自动映射；导出的裁剪尺寸取源图像素，而不是屏幕缩放后的尺寸。

## Export

面板底部固定：

```text
Original stays unchanged
[ Cancel ] [ Export copy… ]
```

点击 `Export copy…` 打开导出对话框。

---

# 三、导出对话框

## 布局

- 左侧：裁剪后的预览图 + `1980 × 1224 · cropped preview`；
- 右侧：格式、质量、尺寸、文件名、元数据；
- 底部：`Cancel` / `Export copy`；
- 背景模糊并压暗，避免误操作。

## 格式

```text
[ JPEG ] [ PNG ] [ WebP ]
```

默认 JPEG。

- JPEG：质量 40–100，默认 90；透明区域压平到白色；
- PNG：无损，质量滑块禁用；保留 alpha；
- WebP：质量 40–100；保留 alpha。

## 尺寸

```text
[ Original pixels v ] [ 4096 ] px
```

选项：

- Original pixels：保持裁剪后的源尺寸；
- Long edge：指定长边；
- Fit within：指定最大宽高。

v1 默认 **Original pixels**，不主动放大。  
如果目标尺寸大于源尺寸，显示提示 `This will upscale the image`，但仍允许。

## 文件名和路径

默认文件名：

```text
<原名>-edited.<ext>
```

默认保存位置：原图所在文件夹。  
如果同名文件存在：

```text
A file with this name already exists.
[Choose another name] [Replace]
```

不提供静默覆盖。

## 元数据

```text
[x] Keep EXIF and ICC metadata where supported
```

默认勾选，但在以下情况自动处理：

- 裁剪/旋转后更新 Orientation；
- JPEG 保留 EXIF/ICC；
- PNG/WebP 按编码器能力保留 ICC；
- 不保留原图的“编辑历史”或私有 XMP 时明确说明。

## 进度

导出在后台执行：

```text
Exporting…  62%
[Cancel]
```

- 编码期间 UI 保持响应；
- 大图显示进度；
- 取消后删除未完成临时文件；
- 成功提示 `Saved to <path>`，并提供“在文件夹中显示”。

---

# 四、大图 / 特殊图片策略

## 只有代理图的大 TIFF

如果当前文件只有代理，没有完整分辨率可编辑：

- Edit 按钮仍显示，但置灰或打开后提示：

```text
Editing this image requires a full-resolution decode.
This file currently has a preview only.
```

v1 **不允许编辑代理图**，避免用户导出低清结果却不知情。

## AVIF / TIFF / GIF

- AVIF：编辑静态帧；动画仍只取第一帧；
- TIFF：只编辑第一页；
- GIF：只编辑第一帧，导出静态图；
- 大 TIFF / 100% tile：v1 不直接编辑 tile 源。

## 双画布

v1 明确禁止编辑：

- 双画布中 Edit 按钮置灰；
- 点击时提示“先退出双画布再编辑”；
- 不保存每个 pane 的编辑状态；
- 不实现 active pane 编辑。

# 五、交互细节

| 操作 | 行为 |
| --- | --- |
| 点击 Edit | 打开编辑面板 |
| `Ctrl+E` | 打开/关闭编辑面板 |
| 点击 Crop | 显示裁剪框并锁定当前比例 |
| 拖动角/边 | 调整裁剪区域 |
| 方向键 | 以 1 px 微调裁剪框（Shift=10 px） |
| `Esc` | 先关闭导出对话框，再关闭编辑面板 |
| 关闭编辑 | 有改动时确认放弃 |
| 切换图片 | 有改动时确认放弃 |

不建议：

- 拖拽图片本身来移动裁剪框，容易和现有平移手势冲突；
- 编辑后自动覆盖原图；
- 在查看模式直接永久修改。

---

# 六、视觉规范

| 元素 | 规范 |
| --- | --- |
| 面板宽度 | 350 px |
| 面板背景 | `#FBF8F3`，与信息抽屉一致 |
| 强调色 | `#D65A43` 陶土红 |
| 辅助色 | `#237F76` teal，用于“原图安全”提示 |
| 圆角 | 面板 20 px，按钮 9–11 px |
| 字体 | 沿用 Inter / 系统字体 |
| 裁剪遮罩 | 黑色 52% 透明度 |
| 裁剪框 | 白色 1.5 px + 8 个陶土红手柄 |
| 动效 | 面板 180 ms 滑入；裁剪框 120 ms |

---

# 七、实现范围建议

v1 只做：

- 90° rotate；
- flip H/V；
- free / original / 1:1 / 4:3 / 16:9 crop；
- JPEG / PNG / WebP 导出；
- quality、原始尺寸/长边/最大宽高；
- 文件名、保存位置、元数据开关；
- 原文件保护、覆盖确认、进度和错误处理。

v1 不做：

- 曝光、对比度、饱和度、曲线；
- 自由角度校正/透视；
- 文字、贴纸、图层；
- 批量编辑；
- 非破坏编辑历史；
- 视频。

估算：

- 编辑状态模型和预览渲染：3–5 天；
- 面板和裁剪交互：3–5 天；
- 导出编码、尺寸、元数据：3–5 天；
- 边界/性能/测试：3–5 天；
- 合计约 **2–3 周**。

---

# 八、确认后的实现顺序

1. 编辑状态模型：rotation、flip、crop rect；
2. 单画布编辑面板和 dock 入口；
3. 裁剪 overlay 与 8 向手柄；
4. JPEG / PNG / WebP 导出、尺寸和元数据；
5. 代理图、双画布、错误和取消路径；
6. 测试和本地包。

