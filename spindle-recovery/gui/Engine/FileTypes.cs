using System;
using System.Collections.Generic;
using System.IO;

namespace SpindleRecovery.Engine
{
    /// <summary>Maps file extensions to the categories shown in the UI and to PhotoRec file families.</summary>
    public static class FileTypes
    {
        static readonly Dictionary<string, FileCategory> map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);

        // PhotoRec "fileopt" family identifiers per category. Every name here must exist in
        // PhotoRec 7.2 (an unknown name aborts the scan); "tif" also covers CR2/NEF/DNG raw
        // photos, "riff" covers AVI/WAV, "mov" covers MP4/M4A/3GP, "doc" covers Office files.
        public static readonly Dictionary<FileCategory, string[]> PhotoRecFamilies = new Dictionary<FileCategory, string[]>
        {
            [FileCategory.Pictures] = new[] { "jpg", "png", "gif", "bmp", "tif", "psd", "raf", "crw", "orf", "rw2", "mrw", "ico", "exr", "pcx", "jpg" },
            [FileCategory.Video] = new[] { "mov", "mpg", "mkv", "flv", "asf", "riff", "m2ts", "rm", "swf", "ogg" },
            [FileCategory.Audio] = new[] { "mp3", "flac", "riff", "ogg", "mid", "aif", "amr", "ape", "au", "ra", "caf" },
            [FileCategory.Documents] = new[] { "doc", "zip", "pdf", "txt", "pst", "one", "chm", "djv", "ps", "lit", "qbb", "sqlite", "accdb", "mdb", "ttf" },
            [FileCategory.Archives] = new[] { "zip", "rar", "7z", "gz", "bz2", "cab", "lzh", "arj", "ace", "iso" },
            [FileCategory.Other] = new[] { "exe", "elf", "class", "pyc", "sqm", "key", "pfx", "dbf", "edb", "evt", "lnk", "reg" },
        };

        static FileTypes()
        {
            Add(FileCategory.Pictures, "jpg jpeg jpe jfif png gif bmp dib tif tiff psd webp heic heif raf cr2 cr3 crw nef nrw orf rw2 x3f mrw dng arw srf sr2 pef ico svg xcf exr jp2 pcx tga wmf emf");
            Add(FileCategory.Video, "mov mp4 m4v 3gp 3g2 mpg mpeg mkv webm avi flv f4v asf wmv mts m2ts ts rm rmvb swf vob ogv");
            Add(FileCategory.Audio, "mp3 flac wav ogg oga opus mid midi aif aiff amr ape au m4a m4p aac wma ra voc caf dsf wv");
            Add(FileCategory.Documents, "doc docx docm dot dotx xls xlsx xlsm xlt ppt pptx pps ppsx pdf rtf txt text csv xml html htm odt ods odp odg wpd pst ost dbx mbox eml msg one chm djv djvu ps lit qbb qbw pub vsd vsdx sqlite db accdb mdb ttf otf eps epub md json log ini");
            Add(FileCategory.Archives, "zip rar 7z gz tgz bz2 tbz xz txz tar cab lzh lha arj ace dmg iso img vmdk vdi vhd vhdx z zst");
        }

        static void Add(FileCategory c, string exts)
        {
            foreach (var e in exts.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!map.ContainsKey(e)) map[e] = c;
        }

        public static FileCategory Categorize(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? "").TrimStart('.');
            return map.TryGetValue(ext, out var c) ? c : FileCategory.Other;
        }

        public static string Describe(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return "File";
            return ext.ToUpperInvariant() + " file";
        }

        public static string FormatSize(long bytes)
        {
            string[] u = { "bytes", "KB", "MB", "GB", "TB", "PB" };
            double v = bytes;
            int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
            return i == 0 ? $"{bytes:N0} bytes" : (v >= 100 ? $"{v:0} {u[i]}" : $"{v:0.#} {u[i]}");
        }
    }
}
