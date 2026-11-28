using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpindleRecovery.Engine
{
    /// <summary>One recoverable format in the format picker, e.g. "JPEG" (Pictures).</summary>
    public sealed class FormatOption
    {
        public readonly string Id;            // stable key (saved in settings)
        public readonly string Label;
        public readonly FileCategory Category;
        public readonly string[] Families;    // PhotoRec "fileopt" families that find this format
        public readonly string[] Extensions;  // extensions this format produces (for filtering results)

        public FormatOption(string id, string label, FileCategory category, string families, string extensions)
        {
            Id = id; Label = label; Category = category;
            Families = families.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Extensions = extensions.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public string ExtensionList => "." + string.Join(" .", Extensions);
        public override string ToString() => Label;
    }

    /// <summary>
    /// The formats chosen for a scan. Decides which PhotoRec families Deep Scan enables and which
    /// found files are shown (Quick Scan lists everything, so its results are filtered here).
    /// </summary>
    public sealed class FileSelection
    {
        public readonly HashSet<string> FormatIds;

        public FileSelection(IEnumerable<string> formatIds)
        {
            FormatIds = new HashSet<string>(formatIds.Where(id => FileTypes.FormatsById.ContainsKey(id)));
        }

        public static FileSelection All() => new FileSelection(FileTypes.Formats.Select(f => f.Id));

        public static FileSelection FromCategories(IEnumerable<FileCategory> cats)
        {
            var set = new HashSet<FileCategory>(cats ?? FileTypes.DisplayOrder);
            return new FileSelection(FileTypes.Formats.Where(f => set.Contains(f.Category)).Select(f => f.Id));
        }

        public IEnumerable<FormatOption> Selected => FileTypes.Formats.Where(f => FormatIds.Contains(f.Id));
        public bool IsEmpty => FormatIds.Count == 0;
        public bool IsEverything => FileTypes.Formats.All(f => FormatIds.Contains(f.Id));

        public HashSet<FileCategory> Categories => new HashSet<FileCategory>(Selected.Select(f => f.Category));

        public bool IsWholeCategory(FileCategory c) => FileTypes.Formats.Where(f => f.Category == c).All(f => FormatIds.Contains(f.Id));

        public int CountIn(FileCategory c) => FileTypes.Formats.Count(f => f.Category == c && FormatIds.Contains(f.Id));

        /// <summary>PhotoRec families to enable (sorted); empty when nothing is chosen.</summary>
        public List<string> Families => Selected.SelectMany(f => f.Families).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Whether a found file matches the selection. A fully chosen category keeps every file in it
        /// (including extensions the catalogue does not list); a partly chosen one keeps only the
        /// extensions of its chosen formats.
        /// </summary>
        public bool Includes(FoundFile f)
        {
            var c = f.Category;
            if (IsWholeCategory(c)) return true;
            var ext = (f.Extension ?? "").ToLowerInvariant();
            return Selected.Any(o => o.Category == c && o.Extensions.Contains(ext));
        }

        /// <summary>"Pictures (JPEG, PNG), Documents" style summary.</summary>
        public string Describe()
        {
            if (IsEmpty) return "nothing";
            if (IsEverything) return "all file types";
            var parts = new List<string>();
            foreach (var c in FileTypes.DisplayOrder)
            {
                int n = CountIn(c);
                if (n == 0) continue;
                if (IsWholeCategory(c)) { parts.Add(FileTypes.DisplayName(c)); continue; }
                var labels = Selected.Where(f => f.Category == c).Select(f => f.Label).ToList();
                parts.Add(FileTypes.DisplayName(c) + " (" + (labels.Count <= 3 ? string.Join(", ", labels) : labels.Count + " formats") + ")");
            }
            return string.Join(", ", parts);
        }
    }

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

        /// <summary>
        /// Popular formats offered in the format picker. Each names the PhotoRec families that find it
        /// (checked against PhotoRec 7.2 by the unit tests) and the extensions it produces.
        /// </summary>
        public static readonly FormatOption[] Formats =
        {
            new FormatOption("jpg",  "JPEG photos",                     FileCategory.Pictures, "jpg",  "jpg jpeg jpe jfif"),
            new FormatOption("heic", "HEIC / HEIF (iPhone)",            FileCategory.Pictures, "mov",  "heic heif"),
            new FormatOption("png",  "PNG images",                      FileCategory.Pictures, "png",  "png"),
            new FormatOption("gif",  "GIF images",                      FileCategory.Pictures, "gif",  "gif"),
            new FormatOption("bmp",  "Bitmap (BMP)",                    FileCategory.Pictures, "bmp",  "bmp dib"),
            new FormatOption("webp", "WebP images",                     FileCategory.Pictures, "riff", "webp"),
            new FormatOption("raw",  "Camera RAW: Nikon, Sony, Canon CR2, Adobe DNG, TIFF", FileCategory.Pictures, "tif", "tif tiff nef nrw arw srf sr2 cr2 dng pef"),
            new FormatOption("cr3",  "Canon RAW CR3",                   FileCategory.Pictures, "mov",  "cr3"),
            new FormatOption("raw2", "Camera RAW: Fuji, Olympus, Panasonic, old Canon, Minolta", FileCategory.Pictures, "raf orf rw2 crw mrw", "raf orf rw2 crw mrw"),
            new FormatOption("psd",  "Photoshop (PSD)",                 FileCategory.Pictures, "psd",  "psd"),
            new FormatOption("ico",  "Icons, OpenEXR, PCX",             FileCategory.Pictures, "ico exr pcx", "ico exr pcx"),

            new FormatOption("mp4",  "MP4 / MOV / M4V / 3GP",           FileCategory.Video, "mov",  "mp4 mov m4v 3gp 3g2"),
            new FormatOption("avi",  "AVI",                             FileCategory.Video, "riff", "avi"),
            new FormatOption("mkv",  "MKV / WebM",                      FileCategory.Video, "mkv",  "mkv webm"),
            new FormatOption("mts",  "AVCHD camcorder (MTS / M2TS)",    FileCategory.Video, "m2ts", "mts m2ts ts"),
            new FormatOption("wmv",  "Windows Media Video (WMV / ASF)", FileCategory.Video, "asf",  "wmv asf"),
            new FormatOption("mpg",  "MPEG / DVD video",                FileCategory.Video, "mpg",  "mpg mpeg m2v vob"),
            new FormatOption("flv",  "Flash video (FLV / SWF)",         FileCategory.Video, "flv swf", "flv f4v swf"),
            new FormatOption("rm",   "RealMedia / Ogg video",           FileCategory.Video, "rm ogg", "rm rmvb ogv"),

            new FormatOption("mp3",  "MP3",                             FileCategory.Audio, "mp3",  "mp3"),
            new FormatOption("m4a",  "M4A / AAC (iTunes)",              FileCategory.Audio, "mov",  "m4a m4p m4b aac"),
            new FormatOption("wav",  "WAV",                             FileCategory.Audio, "riff", "wav"),
            new FormatOption("flac", "FLAC",                            FileCategory.Audio, "flac", "flac"),
            new FormatOption("ogg",  "Ogg Vorbis / Opus",               FileCategory.Audio, "ogg",  "ogg oga opus"),
            new FormatOption("wma",  "Windows Media Audio (WMA)",       FileCategory.Audio, "asf",  "wma"),
            new FormatOption("aif",  "AIFF / CAF",                      FileCategory.Audio, "aif caf", "aif aiff aifc caf"),
            new FormatOption("mid",  "MIDI",                            FileCategory.Audio, "mid",  "mid midi"),
            new FormatOption("amr",  "Voice recordings (AMR), APE, AU, RealAudio", FileCategory.Audio, "amr ape au ra", "amr ape au ra"),

            new FormatOption("ooxml","Word / Excel / PowerPoint (2007 and later)", FileCategory.Documents, "zip", "docx docm xlsx xlsm pptx pptm ppsx dotx vsdx"),
            new FormatOption("ole",  "Word / Excel / PowerPoint (97-2003)", FileCategory.Documents, "doc", "doc xls ppt pps dot xlt vsd pub"),
            new FormatOption("pdf",  "PDF",                             FileCategory.Documents, "pdf",  "pdf"),
            new FormatOption("odf",  "OpenDocument / LibreOffice, EPUB", FileCategory.Documents, "zip", "odt ods odp odg epub"),
            new FormatOption("text", "Text, CSV, HTML, XML, RTF",       FileCategory.Documents, "txt tx?", "txt text csv html htm xml rtf md json log ini"),
            new FormatOption("db",   "Databases (Access, SQLite, QuickBooks)", FileCategory.Documents, "mdb accdb sqlite qbb", "mdb accdb sqlite db qbb qbw"),
            new FormatOption("one",  "OneNote, CHM help, DjVu, PostScript", FileCategory.Documents, "one chm djv ps", "one chm djv djvu ps eps"),
            new FormatOption("font", "Fonts, eBooks (LIT)",             FileCategory.Documents, "ttf lit", "ttf otf lit"),

            new FormatOption("pst",  "Outlook PST / OST",               FileCategory.Email, "pst",  "pst ost"),
            new FormatOption("mbox", "Mailboxes (MBOX: Thunderbird, Apple Mail)", FileCategory.Email, "tx?", "mbox"),
            new FormatOption("nsf",  "Lotus Notes (NSF)",               FileCategory.Email, "nsf",  "nsf"),
            new FormatOption("nk2",  "Outlook autocomplete (NK2)",      FileCategory.Email, "nk2",  "nk2"),

            new FormatOption("zip",  "ZIP",                             FileCategory.Archives, "zip", "zip"),
            new FormatOption("rar",  "RAR",                             FileCategory.Archives, "rar", "rar"),
            new FormatOption("7z",   "7-Zip",                           FileCategory.Archives, "7z",  "7z"),
            new FormatOption("gz",   "GZip / BZip2 / TAR",              FileCategory.Archives, "gz bz2", "gz tgz bz2 tbz tar"),
            new FormatOption("iso",  "ISO disk images",                 FileCategory.Archives, "iso", "iso"),
            new FormatOption("cab",  "CAB, LZH, ARJ, ACE",              FileCategory.Archives, "cab lzh arj ace", "cab lzh lha arj ace"),

            new FormatOption("exe",  "Programs (EXE / DLL)",            FileCategory.Other, "exe", "exe dll sys"),
            new FormatOption("dev",  "Linux programs, Java, Python",    FileCategory.Other, "elf class pyc", "elf so class pyc"),
            new FormatOption("keys", "Keys and certificates",           FileCategory.Other, "key pfx", "key pfx p12"),
            new FormatOption("win",  "Windows logs, registry, shortcuts", FileCategory.Other, "evt reg lnk edb", "evt evtx reg lnk edb"),
            new FormatOption("misc", "dBase, SQM and other formats",    FileCategory.Other, "dbf sqm", "dbf sqm"),
        };

        public static readonly Dictionary<string, FormatOption> FormatsById = Formats.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        static FileTypes()
        {
            Add(FileCategory.Pictures, "jpg jpeg jpe jfif png gif bmp dib tif tiff psd webp heic heif raf cr2 cr3 crw nef nrw orf rw2 x3f mrw dng arw srf sr2 pef ico svg xcf exr jp2 pcx tga wmf emf");
            Add(FileCategory.Video, "mov mp4 m4v 3gp 3g2 mpg mpeg mkv webm avi flv f4v asf wmv mts m2ts ts rm rmvb swf vob ogv");
            Add(FileCategory.Audio, "mp3 flac wav ogg oga opus mid midi aif aiff amr ape au m4a m4p aac wma ra voc caf dsf wv");
            Add(FileCategory.Email, "pst ost dbx mbox eml emlx msg nsf nk2 olm");
            Add(FileCategory.Documents, "doc docx docm dot dotx xls xlsx xlsm xlt ppt pptx pps ppsx pdf rtf txt text csv xml html htm odt ods odp odg wpd one chm djv djvu ps lit qbb qbw pub vsd vsdx sqlite db accdb mdb ttf otf eps epub md json log ini");
            Add(FileCategory.Archives, "zip rar 7z gz tgz bz2 tbz xz txz tar cab lzh lha arj ace dmg iso img vmdk vdi vhd vhdx z zst");
            // every extension in the format picker belongs to its format's category
            foreach (var f in Formats)
                foreach (var e in f.Extensions)
                    if (!map.ContainsKey(e)) map[e] = f.Category;
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
