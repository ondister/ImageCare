using System.Runtime.InteropServices;

namespace LibRawDotNet.Native;

internal class LibRawNativeWrapper
{
    private const string LibraryName = @"libraw";

    // callback functions
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public delegate int ProgressCallback(nint unused_data, LibRawProgress state, int iter, int expected);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public delegate void DataCallback(nint data, string file, int offset);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public delegate void MemoryCallback(nint data, string file, string where);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public delegate void EXIFParserCallback(nint context, int tag, int type, int len, uint ord, nint ifp, long _base);

    // Initialization and denitialization
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_init(LibRawInitFlags flags);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_close(nint handler);

    // Data Loading from a File/Buffer
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_open_file(nint handler, string filename);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_open_file_ex(nint handler, string filename, long max_buff_sz);

    [DllImport(LibraryName, CharSet = CharSet.Unicode)]
    public static extern LibRawErrors libraw_open_wfile(nint handler, string filename);

    [DllImport(LibraryName, CharSet = CharSet.Unicode)]
    public static extern LibRawErrors libraw_open_wfile_ex(nint handler, string filename, long max_buff_sz);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_open_buffer(nint handler, byte[] buffer, long size);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_unpack(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_unpack_thumb(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_unpack_thumb_ex(nint handler, int index);

    // Parameters setters/getters
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_get_raw_height(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_get_raw_width(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_get_iheight(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_get_iwidth(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern float libraw_get_cam_mul(nint handler, int index);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern float libraw_get_pre_mul(nint handler, int index);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern float libraw_get_rgb_cam(nint handler, int index1, int index2);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_get_iparams(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_get_lensinfo(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_get_imgother(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_get_color_maximum(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_user_mul(nint handler, int index, float val);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_demosaic(nint handler, LibRawInterpolationQuality value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_adjust_maximum_thr(nint handler, float value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_output_color(nint handler, LibRawOutputColor value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_output_bps(nint handler, LibRawOutputBps value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_gamma(nint handler, int index, float value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_no_auto_bright(nint handler, int value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_bright(nint handler, float value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_highlight(nint handler, LibRawHighlightMode value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_fbdd_noiserd(nint handler, LibRawFbddNoiseReduction value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_output_tif(nint handler, LibRawOutputFormats value);

    // Auxiliary Functions
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_version();

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_versionNumber();

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawRuntimeCapabilities libraw_capabilities();

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_cameraCount();

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_cameraList();

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_get_decoder_info(nint handler, nint decoder);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_unpack_function_name(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern int libraw_COLOR(nint handler, int row, int col);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_subtract_black(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_recycle_datastream(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_recycle(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_strerror(LibRawErrors errorcode);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_strprogress(LibRawProgress progress);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_memerror_handler(nint handler, MemoryCallback cb, nint datap);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_exifparser_handler(nint handler, EXIFParserCallback cb, nint datap);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_dataerror_handler(nint handler, DataCallback func, nint datap);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_set_progress_handler(nint handler, ProgressCallback callback, nint datap);

    // Data Postprocessing, Emulation of dcraw Behavior
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_dcraw_process(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_raw2image(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_free_image(nint handler);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_adjust_sizes_info_only(nint handler);

    // Writing to Output Files
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_dcraw_ppm_tiff_writer(nint handler, string filename);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern LibRawErrors libraw_dcraw_thumb_writer(nint handler, string filename);

    // Writing processing results to memory buffer
    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_dcraw_make_mem_image(nint handler, ref LibRawErrors errc);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern nint libraw_dcraw_make_mem_thumb(nint handler, ref LibRawErrors errc);

    [DllImport(LibraryName, CharSet = CharSet.Ansi)]
    public static extern void libraw_dcraw_clear_mem(nint img);

    // Microsoft Visual C runtime functions
    [DllImport("msvcrt", CharSet = CharSet.Ansi)]
    public static extern nint strerror(int errc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_iparams_t
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
        public string guard;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string make;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string model;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string software;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string normalized_make;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string normalized_model;
        public LibRawCameraMakerIndex maker_index;
        public uint raw_count;
        public uint dng_version;
        public uint is_foveon;
        public int colors;
        public uint filters;
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 36)]
        public byte[] xtrans;
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 36)]
        public byte[] xtrans_abs;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
        public string cdesc;
        public uint xmplen;
        public nint xmpdata;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_nikonlens_t
    {
        public float EffectiveMaxAp;
        public byte LensIDNumber;
        public byte LensFStops;
        public byte MCUVersion;
        public byte LensType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_dnglens_t
    {
        public float MinFocal;
        public float MaxFocal;
        public float MaxAp4MinFocal;
        public float MaxAp4MaxFocal;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_makernotes_lens_t
    {
        public long LensID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Lens;
        public ushort LensFormat; /* to characterize the image circle the lens covers */
        public ushort LensMount; /* 'male', lens itself */
        public long CamID;
        public ushort CameraFormat; /* some of the sensor formats */
        public ushort CameraMount; /* 'female', body throat */
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string body;
        public short FocalType; /* -1/0 is unknown; 1 is fixed focal; 2 is zoom */
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string LensFeatures_pre;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string LensFeatures_suf;
        public float MinFocal;
        public float MaxFocal;
        public float MaxAp4MinFocal;
        public float MaxAp4MaxFocal;
        public float MinAp4MinFocal;
        public float MinAp4MaxFocal;
        public float MaxAp;
        public float MinAp;
        public float CurFocal;
        public float CurAp;
        public float MaxAp4CurFocal;
        public float MinAp4CurFocal;
        public float MinFocusDistance;
        public float FocusRangeIndex;
        public float LensFStops;
        public long TeleconverterID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Teleconverter;
        public long AdapterID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Adapter;
        public long AttachmentID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Attachment;
        public ushort FocalUnits;
        public float FocalLengthIn35mmFormat;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_lensinfo_t
    {
        public float MinFocal;
        public float MaxFocal;
        public float MaxAp4MinFocal;
        public float MaxAp4MaxFocal;
        public float EXIF_MaxAp;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string LensMake;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Lens;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string LensSerial;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string InternalLensSerial;
        public ushort FocalLengthIn35mmFormat;
        [MarshalAs(UnmanagedType.Struct)]
        public libraw_nikonlens_t nikon;
        [MarshalAs(UnmanagedType.Struct)]
        public libraw_dnglens_t dng;
        [MarshalAs(UnmanagedType.Struct)]
        public libraw_makernotes_lens_t makernotes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_gps_info_t
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.R4, SizeConst = 3)]
        public float[] latitude; /* Deg,min,sec */
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.R4, SizeConst = 3)]
        public float[] longitude; /* Deg,min,sec */
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.R4, SizeConst = 3)]
        public float[] gpstimestamp; /* Deg,min,sec */
        public float altitude;
        public byte altref;
        public byte latref;
        public byte longref;
        public byte gpsstatus;
        public byte gpsparsed;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_imgother_t
    {
        public float iso_speed;
        public float shutter;
        public float aperture;
        public float focal_len;
        public long timestamp; // time_t
        public uint shot_order;
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U4, SizeConst = 32)]
        public uint[] gpsdata;
        [MarshalAs(UnmanagedType.Struct)]
        public libraw_gps_info_t parsed_gps;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string desc;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string artist;
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.R4, SizeConst = 4)]
        public float[] analogbalance;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_decoder_info_t
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string decoder_name;
        public LibRawDecoderFlags decoder_flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi /*, Pack = 1 */)]
    public struct libraw_processed_image_t
    {
        public LibRawImageFormats type;
        public ushort height;
        public ushort width;
        public ushort colors;
        public ushort bits;
        public uint data_size;
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 1)]
        public byte[] data;
    }
}