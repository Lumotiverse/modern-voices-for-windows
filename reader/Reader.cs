// Modern Voices: Reader - reads text aloud with the voices Windows has (the Modern Voices first):
// pasted or copied text, with pause, a place it remembers, and a list of texts to read later.
// Part of Modern Voices for Windows. GPL-3.0-or-later.
//
// Written for the C# compiler that is part of Windows' .NET Framework 4 (C# 5), so that it builds
// with nothing installed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Modern Voices: Reader")]
[assembly: System.Reflection.AssemblyProduct("Modern Voices for Windows")]
[assembly: System.Reflection.AssemblyVersion("0.17.0.0")]

namespace ModernVoicesReader
{
    // ---------------------------------------------------------------- colours (light and dark)

    public class Palette
    {
        public bool Dark;
        public Color Back, Raised, Input, Text, Muted, Border, Accent;
    }

    public static class Theme
    {
        // Shared with Modern Voices Config: light or dark, as last chosen with the moon in Modern Voices Config (dark until then).
        const string OurKey = @"Software\Modern Voices for Windows";

        public static string Choice
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OurKey))
                    {
                        string value = key == null ? null : key.GetValue("Theme") as string;
                        if (value == "light" || value == "dark") { return value; }
                    }
                }
                catch (Exception) { }
                return "auto";
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(OurKey)) { key.SetValue("Theme", value); }
                }
                catch (Exception) { }
            }
        }

        // Windows' own choice for apps (Settings > Personalisation > Colours).
        public static bool WindowsIsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch (Exception) { return false; }
        }

        public static bool IsDark()
        {
            return Choice != "light";      // dark until light is chosen
        }

        public static Palette Get(bool dark)
        {
            Palette p = new Palette();
            p.Dark = dark;
            if (dark)
            {
                p.Back = Color.FromArgb(32, 32, 32); p.Raised = Color.FromArgb(51, 51, 51); p.Input = Color.FromArgb(24, 24, 24);
                p.Text = Color.FromArgb(240, 240, 240); p.Muted = Color.FromArgb(160, 160, 160);
                p.Border = Color.FromArgb(90, 90, 90); p.Accent = Color.FromArgb(96, 165, 250);
            }
            else
            {
                p.Back = SystemColors.Control; p.Raised = SystemColors.Control; p.Input = SystemColors.Window;
                p.Text = SystemColors.ControlText; p.Muted = Color.DimGray;
                p.Border = SystemColors.ControlDark; p.Accent = Color.FromArgb(0, 102, 204);
            }
            return p;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void Apply(Form form, bool dark)
        {
            Palette p = Get(dark);
            form.BackColor = p.Back;
            form.ForeColor = p.Text;
            try
            {
                int on = dark ? 1 : 0;
                // The title bar (Windows 10 2004 and later: 20; before: 19).
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) { DwmSetWindowAttribute(form.Handle, 19, ref on, 4); }
            }
            catch (Exception) { }
            foreach (Control c in form.Controls) { ApplyTo(c, p); }
            form.Invalidate(true);
        }

        static void ApplyTo(Control c, Palette p)
        {
            if (c is Button)
            {
                Button b = (Button)c;
                if (p.Dark)
                {
                    b.FlatStyle = FlatStyle.Flat; b.BackColor = p.Raised; b.ForeColor = p.Text;
                    b.FlatAppearance.BorderColor = p.Border;
                }
                else { b.FlatStyle = FlatStyle.Standard; b.ForeColor = p.Text; b.UseVisualStyleBackColor = true; }
            }
            else if (c is TextBox || c is ListBox)
            {
                c.BackColor = p.Input; c.ForeColor = p.Text;
                Scrollbars(c, p.Dark);
            }
            else if (c is ComboBox)
            {
                ((ComboBox)c).FlatStyle = p.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                c.BackColor = p.Input; c.ForeColor = p.Text;
            }
            else if (c is Label) { c.ForeColor = (c.Tag as string) == "muted" ? p.Muted : p.Text; }
            else if (c is CheckBox) { c.ForeColor = p.Text; }
            else if (c is TrackBar) { c.BackColor = p.Back; }
            else { c.BackColor = p.Back; c.ForeColor = p.Text; }
            foreach (Control child in c.Controls) { ApplyTo(child, p); }
        }

        static void Scrollbars(Control c, bool dark)
        {
            try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); } catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------- text made ready to read

    // The text to speak, and for each of its characters where it came from in the original (so the
    // place being read can be shown, and remembered, in the original).
    public class Prepared
    {
        public string Text;
        public int[] Map;

        public int Original(int index)
        {
            if (Map.Length == 0) { return 0; }
            if (index < 0) { index = 0; }
            if (index >= Map.Length) { index = Map.Length - 1; }
            return Map[index];
        }

        // The first character that came from the original at or after a place in it.
        public int FromOriginal(int original)
        {
            int low = 0, high = Map.Length;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (Map[mid] < original) { low = mid + 1; } else { high = mid; }
            }
            return low;
        }
    }

    public static class Cleaner
    {
        class Edit { public int Start, Length; public string With; }

        static void Find(List<Edit> edits, string text, string pattern, string with, int keepGroup)
        {
            foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Multiline))
            {
                if (keepGroup > 0)
                {
                    // Keep one part of the match: remove what is before it and after it.
                    Group keep = m.Groups[keepGroup];
                    edits.Add(new Edit { Start = m.Index, Length = keep.Index - m.Index, With = "" });
                    edits.Add(new Edit { Start = keep.Index + keep.Length, Length = m.Index + m.Length - keep.Index - keep.Length, With = "" });
                }
                else { edits.Add(new Edit { Start = m.Index, Length = m.Length, With = with }); }
            }
        }

        // With tidy on: links, code and the symbols of formatted text (markdown) are left out, so
        // that they are not read aloud ("asterisk asterisk", addresses letter by letter).
        public static Prepared Prepare(string text, bool tidy)
        {
            List<Edit> edits = new List<Edit>();
            if (tidy)
            {
                Find(edits, text, @"^[ \t]*(```|~~~)[^\n]*\n[\s\S]*?^[ \t]*\1[ \t]*\r?$", " Code left out. ", 0);   // blocks of code
                Find(edits, text, @"!?\[([^\]\n]+)\]\([^)\s]+\)", null, 1);                                       // [words](address)
                Find(edits, text, @"\b(?:https?://|www\.)[^\s<>""')\]]+", "link", 0);                             // addresses
                Find(edits, text, @"^[ \t]*(?:[-*_=][ \t]*){3,}$", "", 0);                                        // rules: --- ***
                Find(edits, text, @"^[ \t]*\|?[ \t]*:?-{2,}:?[ \t]*(?:\|[ \t]*:?-{2,}:?[ \t]*)*\|?[ \t]*\r?$", "", 0);  // a table's ruled line
                Find(edits, text, @"^[ \t]{0,3}#{1,6}[ \t]+", "", 0);                                             // # headings
                Find(edits, text, @"^[ \t]*>[ \t]?", "", 0);                                                      // > quotes
                Find(edits, text, @"^[ \t]*[-*+\u2022\u25E6\u25AA\u2023][ \t]+(?:\[[ xX]\][ \t]+)?", "", 0);                              // - bullets, - [ ] boxes
                Find(edits, text, @"\*\*|__|~~|`+", "", 0);                                                       // **bold** `code`
                Find(edits, text, @"(?<![\w*])[*_](?=\S)|(?<=\S)[*_](?![\w*])", "", 0);                           // *slanted*
                Find(edits, text, @"[ \t]*\|[ \t]*", ", ", 0);                                                    // table columns
            }
            edits.Sort(delegate(Edit a, Edit b) { return a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length); });
            StringBuilder made = new StringBuilder(text.Length);
            List<int> map = new List<int>(text.Length);
            int at = 0;
            foreach (Edit e in edits)
            {
                if (e.Start < at) { continue; }   // inside one already made
                for (int i = at; i < e.Start; i++) { made.Append(text[i]); map.Add(i); }
                if (e.With != null) { foreach (char ch in e.With) { made.Append(ch); map.Add(e.Start); } }
                at = e.Start + e.Length;
            }
            for (int i = at; i < text.Length; i++) { made.Append(text[i]); map.Add(i); }
            Prepared p = new Prepared();
            p.Text = made.ToString();
            p.Map = map.ToArray();
            return p;
        }

        // Where the word at a place starts (reading goes from the word clicked): the place itself at a
        // word's start or in a gap before one (the next word's start then), else back to the word's start.
        public static int WordStart(string text, int position)
        {
            if (position >= text.Length) { return text.Length; }
            if (position < 0) { position = 0; }
            int at = position;
            if (char.IsWhiteSpace(text[at]))
            {
                while (at < text.Length && char.IsWhiteSpace(text[at])) { at++; }
                return at;
            }
            while (at > 0 && !char.IsWhiteSpace(text[at - 1])) { at--; }
            return at;
        }

        // Where the sentence around a place starts (reading goes on from the start of a sentence).
        public static int SentenceStart(string text, int position)
        {
            if (position > text.Length) { position = text.Length; }
            int i = position - 1;
            // A place at the very start of a sentence stays there.
            while (i >= 0 && char.IsWhiteSpace(text[i])) { i--; }
            for (; i >= 0; i--)
            {
                char ch = text[i];
                if (ch == '\n') { break; }
                if ((ch == '.' || ch == '!' || ch == '?') && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])) { break; }
            }
            int start = i + 1;
            while (start < text.Length && start < position && char.IsWhiteSpace(text[start])) { start++; }
            return start;
        }
    }

    // ---------------------------------------------------------------- text from files

    public static class Files
    {
        public const string Filter = "Text and documents (*.txt, *.md, *.docx, *.pdf, *.rtf, *.html)|*.txt;*.md;*.markdown;*.text;*.log;*.docx;*.pdf;*.rtf;*.html;*.htm|All files (*.*)|*.*";

        // The text of a file, by its kind: Word documents (.docx), PDFs, rich text (.rtf), web pages
        // (.html); anything else is read as plain text.
        public static string Read(string path)
        {
            string kind = Path.GetExtension(path).ToLowerInvariant();
            // (A PDF is mostly pictures and fonts: it may be far larger than its text.)
            if (kind == ".pdf")
            {
                if (new FileInfo(path).Length > 300L * 1024 * 1024) { throw new InvalidOperationException("the file is larger than 300 MB"); }
                return Pdf.Read(path);
            }
            if (new FileInfo(path).Length > 30 * 1024 * 1024) { throw new InvalidOperationException("the file is larger than 30 MB"); }
            if (kind == ".docx") { return FromWord(path); }
            if (kind == ".rtf")
            {
                using (RichTextBox rich = new RichTextBox()) { rich.Rtf = Plain(path); return rich.Text; }
            }
            if (kind == ".html" || kind == ".htm") { return Html.ToText(Plain(path)); }
            if (kind == ".doc" || kind == ".odt" || kind == ".epub")
            {
                throw new InvalidOperationException("files of this kind (" + kind + ") can't be read: copy the text and press Paste and read");
            }
            return Plain(path);
        }

        // Plain text, whichever way it is stored: as its first bytes say, else UTF-8, else (not
        // valid UTF-8) Windows' own code page.
        public static string Plain(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3); }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); }
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { return Encoding.Default.GetString(bytes); }
        }

        // A Word document is a zip with its text in word/document.xml: paragraphs (w:p) of runs of
        // text (w:t), with tabs and line breaks between.
        public static string FromWord(string path)
        {
            StringBuilder text = new StringBuilder();
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry entry = zip.GetEntry("word/document.xml");
                if (entry == null) { throw new InvalidOperationException("it is not a Word document (no word/document.xml in it)"); }
                using (Stream stream = entry.Open())
                using (XmlReader xml = XmlReader.Create(stream))
                {
                    bool inText = false;
                    while (xml.Read())
                    {
                        if (xml.NodeType == XmlNodeType.Element)
                        {
                            if (xml.LocalName == "t") { inText = !xml.IsEmptyElement; }
                            else if (xml.LocalName == "tab") { text.Append(' '); }
                            else if (xml.LocalName == "br" || xml.LocalName == "cr") { text.Append("\r\n"); }
                        }
                        else if (xml.NodeType == XmlNodeType.EndElement)
                        {
                            if (xml.LocalName == "t") { inText = false; }
                            else if (xml.LocalName == "p") { text.Append("\r\n"); }
                        }
                        else if (inText && (xml.NodeType == XmlNodeType.Text || xml.NodeType == XmlNodeType.Whitespace || xml.NodeType == XmlNodeType.SignificantWhitespace))
                        {
                            text.Append(xml.Value);
                        }
                    }
                }
            }
            return text.ToString();
        }

        public static string SafeName(string title)
        {
            string name = Regex.Replace(Regex.Replace(title, @"[\\/:*?""<>|\r\n\t]+", " "), @"\s+", " ").Trim().TrimEnd('.');
            if (name.Length > 60) { name = name.Substring(0, 60).Trim(); }
            return name.Length == 0 ? "Reading" : name;
        }
    }

    // ---------------------------------------------------------------- text from PDFs

    // A PDF's text, read with PdfPig (https://github.com/UglyToad/PdfPig, Apache 2.0): its DLLs lie beside
    // this program and are loaded the first time a PDF is opened (by name, so the Reader is built and
    // starts without them). A PDF stores letters at places on a page, not sentences: PdfPig gives a
    // page's lines in the order they were written, and Tidy makes paragraphs of them.
    public static class Pdf
    {
        public static string Library { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "UglyToad.PdfPig.dll"); } }
        public static bool Available
        {
            get { return File.Exists(Library) && File.Exists(Path.Combine(Path.GetDirectoryName(Library), "UglyToad.PdfPig.DocumentLayoutAnalysis.dll")); }
        }

        // Each page's text as PdfPig gives it: a line per line, an empty line where the gap is a paragraph's.
        public static List<string> Pages(string path)
        {
            List<HashSet<string>> marks;
            return Pages(path, out marks);
        }

        // 'marks': each page's letters drawn in a font of symbols (Wingdings, Symbol, Dingbats): a list's
        // bullet is often a letter of one of these (Wingdings' "l" is a round bullet, its "\u00D8" an arrowhead).
        public static List<string> Pages(string path, out List<HashSet<string>> marks)
        {
            marks = new List<HashSet<string>>();
            if (!Available) { throw new InvalidOperationException("the PDF reader is missing (" + Path.GetFileName(Library) + "): install Modern Voices again"); }
            List<string> pages = new List<string>();
            try
            {
                System.Reflection.Assembly main = System.Reflection.Assembly.LoadFrom(Library);
                System.Reflection.Assembly layout = System.Reflection.Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Library), "UglyToad.PdfPig.DocumentLayoutAnalysis.dll"));
                Type document = main.GetType("UglyToad.PdfPig.PdfDocument", true);
                Type page = main.GetType("UglyToad.PdfPig.Content.Page", true);
                Type options = main.GetType("UglyToad.PdfPig.ParsingOptions", true);
                Type extractor = layout.GetType("UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor", true);
                System.Reflection.MethodInfo open = document.GetMethod("Open", new Type[] { typeof(string), options });
                System.Reflection.MethodInfo getPage = document.GetMethod("GetPage", new Type[] { typeof(int) });
                System.Reflection.MethodInfo getText = extractor.GetMethod("GetText", new Type[] { page, typeof(bool) });
                if (open == null || getPage == null || getText == null) { throw new InvalidOperationException("the PDF reader is not the one expected"); }
                object opened = open.Invoke(null, new object[] { path, null });
                try
                {
                    int count = (int)document.GetProperty("NumberOfPages").GetValue(opened, null);
                    for (int i = 1; i <= count; i++)
                    {
                        // (A page that can't be read is left out: the rest is still worth reading.)
                        object read = null;
                        try
                        {
                            read = getPage.Invoke(opened, new object[] { i });
                            pages.Add((string)getText.Invoke(null, new object[] { read, true }) ?? "");
                        }
                        catch (System.Reflection.TargetInvocationException) { pages.Add(""); }
                        marks.Add(SymbolLetters(read, page));
                    }
                }
                finally { IDisposable closing = opened as IDisposable; if (closing != null) { closing.Dispose(); } }
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                if (inner.GetType().Name.IndexOf("Encrypted", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException("it is locked with a password");
                }
                throw new InvalidOperationException("it could not be read as a PDF (" + inner.Message.Split('\r', '\n')[0] + ")");
            }
            return pages;
        }

        static HashSet<string> SymbolLetters(object read, Type page)
        {
            HashSet<string> found = new HashSet<string>();
            if (read == null) { return found; }
            try
            {
                System.Collections.IEnumerable letters = page.GetProperty("Letters").GetValue(read, null) as System.Collections.IEnumerable;
                if (letters == null) { return found; }
                System.Reflection.PropertyInfo value = null, font = null;
                foreach (object letter in letters)
                {
                    if (value == null) { value = letter.GetType().GetProperty("Value"); font = letter.GetType().GetProperty("FontName"); }
                    if (value == null || font == null) { break; }
                    string name = (font.GetValue(letter, null) as string ?? "").ToLowerInvariant();
                    string text = (value.GetValue(letter, null) as string ?? "").Trim();
                    if (text.Length > 0 && (name.Contains("wingding") || name.Contains("webding") || name.Contains("dingbat") || name.Contains("symbol")))
                    {
                        found.Add(text);
                    }
                }
            }
            catch (Exception) { }
            return found;
        }

        public static string Read(string path)
        {
            List<HashSet<string>> marks;
            string text = Tidy(Pages(path, out marks), marks);
            if (text.Trim().Length == 0)
            {
                throw new InvalidOperationException("there is no text in it, only pictures of pages (a scan?). Show a page on the screen and use the magnifying glass");
            }
            return text;
        }

        static readonly Regex EndsSentence = new Regex(@"[.!?:;…][""'”’)\]]*$");

        // Paragraphs out of pages of lines. Lines of a paragraph are joined (a word broken at a line's end
        // with a hyphen is made whole); a line that ends a sentence well short of the others ends a
        // paragraph too; page numbers, and lines that head or foot most pages, are left out; a paragraph
        // that runs over a page's end goes on.
        //
        // A list's items: a line that starts with a bullet (a dot, a square, an arrowhead, a tick, a dash, a
        // letter of a font of symbols, or a sign Windows has no letter for) starts a paragraph of its own,
        // without the bullet. (Read out, a bullet is a sign's name: "play button" for an arrowhead.)
        // 'marks' (may be null): each page's letters drawn in a font of symbols (Pages).
        public static string Tidy(List<string> pages, List<HashSet<string>> marks = null)
        {
            // Each page's blocks (what stands between empty lines), each block its lines. A list item's
            // first line starts with Item (taken off when its paragraph is made).
            List<List<List<string>>> all = new List<List<List<string>>>();
            for (int pageNumber = 0; pageNumber < pages.Count; pageNumber++)
            {
                string pageText = pages[pageNumber];
                HashSet<string> symbols = marks != null && pageNumber < marks.Count ? marks[pageNumber] : null;
                List<List<string>> blocks = new List<List<string>>();
                List<string> block = new List<string>();
                bool itemNext = false;
                foreach (string rawLine in (pageText ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string line = Regex.Replace(Unmarked(rawLine.Replace('\u00A0', ' '), symbols), @"[ \t]+", " ").Trim();
                    string rest;
                    bool item = Bullet(line, symbols, out rest);
                    if (item) { line = rest; }
                    if (line.Length == 0)
                    {
                        // (A bullet on a line of its own: the item is the next line.)
                        if (item) { itemNext = true; }
                        if (block.Count > 0) { blocks.Add(block); block = new List<string>(); }
                        continue;
                    }
                    if (item || itemNext)
                    {
                        if (block.Count > 0) { blocks.Add(block); block = new List<string>(); }
                        line = Item + line;
                        itemNext = false;
                    }
                    block.Add(line);
                }
                if (block.Count > 0) { blocks.Add(block); }
                all.Add(blocks);
            }
            // A page's first and last line, where they are a page number or the same on most pages (numbers
            // apart), are a header or footer: not read.
            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach (List<List<string>> blocks in all)
            {
                foreach (string edge in Edges(blocks))
                {
                    string key = EdgeKey(edge);
                    int count;
                    seen.TryGetValue(key, out count);
                    seen[key] = count + 1;
                }
            }
            int filled = 0;
            foreach (List<List<string>> blocks in all) { if (blocks.Count > 0) { filled++; } }
            foreach (List<List<string>> blocks in all)
            {
                for (int pass = 0; pass < 2 && blocks.Count > 0; pass++)
                {
                    List<string> edgeBlock = pass == 0 ? blocks[0] : blocks[blocks.Count - 1];
                    string edge = pass == 0 ? edgeBlock[0] : edgeBlock[edgeBlock.Count - 1];
                    bool number = Regex.IsMatch(edge, @"^(page\s+)?[-–— ]*\d{1,4}[-–— ]*(\s*(of|/)\s*\d{1,4})?$", RegexOptions.IgnoreCase);
                    int times;
                    seen.TryGetValue(EdgeKey(edge), out times);
                    bool repeated = filled >= 3 && edge.Length <= 80 && times * 2 > filled;
                    if (!number && !repeated) { continue; }
                    edgeBlock.RemoveAt(pass == 0 ? 0 : edgeBlock.Count - 1);
                    if (edgeBlock.Count == 0) { blocks.Remove(edgeBlock); }
                }
            }
            // Paragraphs.
            List<string> paragraphs = new List<string>();
            bool pageStart = false;
            foreach (List<List<string>> blocks in all)
            {
                pageStart = true;
                foreach (List<string> lines in blocks)
                {
                    int longest = 0;
                    foreach (string line in lines) { longest = Math.Max(longest, line.Length); }
                    StringBuilder paragraph = new StringBuilder();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Join(paragraph, lines[i]);
                        bool last = i == lines.Count - 1;
                        // (A short last line of a paragraph set without a gap after it.)
                        bool shortEnd = !last && lines.Count >= 3 && lines[i].Length * 4 < longest * 3 && EndsSentence.IsMatch(lines[i]);
                        if (!last && !shortEnd) { continue; }
                        string made = paragraph.ToString();
                        paragraph.Length = 0;
                        bool listed = made.StartsWith(Item);
                        if (listed) { made = made.Substring(Item.Length); }
                        // The first paragraph of a page goes on from the last of the page before, where that one
                        // stopped mid-sentence and this one starts with a small letter (not a list's item).
                        if (pageStart && !listed && paragraphs.Count > 0 && made.Length > 0 && char.IsLower(made[0]) && !EndsSentence.IsMatch(paragraphs[paragraphs.Count - 1]))
                        {
                            StringBuilder joined = new StringBuilder(paragraphs[paragraphs.Count - 1]);
                            Join(joined, made);
                            paragraphs[paragraphs.Count - 1] = joined.ToString();
                        }
                        else if (made.Length > 0) { paragraphs.Add(made); }
                        pageStart = false;
                    }
                }
            }
            return string.Join("\r\n\r\n", paragraphs.ToArray());
        }

        const string Item = "\u0001";      // (marks a list item's first line, inside Tidy only)

        // Signs that stand for a list's bullet at a line's start: dots, squares, diamonds, arrowheads,
        // arrows and ticks. (Dashes and stars count only with a space after them: "-5" is a number.)
        const string BulletSigns = "\u2022\u25E6\u2023\u2043\u2219\u00B7\u25AA\u25AB\u25A0\u25A1\u25CF\u25CB\u25C6\u25C7\u25CA\u2666\u2756" +
            "\u25BA\u25B6\u25B8\u25B9\u25BB\u25B7\u27A2\u27A3\u27A4\u2794\u279C\u279D\u279E\u2192\u21D2\u21E8\u2713\u2714\u2717\u2718" +
            "\u2751\u2752\u274F\u2750\u29BE\u29BF\u2B9A\u2BC8\u25A3\u25FE\u25FD\u25AC";

        // A sign Windows has no letter for (the Private Use Area: how a symbol font's letters often come
        // out of a PDF).
        static bool Unlettered(char c) { return c >= '\uE000' && c <= '\uF8FF'; }

        public static bool Bullet(string line, HashSet<string> symbols, out string rest)
        {
            rest = line;
            if (line.Length == 0) { return false; }
            char first = line[0];
            bool spaced = line.Length == 1 || char.IsWhiteSpace(line[1]);
            bool sign = BulletSigns.IndexOf(first) >= 0 || Unlettered(first);
            // A symbol font's letter ("l", "n", "q", "\u00D8"...) or a dash or star: only with a space after it.
            bool symbol = spaced && symbols != null && symbols.Contains(first.ToString());
            bool dash = spaced && line.Length > 1 && (first == '-' || first == '\u2013' || first == '\u2014' || first == '*' || first == 'o' && symbols != null && symbols.Contains("o"));
            if (!sign && !symbol && !dash) { return false; }
            // (A dash with no words after it is not a bullet: "- 3 -" is a page number.)
            string after = line.Substring(1).TrimStart();
            if (dash && !Regex.IsMatch(after, @"\p{L}")) { return false; }
            rest = after;
            return true;
        }

        // A line without the signs of a symbol font, or with no letter, inside it (a bullet's sign at its
        // start is kept, for Bullet to find).
        static string Unmarked(string line, HashSet<string> symbols)
        {
            if (line.Length < 2) { return line; }
            StringBuilder kept = new StringBuilder(line.Length);
            int start = 0;
            while (start < line.Length && char.IsWhiteSpace(line[start])) { start++; }
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                bool symbol = symbols != null && symbols.Contains(c.ToString()) && !char.IsLetterOrDigit(c);
                if (i > start && (Unlettered(c) || symbol)) { kept.Append(' '); continue; }
                kept.Append(c);
            }
            return kept.ToString();
        }

        static List<string> Edges(List<List<string>> blocks)
        {
            List<string> edges = new List<string>();
            if (blocks.Count == 0) { return edges; }
            edges.Add(blocks[0][0]);
            List<string> end = blocks[blocks.Count - 1];
            if (blocks.Count > 1 || end.Count > 1) { edges.Add(end[end.Count - 1]); }
            return edges;
        }
        static string EdgeKey(string line) { return Regex.Replace(line.ToLowerInvariant(), @"\d+", "#"); }

        // A line added to those before it: after a space, or straight on where the line before broke a word
        // with a hyphen ("hyphen-" + "ated": hyphenated; "well-" + "Known" keeps its hyphen).
        static void Join(StringBuilder text, string line)
        {
            if (text.Length == 0) { text.Append(line); return; }
            int end = text.Length - 1;
            bool hyphen = (text[end] == '-' || text[end] == '­' || text[end] == '‐') && end > 0 && char.IsLetter(text[end - 1]);
            if (hyphen && line.Length > 0 && char.IsLower(line[0])) { text.Length = end; text.Append(line); }
            else if (hyphen) { text.Append(line); }
            else { text.Append(' ').Append(line); }
        }
    }

    // ---------------------------------------------------------------- sound as MP3

    // A WAV file made into an MP3 by LAME (https://lame.sourceforge.io, LGPL): libmp3lame.64.dll or
    // libmp3lame.32.dll beside this program, loaded the first time an MP3 is saved.
    public static class Mp3
    {
        public const int Kbps = 64;     // (one voice, 22 kHz: more is not heard)
        public static string Library
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), IntPtr.Size == 8 ? "libmp3lame.64.dll" : "libmp3lame.32.dll"); }
        }
        public static bool Available { get { return File.Exists(Library); } }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
        static extern IntPtr GetProcAddress(IntPtr library, string name);
        static IntPtr LoadNative(string path) { return LoadLibraryW(path); }
        static IntPtr FindNative(IntPtr library, string name) { return GetProcAddress(library, name); }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr InitFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetFn(IntPtr lame, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GoFn(IntPtr lame);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EncodeFn(IntPtr lame, short[] left, short[] right, int samples, [In, Out] byte[] into, int room);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FlushFn(IntPtr lame, [In, Out] byte[] into, int room);

        static IntPtr library = IntPtr.Zero;
        static readonly Dictionary<string, Delegate> functions = new Dictionary<string, Delegate>();
        static Delegate Function(string name, Type kind)
        {
            lock (functions)
            {
                Delegate found;
                if (functions.TryGetValue(name, out found)) { return found; }
                if (library == IntPtr.Zero)
                {
                    library = LoadNative(Library);
                    if (library == IntPtr.Zero) { throw new InvalidOperationException("the MP3 encoder could not be loaded (" + Path.GetFileName(Library) + ")"); }
                }
                IntPtr address = FindNative(library, name);
                if (address == IntPtr.Zero) { throw new InvalidOperationException("the MP3 encoder is not the one expected (no " + name + ")"); }
                found = Marshal.GetDelegateForFunctionPointer(address, kind);
                functions[name] = found;
                return found;
            }
        }
        static void Set(IntPtr lame, string name, int value)
        {
            if (((SetFn)Function(name, typeof(SetFn)))(lame, value) < 0) { throw new InvalidOperationException("the MP3 encoder refused " + name + " " + value); }
        }

        // Where a WAV file's sound is, and what it is: 16-bit, one or two channels.
        public static void WavInfo(Stream wav, out int rate, out int channels, out long start, out long length)
        {
            rate = 0; channels = 0; start = 0; length = 0;
            byte[] head = new byte[12];
            if (wav.Read(head, 0, 12) < 12 || Encoding.ASCII.GetString(head, 0, 4) != "RIFF" || Encoding.ASCII.GetString(head, 8, 4) != "WAVE")
            {
                throw new InvalidOperationException("the sound made is not a WAV file");
            }
            byte[] chunk = new byte[8];
            int bits = 0;
            while (wav.Read(chunk, 0, 8) == 8)
            {
                string id = Encoding.ASCII.GetString(chunk, 0, 4);
                long size = BitConverter.ToUInt32(chunk, 4);
                if (id == "fmt ")
                {
                    byte[] format = new byte[16];
                    if (size < 16 || wav.Read(format, 0, 16) < 16) { break; }
                    channels = BitConverter.ToUInt16(format, 2);
                    rate = BitConverter.ToInt32(format, 4);
                    bits = BitConverter.ToUInt16(format, 14);
                    if (BitConverter.ToUInt16(format, 0) != 1 || bits != 16 || channels < 1 || channels > 2) { throw new InvalidOperationException("the sound made is not 16-bit PCM"); }
                    wav.Seek(size - 16 + (size & 1), SeekOrigin.Current);
                }
                else if (id == "data")
                {
                    if (rate == 0) { break; }
                    start = wav.Position;
                    // (A length that was never filled in, or runs past the file's end: what is there.)
                    long there = wav.Length - start;
                    length = size == 0 || size > there ? there : size;
                    return;
                }
                else { wav.Seek(size + (size & 1), SeekOrigin.Current); }
            }
            throw new InvalidOperationException("the sound made has no sound in it");
        }

        // 'stop' is asked between pieces (true: given up, with an OperationCanceledException); 'progress' is
        // told how far it is, 0 to 100. Either may be null.
        public static void Encode(string wavPath, string mp3Path, Func<bool> stop, Action<int> progress)
        {
            using (FileStream wav = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                int rate, channels;
                long start, length;
                WavInfo(wav, out rate, out channels, out start, out length);
                wav.Seek(start, SeekOrigin.Begin);
                IntPtr lame = ((InitFn)Function("lame_init", typeof(InitFn)))();
                if (lame == IntPtr.Zero) { throw new InvalidOperationException("the MP3 encoder did not start"); }
                try
                {
                    Set(lame, "lame_set_in_samplerate", rate);
                    Set(lame, "lame_set_out_samplerate", rate);
                    Set(lame, "lame_set_num_channels", channels);
                    Set(lame, "lame_set_mode", channels == 1 ? 3 : 1);      // mono, or joint stereo
                    Set(lame, "lame_set_brate", channels == 1 ? Kbps : Kbps * 2);
                    Set(lame, "lame_set_quality", 3);
                    Set(lame, "lame_set_bWriteVbrTag", 0);     // (a constant rate: players work its length out from its size)
                    if (((GoFn)Function("lame_init_params", typeof(GoFn)))(lame) < 0) { throw new InvalidOperationException("the MP3 encoder refused the sound's format (" + rate + " Hz)"); }
                    EncodeFn encode = (EncodeFn)Function("lame_encode_buffer", typeof(EncodeFn));
                    FlushFn flush = (FlushFn)Function("lame_encode_flush", typeof(FlushFn));
                    const int Piece = 8192;
                    byte[] raw = new byte[Piece * 2 * channels];
                    short[] left = new short[Piece], right = channels == 2 ? new short[Piece] : left;
                    byte[] made = new byte[Piece * 5 / 4 + 7200];
                    long done = 0;
                    int said = -1;
                    using (FileStream mp3 = new FileStream(mp3Path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        while (done < length)
                        {
                            if (stop != null && stop()) { throw new OperationCanceledException(); }
                            int want = (int)Math.Min(raw.Length, length - done), got = 0;
                            while (got < want)
                            {
                                int read = wav.Read(raw, got, want - got);
                                if (read <= 0) { break; }
                                got += read;
                            }
                            int samples = got / (2 * channels);
                            if (samples == 0) { break; }
                            for (int i = 0; i < samples; i++)
                            {
                                left[i] = (short)(raw[i * 2 * channels] | (raw[i * 2 * channels + 1] << 8));
                                if (channels == 2) { right[i] = (short)(raw[i * 4 + 2] | (raw[i * 4 + 3] << 8)); }
                            }
                            int bytes = encode(lame, left, right, samples, made, made.Length);
                            if (bytes < 0) { throw new InvalidOperationException("the MP3 encoder failed (" + bytes + ")"); }
                            mp3.Write(made, 0, bytes);
                            done += got;
                            int percent = (int)(100 * done / Math.Max(1, length));
                            if (progress != null && percent != said) { said = percent; progress(percent); }
                            if (got < want) { break; }
                        }
                        int rest = flush(lame, made, made.Length);
                        if (rest > 0) { mp3.Write(made, 0, rest); }
                    }
                }
                finally { ((GoFn)Function("lame_close", typeof(GoFn)))(lame); }
            }
        }
    }

    // ---------------------------------------------------------------- text from web pages and formatted copies

    public static class Html
    {
        // What was copied, out of the wrapping the clipboard puts around it.
        public static string Fragment(string copied)
        {
            const string start = "<!--StartFragment-->", end = "<!--EndFragment-->";
            int a = copied.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            int b = copied.LastIndexOf(end, StringComparison.OrdinalIgnoreCase);
            if (a >= 0 && b > a) { return copied.Substring(a + start.Length, b - a - start.Length); }
            int first = copied.IndexOf('<');
            return first > 0 ? copied.Substring(first) : copied;
        }

        // The clipboard's formatted copy from its bytes: UTF-8, up to the zero that ends it.
        public static string FromBytes(byte[] bytes)
        {
            int length = Array.IndexOf(bytes, (byte)0);
            return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
        }

        // UTF-8 that was read in Windows' code page, read again as UTF-8 (left alone where that does not work out).
        public static string Utf8Again(string misread)
        {
            try
            {
                Encoding ansi = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                return new UTF8Encoding(false, true).GetString(ansi.GetBytes(misread));
            }
            catch (Exception) { return misread; }
        }

        // At least so many line ends at the end of what is made so far (none at its very start).
        static void Break(StringBuilder made, int lines)
        {
            while (made.Length > 0 && (made[made.Length - 1] == ' ' || made[made.Length - 1] == '\t')) { made.Length--; }
            if (made.Length == 0) { return; }
            int have = 0;
            while (have < made.Length && made[made.Length - 1 - have] == '\n') { have++; }
            for (; have < lines; have++) { made.Append('\n'); }
        }

        // A page's words laid out as plain text: an empty line between paragraphs, a line for each
        // item of a list with its bullet or number (indented when the list is inside another), a line
        // break where there is one; scripts, styles and the like left out.
        public static string ToText(string html)
        {
            html = Regex.Replace(html, @"<(script|style|head|noscript|svg|button|select)\b[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
            StringBuilder made = new StringBuilder();
            List<int> lists = new List<int>();     // for each list open: its next number, or 0 for bullets
            int keepLines = 0;                     // inside <pre>: line ends are kept
            bool afterMark = false;                // just after a bullet or number: its words come on the same line
            foreach (string token in Regex.Split(html, @"(<[^>]*>)"))
            {
                if (token.Length == 0) { continue; }
                if (token[0] == '<' && token[token.Length - 1] == '>')
                {
                    Match tag = Regex.Match(token, @"^<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)");
                    if (!tag.Success) { continue; }
                    bool closing = tag.Groups[1].Value == "/";
                    string name = tag.Groups[2].Value.ToLowerInvariant();
                    int inList = lists.Count > 0 ? 1 : 2;      // paragraphs in a list: no empty lines between
                    if (name == "br") { Break(made, 0); made.Append('\n'); }
                    else if (name == "pre") { keepLines = Math.Max(0, keepLines + (closing ? -1 : 1)); Break(made, inList); }
                    else if (name == "ul" || name == "ol")
                    {
                        if (!closing) { Break(made, inList); lists.Add(name == "ol" ? 1 : 0); }
                        else
                        {
                            if (lists.Count > 0) { lists.RemoveAt(lists.Count - 1); }
                            Break(made, lists.Count > 0 ? 1 : 2);
                        }
                    }
                    else if (name == "li")
                    {
                        if (closing) { continue; }
                        Break(made, 1);
                        made.Append(' ', 4 * Math.Max(0, lists.Count - 1));
                        if (lists.Count > 0 && lists[lists.Count - 1] > 0)
                        {
                            made.Append(lists[lists.Count - 1].ToString(CultureInfo.InvariantCulture)).Append(". ");
                            lists[lists.Count - 1]++;
                        }
                        else { made.Append("\u2022 "); }
                        afterMark = true;
                    }
                    else if (name == "p" || name == "blockquote" || name == "table" || (name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6'))
                    {
                        if (!afterMark) { Break(made, inList); }
                    }
                    else if (name == "div" || name == "tr" || name == "section" || name == "article" || name == "header" || name == "footer" ||
                             name == "dt" || name == "dd" || name == "figcaption" || name == "title")
                    {
                        if (!afterMark) { Break(made, 1); }
                    }
                    else if ((name == "td" || name == "th") && closing) { made.Append('\t'); }
                    continue;
                }
                string words = WebUtility.HtmlDecode(token);
                if (keepLines > 0) { words = words.Replace("\r\n", "\n").Replace('\r', '\n'); }
                else
                {
                    words = Regex.Replace(words, @"[ \t\r\n]+", " ");
                    if (afterMark || made.Length == 0 || made[made.Length - 1] == '\n' || made[made.Length - 1] == ' ') { words = words.TrimStart(' '); }
                }
                if (words.Length == 0) { continue; }
                made.Append(words);
                if (words.Trim().Length > 0) { afterMark = false; }
            }
            string text = Regex.Replace(made.ToString(), @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim('\n', ' ', '\t').Replace("\n", "\r\n");
        }
    }

    // ---------------------------------------------------------------- text from the screen

    // Windows' own text recognition, through the helper script installed with the program (it
    // answers in a moment or two, in a process of its own).
    public static class Ocr
    {
        public static string Script
        {
            get { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), @"..\installer\Ocr.ps1")); }
        }

        public static Process Prepare(string image, string output)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            info.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Script + "\" -Image \"" + image + "\" -Out \"" + output + "\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            Process process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            return process;
        }

        // The part of a picture to read, twice the size when it is small (small print is read better).
        public static Bitmap Enlarged(Bitmap whole, Rectangle part)
        {
            int times = (part.Height < 1400 && part.Width * 2 <= 5000) ? 2 : 1;
            Bitmap made = new Bitmap(part.Width * times, part.Height * times);
            using (Graphics g = Graphics.FromImage(made))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(whole, new Rectangle(0, 0, made.Width, made.Height), part, GraphicsUnit.Pixel);
            }
            return made;
        }
    }

    // The whole screen as it was, dimmed, to drag a box on: what is in the box is read.
    public class RegionPicker : Form
    {
        readonly Bitmap shot;
        Point corner, other;
        bool dragging;
        public Rectangle Picked;

        public RegionPicker(Bitmap shot, Rectangle screen)
        {
            this.shot = shot;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Font = new Font("Segoe UI", 12f);
        }

        Rectangle Chosen()
        {
            return Rectangle.FromLTRB(Math.Min(corner.X, other.X), Math.Min(corner.Y, other.Y), Math.Max(corner.X, other.X), Math.Max(corner.Y, other.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(shot, 0, 0);
            Rectangle chosen = Chosen();
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            using (Region outside = new Region(ClientRectangle))
            {
                if (dragging && chosen.Width > 0 && chosen.Height > 0) { outside.Exclude(chosen); }
                e.Graphics.FillRegion(dim, outside);
            }
            if (dragging && chosen.Width > 0 && chosen.Height > 0)
            {
                using (Pen edge = new Pen(Color.FromArgb(96, 165, 250), 2f)) { e.Graphics.DrawRectangle(edge, chosen); }
            }
            else
            {
                string hint = "Drag a box around the text to read aloud.   Esc cancels.";
                Size size = TextRenderer.MeasureText(hint, Font);
                Rectangle primary = Screen.PrimaryScreen.Bounds;
                Rectangle panel = new Rectangle(primary.X - Left + (primary.Width - size.Width) / 2 - 16, primary.Y - Top + 40, size.Width + 32, size.Height + 20);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(230, 32, 32, 32))) { e.Graphics.FillRectangle(back, panel); }
                TextRenderer.DrawText(e.Graphics, hint, Font, panel, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { DialogResult = DialogResult.Cancel; Close(); return; }
            corner = e.Location; other = e.Location; dragging = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) { other = e.Location; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) { return; }
            other = e.Location;
            Picked = Chosen();
            dragging = false;
            DialogResult = (Picked.Width > 8 && Picked.Height > 8) ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        }
    }

    // ---------------------------------------------------------------- what is kept between runs

    // Where reading stopped in a file that was opened (so that opening it again goes on from there).
    public class Place
    {
        public int Position;
        public int Length;
    }

    public class State
    {
        public string Voice = "";
        public int Rate;
        public int Volume = 100;             // of the Reader's own speech (0 to 100)
        public bool AutoRead;
        public bool Tidy = true;
        public int Position;                 // in the text in the box
        public string OpenedFile = "";       // the file the box's text came from, while it is unchanged
        public bool ScreenWarningOff;        // "Do not display this warning again" was ticked (Read screen's warning)
        public bool KeepText;                // "Save text for next time?" was answered Yes as the Reader closed
        public bool SaveOnClose = true;      // "Save text on close" is ticked: closing with text asks that question
        public string AudioKind = "mp3";     // what Save as audio last made: "mp3" or "wav"
        public int[] Bounds = new int[0];
        public Dictionary<string, Place> Places = new Dictionary<string, Place>();
    }

    public class Store
    {
        public readonly string Folder;
        public State State = new State();

        public Store(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(Folder);
            try
            {
                string file = Path.Combine(Folder, "state.json");
                if (File.Exists(file))
                {
                    State loaded = new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(file, Encoding.UTF8));
                    if (loaded != null) { State = loaded; }
                }
            }
            catch (Exception) { State = new State(); }
            if (State.Places == null) { State.Places = new Dictionary<string, Place>(); }
            if (State.Bounds == null) { State.Bounds = new int[0]; }
            if (State.OpenedFile == null) { State.OpenedFile = ""; }
        }

        public void Save()
        {
            try
            {
                string file = Path.Combine(Folder, "state.json");
                File.WriteAllText(file + ".writing", new JavaScriptSerializer().Serialize(State), Encoding.UTF8);
                if (File.Exists(file)) { File.Delete(file); }
                File.Move(file + ".writing", file);
            }
            catch (Exception) { }
        }

        public string CurrentFile { get { return Path.Combine(Folder, "current.txt"); } }

        public string Read(string file)
        {
            try { return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : ""; }
            catch (Exception) { return ""; }
        }

        public void Write(string file, string text)
        {
            try { File.WriteAllText(file, text, Encoding.UTF8); } catch (Exception) { }
        }

        // The place kept for a file (0 when it has none, or the file's text is not the length it was).
        public int PlaceIn(string path, int length)
        {
            Place place;
            if (State.Places.TryGetValue(path.ToLowerInvariant(), out place) && place != null && place.Length == length && place.Position < length) { return place.Position; }
            return 0;
        }

        public void KeepPlace(string path, int position, int length)
        {
            if (State.Places.Count > 300) { State.Places.Clear(); }     // (not kept for ever)
            Place place = new Place();
            place.Position = position; place.Length = length;
            State.Places[path.ToLowerInvariant()] = place;
        }

        public static string TitleOf(string text)
        {
            string line = Regex.Replace(text.Trim(), @"\s+", " ");
            line = Regex.Replace(line, @"^[#>*\-\u2022\s]+", "");
            return line.Length > 60 ? line.Substring(0, 60).TrimEnd() + "..." : line;
        }
    }

    // The text box: what is pasted into it is laid out as it was where it was copied.
    // Undo and redo are the window's own (the box's own undo has one step, and forgets it when the
    // program changes the text): Ctrl+Z, Ctrl+Y (and Ctrl+Shift+Z), and Undo in the right-click menu.
    public class PasteBox : TextBox
    {
        public Func<string> Pasting;
        public Action UndoAsked, RedoAsked;
        public Func<bool> CanUndoAsked;

        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { if (UndoAsked != null) { UndoAsked(); } return true; }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { if (RedoAsked != null) { RedoAsked(); } return true; }
            return base.ProcessCmdKey(ref m, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0304 || m.Msg == 0x00C7)                    // WM_UNDO, EM_UNDO
            {
                if (UndoAsked != null) { UndoAsked(); }
                m.Result = (IntPtr)1;
                return;
            }
            if (m.Msg == 0x00C6 && CanUndoAsked != null)               // EM_CANUNDO (the menu's Undo: greyed or not)
            {
                m.Result = (IntPtr)(CanUndoAsked() ? 1 : 0);
                return;
            }
            if (m.Msg == 0x0302 && Pasting != null && !ReadOnly)      // WM_PASTE
            {
                string text = Pasting();
                if (text.Length > 0) { SelectedText = text; }
                return;
            }
            base.WndProc(ref m);
        }
    }

    // ---------------------------------------------------------------- a voice in the list

    public class VoiceChoice
    {
        public string Name;      // what Windows calls it (what is asked for)
        public string Shown;     // its nickname, if it has one
        public string TokenId;   // its name in Windows' list of voices (the registry)
        public string Spoken;    // the name the speech object knows it by (its name when the program started)
        public bool Ours;
        public override string ToString() { return Shown; }
    }

    // ---------------------------------------------------------------- the voice list's dots

    // A small note beside the pointer, over everything (the dropped-down list included), that never takes
    // the keyboard or the mouse from what is under it.
    public class TipWindow : Form
    {
        readonly Label words = new Label();
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int how);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        public TipWindow(string text, Font font)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = SystemColors.Info; ForeColor = SystemColors.InfoText; Font = font;
            words.AutoSize = true; words.Text = text; words.Location = new Point(5, 3);
            Controls.Add(words);
            ClientSize = new Size(words.PreferredWidth + 10, words.PreferredHeight + 6);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x00000008 | 0x00000080;     // no activation, topmost, a tool window
                return p;
            }
        }
        public void ShowAt(Point screen)
        {
            IntPtr handle = Handle;
            SetWindowPos(handle, (IntPtr)(-1), screen.X, screen.Y, 0, 0, 0x0001 | 0x0010);     // topmost; no size, no activation
            ShowWindow(handle, 4);                                                             // shown, not activated
        }
        public void HideTip() { if (IsHandleCreated && Visible) { ShowWindow(Handle, 0); } }
    }

    // The dropped-down list of voices: each row starts with a dot, filled for Windows' default voice. A
    // click on a dot makes that voice the default (the list stays open, and the voice read with stays as
    // it is); a click on a name chooses the voice, as in any list.
    public class DotList : NativeWindow
    {
        public int Zone;                     // how wide the dots' column is
        public Action<int> DotClicked;       // (the row)
        public int Clicks;                   // how many clicks on dots were taken here

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hwnd, out DotRect r);
        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr hwnd, IntPtr r, bool erase);
        [StructLayout(LayoutKind.Sequential)]
        public struct DotRect { public int Left, Top, Right, Bottom; }

        // The row whose dot the pointer is on (-1: none).
        int RowAt(IntPtr l)
        {
            int x = (short)((long)l & 0xFFFF), y = (short)(((long)l >> 16) & 0xFFFF);
            DotRect r;
            if (!GetClientRect(Handle, out r) || x < 0 || x >= Zone || y < 0 || y >= r.Bottom) { return -1; }
            long found = (long)SendMessage(Handle, 0x01A9, IntPtr.Zero, l);          // LB_ITEMFROMPOINT
            return ((found >> 16) & 0xFFFF) != 0 ? -1 : (int)(found & 0xFFFF);
        }

        public void Redraw() { if (Handle != IntPtr.Zero) { InvalidateRect(Handle, IntPtr.Zero, false); } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0201 || m.Msg == 0x0202 || m.Msg == 0x0203)                 // left button: down, up, double
            {
                int row = RowAt(m.LParam);
                if (row >= 0)
                {
                    if (m.Msg != 0x0202) { Clicks++; if (DotClicked != null) { DotClicked(row); } }
                    m.Result = IntPtr.Zero;
                    return;                                                        // (not the list's: nothing is chosen, it stays open)
                }
            }
            base.WndProc(ref m);
        }
    }

    // ---------------------------------------------------------------- the window

    public class MainForm : Form
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        const int WM_SETTINGCHANGE = 0x001A;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hwnd, int show);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern UIntPtr GlobalSize(IntPtr memory);

        readonly Store store;
        SpeechSynthesizer synth;         // (made once the window exists, so that its events come to the window's thread)
        readonly bool silent;            // the build's test: speech goes nowhere
        public readonly float Dpi;

        ComboBox voiceBox;
        TrackBar volumeBar;              // how loud the Reader speaks
        TextBox nickBox;                 // a new nickname for the voice chosen
        Button nickButton;
        Panel underPanel;                // the line under the buttons: its four things are placed by AlignLine
        readonly DotList dots = new DotList();
        TipWindow dotTip;                // "Set Windows default voice", while the pointer is on a dot
        Timer dotTimer;                  // watches the pointer while the list is down
        IntPtr voiceList;                // the dropped-down list's own window
        int voiceIndex = -1;             // the voice read with (the list's row)
        DateTime listClosedAt = DateTime.MinValue, voiceKeyAt = DateTime.MinValue;
        string voicesSeen = "";          // Windows' voices and their names, as last listed
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out DotList.DotRect r);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        string defaultToken = "";        // Windows' default voice (its name in the registry)
        const string DefaultVoiceKey = @"Software\Microsoft\Speech\Voices";
        const string TokensPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\";
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, string l);
        [DllImport("user32.dll")]
        static extern bool GetComboBoxInfo(IntPtr combo, ref ComboInfo info);
        // The volume of the Reader's own sound (its audio session, as Windows' Volume Mixer shows it). Given a
        // device number rather than an open device, this sets the whole program's session; it is heard at once,
        // mid-word, where the speech object's own Volume is taken only when reading starts.
        [DllImport("winmm.dll")]
        static extern int waveOutSetVolume(IntPtr device, uint volume);
        [DllImport("winmm.dll")]
        static extern int waveOutGetVolume(IntPtr device, out uint volume);
        void ApplyVolume()
        {
            uint level = (uint)(0xFFFF * Math.Max(0, Math.Min(100, volumeBar.Value)) / 100);
            try { waveOutSetVolume(IntPtr.Zero, level | (level << 16)); } catch (Exception) { }
        }
        // The session's volume now, 0 to 100 (-1 if Windows would not say): for the build's test.
        public int SessionVolume
        {
            get
            {
                uint v;
                try { if (waveOutGetVolume(IntPtr.Zero, out v) != 0) { return -1; } } catch (Exception) { return -1; }
                return (int)Math.Round((v & 0xFFFF) * 100.0 / 0xFFFF);
            }
        }
        public void TestVolume(int value) { volumeBar.Value = value; }
        [StructLayout(LayoutKind.Sequential)]
        struct ComboInfo
        {
            public int Size;
            public DotList.DotRect Item, Button;
            public int ButtonState;
            public IntPtr Combo, Edit, List;
        }
        Label configButton;              // a cog to click: opens Modern Voices Config
        string shownTheme;               // the theme the window wears (Config changes it: the tick follows)
        int alignedFor;                  // the time's width the line under the buttons was laid out for
        TrackBar rateBar;
        Label rateLabel, timeLabel, statusLabel;
        PasteBox box;
        Button playButton, stopButton, startButton;
        Button clipboardButton, clearButton, fileButton, audioButton, undoButton, redoButton;
        // Undo: the texts the box held before each of its last changes (newest last), and those undone (for redo).
        const int UndoSteps = 10;
        class Held { public string Text; public int Caret; }
        readonly List<Held> undone = new List<Held>(), before = new List<Held>();
        string heldText = "";            // the box's text after its last change
        int heldCaret;                   // and where the caret was then
        DateTime typedAt = DateTime.MinValue;
        Font symbolFont;                 // Windows' own symbols (play, pause, stop), where it has them
        bool symbols;
        ToolTip tips = new ToolTip();
        SpeechSynthesizer saver;         // while the text is being saved as sound
        Button screenButton;
        Process recogniser;              // while the text of a picture is being read
        public string LastRecognised = "";
        public int LastRecogniserCode = -1;
        string savingTo = "";
        string savingWav;                // an MP3 is wanted: the WAV the voice makes first (a temporary file), else null
        bool encoding;                   // that WAV is being made into the MP3 (on another thread)
        volatile bool stopEncoding;
        CheckBox autoBox, tidyBox, saveBox;
        ProgressBar progress;
        Timer tick;

        enum Mode { Stopped, Playing, Paused }
        Mode mode = Mode.Stopped;
        Prepared prepared;               // what is being spoken
        int spokenFrom;                  // where in prepared.Text the speech started
        int spokenAt;                    // the latest place reported, in prepared.Text
        int position;                    // the place in the box's text (kept between runs)
        bool loading;                    // the box is being filled by the program
        bool stopping;                   // a stop asked for: the end that follows is not the text's end
        string lastClipboard = "";
        DateTime lastClipboardAt = DateTime.MinValue;
        double charsPerSecond;           // measured while reading
        DateTime playingSince;
        double playedSeconds;            // before the last pause
        int measuredFrom;
        public int ProgressEvents;       // (for the build's test)
        bool finished;                   // the reading came to the end of the text (the bar is empty)
        public int BarValue { get { return progress.Value; } }
        public string LastWord = "";
        public string FirstWord = "";
        public int Misplaced;            // words reported at a place where the text has something else

        public MainForm(Store store, bool silent)
        {
            this.store = store;
            this.silent = silent;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) { Dpi = Math.Max(1f, g.DpiX / 96f); }
            Text = "Modern Voices: Reader";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Px(950), Px(380));
            Size = new Size(Px(950), Px(640));
            Padding = new Padding(Px(8));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            synth = new SpeechSynthesizer();
            symbolFont = new Font("Segoe MDL2 Assets", 11f);
            symbols = symbolFont.Name == "Segoe MDL2 Assets";
            Build();
            FillVoices();
            Restore();
            synth.SpeakProgress += OnProgress;
            synth.SpeakCompleted += OnCompleted;
            tick = new Timer();
            tick.Interval = 500;
            tick.Tick += delegate { UpdateTime(); FollowTheme(); };
            tick.Start();
            UpdateButtons();
        }

        int Px(double n) { return (int)Math.Round(n * Dpi); }

        // ---- building the window

        Label NewLabel(string text, bool muted)
        {
            Label l = new Label();
            l.AutoSize = true; l.Text = text; l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(3, Px(3), 3, Px(3));
            if (muted) { l.Tag = "muted"; }
            return l;
        }

        Button NewButton(string text, EventHandler click)
        {
            Button b = new Button();
            b.Text = text; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(Px(8), Px(2), Px(8), Px(2));
            b.Anchor = AnchorStyles.Left;
            b.Click += click;
            return b;
        }

        FlowLayoutPanel NewFlow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.Dock = DockStyle.Fill;
            f.WrapContents = true; f.Margin = new Padding(0);
            return f;
        }

        TableLayoutPanel NewLine(params bool[] fills)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = fills.Length; t.RowCount = 1;
            foreach (bool fill in fills) { t.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100f) : new ColumnStyle(SizeType.AutoSize)); }
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.AutoSize = true; t.AutoSizeMode = AutoSizeMode.GrowAndShrink; t.Dock = DockStyle.Fill; t.Margin = new Padding(0);
            return t;
        }

        // Play, pause, stop and "from the start" as the usual symbols (words, where Windows has no such symbols).
        Button NewSymbolButton(string symbol, string words, string tip, EventHandler click)
        {
            Button b = NewButton(symbols ? symbol : words, click);
            if (symbols)
            {
                b.Font = symbolFont;
                b.Padding = new Padding(Px(8), Px(5), Px(8), Px(3));
            }
            tips.SetToolTip(b, tip);
            return b;
        }

        // The line under the buttons is placed against them: the progress bar as wide as the first two
        // buttons, the time after it, "Instantly read copy" starting where Paste and read does, "Don't
        // read links" where Save as audio does, and "Save text on close" where Undo does.
        bool aligning;
        void AlignLine()
        {
            if (underPanel == null || undoButton == null || !IsHandleCreated || aligning) { return; }
            aligning = true;
            try
            {
                Func<Control, int> leftOf = delegate(Control c) { return underPanel.PointToClient(c.Parent.PointToScreen(c.Location)).X; };
                int start = leftOf(startButton);
                progress.Left = start;
                progress.Width = Math.Max(Px(50), leftOf(playButton) + playButton.Width - start);     // as wide as the first two buttons
                timeLabel.Left = progress.Right + Px(8);
                autoBox.Left = leftOf(clipboardButton);
                tidyBox.Left = leftOf(audioButton);
                saveBox.Left = leftOf(undoButton);
                foreach (Control c in new Control[] { progress, timeLabel, autoBox, tidyBox, saveBox }) { c.Top = Math.Max(0, (underPanel.Height - c.Height) / 2); }
                alignedFor = timeLabel.Width;
            }
            finally { aligning = false; }
        }

        // ---- the voice list's dots, and the nickname

        // The dropped-down list's own window: found, and its clicks on dots taken (DotList).
        void HookVoiceList()
        {
            try
            {
                if (!voiceBox.IsHandleCreated) { return; }
                ComboInfo info = new ComboInfo();
                info.Size = Marshal.SizeOf(typeof(ComboInfo));
                if (!GetComboBoxInfo(voiceBox.Handle, ref info) || info.List == IntPtr.Zero) { return; }
                voiceList = info.List;
                if (dots.Handle != info.List) { dots.ReleaseHandle(); dots.AssignHandle(info.List); }
            }
            catch (Exception) { }
        }

        // The row whose dot the pointer is on, in the dropped-down list (-1: none).
        int DotUnderPointer()
        {
            DotList.DotRect r;
            if (voiceList == IntPtr.Zero || !GetWindowRect(voiceList, out r)) { return -1; }
            Point at = Cursor.Position;
            int x = at.X - r.Left - 1, y = at.Y - r.Top - 1;       // (inside the list's one-pixel border)
            if (x < 0 || x >= dots.Zone || y < 0 || at.Y >= r.Bottom - 1) { return -1; }
            long found = (long)SendMessage(voiceList, 0x01A9, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)));     // LB_ITEMFROMPOINT
            return ((found >> 16) & 0xFFFF) != 0 ? -1 : (int)(found & 0xFFFF);
        }

        // A row of the list was chosen. With the mouse on the row's dot, that is a click on the dot (should
        // the list have taken the click as its own): Windows' default voice is set, the voice read with
        // stays, and the list comes down again to show it.
        void VoiceChosen()
        {
            bool byMouse = (DateTime.UtcNow - voiceKeyAt).TotalMilliseconds > 300 &&
                (voiceBox.DroppedDown || (DateTime.UtcNow - listClosedAt).TotalMilliseconds < 400);
            int row = voiceBox.SelectedIndex;
            if (byMouse && row >= 0 && DotUnderPointer() == row)
            {
                loading = true;
                voiceBox.SelectedIndex = voiceIndex;
                loading = false;
                SetDefaultVoice((VoiceChoice)voiceBox.Items[row]);
                BeginInvoke((MethodInvoker)delegate { if (!voiceBox.DroppedDown) { voiceBox.DroppedDown = true; } });
                return;
            }
            voiceIndex = row;
            VoiceChanged();
        }

        // A voice's name as Windows has it now.
        static string NameNow(string tokenId, string otherwise)
        {
            try
            {
                using (RegistryKey token = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Speech\Voices\Tokens\" + tokenId))
                {
                    if (token == null) { return otherwise; }
                    string name = "";
                    using (RegistryKey attributes = token.OpenSubKey("Attributes")) { if (attributes != null) { name = Convert.ToString(attributes.GetValue("Name", "")); } }
                    if (name.Length == 0) { name = Convert.ToString(token.GetValue("", "")); }
                    return name.Length > 0 ? name : otherwise;
                }
            }
            catch (Exception) { return otherwise; }
        }

        // What Windows lists as voices now, and under what names (to notice a voice added, removed or renamed
        // in Modern Voices Config while this window was open).
        static string VoicesNow()
        {
            StringBuilder all = new StringBuilder();
            try
            {
                using (RegistryKey tokens = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Speech\Voices\Tokens"))
                {
                    if (tokens != null)
                    {
                        foreach (string name in tokens.GetSubKeyNames())
                        {
                            using (RegistryKey token = tokens.OpenSubKey(name)) { all.Append(name).Append('=').Append(token == null ? "" : Convert.ToString(token.GetValue(""))).Append('|'); }
                        }
                    }
                }
            }
            catch (Exception) { }
            return all.ToString();
        }

        // Windows' voices read afresh (the speech object keeps the list it found when it was made, so a new
        // one is made), with the same voice chosen: by its place in Windows' list, as its name may be new.
        void RefreshVoices()
        {
            VoiceChoice chosen = CurrentVoice;
            string token = chosen == null ? "" : chosen.TokenId;
            if (mode != Mode.Stopped) { Halt(); }
            try
            {
                synth.SpeakProgress -= OnProgress; synth.SpeakCompleted -= OnCompleted;
                synth.Dispose();
            }
            catch (Exception) { }
            synth = new SpeechSynthesizer();
            synth.SpeakProgress += OnProgress;
            synth.SpeakCompleted += OnCompleted;
            FillVoices();
            loading = true;
            for (int i = 0; i < voiceBox.Items.Count; i++)
            {
                if (((VoiceChoice)voiceBox.Items[i]).TokenId == token) { voiceBox.SelectedIndex = i; }
            }
            if (voiceBox.SelectedIndex < 0 && voiceBox.Items.Count > 0) { voiceBox.SelectedIndex = 0; }
            loading = false;
            voiceIndex = voiceBox.SelectedIndex;
            ReadDefaultVoice();
            VoiceChanged();
            UpdateButtons();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // (Back from Modern Voices Config, perhaps: its voices and their names as they are now.)
            if (synth != null && voiceBox != null && saver == null && mode == Mode.Stopped && voicesSeen.Length > 0 && VoicesNow() != voicesSeen)
            {
                RefreshVoices();
            }
        }

        // A click on a row's dot, as the mouse makes it (for the build's test): what came of it.
        public string TestDotClick(int row)
        {
            int chosenWas = voiceBox.SelectedIndex, clicksWas = dots.Clicks;
            voiceBox.DroppedDown = true;
            Application.DoEvents();
            HookVoiceList();
            IntPtr at = (IntPtr)(((row * voiceBox.ItemHeight + voiceBox.ItemHeight / 2) << 16) | (dots.Zone / 2));
            SendMessage(voiceList, 0x0201, (IntPtr)1, at);
            SendMessage(voiceList, 0x0202, IntPtr.Zero, at);
            Application.DoEvents();
            string result = "default " + DefaultToken + "; list " + (voiceBox.DroppedDown ? "open" : "closed") +
                "; chosen " + (voiceBox.SelectedIndex == chosenWas ? "unchanged" : "changed") + "; taken " + (dots.Clicks - clicksWas);
            voiceBox.DroppedDown = false;
            Application.DoEvents();
            return result;
        }

        void ReadDefaultVoice()
        {
            defaultToken = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(DefaultVoiceKey))
                {
                    string id = key == null ? "" : Convert.ToString(key.GetValue("DefaultTokenId", ""));
                    int cut = id.LastIndexOf('\\');
                    defaultToken = cut >= 0 ? id.Substring(cut + 1) : id;
                }
            }
            catch (Exception) { }
        }
        public string DefaultToken { get { ReadDefaultVoice(); return defaultToken; } }

        // Makes a voice Windows' default (what programs that don't choose a voice speak with).
        public void SetDefaultVoice(VoiceChoice voice)
        {
            if (voice == null || string.IsNullOrEmpty(voice.TokenId)) { return; }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(DefaultVoiceKey))
                {
                    key.SetValue("DefaultTokenId", TokensPath + voice.TokenId, RegistryValueKind.String);
                }
                defaultToken = voice.TokenId;
                dots.Redraw();
                Status("Windows' default voice is now " + voice.Shown + ". Programs that were open use it once restarted.");
            }
            catch (Exception e) { Status("Windows' default voice was not changed: " + e.Message); }
        }
        public void TestSetDefault(int row) { SetDefaultVoice((VoiceChoice)voiceBox.Items[row]); }
        public string TopRow { get { return voiceBox.Height + "/" + nickBox.Height + "/" + nickButton.Width + "x" + nickButton.Height; } }
        public bool TopRowEven { get { return nickBox.Height == voiceBox.Height && nickButton.Height == voiceBox.Height && nickButton.Width == voiceBox.Height; } }
        public string TestToken(int row) { return ((VoiceChoice)voiceBox.Items[row]).TokenId; }

        void DrawVoice(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= voiceBox.Items.Count) { return; }
            VoiceChoice voice = (VoiceChoice)voiceBox.Items[e.Index];
            Palette p = Theme.Get(Theme.IsDark());
            bool closed = (e.State & DrawItemState.ComboBoxEdit) != 0;       // (the box itself: the name alone)
            bool lit = !closed && (e.State & DrawItemState.Selected) != 0;
            Color back = lit ? p.Accent : p.Input, fore = lit ? Color.White : p.Text;
            using (SolidBrush fill = new SolidBrush(back)) { e.Graphics.FillRectangle(fill, e.Bounds); }
            int x = e.Bounds.Left + Px(2);
            if (!closed)
            {
                int size = Px(12), left = e.Bounds.Left + (dots.Zone - size) / 2, topAt = e.Bounds.Top + (e.Bounds.Height - size) / 2;
                SmoothingMode was = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen ring = new Pen(lit ? Color.White : p.Muted, Math.Max(1f, Dpi * 1.2f))) { e.Graphics.DrawEllipse(ring, left, topAt, size, size); }
                if (string.Equals(voice.TokenId, defaultToken, StringComparison.OrdinalIgnoreCase))
                {
                    int inner = size - Px(6);
                    using (SolidBrush dot = new SolidBrush(fore)) { e.Graphics.FillEllipse(dot, left + (size - inner) / 2f, topAt + (size - inner) / 2f, inner, inner); }
                }
                e.Graphics.SmoothingMode = was;
                x = e.Bounds.Left + dots.Zone;
            }
            TextRenderer.DrawText(e.Graphics, voice.Shown, voiceBox.Font, new Rectangle(x, e.Bounds.Top, e.Bounds.Right - x, e.Bounds.Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void UpdateNickButton()
        {
            VoiceChoice voice = CurrentVoice;
            bool ours = voice != null && voice.Ours;
            nickBox.Enabled = ours;
            nickButton.Enabled = ours && CleanNickname(nickBox.Text).Length > 0;
        }

        public static string CleanNickname(string typed)
        {
            return Regex.Replace(Regex.Replace(typed ?? "", @"[()\r\n]", " "), @"\s+", " ").Trim();
        }

        // Gives the voice chosen the nickname typed: Windows keeps voices' names where only an administrator
        // may write, so a small script beside Modern Voices Config does it, with Windows' permission prompt.
        // The helper that renames voices for this window (installer\Nickname.ps1, staying): started with
        // Windows' permission prompt at the first nickname, then asked over a pipe of its own, so that
        // Windows asks once while the Reader is open. It ends when the Reader does.
        readonly string nickPipe = "ModernVoicesNickname_" + Guid.NewGuid().ToString("N");
        bool nickHelper;                 // started (it may have ended since: then it is started again)

        // Asks the helper for a nickname: its answer (0 done, 2 refused, 3 another voice has the name, 1 failed),
        // or -1 when it is not there.
        int AskNickHelper(string request, int waitMs)
        {
            try
            {
                using (System.IO.Pipes.NamedPipeClientStream pipe = new System.IO.Pipes.NamedPipeClientStream(".", nickPipe, System.IO.Pipes.PipeDirection.InOut))
                {
                    pipe.Connect(waitMs);
                    byte[] line = Encoding.UTF8.GetBytes(request + "\n");
                    pipe.Write(line, 0, line.Length);
                    pipe.Flush();
                    StringBuilder answer = new StringBuilder();
                    for (int b = pipe.ReadByte(); b >= 0 && b != '\n'; b = pipe.ReadByte()) { answer.Append((char)b); }
                    int code;
                    return int.TryParse(answer.ToString().Trim(), out code) ? code : -1;
                }
            }
            catch (Exception) { return -1; }
        }

        void ChangeNickname()
        {
            VoiceChoice voice = CurrentVoice;
            string nick = CleanNickname(nickBox.Text);
            if (voice == null || !voice.Ours || nick.Length == 0) { return; }
            try
            {
                string request = voice.TokenId + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(nick));
                int code = nickHelper ? AskNickHelper(request, 1500) : -1;
                if (code < 0)
                {
                    string app = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
                    string script = Path.Combine(app, "installer", "Nickname.ps1");
                    if (!File.Exists(script)) { Status("The nickname was not changed: " + script + " was not found."); return; }
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    bool narrow = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess;
                    string shell = Path.Combine(windows, narrow ? "Sysnative" : "System32", @"WindowsPowerShell\v1.0\powershell.exe");
                    System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(shell,
                        "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Serve " + nickPipe +
                        " -OwnerPid " + System.Diagnostics.Process.GetCurrentProcess().Id +
                        " -OwnerSid " + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value);
                    start.UseShellExecute = true; start.Verb = "runas";
                    start.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    using (System.Diagnostics.Process.Start(start)) { }
                    nickHelper = true;
                    code = AskNickHelper(request, 20000);
                    if (code < 0)
                    {
                        // (The helper did not answer: the same script, once, as before. Windows asks again.)
                        nickHelper = false;
                        start.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Token " + voice.TokenId +
                            " -Nick64 " + Convert.ToBase64String(Encoding.UTF8.GetBytes(nick));
                        using (System.Diagnostics.Process once = System.Diagnostics.Process.Start(start))
                        {
                            if (!once.WaitForExit(30000)) { Status("The nickname was not changed (it took too long)."); return; }
                            code = once.ExitCode;
                        }
                    }
                }
                if (code == 3) { Status("Another voice is already named that."); return; }
                if (code != 0) { Status("The nickname was not changed (error " + code + ")."); return; }
            }
            catch (System.ComponentModel.Win32Exception) { Status("The nickname was not changed: Windows' permission was not given."); return; }
            catch (Exception e) { Status("The nickname was not changed: " + e.Message); return; }
            // The list again, with the same voice chosen under its new name.
            RefreshVoices();
            nickBox.Text = "";
            Status("Now named " + (CurrentVoice == null ? nick : CurrentVoice.Name) + ". Programs that were open show the new name once restarted.");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            nickBox.Height = voiceBox.Height; nickButton.Size = new Size(voiceBox.Height, voiceBox.Height);     // (as the list is, once drawn)
            AlignLine();
        }

        // Light or dark as chosen with the moon in Modern Voices Config: now, and whenever that changes.
        public void FollowTheme()
        {
            string choice = Theme.Choice;
            if (choice == shownTheme) { return; }
            shownTheme = choice;
            Theme.Apply(this, Theme.IsDark());
            AlignLine();
        }

        // Modern Voices Config (the settings window, a program beside this one), as its Start menu entry opens it.
        void OpenConfig()
        {
            try
            {
                // Already open: that window comes to the front.
                foreach (System.Diagnostics.Process running in System.Diagnostics.Process.GetProcessesByName("ModernVoicesConfig"))
                {
                    try
                    {
                        if (running.MainWindowHandle != IntPtr.Zero)
                        {
                            if (IsIconic(running.MainWindowHandle)) { ShowWindowAsync(running.MainWindowHandle, 9); }
                            SetForegroundWindow(running.MainWindowHandle);
                            return;
                        }
                    }
                    catch (Exception) { }
                    finally { running.Dispose(); }
                }
                string app = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
                string config = Path.Combine(app, "config", "ModernVoicesConfig.exe");
                if (!File.Exists(config)) { Status("Modern Voices Config was not found (" + config + ")."); return; }
                System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(config);
                start.WorkingDirectory = Path.GetDirectoryName(config); start.UseShellExecute = true;
                System.Diagnostics.Process.Start(start);
                Status("Opening Modern Voices Config...");
            }
            catch (Exception e) { Status("Modern Voices Config did not open: " + e.Message); }
        }

        void Build()
        {
            TableLayoutPanel page = new TableLayoutPanel();
            page.ColumnCount = 1; page.Dock = DockStyle.Fill;
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(page);
            int row = 0;

            // The voice (left) and the speed (right).
            TableLayoutPanel top = NewLine(false, false, false, false, true, false, false, false);
            voiceBox = new ComboBox();
            voiceBox.DropDownStyle = ComboBoxStyle.DropDownList; voiceBox.Width = Px(260); voiceBox.Anchor = AnchorStyles.Left;
            voiceBox.SelectedIndexChanged += delegate { if (!loading) { VoiceChosen(); } };
            voiceBox.KeyDown += delegate { voiceKeyAt = DateTime.UtcNow; };
            // Each row drawn here: a dot (filled: Windows' default voice), then the voice's name.
            voiceBox.DrawMode = DrawMode.OwnerDrawFixed;
            voiceBox.ItemHeight = voiceBox.Font.Height + Px(5);
            voiceBox.DrawItem += DrawVoice;
            voiceBox.DropDown += delegate { ReadDefaultVoice(); HookVoiceList(); dotTimer.Start(); };
            voiceBox.DropDownClosed += delegate { listClosedAt = DateTime.UtcNow; dotTimer.Stop(); if (dotTip != null) { dotTip.HideTip(); } };
            voiceBox.HandleCreated += delegate { HookVoiceList(); };
            voiceBox.HandleDestroyed += delegate { dots.ReleaseHandle(); voiceList = IntPtr.Zero; };
            dots.Zone = Px(26);
            dots.DotClicked = delegate(int at) { if (at < voiceBox.Items.Count) { SetDefaultVoice((VoiceChoice)voiceBox.Items[at]); } };
            // While the list is down: the note beside the pointer when it is on a dot.
            dotTimer = new Timer();
            dotTimer.Interval = 80;
            dotTimer.Tick += delegate
            {
                bool on = voiceBox.DroppedDown && DotUnderPointer() >= 0;
                if (on)
                {
                    if (dotTip == null) { dotTip = new TipWindow("Set Windows default voice", Font); }
                    Point at = Cursor.Position;
                    dotTip.ShowAt(new Point(at.X + Px(16), at.Y + Px(20)));
                }
                else if (dotTip != null) { dotTip.HideTip(); }
            };
            top.Controls.Add(voiceBox, 0, 0);
            top.Controls.Add(NewLabel("Voice", false), 1, 0);
            // A nickname for the voice chosen (its box says what it is for, in grey, while empty).
            nickBox = new TextBox();
            nickBox.Width = Px(170); nickBox.MaxLength = 40; nickBox.Anchor = AnchorStyles.Left;
            nickBox.Margin = new Padding(Px(14), 3, 3, 3);
            nickBox.HandleCreated += delegate { SendMessage(nickBox.Handle, 0x1501, (IntPtr)1, "Change nickname"); };   // EM_SETCUEBANNER
            nickBox.TextChanged += delegate { UpdateNickButton(); };
            nickBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (nickButton.Enabled) { ChangeNickname(); } }
            };
            nickButton = NewSymbolButton("\uE73E", "Set", "Give the voice chosen this nickname (Windows asks for permission the first time)", delegate { ChangeNickname(); });
            nickButton.Enabled = false;
            // (Both as tall as the voice list beside them; the tick's button square.)
            nickBox.AutoSize = false; nickBox.Height = voiceBox.Height;
            nickButton.AutoSize = false; nickButton.Padding = new Padding(0); nickButton.Anchor = AnchorStyles.Left;
            nickButton.Size = new Size(voiceBox.Height, voiceBox.Height);
            if (symbols) { nickButton.Font = new Font("Segoe MDL2 Assets", 8.5f); }
            top.Controls.Add(nickBox, 2, 0);
            top.Controls.Add(nickButton, 3, 0);
            top.Controls.Add(NewLabel("Speed", false), 5, 0);
            rateBar = new TrackBar();
            rateBar.Minimum = -10; rateBar.Maximum = 10; rateBar.TickFrequency = 5; rateBar.LargeChange = 1;
            rateBar.AutoSize = false; rateBar.Width = Px(150); rateBar.Height = Px(30); rateBar.Anchor = AnchorStyles.Left;
            rateBar.Margin = new Padding(3, Px(13), 3, 0);     // (its bar is drawn above its middle: lowered to line up with the labels)
            rateBar.TickStyle = TickStyle.None;
            rateBar.ValueChanged += delegate { RateChanged(); };
            top.Controls.Add(rateBar, 6, 0);
            rateLabel = NewLabel("0", false);
            rateLabel.MinimumSize = new Size(Px(26), 0);
            top.Controls.Add(rateLabel, 7, 0);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(top, 0, row++);

            // The text.
            box = new PasteBox();
            box.Multiline = true; box.AcceptsReturn = true; box.AcceptsTab = false; box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical; box.HideSelection = false; box.MaxLength = 0;
            box.Dock = DockStyle.Fill; box.Font = new Font("Segoe UI", 10.5f);
            box.Pasting = ClipboardText;
            box.UndoAsked = Undo; box.RedoAsked = Redo;
            box.CanUndoAsked = delegate { return before.Count > 0; };
            box.TextChanged += delegate { if (!loading) { NoteChange(false); TextEdited(); } };
            // (A click is a press and release at the same character; a drag selects text, as ever.)
            box.MouseDown += delegate(object sender, MouseEventArgs e) { pressedAt = e.Button == MouseButtons.Left ? box.GetCharIndexFromPosition(e.Location) : -1; };
            box.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (mode != Mode.Stopped)
                {
                    int releasedAt = box.GetCharIndexFromPosition(e.Location);
                    if (e.Button == MouseButtons.Left && pressedAt >= 0 && releasedAt == pressedAt) { ClickedWhileReading(releasedAt); }
                    pressedAt = -1;
                    return;
                }
                heldCaret = box.SelectionStart; CaretMoved(e.Button == MouseButtons.Left);
            };
            box.KeyUp += delegate { heldCaret = box.SelectionStart; CaretMoved(); };
            // A file dropped on the text is opened.
            box.AllowDrop = true;
            box.DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; }
            };
            box.DragDrop += delegate(object sender, DragEventArgs e)
            {
                string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null && dropped.Length > 0) { OpenFile(dropped[0]); }
            };
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); page.Controls.Add(box, 0, row++);

            // From the start, play or pause, stop; the clipboard, a file, the screen; as sound. Clear at the far end.
            TableLayoutPanel buttonLine = NewLine(true, false);
            FlowLayoutPanel buttons = NewFlow();
            buttons.WrapContents = false;     // (one line: the window is never narrower than it needs)
            startButton = NewSymbolButton("\uE892", "From the start", "Read from the start", delegate { FromStart(); });
            playButton = NewSymbolButton("\uE768", "Play", "Play", delegate { PlayOrPause(); });
            stopButton = NewSymbolButton("\uE71A", "Stop", "Stop (Play goes on from the same place)", delegate { Stop(); });
            clipboardButton = NewButton("Paste and read", delegate { ReadClipboard(false); });
            fileButton = NewButton("Open a file...", delegate { ChooseFile(); });
            screenButton = NewSymbolButton("\uE721", "Read screen...", "Select an area of the screen to read with TTS", delegate { ReadScreen(); });
            audioButton = NewButton("Save as audio...", delegate { ChooseAudioFile(); });
            clearButton = NewButton("Clear", delegate { ClearText(); });
            undoButton = NewSymbolButton("\uE7A7", "Undo", "Undo (Ctrl+Z)", delegate { Undo(); box.Focus(); });
            redoButton = NewSymbolButton("\uE7A6", "Redo", "Redo (Ctrl+Y)", delegate { Redo(); box.Focus(); });
            foreach (Button b in new Button[] { startButton, playButton, stopButton, fileButton, clipboardButton, screenButton, audioButton })
            {
                b.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;    // (all one height)
                buttons.Controls.Add(b);
            }
            // Between Save as audio and undo: how loud (a small speaker, and its slider).
            Label volumeLabel = NewLabel(symbols ? "\uE767" : "Volume", false);
            if (symbols) { volumeLabel.Font = symbolFont; }
            volumeLabel.Anchor = AnchorStyles.None; volumeLabel.Margin = new Padding(Px(12), 0, 0, 0);
            volumeBar = new TrackBar();
            volumeBar.Minimum = 0; volumeBar.Maximum = 100; volumeBar.Value = 100; volumeBar.SmallChange = 5; volumeBar.LargeChange = 10;
            volumeBar.TickStyle = TickStyle.None; volumeBar.AutoSize = false; volumeBar.Width = Px(88); volumeBar.Height = Px(26);
            volumeBar.Anchor = AnchorStyles.None; volumeBar.Margin = new Padding(0, Px(8), 0, 0);     // (its bar is drawn above its middle)
            tips.SetToolTip(volumeBar, "Volume"); tips.SetToolTip(volumeLabel, "Volume");
            volumeBar.ValueChanged += delegate
            {
                tips.SetToolTip(volumeBar, "Volume: " + volumeBar.Value + "%");
                if (loading) { return; }
                store.State.Volume = volumeBar.Value;
                ApplyVolume();      // (heard at once, mid-sentence too)
            };
            volumeBar.MouseUp += delegate { store.Save(); };
            volumeBar.KeyUp += delegate { store.Save(); };
            buttons.Controls.Add(volumeLabel); buttons.Controls.Add(volumeBar);
            // (A wider gap after Stop: the three that play, then the four that bring text in or take it out.)
            fileButton.Margin = new Padding(fileButton.Margin.Left + Px(14), fileButton.Margin.Top, fileButton.Margin.Right, fileButton.Margin.Bottom);
            clearButton.MinimumSize = new Size((int)(clearButton.PreferredSize.Width * 1.3), 0);
            // At the far end: undo, redo, Clear (with a gap always left before them).
            FlowLayoutPanel ends = NewFlow();
            ends.WrapContents = false; ends.Dock = DockStyle.None; ends.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            ends.Margin = new Padding(Px(12), 0, 0, 0);
            foreach (Button b in new Button[] { undoButton, redoButton, clearButton })
            {
                b.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
                ends.Controls.Add(b);
            }
            buttonLine.Controls.Add(buttons, 0, 0);
            buttonLine.Controls.Add(ends, 1, 0);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(buttonLine, 0, row++);

            // How far, how long is left; what is read.
            TableLayoutPanel under = NewLine(true);
            Font lineFont = new Font("Segoe UI", 10f);     // (this line a little larger than the buttons' words)
            progress = new ProgressBar();
            // (What is left to read, in green, against the bar's right end: full as reading starts, emptying
            // towards the right, empty when the reading has finished.)
            progress.RightToLeft = RightToLeft.Yes; progress.RightToLeftLayout = true;
            progress.Maximum = 1000; progress.Width = Px(120); progress.Height = Px(18); progress.Anchor = AnchorStyles.Left;
            progress.Margin = new Padding(3, Px(6), Px(6), Px(6));
            timeLabel = NewLabel("", false);
            timeLabel.Font = lineFont;
            timeLabel.MinimumSize = new Size(Px(104), 0);
            autoBox = new CheckBox();
            autoBox.Text = "Instantly read copy"; autoBox.AutoSize = true; autoBox.Anchor = AnchorStyles.Left; autoBox.Font = lineFont;
            autoBox.Margin = new Padding(Px(4), 3, Px(4), 3);
            autoBox.CheckedChanged += delegate { if (!loading) { store.State.AutoRead = autoBox.Checked; store.Save(); } };
            tidyBox = new CheckBox();
            tidyBox.Text = "Don't read links"; tidyBox.AutoSize = true; tidyBox.Anchor = AnchorStyles.Left; tidyBox.Font = lineFont;
            tidyBox.Margin = new Padding(Px(4), 3, 0, 3);
            tidyBox.CheckedChanged += delegate { if (!loading) { store.State.Tidy = tidyBox.Checked; store.Save(); } };
            // Ticked (as it is to begin with): closing with text in the box asks "Save text for next time?".
            // Not ticked: closing does not ask, and does not keep the text.
            saveBox = new CheckBox();
            saveBox.Text = "Save text on close"; saveBox.AutoSize = true; saveBox.Anchor = AnchorStyles.Left; saveBox.Font = lineFont;
            saveBox.Margin = new Padding(Px(4), 3, 0, 3);
            saveBox.Checked = true;
            saveBox.CheckedChanged += delegate { if (!loading) { store.State.SaveOnClose = saveBox.Checked; store.Save(); } };
            tips.SetToolTip(saveBox, "When you close the Reader with text in the box, asks whether to keep the text for next time.\r\nNot ticked: it closes without asking, and the text is not kept.");
            tips.SetToolTip(autoBox, "Reads whatever you copy, in any program, as soon as you copy it");
            tips.SetToolTip(tidyBox, "Leaves out links, code and the symbols of formatted text");
            // (Placed by AlignLine, against the buttons above.)
            underPanel = new Panel();
            underPanel.Dock = DockStyle.Fill; underPanel.Margin = new Padding(0);
            foreach (Control c in new Control[] { progress, timeLabel, autoBox, tidyBox, saveBox }) { c.Anchor = AnchorStyles.Left | AnchorStyles.Top; underPanel.Controls.Add(c); }
            underPanel.Height = Math.Max(autoBox.PreferredSize.Height, progress.Height) + Px(8);
            under.Controls.Add(underPanel, 0, 0);
            ends.LocationChanged += delegate { AlignLine(); };
            buttons.SizeChanged += delegate { AlignLine(); };
            underPanel.SizeChanged += delegate { AlignLine(); };
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(under, 0, row++);

            // The last line: what is happening (left) and, in grey at the far right, a cog to click (no
            // button around it): it opens Modern Voices Config, where the moon for light and dark is.
            TableLayoutPanel last = NewLine(true, false);
            statusLabel = NewLabel("", true);
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right; statusLabel.AutoEllipsis = true;
            statusLabel.AutoSize = false; statusLabel.Height = Px(20); statusLabel.Dock = DockStyle.Fill;
            last.Controls.Add(statusLabel, 0, 0);
            configButton = NewLabel("Settings", true);     // ("muted": the grey of the status line, not the text's white)
            configButton.Anchor = AnchorStyles.Right; configButton.Cursor = Cursors.Hand;
            configButton.Margin = new Padding(Px(6), 0, Px(4), 0);
            if (symbols) { configButton.Font = new Font("Segoe MDL2 Assets", 15f); configButton.Text = "\uE713"; }
            else { configButton.Font = lineFont; }
            tips.SetToolTip(configButton, "Modern Voices Config: voices, speed and pauses, pronunciation, light or dark");
            configButton.Click += delegate { OpenConfig(); };
            last.Controls.Add(configButton, 1, 0);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(last, 0, row++);
            page.RowCount = row;
        }

        // ---- voices

        // The Modern Voices first, by nickname where they have one; then Windows' other voices.
        void FillVoices()
        {
            HashSet<string> nicknames = new HashSet<string>();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Modern Voices for Windows\Nicknames"))
                {
                    if (key != null) { foreach (string name in key.GetValueNames()) { nicknames.Add(Convert.ToString(key.GetValue(name))); } }
                }
            }
            catch (Exception) { }
            List<VoiceChoice> ours = new List<VoiceChoice>(), others = new List<VoiceChoice>();
            foreach (InstalledVoice v in synth.GetInstalledVoices())
            {
                if (!v.Enabled) { continue; }
                VoiceChoice choice = new VoiceChoice();
                // (The speech object goes on giving the names voices had when the program started, even a
                // new one: a voice's name now is read from where Windows keeps it.)
                choice.Spoken = v.VoiceInfo.Name;
                choice.TokenId = v.VoiceInfo.Id;
                choice.Name = NameNow(choice.TokenId, choice.Spoken);
                choice.Shown = choice.Name;
                choice.Ours = v.VoiceInfo.Id.StartsWith("ModernVoices_", StringComparison.OrdinalIgnoreCase);
                if (choice.Ours)
                {
                    Match m = Regex.Match(choice.Name, @"\(([^()]+)\)\s*$");
                    if (m.Success && nicknames.Contains(m.Groups[1].Value)) { choice.Shown = m.Groups[1].Value; }
                    ours.Add(choice);
                }
                else { others.Add(choice); }
            }
            Comparison<VoiceChoice> byShown = delegate(VoiceChoice a, VoiceChoice b) { return string.Compare(a.Shown, b.Shown, StringComparison.CurrentCultureIgnoreCase); };
            ours.Sort(byShown); others.Sort(byShown);
            loading = true;
            voiceBox.Items.Clear();
            foreach (VoiceChoice c in ours) { voiceBox.Items.Add(c); }
            foreach (VoiceChoice c in others) { voiceBox.Items.Add(c); }
            // (The list as wide as its longest name, with its dot.)
            int widest = voiceBox.Width;
            foreach (VoiceChoice c in voiceBox.Items) { widest = Math.Max(widest, TextRenderer.MeasureText(c.Shown, voiceBox.Font).Width + dots.Zone + Px(28)); }
            voiceBox.DropDownWidth = widest;
            voicesSeen = VoicesNow();
            loading = false;
        }

        public int VoiceCount { get { return voiceBox.Items.Count; } }
        public int OurVoiceCount
        {
            get { int n = 0; foreach (VoiceChoice c in voiceBox.Items) { if (c.Ours) { n++; } } return n; }
        }
        public string VoicesShown
        {
            get { List<string> all = new List<string>(); foreach (VoiceChoice c in voiceBox.Items) { all.Add(c.Shown); } return string.Join(" | ", all.ToArray()); }
        }

        VoiceChoice CurrentVoice { get { return voiceBox.SelectedItem as VoiceChoice; } }

        void VoiceChanged()
        {
            VoiceChoice v = CurrentVoice;
            if (v == null) { return; }
            store.State.Voice = v.TokenId;      // (not its name, which a nickname changes)
            store.Save();
            UpdateNickButton();
            charsPerSecond = 0;
            if (mode != Mode.Stopped) { Restart(); }   // goes on with the new voice from the sentence being read
        }

        void RateChanged()
        {
            rateLabel.Text = (rateBar.Value > 0 ? "+" : "") + rateBar.Value.ToString(CultureInfo.InvariantCulture);
            if (loading) { return; }
            store.State.Rate = rateBar.Value;
            store.Save();
            charsPerSecond = 0;
            if (mode != Mode.Stopped) { Restart(); }
        }

        // ---- the state kept between runs

        // The voice selected as the program opens: Windows' default voice (the one with the dot); if that is
        // not in the list, the voice used last time; else the first.
        int StartVoiceIndex(string usedLast)
        {
            ReadDefaultVoice();
            string token = defaultToken;
            if (string.IsNullOrEmpty(token))
            {
                // (No default chosen by this person: the one Windows gives a program that doesn't choose.)
                try { token = synth.Voice.Id; } catch (Exception) { token = ""; }
            }
            int chosen = -1;
            for (int i = 0; i < voiceBox.Items.Count && chosen < 0; i++)
            {
                if (string.Equals(((VoiceChoice)voiceBox.Items[i]).TokenId, token, StringComparison.OrdinalIgnoreCase)) { chosen = i; }
            }
            for (int i = 0; i < voiceBox.Items.Count && chosen < 0; i++)
            {
                VoiceChoice kept = (VoiceChoice)voiceBox.Items[i];
                if (kept.TokenId == usedLast || kept.Name == usedLast) { chosen = i; }
            }
            if (chosen < 0 && voiceBox.Items.Count > 0) { chosen = 0; }
            return chosen;
        }
        public int TestStartVoice() { return StartVoiceIndex(""); }

        void Restore()
        {
            State s = store.State;
            loading = true;
            if (s.Bounds.Length == 4)
            {
                Rectangle wanted = new Rectangle(s.Bounds[0], s.Bounds[1], s.Bounds[2], s.Bounds[3]);
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(wanted)) { StartPosition = FormStartPosition.Manual; Bounds = wanted; break; }
                }
            }
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Width > area.Width) { Width = area.Width; }
            if (Height > area.Height) { Height = area.Height; }
            rateBar.Value = Math.Max(-10, Math.Min(10, s.Rate));
            volumeBar.Value = Math.Max(0, Math.Min(100, s.Volume));
            ApplyVolume();
            autoBox.Checked = s.AutoRead;
            tidyBox.Checked = s.Tidy;
            saveBox.Checked = s.SaveOnClose;
            int chosen = StartVoiceIndex(s.Voice);
            voiceBox.SelectedIndex = chosen;
            voiceIndex = chosen;
            ReadDefaultVoice();
            UpdateNickButton();
            RestoreKeptText();
            loading = false;
            RateChanged();
            ShowPlace();
            if (voiceBox.Items.Count == 0) { Status("Windows has no voices to read with: add some in Modern Voices Config."); }
            else if (position > 0 && position < box.TextLength) { Status("Play goes on from where you stopped."); }
        }

        // The box starts with the text kept as the Reader was last closed ("Save text for next time?": Yes),
        // at the place it had; else empty. Kept for one start: closing asks again.
        public void RestoreKeptText()
        {
            State s = store.State;
            string kept = s.KeepText ? store.Read(store.CurrentFile) : "";
            bool was = loading;
            loading = true;
            box.Text = kept;
            position = kept.Length > 0 ? Math.Max(0, Math.Min(kept.Length, s.Position)) : 0;
            loading = was;
            heldText = kept; heldCaret = position;
            s.KeepText = false; s.Position = 0; s.OpenedFile = "";
            try { if (File.Exists(store.CurrentFile)) { File.Delete(store.CurrentFile); } } catch (Exception) { }
        }
        // As the Reader closes: the text is kept for the next start, or not.
        public void KeepTextForNextTime(bool keep)
        {
            State s = store.State;
            s.KeepText = keep && box.Text.Trim().Length > 0;
            if (s.KeepText) { store.Write(store.CurrentFile, box.Text); s.Position = position; }
            else { s.Position = 0; try { if (File.Exists(store.CurrentFile)) { File.Delete(store.CurrentFile); } } catch (Exception) { } }
            store.Save();
        }
        // "Save text for next time?" was answered: the text is kept or not, and "Don't ask again" unticks
        // "Save text on close" (from then on closing neither asks nor keeps).
        public void AnswerSaveText(bool keep, bool notAgain)
        {
            KeepTextForNextTime(keep);
            if (notAgain) { saveBox.Checked = false; store.State.SaveOnClose = false; store.Save(); }
        }
        public bool SaveOnCloseTicked { get { return saveBox.Checked; } set { saveBox.Checked = value; } }
        public string SaveOnCloseWords { get { return saveBox.Text; } }
        // (How far "Save text on close" starts from where Undo does: the build's test.)
        public int SaveBoxOffset
        {
            get { return saveBox.PointToScreen(Point.Empty).X - undoButton.PointToScreen(Point.Empty).X; }
        }
        public bool SaveBoxFits { get { return saveBox.Right <= underPanel.ClientSize.Width && saveBox.Visible; } }
        void Remember()
        {
            State s = store.State;
            if (!s.KeepText) { s.Position = 0; }      // (the box's text is kept only when asked for; a file's place is)
            if (s.OpenedFile != "") { store.KeepPlace(s.OpenedFile, position, box.TextLength); }
            if (WindowState == FormWindowState.Normal) { s.Bounds = new int[] { Left, Top, Width, Height }; }
            store.Save();
        }

        // ---- reading

        // What is happening, on the last line: shown for five seconds, then it fades away. (A message that
        // goes on being renewed, as the progress of saving is, stays.)
        Timer statusTimer;
        DateTime statusAt;
        const double StatusSeconds = 5.0, StatusFade = 0.6;
        void Status(string text)
        {
            Palette p = Theme.Get(Theme.IsDark());
            statusLabel.ForeColor = p.Muted;
            statusLabel.Text = text;
            statusAt = DateTime.UtcNow;
            if (statusTimer == null)
            {
                statusTimer = new Timer();
                statusTimer.Interval = 50;
                statusTimer.Tick += delegate { FadeStatus((DateTime.UtcNow - statusAt).TotalSeconds); };
            }
            if (text.Length > 0) { statusTimer.Start(); } else { statusTimer.Stop(); }
        }
        // The status line `shown` seconds after its message came: there, fading, or gone.
        public void FadeStatus(double shown)
        {
            if (shown < StatusSeconds) { return; }
            Palette p = Theme.Get(Theme.IsDark());
            double t = Math.Min(1.0, (shown - StatusSeconds) / StatusFade);
            if (t >= 1.0)
            {
                statusLabel.Text = ""; statusLabel.ForeColor = p.Muted;
                if (statusTimer != null) { statusTimer.Stop(); }
                return;
            }
            statusLabel.ForeColor = Color.FromArgb(
                (int)(p.Muted.R + (p.Back.R - p.Muted.R) * t), (int)(p.Muted.G + (p.Back.G - p.Muted.G) * t), (int)(p.Muted.B + (p.Back.B - p.Muted.B) * t));
        }
        public string StatusText { get { return statusLabel.Text; } }
        public void TestStatus(string text) { Status(text); }

        // What is being read now. A reading that was stopped can still have words on their way here:
        // without this they would be taken as the new reading's (and move the place to where the old one was).
        Prompt currentPrompt;

        public void Play()
        {
            if (mode == Mode.Paused) { PauseOrResume(); return; }
            if (mode == Mode.Playing) { return; }
            if (box.TextLength == 0) { Status("Nothing to read: paste some text, or press Paste and read."); return; }
            if (CurrentVoice == null) { Status("Choose a voice first."); return; }
            if (finished || position >= box.TextLength) { position = 0; placeClicked = false; }
            StartAt(position, placeClicked);
        }

        // Whether the place is one clicked in the text: reading starts at that very word. (A place the
        // reading stopped at, or kept for a file, starts at its sentence.)
        bool placeClicked;

        void StartAt(int from, bool atWord = false)
        {
            string text = box.Text;
            placeClicked = false;
            prepared = Cleaner.Prepare(text, tidyBox.Checked);
            int start = prepared.FromOriginal(atWord ? Cleaner.WordStart(text, from) : Cleaner.SentenceStart(text, from));
            if (start >= prepared.Text.Length || prepared.Text.Substring(start).Trim().Length == 0)
            {
                // Nothing left to read from here (the end, or only blank lines after it): from the start again.
                position = 0;
                if (from > 0) { StartAt(0); return; }
                ShowPlace();
                Status("Nothing to read here.");
                return;
            }
            spokenFrom = start; spokenAt = start; measuredFrom = start;
            playedSeconds = 0; playingSince = DateTime.UtcNow;
            FirstWord = "";
            try
            {
                if (silent) { synth.SetOutputToNull(); } else { synth.SetOutputToDefaultAudioDevice(); }
                synth.SelectVoice(CurrentVoice.Spoken);
                synth.Rate = rateBar.Value;
                synth.Volume = 100;      // (the slider sets the Reader's sound as a whole: ApplyVolume)
                ApplyVolume();
                stopping = false;
                finished = false;
                currentPrompt = synth.SpeakAsync(prepared.Text.Substring(start));
            }
            catch (Exception e)
            {
                Status("Could not start reading: " + e.Message);
                return;
            }
            mode = Mode.Playing;
            box.ReadOnly = true;
            Status("");
            UpdateButtons();
        }

        void PlayOrPause()
        {
            if (mode == Mode.Playing) { PauseOrResume(); } else { Play(); }
        }

        public void PauseOrResume()
        {
            if (mode == Mode.Playing)
            {
                synth.Pause();
                playedSeconds += (DateTime.UtcNow - playingSince).TotalSeconds;
                mode = Mode.Paused;
                Remember();
            }
            else if (mode == Mode.Paused)
            {
                synth.Resume();
                playingSince = DateTime.UtcNow;
                mode = Mode.Playing;
            }
            UpdateButtons();
        }

        // Stops, keeping the place: Play goes on from the sentence it was in.
        public void Stop()
        {
            if (mode == Mode.Stopped) { return; }
            Halt();
            Remember();
            Status("Stopped. Play goes on from here; click a word to read from there.");
        }

        void Halt()
        {
            stopping = true;
            try
            {
                if (mode == Mode.Paused) { synth.Resume(); }
                synth.SpeakAsyncCancelAll();
            }
            catch (Exception) { }
            mode = Mode.Stopped;
            box.ReadOnly = false;
            UpdateButtons();
        }

        void Restart()
        {
            int from = position;
            Halt();
            StartAt(from);
        }

        void FromStart()
        {
            if (mode != Mode.Stopped) { Halt(); }
            position = 0;
            if (box.TextLength > 0) { StartAt(0); } else { ShowPlace(); }
        }

        void OnProgress(object sender, SpeakProgressEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new EventHandler<SpeakProgressEventArgs>(OnProgress), sender, e); return; }
            if (mode == Mode.Stopped || prepared == null || e.Prompt != currentPrompt) { return; }
            ProgressEvents++;
            spokenAt = Math.Min(prepared.Text.Length, spokenFrom + e.CharacterPosition);
            int start = prepared.Original(spokenAt);
            int end = prepared.Original(Math.Min(prepared.Text.Length - 1, spokenAt + Math.Max(1, e.CharacterCount) - 1)) + 1;
            position = start;
            LastWord = e.Text;
            if (e.Text != null && (spokenAt + e.Text.Length > prepared.Text.Length || prepared.Text.Substring(spokenAt, e.Text.Length) != e.Text)) { Misplaced++; }
            if (FirstWord == "") { FirstWord = e.Text; }
            if (start < box.TextLength)
            {
                box.Select(start, Math.Max(1, Math.Min(box.TextLength - start, end - start)));
                box.ScrollToCaret();
            }
            double seconds = playedSeconds + (mode == Mode.Playing ? (DateTime.UtcNow - playingSince).TotalSeconds : 0);
            if (seconds > 4 && spokenAt - measuredFrom > 40) { charsPerSecond = (spokenAt - measuredFrom) / seconds; }
            UpdateTime();
        }

        void OnCompleted(object sender, SpeakCompletedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new EventHandler<SpeakCompletedEventArgs>(OnCompleted), sender, e); return; }
            if (stopping || e.Cancelled || e.Prompt != currentPrompt) { return; }
            // The end of the text: next time from its start.
            mode = Mode.Stopped;
            box.ReadOnly = false;
            position = 0;
            finished = e.Error == null;      // (the bar stays empty until reading starts again or the text changes)
            if (e.Error != null) { Status("Reading stopped: " + e.Error.Message); }
            else { Status("Finished."); }
            box.Select(0, 0);
            Remember();
            UpdateButtons();
            UpdateTime();
        }

        // How fast this voice reads, in characters a second: measured once it has read for a few
        // seconds; a typical figure for the speed until then.
        double Pace()
        {
            if (charsPerSecond > 1) { return charsPerSecond; }
            return 15.0 * Math.Pow(3.0, rateBar.Value / 10.0);
        }

        public static string Clock(double seconds)
        {
            int s = (int)Math.Round(seconds);
            if (s >= 3600) { return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60); }
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", s / 60, s % 60);
        }

        void UpdateTime()
        {
            int length = box.TextLength;
            if (length == 0) { timeLabel.Text = ""; progress.Value = 0; return; }
            int remaining;
            if (mode != Mode.Stopped && prepared != null) { remaining = prepared.Text.Length - spokenAt; }
            else { remaining = length - Math.Min(length, position); }
            int left = finished ? 0 : 1000 - (int)(1000.0 * Math.Min(length, position) / length);
            progress.Value = Math.Max(0, Math.Min(1000, left));
            timeLabel.Text = "about " + Clock(remaining / Pace()) + " left";
            if (timeLabel.Width != alignedFor) { AlignLine(); }     // (a longer time: the bar gives way)
        }

        void ShowPlace()
        {
            if (position > box.TextLength) { position = box.TextLength; }
            box.Select(position, 0);
            box.ScrollToCaret();
            UpdateTime();
        }

        // Clicking (or moving) in the text while nothing is being read: Play starts from there.
        void CaretMoved(bool clicked = false)
        {
            if (mode != Mode.Stopped || loading) { return; }
            // (A click on the very place it stopped at counts too: from that word, not its sentence.)
            if (box.SelectionLength == 0 && (box.SelectionStart != position || clicked))
            {
                finished = false;
                position = box.SelectionStart;
                placeClicked = true;
                UpdateTime();
            }
        }

        // A click in the text while it is being read (or paused): reading goes on from the word clicked.
        public void ClickedWhileReading(int at)
        {
            if (mode == Mode.Stopped || loading || at < 0 || at >= box.TextLength) { return; }
            Halt();
            finished = false;
            position = at;
            StartAt(at, true);
        }
        int pressedAt = -1;
        public void TestClick(int at)
        {
            if (mode == Mode.Stopped) { box.Select(at, 0); CaretMoved(true); } else { ClickedWhileReading(at); }
        }

        void TextEdited()
        {
            // Text typed or pasted in: no longer the file's text as it was opened.
            finished = false;
            store.State.OpenedFile = "";
            if (position > box.TextLength) { position = box.TextLength; }
            UpdateButtons();
            UpdateTime();
        }

        // Puts a new text in the box (file: the file it is from, to keep its place for that file).
        void LoadText(string text, int at, string file)
        {
            if (mode != Mode.Stopped) { Halt(); }
            Remember();
            loading = true;
            store.State.OpenedFile = file;
            box.Text = Regex.Replace(text, "\r?\n", "\r\n");   // (the box shows a line break only as CR LF)
            loading = false;
            finished = false;
            NoteChange(true);
            position = Math.Max(0, Math.Min(box.TextLength, at));
            Remember();
            ShowPlace();
            UpdateButtons();
        }

        void ClearText()
        {
            LoadText("", 0, "");
            Status("");
            box.Focus();
        }

        // ---- files

        void ChooseFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Open a file to read";
                dialog.Filter = Files.Filter;
                if (dialog.ShowDialog(this) == DialogResult.OK) { OpenFile(dialog.FileName); }
            }
        }

        public void OpenFile(string path)
        {
            string text;
            // (A long PDF takes a moment to read through.)
            bool slow = Path.GetExtension(path).ToLowerInvariant() == ".pdf";
            if (slow) { Status("Opening " + Path.GetFileName(path) + "..."); Cursor = Cursors.WaitCursor; Update(); }
            try { text = Files.Read(path); }
            catch (Exception e)
            {
                Status("Could not open " + Path.GetFileName(path) + ": " + e.Message + ".");
                return;
            }
            finally { if (slow) { Cursor = Cursors.Default; } }
            if (text.Trim().Length == 0) { Status(Path.GetFileName(path) + " has no text in it."); return; }
            text = Regex.Replace(text, "\r?\n", "\r\n");
            string full = Path.GetFullPath(path);
            int at = store.PlaceIn(full, text.Length);
            LoadText(text, at, full);
            Status("Opened " + Path.GetFileName(path) + (at > 0 ? ": Play goes on from where you stopped in it." : "."));
        }

        // ---- text on the screen that can't be selected (pictures, games, some PDFs): a box is dragged
        // around it and Windows' text recognition reads it

        void ReadScreen()
        {
            if (recogniser != null) { return; }
            if (!File.Exists(Ocr.Script)) { Status("The text recognition helper is missing (" + Ocr.Script + "): install Modern Voices again."); return; }
            // What it is for, and that it can misread: asked each time until "Do not display this warning again" is ticked.
            if (!store.State.ScreenWarningOff)
            {
                CheckBox notAgain;
                using (Form ask = Program.ScreenWarningWindow(out notAgain))
                {
                    ask.StartPosition = FormStartPosition.CenterParent; ask.ShowInTaskbar = false;
                    if (ask.ShowDialog(this) != DialogResult.Yes) { return; }
                    if (notAgain.Checked) { store.State.ScreenWarningOff = true; store.Save(); }
                }
            }
            Rectangle screen = SystemInformation.VirtualScreen;
            // The screen as it is without this window.
            Hide();
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            Bitmap shot = new Bitmap(screen.Width, screen.Height);
            using (Graphics g = Graphics.FromImage(shot)) { g.CopyFromScreen(screen.Location, Point.Empty, screen.Size); }
            DialogResult answer;
            Rectangle picked;
            using (RegionPicker picker = new RegionPicker(shot, screen))
            {
                answer = picker.ShowDialog();
                picked = picker.Picked;
            }
            Show();
            Activate();
            picked.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
            if (answer != DialogResult.OK || picked.Width < 8 || picked.Height < 8)
            {
                shot.Dispose();
                Status("Nothing chosen.");
                return;
            }
            string image = Path.Combine(Path.GetTempPath(), "ModernVoicesReader-" + Guid.NewGuid().ToString("N") + ".png");
            using (Bitmap part = Ocr.Enlarged(shot, picked)) { part.Save(image, ImageFormat.Png); }
            shot.Dispose();
            Recognise(image, true);
        }

        public bool Recognising { get { return recogniser != null; } }

        public void Recognise(string image, bool thenPlay)
        {
            string output = image + ".txt";
            Status("Reading the text in the picture...");
            recogniser = Ocr.Prepare(image, output);
            recogniser.Exited += delegate
            {
                try { BeginInvoke(new MethodInvoker(delegate { Recognised(image, output, thenPlay); })); } catch (Exception) { }
            };
            try { recogniser.Start(); }
            catch (Exception e)
            {
                recogniser = null;
                Status("Could not start the text recognition: " + e.Message);
            }
        }

        void Recognised(string image, string output, bool thenPlay)
        {
            if (recogniser == null) { return; }
            int code = recogniser.ExitCode;
            recogniser.Dispose();
            recogniser = null;
            LastRecogniserCode = code;
            string text = "", problem = "";
            try
            {
                if (File.Exists(output)) { text = File.ReadAllText(output, Encoding.UTF8); File.Delete(output); }
                if (File.Exists(output + ".error")) { problem = File.ReadAllText(output + ".error"); File.Delete(output + ".error"); }
                File.Delete(image);
            }
            catch (Exception) { }
            if (code == 2) { Status("Windows has no text recognition for your language: add it in Settings > Time & language > Language (the language's optional features)."); }
            else if (code != 0) { Status("Could not read the picture" + (problem.Length > 0 ? ": " + problem : ".")); }
            else if (text.Trim().Length == 0) { Status("No text was found there."); }
            else
            {
                LastRecognised = text;
                LoadText(text, 0, "");
                Status("Read from the screen. Recognition is not perfect: check the text if something sounds wrong.");
                if (thenPlay) { Play(); }
            }
        }

        // ---- the text as a sound file (MP3 or WAV), made as fast as the voice can: not played. The voice
        // makes a WAV; for an MP3 that is a temporary file, which LAME then makes the MP3 from (Mp3).

        public const string AudioKinds = "MP3 audio, smaller (*.mp3)|*.mp3|WAV audio, as the voice made it (*.wav)|*.wav";
        void ChooseAudioFile()
        {
            if (Saving) { CancelSaving(); return; }
            if (box.TextLength == 0) { Status("Nothing to save: paste some text first."); return; }
            if (CurrentVoice == null) { Status("Choose a voice first."); return; }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                // (The kind made last time is offered first; without the MP3 encoder, WAV only.)
                bool mp3 = Mp3.Available && store.State.AudioKind != "wav";
                dialog.Title = "Save the text as audio";
                dialog.Filter = Mp3.Available ? AudioKinds : "WAV audio (*.wav)|*.wav";
                dialog.FilterIndex = mp3 || !Mp3.Available ? 1 : 2;
                dialog.AddExtension = true;
                dialog.FileName = Files.SafeName(Store.TitleOf(box.Text)) + (mp3 ? ".mp3" : ".wav");
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                string kind = Path.GetExtension(dialog.FileName).ToLowerInvariant() == ".mp3" ? "mp3" : "wav";
                if (Mp3.Available && store.State.AudioKind != kind) { store.State.AudioKind = kind; store.Save(); }
                SaveAudio(dialog.FileName);
            }
        }

        public bool Saving { get { return saver != null || encoding; } }

        public void SaveAudio(string path)
        {
            if (Saving) { return; }
            bool mp3 = Path.GetExtension(path).ToLowerInvariant() == ".mp3";
            if (mp3 && !Mp3.Available)
            {
                Status("The MP3 encoder is missing (" + Path.GetFileName(Mp3.Library) + "): install Modern Voices again, or save as WAV.");
                return;
            }
            savingWav = mp3 ? Path.Combine(Path.GetTempPath(), "ModernVoicesReader-" + Guid.NewGuid().ToString("N") + ".wav") : null;
            Prepared whole = Cleaner.Prepare(box.Text, tidyBox.Checked);
            int length = Math.Max(1, whole.Text.Length);
            try
            {
                saver = new SpeechSynthesizer();
                saver.SelectVoice(CurrentVoice.Spoken);
                saver.Rate = rateBar.Value;
                saver.Volume = volumeBar.Value;
                saver.SetOutputToWaveFile(savingWav ?? path, new SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                savingTo = path;
                saver.SpeakProgress += delegate(object sender, SpeakProgressEventArgs e)
                {
                    if (InvokeRequired || saver == null) { return; }
                    Status("Saving as audio: " + Math.Min(99, 100 * e.CharacterPosition / length) + "%  (" + Path.GetFileName(savingTo) + ")");
                };
                saver.SpeakCompleted += delegate(object sender, SpeakCompletedEventArgs e)
                {
                    bool finished = !e.Cancelled && e.Error == null;
                    string problem = e.Error == null ? "" : e.Error.Message;
                    // (Tidied up once this event is over: the voice can't be let go from inside it.)
                    BeginInvoke(new MethodInvoker(delegate { SavingEnded(finished, problem); }));
                };
                saver.SpeakAsync(whole.Text);
            }
            catch (Exception e)
            {
                SavingEnded(false, e.Message);
                return;
            }
            audioButton.Text = "Stop saving";
            Status("Saving as audio: 0%  (" + Path.GetFileName(path) + ")");
        }

        void CancelSaving()
        {
            if (encoding) { stopEncoding = true; return; }
            if (saver == null) { return; }
            try { saver.SpeakAsyncCancelAll(); } catch (Exception) { SavingEnded(false, ""); }
        }

        void SavingEnded(bool finished, string problem)
        {
            if (saver != null)
            {
                try { saver.SetOutputToNull(); } catch (Exception) { }
                try { saver.Dispose(); } catch (Exception) { }
                saver = null;
            }
            string spoken = savingWav ?? savingTo;
            if (finished && File.Exists(spoken))
            {
                double seconds = Math.Max(0, new FileInfo(spoken).Length - 44) / 44100.0;
                // An MP3: made from the WAV now, on another thread (a long text takes a while); the button
                // goes on reading "Stop saving" until it is done.
                if (savingWav != null) { StartEncoding(savingWav, savingTo, seconds); return; }
                audioButton.Text = "Save as audio...";
                SayAudioSaved(seconds);
            }
            else
            {
                audioButton.Text = "Save as audio...";
                try { if (File.Exists(spoken)) { File.Delete(spoken); } } catch (Exception) { }
                Status(problem.Length > 0 ? "Could not save the sound: " + problem : "Saving stopped: no file made.");
            }
        }

        void SayAudioSaved(double seconds)
        {
            long bytes = new FileInfo(savingTo).Length;
            Status("Saved " + Path.GetFileName(savingTo) + ": " + Clock(seconds) + " long, " +
                (bytes / 1048576.0).ToString(bytes < 1048576 ? "0.##" : "0.#", CultureInfo.InvariantCulture) + " MB.");
        }

        void StartEncoding(string wav, string target, double seconds)
        {
            encoding = true;
            stopEncoding = false;
            Status("Saving as audio: making the MP3...  (" + Path.GetFileName(target) + ")");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string failed = null;
                bool stopped = false;
                try
                {
                    Mp3.Encode(wav, target, delegate { return stopEncoding; }, delegate(int percent)
                    {
                        if (percent % 5 != 0) { return; }
                        Later(delegate { if (encoding) { Status("Saving as audio: making the MP3, " + percent + "%  (" + Path.GetFileName(target) + ")"); } });
                    });
                }
                catch (OperationCanceledException) { stopped = true; }
                catch (Exception e) { failed = e.Message; }
                try { File.Delete(wav); } catch (Exception) { }
                if (stopped || failed != null) { try { File.Delete(target); } catch (Exception) { } }
                Later(delegate
                {
                    encoding = false;
                    savingWav = null;
                    audioButton.Text = "Save as audio...";
                    if (failed != null) { Status("Could not save the sound: " + failed + "."); }
                    else if (stopped) { Status("Saving stopped: no file made."); }
                    else { SayAudioSaved(seconds); }
                });
            });
        }

        // Something for the window to do, asked from another thread (nothing, once the window has gone).
        void Later(MethodInvoker work)
        {
            try { if (IsHandleCreated && !IsDisposed) { BeginInvoke(work); } }
            catch (Exception) { }
        }

        // ---- the clipboard

        // What was copied, laid out as it was: from the formatted copy web pages and documents also
        // put on the clipboard (paragraphs apart, lists with their bullets and numbers), else the plain text.
        public static string ClipboardText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText(TextDataFormat.Html))
                    {
                        string made = Html.ToText(Html.Fragment(CopiedHtml()));
                        if (made.Trim().Length > 0) { return made; }
                    }
                    return Clipboard.ContainsText() ? Regex.Replace(Clipboard.GetText(), "\r?\n", "\r\n") : "";
                }
                catch (Exception) { System.Threading.Thread.Sleep(40); }   // another program has it open: again in a moment
            }
            return "";
        }

        // The formatted copy on the clipboard, read as the UTF-8 it is. (.NET's own Clipboard.GetText reads
        // it in Windows' code page: a curly apostrophe or an ellipsis came out as three wrong letters.)
        static string CopiedHtml()
        {
            byte[] bytes = null;
            uint format = RegisterClipboardFormat("HTML Format");
            if (format != 0 && OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    IntPtr memory = GetClipboardData(format);
                    if (memory != IntPtr.Zero)
                    {
                        IntPtr at = GlobalLock(memory);
                        if (at != IntPtr.Zero)
                        {
                            try
                            {
                                int size = (int)GlobalSize(memory).ToUInt64();
                                bytes = new byte[size];
                                Marshal.Copy(at, bytes, 0, size);
                            }
                            finally { GlobalUnlock(memory); }
                        }
                    }
                }
                finally { CloseClipboard(); }
            }
            if (bytes == null) { return Html.Utf8Again(Clipboard.GetText(TextDataFormat.Html)); }
            return Html.FromBytes(bytes);
        }

        void ReadClipboard(bool copiedJustNow)
        {
            string text = ClipboardText();
            if (text.Trim().Length == 0)
            {
                if (!copiedJustNow) { Status("The clipboard has no text: copy some first (Ctrl+C)."); }
                return;
            }
            if (copiedJustNow)
            {
                // Programs often report one copy several times.
                if (text == lastClipboard && (DateTime.UtcNow - lastClipboardAt).TotalSeconds < 2) { return; }
                lastClipboard = text; lastClipboardAt = DateTime.UtcNow;
            }
            LoadText(text, 0, "");
            Play();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { AddClipboardFormatListener(Handle); } catch (Exception) { }
            shownTheme = null; FollowTheme();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE && autoBox != null && autoBox.Checked && !ContainsFocus) { ReadClipboard(true); }
            
            base.WndProc(ref m);
        }

        void UpdateButtons()
        {
            bool has = box.TextLength > 0 && voiceBox.Items.Count > 0;
            bool playing = mode == Mode.Playing;
            playButton.Enabled = has;
            playButton.Text = symbols ? (playing ? "\uE769" : "\uE768") : (playing ? "Pause" : "Play");
            tips.SetToolTip(playButton, playing ? "Pause" : (mode == Mode.Paused ? "Go on" : "Play"));
            stopButton.Enabled = mode != Mode.Stopped;
            startButton.Enabled = has;
            undoButton.Enabled = before.Count > 0;
            redoButton.Enabled = undone.Count > 0;
        }

        // ---- undo and redo (the box's last ten changes)

        // The box's text has changed: what it held before is kept for Undo. Typing letter after letter is
        // one change (until a second's pause); anything else (a paste, a file, Clear) is a change of its own.
        void NoteChange(bool whole)
        {
            string now = box.Text;
            if (now == heldText) { return; }
            bool typing = !whole && Math.Abs(now.Length - heldText.Length) <= 2 && (DateTime.UtcNow - typedAt).TotalSeconds < 1.0 && before.Count > 0;
            if (!typing)
            {
                before.Add(new Held { Text = heldText, Caret = heldCaret });
                if (before.Count > UndoSteps) { before.RemoveAt(0); }
            }
            undone.Clear();
            typedAt = whole ? DateTime.MinValue : DateTime.UtcNow;
            heldText = now; heldCaret = box.SelectionStart;
        }

        public void Undo() { StepBack(before, undone); }
        public void Redo() { StepBack(undone, before); }

        // Puts back the newest text of `from`, keeping the box's present one in `into`.
        void StepBack(List<Held> from, List<Held> into)
        {
            if (from.Count == 0 || saver != null) { return; }
            if (mode != Mode.Stopped) { Halt(); }
            Held back = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            into.Add(new Held { Text = box.Text, Caret = box.SelectionStart });
            if (into.Count > UndoSteps) { into.RemoveAt(0); }
            loading = true;
            box.Text = back.Text;
            loading = false;
            box.SelectionStart = Math.Min(back.Caret, box.TextLength); box.SelectionLength = 0;
            box.ScrollToCaret();
            heldText = back.Text; heldCaret = box.SelectionStart;
            typedAt = DateTime.MinValue;
            position = Math.Min(position, box.TextLength);
            TextEdited();
            ShowPlace();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (mode != Mode.Stopped) { Halt(); }
            stopEncoding = true;
            if (saver != null) { try { saver.SpeakAsyncCancelAll(); } catch (Exception) { } SavingEnded(false, ""); }
            // Text in the box: kept for next time, if wanted ("Save text on close" not ticked: not asked, not kept).
            if (!silent && e.CloseReason == CloseReason.UserClosing && box.Text.Trim().Length > 0 && store.State.SaveOnClose)
            {
                CheckBox notAgain;
                using (Form ask = Program.SaveTextWindow(out notAgain))
                {
                    ask.StartPosition = FormStartPosition.CenterParent; ask.ShowInTaskbar = false;
                    bool keep = ask.ShowDialog(this) == DialogResult.Yes;
                    AnswerSaveText(keep, notAgain.Checked);
                }
            }
            Remember();
            try { RemoveClipboardFormatListener(Handle); } catch (Exception) { }
            if (nickHelper) { AskNickHelper("quit", 300); }      // (it would end by itself within a second too)
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { synth.Dispose(); } catch (Exception) { } }
            base.Dispose(disposing);
        }

        // ---- for the build's test

        public bool Reading { get { return mode != Mode.Stopped; } }
        public int Position { get { return position; } }
        public string BoxText { get { return box.Text; } }
        public void TestLoad(string text, int at) { LoadText(text, at, ""); }
        public void TestType(string text) { box.SelectionStart = box.TextLength; box.SelectedText = text; }
        public bool CanUndo { get { return undoButton.Enabled; } }
        public bool CanRedo { get { return redoButton.Enabled; } }
        public void TestPlace(int at) { position = at; }
        public string PlayLabel { get { return playButton.Text; } }
        public void TestTidy(bool on) { tidyBox.Checked = on; }
        public bool TestChooseOurs()
        {
            for (int i = 0; i < voiceBox.Items.Count; i++) { if (((VoiceChoice)voiceBox.Items[i]).Ours) { voiceBox.SelectedIndex = i; return true; } }
            return false;
        }
        public void Picture(string file)
        {
            using (Bitmap b = new Bitmap(Width, Height))
            {
                DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
                b.Save(file, ImageFormat.Png);
            }
        }
    }

    // ---------------------------------------------------------------- starting

    public static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--selftest") { return SelfTest.Run(args[1]); }
            // Already running: a second one only if wanted, else the one that is open comes to the front.
            bool first;
            System.Threading.Mutex running = new System.Threading.Mutex(true, "ModernVoicesReader.Running", out first);
            if (!first && !AnotherWanted()) { return 0; }
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modern Voices Reader");
            // The busy pointer over the program until its window is up.
            Application.UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            MainForm form = new MainForm(new Store(folder), false);
            form.Shown += delegate { Application.UseWaitCursor = false; Cursor.Current = Cursors.Default; };
            // A file given to the program (dropped on it, or "Open with") is opened.
            if (args.Length >= 1 && File.Exists(args[0]))
            {
                string given = args[0];
                form.Shown += delegate { form.OpenFile(given); };
            }
            Application.Run(form);
            GC.KeepAlive(running);
            return 0;
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hwnd, int show);

        // Asks, when the Reader is already open, whether to open another: Yes, or Cancel (the open one is shown).
        static bool AnotherWanted()
        {
            bool yes;
            using (Form ask = AlreadyRunningWindow())
            {
                yes = ask.ShowDialog() == DialogResult.Yes;
            }
            if (yes) { return true; }
            int self = System.Diagnostics.Process.GetCurrentProcess().Id;
            foreach (System.Diagnostics.Process other in System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
            {
                try
                {
                    if (other.Id != self && other.MainWindowHandle != IntPtr.Zero)
                    {
                        if (IsIconic(other.MainWindowHandle)) { ShowWindowAsync(other.MainWindowHandle, 9); }
                        SetForegroundWindow(other.MainWindowHandle);
                        break;
                    }
                }
                catch (Exception) { }
                finally { other.Dispose(); }
            }
            return false;
        }

        // The warning's window (in the theme the Reader wears): the question, Yes and Cancel.
        public static Form AlreadyRunningWindow()
        {
            CheckBox none;
            return AskWindow("Modern Voices is already running. Would you like to start a 2nd instance?", null, out none);
        }

        // Closing with text in the box: Yes keeps it for the next start, No lets it go. "Don't ask again"
        // unticks the Reader's "Save text on close".
        public const string DontAskAgain = "Don't ask again";
        public static Form SaveTextWindow(out CheckBox notAgain)
        {
            return AskWindow("Save text for next time?", DontAskAgain, out notAgain, "No");
        }

        public const string ScreenWarning = "The screen reader can make mistakes, and is intended for use on images containing text. " +
            "It is more accurate to copy and paste actual text when you can.\r\n\r\nContinue?";

        // Read screen's warning: the question, a box to tick so that it is not shown again, Yes and Cancel.
        public static Form ScreenWarningWindow(out CheckBox notAgain)
        {
            return AskWindow(ScreenWarning, "Do not display this warning again", out notAgain);
        }

        // A question's window (in the theme the Reader wears): the question, a box to tick under it when
        // 'tick' names one, Yes and Cancel.
        static Form AskWindow(string asked, string tick, out CheckBox box, string refusal = "Cancel")
        {
            Form ask = new Form();
            ask.Text = "Modern Voices: Reader";
            ask.FormBorderStyle = FormBorderStyle.FixedDialog;
            ask.MaximizeBox = false; ask.MinimizeBox = false; ask.ShowInTaskbar = true;
            ask.StartPosition = FormStartPosition.CenterScreen;
            ask.Font = new Font("Segoe UI", 9.75f);
            ask.AutoScaleMode = AutoScaleMode.None;
            ask.AutoSize = true; ask.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ask.Padding = new Padding(18, 16, 18, 14);
            try { ask.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            TableLayoutPanel rows = new TableLayoutPanel();
            rows.AutoSize = true; rows.AutoSizeMode = AutoSizeMode.GrowAndShrink; rows.ColumnCount = 1; rows.RowCount = 3;
            rows.Dock = DockStyle.Fill;
            Label question = new Label();
            question.AutoSize = true; question.Margin = new Padding(0, 0, 0, 16);
            if (tick != null) { question.MaximumSize = new Size(ask.Font.Height * 27, 0); }      // (a long question wraps)
            question.Text = asked;
            box = null;
            if (tick != null)
            {
                box = new CheckBox();
                box.Text = tick; box.AutoSize = true; box.Margin = new Padding(0, 0, 0, 16);
            }
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true; buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Anchor = AnchorStyles.Right; buttons.Margin = new Padding(0);
            Button cancel = new Button();
            cancel.Text = refusal; cancel.DialogResult = DialogResult.Cancel; cancel.AutoSize = true;
            cancel.MinimumSize = new Size(88, 30); cancel.Margin = new Padding(8, 0, 0, 0);
            Button yes = new Button();
            yes.Text = "Yes"; yes.DialogResult = DialogResult.Yes; yes.AutoSize = true;
            yes.MinimumSize = new Size(88, 30); yes.Margin = new Padding(0);
            buttons.Controls.Add(cancel); buttons.Controls.Add(yes);
            rows.Controls.Add(question, 0, 0);
            if (box != null) { rows.Controls.Add(box, 0, 1); }
            rows.Controls.Add(buttons, 0, 2);
            ask.Controls.Add(rows);
            ask.AcceptButton = yes; ask.CancelButton = cancel;
            Theme.Apply(ask, Theme.IsDark());
            return ask;
        }
    }

    // The build's test (ModernVoicesReader.exe --selftest FOLDER): what it finds goes in
    // FOLDER\reader-selftest.log, with pictures of the window; the exit code is the number of failures.
    public static class SelfTest
    {
        static StringBuilder log = new StringBuilder();
        static int failures;

        static void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) { failures++; }
        }

        // A PDF of plain pages for the test: each page its lines from the top, in 12 point Helvetica, 14
        // points apart; an empty line leaves a paragraph's gap. (Letters as Windows-1252.)
        public static byte[] TestPdf(string[][] pages)
        {
            Encoding latin = Encoding.GetEncoding(1252);
            List<byte[]> objects = new List<byte[]>();
            StringBuilder kids = new StringBuilder();
            for (int i = 0; i < pages.Length; i++) { kids.Append(4 + i * 2).Append(" 0 R "); }
            objects.Add(latin.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(latin.GetBytes("<< /Type /Pages /Kids [ " + kids + "] /Count " + pages.Length + " >>"));
            objects.Add(latin.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            foreach (string[] lines in pages)
            {
                StringBuilder content = new StringBuilder("BT /F1 12 Tf 72 780 Td ");
                int down = 0;
                foreach (string line in lines)
                {
                    if (line.Length == 0) { down += 20; continue; }
                    content.Append("0 -").Append(down).Append(" Td (").Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj ");
                    down = 14;
                }
                content.Append("ET");
                byte[] stream = latin.GetBytes(content.ToString());
                objects.Add(latin.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents " + (objects.Count + 2) + " 0 R >>"));
                List<byte> body = new List<byte>(latin.GetBytes("<< /Length " + stream.Length + " >>\nstream\n"));
                body.AddRange(stream);
                body.AddRange(latin.GetBytes("\nendstream"));
                objects.Add(body.ToArray());
            }
            using (MemoryStream file = new MemoryStream())
            {
                byte[] head = latin.GetBytes("%PDF-1.4\n");
                file.Write(head, 0, head.Length);
                List<long> places = new List<long>();
                for (int i = 0; i < objects.Count; i++)
                {
                    places.Add(file.Position);
                    byte[] open = latin.GetBytes((i + 1) + " 0 obj\n"), close = latin.GetBytes("\nendobj\n");
                    file.Write(open, 0, open.Length); file.Write(objects[i], 0, objects[i].Length); file.Write(close, 0, close.Length);
                }
                long table = file.Position;
                StringBuilder tail = new StringBuilder("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long place in places) { tail.Append(place.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n"); }
                tail.Append("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + table + "\n%%EOF\n");
                byte[] end = latin.GetBytes(tail.ToString());
                file.Write(end, 0, end.Length);
                return file.ToArray();
            }
        }

        // A WAV file for the test: a tone, 22050 Hz, 16-bit, one channel.
        public static void TestWav(string path, double seconds)
        {
            int samples = (int)(22050 * seconds);
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples * 2);
                for (int i = 0; i < samples; i++) { w.Write((short)(8000 * Math.Sin(i * 2 * Math.PI * 440 / 22050))); }
            }
        }

        // An MP3 file as LAME makes it here: it starts with a frame (0xFF, then 0xE or 0xF in the next
        // byte's top bits), and its length is its size at the rate asked for.
        public static double Mp3Seconds(string path)
        {
            byte[] start = new byte[2];
            using (FileStream f = File.OpenRead(path)) { if (f.Read(start, 0, 2) < 2) { return 0; } }
            if (start[0] != 0xFF || (start[1] & 0xE0) != 0xE0) { return 0; }
            return new FileInfo(path).Length * 8.0 / (Mp3.Kbps * 1000);
        }

        static bool WaitWhile(Func<bool> busy, int seconds)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (busy() && DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            return !busy();
        }

        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            string was = Theme.Choice;
            try
            {
                // Tidying, and the places kept through it.
                string marked = "# A heading\r\n\r\nSome **bold** words, a [link](https://example.com/a) and https://example.com/long/address here.\r\n\r\n```\r\ncode();\r\n```\r\n\r\n- one\r\n- two | three\r\n";
                Prepared tidy = Cleaner.Prepare(marked, true);
                log.AppendLine("tidied: " + tidy.Text.Replace("\r\n", " / "));
                Check(!tidy.Text.Contains("#") && !tidy.Text.Contains("*") && !tidy.Text.Contains("http") && !tidy.Text.Contains("code();") && !tidy.Text.Contains("`"),
                    "tidying leaves out the heading mark, the stars, the addresses and the code");
                Check(tidy.Text.Contains("Some bold words, a link and link here."), "tidying keeps the words (and a link's own)");
                int bold = tidy.Text.IndexOf("bold", StringComparison.Ordinal);
                Check(marked.Substring(tidy.Original(bold), 4) == "bold", "a tidied place maps back to the original");
                Prepared plain = Cleaner.Prepare(marked, false);
                Check(plain.Text == marked && plain.Original(7) == 7, "without tidying the text is as it is");
                string two = "First sentence here. Second one follows! Third?";
                Check(Cleaner.SentenceStart(two, 30) == 21 && Cleaner.SentenceStart(two, 5) == 0 && Cleaner.SentenceStart(two, 21) == 21,
                    "a sentence's start is found from a place in it");
                Check(MainForm.Clock(75) == "1:15" && MainForm.Clock(3725) == "1:02:05", "times are shown as m:ss and h:mm:ss");

                // Files: plain text however it is stored, a Word document, a web page.
                string made = Path.Combine(folder, "files");
                if (Directory.Exists(made)) { Directory.Delete(made, true); }
                Directory.CreateDirectory(made);
                File.WriteAllText(Path.Combine(made, "utf8.txt"), "Caf\u00e9 na\u00efve \u2013 text.", new UTF8Encoding(false));
                File.WriteAllBytes(Path.Combine(made, "ansi.txt"), Encoding.GetEncoding(1252).GetBytes("Caf\u00e9 na\u00efve text."));
                File.WriteAllText(Path.Combine(made, "utf16.txt"), "Caf\u00e9 text.", Encoding.Unicode);
                Check(Files.Read(Path.Combine(made, "utf8.txt")) == "Caf\u00e9 na\u00efve \u2013 text." && Files.Read(Path.Combine(made, "utf16.txt")) == "Caf\u00e9 text.",
                    "plain text is read as UTF-8 and UTF-16");
                log.AppendLine("ansi: " + Files.Read(Path.Combine(made, "ansi.txt")) + " (this PC's code page: " + Encoding.Default.CodePage + ")");
                Check(Encoding.Default.CodePage != 1252 || Files.Read(Path.Combine(made, "ansi.txt")) == "Caf\u00e9 na\u00efve text.", "plain text that is not UTF-8 is read in Windows' own code page");
                string word = Path.Combine(made, "letter.docx");
                using (ZipArchive zip = ZipFile.Open(word, ZipArchiveMode.Create))
                {
                    using (StreamWriter w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false)))
                    {
                        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                            "<w:p><w:r><w:t>Dear reader,</w:t></w:r></w:p>" +
                            "<w:p><w:r><w:t xml:space=\"preserve\">This is </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t xml:space=\"preserve\"> and</w:t><w:tab/><w:t>tabbed.</w:t><w:br/><w:t>Next line.</w:t></w:r></w:p>" +
                            "<w:p><w:r><w:instrText>PAGE</w:instrText></w:r></w:p></w:body></w:document>");
                    }
                }
                // PDFs: lines made into paragraphs (tested without a PDF: Tidy takes pages of lines).
                List<string> pdfPages = new List<string>();
                pdfPages.Add("A Running Head\r\n\r\nChapter One\r\n\r\nThis line ends with a hyphen-\r\nated word, and this one with a well-\r\nKnown name. A sentence starts here and\r\nruns on to the next line. It ends\r\nshort.\r\nA paragraph with no gap before it starts on this line and\r\nthen it stops in the middle of a\r\n\r\n1");
                pdfPages.Add("A Running Head\r\n\r\nsentence, which goes on over the page.\r\n\r\nSecond page, second paragraph.\r\n\r\nPage 2 of 3");
                pdfPages.Add("A Running Head\r\n\r\nThe last page.\r\n\r\n- 3 -");
                string tidied = Pdf.Tidy(pdfPages);
                log.AppendLine("pdf lines tidied: " + tidied.Replace("\r\n\r\n", " / "));
                Check(tidied == "Chapter One\r\n\r\nThis line ends with a hyphenated word, and this one with a well-Known name. A sentence starts here and runs on to the next line. It ends short.\r\n\r\n" +
                    "A paragraph with no gap before it starts on this line and then it stops in the middle of a sentence, which goes on over the page.\r\n\r\nSecond page, second paragraph.\r\n\r\nThe last page.",
                    "a PDF's lines become paragraphs: broken words joined, page numbers and running heads left out, a paragraph goes on over a page");
                Check(Pdf.Tidy(new List<string>(new string[] { "", "  \r\n" })) == "", "a PDF with no text gives none");
                // Lists: each item a paragraph of its own, its bullet not read.
                List<string> listPage = new List<string>();
                listPage.Add("Things to bring:\r\n\u2022 A coat\r\n\u25B6 Boots that\r\nfit well\r\n\uF0D8 Water\r\nl A map\r\n\u2022\r\nA torch\r\n- Food\r\n\r\n-5 degrees at night \uF0A7 or colder.");
                List<HashSet<string>> listMarks = new List<HashSet<string>>();
                listMarks.Add(new HashSet<string>(new string[] { "l", "\uF0D8" }));
                string listed = Pdf.Tidy(listPage, listMarks);
                log.AppendLine("pdf list tidied: " + listed.Replace("\r\n\r\n", " / "));
                Check(listed == "Things to bring:\r\n\r\nA coat\r\n\r\nBoots that fit well\r\n\r\nWater\r\n\r\nA map\r\n\r\nA torch\r\n\r\nFood\r\n\r\n-5 degrees at night or colder.",
                    "a PDF's list: each item its own paragraph, without its bullet (dots, arrowheads, a symbol font's letters, dashes)");
                // A real PDF, read by the PDF reader beside this program.
                Check(Pdf.Available, "the PDF reader is beside the Reader (" + Path.GetFileName(Pdf.Library) + ")");
                string pdf = Path.Combine(made, "pages.pdf");
                if (Pdf.Available)
                {
                    File.WriteAllBytes(pdf, TestPdf(new string[][] {
                        new string[] { "A Heading", "", "The first paragraph starts on this line, which is long enough,", "and goes on to a second line (with brackets) and a caf\u00e9.", "", "A second paragraph, after a gap, ends in the mid-", "dle of a word. Then it stops in the middle of a" },
                        new string[] { "sentence that goes on over the page.", "", "The last paragraph.", "\u2022 A listed item", "\u2022 Another one", "", "2" } }));
                    List<string> rawPages = Pdf.Pages(pdf);
                    for (int i = 0; i < rawPages.Count; i++) { log.AppendLine("pdf page " + (i + 1) + " as read: " + rawPages[i].Replace("\r", "").Replace("\n", " | ")); }
                    string fromPdf = Files.Read(pdf);
                    log.AppendLine("pdf: " + fromPdf.Replace("\r\n\r\n", " / "));
                    Check(fromPdf == "A Heading\r\n\r\nThe first paragraph starts on this line, which is long enough, and goes on to a second line (with brackets) and a caf\u00e9.\r\n\r\n" +
                        "A second paragraph, after a gap, ends in the middle of a word. Then it stops in the middle of a sentence that goes on over the page.\r\n\r\nThe last paragraph.\r\n\r\nA listed item\r\n\r\nAnother one",
                        "a PDF is read: its paragraphs, accents, brackets and a list, across two pages");
                    string noText = Path.Combine(made, "scan.pdf");
                    File.WriteAllBytes(noText, TestPdf(new string[][] { new string[] { } }));
                    string scanSaid = "";
                    try { Files.Read(noText); } catch (InvalidOperationException e) { scanSaid = e.Message; }
                    string notPdf = Path.Combine(made, "broken.pdf");
                    File.WriteAllText(notPdf, "This is not a PDF at all.");
                    string brokenSaid = "";
                    try { Files.Read(notPdf); } catch (InvalidOperationException e) { brokenSaid = e.Message; }
                    log.AppendLine("a PDF without text: " + scanSaid + "; not a PDF: " + brokenSaid);
                    Check(scanSaid.Contains("no text") && brokenSaid.Contains("could not be read"), "a PDF with no text, and a file that is no PDF, are refused with a reason");
                }
                // MP3: a tone encoded by the encoder beside this program.
                Check(Mp3.Available, "the MP3 encoder is beside the Reader (" + Path.GetFileName(Mp3.Library) + ")");
                if (Mp3.Available)
                {
                    string tone = Path.Combine(made, "tone.wav"), toneMp3 = Path.Combine(made, "tone.mp3");
                    TestWav(tone, 3);
                    int lastPercent = -1;
                    Mp3.Encode(tone, toneMp3, null, delegate(int percent) { lastPercent = percent; });
                    double toneSeconds = Mp3Seconds(toneMp3);
                    log.AppendLine("a 3 s tone as MP3: " + new FileInfo(toneMp3).Length + " bytes, " + toneSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s at " + Mp3.Kbps + " kbit/s; the WAV is " + new FileInfo(tone).Length + " bytes");
                    Check(toneSeconds > 2.9 && toneSeconds < 3.3 && lastPercent == 100, "a WAV file is made into an MP3 of the same length");
                    bool gaveUp = false;
                    try { Mp3.Encode(tone, toneMp3, delegate { return true; }, null); } catch (OperationCanceledException) { gaveUp = true; }
                    Check(gaveUp, "making an MP3 can be stopped");
                }
                string fromWord = Files.Read(word);
                log.AppendLine("docx: " + fromWord.Replace("\r\n", " / "));
                Check(fromWord == "Dear reader,\r\nThis is bold and tabbed.\r\nNext line.\r\n\r\n", "a Word document's paragraphs, runs, tabs and line breaks are read (not its field codes)");
                string fromHtml = Html.ToText("<html><head><title>T</title><style>p{color:red}</style></head><body><h1>Heading</h1><p>One &amp; two<br>three.</p><script>x()</script><p>Last&nbsp;one.</p></body></html>");
                log.AppendLine("html: " + fromHtml.Replace("\r\n", " / "));
                Check(fromHtml == "Heading\r\n\r\nOne & two\r\nthree.\r\n\r\nLast\u00a0one.", "a web page's words are read, an empty line between its paragraphs (not its scripts or styles)");
                string copied = "Version:0.9\r\nStartHTML:0\r\n<html><body><!--StartFragment--><p>Intro:</p><ul><li>First</li><li>Second <b>bold</b><ul><li>Nested</li></ul></li></ul>" +
                    "<ol><li><p>One</p></li><li><p>Two</p></li></ol><p>End.</p><!--EndFragment--></body></html>";
                string laidOut = Html.ToText(Html.Fragment(copied));
                string curly = "I\u2019d typed it out\u2026 \u00a3150";
                Check(Html.FromBytes(Encoding.UTF8.GetBytes("<p>" + curly + "</p>\0\0")) == "<p>" + curly + "</p>", "a formatted copy is read as UTF-8: curly quotes, ellipses and pound signs stay themselves");
                Check(Encoding.Default.CodePage != 1252 || Html.Utf8Again(Encoding.Default.GetString(Encoding.UTF8.GetBytes(curly))) == curly, "UTF-8 misread in Windows' code page is put right");
                log.AppendLine("copied: " + laidOut.Replace("\r\n", " / "));
                Check(laidOut == "Intro:\r\n\r\n\u2022 First\r\n\u2022 Second bold\r\n    \u2022 Nested\r\n\r\n1. One\r\n2. Two\r\n\r\nEnd.", "a formatted copy keeps its paragraphs, bullets and numbers");
                Check(Cleaner.Prepare("\u2022 First\r\n\u2022 Second", true).Text == "First\r\nSecond", "bullets are not read out (with tidying on)");
                Check(Files.SafeName("What: is/this? ") == "What is this", "a file name is made from a title");

                // The window, with nothing kept from before.
                string data = Path.Combine(folder, "data");
                if (Directory.Exists(data)) { Directory.Delete(data, true); }
                MainForm form = new MainForm(new Store(data), true);
                form.Show();
                Application.DoEvents();
                log.AppendLine("voices: " + form.VoicesShown);
                Check(form.VoiceCount > 0, "voices are listed (" + form.VoiceCount + ", " + form.OurVoiceCount + " Modern Voices)");
                if (form.VoiceCount > 0)
                {
                    // The dot of a voice in the list: Windows' default voice (put back as it was).
                    object defaultWas = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Speech\Voices", "DefaultTokenId", null);
                    int last = form.VoiceCount - 1;
                    form.TestSetDefault(last);
                    string now = Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Speech\Voices", "DefaultTokenId", ""));
                    Check(form.TestStartVoice() == last, "the voice selected as the Reader opens is Windows' default voice");
                    Check(form.DefaultToken == form.TestToken(last) && now == @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\" + form.TestToken(last), "a voice's dot makes it Windows' default voice (" + form.DefaultToken + ")");
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Speech\Voices"))
                    {
                        if (defaultWas == null) { key.DeleteValue("DefaultTokenId", false); } else { key.SetValue("DefaultTokenId", defaultWas); }
                    }
                }
                if (form.VoiceCount > 0)
                {
                    object defaultThen = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Speech\Voices", "DefaultTokenId", null);
                    string came = form.TestDotClick(0);
                    Check(came == "default " + form.TestToken(0) + "; list open; chosen unchanged; taken 1", "a click on a row's dot in the dropped list: that voice is the default, the list stays open, the voice read with stays (" + came + ")");
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Speech\Voices"))
                    {
                        if (defaultThen == null) { key.DeleteValue("DefaultTokenId", false); } else { key.SetValue("DefaultTokenId", defaultThen); }
                    }
                }
                form.TestStatus("A message");
                form.FadeStatus(4.9);
                bool there = form.StatusText == "A message";
                form.FadeStatus(5.3);
                bool fading = form.StatusText == "A message";
                form.FadeStatus(5.7);
                Check(there && fading && form.StatusText == "", "a message on the last line is there for five seconds, fades, and is gone");
                Check(form.TopRowEven, "the nickname box and its tick are as tall as the voice list (" + form.TopRow + ")");
                Check(MainForm.CleanNickname("  My (best)\r\n voice ") == "My best voice", "a nickname is tidied (no brackets or line ends)");
                // Text in a picture (where this PC has Windows' text recognition).
                string picture = Path.Combine(made, "words.png");
                using (Bitmap drawn = new Bitmap(900, 300))
                {
                    using (Graphics g = Graphics.FromImage(drawn))
                    using (Font face = new Font("Segoe UI", 22f))
                    {
                        g.Clear(Color.White);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        g.DrawString("The quick brown fox jumps", face, Brushes.Black, 20, 20);
                        g.DrawString("over the lazy dog.", face, Brushes.Black, 20, 62);
                        g.DrawString("A new paragraph follows.", face, Brushes.Black, 20, 180);
                    }
                    drawn.Save(picture, ImageFormat.Png);
                }
                Check(File.Exists(Ocr.Script), "the text recognition helper is installed (" + Ocr.Script + ")");
                form.Recognise(picture, false);
                bool answered = WaitWhile(delegate { return form.Recognising; }, 120);
                log.AppendLine("picture read (code " + form.LastRecogniserCode + "): " + form.LastRecognised.Replace("\r\n", " / "));
                if (form.LastRecogniserCode == 2) { log.AppendLine("(this PC has no text recognition installed: reading a picture not tried)"); }
                else
                {
                    Check(answered && form.LastRecogniserCode == 0 && form.LastRecognised.ToLowerInvariant().Contains("quick brown fox"), "the words in a picture are read");
                    Check(form.LastRecognised.Contains("dog.\r\nA new"), "a gap between lines starts a new paragraph; lines close together are joined");
                }

                string text = "The quick brown fox jumps over the lazy dog. Then it reads a second sentence, with a comma.\r\nAnd a new paragraph to finish with.";
                form.TestLoad(text, 0);
                Theme.Choice = "light"; form.FollowTheme(); Application.DoEvents();
                form.Picture(Path.Combine(folder, "reader-light.png"));
                Theme.Choice = "dark"; form.FollowTheme(); Application.DoEvents();
                form.Picture(Path.Combine(folder, "reader-dark.png"));
                Check(true, "pictures of the window, light and dark");

                if (form.OurVoiceCount > 0 && form.TestChooseOurs())
                {
                    int barBefore = form.BarValue;
                    form.Play();
                    Check(form.Reading, "Play starts reading");
                    bool done = WaitWhile(delegate { return form.Reading; }, 90);
                    Check(done, "reading reaches the end");
                    Check(barBefore == 1000 && form.BarValue == 0, "the bar is full before reading and empty when it has finished (" + barBefore + ", " + form.BarValue + ")");
                    Check(form.ProgressEvents >= 15, "the place is followed word by word (" + form.ProgressEvents + " words reported, first '" + form.FirstWord + "', last '" + form.LastWord + "')");
                    Check(form.Misplaced == 0, "each word is reported at its own place in the text (" + form.Misplaced + " misplaced)");
                    Check(form.FirstWord == "The" && form.LastWord.StartsWith("with", StringComparison.Ordinal), "the first and last words are the text's");
                    Check(form.Position == 0, "at the end, the place goes back to the start");

                    // From blank lines after the last sentence (a click below the text): Play reads from the start.
                    string trailing = text + "\r\n\r\n";
                    form.TestLoad(trailing, trailing.Length - 1);
                    form.Play();
                    WaitWhile(delegate { return form.FirstWord == "" && form.Reading; }, 60);
                    Check(form.FirstWord == "The", "Play with nothing left after the place reads from the start (first word '" + form.FirstWord + "')");
                    form.Stop();

                    // The volume slider sets the Reader's own sound at once, while reading too.
                    form.TestVolume(40);
                    int session = form.SessionVolume;
                    if (session < 0) { log.AppendLine("(this PC would not report the Reader's sound volume: the slider not checked)"); }
                    else { Check(session == 40, "the volume slider sets the Reader's sound (" + session + "%)"); }
                    form.TestVolume(100);

                    // From a place in the second sentence: reading starts at that sentence.
                    form.TestLoad(text, text.IndexOf("second", StringComparison.Ordinal));
                    form.Play();
                    WaitWhile(delegate { return form.FirstWord == "" && form.Reading; }, 60);
                    Check(form.FirstWord == "Then", "Play from a place goes on from the start of its sentence (first word '" + form.FirstWord + "')");
                    // A click on a word while it reads: it goes on from that very word. And clicked while stopped, Play does.
                    form.TestClick(text.IndexOf("second", StringComparison.Ordinal) + 2);
                    WaitWhile(delegate { return form.FirstWord == "" && form.Reading; }, 60);
                    Check(form.Reading && form.FirstWord == "second", "a click in the text while it reads goes on from the word clicked (first word '" + form.FirstWord + "')");
                    form.Stop();
                    form.TestClick(text.IndexOf("second", StringComparison.Ordinal));
                    form.Play();
                    WaitWhile(delegate { return form.FirstWord == "" && form.Reading; }, 60);
                    Check(form.FirstWord == "second", "a click while stopped: Play reads from the word clicked (first word '" + form.FirstWord + "')");
                    form.Stop();
                    form.TestLoad(text, text.IndexOf("second", StringComparison.Ordinal));
                    form.Play();
                    WaitWhile(delegate { return form.FirstWord == "" && form.Reading; }, 60);
                    form.PauseOrResume();
                    int paused = form.Position;
                    Check(form.Reading, "Pause keeps the reading");
                    form.Stop();
                    Check(!form.Reading && form.Position == paused && paused > 0, "Stop keeps the place (" + paused + ")");

                    // A file opened into the box; the text saved as sound.
                    form.OpenFile(word);
                    Check(form.BoxText == fromWord && form.Position == 0, "Open a file puts its text in the box");
                    form.TestLoad(text, paused);
                    string wav = Path.Combine(made, "saved.wav");
                    form.SaveAudio(wav);
                    Check(form.Saving, "saving as sound starts");
                    bool saved = WaitWhile(delegate { return form.Saving; }, 120);
                    double seconds = File.Exists(wav) ? (new FileInfo(wav).Length - 44) / 44100.0 : 0;
                    Check(saved && seconds > 4 && seconds < 40, "the text is saved as a WAV file (" + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s of sound)");
                    // The same as an MP3: as long as the WAV, a fraction of its size, and no temporary file left.
                    if (Mp3.Available)
                    {
                        string mp3 = Path.Combine(made, "said.mp3");
                        int tempBefore = Directory.GetFiles(Path.GetTempPath(), "ModernVoicesReader-*.wav").Length;
                        form.SaveAudio(mp3);
                        Check(form.Saving, "saving as MP3 starts");
                        bool savedMp3 = WaitWhile(delegate { return form.Saving; }, 120);
                        double mp3Seconds = File.Exists(mp3) ? Mp3Seconds(mp3) : 0;
                        log.AppendLine("saved as MP3: " + (File.Exists(mp3) ? new FileInfo(mp3).Length : 0) + " bytes, " + mp3Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s; the WAV " + new FileInfo(wav).Length + " bytes; the Reader says: " + form.StatusText);
                        Check(savedMp3 && Math.Abs(mp3Seconds - seconds) < 1.5 && new FileInfo(mp3).Length * 4 < new FileInfo(wav).Length, "the text is saved as an MP3 file (" + mp3Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s of sound)");
                        Check(Directory.GetFiles(Path.GetTempPath(), "ModernVoicesReader-*.wav").Length <= tempBefore && form.StatusText.StartsWith("Saved said.mp3"), "the WAV it was made from is gone, and the Reader says what was saved");
                    }
                    if (Pdf.Available)
                    {
                        form.OpenFile(Path.Combine(made, "pages.pdf"));
                        Check(form.BoxText.StartsWith("A Heading\r\n\r\nThe first paragraph") && form.BoxText.EndsWith("Another one"), "Open a file opens a PDF");
                    }

                    // A file's place is kept for when it is opened again.
                    string plainFile = Path.Combine(made, "utf8.txt");
                    form.OpenFile(plainFile);
                    form.TestPlace(5);
                    form.OpenFile(word);
                    form.OpenFile(plainFile);
                    Check(form.Position == 5, "a file opened again goes on from where it was left (" + form.Position + ")");
                    // Undo and redo: each change of the text a step, ten kept.
                    form.TestLoad("one", 0);
                    form.TestLoad("two", 0);
                    form.TestType(" three");
                    form.Undo();
                    bool typedUndone = form.BoxText == "two";
                    form.Undo();
                    bool loadUndone = form.BoxText == "one" && form.CanRedo;
                    form.Redo(); form.Redo();
                    Check(typedUndone && loadUndone && form.BoxText == "two three" && !form.CanRedo, "undo and redo: a typed change and a loaded text, back and forward (" + form.BoxText + ")");
                    form.TestLoad("", 0);
                    form.Undo();
                    Check(form.BoxText == "two three", "Clear is undone");
                    for (int i = 0; i < 14; i++) { form.TestLoad("text " + i, 0); }
                    int steps = 0;
                    while (form.CanUndo && steps < 30) { form.Undo(); steps++; }
                    Check(steps == 10 && form.BoxText == "text 3", "ten changes can be undone (" + steps + ", back to '" + form.BoxText + "')");
                    form.TestType("x");
                    Check(!form.CanRedo, "a new change after undoing: nothing left to redo");
                    form.TestLoad(text, paused);
                    Check(form.PlayLabel == (form.PlayLabel.Length == 1 ? "\uE768" : "Play"), "the play button shows play when nothing is being read");
                    // "Save text for next time?": Yes keeps the box's text for the next start (once); No, or no
                    // answer, starts empty.
                    form.KeepTextForNextTime(true);
                    form.Close();
                    form.Dispose();
                    MainForm kept = new MainForm(new Store(data), true);
                    Check(kept.BoxText == text, "Save text for next time, Yes: the next start has the text");
                    kept.KeepTextForNextTime(false);
                    kept.Close();
                    kept.Dispose();
                    CheckBox saveAgain;
                    using (Form saveAsk = Program.SaveTextWindow(out saveAgain))
                    {
                        saveAsk.Show(); Application.DoEvents();
                        Check(saveAsk.AcceptButton != null && saveAsk.CancelButton != null, "closing with text asks \"Save text for next time?\": Yes and No");
                        Check(saveAgain != null && !saveAgain.Checked && saveAgain.Text == Program.DontAskAgain, "it has a \"Don't ask again\" box, not ticked");
                        try { using (Bitmap shot = new Bitmap(saveAsk.Width, saveAsk.Height)) { saveAsk.DrawToBitmap(shot, new Rectangle(0, 0, saveAsk.Width, saveAsk.Height)); shot.Save(Path.Combine(folder, "reader-save-text.png")); } } catch (Exception) { }
                        saveAsk.Close();
                    }
                    MainForm again = new MainForm(new Store(data), true);
                    Check(again.BoxText == "", "No: the next start is empty");
                    again.Show();
                    Application.DoEvents();
                    // "Save text on close": ticked to begin with, under Undo; "Don't ask again" unticks it for good.
                    System.Threading.Thread.Sleep(50); Application.DoEvents();
                    log.AppendLine("\"" + again.SaveOnCloseWords + "\" starts " + again.SaveBoxOffset + " px from Undo's left edge");
                    Check(new State().SaveOnClose && again.SaveOnCloseTicked && again.SaveOnCloseWords == "Save text on close", "\"Save text on close\" is ticked to begin with");
                    Check(Math.Abs(again.SaveBoxOffset) <= 1 && again.SaveBoxFits, "it starts where the Undo button does, under it, and fits in the window");
                    again.AnswerSaveText(false, true);
                    Check(!again.SaveOnCloseTicked && !new Store(data).State.SaveOnClose, "\"Don't ask again\" unticks it, and that is kept");
                    again.SaveOnCloseTicked = true;
                    Check(new Store(data).State.SaveOnClose, "ticked again in the Reader: kept too");
                    using (Form ask = Program.AlreadyRunningWindow())
                    {
                        ask.Show(); Application.DoEvents();
                        Check(ask.AcceptButton != null && ask.CancelButton != null && ask.Width > 300, "already running: the warning has its question, Yes and Cancel");
                        try { using (Bitmap shot = new Bitmap(ask.Width, ask.Height)) { ask.DrawToBitmap(shot, new Rectangle(0, 0, ask.Width, ask.Height)); shot.Save(Path.Combine(folder, "reader-already-running.png")); } } catch (Exception) { }
                        ask.Close();
                    }
                    // Read screen's warning: its words, the box to tick, Yes and Cancel.
                    CheckBox warnBox;
                    using (Form warnForm = Program.ScreenWarningWindow(out warnBox))
                    {
                        warnForm.Show();
                        Application.DoEvents();
                        int warnAnswers = 0;
                        string warnWords = "";
                        foreach (Control warnRow in warnForm.Controls[0].Controls)
                        {
                            if (warnRow is Label) { warnWords = warnRow.Text; }
                            foreach (Control warnPart in warnRow.Controls) { if (warnPart is Button && (warnPart.Text == "Yes" || warnPart.Text == "Cancel")) { warnAnswers++; } }
                        }
                        Check(warnWords == Program.ScreenWarning && warnBox != null && !warnBox.Checked && warnBox.Text == "Do not display this warning again" && warnAnswers == 2,
                            "Read screen's warning: its words, a box to not show it again (unticked), Yes and Cancel");
                        try { using (Bitmap shot = new Bitmap(warnForm.Width, warnForm.Height)) { warnForm.DrawToBitmap(shot, new Rectangle(0, 0, warnForm.Width, warnForm.Height)); shot.Save(Path.Combine(folder, "reader-screen-warning.png")); } } catch (Exception) { }
                        warnForm.Close();
                    }
                    Check(!new State().ScreenWarningOff, "the warning is shown until it is turned off");
                    Check(again.BoxText == "" && again.Position == 0 && !File.Exists(Path.Combine(data, "current.txt")), "opened again: the box is empty (its text is not kept)");
                    again.Close();
                    again.Dispose();
                }
                else
                {
                    log.AppendLine("(no Modern Voices installed: reading not tried)");
                    form.Close();
                    form.Dispose();
                }
            }
            catch (Exception e)
            {
                log.AppendLine("  FAIL stopped: " + e);
                failures++;
            }
            finally { Theme.Choice = was; }
            File.WriteAllText(Path.Combine(folder, "reader-selftest.log"), log.ToString(), Encoding.UTF8);
            return failures;
        }
    }
}
