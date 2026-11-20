#ifndef AIRGLANCE_AVIF_H
#define AIRGLANCE_AVIF_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define AG_AVIF_API __declspec(dllexport)
#else
#define AG_AVIF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct AirGlanceAvifInfo
{
    uint32_t width;
    uint32_t height;
    uint32_t depth;
    uint32_t transform_flags;
    uint32_t irot_angle;
    uint32_t imir_axis;
    uint32_t clap_x;
    uint32_t clap_y;
    uint32_t clap_width;
    uint32_t clap_height;
    uint32_t frame_count;
    int32_t has_alpha;
    size_t icc_size;
    size_t exif_size;
} AirGlanceAvifInfo;

/* Returns 1 when the library is loaded and usable. */
AG_AVIF_API int airglance_avif_probe(void);

/* Returns the libavif version string, for diagnostics. */
AG_AVIF_API const char * airglance_avif_version(void);

/*
 * Parses the AVIF container without decoding pixels. Fills metadata in *info.
 * If icc/exif buffers are provided, copies up to their capacity; info->icc_size
 * and info->exif_size always report the full metadata sizes.
 *
 * Returns 0 on success, 1 on invalid arguments, 2 on allocation failure,
 * 3 when the file cannot be parsed.
 */
AG_AVIF_API int airglance_avif_info(const uint8_t * data,
                                    size_t size,
                                    AirGlanceAvifInfo * info,
                                    uint8_t * icc,
                                    size_t icc_capacity,
                                    uint8_t * exif,
                                    size_t exif_capacity,
                                    char * error,
                                    size_t error_capacity);

/*
 * Decodes the first frame as 8-bit BGRA, premultiplied alpha, tightly packed.
 * The caller must provide dst with at least width * height * 4 bytes. The
 * dimensions come from airglance_avif_info(); clap cropping and irot/imir are
 * reported as metadata and are applied by the caller.
 *
 * Returns 0 on success, 1 on invalid arguments, 2 on allocation failure,
 * 3 when the file cannot be decoded, 4 when dst is too small.
 */
AG_AVIF_API int airglance_avif_decode(const uint8_t * data,
                                      size_t size,
                                      uint8_t * dst,
                                      size_t dst_capacity,
                                      uint32_t * out_width,
                                      uint32_t * out_height,
                                      int32_t * out_has_alpha,
                                      char * error,
                                      size_t error_capacity);

#ifdef __cplusplus
}
#endif

#endif
