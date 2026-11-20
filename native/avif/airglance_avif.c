#include "airglance_avif.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <avif/avif.h>

static void ag_set_error(char * error, size_t error_capacity, const char * message)
{
    if (error == NULL || error_capacity == 0)
    {
        return;
    }

    size_t length = strlen(message);
    if (length >= error_capacity)
    {
        length = error_capacity - 1;
    }

    memcpy(error, message, length);
    error[length] = '\0';
}

static void ag_set_result_error(const avifDecoder * decoder,
                                avifResult result,
                                char * error,
                                size_t error_capacity)
{
    const char * name = avifResultToString(result);
    if (decoder != NULL && decoder->diag.error[0] != '\0')
    {
        char message[512];
        snprintf(message, sizeof(message), "%s: %s", name, decoder->diag.error);
        ag_set_error(error, error_capacity, message);
    }
    else
    {
        ag_set_error(error, error_capacity, name);
    }
}

static avifDecoder * ag_decoder_create(const uint8_t * data,
                                       size_t size,
                                       char * error,
                                       size_t error_capacity)
{
    if (data == NULL || size == 0)
    {
        ag_set_error(error, error_capacity, "empty AVIF payload");
        return NULL;
    }

    avifDecoder * decoder = avifDecoderCreate();
    if (decoder == NULL)
    {
        ag_set_error(error, error_capacity, "libavif could not allocate a decoder");
        return NULL;
    }

    decoder->maxThreads = 4;
    decoder->imageSizeLimit = 250u * 1000u * 1000u;
    decoder->imageDimensionLimit = 32768;
    decoder->imageCountLimit = 64;
    // Be lenient with files produced by older encoders that omitted 'pixi' or
    // the alpha item's 'ispe'; still require a valid clean-aperture rect.
    decoder->strictFlags = AVIF_STRICT_CLAP_VALID;

    avifResult result = avifDecoderSetIOMemory(decoder, data, size);
    if (result != AVIF_RESULT_OK)
    {
        ag_set_result_error(decoder, result, error, error_capacity);
        avifDecoderDestroy(decoder);
        return NULL;
    }

    result = avifDecoderParse(decoder);
    if (result != AVIF_RESULT_OK)
    {
        ag_set_result_error(decoder, result, error, error_capacity);
        avifDecoderDestroy(decoder);
        return NULL;
    }

    return decoder;
}

int airglance_avif_probe(void)
{
    return avifVersion() != NULL ? 1 : 0;
}

const char * airglance_avif_version(void)
{
    return avifVersion();
}

int airglance_avif_info(const uint8_t * data,
                        size_t size,
                        AirGlanceAvifInfo * info,
                        uint8_t * icc,
                        size_t icc_capacity,
                        uint8_t * exif,
                        size_t exif_capacity,
                        char * error,
                        size_t error_capacity)
{
    if (info == NULL || data == NULL || size == 0)
    {
        ag_set_error(error, error_capacity, "invalid info arguments");
        return 1;
    }

    memset(info, 0, sizeof(*info));

    avifDecoder * decoder = ag_decoder_create(data, size, error, error_capacity);
    if (decoder == NULL)
    {
        return 3;
    }

    const avifImage * image = decoder->image;
    if (image == NULL)
    {
        ag_set_error(error, error_capacity, "libavif returned an empty image");
        avifDecoderDestroy(decoder);
        return 3;
    }

    info->width = image->width;
    info->height = image->height;
    info->depth = image->depth;
    info->transform_flags = image->transformFlags;
    info->frame_count = decoder->imageCount > 0 ? (uint32_t)decoder->imageCount : 1u;
    info->has_alpha = decoder->alphaPresent ? 1 : 0;

    if ((image->transformFlags & AVIF_TRANSFORM_IROT) != 0)
    {
        info->irot_angle = image->irot.angle;
    }

    if ((image->transformFlags & AVIF_TRANSFORM_IMIR) != 0)
    {
        info->imir_axis = image->imir.axis;
    }

    if ((image->transformFlags & AVIF_TRANSFORM_CLAP) != 0)
    {
        avifCropRect crop_rect;
        if (avifCropRectFromCleanApertureBox(
                &crop_rect, &image->clap, image->width, image->height, NULL))
        {
            info->clap_x = crop_rect.x;
            info->clap_y = crop_rect.y;
            info->clap_width = crop_rect.width;
            info->clap_height = crop_rect.height;
        }
    }

    info->icc_size = image->icc.size;
    info->exif_size = image->exif.size;

    if (icc != NULL && icc_capacity > 0 && image->icc.size > 0 && image->icc.data != NULL)
    {
        size_t copy_size = image->icc.size < icc_capacity ? image->icc.size : icc_capacity;
        memcpy(icc, image->icc.data, copy_size);
    }

    if (exif != NULL && exif_capacity > 0 && image->exif.size > 0 && image->exif.data != NULL)
    {
        size_t copy_size = image->exif.size < exif_capacity ? image->exif.size : exif_capacity;
        memcpy(exif, image->exif.data, copy_size);
    }

    avifDecoderDestroy(decoder);
    return 0;
}

int airglance_avif_decode(const uint8_t * data,
                          size_t size,
                          uint8_t * dst,
                          size_t dst_capacity,
                          uint32_t * out_width,
                          uint32_t * out_height,
                          int32_t * out_has_alpha,
                          char * error,
                          size_t error_capacity)
{
    if (data == NULL || size == 0 || dst == NULL || out_width == NULL || out_height == NULL)
    {
        ag_set_error(error, error_capacity, "invalid decode arguments");
        return 1;
    }

    avifDecoder * decoder = ag_decoder_create(data, size, error, error_capacity);
    if (decoder == NULL)
    {
        return 3;
    }

    avifResult result = avifDecoderNextImage(decoder);
    if (result != AVIF_RESULT_OK)
    {
        ag_set_result_error(decoder, result, error, error_capacity);
        avifDecoderDestroy(decoder);
        return 3;
    }

    avifImage * image = decoder->image;
    if (image == NULL || image->width == 0 || image->height == 0)
    {
        ag_set_error(error, error_capacity, "libavif returned an empty image");
        avifDecoderDestroy(decoder);
        return 3;
    }

    const uint32_t width = image->width;
    const uint32_t height = image->height;
    const size_t required_size = (size_t)width * (size_t)height * 4;
    if (dst_capacity < required_size)
    {
        ag_set_error(error, error_capacity, "destination buffer is too small");
        avifDecoderDestroy(decoder);
        return 4;
    }

    avifRGBImage rgb;
    avifRGBImageSetDefaults(&rgb, image);
    rgb.format = AVIF_RGB_FORMAT_BGRA;
    rgb.depth = 8;
    rgb.alphaPremultiplied = AVIF_TRUE;
    rgb.pixels = dst;
    rgb.rowBytes = width * 4;
    rgb.maxThreads = 4;

    result = avifImageYUVToRGB(image, &rgb);
    if (result != AVIF_RESULT_OK)
    {
        ag_set_result_error(decoder, result, error, error_capacity);
        avifDecoderDestroy(decoder);
        return 3;
    }

    *out_width = width;
    *out_height = height;
    if (out_has_alpha != NULL)
    {
        *out_has_alpha = decoder->alphaPresent ? 1 : 0;
    }

    avifDecoderDestroy(decoder);
    return 0;
}
