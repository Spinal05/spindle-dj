using System;
using System.Collections.Generic;
using System.IO;

namespace SpindleRecovery.Engine
{
    /// <summary>Maps file extensions to the categories shown in the UI and to PhotoRec file families.</summary>
    public static class FileTypes
    {
        static readonly Dictionary<string, FileCategory> map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The order categories appear in on screen.</summary>
        public static readonly FileCategory[] DisplayOrder =
        {
            FileCategory.Pictures, FileCategory.Video, FileCategory.Audio, FileCategory.Documents,
            FileCategory.Email, FileCategory.Archives, FileCategory.Other,
        };

        public static HashSet<FileCategory> AllCategories() => new HashSet<FileCategory>(DisplayOrder);

        public static string DisplayName(FileCategory c)
        {
            switch (c)
            {
                case FileCategory.Pictures: return "Pictures";
                case FileCategory.Video: return "Videos";
                case FileCategory.Audio: return "Music";
                case FileCategory.Documents: return "Documents";
                case FileCategory.Email: return "Email";
                case FileCategory.Archives: return "Archives";
                default: return "Other files";
            }
        }

        /// <summary>Examples shown next to each category so people know what it includes.</summary>
        public static string Examples(FileCategory c)
        {
            switch (c)
            {
                case FileCategory.Pictures: return "JPG, PNG, HEIC, GIF, camera RAW (CR2, NEF, ARW…), PSD";
                case FileCategory.Video: return "MP4, MOV, AVI, MKV, MTS, WMV";
                case FileCategory.Audio: return "MP3, WAV, FLAC, M4A, OGG, AIFF";
                case FileCategory.Documents: return "Word, Excel, PowerPoint, PDF, text, OpenOffice";
                case FileCategory.Email: return "Outlook PST/OST, mailboxes (MBOX), Lotus Notes";
                case FileCategory.Archives: return "ZIP, RAR, 7Z, GZ, ISO disk images";
                default: return "Programs, databases, and everything else";
            }
        }

        /// <summary>Short list of chosen categories for headings, e.g. "Pictures, Videos".</summary>
        public static string Describe(ICollection<FileCategory> chosen)
        {
            if (chosen == null || chosen.Count == 0 || chosen.Count >= DisplayOrder.Length) return "all file types";
            var names = new List<string>();
            foreach (var c in DisplayOrder) if (chosen.Contains(c)) names.Add(DisplayName(c));
            return string.Join(", ", names);
        }

        // PhotoRec "fileopt" family identifiers per category. Every name here must exist in
        // PhotoRec 7.2 (tests/fixtures/photorec-formats.txt; an unknown name silently ends the
        // format list). "tif" also covers CR2/NEF/DNG raw photos, "riff" covers AVI/WAV, "mov"
        // covers MP4/M4A/3GP, "doc" covers older Office files, "zip" covers DOCX/XLSX/PPTX,
        // "tx?" covers text files with headers such as MBOX mailboxes.
        public static readonly Dictionary<FileCategory, string[]> PhotoRecFamilies = new Dictionary<FileCategory, string[]>
        {
            [FileCategory.Pictures] = new[] { "jpg", "png", "gif", "bmp", "tif", "psd", "raf", "crw", "orf", "rw2", "mrw", "ico", "exr", "pcx" },
            [FileCategory.Video] = new[] { "mov", "mpg", "mkv", "flv", "asf", "riff", "m2ts", "rm", "swf", "ogg" },
            [FileCategory.Audio] = new[] { "mp3", "flac", "riff", "ogg", "mid", "aif", "amr", "ape", "au", "ra", "caf", "mov" },
            [FileCategory.Documents] = new[] { "doc", "zip", "pdf", "txt", "one", "chm", "djv", "ps", "lit", "qbb", "sqlite", "accdb", "mdb", "ttf" },
            [FileCategory.Email] = new[] { "pst", "nsf", "nk2", "tx?" },
            [FileCategory.Archives] = new[] { "zip", "rar", "7z", "gz", "bz2", "cab", "lzh", "arj", "ace", "iso" },
            [FileCategory.Other] = new[] { "exe", "elf", "class", "pyc", "sqm", "key", "pfx", "dbf", "edb", "evt", "lnk", "reg" },
        };

        static FileTypes()
        {
            Add(FileCategory.Pictures, "jpg jpeg jpe jfif png gif bmp dib tif tiff psd webp heic heif raf cr2 cr3 crw nef nrw orf rw2 x3f mrw dng arw srf sr2 pef ico svg xcf exr jp2 pcx tga wmf emf");
            Add(FileCategory.Video, "mov mp4 m4v 3gp 3g2 mpg mpeg mkv webm avi flv f4v asf wmv mts m2ts ts rm rmvb swf vob ogv");
            Add(FileCategory.Audio, "mp3 flac wav ogg oga opus mid midi aif aiff amr ape au m4a m4p aac wma ra voc caf dsf wv");
            Add(FileCategory.Email, "pst ost dbx mbox eml emlx msg nsf nk2 olm");
            Add(FileCategory.Documents, "doc docx docm dot dotx xls xlsx xlsm xlt ppt pptx pps ppsx pdf rtf txt text csv xml html htm odt ods odp odg wpd one chm djv djvu ps lit qbb qbw pub vsd vsdx sqlite db accdb mdb ttf otf eps epub md json log ini");
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
