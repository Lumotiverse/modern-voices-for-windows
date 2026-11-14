// Modern Voices Config - the settings window of Modern Voices for Windows: the installed voices and how
// each sounds (Voice manager), speed and pauses for every voice, pronunciation rules, and the voices to
// add or remove. Part of Modern Voices for Windows. GPL-3.0-or-later.
//
// Written for the C# compiler that is part of Windows' .NET Framework 4 (C# 5), so that it builds with
// nothing installed. Keep the source pure ASCII (symbols as \uXXXX).
//
// It runs without administrator rights. What it changes itself: the settings folder's files (settings.json,
// dictionary.txt, each voice's own settings: see Store) and Windows' default voice. Two things are the
// administrator's: a voice's nickname (its name in Windows' list) and installing or removing voices (the
// installer's work: installer\Install.ps1 with the voices ticked). For those it starts a second copy of
// itself as administrator (--helper: Windows asks for permission, once), which stays until the window
// closes and does only those two things, as asked over a pipe (see Elevated and Helper).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Modern Voices Config")]
[assembly: System.Reflection.AssemblyProduct("Modern Voices for Windows")]
[assembly: System.Reflection.AssemblyVersion("0.12.0.0")]

namespace ModernVoicesConfig
{
    static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool MoveFileEx(string from, string to, int flags);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hwnd, int show);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);
    }

    // ---------------------------------------------------------------- where the program is

    public static class App
    {
        public const string Name = "Modern Voices for Windows";
        public const string BaseUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0";   // v1.0.0: the voices as tested
        public const double DefaultPeak = 1;
        public const double DefaultBoost = 1.5;
        public const string PreviewText = "Speaking a preview of the selected voice, to hear how natural it sounds.\r\n" +
            "Change the dictation speed, and 'Comma pause' values, to adjust the sound to your liking.\r\n" +
            "Change the 'Paragraph gap', to adjust the wait before a new paragraph.";

        public static string Folder, VoiceRoot, SettingsFolder, SettingsFile, DictionaryFile, Installer;

        public static string VersionText
        {
            get
            {
                System.Version v = typeof(App).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        // The installed program's folder (Installed apps' entry says), else the folder this program is in
        // (the kit, before installing).
        public static void Find()
        {
            string folder = null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows"))
                {
                    string location = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(location) && Directory.Exists(Path.Combine(location, "engine"))) { folder = location.TrimEnd('\\'); }
                }
            }
            catch (Exception) { }
            if (folder == null) { folder = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath)); }
            Folder = folder;
            VoiceRoot = Path.Combine(folder, "voices");
            Installer = Path.Combine(folder, "installer");
            // The settings people change are kept apart from the program, in a folder of the PC's own that
            // every user of it may write to (the installer makes it so): changing them needs no administrator.
            SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
            SettingsFile = Path.Combine(SettingsFolder, "settings.json");
            DictionaryFile = Path.Combine(SettingsFolder, "dictionary.txt");
        }

        // A voice's own settings file, for its folder: voices\<the folder's name>.json in the settings folder.
        public static string VoiceFile(string dir)
        {
            return Path.Combine(Path.Combine(SettingsFolder, "voices"), Path.GetFileName(dir.TrimEnd('\\')) + ".json");
        }
    }

    // ---------------------------------------------------------------- the catalogue of voices

    // One voice of the catalogue (one model): its speakers by name, in the model's own order.
    public class Entry
    {
        public string Key, Name, Display, Code, Accent, Quality, Onnx;
        public int SizeMb;
        public List<string> SpeakerNames = new List<string>();
        public Dictionary<string, int> SpeakerIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public bool Several { get { return SpeakerNames.Count > 1; } }
        public string FirstSpeaker { get { return SpeakerNames.Count > 0 ? SpeakerNames[0] : null; } }
    }

    class QuickClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request != null) { request.Timeout = 20000; }
            return request;
        }
    }

    public static class Catalogue
    {
        static readonly Dictionary<string, string> PrettyNames = new Dictionary<string, string> {
            { "hfc_female", "HFC Female" }, { "hfc_male", "HFC Male" }, { "l2arctic", "L2 Arctic" }, { "libritts", "LibriTTS" },
            { "libritts_r", "LibriTTS-R" }, { "ljspeech", "LJ Speech" }, { "vctk", "VCTK" }, { "jenny_dioco", "Jenny" } };

        public static string PrettyName(string name)
        {
            string pretty;
            if (PrettyNames.TryGetValue(name, out pretty)) { return pretty; }
            List<string> parts = new List<string>();
            foreach (string part in name.Split('_'))
            {
                if (part.Length > 0) { parts.Add(part.Substring(0, 1).ToUpper() + part.Substring(1)); }
            }
            return string.Join(" ", parts.ToArray());
        }

        // Downloads to path (through a .part file).
        public static void Download(string url, string path)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;      // TLS 1.2
            string part = path + ".part";
            using (QuickClient client = new QuickClient())
            {
                client.Headers.Add("User-Agent", "ModernVoicesForWindows/" + App.VersionText);
                client.DownloadFile(url, part);
            }
            if (File.Exists(path)) { File.Delete(path); }
            File.Move(part, path);
        }

        static object Value(object dictionary, string key)
        {
            Dictionary<string, object> d = dictionary as Dictionary<string, object>;
            object found;
            if (d != null && d.TryGetValue(key, out found)) { return found; }
            return null;
        }

        // The English voices of medium or high quality, by accent and name. The list is the installer's
        // (catalogue.json in the program's folder, downloaded when it installs or updates); without it,
        // downloaded now. 'problem' says what went wrong, when something did.
        public static List<Entry> Load(out string problem)
        {
            problem = null;
            List<string> places = new List<string>();
            places.Add(Path.Combine(App.Folder, "catalogue.json"));
            places.Add(Path.Combine(Path.GetTempPath(), "modernvoices_catalogue.json"));
            foreach (string place in places)
            {
                try
                {
                    if (File.Exists(place))
                    {
                        List<Entry> read = Parse(File.ReadAllText(place, new UTF8Encoding(false)));
                        if (read.Count > 0) { return read; }
                    }
                }
                catch (Exception) { }
            }
            places.Reverse();      // (the program's folder is the administrator's to write to: tried last)
            foreach (string place in places)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(place));
                    Download(App.BaseUrl + "/voices.json", place);
                    List<Entry> fetched = Parse(File.ReadAllText(place, new UTF8Encoding(false)));
                    problem = null;
                    return fetched;
                }
                catch (Exception e) { problem = "Could not download the voice list from Hugging Face: " + e.Message; }
            }
            return new List<Entry>();
        }

        public static List<Entry> Parse(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 100;
            Dictionary<string, object> all = serializer.DeserializeObject(json) as Dictionary<string, object>;
            List<Entry> list = new List<Entry>();
            if (all == null) { return list; }
            foreach (KeyValuePair<string, object> pair in all)
            {
                object language = Value(pair.Value, "language");
                if ((Value(language, "family") as string) != "en") { continue; }
                string quality = Value(pair.Value, "quality") as string;
                if (quality != "medium" && quality != "high") { continue; }
                Dictionary<string, object> files = Value(pair.Value, "files") as Dictionary<string, object>;
                if (files == null) { continue; }
                string onnx = null, config = null;
                foreach (string path in files.Keys)
                {
                    if (path.EndsWith(".onnx", StringComparison.Ordinal)) { onnx = path; }
                    else if (path.EndsWith(".onnx.json", StringComparison.Ordinal)) { config = path; }
                }
                if (onnx == null || config == null) { continue; }
                Entry entry = new Entry();
                entry.Key = pair.Key;
                entry.Name = Convert.ToString(Value(pair.Value, "name"), CultureInfo.InvariantCulture);
                entry.Code = Convert.ToString(Value(language, "code"), CultureInfo.InvariantCulture);
                entry.Quality = quality;
                entry.Onnx = onnx;
                entry.Display = PrettyName(entry.Name) + (quality == "high" ? " HQ" : "");
                entry.Accent = entry.Code == "en_GB" ? "British" : entry.Code == "en_US" ? "American" : entry.Code;
                entry.SizeMb = (int)Math.Round(Convert.ToDouble(Value(Value(files, onnx), "size_bytes"), CultureInfo.InvariantCulture) / 1048576.0);
                Dictionary<string, object> map = Value(pair.Value, "speaker_id_map") as Dictionary<string, object>;
                if (map != null && map.Count > 0)
                {
                    List<KeyValuePair<string, int>> speakers = new List<KeyValuePair<string, int>>();
                    foreach (KeyValuePair<string, object> speaker in map)
                    {
                        speakers.Add(new KeyValuePair<string, int>(speaker.Key, Convert.ToInt32(speaker.Value, CultureInfo.InvariantCulture)));
                    }
                    speakers.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return a.Value.CompareTo(b.Value); });
                    foreach (KeyValuePair<string, int> speaker in speakers)
                    {
                        if (entry.SpeakerIds.ContainsKey(speaker.Key)) { continue; }
                        entry.SpeakerNames.Add(speaker.Key);
                        entry.SpeakerIds[speaker.Key] = speaker.Value;
                    }
                }
                list.Add(entry);
            }
            list.Sort(delegate(Entry a, Entry b)
            {
                int order = string.Compare(a.Accent, b.Accent, StringComparison.CurrentCultureIgnoreCase);
                if (order == 0) { order = string.Compare(a.Display, b.Display, StringComparison.CurrentCultureIgnoreCase); }
                if (order == 0) { order = string.CompareOrdinal(a.Key, b.Key); }
                return order;
            });
            return list;
        }
    }

    // ---------------------------------------------------------------- the voices as Windows has them

    public static class Voices
    {
        const string Tokens64 = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
        const string Tokens32 = @"SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens";
        const string OurSettings = @"SOFTWARE\Modern Voices for Windows";
        const string UserSpeech = @"Software\Microsoft\Speech\Voices";

        // A voice id is a catalogue key, or key#speaker for one speaker of a multi-speaker voice.
        public static string KeyOf(string id)
        {
            int at = id.IndexOf('#');
            return at < 0 ? id : id.Substring(0, at);
        }
        public static string SpeakerOf(string id)
        {
            int at = id.IndexOf('#');
            return at < 0 ? null : id.Substring(at + 1);
        }
        public static string IdOf(string key, string speaker)
        {
            return string.IsNullOrEmpty(speaker) ? key : key + "#" + speaker;
        }

        public static string TokenName(string id)
        {
            return Regex.Replace("ModernVoices_" + id.Replace("#", "_s"), @"[^A-Za-z0-9_\-]", "_");
        }

        // The ids installed now (from the tokens for 64-bit programs).
        public static List<string> InstalledIds()
        {
            List<string> ids = new List<string>();
            try
            {
                using (RegistryKey tokens = Registry.LocalMachine.OpenSubKey(Tokens64))
                {
                    if (tokens == null) { return ids; }
                    foreach (string name in tokens.GetSubKeyNames())
                    {
                        using (RegistryKey token = tokens.OpenSubKey(name))
                        {
                            if (token == null) { continue; }
                            object ours = token.GetValue("ModernVoice");
                            string id = token.GetValue("VoiceId") as string;
                            if (ours is int && (int)ours == 1 && !string.IsNullOrEmpty(id)) { ids.Add(id); }
                        }
                    }
                }
            }
            catch (Exception) { }
            return ids;
        }

        // The names Windows has for this program's voices (what Preview asks for).
        public static List<string> OurNames()
        {
            List<string> names = new List<string>();
            try
            {
                using (RegistryKey tokens = Registry.LocalMachine.OpenSubKey(Tokens64))
                {
                    if (tokens == null) { return names; }
                    foreach (string name in tokens.GetSubKeyNames())
                    {
                        if (!name.StartsWith("ModernVoices_", StringComparison.OrdinalIgnoreCase)) { continue; }
                        using (RegistryKey token = tokens.OpenSubKey(name))
                        {
                            string shown = token == null ? null : token.GetValue("") as string;
                            if (shown != null) { names.Add(shown); }
                        }
                    }
                }
            }
            catch (Exception) { }
            return names;
        }

        public static bool UsEnglishTags
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(OurSettings))
                    {
                        object value = key == null ? null : key.GetValue("UsEnglishTags");
                        return value is int && (int)value == 1;
                    }
                }
                catch (Exception) { return false; }
            }
        }

        // A nickname of your own for a voice, shown in brackets in its name in place of its accent ("VCTK
        // p262 (Irish Female)"): kept by voice id under the program's settings key.
        public static string Nickname(string id)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(OurSettings + @"\Nicknames"))
                {
                    string value = key == null ? null : key.GetValue(id) as string;
                    return value ?? "";
                }
            }
            catch (Exception) { return ""; }
        }

        // The name a voice would have with a nickname (none: its accent).
        public static string FormatName(Entry entry, string speaker, string nick)
        {
            string what = string.IsNullOrEmpty(nick) ? entry.Accent : nick;
            if (!string.IsNullOrEmpty(speaker)) { return entry.Display + " " + speaker + " (" + what + ")"; }
            return entry.Display + " (" + what + ")";
        }

        // The name programs see for a voice.
        public static string DisplayName(Entry entry, string speaker)
        {
            return FormatName(entry, speaker, Nickname(IdOf(entry.Key, speaker)));
        }

        // Sets (or, empty, removes) a voice's nickname and renames it where Windows lists it, for 64- and
        // 32-bit programs, to the name given. The administrator's to do: the helper's part (see Helper).
        public static void SetNickname(string id, string nick, string display)
        {
            bool found = false;
            foreach (string root in new string[] { Tokens64, Tokens32 })
            {
                using (RegistryKey token = Registry.LocalMachine.OpenSubKey(root + "\\" + TokenName(id), true))
                {
                    if (token == null) { continue; }
                    found = true;
                    token.SetValue("", display);
                    using (RegistryKey attributes = token.CreateSubKey("Attributes")) { attributes.SetValue("Name", display); }
                }
            }
            if (!found) { throw new InvalidOperationException("That voice is not installed."); }
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(OurSettings + @"\Nicknames"))
            {
                if (!string.IsNullOrEmpty(nick)) { key.SetValue(id, nick); }
                else { key.DeleteValue(id, false); }
            }
        }

        // SAPI's default voice (what sapi.cpl sets): a token's name, kept for the Windows user.
        public static string DefaultToken
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UserSpeech))
                    {
                        string id = key == null ? null : key.GetValue("DefaultTokenId") as string;
                        if (string.IsNullOrEmpty(id)) { return ""; }
                        return id.Substring(id.LastIndexOf('\\') + 1);
                    }
                }
                catch (Exception) { return ""; }
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UserSpeech))
                {
                    key.SetValue("DefaultTokenId", @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\" + value);
                }
            }
        }
        public static string DefaultTokenId
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UserSpeech)) { return key == null ? null : key.GetValue("DefaultTokenId") as string; }
                }
                catch (Exception) { return null; }
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UserSpeech))
                {
                    if (value == null) { key.DeleteValue("DefaultTokenId", false); } else { key.SetValue("DefaultTokenId", value); }
                }
            }
        }

        // Windows' own default speech speed (its Speech settings, sapi.cpl: -10 to 10, per user): null when
        // it has never been set. Programs that leave the speed to Windows add it on top of the speed set here.
        public static int? WindowsRateStored
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UserSpeech))
                    {
                        object value = key == null ? null : key.GetValue("DefaultTTSRate");
                        if (value is int) { return (int)value; }
                    }
                }
                catch (Exception) { }
                return null;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UserSpeech))
                {
                    if (value == null) { key.DeleteValue("DefaultTTSRate", false); }
                    else { key.SetValue("DefaultTTSRate", value.Value, RegistryValueKind.DWord); }
                }
            }
        }
        public static int WindowsRate { get { return WindowsRateStored ?? 0; } }
    }

    // ---------------------------------------------------------------- the settings files

    public class VoiceSettings { public double? Peak, Boost, LengthScale; }
    public class Pauses { public bool Own, Stored; public double Sentence, Comma, Paragraph; }
    public class Rule { public bool On, Whole, Case; public string From, To; }

    // The files the engine reads while speaking (a change takes effect from the next thing said), all in
    // the settings folder: settings.json (speed, pauses and output volume for every voice), dictionary.txt
    // (pronunciation) and voices\<voice folder's name>.json, a voice's own settings (loudness, speed, its
    // own pauses). A voice's voice.json, in its folder (the installer's), has them as the voice was
    // installed, and from before 0.12.0 as they were set then: what its own file sets comes first. Each
    // file is written to a new one that then replaces the old, so the engine never reads half a file. The
    // installer's VoiceConvert.ps1 and the engine read them the same way: keep the three in step.
    public static class Store
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        const string Number = @"(-?\d+(?:\.\d+)?)";
        public const int SentenceLimit = 2000, CommaLimit = 1000, ParagraphLimit = 3000;
        public const int SentenceDefault = 500, CommaDefault = 0, ParagraphDefault = 1200;
        public const int SpeedDefault = 100;
        public const double MaxPeak = 2;

        static string Format(double number) { return number.ToString("0.###", CultureInfo.InvariantCulture); }

        static void Replace(string path, string text)
        {
            string temp = path + ".writing";
            File.WriteAllText(temp, text, Utf8);
            Swap(temp, path);
        }
        static void Swap(string temp, string path)
        {
            if (!Native.MoveFileEx(temp, path, 1 | 8))      // replace the existing file, written through
            {
                int error = Marshal.GetLastWin32Error();
                try { File.Delete(temp); } catch (Exception) { }
                throw new IOException("Windows would not write " + path + " (error " + error + ").");
            }
        }

        static string ReadAll(string path) { return File.Exists(path) ? File.ReadAllText(path, Utf8) : ""; }
        // A settings file's text, to change (an empty one when there is none yet, its folder made).
        static string ToChange(string path)
        {
            if (File.Exists(path)) { return File.ReadAllText(path, Utf8).Replace("\r\n", "\n"); }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return "{\n}\n";
        }
        // Text with a block added before its last brace.
        static string WithBlock(string text, string block)
        {
            string body = text.Substring(0, text.LastIndexOf('}')).TrimEnd();
            return body + (body.EndsWith("{", StringComparison.Ordinal) ? "\n  " : ",\n  ") + block + "\n}\n";
        }

        static double? Read(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*" + Number);
            if (!m.Success) { return null; }
            return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // A voice's settings as text: its own file's, then its voice.json's (a search finds the first).
        static string VoiceText(string dir)
        {
            return ReadAll(App.VoiceFile(dir)) + "\n" + ReadAll(Path.Combine(dir, "voice.json"));
        }

        // What is set for a voice, by its folder (null where nothing says); null when it has no voice.json.
        public static VoiceSettings GetVoice(string dir)
        {
            if (!File.Exists(Path.Combine(dir, "voice.json"))) { return null; }
            string text = VoiceText(dir);
            VoiceSettings s = new VoiceSettings();
            s.Peak = Read(text, "peak"); s.Boost = Read(text, "boost"); s.LengthScale = Read(text, "length_scale");
            return s;
        }

        // Records a voice's loudness and speed in its own settings file.
        public static void SetVoice(string dir, double peak, double boost, double lengthScale)
        {
            if (peak < 0 || peak > MaxPeak) { throw new ArgumentException("peak must be from 0 to " + Format(MaxPeak) + " (it is " + Format(peak) + ")."); }
            if (boost < 1 || boost > 5) { throw new ArgumentException("boost must be from 1 to 5 (it is " + Format(boost) + ")."); }
            if (lengthScale < 0.3 || lengthScale > 4) { throw new ArgumentException("length_scale must be from 0.3 to 4 (it is " + Format(lengthScale) + ")."); }
            string path = App.VoiceFile(dir);
            string text = ToChange(path);
            string block = "\"loudness\": {\n    \"peak\": " + Format(peak) + ",\n    \"boost\": " + Format(boost) + "\n  }";
            Match existing = Regex.Match(text, "\"loudness\"\\s*:\\s*\\{[^}]*\\}");
            text = existing.Success ? text.Substring(0, existing.Index) + block + text.Substring(existing.Index + existing.Length) : WithBlock(text, block);
            Match speed = Regex.Match(text, "\"length_scale\"\\s*:\\s*" + Number);
            if (speed.Success)
            {
                Group g = speed.Groups[1];
                text = text.Substring(0, g.Index) + Format(lengthScale) + text.Substring(g.Index + g.Length);
            }
            else { text = WithBlock(text, "\"inference\": {\n    \"length_scale\": " + Format(lengthScale) + "\n  }"); }
            Replace(path, text);
        }

        // The pauses a settings text sets: Own (its own_pauses), Stored (it has any), and the three in
        // milliseconds (the defaults where it says nothing).
        static Pauses PausesOf(string text)
        {
            Pauses p = new Pauses();
            p.Own = (Read(text, "own_pauses") ?? 0) >= 1;
            p.Stored = Regex.IsMatch(text, "\"pauses\"\\s*:");
            p.Sentence = Read(text, "sentence_ms") ?? SentenceDefault;
            p.Comma = Read(text, "comma_ms") ?? CommaDefault;
            p.Paragraph = Read(text, "paragraph_ms") ?? ParagraphDefault;
            return p;
        }
        public static Pauses GetPauses(string path) { return PausesOf(ReadAll(path)); }
        public static Pauses GetVoicePauses(string dir) { return PausesOf(VoiceText(dir)); }

        static void CheckPauses(double sentence, double comma, double paragraph)
        {
            if (sentence < 0 || sentence > SentenceLimit) { throw new ArgumentException("The sentence gap must be from 0 to " + SentenceLimit + " ms (it is " + Format(sentence) + ")."); }
            if (comma < 0 || comma > CommaLimit) { throw new ArgumentException("The comma gap must be from 0 to " + CommaLimit + " ms (it is " + Format(comma) + ")."); }
            if (paragraph < 0 || paragraph > ParagraphLimit) { throw new ArgumentException("The paragraph gap must be from 0 to " + ParagraphLimit + " ms (it is " + Format(paragraph) + ")."); }
        }

        // Puts a "pauses" block in a settings file (made if missing), in place of the one there.
        static void SetPausesBlock(string path, string block)
        {
            string text = ToChange(path);
            Match existing = Regex.Match(text, "\"pauses\"\\s*:\\s*\\{[^}]*\\}");
            text = existing.Success ? text.Substring(0, existing.Index) + block + text.Substring(existing.Index + existing.Length) : WithBlock(text, block);
            Replace(path, text);
        }

        static string PausesBlock(double sentence, double comma, double paragraph, bool? own)
        {
            string ownLine = own.HasValue ? "\n    \"own_pauses\": " + (own.Value ? "1" : "0") + "," : "";
            return "\"pauses\": {" + ownLine + "\n    \"sentence_ms\": " + Format(sentence) + ",\n    \"comma_ms\": " + Format(comma) +
                ",\n    \"paragraph_ms\": " + Format(paragraph) + "\n  }";
        }

        // The pauses for every voice without its own: settings.json.
        public static void SetGlobalPauses(string path, double sentence, double comma, double paragraph)
        {
            CheckPauses(sentence, comma, paragraph);
            SetPausesBlock(path, PausesBlock(sentence, comma, paragraph, null));
        }

        // A voice's own pauses (own true) or not (the global ones apply; its values are kept for next time).
        public static void SetVoicePauses(string dir, bool own, double sentence, double comma, double paragraph)
        {
            CheckPauses(sentence, comma, paragraph);
            SetPausesBlock(App.VoiceFile(dir), PausesBlock(sentence, comma, paragraph, own));
        }

        // A number of settings.json's own, by its key: set, or added first.
        static void SetGlobalNumber(string path, string key, double number)
        {
            string text = ToChange(path);
            string line = "\"" + key + "\": " + Format(number);
            Match existing = Regex.Match(text, "\"" + key + "\"\\s*:\\s*" + Number);
            if (existing.Success)
            {
                text = text.Substring(0, existing.Index) + line + text.Substring(existing.Index + existing.Length);
            }
            else
            {
                int start = text.IndexOf('{') + 1;
                string rest = text.Substring(start).TrimStart();
                text = text.Substring(0, start) + "\n  " + line + (rest.StartsWith("}", StringComparison.Ordinal) ? "\n" : ",\n  ") + rest;
            }
            Replace(path, text);
        }

        // The speed every voice speaks at before a program's rate, in % (settings.json's speed_percent).
        public static double GetGlobalSpeed(string path)
        {
            double? found = Read(ReadAll(path), "speed_percent");
            if (!found.HasValue) { return SpeedDefault; }
            return Math.Max(25, Math.Min(200, found.Value));
        }
        public static void SetGlobalSpeed(string path, double percent)
        {
            if (percent < 25 || percent > 200) { throw new ArgumentException("speed must be 25 to 200% (got " + Format(percent) + ")"); }
            SetGlobalNumber(path, "speed_percent", percent);
        }

        // The output volume: how loud every voice comes out, of what its own settings make, in %
        // (settings.json's output_percent: 100 unless turned down, 0 for silence). The engine looks at it
        // as speech goes out, so it is heard while a voice is speaking.
        public static double GetOutput(string path)
        {
            double? found = Read(ReadAll(path), "output_percent");
            if (!found.HasValue) { return 100; }
            return Math.Max(0, Math.Min(100, found.Value));
        }
        public static void SetOutput(string path, double percent)
        {
            if (percent < 0 || percent > 100) { throw new ArgumentException("the output volume must be 0 to 100% (got " + Format(percent) + ")"); }
            SetGlobalNumber(path, "output_percent", percent);
        }

        // The pronunciation dictionary for every voice: a rule a line, tab-separated: on (1 or 0), whole
        // words only (w) or parts of words too (p), capitals matched (c) or not (i), the word or phrase,
        // what it is said as.
        public static List<Rule> GetDictionary(string path)
        {
            List<Rule> rules = new List<Rule>();
            if (!File.Exists(path)) { return rules; }
            foreach (string line in File.ReadAllLines(path, Utf8))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 5 || (parts[0] != "0" && parts[0] != "1")) { continue; }
                Rule rule = new Rule();
                rule.On = parts[0] == "1"; rule.Whole = parts[1] != "p"; rule.Case = parts[2] == "c"; rule.From = parts[3]; rule.To = parts[4];
                rules.Add(rule);
            }
            return rules;
        }
        public static void SetDictionary(string path, IEnumerable<Rule> rules)
        {
            List<string> lines = new List<string>();
            lines.Add("# Modern Voices for Windows - pronunciation dictionary: on, whole words (w) or parts too (p), capitals matched (c) or not (i), said, as");
            foreach (Rule rule in rules)
            {
                string from = Regex.Replace(rule.From ?? "", @"[\t\r\n]+", " ").Trim();
                string to = Regex.Replace(rule.To ?? "", @"[\t\r\n]+", " ").Trim();
                if (from.Length == 0) { continue; }
                lines.Add((rule.On ? "1" : "0") + "\t" + (rule.Whole ? "w" : "p") + "\t" + (rule.Case ? "c" : "i") + "\t" + from + "\t" + to);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".writing";
            File.WriteAllLines(temp, lines.ToArray(), Utf8);
            Swap(temp, path);
        }
    }

    // ---------------------------------------------------------------- colours (light and dark)

    public class Palette
    {
        public bool Dark;
        public Color Back, Raised, Input, Text, Muted, Warn, Border, Tab, Selected;
    }

    // Light or dark: as last chosen with the moon (kept for the Windows user and shared with the Reader);
    // dark until light is chosen.
    public static class Theme
    {
        const string OurKey = @"Software\Modern Voices for Windows";
        public static string Override;      // (the build's test: "light" or "dark", whatever is chosen)

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

        public static bool IsDark()
        {
            return (Override ?? Choice) != "light";
        }

        public static Palette Get(bool dark)
        {
            Palette p = new Palette();
            p.Dark = dark;
            if (dark)
            {
                p.Back = Color.FromArgb(32, 32, 32); p.Raised = Color.FromArgb(51, 51, 51); p.Input = Color.FromArgb(24, 24, 24);
                p.Text = Color.FromArgb(240, 240, 240); p.Muted = Color.FromArgb(160, 160, 160); p.Warn = Color.FromArgb(255, 128, 128);
                p.Border = Color.FromArgb(90, 90, 90); p.Tab = Color.FromArgb(20, 20, 20); p.Selected = Color.FromArgb(0, 84, 153);
            }
            else
            {
                p.Back = SystemColors.Control; p.Raised = SystemColors.Control; p.Input = SystemColors.Window;
                p.Text = SystemColors.ControlText; p.Muted = Color.DimGray; p.Warn = Color.Firebrick;
                p.Border = SystemColors.ControlDark; p.Tab = SystemColors.ControlLight; p.Selected = SystemColors.Highlight;
            }
            return p;
        }
    }

    // ---------------------------------------------------------------- the window

    public class Slider
    {
        public TrackBar Bar;
        public Label Value, Example;
    }

    // A row of the Voice manager's list: a voice (model) or one of its installed speakers.
    public class InstalledRow
    {
        public string Key, Dir, Title, Speaker;
        public bool Shared;
        public Entry Entry;
    }

    // The voice (speaker) of the row selected in the Voice manager: its token and the name programs see.
    public class CurrentVoice
    {
        public string Token, Voice, Id, Speaker;
    }

    public class ConfigForm : Form
    {
        // What the window knows.
        public List<Entry> CatalogueList;
        public Dictionary<string, Entry> ByKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        public List<string> Installed;                      // the voice ids installed
        public List<string> InstalledKeys = new List<string>();
        public bool UsTags, Filling, Busy;
        public Dictionary<string, string> Speakers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // a voice's ticked speakers: "p225, p239"
        public Entry Current;                               // the voice selected on Add or remove voices
        public InstalledRow CurrentInstalled;               // the row selected on the Voice manager
        bool nameClick;
        float scale = 1f;
        Font boldFont;
        Palette palette;
        bool? shownDark;
        public int PageIndex;
        Process previewProcess;
        string talking;                                     // what a Preview is saying now: the voice's name ("hear:" + it for Hear it)
        public static string SpeakHow = "--speak";          // (the build's test has it speak to nowhere)
        readonly object applyLock = new object();
        List<string> applyHad = new List<string>();         // the voices installed when Apply changes was pressed
        public List<string> ApplyLines = new List<string>();
        public int ProgressSeen;                            // (the furthest the bar has been: the build's test)
        bool applyUsTags;
        readonly HashSet<Control> scrollHooked = new HashSet<Control>();
        readonly HashSet<ListView> listsReady = new HashSet<ListView>();
        List<Rule> wordRules = new List<Rule>();            // the pronunciation rules, in the list's order

        // Its controls.
        public List<Panel> Pages = new List<Panel>();
        public List<Button> TabButtons = new List<Button>();
        Panel pageHost;
        TableLayoutPanel tabStrip, gapGrid, addGrid;
        public Label ThemeButton, StatusLabel, DefaultLabel, SoundLabel, SizeLabel, WarnLabel, SpeakerLabel, RateWarn, SpeedValue;
        public ProgressBar Progress;
        public Button ApplyButton, CloseButton, PreviewButton, ResetButton, DefaultButton, NickButton, GlobalPreview, RateReset, WordSave, WordRemove;
        public ListView InstalledList, WordList, AddList;
        public CheckedListBox SpeakerList;
        public TextBox NickBox, PreviewBox, WordFrom, WordTo;
        public CheckBox OwnBox, UsBox, WordWhole, WordCase;
        public ComboBox PreviewVoiceBox;
        public TrackBar SpeedBar;
        public Dictionary<string, Slider> Sliders = new Dictionary<string, Slider>();
        public Dictionary<string, Slider> GlobalBars = new Dictionary<string, Slider>();
        Timer saveTimer, globalTimer, applyTimer, outputTimer;
        public TrackBar OutputBar;
        public Label OutputValue;
        public Button HearButton;
        ToolTip tips = new ToolTip();
        static readonly string[] GapKeys = { "Sentence gap", "Comma gap", "Paragraph gap" };

        // Where the window opens, and its size: centred on the main screen, 900 x 1000 at 100% scaling (scaled
        // up with the display), never larger than the screen. (The installer puts its console there first.)
        public static float ScreenScale()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) { return Math.Max(1f, g.DpiX / 96f); }
        }
        public static Rectangle WindowBounds(float scale)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min((int)Math.Round(900 * scale), area.Width);
            int h = Math.Min((int)Math.Round(1000 * scale), area.Height);
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        int Px(double n) { return (int)Math.Round(n * scale); }

        // ---- the window's building blocks. Rows are laid out by what is in them (grids that size
        // themselves), so text and sliders can't overlap whatever the display's text size.

        // Text that wraps to the width it is given.
        Label Words(string text, bool grey = false, bool bold = false)
        {
            Label l = new Label();
            l.AutoSize = true; l.Dock = DockStyle.Fill; l.Text = text; l.UseMnemonic = false;
            l.Margin = new Padding(3, Px(3), 3, Px(3));
            if (grey) { l.ForeColor = Color.DimGray; l.Tag = "muted"; }
            if (bold) { l.Font = boldFont; }
            return l;
        }
        Label Beside(string text)      // a label at the left of a row of controls
        {
            Label l = Words(text);
            l.Dock = DockStyle.None; l.Anchor = AnchorStyles.Left;
            return l;
        }

        Button Btn(string text)
        {
            Button b = new Button();
            b.Text = text; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.UseMnemonic = false;
            b.Padding = new Padding(Px(8), Px(2), Px(8), Px(2));
            b.Anchor = AnchorStyles.Left;
            return b;
        }

        // A button whose words change (Preview to Stop and back) keeps the width it has now, so nothing beside it moves.
        static void KeepWidth(Button b)
        {
            b.MinimumSize = new Size(b.PreferredSize.Width, 0);
        }

        // A tab's rows, each the tab's width, top to bottom; the tab scrolls when they don't fit.
        TableLayoutPanel Rows(Panel tab)
        {
            TableLayoutPanel g = new TableLayoutPanel();
            g.ColumnCount = 1;
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            g.AutoSize = true; g.AutoSizeMode = AutoSizeMode.GrowAndShrink; g.Dock = DockStyle.Top;
            g.Padding = new Padding(Px(6));
            tab.Controls.Add(g);
            return g;
        }

        // The next row of a page: as tall as the control needs, or (fill) taking the room left over.
        static void Row(TableLayoutPanel grid, Control control, bool fill = false)
        {
            grid.RowCount++;
            grid.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(control, 0, grid.RowCount - 1);
        }

        // One row of controls side by side: each column fits its control ("auto") or stretches ("fill").
        static TableLayoutPanel Line(params string[] columns)
        {
            TableLayoutPanel g = new TableLayoutPanel();
            g.ColumnCount = columns.Length; g.RowCount = 1;
            foreach (string kind in columns)
            {
                g.ColumnStyles.Add(kind == "fill" ? new ColumnStyle(SizeType.Percent, 100f) : new ColumnStyle(SizeType.AutoSize));
            }
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.AutoSize = true; g.AutoSizeMode = AutoSizeMode.GrowAndShrink; g.Dock = DockStyle.Fill;
            g.Margin = new Padding(0);
            return g;
        }

        // Controls one after another, onto the next line when they don't fit.
        static FlowLayoutPanel Flow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.Dock = DockStyle.Fill; f.WrapContents = true;
            f.Margin = new Padding(0);
            return f;
        }

        // Sliders: name | slider | value, a row each (and notes under a slider, in its column).
        static int GridRow(TableLayoutPanel grid)
        {
            if (grid.Controls.Count > 0) { grid.RowCount++; grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            return grid.RowCount - 1;
        }
        Slider AddSlider(TableLayoutPanel grid, string title, int min, int max, int small, int large, int tick)
        {
            int row = GridRow(grid);
            Label label = Beside(title);
            TrackBar bar = new TrackBar();
            bar.Maximum = max; bar.Minimum = min; bar.SmallChange = small; bar.LargeChange = large; bar.TickFrequency = tick;
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0);
            // The mouse wheel over a slider scrolls the tab, not the slider (it moves by dragging, clicking or the arrow keys).
            bar.MouseWheel += SliderWheel;
            Label value = Beside("");
            value.MinimumSize = new Size(Px(150), 0);
            grid.Controls.Add(label, 0, row); grid.Controls.Add(bar, 1, row); grid.Controls.Add(value, 2, row);
            Slider s = new Slider();
            s.Bar = bar; s.Value = value;
            return s;
        }
        static void AddNote(TableLayoutPanel grid, Control note)
        {
            int row = GridRow(grid);
            grid.Controls.Add(note, 1, row);
        }
        void SliderWheel(object sender, MouseEventArgs e)
        {
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) { handled.Handled = true; }
            // Scrolls the tab the slider is on, as the mouse wheel would.
            Control at = ((Control)sender).Parent;
            while (at != null && (at.Tag as string) != "page") { at = at.Parent; }
            Panel tab = at as Panel;
            if (tab == null || !tab.VerticalScroll.Visible) { return; }
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) { lines = 3; }
            int step = (int)Math.Round(e.Delta / 120.0 * lines * tab.Font.Height);
            int y = Math.Max(0, Math.Min(tab.VerticalScroll.Maximum, -tab.AutoScrollPosition.Y - step));
            tab.AutoScrollPosition = new Point(0, y);
        }

        ListView NewList(bool boxes)
        {
            ListView v = new ListView();
            v.View = View.Details; v.CheckBoxes = boxes; v.FullRowSelect = true; v.HideSelection = false;
            v.MultiSelect = false; v.Dock = DockStyle.Fill;
            // While Windows makes a list's own window (when its tab is first shown), it reports every ticked
            // row as if it had just been ticked, with the rows only half there: ticks count as the person's
            // only once that is over.
            v.HandleCreated += delegate(object made, EventArgs e)
            {
                ListView ready = (ListView)made;
                try { ready.BeginInvoke(new MethodInvoker(delegate { listsReady.Add(ready); })); } catch (Exception) { }
            };
            v.HandleDestroyed += delegate(object gone, EventArgs e) { listsReady.Remove((ListView)gone); };
            return v;
        }

        // ---- building the window

        public ConfigForm(List<Entry> catalogue, List<string> installed, bool usTags)
        {
            CatalogueList = catalogue;
            Installed = new List<string>(installed);
            UsTags = usTags;
            foreach (Entry known in catalogue) { ByKey[known.Key] = known; }

            Text = "Modern Voices Config " + App.VersionText;
            try
            {
                string cog = Path.Combine(App.Installer, "manager.ico");
                Icon = File.Exists(cog) ? new Icon(cog) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception) { }
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            // Sizes here are for a display at 100%; one scaled up (125%, 150%, ...) gets them scaled with its
            // text. The window never opens larger than the screen, and a tab that doesn't fit scrolls.
            scale = ScreenScale();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(Px(600), area.Width), Math.Min(Px(400), area.Height));
            StartPosition = FormStartPosition.Manual;
            Bounds = WindowBounds(scale);
            Padding = new Padding(Px(8));
            boldFont = new Font(Font, FontStyle.Bold);
            tips.AutoPopDelay = 20000;

            // The tabs: a button each and a page each (drawn by the program, so that they can be dark too).
            pageHost = new Panel();
            pageHost.Dock = DockStyle.Fill;
            Controls.Add(pageHost);
            // The strip across the top: the tabs' buttons at the left and, at the far right, the moon.
            tabStrip = Line("auto", "fill", "auto");
            tabStrip.Dock = DockStyle.Top;
            Controls.Add(tabStrip);
            FlowLayoutPanel tabFlow = Flow();
            tabFlow.WrapContents = false;
            tabStrip.Controls.Add(tabFlow, 0, 0);
            string[] titles = { "Voice manager", "Speed and pauses", "Pronunciation", "Add or remove voices" };
            for (int i = 0; i < titles.Length; i++)
            {
                Panel tab = new Panel();
                tab.Dock = DockStyle.Fill; tab.AutoScroll = true; tab.Tag = "page"; tab.Visible = false;
                pageHost.Controls.Add(tab);
                Pages.Add(tab);
                Button tabButton = Btn(titles[i]);
                tabButton.FlatStyle = FlatStyle.Flat; tabButton.FlatAppearance.BorderSize = 0; tabButton.TabStop = false;
                tabButton.Margin = new Padding(0, 0, Px(2), 0);
                int index = i;
                tabButton.Click += delegate { ShowPage(index); };
                tabFlow.Controls.Add(tabButton);
                TabButtons.Add(tabButton);
            }
            // A moon to click (no button around it), in grey at the right of the strip, its right edge over the
            // right edge of the lists below (the pages' 6 px of padding): dark to light and back (the choice is
            // shared with the Reader).
            ThemeButton = Words("Dark", true);
            ThemeButton.Dock = DockStyle.None; ThemeButton.Anchor = AnchorStyles.Right; ThemeButton.Cursor = Cursors.Hand;
            ThemeButton.Margin = new Padding(Px(4), 0, Px(6), 0);
            Font moon = new Font("Segoe MDL2 Assets", 15f);
            if (moon.Name == "Segoe MDL2 Assets") { ThemeButton.Font = moon; ThemeButton.Text = "\uE708"; }      // Windows' own symbols
            tips.SetToolTip(ThemeButton, "Dark or light (here and in the Reader)");
            ThemeButton.Click += delegate { Theme.Choice = Theme.IsDark() ? "light" : "dark"; ApplyTheme(); };
            tabStrip.Controls.Add(ThemeButton, 2, 0);

            // The bottom of the window: progress while applying changes, what is happening, Apply and Close.
            TableLayoutPanel bottom = Line("fill", "auto", "auto");
            bottom.Dock = DockStyle.Bottom;
            bottom.RowStyles.Clear();
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(20)));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowCount = 2;
            Progress = new ProgressBar();
            Progress.Dock = DockStyle.Fill; Progress.Visible = false;
            Progress.Margin = new Padding(3, Px(6), 3, 0);
            bottom.Controls.Add(Progress, 0, 0);
            bottom.SetColumnSpan(Progress, 3);
            StatusLabel = Words("", true);
            StatusLabel.MinimumSize = new Size(0, Px(34));
            ApplyButton = Btn("Apply changes");
            CloseButton = Btn("Close");
            bottom.Controls.Add(StatusLabel, 0, 1); bottom.Controls.Add(ApplyButton, 1, 1); bottom.Controls.Add(CloseButton, 2, 1);
            Controls.Add(bottom);
            CancelButton = CloseButton;
            CloseButton.Click += delegate { Close(); };

            BuildVoiceManager(Pages[0]);
            BuildSpeedAndPauses(Pages[1]);
            BuildPronunciation(Pages[2]);
            BuildAddOrRemove(Pages[3]);

            saveTimer = new Timer();
            saveTimer.Interval = 350;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveSettings(); };
            globalTimer = new Timer();
            globalTimer.Interval = 350;
            globalTimer.Tick += delegate { globalTimer.Stop(); SaveGlobalSettings(); };
            // Apply changes is greyed out while there is nothing to apply (looked at a few times a second:
            // ticks change in several places).
            applyTimer = new Timer();
            applyTimer.Interval = 300;
            applyTimer.Tick += delegate { UpdateApply(); };

            // The output volume is saved almost as it moves: it is heard while a voice is speaking.
            outputTimer = new Timer();
            outputTimer.Interval = 60;
            outputTimer.Tick += delegate { outputTimer.Stop(); SaveOutput(); };

            // Sliders save as they move (a moment after the last movement).
            foreach (Slider s in Sliders.Values)
            {
                s.Bar.Enabled = false;
                s.Bar.ValueChanged += delegate { UpdateSliderLabels(); if (!Filling) { saveTimer.Stop(); saveTimer.Start(); } };
            }
            SpeedBar.ValueChanged += GlobalBarMoved;
            foreach (Slider s in GlobalBars.Values) { s.Bar.ValueChanged += GlobalBarMoved; }

            UpdateWordList();
            ApplyButton.Click += delegate { Apply(); };
            // In front of whatever started it (else it can open behind, unseen).
            TopMost = true;
            Shown += delegate { Activate(); TopMost = false; applyTimer.Start(); };
            FormClosing += WindowClosing;
            FormClosed += delegate { applyTimer.Stop(); };
            SetGlobalBars(Store.GetPauses(App.SettingsFile), Store.GetGlobalSpeed(App.SettingsFile));
            UpdateOutput();
            UpdateWindowsRate();
            UpdateAddLayout();
            UpdateInstalledList();
            UpdateSummary();
            UpdateApply();
            foreach (ListView view in new ListView[] { InstalledList, AddList, WordList })
            {
                DarkHeader(view);
                // The last column takes the width left over (so that no strip of plain heading shows beside it).
                view.Columns[view.Columns.Count - 1].Width = -2;
                view.Resize += ListResized;
            }
            ApplyTheme();
            // Windows' speed can be changed in its Speech settings, and the theme in the Reader, while the
            // window is open: looked at again whenever it comes to the front.
            Activated += delegate { UpdateWindowsRate(); UpdateOutput(); ApplyTheme(); };
            ShowPage(InstalledList.Items.Count == 0 ? 3 : 0);
        }

        void ListResized(object sender, EventArgs e)
        {
            ListView resized = sender as ListView;
            if (resized != null && resized.Columns.Count > 0) { resized.Columns[resized.Columns.Count - 1].Width = -2; }
        }

        void BuildVoiceManager(Panel tab)
        {
            TableLayoutPanel page = Rows(tab);
            InstalledList = NewList(false);
            InstalledList.Height = Px(300);
            InstalledList.MinimumSize = new Size(0, Px(300));
            InstalledList.Columns.Add("Voice", Px(210));
            InstalledList.Columns.Add("Speaker", Px(80));
            InstalledList.Columns[1].TextAlign = HorizontalAlignment.Center;
            InstalledList.Columns.Add("Nickname", Px(190));
            InstalledList.Columns.Add("On disk", Px(70));
            InstalledList.Columns.Add("Windows default", Px(110));
            InstalledList.Columns.Add("Folder", Px(170));
            Row(page, InstalledList);

            // The selected voice (a row for each installed speaker): hear it, make it Windows' default, name it.
            TableLayoutPanel voiceLine = Line("fill", "auto");
            FlowLayoutPanel voiceTools = Flow();
            voiceTools.WrapContents = false;
            PreviewButton = Btn("Preview");
            DefaultButton = Btn("Make default voice");
            NickBox = new TextBox();
            NickBox.Width = Px(190); NickBox.MaxLength = 40; NickBox.Anchor = AnchorStyles.Left; NickBox.Enabled = false;
            NickBox.Margin = new Padding(Px(12), 3, 3, 3);
            NickButton = Btn("Set nickname");
            NickButton.Enabled = false;
            Button sapiButton = Btn("Windows speech settings");
            PreviewButton.Enabled = false; DefaultButton.Enabled = false;
            voiceTools.Controls.Add(PreviewButton); voiceTools.Controls.Add(DefaultButton); voiceTools.Controls.Add(NickBox); voiceTools.Controls.Add(NickButton);
            sapiButton.Anchor = AnchorStyles.Right;
            voiceLine.Controls.Add(voiceTools, 0, 0); voiceLine.Controls.Add(sapiButton, 1, 0);
            Row(page, voiceLine);
            KeepWidth(PreviewButton);
            string nickTip = "A name of your own for the selected voice, shown in brackets in place of its accent:\r\n" +
                "VCTK p262 (British) becomes VCTK p262 (Irish Female).\r\n" +
                "Type it in the box and press Set nickname; with the box empty, the accent is put back.\r\n" +
                "Windows asks for permission the first time (a voice's name is Windows' to keep).\r\n" +
                "Programs that were open show the new name after a restart.";
            tips.SetToolTip(NickButton, nickTip);
            tips.SetToolTip(NickBox, nickTip);
            tips.SetToolTip(PreviewButton, "Says the text below with the selected voice.\r\nPressed while that voice is speaking, it stops it.");

            TableLayoutPanel previewLine = Line("auto", "fill");
            Label previewLabel = Words("Preview says");
            previewLabel.Dock = DockStyle.None; previewLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            PreviewBox = new TextBox();
            // Several lines (Enter starts a new one: a paragraph pause), three showing.
            int threeLines = PreviewBox.Font.Height * 3 + Px(18);
            PreviewBox.Multiline = true; PreviewBox.AcceptsReturn = true; PreviewBox.WordWrap = true; PreviewBox.ScrollBars = ScrollBars.Vertical;
            PreviewBox.Text = App.PreviewText; PreviewBox.Dock = DockStyle.Fill;
            PreviewBox.MinimumSize = new Size(0, threeLines);
            PreviewBox.Height = threeLines;
            previewLine.Controls.Add(previewLabel, 0, 0); previewLine.Controls.Add(PreviewBox, 1, 0);
            Row(page, previewLine);

            // Windows' default voice, in a box of its own; beside it the output volume, for every voice.
            TableLayoutPanel outLine = Line("auto", "auto", "fill", "auto");
            outLine.Margin = new Padding(0, Px(16), 0, Px(12));
            FlowLayoutPanel defaultBox = Flow();
            defaultBox.WrapContents = false; defaultBox.Dock = DockStyle.None; defaultBox.Anchor = AnchorStyles.Left; defaultBox.Tag = "box";
            defaultBox.Padding = new Padding(Px(8), Px(5), Px(8), Px(5));
            defaultBox.Margin = new Padding(3, 0, Px(18), 0);
            defaultBox.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (palette == null) { return; }
                using (Pen edge = new Pen(palette.Border)) { e.Graphics.DrawRectangle(edge, 0, 0, defaultBox.Width - 1, defaultBox.Height - 1); }
            };
            defaultBox.Resize += delegate { defaultBox.Invalidate(); };
            Label defaultCaption = Beside("Windows default voice");
            defaultCaption.ForeColor = Color.DimGray; defaultCaption.Tag = "muted";
            DefaultLabel = Beside("");
            DefaultLabel.Font = boldFont;
            defaultBox.Controls.Add(defaultCaption); defaultBox.Controls.Add(DefaultLabel);
            Label outputIcon = Beside("Output volume");
            Font symbols = new Font("Segoe MDL2 Assets", 13f);
            if (symbols.Name == "Segoe MDL2 Assets") { outputIcon.Font = symbols; outputIcon.Text = "\uE767"; }      // a speaker
            OutputBar = new TrackBar();
            OutputBar.Maximum = 100; OutputBar.Minimum = 0; OutputBar.SmallChange = 5; OutputBar.LargeChange = 10; OutputBar.TickFrequency = 10;
            OutputBar.Value = 100;
            OutputBar.Dock = DockStyle.Fill;
            OutputBar.Margin = new Padding(0);
            OutputBar.MouseWheel += SliderWheel;
            OutputValue = Beside("100%");
            OutputValue.MinimumSize = new Size(Px(90), 0);
            string outputTip = "Output volume, for every voice in every program: how loud they come out, of what each voice's own Volume makes.\r\n" +
                "It works while a voice is speaking. 0 = silent.";
            tips.SetToolTip(outputIcon, outputTip);
            tips.SetToolTip(OutputBar, outputTip);
            outLine.Controls.Add(defaultBox, 0, 0); outLine.Controls.Add(outputIcon, 1, 0); outLine.Controls.Add(OutputBar, 2, 0); outLine.Controls.Add(OutputValue, 3, 0);
            Row(page, outLine);
            OutputBar.ValueChanged += delegate
            {
                OutputValue.Text = OutputBar.Value == 0 ? "0% (silent)" : OutputBar.Value + "%";
                if (!Filling) { outputTimer.Stop(); outputTimer.Start(); }
            };

            TableLayoutPanel soundLine = Line("fill", "auto");
            SoundLabel = Words("Select a voice above to set how it sounds.", false, true);
            SoundLabel.Dock = DockStyle.None; SoundLabel.Anchor = AnchorStyles.Left;
            ResetButton = Btn("Reset");
            ResetButton.Enabled = false;
            soundLine.Controls.Add(SoundLabel, 0, 0); soundLine.Controls.Add(ResetButton, 1, 0);
            Row(page, soundLine);
            TableLayoutPanel soundGrid = Line("auto", "fill", "auto");
            Sliders["Volume"] = AddSlider(soundGrid, "Volume", 0, (int)(Store.MaxPeak * 100), 10, 10, 10);
            Sliders["Boost"] = AddSlider(soundGrid, "Boost", 10, 40, 1, 5, 5);
            Sliders["Speed"] = AddSlider(soundGrid, "Dictation speed", 25, 200, 1, 5, 25);
            Row(page, soundGrid);
            OwnBox = new CheckBox();
            OwnBox.Text = "Modify pause duration for the selected voice";
            OwnBox.AutoSize = true; OwnBox.Enabled = false; OwnBox.Checked = false;
            Row(page, OwnBox);
            gapGrid = Line("auto", "fill", "auto");
            Sliders["Sentence gap"] = AddSlider(gapGrid, "Sentence pause", 0, Store.SentenceLimit, 50, 250, 250);
            Sliders["Comma gap"] = AddSlider(gapGrid, "Comma pause", 0, Store.CommaLimit, 50, 250, 250);
            Sliders["Paragraph gap"] = AddSlider(gapGrid, "Paragraph gap", 0, Store.ParagraphLimit, 50, 250, 250);
            gapGrid.Visible = false;
            Row(page, gapGrid);
            // Volume moves in tens only (0, 10, ... 200): dragged, it snaps to the nearest.
            Sliders["Volume"].Bar.ValueChanged += delegate
            {
                TrackBar volume = Sliders["Volume"].Bar;
                int snapped = (int)(Math.Round(volume.Value / 10.0, MidpointRounding.AwayFromZero) * 10);
                if (volume.Value != snapped) { volume.Value = snapped; }
            };
            Row(page, Words("Volume: each sentence's loudest moment (0 = as Piper made it; 100% = as loud as sound goes; above that, everything " +
                "but the loudest moments is turned up further, to twice as loud at 200%, and sounds more pressed). Boost: lifts quiet parts more than loud ones " +
                "(higher sounds more compressed). Dictation speed: 100% = the speed for every voice (Speed and pauses tab). Unticked, the voice uses the pauses " +
                "for every voice; ticked, its own.", true));
            Row(page, Words("Changes are saved as you move a slider and take effect straight away, also in programs already using the " +
                "voice (from the next thing they say): press Preview to hear them. A voice's speakers share its settings.", true));

            InstalledList.SelectedIndexChanged += delegate { UpdateInstalled(); };
            PreviewButton.Click += delegate { CurrentVoice voice = GetCurrentVoice(); if (voice != null) { TogglePreview(voice.Voice, voice.Voice, null); } };
            NickButton.Click += delegate { SetNickname(); };
            DefaultButton.Click += delegate { SetDefaultVoice(); };
            sapiButton.Click += delegate
            {
                try { Process.Start(Path.Combine(Environment.SystemDirectory, @"Speech\SpeechUX\sapi.cpl")); }
                catch (Exception e) { StatusLabel.Text = "Windows' speech settings did not open: " + e.Message; }
            };
            ResetButton.Click += delegate { ResetSettings(); };
            OwnBox.CheckedChanged += delegate { UpdateOwnPauses(); };
        }

        void BuildSpeedAndPauses(Panel tab)
        {
            TableLayoutPanel page = Rows(tab);
            Row(page, Words("Speed and pauses for every voice (a voice can adjust its speed and have its own pauses: Voice manager)", false, true));
            FlowLayoutPanel hearLine = Flow();
            PreviewVoiceBox = new ComboBox();
            PreviewVoiceBox.DropDownStyle = ComboBoxStyle.DropDownList; PreviewVoiceBox.Width = Px(280); PreviewVoiceBox.Anchor = AnchorStyles.Left;
            GlobalPreview = Btn("Preview");
            GlobalPreview.Enabled = false;
            hearLine.Controls.Add(Beside("Preview with")); hearLine.Controls.Add(PreviewVoiceBox); hearLine.Controls.Add(GlobalPreview);
            Row(page, hearLine);
            KeepWidth(GlobalPreview);
            TableLayoutPanel globalGrid = Line("auto", "fill", "auto");
            Slider speed = AddSlider(globalGrid, "Dictation speed", 25, 200, 5, 25, 25);
            SpeedBar = speed.Bar; SpeedValue = speed.Value;
            SpeedValue.Font = boldFont;
            AddNote(globalGrid, Words("how fast every voice speaks before a program's own rate: 100% by default, the voices' own pace (rate +2 is about 125%) " +
                "(each voice's own Dictation speed, on the Voice manager, adjusts from here)", true));
            RateWarn = Words("");
            RateWarn.ForeColor = Color.Firebrick; RateWarn.Tag = "warn";
            AddNote(globalGrid, RateWarn);
            RateReset = Btn("Set Windows' speed to 0");
            RateReset.Visible = false;
            globalGrid.Controls.Add(RateReset, 2, globalGrid.RowCount - 1);
            string[][] specs = {
                new string[] { "Sentence gap", "Sentence pause", "after each sentence, when another follows (none after the last)" },
                new string[] { "Comma gap", "Comma pause", "at each , ; or : (0 = commas left as the voice says them)" },
                new string[] { "Paragraph gap", "Paragraph gap", "at a line break, in place of the sentence pause (Enter in Preview says starts a new line)" } };
            int[] limits = { Store.SentenceLimit, Store.CommaLimit, Store.ParagraphLimit };
            for (int i = 0; i < specs.Length; i++)
            {
                Slider gap = AddSlider(globalGrid, specs[i][1], 0, limits[i], 50, 250, 250);
                gap.Value.Font = boldFont;
                AddNote(globalGrid, Words(specs[i][2], true));
                gap.Example = Words("");
                AddNote(globalGrid, gap.Example);
                GlobalBars[specs[i][0]] = gap;
            }
            Row(page, globalGrid);
            Row(page, Words("Each pause is silence added to the voice's own, in milliseconds at 100% dictation speed and a program's rate 0. " +
                "Faster speech uses a smaller share of it and slower a larger share, in step with the speech (the line under each shows what you get). " +
                "A comma pause above 0 has the voice say each part of a sentence separately, so the intonation can sound a little more broken up.", true));
            TableLayoutPanel resetLine = Line("auto", "fill");
            Button pausesReset = Btn("Reset to defaults");
            resetLine.Controls.Add(pausesReset, 0, 0);
            resetLine.Controls.Add(Words("Defaults: dictation speed 100%, sentence 500 ms, comma 0, paragraph 1200 ms. Saved as you move a slider; applies straight away.", true), 1, 0);
            Row(page, resetLine);

            GlobalPreview.Click += delegate { string chosen = PreviewVoiceBox.SelectedItem as string; if (!string.IsNullOrEmpty(chosen)) { TogglePreview(chosen, chosen, null); } };
            PreviewVoiceBox.SelectedIndexChanged += delegate { UpdatePreviewButtons(); };
            pausesReset.Click += delegate { ResetGlobalPauses(); };
            RateReset.Click += delegate { Voices.WindowsRateStored = 0; UpdateWindowsRate(); };
        }

        void BuildPronunciation(Panel tab)
        {
            TableLayoutPanel page = Rows(tab);
            Row(page, Words("Words and names said your way: by every voice, in every program that uses these voices", false, true));
            WordList = NewList(true);
            WordList.Height = Px(260);
            WordList.MinimumSize = new Size(0, Px(260));
            WordList.Columns.Add("Word or phrase (ticked: in use)", Px(250));
            WordList.Columns.Add("Said as", Px(270));
            WordList.Columns.Add("Matches", Px(260));
            Row(page, WordList);
            TableLayoutPanel wordLine = Line("auto", "fill", "auto", "fill");
            WordFrom = new TextBox();
            WordFrom.Dock = DockStyle.Fill; WordFrom.MaxLength = 200;
            WordTo = new TextBox();
            WordTo.Dock = DockStyle.Fill; WordTo.MaxLength = 200;
            wordLine.Controls.Add(Beside("Word or phrase"), 0, 0); wordLine.Controls.Add(WordFrom, 1, 0);
            wordLine.Controls.Add(Beside("Say it as"), 2, 0); wordLine.Controls.Add(WordTo, 3, 0);
            Row(page, wordLine);
            FlowLayoutPanel wordTools = Flow();
            WordWhole = new CheckBox();
            WordWhole.Text = "Whole words only"; WordWhole.AutoSize = true; WordWhole.Checked = true; WordWhole.Anchor = AnchorStyles.Left;
            WordCase = new CheckBox();
            WordCase.Text = "Match capitals"; WordCase.AutoSize = true; WordCase.Anchor = AnchorStyles.Left;
            WordSave = Btn("Add rule");
            Button wordNew = Btn("New rule");
            WordRemove = Btn("Remove");
            HearButton = Btn("Hear it");
            WordRemove.Enabled = false;
            foreach (Control c in new Control[] { WordWhole, WordCase, WordSave, wordNew, WordRemove, HearButton }) { wordTools.Controls.Add(c); }
            Row(page, wordTools);
            KeepWidth(HearButton);
            Row(page, Words("To add a rule: type the word or phrase and how to say it (spell it as it sounds: Niamh as Neev), then Add rule. " +
                "To change one: select it, edit, then Save rule. Untick a rule to switch it off without removing it. Hear it says the " +
                "\"Say it as\" text with the voice selected on the Voice manager, to try a spelling before saving (pressed again while it speaks, it stops).", true));
            Row(page, Words("Rules apply from the next thing a program says. Whole words only: \"cat\" changes \"the cat's\" but not " +
                "\"catalogue\". Match capitals: \"US\" but not \"us\". Where two rules fit, the longer one is used, and a rule never changes " +
                "another rule's result. Programs still show and highlight the original words.", true));

            WordList.SelectedIndexChanged += delegate { UpdateWordFields(); };
            WordList.ItemChecked += WordTicked;
            WordSave.Click += delegate { SaveWord(); };
            wordNew.Click += delegate { ClearWordFields(); };
            WordRemove.Click += delegate { RemoveWord(); };
            HearButton.Click += delegate { HearWord(); };
        }

        void BuildAddOrRemove(Panel tab)
        {
            // The list takes the room there is; the tab scrolls below a minimum.
            addGrid = new TableLayoutPanel();
            addGrid.ColumnCount = 1;
            addGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            addGrid.Padding = new Padding(Px(6));
            addGrid.Location = new Point(0, 0);
            tab.Controls.Add(addGrid);
            Row(addGrid, Words("Tick the voices to install; untick one to remove it. Select a voice and press Play sample to hear it. " +
                "Then press Apply changes: the window shows the progress below."));
            AddList = NewList(true);
            AddList.Columns.Add("Voice", Px(220));
            AddList.Columns.Add("Accent", Px(90));
            AddList.Columns.Add("Quality", Px(70));
            AddList.Columns.Add("Speakers", Px(90));
            AddList.Columns.Add("Download", Px(80));
            AddList.Columns.Add("Catalogue key", Px(220));
            Dictionary<string, List<string>> speakersOf = SpeakersInstalled(null);
            foreach (KeyValuePair<string, List<string>> had in speakersOf)
            {
                if (had.Value.Count > 0) { Speakers[had.Key] = string.Join(", ", had.Value.ToArray()); }
            }
            foreach (Entry v in CatalogueList)
            {
                ListViewItem item = new ListViewItem(v.Display);
                item.SubItems.Add(v.Accent);
                item.SubItems.Add(v.Quality);
                item.SubItems.Add(v.Several ? v.SpeakerNames.Count + " speakers" : "1");
                item.SubItems.Add(v.SizeMb + " MB");
                item.SubItems.Add(v.Key);
                item.Tag = v;
                item.Checked = speakersOf.ContainsKey(v.Key);
                if (item.Checked) { item.Font = boldFont; }
                AddList.Items.Add(item);
            }
            Row(addGrid, AddList, true);
            TableLayoutPanel playLine = Line("auto", "fill");
            Button play = Btn("Play sample");
            SpeakerLabel = Words("Select a voice with several speakers to choose which of them to install (each becomes a voice).");
            playLine.Controls.Add(play, 0, 0); playLine.Controls.Add(SpeakerLabel, 1, 0);
            Row(addGrid, playLine);
            SpeakerList = new CheckedListBox();
            SpeakerList.MultiColumn = true; SpeakerList.ColumnWidth = Px(90); SpeakerList.CheckOnClick = true;
            SpeakerList.IntegralHeight = false; SpeakerList.Enabled = false;
            SpeakerList.Dock = DockStyle.Fill; SpeakerList.Height = Px(110);
            SpeakerList.MinimumSize = new Size(0, Px(110));
            Row(addGrid, SpeakerList);
            SizeLabel = Words("");
            Row(addGrid, SizeLabel);
            WarnLabel = Words("");
            WarnLabel.ForeColor = Color.Firebrick; WarnLabel.Tag = "warn";
            Row(addGrid, WarnLabel);
            UsBox = new CheckBox();
            UsBox.Text = "Also list British voices as US English (only if a program does not show them)";
            UsBox.AutoSize = true; UsBox.Checked = UsTags;
            Row(addGrid, UsBox);
            Row(addGrid, Words("Speakers of one voice share its model, so extra speakers take no extra space. Two qualities of the same voice " +
                "(medium and high) are separate models: each takes its full size. Some programs (World of Warcraft, for one) take longer to " +
                "start the more voices Windows has: keep the list to the ones you use.", true));

            AddList.SelectedIndexChanged += delegate { UpdateSelection(); };
            AddList.ItemChecked += delegate { if (!Filling && listsReady.Contains(AddList)) { UpdateSummary(); } };
            // A click on a speaker's name only selects it (to play its sample); its box is what ticks it.
            SpeakerList.MouseDown += SpeakerMouseDown;
            SpeakerList.MouseUp += delegate { nameClick = false; };
            SpeakerList.ItemCheck += SpeakerTicking;
            play.Click += delegate { PlaySample(); };
            // Selecting a speaker plays its sample (Play sample plays it again, or a voice with one speaker).
            SpeakerList.SelectedIndexChanged += delegate { if (!Filling && SpeakerList.SelectedIndex >= 0) { PlaySample(); } };
            tab.Resize += delegate { UpdateAddLayout(); };
            tab.ClientSizeChanged += delegate { UpdateAddLayout(); };
        }

        void SpeakerMouseDown(object sender, MouseEventArgs e)
        {
            int index = SpeakerList.IndexFromPoint(e.Location);
            nameClick = index >= 0 && (e.X - SpeakerList.GetItemRectangle(index).Left) > Px(18);
        }
        void SpeakerTicking(object sender, ItemCheckEventArgs e)
        {
            if (nameClick) { e.NewValue = e.CurrentValue; return; }
            if (!Filling && e.NewValue == CheckState.Checked && AddList.SelectedItems.Count > 0) { AddList.SelectedItems[0].Checked = true; }
        }

        // The installed speakers of each installed voice (none for a voice with one), in the order installed.
        // 'order', when given, gets the voices' keys in that order.
        Dictionary<string, List<string>> SpeakersInstalled(List<string> order)
        {
            Dictionary<string, List<string>> speakersOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in Installed)
            {
                string key = Voices.KeyOf(id);
                string speaker = Voices.SpeakerOf(id);
                if (!speakersOf.ContainsKey(key))
                {
                    speakersOf[key] = new List<string>();
                    if (order != null) { order.Add(key); }
                }
                if (speaker != null) { speakersOf[key].Add(speaker); }
            }
            return speakersOf;
        }

        // The Add or remove tab's rows: the tab's size, but no shorter than they need (the tab then scrolls).
        void UpdateAddLayout()
        {
            if (addGrid == null) { return; }
            Panel tab = Pages[3];
            Size wanted = new Size(tab.ClientSize.Width, Math.Max(tab.ClientSize.Height, Px(520)));
            if (addGrid.Size != wanted) { addGrid.Size = wanted; }
        }

        // ---- light and dark

        // Dark or light scroll bars (and list lines) for a control. Its window is not made for this: one not
        // made yet gets them when Windows makes it.
        void ScrollTheme(Control c)
        {
            if (c.IsHandleCreated)
            {
                try { Native.SetWindowTheme(c.Handle, palette.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch (Exception) { }
            }
            else if (scrollHooked.Add(c))
            {
                c.HandleCreated += delegate(object made, EventArgs e) { ScrollTheme((Control)made); };
            }
        }

        void ThemeOn(Control c, Palette p)
        {
            Button button = c as Button;
            if (button != null)
            {
                if (TabButtons.Contains(button)) { }      // a tab's button: ShowPage colours those
                else if (p.Dark)
                {
                    button.FlatStyle = FlatStyle.Flat; button.BackColor = p.Raised; button.ForeColor = p.Text;
                    button.FlatAppearance.BorderColor = p.Border;
                }
                else { button.FlatStyle = FlatStyle.Standard; button.ForeColor = p.Text; button.UseVisualStyleBackColor = true; }
            }
            else if (c is TextBox || c is ListView || c is ListBox)
            {
                c.BackColor = p.Input; c.ForeColor = p.Text;
                ListView view = c as ListView;
                if (view != null) { view.OwnerDraw = p.Dark; }
                ScrollTheme(c);
            }
            else if (c is ComboBox)
            {
                ((ComboBox)c).FlatStyle = p.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                c.BackColor = p.Input; c.ForeColor = p.Text;
            }
            else if (c is Label)
            {
                string kind = c.Tag as string;
                c.ForeColor = kind == "muted" ? p.Muted : kind == "warn" ? p.Warn : p.Text;
            }
            else if (c is CheckBox) { c.ForeColor = p.Text; }
            else if (c is TrackBar) { c.BackColor = p.Back; }
            else if (c is ProgressBar) { }
            else
            {
                c.BackColor = (c.Tag as string) == "box" ? p.Input : p.Back;
                c.ForeColor = p.Text;
                if ((c.Tag as string) == "page") { ScrollTheme(c); }      // a page's own scroll bar
            }
            foreach (Control child in c.Controls) { ThemeOn(child, p); }
        }

        // Makes the window light or dark as chosen (nothing to do when it already is).
        public void ApplyTheme()
        {
            bool dark = Theme.IsDark();
            if (shownDark.HasValue && shownDark.Value == dark) { return; }
            shownDark = dark;
            palette = Theme.Get(dark);
            SuspendLayout();
            BackColor = palette.Back; ForeColor = palette.Text;
            try
            {
                // The title bar (Windows 10 version 2004 and later: 20; before that: 19).
                int on = dark ? 1 : 0;
                if (Native.DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) { Native.DwmSetWindowAttribute(Handle, 19, ref on, 4); }
            }
            catch (Exception) { }
            foreach (Control child in Controls) { ThemeOn(child, palette); }
            if (ThemeButton.Text.Length > 1) { ThemeButton.Text = dark ? "Light" : "Dark"; }
            ShowPage(PageIndex);
            ResumeLayout();
            Refresh();
        }

        // Shows one of the tabs' pages; its button stands out.
        public void ShowPage(int index)
        {
            PageIndex = index;
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].Visible = i == index;
                Button b = TabButtons[i];
                b.Font = i == index ? boldFont : Font;
                if (palette != null)
                {
                    b.BackColor = i == index ? palette.Input : palette.Tab;
                    b.ForeColor = i == index ? palette.Text : palette.Muted;
                }
            }
        }

        // A list's column headings are drawn by the program when dark (Windows draws them light only), and
        // so are its rows (Windows would draw the selected one unreadably on a dark list).
        void DarkHeader(ListView view)
        {
            view.DrawColumnHeader += DrawHeading;
            view.DrawItem += delegate { };
            view.DrawSubItem += DrawCell;
        }
        void DrawHeading(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            ListView view = (ListView)sender;
            using (SolidBrush fill = new SolidBrush(palette.Raised)) { e.Graphics.FillRectangle(fill, e.Bounds); }
            using (Pen line = new Pen(palette.Border)) { e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 4); }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (e.Header.TextAlign == HorizontalAlignment.Center) { flags |= TextFormatFlags.HorizontalCenter; }
            Rectangle inside = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, view.Font, inside, palette.Text, flags);
        }
        void DrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView view = (ListView)sender;
            ColumnHeader column = view.Columns[e.ColumnIndex];
            Rectangle cell = new Rectangle(e.Bounds.X, e.Bounds.Y, column.Width, e.Bounds.Height);
            using (SolidBrush fill = new SolidBrush(e.Item.Selected ? palette.Selected : palette.Input)) { e.Graphics.FillRectangle(fill, cell); }
            int left = cell.X + 4;
            if (e.ColumnIndex == 0 && view.CheckBoxes)
            {
                CheckBoxState state = e.Item.Checked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal;
                Size glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(left, cell.Y + (cell.Height - glyph.Height) / 2), state);
                left += glyph.Width + 4;
            }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (column.TextAlign == HorizontalAlignment.Center) { flags |= TextFormatFlags.HorizontalCenter; }
            Rectangle inside = new Rectangle(left, cell.Y, Math.Max(0, cell.Right - left - 4), cell.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, inside, palette.Text, flags);
        }

        // ---- sliders

        public static int RoundedMs(double value) { return (int)(Math.Round(value / 10.0) * 10); }
        static string FormatGap(int ms, string kind)
        {
            if (ms <= 0) { return kind == "Comma gap" ? "none (as said)" : "none added"; }
            return "+" + ms + " ms";
        }

        void UpdateSliderLabels()
        {
            if (SpeedBar == null || Sliders.Count < 6 || GlobalBars.Count < 3) { return; }
            int volume = Sliders["Volume"].Bar.Value;
            Sliders["Volume"].Value.Text = volume == 0 ? "off (as Piper made it)" : volume + "%";
            double boost = Sliders["Boost"].Bar.Value / 10.0;
            Sliders["Boost"].Value.Text = boost <= 1.0 ? "none" : boost.ToString("0.0", CultureInfo.InvariantCulture) + "x";
            Sliders["Speed"].Value.Text = Sliders["Speed"].Bar.Value + "%";
            double speed = SpeedBar.Value / 100.0;
            SpeedValue.Text = SpeedBar.Value + "%";
            double step = Math.Pow(3, 0.2);      // a program's rate +2
            foreach (string k in GapKeys)
            {
                Sliders[k].Value.Text = FormatGap(RoundedMs(Sliders[k].Bar.Value), k);
                Slider g = GlobalBars[k];
                int ms = RoundedMs(g.Bar.Value);
                g.Value.Text = FormatGap(ms, k);
                g.Example.Text = ms > 0 ? "at " + SpeedBar.Value + "%: " + Math.Round(ms / speed) + " ms at rate 0, " +
                    Math.Round(ms / speed / step) + " ms at rate +2, " + Math.Round(ms / speed * step) + " ms at rate -2" : "";
            }
        }

        static int Clamp(double value, int low, int high) { return (int)Math.Max(low, Math.Min(high, value)); }

        // The Speed and pauses tab's sliders, from the settings file's values.
        public void SetGlobalBars(Pauses pauses, double speed)
        {
            Filling = true;
            try
            {
                SpeedBar.Value = Clamp(speed, 25, 200);
                GlobalBars["Sentence gap"].Bar.Value = Clamp(pauses.Sentence, 0, Store.SentenceLimit);
                GlobalBars["Comma gap"].Bar.Value = Clamp(pauses.Comma, 0, Store.CommaLimit);
                GlobalBars["Paragraph gap"].Bar.Value = Clamp(pauses.Paragraph, 0, Store.ParagraphLimit);
                UpdateSliderLabels();
            }
            finally { Filling = false; }
        }

        void GlobalBarMoved(object sender, EventArgs e)
        {
            UpdateSliderLabels();
            if (!Filling) { globalTimer.Stop(); globalTimer.Start(); }
        }

        public void SaveGlobalSettings()
        {
            try
            {
                Store.SetGlobalSpeed(App.SettingsFile, SpeedBar.Value);
                Store.SetGlobalPauses(App.SettingsFile, RoundedMs(GlobalBars["Sentence gap"].Bar.Value), RoundedMs(GlobalBars["Comma gap"].Bar.Value),
                    RoundedMs(GlobalBars["Paragraph gap"].Bar.Value));
            }
            catch (Exception e) { MessageBox.Show(this, "Could not save the speed and pauses: " + e.Message, App.Name); }
        }

        void ResetGlobalPauses()
        {
            SpeedBar.Value = Store.SpeedDefault;
            GlobalBars["Sentence gap"].Bar.Value = Store.SentenceDefault;
            GlobalBars["Comma gap"].Bar.Value = Store.CommaDefault;
            GlobalBars["Paragraph gap"].Bar.Value = Store.ParagraphDefault;
        }

        public void UpdateWindowsRate()
        {
            int rate = Voices.WindowsRate;
            if (rate == 0) { RateWarn.Text = ""; RateReset.Visible = false; return; }
            RateWarn.Text = "Windows' speech speed (its Speech settings) is " + (rate > 0 ? "+" : "") + rate + ". Programs that leave the speed to Windows " +
                "add it on top of this one, so these voices are " + (rate > 0 ? "faster" : "slower") + " still.";
            RateReset.Visible = true;
        }

        // The selected voice's pauses: its own (ticked: its three sliders show), else those for every voice.
        public void UpdateOwnPauses()
        {
            bool own = OwnBox.Checked;
            foreach (string k in GapKeys) { Sliders[k].Bar.Enabled = CurrentInstalled != null && own; }
            gapGrid.Visible = own;
            if (!Filling && CurrentInstalled != null) { saveTimer.Stop(); saveTimer.Start(); }
        }
        public bool OwnPausesShown { get { return gapGrid.Visible; } }

        // ---- the Voice manager

        static long FolderMb(string path)
        {
            if (!Directory.Exists(path)) { return 0; }
            long bytes = 0;
            try
            {
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) { bytes += new FileInfo(file).Length; }
            }
            catch (Exception) { }
            return (long)Math.Round(bytes / 1048576.0);
        }

        // The Voice manager's list, from what is installed now (a row for each installed speaker; one row for
        // a voice with one), the default voice marked.
        public void UpdateInstalledList()
        {
            List<string> keys = new List<string>();
            Dictionary<string, List<string>> speakersOf = SpeakersInstalled(keys);
            InstalledKeys = keys;
            string defaultToken = Voices.DefaultToken;
            string defaultName = "";
            List<string> allVoices = new List<string>();
            InstalledList.BeginUpdate();
            InstalledList.Items.Clear();
            foreach (string key in keys)
            {
                Entry entry;
                ByKey.TryGetValue(key, out entry);
                string title = entry != null ? entry.Display + " (" + entry.Accent + ")" : key;
                List<string> names = speakersOf[key];
                string dir = Path.Combine(App.VoiceRoot, key);
                int rows = Math.Max(1, names.Count);
                for (int r = 0; r < rows; r++)
                {
                    string speaker = names.Count > 0 ? names[r] : null;
                    string id = Voices.IdOf(key, speaker);
                    string nick = Voices.Nickname(id);
                    bool isDefault = string.Equals(Voices.TokenName(id), defaultToken, StringComparison.OrdinalIgnoreCase);
                    string shown = entry != null ? Voices.DisplayName(entry, speaker) : key;
                    if (isDefault && defaultName.Length == 0) { defaultName = shown; }
                    ListViewItem item = new ListViewItem(r == 0 ? title : "");
                    item.SubItems.Add(speaker ?? "n/a");
                    item.SubItems.Add(nick.Length > 0 ? "(" + nick + ")" : "");
                    item.SubItems.Add(r == 0 ? FolderMb(dir) + " MB" : "");
                    item.SubItems.Add(isDefault ? "yes" : "");
                    item.SubItems.Add(r == 0 ? key : "");
                    InstalledRow row = new InstalledRow();
                    row.Key = key; row.Dir = dir; row.Title = title; row.Shared = names.Count > 1; row.Speaker = speaker; row.Entry = entry;
                    item.Tag = row;
                    InstalledList.Items.Add(item);
                    allVoices.Add(shown);
                }
            }
            InstalledList.EndUpdate();
            // The Speed and pauses tab's Preview: any installed voice (Windows' default first, if it is one).
            PreviewVoiceBox.Items.Clear();
            foreach (string n in allVoices) { PreviewVoiceBox.Items.Add(n); }
            if (allVoices.Count > 0) { PreviewVoiceBox.SelectedIndex = Math.Max(0, allVoices.IndexOf(defaultName)); }
            GlobalPreview.Enabled = allVoices.Count > 0;
            DefaultLabel.Text = defaultName.Length > 0 ? defaultName : defaultToken.Length > 0 ? defaultToken + " (not one of these)" : "not set";
            if (InstalledList.Items.Count > 0) { InstalledList.Items[0].Selected = true; }
            else { SoundLabel.Text = "No voices installed yet: add some on the Add or remove voices tab."; }
            UpdateInstalled();
        }

        public CurrentVoice GetCurrentVoice()
        {
            InstalledRow row = CurrentInstalled;
            if (row == null) { return null; }
            CurrentVoice voice = new CurrentVoice();
            voice.Speaker = row.Speaker;
            voice.Id = Voices.IdOf(row.Key, row.Speaker);
            voice.Token = Voices.TokenName(voice.Id);
            voice.Voice = row.Entry != null ? Voices.DisplayName(row.Entry, row.Speaker) : row.Key;
            return voice;
        }

        // The sliders and the nickname box: the selected row's voice.
        public void UpdateInstalled()
        {
            if (saveTimer.Enabled) { saveTimer.Stop(); SaveSettings(); }
            CurrentInstalled = null;
            NickBox.Text = ""; NickBox.Enabled = false; NickButton.Enabled = false;
            bool on = InstalledList.SelectedItems.Count > 0;
            Sliders["Volume"].Bar.Enabled = on; Sliders["Boost"].Bar.Enabled = on; Sliders["Speed"].Bar.Enabled = on;
            OwnBox.Enabled = on;
            foreach (string k in GapKeys) { Sliders[k].Bar.Enabled = false; }
            PreviewButton.Enabled = on; ResetButton.Enabled = on; DefaultButton.Enabled = on;
            if (!on) { return; }
            InstalledRow row = (InstalledRow)InstalledList.SelectedItems[0].Tag;
            VoiceSettings settings = Store.GetVoice(row.Dir);
            double peak = App.DefaultPeak, boost = App.DefaultBoost, lengthScale = 1.0;
            if (settings != null)
            {
                if (settings.Peak.HasValue) { peak = settings.Peak.Value; }
                if (settings.Boost.HasValue) { boost = settings.Boost.Value; }
                if (settings.LengthScale.HasValue && settings.LengthScale.Value > 0) { lengthScale = settings.LengthScale.Value; }
            }
            Pauses voicePauses = Store.GetVoicePauses(row.Dir);
            Pauses shown = voicePauses.Own || voicePauses.Stored ? voicePauses : Store.GetPauses(App.SettingsFile);
            Filling = true;
            try
            {
                OwnBox.Checked = voicePauses.Own;
                gapGrid.Visible = voicePauses.Own;
                Sliders["Sentence gap"].Bar.Value = Clamp(shown.Sentence, 0, Store.SentenceLimit);
                Sliders["Comma gap"].Bar.Value = Clamp(shown.Comma, 0, Store.CommaLimit);
                Sliders["Paragraph gap"].Bar.Value = Clamp(shown.Paragraph, 0, Store.ParagraphLimit);
                Sliders["Volume"].Bar.Value = Clamp(Math.Round(peak * 10, MidpointRounding.AwayFromZero) * 10, 0, (int)(Store.MaxPeak * 100));
                Sliders["Boost"].Bar.Value = Clamp(Math.Round(boost * 10), 10, 40);
                Sliders["Speed"].Bar.Value = Clamp(Math.Round(100 / lengthScale), 25, 200);
                UpdateSliderLabels();
            }
            finally { Filling = false; }
            CurrentInstalled = row;
            foreach (string k in GapKeys) { Sliders[k].Bar.Enabled = OwnBox.Checked; }
            // The nickname box: the selected row's nickname.
            bool named = row.Entry != null;
            NickBox.Enabled = named; NickButton.Enabled = named;
            NickBox.Text = named ? Voices.Nickname(Voices.IdOf(row.Key, row.Speaker)) : "";
            SoundLabel.Text = "How " + row.Title + " sounds" + (row.Shared ? " (all its speakers)" : "");
            UpdatePreviewButtons();
        }

        // ---- the output volume (for every voice): settings.json's, saved as the slider moves.

        public void UpdateOutput()
        {
            if (outputTimer.Enabled) { return; }      // (being moved: what it shows is newer than the file)
            Filling = true;
            try { OutputBar.Value = Clamp(Math.Round(Store.GetOutput(App.SettingsFile)), 0, 100); }
            finally { Filling = false; }
        }
        public void SaveOutput()
        {
            try { Store.SetOutput(App.SettingsFile, OutputBar.Value); }
            catch (Exception e) { StatusLabel.Text = "Could not save the output volume: " + e.Message; }
        }

        public void SaveSettings()
        {
            InstalledRow row = CurrentInstalled;
            if (row == null || !File.Exists(Path.Combine(row.Dir, "voice.json"))) { return; }
            double peak = Sliders["Volume"].Bar.Value / 100.0;
            double boost = Sliders["Boost"].Bar.Value / 10.0;
            double lengthScale = Math.Round(100.0 / Sliders["Speed"].Bar.Value, 3);
            try
            {
                Store.SetVoice(row.Dir, peak, boost, lengthScale);
                Store.SetVoicePauses(row.Dir, OwnBox.Checked, RoundedMs(Sliders["Sentence gap"].Bar.Value), RoundedMs(Sliders["Comma gap"].Bar.Value),
                    RoundedMs(Sliders["Paragraph gap"].Bar.Value));
            }
            catch (Exception e) { MessageBox.Show(this, "Could not save the settings: " + e.Message, App.Name); }
        }

        void ResetSettings()
        {
            if (CurrentInstalled == null) { return; }
            Sliders["Volume"].Bar.Value = (int)(App.DefaultPeak * 100);
            Sliders["Boost"].Bar.Value = (int)(App.DefaultBoost * 10);
            Sliders["Speed"].Bar.Value = 100;
        }

        void SelectInstalledRow(int index)
        {
            if (InstalledList.Items.Count > index && index >= 0) { InstalledList.Items[index].Selected = true; UpdateInstalled(); }
        }
        int SelectedInstalledIndex()
        {
            return InstalledList.SelectedIndices.Count > 0 ? InstalledList.SelectedIndices[0] : 0;
        }

        // Makes the selected voice SAPI's default: what programs that don't choose a voice speak with (the
        // setting in sapi.cpl, Windows' Speech control panel).
        public void SetDefaultVoice()
        {
            CurrentVoice voice = GetCurrentVoice();
            if (voice == null) { return; }
            try { Voices.DefaultToken = voice.Token; }
            catch (Exception e) { MessageBox.Show(this, "Could not set the default voice: " + e.Message, App.Name); return; }
            int index = SelectedInstalledIndex();
            UpdateInstalledList();
            SelectInstalledRow(index);
        }

        // The administrator's part, started if it is not there (Windows asks for permission, once; it then
        // stays until the window closes). False, with the status line saying why, when it is not to be had.
        public bool Elevate()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                Elevated.Start();
                return true;
            }
            catch (OperationCanceledException e) { StatusLabel.Text = e.Message; return false; }
            catch (Exception e)
            {
                StatusLabel.Text = "Windows' permission could not be had: " + e.Message;
                return false;
            }
            finally { Cursor = Cursors.Default; }
        }

        // Gives the selected row's voice the nickname in the box (empty: none), renaming the voice in Windows
        // (the administrator's to do: see Elevate).
        public void SetNickname()
        {
            CurrentVoice voice = GetCurrentVoice();
            InstalledRow row = CurrentInstalled;
            if (voice == null || row.Entry == null) { return; }
            string nick = Regex.Replace(Regex.Replace(NickBox.Text, @"[()\r\n]", " "), @"\s+", " ").Trim();
            string wanted = Voices.FormatName(row.Entry, voice.Speaker, nick);
            if (wanted == voice.Voice) { NickBox.Text = nick; StatusLabel.Text = "It is named " + wanted + " already."; return; }
            if (Voices.OurNames().Contains(wanted))
            {
                StatusLabel.Text = "Another voice is already named " + wanted + ".";
                return;
            }
            if (!Elevate()) { return; }
            try
            {
                Answer answer = Elevated.Ask("nick " + B64.To(voice.Id) + " " + B64.To(nick) + " " + B64.To(wanted), null);
                if (answer.Code != 0) { throw new InvalidOperationException(answer.Message ?? "it was refused."); }
            }
            catch (Exception e) { MessageBox.Show(this, "Could not rename the voice: " + e.Message, App.Name); return; }
            int index = SelectedInstalledIndex();
            UpdateInstalledList();
            SelectInstalledRow(index);
            StatusLabel.Text = "Now named " + wanted + ". Restart programs that were open to see the new name.";
        }

        // ---- Preview: the line is spoken by a second copy of this program, as any program would speak it
        // (the engine is never loaded into this one, so nothing here holds its files).

        public bool PreviewTalking
        {
            get
            {
                try { return previewProcess != null && !previewProcess.HasExited; }
                catch (Exception) { return false; }
            }
        }

        public void StopPreview()
        {
            Process p = previewProcess;
            previewProcess = null;
            talking = null;
            if (p != null)
            {
                try { if (!p.HasExited) { p.Kill(); } } catch (Exception) { }
                try { p.Dispose(); } catch (Exception) { }
            }
            UpdatePreviewButtons();
        }

        // A Preview button pressed: it says the thing with that voice. Pressed while that voice is saying
        // it, it stops it; while another is speaking, that one stops and this one starts at once.
        // 'what' names what is said: the voice's name (or "hear:" and it, for Hear it).
        public void TogglePreview(string what, string voiceName, string say)
        {
            bool same = PreviewTalking && talking == what;
            StopPreview();
            if (same) { return; }
            StartPreview(voiceName, say);
            if (previewProcess != null) { talking = what; }
            UpdatePreviewButtons();
        }

        // Each Preview button reads Stop while what it would say is being said.
        void UpdatePreviewButtons()
        {
            if (IsDisposed || PreviewButton == null || GlobalPreview == null || HearButton == null) { return; }
            bool on = PreviewTalking;
            CurrentVoice voice = GetCurrentVoice();
            PreviewButton.Text = on && voice != null && talking == voice.Voice ? "Stop" : "Preview";
            string chosen = PreviewVoiceBox.SelectedItem as string;
            GlobalPreview.Text = on && chosen != null && talking == chosen ? "Stop" : "Preview";
            HearButton.Text = on && talking != null && talking.StartsWith("hear:", StringComparison.Ordinal) ? "Stop" : "Hear it";
        }

        // Speaks with a voice (by the name programs see). Settings not yet saved are saved first.
        public void StartPreview(string voiceName, string say)
        {
            if (saveTimer.Enabled) { saveTimer.Stop(); SaveSettings(); }
            if (globalTimer.Enabled) { globalTimer.Stop(); SaveGlobalSettings(); }
            if (outputTimer.Enabled) { outputTimer.Stop(); SaveOutput(); }
            StopPreview();
            string text = !string.IsNullOrEmpty(say) ? say : PreviewBox.Text.Trim().Length > 0 ? PreviewBox.Text : App.PreviewText;
            if (text.Length > 6000) { text = text.Substring(0, 6000); }
            try
            {
                Process started = StartSpeaker(SpeakHow, voiceName, text);
                previewProcess = started;
                // When it has finished speaking, its button reads Preview again.
                started.EnableRaisingEvents = true;
                started.Exited += delegate { try { BeginInvoke(new Action<Process>(PreviewEnded), started); } catch (Exception) { } };
            }
            catch (Exception e) { StatusLabel.Text = "Preview did not start: " + e.Message; }
        }
        void PreviewEnded(Process ended)
        {
            if (previewProcess != ended) { return; }
            previewProcess = null;
            talking = null;
            try { ended.Dispose(); } catch (Exception) { }
            UpdatePreviewButtons();
        }
        // This program started again to speak (--speak), or to speak to nowhere and say whether sound came
        // out (--speak-check: the build's test, on a PC with no sound device).
        public static Process StartSpeaker(string how, string voiceName, string text)
        {
            ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, how + " " + B64.To(voiceName) + " " + B64.To(text));
            start.UseShellExecute = false; start.CreateNoWindow = true;
            return Process.Start(start);
        }

        // ---- the Pronunciation tab. Each row of its list is a rule (its Tag); changes are saved at once.

        static string WordMatch(Rule rule)
        {
            return (rule.Whole ? "whole words" : "parts of words too") + (rule.Case ? ", with these capitals" : "");
        }
        ListViewItem AddWordRow(Rule rule)
        {
            ListViewItem item = new ListViewItem(rule.From);
            item.SubItems.Add(rule.To);
            item.SubItems.Add(WordMatch(rule));
            item.Tag = rule;
            item.Checked = rule.On;
            WordList.Items.Add(item);
            return item;
        }
        public void UpdateWordList()
        {
            Filling = true;
            try
            {
                wordRules = Store.GetDictionary(App.DictionaryFile);
                WordList.BeginUpdate();
                WordList.Items.Clear();
                foreach (Rule rule in wordRules) { AddWordRow(rule); }
                WordList.EndUpdate();
            }
            finally { Filling = false; }
            UpdateWordFields();
        }
        // A rule ticked or unticked by the person: switched on or off, and saved.
        void WordTicked(object sender, ItemCheckedEventArgs e)
        {
            if (Filling || !listsReady.Contains(WordList)) { return; }
            Rule rule = e.Item.Tag as Rule;
            if (rule == null || rule.On == e.Item.Checked) { return; }
            rule.On = e.Item.Checked;
            SaveWords();
        }
        // Writes the rules to the dictionary file (the engine picks it up at the next thing said).
        public void SaveWords()
        {
            try { Store.SetDictionary(App.DictionaryFile, wordRules); }
            catch (Exception e) { StatusLabel.Text = "Could not save the pronunciation rules: " + e.Message; }
        }
        // The boxes under the list: the selected rule's, to change it; empty for a new one.
        void UpdateWordFields()
        {
            bool selected = WordList.SelectedItems.Count > 0;
            if (selected)
            {
                Rule rule = (Rule)WordList.SelectedItems[0].Tag;
                WordFrom.Text = rule.From; WordTo.Text = rule.To;
                WordWhole.Checked = rule.Whole; WordCase.Checked = rule.Case;
            }
            WordSave.Text = selected ? "Save rule" : "Add rule";
            WordRemove.Enabled = selected;
        }
        void ClearWordFields()
        {
            List<ListViewItem> chosen = new List<ListViewItem>();
            foreach (ListViewItem item in WordList.SelectedItems) { chosen.Add(item); }
            foreach (ListViewItem item in chosen) { item.Selected = false; }
            WordFrom.Text = ""; WordTo.Text = ""; WordWhole.Checked = true; WordCase.Checked = false;
            UpdateWordFields();
            WordFrom.Focus();
        }
        // Adds the rule in the boxes, or changes the selected one (or the one already there for that word).
        public void SaveWord()
        {
            string from = Regex.Replace(WordFrom.Text, @"\s+", " ").Trim();
            string to = Regex.Replace(WordTo.Text, @"\s+", " ").Trim();
            if (from.Length == 0) { StatusLabel.Text = "Type the word or phrase first."; return; }
            Rule rule = new Rule();
            rule.On = true; rule.Whole = WordWhole.Checked; rule.Case = WordCase.Checked; rule.From = from; rule.To = to;
            ListViewItem target = WordList.SelectedItems.Count > 0 ? WordList.SelectedItems[0] : null;
            if (target == null)
            {
                foreach (ListViewItem item in WordList.Items)
                {
                    Rule other = (Rule)item.Tag;
                    bool same = string.Equals(other.From, from, rule.Case || other.Case ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                    if (same && other.Case == rule.Case) { target = item; break; }
                }
            }
            Filling = true;
            try
            {
                if (target != null)
                {
                    Rule old = (Rule)target.Tag;
                    rule.On = old.On;
                    int at = wordRules.IndexOf(old);
                    if (at >= 0) { wordRules[at] = rule; } else { wordRules.Add(rule); }
                    target.Tag = rule;
                    target.Text = from; target.SubItems[1].Text = to; target.SubItems[2].Text = WordMatch(rule);
                }
                else
                {
                    wordRules.Add(rule);
                    target = AddWordRow(rule);
                }
            }
            finally { Filling = false; }
            SaveWords();
            target.Selected = true;
            target.EnsureVisible();
            UpdateWordFields();
            StatusLabel.Text = "Saved: \"" + from + "\" is said as \"" + to + "\" from the next thing a program says.";
        }
        public void RemoveWord()
        {
            if (WordList.SelectedItems.Count == 0) { return; }
            ListViewItem gone = WordList.SelectedItems[0];
            wordRules.Remove((Rule)gone.Tag);
            WordList.Items.Remove(gone);
            SaveWords();
            ClearWordFields();
        }
        // Says the "Say it as" text (the word itself if that is empty) with the Voice manager's selected voice,
        // else the one chosen on Speed and pauses: to try a spelling before saving it.
        void HearWord()
        {
            string say = WordTo.Text.Trim().Length > 0 ? WordTo.Text.Trim() : WordFrom.Text.Trim();
            if (say.Length == 0) { StatusLabel.Text = "Type something to hear first."; return; }
            CurrentVoice voice = GetCurrentVoice();
            string name = voice != null ? voice.Voice : PreviewVoiceBox.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { StatusLabel.Text = "Install a voice first (Add or remove voices)."; return; }
            TogglePreview("hear:" + name, name, say);
        }

        // ---- Add or remove voices

        List<Entry> Ticked()
        {
            List<Entry> ticked = new List<Entry>();
            foreach (ListViewItem item in AddList.Items) { if (item.Checked) { ticked.Add((Entry)item.Tag); } }
            return ticked;
        }

        public void UpdateSummary()
        {
            List<Entry> ticked = Ticked();
            int total = 0, download = 0, added = 0, gone = 0;
            HashSet<string> tickedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry v in ticked)
            {
                total += v.SizeMb;
                tickedKeys.Add(v.Key);
                if (!InstalledKeys.Contains(v.Key)) { added++; download += v.SizeMb; }
            }
            foreach (string key in InstalledKeys) { if (!tickedKeys.Contains(key)) { gone++; } }
            string text = "Ticked: " + ticked.Count + " voice(s), about " + total + " MB on disk.";
            if (added > 0) { text += " To download: " + added + ", " + download + " MB."; }
            if (gone > 0) { text += " To remove: " + gone + "."; }
            if (added == 0 && gone == 0) { text += " No changes to apply."; }
            SizeLabel.Text = text;
            // The same voice ticked in two qualities: each is a model of its own.
            SortedDictionary<string, List<Entry>> groups = new SortedDictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry v in ticked)
            {
                string sameVoice = v.Name + "|" + v.Code;
                if (!groups.ContainsKey(sameVoice)) { groups[sameVoice] = new List<Entry>(); }
                groups[sameVoice].Add(v);
            }
            List<string> warnings = new List<string>();
            foreach (List<Entry> g in groups.Values)
            {
                if (g.Count < 2) { continue; }
                List<string> qualities = new List<string>(), sizes = new List<string>();
                foreach (Entry v in g) { qualities.Add(v.Quality); sizes.Add(v.SizeMb + " MB"); }
                warnings.Add(Catalogue.PrettyName(g[0].Name) + " (" + g[0].Accent + ") is ticked in " + g.Count + " qualities (" +
                    string.Join(" and ", qualities.ToArray()) + "): each is a separate model, " + string.Join(" + ", sizes.ToArray()) + ".");
            }
            WarnLabel.Text = string.Join(" ", warnings.ToArray());
        }

        static List<string> Names(string joined)
        {
            List<string> names = new List<string>();
            foreach (string part in (joined ?? "").Split(','))
            {
                if (part.Trim().Length > 0) { names.Add(part.Trim()); }
            }
            return names;
        }

        // Keeps the speakers ticked for the voice selected (as the selection moves on, and before the ticks are read).
        public void SaveSpeakers()
        {
            if (Current == null || !Current.Several) { return; }
            List<string> ticked = new List<string>();
            foreach (object item in SpeakerList.CheckedItems) { ticked.Add(Convert.ToString(item)); }
            Speakers[Current.Key] = string.Join(", ", ticked.ToArray());
        }

        // The speakers of the voice selected in the list, to tick.
        public void UpdateSelection()
        {
            SaveSpeakers();
            Current = null;
            Filling = true;
            SpeakerList.BeginUpdate();
            try
            {
                SpeakerList.Items.Clear();
                SpeakerList.Enabled = false;
                if (AddList.SelectedItems.Count == 0) { return; }
                Entry v = (Entry)AddList.SelectedItems[0].Tag;
                Current = v;
                if (!v.Several)
                {
                    SpeakerLabel.Text = v.Display + ": one speaker.";
                    return;
                }
                // Listed in name order, numbers by value (p225 before p1000; the voice's own order is kept in its files).
                List<string> names = new List<string>(v.SpeakerNames);
                names.Sort(delegate(string a, string b)
                {
                    int order = string.Compare(Padded(a), Padded(b), StringComparison.CurrentCultureIgnoreCase);
                    return order != 0 ? order : string.CompareOrdinal(a, b);
                });
                List<string> ticked = Speakers.ContainsKey(v.Key) ? Names(Speakers[v.Key]) : new List<string>();
                foreach (string n in names) { SpeakerList.Items.Add(n, ticked.Contains(n)); }
                SpeakerList.Enabled = true;
                SpeakerLabel.Text = "Speakers of " + v.Display + " (" + names.Count + "): click a name to hear it (Play sample plays it again); tick its box to " +
                    "install it, each becomes a voice (none ticked: " + v.FirstSpeaker + ").";
            }
            finally
            {
                SpeakerList.EndUpdate();
                Filling = false;
            }
        }
        static string Padded(string name)
        {
            return Regex.Replace(name, @"\d+", delegate(Match m) { return m.Value.PadLeft(10, '0'); });
        }

        // Plays the catalogue's sample of the voice selected (of its speaker selected, else the first ticked,
        // else the first).
        void PlaySample()
        {
            if (AddList.SelectedItems.Count == 0) { return; }
            SaveSpeakers();
            Entry v = (Entry)AddList.SelectedItems[0].Tag;
            int number = 0;
            if (v.Several)
            {
                string name = SpeakerList.SelectedItem as string;
                if (string.IsNullOrEmpty(name) && SpeakerList.CheckedItems.Count > 0) { name = SpeakerList.CheckedItems[0] as string; }
                if (!string.IsNullOrEmpty(name) && v.SpeakerIds.ContainsKey(name)) { number = v.SpeakerIds[name]; }
            }
            string folder = v.Onnx.Substring(0, v.Onnx.LastIndexOf('/'));
            string file = Path.Combine(Path.GetTempPath(), "modernvoices_sample_" + v.Key + "_" + number + ".mp3");
            try
            {
                Cursor = Cursors.WaitCursor;
                if (!File.Exists(file)) { Catalogue.Download(App.BaseUrl + "/" + folder + "/samples/speaker_" + number + ".mp3", file); }
                Sample.Play(file);
            }
            catch (Exception e) { MessageBox.Show(this, "Could not play the sample: " + e.Message, App.Name); }
            finally { Cursor = Cursors.Default; }
        }

        // The ids the list's ticks stand for: a key per single-speaker voice, key#name per chosen speaker.
        public List<string> PickedIds()
        {
            List<string> ids = new List<string>();
            foreach (Entry v in Ticked())
            {
                if (!v.Several) { if (!ids.Contains(v.Key)) { ids.Add(v.Key); } continue; }
                List<string> good = new List<string>();
                if (Speakers.ContainsKey(v.Key))
                {
                    foreach (string n in Names(Speakers[v.Key])) { if (v.SpeakerIds.ContainsKey(n)) { good.Add(n); } }
                }
                if (good.Count == 0) { good.Add(v.FirstSpeaker); }
                foreach (string n in good)
                {
                    string id = v.Key + "#" + n;
                    if (!ids.Contains(id)) { ids.Add(id); }
                }
            }
            return ids;
        }

        bool NothingToApply(List<string> wanted, bool us)
        {
            List<string> a = new List<string>(wanted), b = new List<string>(Installed);
            a.Sort(StringComparer.OrdinalIgnoreCase); b.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", a.ToArray()).Equals(string.Join("|", b.ToArray()), StringComparison.OrdinalIgnoreCase) && us == UsTags;
        }

        // Apply changes is greyed out while there is nothing to apply: the voices (and speakers) ticked are the
        // ones installed, and the US English option is as it was.
        public void UpdateApply()
        {
            if (Busy || Filling) { return; }
            SaveSpeakers();
            bool same = NothingToApply(PickedIds(), UsBox.Checked);
            if (ApplyButton.Enabled == same) { ApplyButton.Enabled = !same; }
        }

        // Applies the Add or remove tab's ticks: the installer (installer\Install.ps1) downloads, removes,
        // registers and tests, with no window of its own; what it says shows at the bottom of this one. It
        // is the administrator's work: run by the helper (see Elevate). Then the window shows the new state.
        public void Apply()
        {
            if (Busy) { return; }
            SaveSpeakers();
            List<string> wanted = PickedIds();
            bool us = UsBox.Checked;
            if (NothingToApply(wanted, us)) { StatusLabel.Text = "Nothing to change."; return; }
            if (!Elevate()) { return; }
            string request = "apply " + (us ? "1" : "0") + " " + B64.To(string.Join(",", wanted.ToArray()));
            lock (applyLock) { ApplyLines.Clear(); }
            applyUsTags = us;
            applyHad = new List<string>(Installed);
            ProgressSeen = 0;
            Busy = true;
            pageHost.Enabled = false; tabStrip.Enabled = false; ApplyButton.Enabled = false; CloseButton.Enabled = false;
            Progress.Value = 0; Progress.Visible = true;
            Cursor = Cursors.WaitCursor;
            StatusLabel.Text = "Starting...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int code = 1;
                string stopped = null;
                try
                {
                    Answer answer = Elevated.Ask(request, ApplySaid);
                    code = answer.Code;
                    stopped = answer.Message;
                }
                catch (Exception e) { stopped = "Stopped: " + e.Message; }
                try { BeginInvoke(new Action<int, string>(ApplyFinished), code, stopped); } catch (Exception) { }
            });
        }

        // A line from the installer (on another thread): kept, and shown.
        void ApplySaid(string line)
        {
            lock (applyLock) { ApplyLines.Add(line); }
            try { BeginInvoke(new Action<string>(ApplyShow), line); } catch (Exception) { }
        }
        void ApplyShow(string line)
        {
            if (!Busy) { return; }
            if (line.StartsWith("##progress ", StringComparison.Ordinal))
            {
                int percent;
                if (int.TryParse(line.Substring(11), out percent))
                {
                    Progress.Value = Math.Max(0, Math.Min(100, percent));
                    ProgressSeen = Math.Max(ProgressSeen, Progress.Value);
                }
                return;
            }
            StatusLabel.Text = line;
        }

        // Windows' default voice after voices were installed or removed: when it is not one of these (it is
        // one of Windows' own, such as Hazel, or one of these that has just been removed) and voices were
        // added (or the default was the one removed), the first voice of the list becomes the default. A
        // Modern Voice that is the default stays so; with none installed nothing is done.
        void SetDefaultAfterApply()
        {
            if (Installed.Count == 0) { return; }
            string token = Voices.DefaultToken;
            bool ours = false, added = false;
            foreach (string id in Installed)
            {
                if (string.Equals(Voices.TokenName(id), token, StringComparison.OrdinalIgnoreCase)) { ours = true; }
                if (!applyHad.Contains(id)) { added = true; }
            }
            if (ours) { return; }
            bool wasOurs = token.StartsWith("ModernVoices_", StringComparison.OrdinalIgnoreCase);
            if (!added && !wasOurs) { return; }
            try { Voices.DefaultToken = Voices.TokenName(Installed[0]); } catch (Exception) { }
        }

        // 0: done and every voice passed its test; 2: done, but a voice failed its test; else it stopped
        // ('stopped' says why).
        void ApplyFinished(int code, string stopped)
        {
            Busy = false;
            Progress.Visible = false;
            pageHost.Enabled = true; tabStrip.Enabled = true; ApplyButton.Enabled = true; CloseButton.Enabled = true;
            Cursor = Cursors.Default;
            Installed = Voices.InstalledIds();
            if (code == 0 || code == 2)
            {
                UsTags = applyUsTags;
                SetDefaultAfterApply();
            }
            // The new state: ticks and bold for what is installed, the manager's list.
            Filling = true;
            try
            {
                foreach (ListViewItem item in AddList.Items)
                {
                    string key = ((Entry)item.Tag).Key;
                    bool isIn = false;
                    foreach (string id in Installed) { if (string.Equals(Voices.KeyOf(id), key, StringComparison.OrdinalIgnoreCase)) { isIn = true; break; } }
                    item.Checked = isIn;
                    item.Font = isIn ? boldFont : Font;
                }
            }
            finally { Filling = false; }
            UsBox.Checked = UsTags;
            UpdateInstalledList();
            UpdateSummary();
            UpdateApply();
            if (code == 0)
            {
                StatusLabel.Text = "Done: every voice passed its test. Restart programs that were open to see new voices.";
                if (InstalledList.Items.Count > 0) { ShowPage(0); }
            }
            else if (code == 2)
            {
                StatusLabel.Text = "A voice failed its test. The engine's log says why: " +
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ModernVoices\engine.log");
            }
            else
            {
                string why = string.IsNullOrEmpty(stopped) ? "Stopped: the installer ended without finishing (code " + code + ")." : stopped;
                StatusLabel.Text = why;
                if (!SelfTest.Running) { MessageBox.Show(this, why, App.Name); }
            }
        }

        void WindowClosing(object sender, FormClosingEventArgs e)
        {
            if (Busy) { e.Cancel = true; return; }      // not while changes are being applied
            SaveSpeakers();
            if (saveTimer.Enabled) { saveTimer.Stop(); SaveSettings(); }
            if (globalTimer.Enabled) { globalTimer.Stop(); SaveGlobalSettings(); }
            if (outputTimer.Enabled) { outputTimer.Stop(); SaveOutput(); }
            Sample.Stop();
            StopPreview();
            Elevated.Stop();
        }

        // (For the build's test: changes waiting to be saved are dropped, and the Preview's process.)
        public void StopTimers() { saveTimer.Stop(); globalTimer.Stop(); outputTimer.Stop(); }
        public Process PreviewProcess { get { return previewProcess; } }

        // A picture of the window (the build's test looks at them).
        public void Picture(string file)
        {
            using (Bitmap b = new Bitmap(Width, Height))
            {
                DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
                b.Save(file, ImageFormat.Png);
            }
        }
    }

    // The catalogue's sample of a voice (an MP3), played with Windows' own player.
    static class Sample
    {
        static object player;

        public static void Play(string file)
        {
            Stop();
            System.Windows.Media.MediaPlayer p = new System.Windows.Media.MediaPlayer();
            p.Open(new Uri(file));
            p.Play();
            player = p;
        }
        public static void Stop()
        {
            if (player == null) { return; }
            try { StopPlayer(); } catch (Exception) { }
            player = null;
        }
        static void StopPlayer()
        {
            System.Windows.Media.MediaPlayer p = (System.Windows.Media.MediaPlayer)player;
            p.Stop();
            p.Close();
        }
    }

    // ---------------------------------------------------------------- the administrator's part

    static class B64
    {
        public static string To(string text) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? "")); }
        public static string From(string coded) { return Encoding.UTF8.GetString(Convert.FromBase64String(coded)); }
    }

    // What the helper answered: 0 when done (for Apply changes, the installer's own exit code), and what
    // went wrong when something did.
    public class Answer
    {
        public int Code;
        public string Message;
    }

    // The window's side of the administrator's part: a second copy of this program started as
    // administrator (Windows asks for permission then, once) and kept until the window closes, so that
    // nothing asks again. The two talk over a pipe of the window's making, a line each way: "nick ..." or
    // "apply ..." one way; "say ..." (the installer's progress) and "done <code> [what went wrong]" back.
    // Everything after the command word is Base64, so a line is a line whatever is in it.
    public static class Elevated
    {
        static NamedPipeServerStream pipe;
        static StreamReader reader;
        static StreamWriter writer;
        static Process helper;
        static readonly object asking = new object();

        public static bool Ready
        {
            get
            {
                try { return pipe != null && pipe.IsConnected && helper != null && !helper.HasExited; }
                catch (Exception) { return false; }
            }
        }

        // Starts the helper if it is not there. Throws OperationCanceledException when Windows was not given
        // permission, and another exception (its message says what) when the helper did not start.
        public static void Start()
        {
            if (Ready) { return; }
            Stop();
            string name = "ModernVoicesConfig-" + Guid.NewGuid().ToString("N");
            // The pipe is for this Windows user and for administrators (the helper is one or the other).
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            NamedPipeServerStream server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            Process started = null;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--helper " + name + " " + Process.GetCurrentProcess().Id);
                start.UseShellExecute = true; start.Verb = "runas"; start.WindowStyle = ProcessWindowStyle.Hidden;
                try { started = Process.Start(start); }
                catch (Win32Exception e)
                {
                    if (e.NativeErrorCode == 1223) { throw new OperationCanceledException("Windows was not given permission, so nothing was changed."); }
                    throw;
                }
                IAsyncResult waiting = server.BeginWaitForConnection(null, null);
                DateTime until = DateTime.UtcNow.AddSeconds(30);
                while (!waiting.IsCompleted && DateTime.UtcNow < until && !started.HasExited) { System.Threading.Thread.Sleep(30); }
                if (!waiting.IsCompleted) { throw new IOException("The part of the program that runs as administrator did not start."); }
                server.EndWaitForConnection(waiting);
                // It is the copy just started that has connected, and nothing else.
                uint client;
                if (!Native.GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out client) || client != (uint)started.Id)
                {
                    throw new IOException("Something other than this program answered as its administrator's part.");
                }
            }
            catch (Exception)
            {
                try { server.Dispose(); } catch (Exception) { }
                try { if (started != null && !started.HasExited) { started.Kill(); } } catch (Exception) { }
                throw;
            }
            pipe = server;
            reader = new StreamReader(server, new UTF8Encoding(false));
            writer = new StreamWriter(server, new UTF8Encoding(false));
            writer.AutoFlush = true;
            helper = started;
        }

        public static void Stop()
        {
            try { if (pipe != null) { pipe.Dispose(); } } catch (Exception) { }      // (the helper ends when the pipe does)
            pipe = null; reader = null; writer = null;
            try { if (helper != null) { helper.Dispose(); } } catch (Exception) { }
            helper = null;
        }

        // Asks the helper for one thing and waits for its answer; what the installer says meanwhile goes to
        // 'said' (on the thread that asked).
        public static Answer Ask(string request, Action<string> said)
        {
            lock (asking)
            {
                try
                {
                    writer.WriteLine(request);
                    while (true)
                    {
                        string line = reader.ReadLine();
                        if (line == null) { throw new IOException("The part of the program that runs as administrator stopped."); }
                        if (line.StartsWith("say ", StringComparison.Ordinal))
                        {
                            if (said != null) { said(B64.From(line.Substring(4))); }
                        }
                        else if (line.StartsWith("done ", StringComparison.Ordinal))
                        {
                            string[] parts = line.Split(' ');
                            Answer answer = new Answer();
                            answer.Code = int.Parse(parts[1], CultureInfo.InvariantCulture);
                            answer.Message = parts.Length > 2 ? B64.From(parts[2]) : null;
                            return answer;
                        }
                    }
                }
                catch (Exception)
                {
                    Stop();
                    throw;
                }
            }
        }
    }

    // The helper's side (ModernVoicesConfig.exe --helper PIPE PID, as administrator): it does two things,
    // for the window that started it and for nothing else: gives an installed voice a nickname, and runs
    // the installer for a list of voices. It ends when the window does (the pipe closes).
    static class Helper
    {
        static readonly Regex GoodId = new Regex(@"^[A-Za-z0-9_\-]{1,80}(#[A-Za-z0-9_\-]{1,80})?$");

        public static int Run(string pipeName, string ownerText)
        {
            try
            {
                int owner = int.Parse(ownerText, CultureInfo.InvariantCulture);
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(15000);
                    // The pipe is the one made by the window that started this, and that window is this program.
                    uint server;
                    if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out server) || server != (uint)owner) { return 3; }
                    using (Process window = Process.GetProcessById(owner))
                    {
                        if (!string.Equals(window.MainModule.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) { return 3; }
                    }
                    App.Find();
                    StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false));
                    StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false));
                    writer.AutoFlush = true;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Answer answer = new Answer();
                        try { Do(line.Split(' '), writer, answer); }
                        catch (Exception e) { answer.Code = 1; answer.Message = e.Message; }
                        lock (writer) { writer.WriteLine("done " + answer.Code + (string.IsNullOrEmpty(answer.Message) ? "" : " " + B64.To(answer.Message))); }
                    }
                }
                return 0;
            }
            catch (Exception) { return 1; }
        }

        static void Do(string[] parts, StreamWriter writer, Answer answer)
        {
            if (parts.Length == 4 && parts[0] == "nick")
            {
                string id = B64.From(parts[1]), nick = B64.From(parts[2]), display = B64.From(parts[3]);
                if (!GoodId.IsMatch(id) || nick.Length > 60 || display.Length == 0 || display.Length > 200 || Regex.IsMatch(nick + display, @"[\x00-\x1F]"))
                {
                    throw new ArgumentException("That is not a name a voice can have.");
                }
                Voices.SetNickname(id, nick, display);
            }
            else if (parts.Length == 3 && parts[0] == "apply")
            {
                List<string> ids = new List<string>();
                foreach (string id in B64.From(parts[2]).Split(','))
                {
                    if (id.Length == 0) { continue; }
                    if (!GoodId.IsMatch(id)) { throw new ArgumentException("'" + id + "' is not a voice."); }
                    ids.Add(id);
                }
                RunInstaller(ids, parts[1] == "1", writer, answer);
            }
            else { throw new ArgumentException("That is not something this can do."); }
        }

        static void Say(StreamWriter writer, string line)
        {
            try { lock (writer) { writer.WriteLine("say " + B64.To(line)); } } catch (Exception) { }
        }

        // The installer, for exactly these voices (none: none installed), with no window: what it prints is
        // passed on as it comes. The answer is its exit code (0 done, 2 a voice failed its test) and, when
        // it stopped, the line that says why.
        static void RunInstaller(List<string> ids, bool usTags, StreamWriter writer, Answer answer)
        {
            string script = Path.Combine(App.Installer, "Install.ps1");
            if (!File.Exists(script)) { throw new FileNotFoundException("The installer was not found (" + script + ")."); }
            string arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -ExactVoices -Unattended -NoPause -Progress";
            if (ids.Count > 0) { arguments += " -Voices \"" + string.Join(",", ids.ToArray()) + "\""; }
            if (usTags) { arguments += " -UsEnglishTags"; }
            using (Process run = new Process())
            {
                run.StartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), arguments);
                run.StartInfo.UseShellExecute = false; run.StartInfo.CreateNoWindow = true;
                run.StartInfo.RedirectStandardOutput = true; run.StartInfo.RedirectStandardError = true;
                run.StartInfo.WorkingDirectory = App.Folder;
                run.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) { Say(writer, e.Data.Trim()); } };
                run.Start();
                run.BeginErrorReadLine();
                string stopped = null, line;
                while ((line = run.StandardOutput.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) { continue; }
                    if (line.StartsWith("Stopped:", StringComparison.Ordinal)) { stopped = line; }
                    Say(writer, line);
                }
                if (!run.WaitForExit(30000))
                {
                    try { run.Kill(); } catch (Exception) { }
                    throw new TimeoutException("The installer did not finish.");
                }
                answer.Code = run.ExitCode;
                answer.Message = stopped;
            }
        }
    }

    // ---------------------------------------------------------------- starting

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Preview: this program started again by its own window, to say one thing and end.
            if (args.Length >= 3 && args[0] == "--speak") { return Speak(args[1], args[2], false); }
            if (args.Length >= 3 && args[0] == "--speak-check") { return Speak(args[1], args[2], true); }
            // The administrator's part: this program started again by its own window (see Helper).
            if (args.Length >= 3 && args[0] == "--helper") { return Helper.Run(args[1], args[2]); }
            try { Native.SetProcessDPIAware(); } catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            App.Find();
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                return SelfTest.Run(args[1], Array.IndexOf(args, "--pictures") >= 0, Array.IndexOf(args, "--apply") >= 0);
            }
            // Already open: that window comes to the front.
            bool first;
            System.Threading.Mutex running = new System.Threading.Mutex(true, "ModernVoicesConfig.Running", out first);
            if (!first) { ShowTheOther(); return 0; }
            string problem;
            List<Entry> catalogue = Catalogue.Load(out problem);
            ConfigForm form = new ConfigForm(catalogue, Voices.InstalledIds(), Voices.UsEnglishTags);
            if (problem != null) { form.StatusLabel.Text = problem; }
            Application.Run(form);
            GC.KeepAlive(running);
            return 0;
        }

        static void ShowTheOther()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (Process other in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                try
                {
                    if (other.Id != self && other.MainWindowHandle != IntPtr.Zero)
                    {
                        if (Native.IsIconic(other.MainWindowHandle)) { Native.ShowWindowAsync(other.MainWindowHandle, 9); }
                        Native.SetForegroundWindow(other.MainWindowHandle);
                        break;
                    }
                }
                catch (Exception) { }
                finally { other.Dispose(); }
            }
        }

        // 0: spoken; 1: it could not be; 2 (checking only): spoken, but no sound came out.
        static int Speak(string voice64, string text64, bool check)
        {
            try
            {
                long made = SpeakNow(B64.From(voice64), B64.From(text64), check);
                return !check || made > 20000 ? 0 : 2;
            }
            catch (Exception) { return 1; }
        }
        static long SpeakNow(string voice, string text, bool toNowhere)
        {
            using (System.Speech.Synthesis.SpeechSynthesizer synth = new System.Speech.Synthesis.SpeechSynthesizer())
            using (MemoryStream sound = new MemoryStream())
            {
                synth.SelectVoice(voice);
                if (toNowhere) { synth.SetOutputToWaveStream(sound); } else { synth.SetOutputToDefaultAudioDevice(); }
                synth.Speak(text);
                synth.SetOutputToNull();
                return sound.Length;
            }
        }
    }

    // The build's test (ModernVoicesConfig.exe --selftest FOLDER [--pictures] [--apply]): the window is
    // built and driven as clicks would. What it finds goes in FOLDER\config-selftest.log (with pictures of
    // every tab, light, dark and small, when asked); the exit code is the number of failures. --apply also
    // adds and removes a voice through Apply changes, as a person would (only with voices installed).
    public static class SelfTest
    {
        public static bool Running;
        static StringBuilder log = new StringBuilder();
        static int failures;

        static void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) { failures++; }
        }
        static void Note(string text) { log.AppendLine(text); }

        static bool WaitWhile(Func<bool> busy, int seconds)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (busy() && DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            return !busy();
        }
        static void Settle(int milliseconds)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            do { Application.DoEvents(); System.Threading.Thread.Sleep(10); } while (DateTime.UtcNow < until);
        }

        public static int Run(string folder, bool pictures, bool apply)
        {
            Running = true;
            Directory.CreateDirectory(folder);
            // An error in one of the window's handlers is noted (Windows would otherwise show a box and wait
            // for a click that never comes).
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                log.AppendLine("  FAIL error in the window: " + e.Exception);
                failures++;
            };
            try { Body(folder, pictures, apply); }
            catch (Exception e)
            {
                log.AppendLine("  FAIL the test stopped: " + e);
                failures++;
            }
            finally
            {
                Theme.Override = null;
                try { File.WriteAllText(Path.Combine(folder, "config-selftest.log"), log.ToString(), Encoding.UTF8); } catch (Exception) { }
            }
            return failures;
        }

        static ListViewItem Find(ConfigForm ui, string key)
        {
            foreach (ListViewItem item in ui.AddList.Items) { if (((Entry)item.Tag).Key == key) { return item; } }
            return null;
        }
        static List<Rule> Rules(string from)
        {
            List<Rule> found = new List<Rule>();
            foreach (Rule rule in Store.GetDictionary(App.DictionaryFile)) { if (rule.From == from) { found.Add(rule); } }
            return found;
        }
        static List<string> TickedSpeakers(ConfigForm ui)
        {
            List<string> ticked = new List<string>();
            foreach (object item in ui.SpeakerList.CheckedItems) { ticked.Add(Convert.ToString(item)); }
            return ticked;
        }

        static void Body(string folder, bool pictures, bool apply)
        {
            Note("Modern Voices Config " + App.VersionText + ", program folder: " + App.Folder);
            string problem;
            List<Entry> catalogue = Catalogue.Load(out problem);
            Check(catalogue.Count > 20, "the catalogue is read: " + catalogue.Count + " English voices" + (problem == null ? "" : " (" + problem + ")"));
            List<string> real = Voices.InstalledIds();
            List<string> ids = real.Count > 0 ? real : new List<string>(new string[] { "en_GB-alba-medium", "en_GB-vctk-medium#p239" });
            DateTime started = DateTime.UtcNow;
            ConfigForm ui = new ConfigForm(catalogue, ids, Voices.UsEnglishTags);
            Note("the window was built in " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");
            ui.Show();
            Settle(200);
            // (Lists on tabs not yet opened are made now, as opening their tabs would.)
            IntPtr made = ui.AddList.Handle; made = ui.InstalledList.Handle; made = ui.WordList.Handle; made = ui.SpeakerList.Handle;
            Settle(100);
            Note("window: " + ui.AddList.Items.Count + " voices in the catalogue, " + ui.InstalledList.Items.Count + " installed shown; " + ui.SizeLabel.Text);
            Check(ui.AddList.Items.Count == catalogue.Count && ui.InstalledList.Items.Count == ids.Count, "the lists are filled: the catalogue's voices, a row for each installed voice");
            Check(ui.PageIndex == 0 && ui.Pages[0].Visible && !ui.Pages[3].Visible, "the window opens on the Voice manager");
            // As the window opens, nothing is waiting to be applied: Apply changes is greyed out.
            ui.UpdateApply();
            Check(!ui.ApplyButton.Enabled, "Apply changes is greyed out as the window opens");
            if (real.Count > 0)
            {
                Check(ui.CurrentInstalled != null && ui.Sliders["Volume"].Bar.Enabled && ui.SoundLabel.Text.StartsWith("How ", StringComparison.Ordinal),
                    "the first installed voice is selected, its sliders ready (" + ui.SoundLabel.Text + ")");
            }

            // Speakers: select VCTK, tick a second speaker.
            ListViewItem vctk = Find(ui, "en_GB-vctk-medium");
            Check(vctk != null, "VCTK is in the catalogue");
            if (vctk == null) { return; }
            Entry vctkEntry = (Entry)vctk.Tag;
            vctk.Selected = true;
            ui.UpdateSelection();
            int count = ui.SpeakerList.Items.Count;
            List<string> before = TickedSpeakers(ui);
            int index = 0;
            while (index < count - 1 && before.Contains(Convert.ToString(ui.SpeakerList.Items[index]))) { index++; }
            string other = Convert.ToString(ui.SpeakerList.Items[index]);
            ui.SpeakerList.SetItemChecked(index, true);
            ui.SaveSpeakers();
            List<string> picked = ui.PickedIds();
            Note("speakers: " + count + " listed for VCTK, ticked " + string.Join(",", before.ToArray()) + "; after ticking " + other + ": " + string.Join(" ", picked.ToArray()));
            Check(count == vctkEntry.SpeakerNames.Count && picked.Contains("en_GB-vctk-medium#" + other), "a voice's speakers are listed, and a second one ticked is picked");
            foreach (string kept in before) { Check(picked.Contains("en_GB-vctk-medium#" + kept), "speaker " + kept + " is still picked"); }
            ui.SpeakerList.SetItemChecked(index, false);
            ui.SaveSpeakers();

            // Two qualities of one voice: the warning.
            List<ListViewItem> lessac = new List<ListViewItem>();
            foreach (ListViewItem item in ui.AddList.Items)
            {
                if (((Entry)item.Tag).Name == "lessac" && !item.Checked) { item.Checked = true; lessac.Add(item); }
            }
            ui.UpdateSummary();
            Note("warning: " + ui.WarnLabel.Text);
            Check(ui.WarnLabel.Text.Contains("Lessac"), "two qualities of one voice ticked: the warning shows");
            ui.UpdateApply();
            Check(ui.ApplyButton.Enabled, "a voice ticked: Apply changes is lit");

            if (real.Count > 0 && ui.InstalledList.Items.Count > 0)
            {
                // Sliders, on a real installed voice: saved to its voice.json.
                ui.ShowPage(0);
                ui.InstalledList.Items[0].Selected = true;
                ui.UpdateInstalled();
                string dir = ui.CurrentInstalled.Dir;
                VoiceSettings was = Store.GetVoice(dir);
                ui.Sliders["Volume"].Bar.Value = 57; ui.Sliders["Boost"].Bar.Value = 20; ui.Sliders["Speed"].Bar.Value = 80;
                Check(ui.Sliders["Volume"].Bar.Value == 60, "the Volume slider moves in tens: 57 snaps to " + ui.Sliders["Volume"].Bar.Value);
                ui.SaveSettings();
                ui.StopTimers();
                VoiceSettings now = Store.GetVoice(dir);
                Check(now.Peak == 0.6 && now.Boost == 2 && now.LengthScale == 1.25,
                    "sliders: " + ui.CurrentInstalled.Title + " -> peak " + now.Peak + ", boost " + now.Boost + ", length_scale " + now.LengthScale);
                // Above 100%: to 200%, kept as a peak above 1.
                ui.Sliders["Volume"].Bar.Value = 170;
                ui.SaveSettings();
                ui.StopTimers();
                now = Store.GetVoice(dir);
                Check(now.Peak == 1.7 && ui.Sliders["Volume"].Bar.Maximum == 200 && ui.Sliders["Volume"].Value.Text == "170%",
                    "the Volume slider goes to " + ui.Sliders["Volume"].Bar.Maximum + "%: 170% is kept as peak " + now.Peak);
                Check(File.Exists(App.VoiceFile(dir)) && App.VoiceFile(dir).StartsWith(App.SettingsFolder, StringComparison.OrdinalIgnoreCase),
                    "a voice's settings are kept in the settings folder (" + App.VoiceFile(dir) + "), which needs no administrator");
                Store.SetVoice(dir, was.Peak ?? App.DefaultPeak, was.Boost ?? App.DefaultBoost, was.LengthScale ?? 1);
                ui.UpdateInstalled();
                Check(ui.Sliders["Volume"].Bar.Value == (int)Math.Round((was.Peak ?? App.DefaultPeak) * 100), "the sliders show the voice's settings again (volume " + ui.Sliders["Volume"].Bar.Value + "%)");

                // The Speed and pauses tab: saved to settings.json.
                Pauses pausesBefore = Store.GetPauses(App.SettingsFile);
                double speedBefore = Store.GetGlobalSpeed(App.SettingsFile);
                ui.GlobalBars["Sentence gap"].Bar.Value = 750;
                ui.SpeedBar.Value = 150;
                ui.SaveGlobalSettings();
                ui.StopTimers();
                Pauses pausesNow = Store.GetPauses(App.SettingsFile);
                double speedNow = Store.GetGlobalSpeed(App.SettingsFile);
                Check(pausesNow.Sentence == 750 && speedNow == 150 && speedBefore == 100 && pausesNow.Paragraph == pausesBefore.Paragraph,
                    "speed and pauses tab: speed " + speedBefore + " -> " + speedNow + "%, sentence " + pausesBefore.Sentence + " -> " + pausesNow.Sentence +
                    " ms; example: " + ui.GlobalBars["Sentence gap"].Example.Text);
                Check(ui.GlobalBars["Sentence gap"].Example.Text == "at 150%: 500 ms at rate 0, 401 ms at rate +2, 623 ms at rate -2", "the line under a pause shows what you get at other speeds");
                Store.SetGlobalSpeed(App.SettingsFile, speedBefore);
                Store.SetGlobalPauses(App.SettingsFile, pausesBefore.Sentence, pausesBefore.Comma, pausesBefore.Paragraph);
                ui.SetGlobalBars(Store.GetPauses(App.SettingsFile), Store.GetGlobalSpeed(App.SettingsFile));

                // The output volume, for every voice: saved to settings.json as it moves.
                double outputWas = Store.GetOutput(App.SettingsFile);
                ui.OutputBar.Value = 40;
                ui.SaveOutput();
                ui.StopTimers();
                double outputNow = Store.GetOutput(App.SettingsFile);
                ui.OutputBar.Value = 0;
                string silent = ui.OutputValue.Text;
                ui.SaveOutput();
                ui.StopTimers();
                Check(outputWas == 100 && outputNow == 40 && Store.GetOutput(App.SettingsFile) == 0 && silent.Contains("silent") && Store.GetGlobalSpeed(App.SettingsFile) == speedBefore,
                    "output volume: " + outputWas + "% -> " + outputNow + "% -> 0% ('" + silent + "'), the speed beside it in the file untouched");
                Store.SetOutput(App.SettingsFile, outputWas);
                ui.UpdateOutput();
                Check(ui.OutputBar.Value == 100 && ui.OutputValue.Text == "100%", "the output volume slider shows the file's value again (" + ui.OutputValue.Text + ")");

                // Windows' own speed: the warning shows when it isn't 0, and the button sets it back.
                int? rateWas = Voices.WindowsRateStored;
                Voices.WindowsRateStored = 2;
                ui.UpdateWindowsRate();
                bool warned = ui.RateWarn.Text.Contains("+2");
                Voices.WindowsRateStored = 0;      // what the button does
                ui.UpdateWindowsRate();
                bool cleared = Voices.WindowsRate == 0 && ui.RateWarn.Text.Length == 0;
                Check(warned && cleared, "Windows' speed +2: warning shown " + warned + "; set back to 0, it goes " + cleared);
                Voices.WindowsRateStored = rateWas;

                // A voice's own pauses: off to begin with (its sliders hidden); ticked, its sentence gap moved
                // and saved to its voice.json; unticked again.
                bool offFirst = !ui.OwnBox.Checked && !ui.OwnPausesShown;
                ui.OwnBox.Checked = true;
                ui.UpdateOwnPauses();
                bool shown = ui.OwnPausesShown && ui.Sliders["Sentence gap"].Bar.Enabled;
                ui.Sliders["Sentence gap"].Bar.Value = 300;
                ui.SaveSettings();
                ui.StopTimers();
                Pauses own = Store.GetVoicePauses(dir);
                ui.UpdateApply();
                if (pictures) { Pictures(ui, folder); }
                ui.OwnBox.Checked = false;
                ui.UpdateOwnPauses();
                ui.SaveSettings();
                ui.StopTimers();
                Pauses back = Store.GetVoicePauses(dir);
                Check(offFirst && shown && own.Own && own.Sentence == 300 && !back.Own,
                    "own pauses: off at first " + offFirst + "; ticked: sliders shown " + shown + ", own " + own.Own + ", sentence " + own.Sentence + " ms; unticked: own " + back.Own);

                // A nickname: the voice renamed where Windows lists it, and back.
                string plain = ui.GetCurrentVoice().Voice;
                ui.NickBox.Text = "Test (Name)";
                ui.SetNickname();
                string named = ui.GetCurrentVoice().Voice;
                bool listed = Voices.OurNames().Contains(named);
                string boxShows = ui.NickBox.Text;
                ui.NickBox.Text = "";
                ui.SetNickname();
                string restored = ui.GetCurrentVoice().Voice;
                Check(named.EndsWith("(Test Name)", StringComparison.Ordinal) && listed && boxShows == "Test Name" && restored == plain && Voices.OurNames().Contains(plain),
                    "nickname: " + plain + " -> " + named + " (Windows lists it: " + listed + "; box shows '" + boxShows + "') -> " + restored);

                // A pronunciation rule: added, changed, switched off and removed, each saved to the dictionary file.
                ui.ShowPage(2);
                Settle(100);
                int rulesBefore = Store.GetDictionary(App.DictionaryFile).Count;
                int offBefore = 0, offShown = 0;
                foreach (Rule rule in Store.GetDictionary(App.DictionaryFile)) { if (!rule.On) { offBefore++; } }
                foreach (ListViewItem row in ui.WordList.Items) { if (!row.Checked) { offShown++; } }
                Check(ui.WordList.Items.Count == rulesBefore && offShown == offBefore,
                    "the Pronunciation list shows the dictionary's " + rulesBefore + " rules (" + offShown + " switched off), and opening the tab changes none");
                ui.WordFrom.Text = " Niamh "; ui.WordTo.Text = "Neev";
                ui.SaveWord();
                List<Rule> added = Rules("Niamh");
                ui.WordTo.Text = "Neeve"; ui.WordCase.Checked = true;
                ui.SaveWord();
                List<Rule> changed = Rules("Niamh");
                bool selected = ui.WordList.SelectedItems.Count == 1;
                if (selected) { ui.WordList.SelectedItems[0].Checked = false; }      // (unticking saves)
                Settle(50);
                List<Rule> off = Rules("Niamh");
                ui.RemoveWord();
                int rulesAfter = Store.GetDictionary(App.DictionaryFile).Count;
                Check(added.Count == 1 && added[0].To == "Neev" && added[0].On && added[0].Whole, "pronunciation: a rule is added (Niamh as Neev, on, whole words)");
                Check(changed.Count == 1 && changed[0].To == "Neeve" && changed[0].Case, "pronunciation: the selected rule is changed, not added again");
                Check(selected && off.Count == 1 && !off[0].On, "pronunciation: unticking a rule switches it off");
                Check(rulesAfter == rulesBefore && Rules("Niamh").Count == 0, "pronunciation: Remove takes the rule out (" + rulesBefore + " -> " + rulesAfter + " rules)");
                ui.ShowPage(0);

                // Preview: the selected voice by the name programs see; the Speed and pauses tab lists every voice.
                CurrentVoice voice = ui.GetCurrentVoice();
                List<string> known = Voices.OurNames();
                bool allKnown = ui.PreviewVoiceBox.Items.Count == known.Count;
                foreach (object offered in ui.PreviewVoiceBox.Items) { if (!known.Contains(Convert.ToString(offered))) { allKnown = false; } }
                Check(known.Contains(voice.Voice) && allKnown, "preview: the Voice manager would say it with '" + voice.Voice + "'; Speed and pauses offers " +
                    ui.PreviewVoiceBox.Items.Count + " voice(s), '" + ui.PreviewVoiceBox.SelectedItem + "' chosen");
                using (Process checking = ConfigForm.StartSpeaker("--speak-check", voice.Voice, "Testing the preview of this voice."))
                {
                    bool said = checking.WaitForExit(120000);
                    if (!said) { try { checking.Kill(); } catch (Exception) { } }
                    Check(said && checking.ExitCode == 0, "the second copy of the program speaks with that voice (to nowhere, for this test: " + (said ? "code " + checking.ExitCode : "it did not finish") + ")");
                }
                // The Preview button: it starts the selected voice; pressed again while that voice speaks, it
                // stops it; pressed with another voice selected, that one stops and the new one starts. (The
                // speaking is to nowhere here, and of a long text, so that it lasts while the test looks.)
                ConfigForm.SpeakHow = "--speak-check";
                string was1 = ui.PreviewBox.Text;
                ui.PreviewBox.Text = new StringBuilder().Insert(0, "This is a long preview, said to nowhere, for the test of the button. ", 60).ToString();
                ui.PreviewButton.PerformClick();
                Process speaking = ui.PreviewProcess;
                bool begun = ui.PreviewTalking && ui.PreviewButton.Text == "Stop";
                ui.PreviewButton.PerformClick();
                bool stopped = !ui.PreviewTalking && ui.PreviewButton.Text == "Preview" && ui.PreviewProcess == null;
                Check(speaking != null && begun && stopped, "Preview starts the selected voice (the button reads Stop: " + begun + "), and pressed again stops it (" + stopped + ")");
                if (ui.InstalledList.Items.Count > 1)
                {
                    ui.PreviewButton.PerformClick();
                    Process one = ui.PreviewProcess;
                    ui.InstalledList.Items[1].Selected = true;
                    ui.UpdateInstalled();
                    bool otherShown = ui.PreviewButton.Text == "Preview" && ui.PreviewTalking;
                    ui.PreviewButton.PerformClick();
                    Process two = ui.PreviewProcess;
                    bool swapped = one != null && two != null && !ReferenceEquals(one, two) && ui.PreviewTalking && ui.PreviewButton.Text == "Stop";
                    Check(otherShown && swapped, "with another voice selected the button reads Preview (" + otherShown + "), and pressing it stops the first voice and starts that one (" + swapped + ")");
                    ui.ShowPage(1);
                    Settle(50);
                    ui.GlobalPreview.PerformClick();      // (Speed and pauses: another voice is speaking, so its own starts)
                    bool onOther = ui.PreviewTalking && ui.GlobalPreview.Text == "Stop" && ui.PreviewButton.Text == "Preview";
                    ui.GlobalPreview.PerformClick();
                    ui.ShowPage(0);
                    Settle(50);
                    Check(onOther && !ui.PreviewTalking && ui.GlobalPreview.Text == "Preview", "the Speed and pauses tab's Preview does the same with the voice chosen there");
                    ui.InstalledList.Items[0].Selected = true;
                    ui.UpdateInstalled();
                }
                // Left to finish, the button reads Preview again by itself.
                ui.PreviewBox.Text = "Short.";
                ui.PreviewButton.PerformClick();
                bool ended = WaitWhile(delegate { return ui.PreviewTalking || ui.PreviewButton.Text != "Preview"; }, 120);
                Check(ended, "when the voice has finished, the button reads Preview again");
                ui.StopPreview();
                ui.PreviewBox.Text = was1;
                ConfigForm.SpeakHow = "--speak";
            }

            // Apply with nothing changed: says so, changes nothing.
            foreach (ListViewItem item in lessac) { item.Checked = false; }
            ui.Speakers.Clear();
            foreach (string id in ids)
            {
                string speaker = Voices.SpeakerOf(id);
                if (speaker == null) { continue; }
                string key = Voices.KeyOf(id);
                ui.Speakers[key] = ui.Speakers.ContainsKey(key) ? ui.Speakers[key] + ", " + speaker : speaker;
            }
            ui.Current = null;
            ui.Apply();
            Check(ui.StatusLabel.Text == "Nothing to change." && !ui.Busy, "Apply with nothing changed says so (" + ui.StatusLabel.Text + ")");
            // Apply changes is greyed out with nothing to apply, and not once a tick differs.
            ui.UpdateApply();
            bool greyed = !ui.ApplyButton.Enabled;
            ListViewItem first = null;
            foreach (ListViewItem item in ui.AddList.Items) { if (!item.Checked) { first = item; break; } }
            if (first != null) { first.Checked = true; }
            ui.UpdateApply();
            bool lit = ui.ApplyButton.Enabled;
            if (first != null) { first.Checked = false; }
            ui.UpdateApply();
            Check(greyed && lit && !ui.ApplyButton.Enabled, "Apply changes: nothing to apply, greyed out " + greyed + "; a voice ticked, lit " + lit + "; unticked again, greyed out " + !ui.ApplyButton.Enabled);

            // Windows' default voice, set from the Voice manager (put back afterwards).
            if (real.Count > 0 && ui.InstalledList.Items.Count > 0)
            {
                string defaultWas = Voices.DefaultTokenId;
                ui.InstalledList.Items[0].Selected = true;
                ui.UpdateInstalled();
                string token = ui.GetCurrentVoice().Token;
                ui.SetDefaultVoice();
                string defaultNow = Voices.DefaultTokenId;
                string marked = ui.InstalledList.Items[0].SubItems[4].Text;
                Check(defaultNow == @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\" + token && marked == "yes",
                    "default voice: " + defaultNow + " (marked '" + marked + "'); " + ui.DefaultLabel.Text);
                Voices.DefaultTokenId = defaultWas;
            }

            // A sample, downloaded as Play sample does.
            string sample = Path.Combine(Path.GetTempPath(), "modernvoices_selftest.mp3");
            try { File.Delete(sample); } catch (Exception) { }
            Catalogue.Download(App.BaseUrl + "/en/en_GB/alba/medium/samples/speaker_0.mp3", sample);
            Check(new FileInfo(sample).Length > 1000, "a voice's sample downloads (" + new FileInfo(sample).Length + " bytes)");
            bool played = false;
            string notPlayed = "";
            try { Sample.Play(sample); Settle(300); Sample.Stop(); played = true; } catch (Exception e) { notPlayed = e.Message; }
            Check(played, "the sample is given to Windows' player without an error (not heard: this test can't listen) " + notPlayed);

            // Apply changes for real, as a person would: a second VCTK speaker ticked (no download: the model
            // is shared) and a voice not yet installed (downloaded, with the bar moving); then both unticked
            // again. The installer does the work; the window shows what it says and the new state.
            if (apply && real.Count > 0)
            {
                List<string> had = new List<string>(ui.Installed);
                string defaultBefore = Voices.DefaultTokenId;
                Voices.DefaultTokenId = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\TTS_MS_EN-GB_HAZEL_11.0";      // (one of Windows' own)
                ui.ShowPage(3);
                Settle(100);
                vctk.Selected = true;
                ui.UpdateSelection();
                List<string> tickedNow = TickedSpeakers(ui);
                int extraIndex = 0;
                while (extraIndex < ui.SpeakerList.Items.Count - 1 && tickedNow.Contains(Convert.ToString(ui.SpeakerList.Items[extraIndex]))) { extraIndex++; }
                string extra = "en_GB-vctk-medium#" + Convert.ToString(ui.SpeakerList.Items[extraIndex]);
                ui.SpeakerList.SetItemChecked(extraIndex, true);
                ListViewItem alan = Find(ui, "en_GB-alan-medium");
                bool download = alan != null && !alan.Checked;
                if (download) { alan.Checked = true; }
                string alanFolder = Path.Combine(App.VoiceRoot, "en_GB-alan-medium");
                int more = download ? 2 : 1;
                ui.UpdateApply();
                Check(ui.ApplyButton.Enabled, "a second speaker and another voice ticked: Apply changes is lit (" + ui.SizeLabel.Text + ")");
                ui.Apply();
                Check(ui.Busy && ui.Progress.Visible && !ui.CloseButton.Enabled, "Apply changes: the installer is started, the window waits (progress shown, Close greyed out)");
                bool finished = WaitWhile(delegate { return ui.Busy; }, 1200);
                List<string> after = Voices.InstalledIds();
                Note("apply (add " + extra + (download ? " and en_GB-alan-medium" : "") + "): " + ui.ApplyLines.Count + " lines from the installer; status: " + ui.StatusLabel.Text);
                foreach (string line in ui.ApplyLines) { if (!line.StartsWith("##progress", StringComparison.Ordinal)) { Note("    | " + line); } }
                Check(finished && after.Count == had.Count + more && after.Contains(extra) && ui.StatusLabel.Text.StartsWith("Done", StringComparison.Ordinal),
                    "Apply changes adds them: " + after.Count + " voices installed, every one tested");
                if (download)
                {
                    Check(after.Contains("en_GB-alan-medium") && File.Exists(Path.Combine(alanFolder, "model.onnx")) && ui.ProgressSeen >= 90,
                        "the new voice was downloaded, with the bar moving (it reached " + ui.ProgressSeen + "%)");
                }
                Check(Voices.DefaultToken == Voices.TokenName(after[0]) && ui.InstalledList.Items[0].SubItems[4].Text == "yes",
                    "Windows' default was one of its own voices: the first voice of the list is now the default (" + ui.DefaultLabel.Text + ")");
                Check(ui.InstalledList.Items.Count == after.Count && ui.PageIndex == 0 && !ui.ApplyButton.Enabled && ui.CloseButton.Enabled && !ui.Progress.Visible,
                    "afterwards the Voice manager shows them (" + ui.InstalledList.Items.Count + " rows), with nothing left to apply");
                ui.SpeakerList.SetItemChecked(extraIndex, false);
                if (download) { alan.Checked = false; }
                ui.Apply();
                finished = WaitWhile(delegate { return ui.Busy; }, 1200);
                after = Voices.InstalledIds();
                Note("apply (remove them again): " + ui.ApplyLines.Count + " lines from the installer; status: " + ui.StatusLabel.Text);
                Check(finished && after.Count == had.Count && !after.Contains(extra) && !after.Contains("en_GB-alan-medium") && ui.StatusLabel.Text.StartsWith("Done", StringComparison.Ordinal),
                    "Apply changes removes them again: " + after.Count + " voices installed");
                if (download) { Check(!Directory.Exists(alanFolder), "the removed voice's files are gone"); }
                Check(Voices.DefaultToken == Voices.TokenName(after[0]), "a Modern Voice that is Windows' default stays the default");
                Check(Elevated.Ready, "the administrator's part was started once and is still there (nothing asks twice)");
                Voices.DefaultTokenId = defaultBefore;
            }
            ui.StopTimers();
            ui.Close();
            ui.Dispose();
        }

        // Pictures of the window's tabs: as it opens, dark, and made small.
        static void Pictures(ConfigForm ui, string folder)
        {
            string[][] shapes = { new string[] { "open", "0", "0", "light" }, new string[] { "dark", "0", "0", "dark" }, new string[] { "small", "640", "440", "light" } };
            int page = ui.PageIndex;
            foreach (string[] shape in shapes)
            {
                if (shape[1] != "0") { ui.Size = new Size(int.Parse(shape[1]), int.Parse(shape[2])); }
                Theme.Override = shape[3];
                ui.ApplyTheme();
                for (int i = 0; i < ui.Pages.Count; i++)
                {
                    ui.ShowPage(i);
                    Settle(200);
                    ui.Picture(Path.Combine(folder, shape[0] + "-" + (i + 1) + ".png"));
                }
            }
            Theme.Override = null;
            ui.ApplyTheme();
            ui.ShowPage(page);
            Note("pictures of the window: " + folder);
        }
    }
}
