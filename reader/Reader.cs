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
[assembly: System.Reflection.AssemblyVersion("0.12.4.0")]

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
        public const string Filter = "Text and documents (*.txt, *.md, *.docx, *.rtf, *.html)|*.txt;*.md;*.markdown;*.text;*.log;*.docx;*.rtf;*.html;*.htm|All files (*.*)|*.*";

        // The text of a file, by its kind: Word documents (.docx), rich text (.rtf), web pages
        // (.html); anything else is read as plain text.
        public static string Read(string path)
        {
            if (new FileInfo(path).Length > 30 * 1024 * 1024) { throw new InvalidOperationException("the file is larger than 30 MB"); }
            string kind = Path.GetExtension(path).ToLowerInvariant();
            if (kind == ".docx") { return FromWord(path); }
            if (kind == ".rtf")
            {
                using (RichTextBox rich = new RichTextBox()) { rich.Rtf = Plain(path); return rich.Text; }
            }
            if (kind == ".html" || kind == ".htm") { return Html.ToText(Plain(path)); }
            if (kind == ".doc" || kind == ".pdf" || kind == ".odt" || kind == ".epub")
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
        CheckBox autoBox, tidyBox;
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
        // buttons, the time after it, "Instantly read copy" starting where Paste and read does, and "Don't
        // read links" where Save as audio does.
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
                foreach (Control c in new Control[] { progress, timeLabel, autoBox, tidyBox }) { c.Top = Math.Max(0, (underPanel.Height - c.Height) / 2); }
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
            box.MouseUp += delegate { heldCaret = box.SelectionStart; CaretMoved(); };
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
            tips.SetToolTip(autoBox, "Reads whatever you copy, in any program, as soon as you copy it");
            tips.SetToolTip(tidyBox, "Leaves out links, code and the symbols of formatted text");
            // (Placed by AlignLine, against the buttons above.)
            underPanel = new Panel();
            underPanel.Dock = DockStyle.Fill; underPanel.Margin = new Padding(0);
            foreach (Control c in new Control[] { progress, timeLabel, autoBox, tidyBox }) { c.Anchor = AnchorStyles.Left | AnchorStyles.Top; underPanel.Controls.Add(c); }
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
            int chosen = -1;
            for (int i = 0; i < voiceBox.Items.Count; i++) { VoiceChoice kept = (VoiceChoice)voiceBox.Items[i]; if (kept.TokenId == s.Voice || kept.Name == s.Voice) { chosen = i; } }
            if (chosen < 0 && voiceBox.Items.Count > 0) { chosen = 0; }
            voiceBox.SelectedIndex = chosen;
            voiceIndex = chosen;
            ReadDefaultVoice();
            UpdateNickButton();
            // The box starts empty: its text is not kept when the program is closed (nor what an older version kept).
            box.Text = ""; position = 0; s.Position = 0; s.OpenedFile = "";
            try { if (File.Exists(store.CurrentFile)) { File.Delete(store.CurrentFile); } } catch (Exception) { }
            loading = false;
            RateChanged();
            ShowPlace();
            if (voiceBox.Items.Count == 0) { Status("Windows has no voices to read with: add some in Modern Voices Config."); }
            else if (position > 0 && position < box.TextLength) { Status("Play goes on from where you stopped."); }
        }

        void Remember()
        {
            State s = store.State;
            s.Position = 0;                    // (the box's text is not kept; a file's place is)
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
            if (finished || position >= box.TextLength) { position = 0; }
            StartAt(position);
        }

        void StartAt(int from)
        {
            string text = box.Text;
            prepared = Cleaner.Prepare(text, tidyBox.Checked);
            int start = prepared.FromOriginal(Cleaner.SentenceStart(text, from));
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
            Status("Stopped. Play goes on from here; click in the text to read from somewhere else.");
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
        void CaretMoved()
        {
            if (mode != Mode.Stopped || loading) { return; }
            if (box.SelectionLength == 0 && box.SelectionStart != position)
            {
                finished = false;
                position = box.SelectionStart;
                UpdateTime();
            }
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
            try { text = Files.Read(path); }
            catch (Exception e)
            {
                Status("Could not open " + Path.GetFileName(path) + ": " + e.Message + ".");
                return;
            }
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

        // ---- the text as a sound file (WAV), made as fast as the voice can: not played

        void ChooseAudioFile()
        {
            if (saver != null) { CancelSaving(); return; }
            if (box.TextLength == 0) { Status("Nothing to save: paste some text first."); return; }
            if (CurrentVoice == null) { Status("Choose a voice first."); return; }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save the text as audio";
                dialog.Filter = "Sound (*.wav)|*.wav";
                dialog.FileName = Files.SafeName(Store.TitleOf(box.Text)) + ".wav";
                if (dialog.ShowDialog(this) == DialogResult.OK) { SaveAudio(dialog.FileName); }
            }
        }

        public bool Saving { get { return saver != null; } }

        public void SaveAudio(string path)
        {
            if (saver != null) { return; }
            Prepared whole = Cleaner.Prepare(box.Text, tidyBox.Checked);
            int length = Math.Max(1, whole.Text.Length);
            try
            {
                saver = new SpeechSynthesizer();
                saver.SelectVoice(CurrentVoice.Spoken);
                saver.Rate = rateBar.Value;
                saver.Volume = volumeBar.Value;
                saver.SetOutputToWaveFile(path, new SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
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
            audioButton.Text = "Save as audio...";
            if (finished && File.Exists(savingTo))
            {
                long bytes = new FileInfo(savingTo).Length;
                Status("Saved " + Path.GetFileName(savingTo) + ": " + Clock(Math.Max(0, bytes - 44) / 44100.0) + " long, " +
                    (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB.");
            }
            else
            {
                try { if (File.Exists(savingTo)) { File.Delete(savingTo); } } catch (Exception) { }
                Status(problem.Length > 0 ? "Could not save the sound: " + problem : "Saving stopped: no file made.");
            }
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
            if (saver != null) { try { saver.SpeakAsyncCancelAll(); } catch (Exception) { } SavingEnded(false, ""); }
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
            MainForm form = new MainForm(new Store(folder), false);
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

        public const string ScreenWarning = "The screen reader can make mistakes, and is intended for use on images containing text. " +
            "It is more accurate to copy and paste actual text when you can.\r\n\r\nContinue?";

        // Read screen's warning: the question, a box to tick so that it is not shown again, Yes and Cancel.
        public static Form ScreenWarningWindow(out CheckBox notAgain)
        {
            return AskWindow(ScreenWarning, "Do not display this warning again", out notAgain);
        }

        // A question's window (in the theme the Reader wears): the question, a box to tick under it when
        // 'tick' names one, Yes and Cancel.
        static Form AskWindow(string asked, string tick, out CheckBox box)
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
            cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.AutoSize = true;
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
                    form.Close();
                    form.Dispose();
                    MainForm again = new MainForm(new Store(data), true);
                    again.Show();
                    Application.DoEvents();
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
