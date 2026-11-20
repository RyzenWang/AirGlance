# 真实文件验收（2026-11-20）

## 样本

**ESO Trifid Nebula — eso0930a**

- 来源：<https://www.eso.org/public/archives/images/original/eso0930a.tif>
- 38.2 MB，5752 × 3497（20.1 MP）
- RGB 8-bit，LZW，stripped，`RowsPerStrip=24`

以及由该真实图片重采样得到的 16000 × 16000（256 MP）真实内容文件：

- `eso-trifid-16000-realcontent.tif`
- RGB 8-bit，LZW，`RowsPerStrip=128`
- 623 MB，仅用于触发大 TIFF 的托管预览/区域路径

## 结果

### 20 MP 原始文件

`--bench=startup`：

| 指标 | 结果 |
| --- | --- |
| first_frame_ms | 2530.6 |
| decode_ms | 1098.6 |
| image_mp | 20.1 |
| peak_ws_mb | 222.2 |

这是普通大小 TIFF，走完整解码，不走托管大 TIFF 预览。

LibTiff 对文件里的 `XMLPacket`、`RichTIFFIPTC`、`Photoshop` 标签各报一次
“wrong data type UNDEFINED”，但图像解码正常，属于元数据噪声。

### 256 MP 真实内容文件

| 指标 | 结果 |
| --- | --- |
| first_frame_ms | 1982.6 |
| decode_ms | 1836.2 |
| peak_ws_mb | 366.6 |

确认走了托管大 TIFF 预览路径（`image_mp=256`），没有回退 libvips。

### 100% 区域

- 界面显示 `区域 · 100%`；
- 真实星云内容正确渲染；
- 工作集约 **992 MB**。

内存偏高的原因需要继续拆分：区域 strip 缓存（96 MiB）、LibTiff 条带解码缓冲、
32 个 tile bitmap，以及可能的代理/GC 暂存。功能通过，但这是目前最明显的
内存问题。

## 结论

- 真实 20 MP 文件可正常打开；
- 256 MP 真实内容文件的托管预览路径可用；
- 100% 区域可用，但工作集接近 1 GB，需要优化；
- 12-bit JPEG 未覆盖，仍走回退。

## 复现

```powershell
# 从 ESO 下载
curl -L https://www.eso.org/public/archives/images/original/eso0930a.tif -o eso0930a.tif

# 应用基准
AirGlance.exe --bench=startup eso0930a.tif
AirGlance.exe --bench=startup eso-trifid-16000-realcontent.tif
```

截图：`artifacts/real-file-acceptance/screenshot-real-region.png`
