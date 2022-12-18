using System;
using UnityEngine.Experimental.Rendering;

namespace Pixelism {

    public static class TextureUtility {

        #region GetBitsPerPixel

        // 提供されてないの？
        public static int GetBitsPerPixel(GraphicsFormat graphicsFormat) {
            switch (graphicsFormat) {
                case GraphicsFormat.R8_SRGB:
                case GraphicsFormat.R8_UNorm:
                case GraphicsFormat.R8_SNorm:
                case GraphicsFormat.R8_UInt:
                case GraphicsFormat.R8_SInt:
                    return 8;

                case GraphicsFormat.R8G8_SRGB:
                case GraphicsFormat.R8G8_UNorm:
                case GraphicsFormat.R8G8_SNorm:
                case GraphicsFormat.R8G8_UInt:
                case GraphicsFormat.R8G8_SInt:
                    return 16;

                case GraphicsFormat.R8G8B8_SRGB:
                case GraphicsFormat.R8G8B8_UNorm:
                case GraphicsFormat.R8G8B8_SNorm:
                case GraphicsFormat.R8G8B8_UInt:
                case GraphicsFormat.R8G8B8_SInt:
                    return 24;

                case GraphicsFormat.R8G8B8A8_SRGB:
                case GraphicsFormat.R8G8B8A8_UNorm:
                case GraphicsFormat.R8G8B8A8_SNorm:
                case GraphicsFormat.R8G8B8A8_UInt:
                case GraphicsFormat.R8G8B8A8_SInt:
                case GraphicsFormat.B8G8R8A8_SRGB:
                case GraphicsFormat.B8G8R8A8_UNorm:
                case GraphicsFormat.B8G8R8A8_SNorm:
                case GraphicsFormat.B8G8R8A8_UInt:
                case GraphicsFormat.B8G8R8A8_SInt:
                    return 32;

                case GraphicsFormat.R16_UNorm:
                case GraphicsFormat.R16_SNorm:
                case GraphicsFormat.R16_UInt:
                case GraphicsFormat.R16_SInt:
                case GraphicsFormat.R16_SFloat:
                    return 16;

                case GraphicsFormat.R16G16_UNorm:
                case GraphicsFormat.R16G16_SNorm:
                case GraphicsFormat.R16G16_UInt:
                case GraphicsFormat.R16G16_SInt:
                case GraphicsFormat.R16G16_SFloat:
                    return 32;

                case GraphicsFormat.R16G16B16_UNorm:
                case GraphicsFormat.R16G16B16_SNorm:
                case GraphicsFormat.R16G16B16_UInt:
                case GraphicsFormat.R16G16B16_SInt:
                case GraphicsFormat.R16G16B16_SFloat:
                    return 48;

                case GraphicsFormat.R16G16B16A16_UNorm:
                case GraphicsFormat.R16G16B16A16_SNorm:
                case GraphicsFormat.R16G16B16A16_UInt:
                case GraphicsFormat.R16G16B16A16_SInt:
                case GraphicsFormat.R16G16B16A16_SFloat:
                    return 64;

                case GraphicsFormat.R32_UInt:
                case GraphicsFormat.R32_SInt:
                case GraphicsFormat.R32_SFloat:
                    return 32;

                case GraphicsFormat.R32G32_UInt:
                case GraphicsFormat.R32G32_SInt:
                case GraphicsFormat.R32G32_SFloat:
                    return 64;

                case GraphicsFormat.R32G32B32_UInt:
                case GraphicsFormat.R32G32B32_SInt:
                case GraphicsFormat.R32G32B32_SFloat:
                    return 96;

                case GraphicsFormat.R32G32B32A32_UInt:
                case GraphicsFormat.R32G32B32A32_SInt:
                case GraphicsFormat.R32G32B32A32_SFloat:
                    return 128;

                case GraphicsFormat.B8G8R8_SRGB:
                case GraphicsFormat.B8G8R8_UNorm:
                case GraphicsFormat.B8G8R8_SNorm:
                case GraphicsFormat.B8G8R8_UInt:
                case GraphicsFormat.B8G8R8_SInt:
                    return 24;

                case GraphicsFormat.R4G4B4A4_UNormPack16:
                case GraphicsFormat.B4G4R4A4_UNormPack16:
                case GraphicsFormat.R5G6B5_UNormPack16:
                case GraphicsFormat.B5G6R5_UNormPack16:
                case GraphicsFormat.R5G5B5A1_UNormPack16:
                case GraphicsFormat.B5G5R5A1_UNormPack16:
                case GraphicsFormat.A1R5G5B5_UNormPack16:
                    return 16;

                case GraphicsFormat.E5B9G9R9_UFloatPack32:
                case GraphicsFormat.B10G11R11_UFloatPack32:
                case GraphicsFormat.A2B10G10R10_UNormPack32:
                case GraphicsFormat.A2B10G10R10_UIntPack32:
                case GraphicsFormat.A2B10G10R10_SIntPack32:
                case GraphicsFormat.A2R10G10B10_UNormPack32:
                case GraphicsFormat.A2R10G10B10_UIntPack32:
                case GraphicsFormat.A2R10G10B10_SIntPack32:
                case GraphicsFormat.A2R10G10B10_XRSRGBPack32:
                case GraphicsFormat.A2R10G10B10_XRUNormPack32:
                case GraphicsFormat.R10G10B10_XRSRGBPack32:
                case GraphicsFormat.R10G10B10_XRUNormPack32:
                case GraphicsFormat.A10R10G10B10_XRSRGBPack32:
                case GraphicsFormat.A10R10G10B10_XRUNormPack32:
                    return 32;

                case GraphicsFormat.RGBA_DXT3_SRGB:
                case GraphicsFormat.RGBA_DXT3_UNorm:
                    return 8; // 4x4
                case GraphicsFormat.RGBA_DXT5_SRGB:
                case GraphicsFormat.RGBA_DXT5_UNorm:
                    return 8; // 4x4
                case GraphicsFormat.R_BC4_UNorm:
                case GraphicsFormat.R_BC4_SNorm:
                    return 4; // 4x4
                case GraphicsFormat.RG_BC5_UNorm:
                case GraphicsFormat.RG_BC5_SNorm:
                    return 8; // 4x4
                case GraphicsFormat.RGB_BC6H_UFloat:
                case GraphicsFormat.RGB_BC6H_SFloat:
                    return 8; // 4x4
                case GraphicsFormat.RGBA_BC7_SRGB:
                case GraphicsFormat.RGBA_BC7_UNorm:
                    return 8; // 4x4

                case GraphicsFormat.RGB_PVRTC_2Bpp_SRGB:
                case GraphicsFormat.RGB_PVRTC_2Bpp_UNorm:
                    break;

                case GraphicsFormat.RGB_PVRTC_4Bpp_SRGB:
                case GraphicsFormat.RGB_PVRTC_4Bpp_UNorm:
                    break;

                case GraphicsFormat.RGBA_PVRTC_2Bpp_SRGB:
                case GraphicsFormat.RGBA_PVRTC_2Bpp_UNorm:
                    break;

                case GraphicsFormat.RGBA_PVRTC_4Bpp_SRGB:
                case GraphicsFormat.RGBA_PVRTC_4Bpp_UNorm:
                    break;

                case GraphicsFormat.RGB_ETC_UNorm:
                    break;

                case GraphicsFormat.RGB_ETC2_SRGB:
                    break;

                case GraphicsFormat.RGB_ETC2_UNorm:
                    break;

                case GraphicsFormat.RGB_A1_ETC2_SRGB:
                    break;

                case GraphicsFormat.RGB_A1_ETC2_UNorm:
                    break;

                case GraphicsFormat.RGBA_ETC2_SRGB:
                    break;

                case GraphicsFormat.RGBA_ETC2_UNorm:
                    break;

                case GraphicsFormat.R_EAC_UNorm:
                    break;

                case GraphicsFormat.R_EAC_SNorm:
                    break;

                case GraphicsFormat.RG_EAC_UNorm:
                    break;

                case GraphicsFormat.RG_EAC_SNorm:
                    break;

                case GraphicsFormat.RGBA_ASTC4X4_SRGB:
                    break;

                case GraphicsFormat.RGBA_ASTC4X4_UNorm:
                    break;

                case GraphicsFormat.RGBA_ASTC5X5_SRGB:
                    break;

                case GraphicsFormat.RGBA_ASTC5X5_UNorm:
                    break;

                case GraphicsFormat.RGBA_ASTC6X6_SRGB:
                    break;

                case GraphicsFormat.RGBA_ASTC6X6_UNorm:
                    break;

                case GraphicsFormat.RGBA_ASTC8X8_SRGB:
                    break;

                case GraphicsFormat.RGBA_ASTC8X8_UNorm:
                    break;

                case GraphicsFormat.RGBA_ASTC10X10_SRGB:
                    break;

                case GraphicsFormat.RGBA_ASTC10X10_UNorm:
                    break;

                case GraphicsFormat.RGBA_ASTC12X12_SRGB:
                    break;

                case GraphicsFormat.RGBA_ASTC12X12_UNorm:
                    break;

                case GraphicsFormat.None:
                default:
                    break;
            }
            throw new NotImplementedException();
        }

        #endregion GetBitsPerPixel
    }
}
