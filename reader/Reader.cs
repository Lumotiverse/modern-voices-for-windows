// Modern Voices: Reader - reads text aloud with the voices Windows has (the Modern Voices first):
// pasted or copied text, with pause, a place it remembers, and a list of texts to read later.
// Part of Modern Voices for Windows. GPL-3.0-or-later.
//
// Written for the C# compiler that is part of Windows' .NET Framework 4 (C# 5), so that it builds
// with nothing installed.
using System;
using System.Collections.Generic;
using System.Drawing;
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
[assembly: System.Reflection.AssemblyVersion("0.8.1.0")]

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
        // Shared with the Voice manager: auto (follow Windows), light or dark.
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
            string choice = Choice;
            return choice == "dark" || (choice == "auto" && WindowsIsDark());
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
                Find(edits, text, @"^[ \t]*[-*+][ \t]+(?:\[[ xX]\][ \t]+)?", "", 0);                              // - bullets, - [ ] boxes
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
            if (kind == ".html" || kind == ".htm") { return FromHtml(Plain(path)); }
            if (kind == ".doc" || kind == ".pdf" || kind == ".odt" || kind == ".epub")
            {
                throw new InvalidOperationException("files of this kind (" + kind + ") can't be read: copy the text and press Read clipboard");
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

        // A web page's words: scripts and styles left out, a line for each paragraph.
        public static string FromHtml(string html)
        {
            html = Regex.Replace(html, @"<(script|style|head|noscript)\b[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
            html = Regex.Replace(html, @"\s+", " ");
            html = Regex.Replace(html, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</\s*(p|div|li|h[1-6]|tr|blockquote|section|article|title)\s*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = WebUtility.HtmlDecode(html);
            html = Regex.Replace(html, @"[ \t]*\n[ \t]*", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            return html.Trim().Replace("\n", "\r\n");
        }

        public static string SafeName(string title)
        {
            string name = Regex.Replace(Regex.Replace(title, @"[\\/:*?""<>|\r\n\t]+", " "), @"\s+", " ").Trim().TrimEnd('.');
            if (name.Length > 60) { name = name.Substring(0, 60).Trim(); }
            return name.Length == 0 ? "Reading" : name;
        }
    }

    // ---------------------------------------------------------------- what is kept between runs

    public class Saved
    {
        public string Id = "";
        public string Title = "";
        public int Position;
        public int Length;
        public string Added = "";
    }

    public class State
    {
        public string Voice = "";
        public int Rate;
        public bool AutoRead;
        public bool Tidy = true;
        public int Position;                 // in the text in the box
        public string CurrentId = "";        // the list's text in the box, if it is one
        public int[] Bounds = new int[0];
        public List<Saved> List = new List<Saved>();
    }

    public class Store
    {
        public readonly string Folder;
        public State State = new State();

        public Store(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(Path.Combine(Folder, "list"));
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
            if (State.List == null) { State.List = new List<Saved>(); }
            if (State.Bounds == null) { State.Bounds = new int[0]; }
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
        public string ItemFile(string id) { return Path.Combine(Path.Combine(Folder, "list"), id + ".txt"); }

        public string Read(string file)
        {
            try { return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : ""; }
            catch (Exception) { return ""; }
        }

        public void Write(string file, string text)
        {
            try { File.WriteAllText(file, text, Encoding.UTF8); } catch (Exception) { }
        }

        public Saved Find(string id)
        {
            foreach (Saved s in State.List) { if (s.Id == id) { return s; } }
            return null;
        }

        public static string TitleOf(string text)
        {
            string line = Regex.Replace(text.Trim(), @"\s+", " ");
            line = Regex.Replace(line, @"^[#>*\-\s]+", "");
            return line.Length > 60 ? line.Substring(0, 60).TrimEnd() + "..." : line;
        }
    }

    // ---------------------------------------------------------------- a voice in the list

    public class VoiceChoice
    {
        public string Name;      // what Windows calls it (what is asked for)
        public string Shown;     // its nickname, if it has one
        public bool Ours;
        public override string ToString() { return Shown; }
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

        readonly Store store;
        SpeechSynthesizer synth;         // (made once the window exists, so that its events come to the window's thread)
        readonly bool silent;            // the build's test: speech goes nowhere
        public readonly float Dpi;

        ComboBox voiceBox, themeBox;
        TrackBar rateBar;
        Label rateLabel, timeLabel, statusLabel, listLabel;
        TextBox box;
        Button playButton, pauseButton, stopButton, startButton;
        Button clipboardButton, clearButton, saveButton, addClipboardButton, openButton, removeButton, fileButton, audioButton;
        SpeechSynthesizer saver;         // while the text is being saved as sound
        string savingTo = "";
        CheckBox autoBox, tidyBox;
        ProgressBar progress;
        ListBox listBox;
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
            MinimumSize = new Size(Px(480), Px(440));
            Size = new Size(Px(760), Px(680));
            Padding = new Padding(Px(8));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            synth = new SpeechSynthesizer();
            Build();
            FillVoices();
            Restore();
            synth.SpeakProgress += OnProgress;
            synth.SpeakCompleted += OnCompleted;
            tick = new Timer();
            tick.Interval = 500;
            tick.Tick += delegate { UpdateTime(); };
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

        void Build()
        {
            TableLayoutPanel page = new TableLayoutPanel();
            page.ColumnCount = 1; page.Dock = DockStyle.Fill;
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(page);
            int row = 0;

            // Voice, speed, theme.
            FlowLayoutPanel top = NewFlow();
            top.Controls.Add(NewLabel("Voice", false));
            voiceBox = new ComboBox();
            voiceBox.DropDownStyle = ComboBoxStyle.DropDownList; voiceBox.Width = Px(230); voiceBox.Anchor = AnchorStyles.Left;
            voiceBox.SelectedIndexChanged += delegate { if (!loading) { VoiceChanged(); } };
            top.Controls.Add(voiceBox);
            top.Controls.Add(NewLabel("Speed", false));
            rateBar = new TrackBar();
            rateBar.Minimum = -10; rateBar.Maximum = 10; rateBar.TickFrequency = 5; rateBar.LargeChange = 1;
            rateBar.AutoSize = false; rateBar.Width = Px(120); rateBar.Height = Px(30); rateBar.Anchor = AnchorStyles.Left;
            rateBar.TickStyle = TickStyle.None;
            rateBar.ValueChanged += delegate { RateChanged(); };
            top.Controls.Add(rateBar);
            rateLabel = NewLabel("0", false);
            rateLabel.MinimumSize = new Size(Px(26), 0);
            top.Controls.Add(rateLabel);
            top.Controls.Add(NewLabel("Theme", false));
            themeBox = new ComboBox();
            themeBox.DropDownStyle = ComboBoxStyle.DropDownList; themeBox.Width = Px(120); themeBox.Anchor = AnchorStyles.Left;
            themeBox.Items.AddRange(new object[] { "As Windows", "Light", "Dark" });
            themeBox.SelectedIndex = Theme.Choice == "light" ? 1 : Theme.Choice == "dark" ? 2 : 0;
            themeBox.SelectedIndexChanged += delegate
            {
                Theme.Choice = themeBox.SelectedIndex == 1 ? "light" : themeBox.SelectedIndex == 2 ? "dark" : "auto";
                Theme.Apply(this, Theme.IsDark());
            };
            top.Controls.Add(themeBox);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(top, 0, row++);

            // The text.
            box = new TextBox();
            box.Multiline = true; box.AcceptsReturn = true; box.AcceptsTab = false; box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical; box.HideSelection = false; box.MaxLength = 0;
            box.Dock = DockStyle.Fill; box.Font = new Font("Segoe UI", 10.5f);
            box.TextChanged += delegate { if (!loading) { TextEdited(); } };
            box.MouseUp += delegate { CaretMoved(); };
            box.KeyUp += delegate { CaretMoved(); };
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); page.Controls.Add(box, 0, row++);

            // Play, pause, how far, how long is left.
            TableLayoutPanel playLine = NewLine(false, false, false, false, true, false);
            playButton = NewButton("Play", delegate { Play(); });
            pauseButton = NewButton("Pause", delegate { PauseOrResume(); });
            stopButton = NewButton("Stop", delegate { Stop(); });
            startButton = NewButton("From the start", delegate { FromStart(); });
            progress = new ProgressBar();
            progress.Dock = DockStyle.Fill; progress.Maximum = 1000; progress.Height = Px(18);
            progress.Margin = new Padding(Px(8), Px(8), Px(8), Px(8));
            timeLabel = NewLabel("", false);
            timeLabel.MinimumSize = new Size(Px(120), 0);
            playLine.Controls.Add(playButton, 0, 0); playLine.Controls.Add(pauseButton, 1, 0); playLine.Controls.Add(stopButton, 2, 0);
            playLine.Controls.Add(startButton, 3, 0); playLine.Controls.Add(progress, 4, 0); playLine.Controls.Add(timeLabel, 5, 0);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(playLine, 0, row++);

            // The clipboard, tidying.
            FlowLayoutPanel tools = NewFlow();
            clipboardButton = NewButton("Read clipboard", delegate { ReadClipboard(false); });
            autoBox = new CheckBox();
            autoBox.Text = "Read text as soon as it is copied"; autoBox.AutoSize = true; autoBox.Anchor = AnchorStyles.Left;
            autoBox.Margin = new Padding(Px(8), Px(6), Px(8), 3);
            autoBox.CheckedChanged += delegate { if (!loading) { store.State.AutoRead = autoBox.Checked; store.Save(); } };
            tidyBox = new CheckBox();
            tidyBox.Text = "Leave out links, code and symbols"; tidyBox.AutoSize = true; tidyBox.Anchor = AnchorStyles.Left;
            tidyBox.Margin = new Padding(Px(8), Px(6), Px(8), 3);
            tidyBox.CheckedChanged += delegate { if (!loading) { store.State.Tidy = tidyBox.Checked; store.Save(); } };
            clearButton = NewButton("Clear", delegate { ClearText(); });
            fileButton = NewButton("Open a file...", delegate { ChooseFile(); });
            audioButton = NewButton("Save as sound...", delegate { ChooseAudioFile(); });
            tools.Controls.Add(clipboardButton); tools.Controls.Add(fileButton); tools.Controls.Add(audioButton); tools.Controls.Add(clearButton);
            tools.Controls.Add(autoBox); tools.Controls.Add(tidyBox);
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
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(tools, 0, row++);

            // To read later.
            listLabel = NewLabel("To read later (each remembers where you stopped)", false);
            listLabel.Font = new Font(Font, FontStyle.Bold);
            listLabel.Margin = new Padding(3, Px(10), 3, Px(3));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(listLabel, 0, row++);
            listBox = new ListBox();
            listBox.Dock = DockStyle.Fill; listBox.IntegralHeight = false; listBox.Height = Px(110);
            listBox.MinimumSize = new Size(0, Px(110));
            listBox.DoubleClick += delegate { OpenSelected(); };
            listBox.SelectedIndexChanged += delegate { UpdateButtons(); };
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(listBox, 0, row++);
            FlowLayoutPanel listTools = NewFlow();
            saveButton = NewButton("Save this text to the list", delegate { SaveToList(); });
            addClipboardButton = NewButton("Add the clipboard to the list", delegate { AddClipboardToList(); });
            openButton = NewButton("Open", delegate { OpenSelected(); });
            removeButton = NewButton("Remove", delegate { RemoveSelected(); });
            listTools.Controls.Add(saveButton); listTools.Controls.Add(addClipboardButton); listTools.Controls.Add(openButton); listTools.Controls.Add(removeButton);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(listTools, 0, row++);

            statusLabel = NewLabel("", true);
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right; statusLabel.AutoEllipsis = true;
            statusLabel.AutoSize = false; statusLabel.Height = Px(20); statusLabel.Dock = DockStyle.Fill;
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(statusLabel, 0, row++);
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
                choice.Name = v.VoiceInfo.Name;
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
            store.State.Voice = v.Name;
            store.Save();
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
            autoBox.Checked = s.AutoRead;
            tidyBox.Checked = s.Tidy;
            int chosen = -1;
            for (int i = 0; i < voiceBox.Items.Count; i++) { if (((VoiceChoice)voiceBox.Items[i]).Name == s.Voice) { chosen = i; } }
            if (chosen < 0 && voiceBox.Items.Count > 0) { chosen = 0; }
            voiceBox.SelectedIndex = chosen;
            if (s.CurrentId != "" && store.Find(s.CurrentId) == null) { s.CurrentId = ""; }
            box.Text = store.Read(s.CurrentId != "" ? store.ItemFile(s.CurrentId) : store.CurrentFile);
            position = Math.Max(0, Math.Min(box.TextLength, s.CurrentId != "" ? store.Find(s.CurrentId).Position : s.Position));
            loading = false;
            RateChanged();
            FillList();
            ShowPlace();
            if (voiceBox.Items.Count == 0) { Status("Windows has no voices to read with: add some in Modern Voices."); }
            else if (position > 0 && position < box.TextLength) { Status("Play goes on from where you stopped."); }
        }

        void Remember()
        {
            State s = store.State;
            if (s.CurrentId != "")
            {
                Saved item = store.Find(s.CurrentId);
                if (item != null) { item.Position = position; item.Length = box.TextLength; }
            }
            else
            {
                s.Position = position;
                store.Write(store.CurrentFile, box.Text);
            }
            if (WindowState == FormWindowState.Normal) { s.Bounds = new int[] { Left, Top, Width, Height }; }
            store.Save();
        }

        // ---- reading

        void Status(string text) { statusLabel.Text = text; }

        public void Play()
        {
            if (mode == Mode.Paused) { PauseOrResume(); return; }
            if (mode == Mode.Playing) { return; }
            if (box.TextLength == 0) { Status("Nothing to read: paste some text, or press Read clipboard."); return; }
            if (CurrentVoice == null) { Status("Choose a voice first."); return; }
            if (position >= box.TextLength) { position = 0; }
            StartAt(position);
        }

        void StartAt(int from)
        {
            string text = box.Text;
            prepared = Cleaner.Prepare(text, tidyBox.Checked);
            int start = prepared.FromOriginal(Cleaner.SentenceStart(text, from));
            if (start >= prepared.Text.Length || prepared.Text.Substring(start).Trim().Length == 0)
            {
                position = 0;
                ShowPlace();
                Status("That is the end of the text.");
                return;
            }
            spokenFrom = start; spokenAt = start; measuredFrom = start;
            playedSeconds = 0; playingSince = DateTime.UtcNow;
            FirstWord = "";
            try
            {
                if (silent) { synth.SetOutputToNull(); } else { synth.SetOutputToDefaultAudioDevice(); }
                synth.SelectVoice(CurrentVoice.Name);
                synth.Rate = rateBar.Value;
                stopping = false;
                synth.SpeakAsync(prepared.Text.Substring(start));
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
            if (mode == Mode.Stopped || prepared == null) { return; }
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
            if (stopping || e.Cancelled) { return; }
            // The end of the text: next time from its start.
            mode = Mode.Stopped;
            box.ReadOnly = false;
            position = 0;
            if (e.Error != null) { Status("Reading stopped: " + e.Error.Message); }
            else { Status("Finished."); }
            box.Select(0, 0);
            Remember();
            FillList();
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
            progress.Value = Math.Max(0, Math.Min(1000, (int)(1000.0 * Math.Min(length, position) / length)));
            timeLabel.Text = "about " + Clock(remaining / Pace()) + " left";
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
                position = box.SelectionStart;
                UpdateTime();
            }
        }

        void TextEdited()
        {
            // Text typed or pasted in: no longer the list's text as it was saved.
            if (store.State.CurrentId != "")
            {
                Saved item = store.Find(store.State.CurrentId);
                if (item != null) { store.Write(store.ItemFile(item.Id), box.Text); item.Length = box.TextLength; item.Title = Store.TitleOf(box.Text); FillList(); }
            }
            if (position > box.TextLength) { position = box.TextLength; }
            UpdateButtons();
            UpdateTime();
        }

        // Puts a new text in the box (the one there keeps its place, if it is in the list).
        void LoadText(string text, int at, string id)
        {
            if (mode != Mode.Stopped) { Halt(); }
            Remember();
            loading = true;
            store.State.CurrentId = id;
            box.Text = Regex.Replace(text, "\r?\n", "\r\n");   // (the box shows a line break only as CR LF)
            loading = false;
            position = Math.Max(0, Math.Min(box.TextLength, at));
            Remember();
            FillList();
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
            LoadText(text, 0, "");
            Status("Opened " + Path.GetFileName(path) + ". Save it to the list to keep your place in it.");
        }

        // ---- the text as a sound file (WAV), made as fast as the voice can: not played

        void ChooseAudioFile()
        {
            if (saver != null) { CancelSaving(); return; }
            if (box.TextLength == 0) { Status("Nothing to save: paste some text first."); return; }
            if (CurrentVoice == null) { Status("Choose a voice first."); return; }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save the text as sound";
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
                saver.SelectVoice(CurrentVoice.Name);
                saver.Rate = rateBar.Value;
                saver.SetOutputToWaveFile(path, new SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                savingTo = path;
                saver.SpeakProgress += delegate(object sender, SpeakProgressEventArgs e)
                {
                    if (InvokeRequired || saver == null) { return; }
                    Status("Saving as sound: " + Math.Min(99, 100 * e.CharacterPosition / length) + "%  (" + Path.GetFileName(savingTo) + ")");
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
            Status("Saving as sound: 0%  (" + Path.GetFileName(path) + ")");
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
            audioButton.Text = "Save as sound...";
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

        static string ClipboardText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
                catch (Exception) { System.Threading.Thread.Sleep(40); }   // another program has it open: again in a moment
            }
            return "";
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

        void AddClipboardToList()
        {
            string text = ClipboardText();
            if (text.Trim().Length == 0) { Status("The clipboard has no text: copy some first (Ctrl+C)."); return; }
            AddToList(text, 0);
            Status("Added to the list.");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { AddClipboardFormatListener(Handle); } catch (Exception) { }
            Theme.Apply(this, Theme.IsDark());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE && autoBox != null && autoBox.Checked && !ContainsFocus) { ReadClipboard(true); }
            else if (m.Msg == WM_SETTINGCHANGE && Theme.Choice == "auto") { Theme.Apply(this, Theme.IsDark()); }
            base.WndProc(ref m);
        }

        // ---- the list of texts to read later

        Saved AddToList(string text, int at)
        {
            Saved item = new Saved();
            item.Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            item.Title = Store.TitleOf(text);
            item.Position = at; item.Length = text.Length;
            item.Added = DateTime.Now.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            store.Write(store.ItemFile(item.Id), text);
            store.State.List.Insert(0, item);
            store.Save();
            FillList();
            return item;
        }

        void SaveToList()
        {
            if (box.TextLength == 0) { Status("Nothing to save: paste some text first."); return; }
            if (store.State.CurrentId != "") { Status("This text is already in the list."); return; }
            Saved item = AddToList(box.Text, position);
            store.State.CurrentId = item.Id;
            store.Save();
            FillList();
            UpdateButtons();
            Status("Saved to the list: it will remember where you stop.");
        }

        void FillList()
        {
            int selected = listBox.SelectedIndex;
            listBox.BeginUpdate();
            listBox.Items.Clear();
            foreach (Saved s in store.State.List)
            {
                int at = s.Id == store.State.CurrentId ? position : s.Position;
                int percent = s.Length > 0 ? (int)Math.Round(100.0 * Math.Min(at, s.Length) / s.Length) : 0;
                string place = percent <= 0 ? "not started" : percent + "% read";
                listBox.Items.Add((s.Id == store.State.CurrentId ? "> " : "") + s.Title + "   (" + place + ", " + s.Added + ")");
            }
            listBox.EndUpdate();
            if (selected >= 0 && selected < listBox.Items.Count) { listBox.SelectedIndex = selected; }
            listLabel.Text = "To read later (" + store.State.List.Count + "): each remembers where you stopped";
        }

        public int ListCount { get { return store.State.List.Count; } }

        void OpenSelected()
        {
            int i = listBox.SelectedIndex;
            if (i < 0 || i >= store.State.List.Count) { return; }
            Saved item = store.State.List[i];
            LoadText(store.Read(store.ItemFile(item.Id)), item.Position, item.Id);
            Status(item.Position > 0 ? "Play goes on from where you stopped." : "");
        }

        void RemoveSelected()
        {
            int i = listBox.SelectedIndex;
            if (i < 0 || i >= store.State.List.Count) { return; }
            Saved item = store.State.List[i];
            store.State.List.RemoveAt(i);
            try { File.Delete(store.ItemFile(item.Id)); } catch (Exception) { }
            if (store.State.CurrentId == item.Id) { store.State.CurrentId = ""; }   // the text stays in the box
            Remember();
            FillList();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool has = box.TextLength > 0 && voiceBox.Items.Count > 0;
            playButton.Enabled = has && mode != Mode.Playing;
            pauseButton.Enabled = mode != Mode.Stopped;
            pauseButton.Text = mode == Mode.Paused ? "Resume" : "Pause";
            stopButton.Enabled = mode != Mode.Stopped;
            startButton.Enabled = has;
            saveButton.Enabled = box.TextLength > 0 && store.State.CurrentId == "";
            openButton.Enabled = listBox.SelectedIndex >= 0;
            removeButton.Enabled = listBox.SelectedIndex >= 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (mode != Mode.Stopped) { Halt(); }
            if (saver != null) { try { saver.SpeakAsyncCancelAll(); } catch (Exception) { } SavingEnded(false, ""); }
            Remember();
            try { RemoveClipboardFormatListener(Handle); } catch (Exception) { }
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
        public void TestSave() { SaveToList(); }
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
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modern Voices Reader");
            MainForm form = new MainForm(new Store(folder), false);
            // A file given to the program (dropped on it, or "Open with") is opened.
            if (args.Length >= 1 && File.Exists(args[0]))
            {
                string given = args[0];
                form.Shown += delegate { form.OpenFile(given); };
            }
            Application.Run(form);
            return 0;
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
                string fromHtml = Files.FromHtml("<html><head><title>T</title><style>p{color:red}</style></head><body><h1>Heading</h1><p>One &amp; two<br>three.</p><script>x()</script><p>Last&nbsp;one.</p></body></html>");
                log.AppendLine("html: " + fromHtml.Replace("\r\n", " / "));
                Check(fromHtml == "Heading\r\nOne & two\r\nthree.\r\nLast\u00a0one.", "a web page's words are read (not its scripts or styles)");
                Check(Files.SafeName("What: is/this? ") == "What is this", "a file name is made from a title");

                // The window, with nothing kept from before.
                string data = Path.Combine(folder, "data");
                if (Directory.Exists(data)) { Directory.Delete(data, true); }
                MainForm form = new MainForm(new Store(data), true);
                form.Show();
                Application.DoEvents();
                log.AppendLine("voices: " + form.VoicesShown);
                Check(form.VoiceCount > 0, "voices are listed (" + form.VoiceCount + ", " + form.OurVoiceCount + " Modern Voices)");
                string text = "The quick brown fox jumps over the lazy dog. Then it reads a second sentence, with a comma.\r\nAnd a new paragraph to finish with.";
                form.TestLoad(text, 0);
                Theme.Choice = "light"; Theme.Apply(form, false); Application.DoEvents();
                form.Picture(Path.Combine(folder, "reader-light.png"));
                Theme.Choice = "dark"; Theme.Apply(form, true); Application.DoEvents();
                form.Picture(Path.Combine(folder, "reader-dark.png"));
                Check(true, "pictures of the window, light and dark");

                if (form.OurVoiceCount > 0 && form.TestChooseOurs())
                {
                    form.Play();
                    Check(form.Reading, "Play starts reading");
                    bool done = WaitWhile(delegate { return form.Reading; }, 90);
                    Check(done, "reading reaches the end");
                    Check(form.ProgressEvents >= 15, "the place is followed word by word (" + form.ProgressEvents + " words reported, first '" + form.FirstWord + "', last '" + form.LastWord + "')");
                    Check(form.Misplaced == 0, "each word is reported at its own place in the text (" + form.Misplaced + " misplaced)");
                    Check(form.FirstWord == "The" && form.LastWord.StartsWith("with", StringComparison.Ordinal), "the first and last words are the text's");
                    Check(form.Position == 0, "at the end, the place goes back to the start");

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

                    // Kept between runs: the text, its place, and the list.
                    form.TestSave();
                    Check(form.ListCount == 1, "the text is saved to the list");
                    form.Close();
                    form.Dispose();
                    MainForm again = new MainForm(new Store(data), true);
                    again.Show();
                    Application.DoEvents();
                    Check(again.BoxText == text && again.Position == paused && again.ListCount == 1, "opened again: the same text, place (" + again.Position + ") and list");
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
