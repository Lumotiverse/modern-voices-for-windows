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
[assembly: System.Reflection.AssemblyVersion("0.18.7.0")]

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

        // A list's column headings: the little arrow that says which column it is sorted by.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct HDITEM
        {
            public int mask, cxy;
            public IntPtr pszText, hbm;
            public int cchTextMax, fmt;
            public IntPtr lParam;
            public int iImage, iOrder;
            public uint type;
            public IntPtr pvFilter;
            public uint state;
        }
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendHeaderItem(IntPtr hwnd, int message, IntPtr index, ref HDITEM item);
        const int LVM_GETHEADER = 0x101F, HDM_GETITEMW = 0x120B, HDM_SETITEMW = 0x120C, HDI_FORMAT = 0x4, HDF_SORTDOWN = 0x200, HDF_SORTUP = 0x400;
        // column: the one sorted by (-1: none), down: Z to A.
        public static void SortArrows(ListView view, int column, bool down)
        {
            if (!view.IsHandleCreated) { return; }
            IntPtr header = SendMessage(view.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header == IntPtr.Zero) { return; }
            for (int i = 0; i < view.Columns.Count; i++)
            {
                HDITEM item = new HDITEM();
                item.mask = HDI_FORMAT;
                if (SendHeaderItem(header, HDM_GETITEMW, new IntPtr(i), ref item) == IntPtr.Zero) { continue; }
                item.fmt &= ~(HDF_SORTDOWN | HDF_SORTUP);
                if (i == column) { item.fmt |= down ? HDF_SORTDOWN : HDF_SORTUP; }
                SendHeaderItem(header, HDM_SETITEMW, new IntPtr(i), ref item);
            }
        }
    }

    // ---------------------------------------------------------------- where the program is

    public static class App
    {
        public const string Name = "Modern Voices for Windows";
        public const string BaseUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0";   // v1.0.0: the voices as tested
        public const double DefaultPeak = 1;
        public const double DefaultBoost = 1.5;
        public static string PreviewText { get { return PreviewTexts.For("en"); } }
        public const string OldPreviewText = "Speaking a preview of the selected voice, to hear how natural it sounds.\r\n" +
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
        // The file of a language's pronunciation rules (as the engine names them): dictionary.txt for
        // English, dictionary-de.txt for German.
        public static string DictionaryFor(string family)
        {
            return string.IsNullOrEmpty(family) || family == "en" ? DictionaryFile : Path.Combine(SettingsFolder, "dictionary-" + family + ".txt");
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
        public string Family, Language;     // "de" and "German" (from the catalogue)
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

        // The languages offered beside English ("Additional languages"): European ones written left to
        // right with spaces between words, which the engine's handling of sentences, pauses and words fits.
        // Not offered: languages written right to left or without spaces (Arabic, Persian, Chinese), and
        // the others not looked at yet. (The installer itself installs a voice of any language it is given.)
        public static readonly string[] Offered = { "bg", "ca", "cs", "cy", "da", "de", "el", "es", "eu", "fi", "fr", "gl", "hu", "is", "it",
            "lb", "lv", "nl", "no", "pl", "pt", "ro", "ru", "sk", "sl", "sr", "sv", "tr", "uk" };

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

        // The voices of medium or high quality, English and the languages offered: English first, then by
        // language, accent and name. The list is the installer's
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
            // (A language the catalogue has from several countries: its voices say which, as the installer's do.)
            Dictionary<string, HashSet<string>> codesOf = new Dictionary<string, HashSet<string>>();
            foreach (KeyValuePair<string, object> pair in all)
            {
                object language = Value(pair.Value, "language");
                string itsFamily = Convert.ToString(Value(language, "family"), CultureInfo.InvariantCulture);
                if (!codesOf.ContainsKey(itsFamily)) { codesOf[itsFamily] = new HashSet<string>(); }
                codesOf[itsFamily].Add(Convert.ToString(Value(language, "code"), CultureInfo.InvariantCulture));
            }
            foreach (KeyValuePair<string, object> pair in all)
            {
                object language = Value(pair.Value, "language");
                string family = Convert.ToString(Value(language, "family"), CultureInfo.InvariantCulture);
                if (family != "en" && Array.IndexOf(Offered, family) < 0) { continue; }
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
                entry.Family = family;
                entry.Language = Convert.ToString(Value(language, "name_english"), CultureInfo.InvariantCulture);
                if (entry.Language.Length == 0) { entry.Language = entry.Code; }
                string country = Convert.ToString(Value(language, "country_english"), CultureInfo.InvariantCulture);
                entry.Accent = entry.Code == "en_GB" ? "British" : entry.Code == "en_US" ? "American"
                    : codesOf[family].Count > 1 && country.Length > 0 ? entry.Language + ", " + country : entry.Language;
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
                int order = (a.Family == "en" ? 0 : 1).CompareTo(b.Family == "en" ? 0 : 1);
                if (order == 0) { order = string.Compare(a.Language, b.Language, StringComparison.CurrentCultureIgnoreCase); }
                if (order == 0) { order = string.Compare(a.Accent, b.Accent, StringComparison.CurrentCultureIgnoreCase); }
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

        // A voice's gender as Windows has it (SAPI's Gender attribute, which programs that pick a voice by
        // gender go by): "Female", "Male", or "" (none given).
        public static string Gender(string id)
        {
            try
            {
                using (RegistryKey attributes = Registry.LocalMachine.OpenSubKey(Tokens64 + "\\" + TokenName(id) + @"\Attributes"))
                {
                    string value = attributes == null ? null : attributes.GetValue("Gender") as string;
                    return NormalGender(value);
                }
            }
            catch (Exception) { return ""; }
        }
        public static string NormalGender(string value)
        {
            if (string.Equals(value, "Female", StringComparison.OrdinalIgnoreCase)) { return "Female"; }
            if (string.Equals(value, "Male", StringComparison.OrdinalIgnoreCase)) { return "Male"; }
            return "";
        }

        // Sets (or, empty, removes) a voice's gender where Windows lists it, for 64- and 32-bit programs, and
        // keeps the choice (an empty one too) under the program's settings key, so that Repair and updates
        // give the voice the same. The administrator's to do: the helper's part (see Helper).
        public static void SetGender(string id, string gender)
        {
            gender = NormalGender(gender);
            bool found = false;
            foreach (string root in new string[] { Tokens64, Tokens32 })
            {
                using (RegistryKey token = Registry.LocalMachine.OpenSubKey(root + "\\" + TokenName(id), true))
                {
                    if (token == null) { continue; }
                    found = true;
                    using (RegistryKey attributes = token.CreateSubKey("Attributes"))
                    {
                        if (gender.Length > 0) { attributes.SetValue("Gender", gender); } else { attributes.DeleteValue("Gender", false); }
                    }
                }
            }
            if (!found) { throw new InvalidOperationException("That voice is not installed."); }
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(OurSettings + @"\Genders")) { key.SetValue(id, gender); }
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
        public const int SentenceDefault = 500, CommaDefault = 60, ParagraphDefault = 1200;
        public const int SpeedDefault = 100;
        public const double MaxPeak = 1;

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

        // How much of Piper's own variation the voices use, in % (settings.json's variation_percent: 100 is
        // Piper's own; 75 unless set). The engine loads a voice with it.
        public const int VariationDefault = 60, VariationLimit = 150;
        public static double GetVariation(string path)
        {
            double? found = Read(ReadAll(path), "variation_percent");
            if (!found.HasValue) { return VariationDefault; }
            return Math.Max(0, Math.Min(VariationLimit, found.Value));
        }
        public static void SetVariation(string path, double percent)
        {
            if (percent < 0 || percent > VariationLimit) { throw new ArgumentException("variation must be 0 to " + VariationLimit + "% (got " + Format(percent) + ")"); }
            SetGlobalNumber(path, "variation_percent", percent);
        }

        // A voice's own variation (its own settings file's own_variation_percent; null: none set), used
        // by the engine while the voice overrides the settings for every voice (its own pauses' tick).
        public static double? GetVoiceVariation(string dir)
        {
            double? found = Read(ReadAll(App.VoiceFile(dir)), "own_variation_percent");
            if (!found.HasValue) { return null; }
            return Math.Max(0, Math.Min(VariationLimit, found.Value));
        }
        public static void SetVoiceVariation(string dir, double percent)
        {
            if (percent < 0 || percent > VariationLimit) { throw new ArgumentException("variation must be 0 to " + VariationLimit + "% (got " + Format(percent) + ")"); }
            SetGlobalNumber(App.VoiceFile(dir), "own_variation_percent", percent);
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
        // Whether a list of rules is switched off as a whole (a line "#off": its rules are kept, none is used).
        public static bool DictionaryOff(string path)
        {
            if (!File.Exists(path)) { return false; }
            foreach (string line in File.ReadAllLines(path, Utf8)) { if (line.Trim() == "#off") { return true; } }
            return false;
        }
        public static void SetDictionary(string path, IEnumerable<Rule> rules, bool off = false)
        {
            List<string> lines = new List<string>();
            lines.Add("# Modern Voices for Windows - pronunciation dictionary: on, whole words (w) or parts too (p), capitals matched (c) or not (i), said, as");
            if (off) { lines.Add("#off"); }
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
        // The tabs' buttons: one not selected (its fill is Tab) has TabEdge around it and TabText on it;
        // the selected one (filled as the lists are) has TabOn around it. Line: under the header.
        public Color TabEdge, TabText, TabOn;
    }

    // Light or dark: as last chosen with the moon (kept for the Windows user and shared with the Reader);
    // dark until light is chosen.
    // The additional languages chosen (their voices are listed on Add or remove voices): the person's
    // own choice, kept as the theme is.
    // The text the preview speaks before any is typed: the same words in each language offered (commas, full
    // stops and lines where they are, so the pauses can be heard), in the voice's own language. The
    // translations are Claude's, not checked by a speaker of each; 'Paragraph gap' is left in English, as
    // the window names it so.
    public static class PreviewTexts
    {
        public static readonly Dictionary<string, string> ByLanguage = new Dictionary<string, string>
        {
            { "en", "Speaking a preview of the selected voice, to hear how natural it sounds.\r\n" +
                "You can change the volume, dictation speed, and 'pause' values, to modify each voice sound to your liking.\r\n" +
                "Change the 'Paragraph gap', to adjust the wait time before a new paragraph starts." },
            { "bg", "Това е преглед на избрания глас, за да чуете колко естествено звучи.\r\n" +
                "Можете да промените силата на звука, скоростта на диктовка, и стойностите за 'пауза', за да настроите звученето на всеки глас по ваш вкус.\r\n" +
                "Променете 'Paragraph gap', за да настроите времето за изчакване преди началото на нов абзац." },
            { "ca", "Aquesta és una previsualització de la veu seleccionada, per escoltar com de natural sona.\r\n" +
                "Podeu canviar el volum, la velocitat de dictat, i els valors de 'pausa', per ajustar el so de cada veu al vostre gust.\r\n" +
                "Canvieu el 'Paragraph gap', per ajustar el temps d'espera abans que comenci un paràgraf nou." },
            { "cs", "Toto je ukázka vybraného hlasu, abyste slyšeli, jak přirozeně zní.\r\n" +
                "Můžete změnit hlasitost, rychlost diktování, a hodnoty 'pauzy', abyste zvuk každého hlasu upravili podle svých představ.\r\n" +
                "Změňte 'Paragraph gap', abyste upravili čekací dobu před začátkem nového odstavce." },
            { "cy", "Dyma ragolwg o'r llais a ddewiswyd, i glywed pa mor naturiol mae'n swnio.\r\n" +
                "Gallwch newid lefel y sain, cyflymder arddweud, a gwerthoedd 'saib', i addasu sain pob llais at eich dant.\r\n" +
                "Newidiwch y 'Paragraph gap', i addasu'r amser aros cyn i baragraff newydd ddechrau." },
            { "da", "Dette er en forhåndsvisning af den valgte stemme, så du kan høre, hvor naturlig den lyder.\r\n" +
                "Du kan ændre lydstyrken, dikteringshastigheden, og 'pause'-værdierne, for at tilpasse hver stemmes lyd efter din smag.\r\n" +
                "Skift 'Paragraph gap', for at justere ventetiden, før et nyt afsnit begynder." },
            { "de", "Hier hören Sie eine Vorschau der ausgewählten Stimme, damit Sie hören, wie natürlich sie klingt.\r\n" +
                "Sie können die Lautstärke, die Diktiergeschwindigkeit, und die 'Pausen'-Werte ändern, um den Klang jeder Stimme nach Ihren Wünschen anzupassen.\r\n" +
                "Ändern Sie den 'Paragraph gap', um die Wartezeit vor einem neuen Absatz einzustellen." },
            { "el", "Αυτή είναι μια προεπισκόπηση της επιλεγμένης φωνής, για να ακούσετε πόσο φυσικά ακούγεται.\r\n" +
                "Μπορείτε να αλλάξετε την ένταση, την ταχύτητα υπαγόρευσης, και τις τιμές 'παύσης', για να προσαρμόσετε τον ήχο κάθε φωνής όπως σας αρέσει.\r\n" +
                "Αλλάξτε το 'Paragraph gap', για να ρυθμίσετε τον χρόνο αναμονής πριν ξεκινήσει μια νέα παράγραφος." },
            { "es", "Esta es una vista previa de la voz seleccionada, para escuchar lo natural que suena.\r\n" +
                "Puede cambiar el volumen, la velocidad de dictado, y los valores de 'pausa', para ajustar el sonido de cada voz a su gusto.\r\n" +
                "Cambie el 'Paragraph gap', para ajustar el tiempo de espera antes de que empiece un nuevo párrafo." },
            { "eu", "Hau hautatutako ahotsaren aurrebista bat da, zein natural entzuten den entzuteko.\r\n" +
                "Bolumena, diktaketa-abiadura, eta 'eten' balioak alda ditzakezu, ahots bakoitzaren soinua zure gustura egokitzeko.\r\n" +
                "Aldatu 'Paragraph gap', paragrafo berri bat hasi aurreko itxaronaldia doitzeko." },
            { "fi", "Tämä on valitun äänen esikatselu, jotta kuulet, kuinka luonnolliselta se kuulostaa.\r\n" +
                "Voit muuttaa äänenvoimakkuutta, sanelunopeutta, ja 'tauko'-arvoja, säätääksesi jokaisen äänen mieleiseksesi.\r\n" +
                "Muuta 'Paragraph gap' -arvoa, säätääksesi odotusaikaa ennen uuden kappaleen alkua." },
            { "fr", "Voici un aperçu de la voix sélectionnée, pour entendre à quel point elle semble naturelle.\r\n" +
                "Vous pouvez modifier le volume, la vitesse de dictée, et les valeurs de 'pause', pour adapter le son de chaque voix à votre goût.\r\n" +
                "Modifiez le 'Paragraph gap', pour régler le temps d'attente avant le début d'un nouveau paragraphe." },
            { "gl", "Esta é unha vista previa da voz seleccionada, para escoitar o natural que soa.\r\n" +
                "Pode cambiar o volume, a velocidade de ditado, e os valores de 'pausa', para axustar o son de cada voz ao seu gusto.\r\n" +
                "Cambie o 'Paragraph gap', para axustar o tempo de espera antes de que comece un novo parágrafo." },
            { "hu", "Ez a kiválasztott hang előnézete, hogy meghallja, milyen természetesen szól.\r\n" +
                "Módosíthatja a hangerőt, a diktálási sebességet, és a 'szünet' értékeit, hogy minden hang hangzását ízlése szerint alakítsa.\r\n" +
                "Módosítsa a 'Paragraph gap' értékét, hogy beállítsa a várakozási időt egy új bekezdés kezdete előtt." },
            { "is", "Þetta er sýnishorn af valinni rödd, svo þú heyrir hversu eðlileg hún hljómar.\r\n" +
                "Þú getur breytt hljóðstyrk, upplestrarhraða, og 'hlé'-gildum, til að laga hljóm hverrar raddar að þínum smekk.\r\n" +
                "Breyttu 'Paragraph gap', til að stilla biðtímann áður en ný málsgrein hefst." },
            { "it", "Questa è un'anteprima della voce selezionata, per sentire quanto suona naturale.\r\n" +
                "Puoi modificare il volume, la velocità di dettatura, e i valori di 'pausa', per regolare il suono di ogni voce come preferisci.\r\n" +
                "Cambia il 'Paragraph gap', per regolare l'attesa prima che inizi un nuovo paragrafo." },
            { "lb", "Dëst ass eng Virschau vun der gewielter Stëmm, fir ze héieren, wéi natierlech se klénkt.\r\n" +
                "Dir kënnt de Volume, d'Diktéiergeschwindegkeet, an d'Wäerter vun der 'Paus' änneren, fir de Klang vun all Stëmm no Ärem Goût unzepassen.\r\n" +
                "Ännert de 'Paragraph gap', fir d'Waardezäit virun engem neien Abschnitt unzepassen." },
            { "lv", "Šis ir izvēlētās balss priekšskatījums, lai dzirdētu, cik dabiski tā skan.\r\n" +
                "Varat mainīt skaļumu, diktēšanas ātrumu, un 'pauzes' vērtības, lai pielāgotu katras balss skanējumu pēc savas gaumes.\r\n" +
                "Mainiet 'Paragraph gap', lai pielāgotu gaidīšanas laiku pirms jaunas rindkopas sākuma." },
            { "nl", "Dit is een voorbeeld van de geselecteerde stem, om te horen hoe natuurlijk die klinkt.\r\n" +
                "Je kunt het volume, de dicteersnelheid, en de 'pauze'-waarden wijzigen, om het geluid van elke stem naar wens aan te passen.\r\n" +
                "Wijzig de 'Paragraph gap', om de wachttijd voor een nieuwe alinea in te stellen." },
            { "no", "Dette er en forhåndsvisning av den valgte stemmen, så du kan høre hvor naturlig den høres ut.\r\n" +
                "Du kan endre volumet, dikteringshastigheten, og 'pause'-verdiene, for å tilpasse lyden til hver stemme etter ønske.\r\n" +
                "Endre 'Paragraph gap', for å justere ventetiden før et nytt avsnitt begynner." },
            { "pl", "To jest podgląd wybranego głosu, aby usłyszeć, jak naturalnie brzmi.\r\n" +
                "Możesz zmienić głośność, szybkość dyktowania, oraz wartości 'pauzy', aby dostosować brzmienie każdego głosu do swoich upodobań.\r\n" +
                "Zmień 'Paragraph gap', aby dostosować czas oczekiwania przed rozpoczęciem nowego akapitu." },
            { "pt", "Esta é uma amostra da voz selecionada, para ouvir quão natural ela soa.\r\n" +
                "Pode alterar o volume, a velocidade de ditado, e os valores de 'pausa', para ajustar o som de cada voz ao seu gosto.\r\n" +
                "Altere o 'Paragraph gap', para ajustar o tempo de espera antes do início de um novo parágrafo." },
            { "ro", "Aceasta este o previzualizare a vocii selectate, pentru a auzi cât de natural sună.\r\n" +
                "Puteți modifica volumul, viteza de dictare, și valorile de 'pauză', pentru a ajusta sunetul fiecărei voci după preferințe.\r\n" +
                "Modificați 'Paragraph gap', pentru a ajusta timpul de așteptare înainte de începerea unui paragraf nou." },
            { "ru", "Это предварительное прослушивание выбранного голоса, чтобы услышать, насколько естественно он звучит.\r\n" +
                "Вы можете изменить громкость, скорость диктовки, и значения 'пауз', чтобы настроить звучание каждого голоса по своему вкусу.\r\n" +
                "Измените 'Paragraph gap', чтобы настроить время ожидания перед началом нового абзаца." },
            { "sk", "Toto je ukážka vybraného hlasu, aby ste počuli, ako prirodzene znie.\r\n" +
                "Môžete zmeniť hlasitosť, rýchlosť diktovania, a hodnoty 'pauzy', aby ste zvuk každého hlasu upravili podľa svojich predstáv.\r\n" +
                "Zmeňte 'Paragraph gap', aby ste upravili čas čakania pred začiatkom nového odseku." },
            { "sl", "To je predogled izbranega glasu, da slišite, kako naravno zveni.\r\n" +
                "Spremenite lahko glasnost, hitrost narekovanja, in vrednosti 'premora', da zvok vsakega glasu prilagodite svojemu okusu.\r\n" +
                "Spremenite 'Paragraph gap', da prilagodite čas čakanja pred začetkom novega odstavka." },
            { "sr", "Ово је преглед изабраног гласа, да бисте чули колико природно звучи.\r\n" +
                "Можете да промените јачину звука, брзину диктирања, и вредности 'паузе', да бисте прилагодили звук сваког гласа по свом укусу.\r\n" +
                "Промените 'Paragraph gap', да бисте подесили време чекања пре почетка новог пасуса." },
            { "sv", "Det här är en förhandsvisning av den valda rösten, så att du hör hur naturlig den låter.\r\n" +
                "Du kan ändra volymen, dikteringshastigheten, och 'paus'-värdena, för att anpassa varje röst efter din smak.\r\n" +
                "Ändra 'Paragraph gap', för att justera väntetiden innan ett nytt stycke börjar." },
            { "tr", "Bu, seçilen sesin bir önizlemesidir, ne kadar doğal duyulduğunu dinlemeniz için.\r\n" +
                "Her sesi beğeninize göre ayarlamak için ses düzeyini, dikte hızını, ve 'duraklama' değerlerini değiştirebilirsiniz.\r\n" +
                "Yeni bir paragraf başlamadan önceki bekleme süresini ayarlamak için, 'Paragraph gap' değerini değiştirin." },
            { "uk", "Це попереднє прослуховування вибраного голосу, щоб почути, наскільки природно він звучить.\r\n" +
                "Ви можете змінити гучність, швидкість диктування, і значення 'пауз', щоб налаштувати звучання кожного голосу на свій смак.\r\n" +
                "Змініть 'Paragraph gap', щоб налаштувати час очікування перед початком нового абзацу." },
        };
        // A language's preview text (English for one without).
        public static string For(string family)
        {
            string text;
            return family != null && ByLanguage.TryGetValue(family, out text) ? text : ByLanguage["en"];
        }
        // Whether a text is one of these, as given (not typed over): it can be swapped for another language's.
        public static bool IsOneOfThem(string text)
        {
            string plain = Plain(text);
            if (plain.Length == 0) { return true; }
            foreach (string known in ByLanguage.Values) { if (Plain(known) == plain) { return true; } }
            // (And the English text of earlier versions.)
            return plain == Plain(App.OldPreviewText);
        }
        static string Plain(string text) { return (text ?? "").Replace("\r", "").Trim(); }
        // A voice's language from its folder's name (en_GB-alba-medium: "en").
        public static string LanguageOfKey(string key)
        {
            int mark = (key ?? "").IndexOfAny(new char[] { '_', '-' });
            return mark > 0 ? key.Substring(0, mark).ToLowerInvariant() : "en";
        }
    }

    // Which languages' voices Add or remove voices lists (kept for the person at the PC): the additional
    // languages chosen, whether English is listed, and what the "Show" box was on. A fresh install's
    // language choice (the installer's first window) sets them once.
    public static class Languages
    {
        const string OurKey = @"Software\Modern Voices for Windows";
        public static string Override;      // (the build's test: its own choices, the person's left alone)
        static bool englishInTest = true;
        static string showInTest = "";
        public const string AllVoices = "All voices", BritishOnly = "British only", AmericanOnly = "American only";

        static string Kept(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OurKey)) { return key == null ? null : key.GetValue(name) as string; }
            }
            catch (Exception) { return null; }
        }
        static void Keep(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(OurKey)) { key.SetValue(name, value); }
            }
            catch (Exception) { }
        }

        public static List<string> Enabled
        {
            get
            {
                string value = Override ?? Kept("Languages");
                List<string> list = new List<string>();
                foreach (string family in (value ?? "").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (family != "en" && !list.Contains(family)) { list.Add(family); }
                }
                return list;
            }
            set
            {
                string text = string.Join(" ", value.ToArray());
                if (Override != null) { Override = text; return; }
                Keep("Languages", text);
            }
        }

        // English's voices listed (as they are unless switched off: the installer's language choice, or the
        // Additional languages window). The window lists them all the same while no other language is.
        public static bool English
        {
            get { return Override != null ? englishInTest : Kept("English") != "0"; }
            set { if (Override != null) { englishInTest = value; } else { Keep("English", value ? "1" : "0"); } }
        }

        // What the "Show" box was on (its words), for the next start: "" for all voices.
        public static string Show
        {
            get { return Override != null ? showInTest : (Kept("Show") ?? ""); }
            set { if (Override != null) { showInTest = value ?? ""; } else { Keep("Show", value ?? ""); } }
        }

        // The installer's language choice, taken up: "en" (all English voices), "en_GB" or "en_US" (English,
        // with Show on British or American), or another language offered (that one listed and English not).
        public static bool Apply(string choice)
        {
            choice = (choice ?? "").Trim();
            if (choice == "en") { English = true; Show = ""; return true; }
            if (choice == "en_GB") { English = true; Show = BritishOnly; return true; }
            if (choice == "en_US") { English = true; Show = AmericanOnly; return true; }
            if (Array.IndexOf(Catalogue.Offered, choice) < 0) { return false; }
            List<string> enabled = Enabled;
            if (!enabled.Contains(choice)) { enabled.Add(choice); }
            Enabled = enabled;
            English = false; Show = "";
            return true;
        }

        // The language chosen when installing ("" before 0.18.1, or for an install that chose none): the
        // "select voices" note is shown in it too.
        static string installInTest = "";
        public static string InstallLanguage
        {
            get { return Override != null ? installInTest : (Kept("InstallLanguage") ?? ""); }
            set { if (Override != null) { installInTest = value ?? ""; } else { Keep("InstallLanguage", value ?? ""); } }
        }

        // The choice the installer left (a fresh install's first window), taken up the first time the
        // program opens after it: install-language.txt in the settings folder, deleted once read. (The
        // installer runs as an administrator, perhaps another account: it can't set this person's own.)
        public const string ChoiceFile = "install-language.txt";
        public static void TakeInstallChoice()
        {
            try
            {
                string file = Path.Combine(App.SettingsFolder, ChoiceFile);
                if (!File.Exists(file)) { return; }
                string stamp = File.GetLastWriteTimeUtc(file).Ticks.ToString(CultureInfo.InvariantCulture);
                string choice = File.ReadAllText(file);
                try { File.Delete(file); } catch (Exception) { }
                // (Should the file not go, it is still taken up only once.)
                if (Kept("InstallChoice") == stamp) { return; }
                Keep("InstallChoice", stamp);
                if (Apply(choice)) { InstallLanguage = choice.Trim(); }
            }
            catch (Exception) { }
        }
    }

    // How the lists were arranged last time, for the next start (kept for the Windows user, as the theme is):
    // the Voice manager's column order ("VoiceColumns": the columns' names, left to right) and each list's
    // sorting ("VoiceSort", "AddSort": a column's name and "up" or "down"; none: as listed). The build's test
    // keeps its own here and leaves the person's alone.
    public static class ListLayout
    {
        const string OurKey = @"Software\Modern Voices for Windows";
        public static Dictionary<string, string> Override;

        public static string Get(string name)
        {
            if (Override != null) { string held; return Override.TryGetValue(name, out held) ? held : ""; }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OurKey)) { return (key == null ? null : key.GetValue(name) as string) ?? ""; }
            }
            catch (Exception) { return ""; }
        }
        public static void Set(string name, string value)
        {
            if (Override != null) { Override[name] = value ?? ""; return; }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(OurKey)) { key.SetValue(name, value ?? ""); }
            }
            catch (Exception) { }
        }
    }

    // The "Powered by" window is shown the first time Modern Voices Config opens: whether it has been
    // (kept with the theme; uninstalling removes it, so a new install shows the window again).
    public static class Welcome
    {
        const string OurKey = @"Software\Modern Voices for Windows";
        public static bool Seen
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OurKey)) { return key != null && Convert.ToString(key.GetValue("Welcomed")) == "1"; }
                }
                catch (Exception) { return true; }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(OurKey)) { key.SetValue("Welcomed", value ? "1" : "0"); }
                }
                catch (Exception) { }
            }
        }
    }

    // The work this program is put together from: what each is called here, whose it is, what it does
    // here, and where it lives.
    public class Credit
    {
        public string Name, By, Does, Link;
        public Credit(string name, string by, string does, string link) { Name = name; By = by; Does = does; Link = link; }
        public static readonly Credit[] All = {
            new Credit("Spoken Players Engine", "f-rolf", "The Windows speech engine this program's engine is based on.", "https://github.com/f-rolf/spoken-players-voicepack"),
            new Credit("Piper (Neural Voices)", "Michael Hansen and contributors", "The voices, and the way of making them.", "https://github.com/rhasspy/piper"),
            new Credit("eSpeak NG", "Jonathan Duddington, Reece H. Dunn and contributors", "Works out how words are pronounced.", "https://github.com/espeak-ng/espeak-ng"),
            new Credit("sherpa-onnx", "Xiaomi Corporation and the k2-fsa contributors", "Runs the voices.", "https://github.com/k2-fsa/sherpa-onnx"),
            new Credit("ONNX Runtime", "Microsoft Corporation", "Runs the voices' models.", "https://github.com/microsoft/onnxruntime"),
            new Credit("PdfPig", "UglyToad and contributors", "Reads the text of PDFs in the Reader.", "https://github.com/UglyToad/PdfPig"),
            new Credit("LAME", "The LAME Project", "Makes the Reader's MP3 files.", "https://lame.sourceforge.io") };
        public const string Thanks = "Thanks to everyone who recorded their voice for a research collection or audiobook, and allowed it to be used for free in software such as this.";
        public const string Home = "https://github.com/Lumotiverse/modern-voices-for-windows";
        public const string Coding = "All coding done by Anthropic's Fable 5.2, assisted by Opus 5.5. Testing and UX done by Lumoti.";
        public const string FromLumoti = "I hope anyone who uses this software finds it useful. It was only made possible by the incredible effort of the " +
            "projects and people listed above, and also their generosity in publishing as open source. If you benefit from this program please show them " +
            "your love and support.\r\n\r\nCompassion, acceptance and understanding are the answer. Reject hate. Kindness is free! <3";
    }

    // A choice of the "Show" box (and of the Pronunciation tab's "Rules for"): the voices of a language
    // (Family; null: any) and of one of its countries (Code; null: any).
    public class LanguageChoice
    {
        public string Text, Family, Code;
        public LanguageChoice(string text, string family, string code) { Text = text; Family = family; Code = code; }
        public override string ToString() { return Text; }
    }

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
                p.Border = Color.FromArgb(90, 90, 90); p.Tab = Color.FromArgb(64, 64, 64); p.Selected = Color.FromArgb(0, 84, 153);
                p.TabEdge = Color.FromArgb(112, 112, 112); p.TabText = Color.FromArgb(205, 205, 205); p.TabOn = Color.FromArgb(240, 240, 240);
            }
            else
            {
                p.Back = SystemColors.Control; p.Raised = SystemColors.Control; p.Input = SystemColors.Window;
                p.Text = SystemColors.ControlText; p.Muted = Color.DimGray; p.Warn = Color.Firebrick;
                p.Border = SystemColors.ControlDark; p.Tab = Color.FromArgb(222, 222, 222); p.Selected = SystemColors.Highlight;
                p.TabEdge = Color.FromArgb(172, 172, 172); p.TabText = Color.FromArgb(64, 64, 64); p.TabOn = Color.FromArgb(96, 96, 96);
            }
            return p;
        }
    }

    // ---------------------------------------------------------------- the window

    // The tick boxes of the lists, drawn by the program so that they can be larger than Windows' own:
    // a white box with a grey edge; ticked, blue with a white tick.
    public static class Boxes
    {
        public static void Draw(Graphics g, Rectangle r, bool ticked)
        {
            System.Drawing.Drawing2D.SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            Rectangle edge = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (SolidBrush fill = new SolidBrush(ticked ? Color.FromArgb(0, 103, 192) : Color.White)) { g.FillRectangle(fill, edge); }
            using (Pen line = new Pen(ticked ? Color.FromArgb(0, 84, 153) : Color.FromArgb(96, 96, 96))) { g.DrawRectangle(line, edge); }
            if (ticked)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float w = r.Width, h = r.Height;
                using (Pen tick = new Pen(Color.White, Math.Max(1.6f, w / 8f)))
                {
                    tick.StartCap = System.Drawing.Drawing2D.LineCap.Round; tick.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    tick.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawLines(tick, new PointF[] { new PointF(r.X + w * 0.24f, r.Y + h * 0.52f), new PointF(r.X + w * 0.42f, r.Y + h * 0.70f),
                        new PointF(r.X + w * 0.76f, r.Y + h * 0.30f) });
                }
            }
            g.SmoothingMode = was;
        }
        // Not ticked (0) and ticked (1), for a list with tick boxes.
        public static ImageList Images(int size)
        {
            ImageList images = new ImageList();
            images.ColorDepth = ColorDepth.Depth32Bit;
            images.ImageSize = new Size(size, size);
            for (int i = 0; i < 2; i++)
            {
                Bitmap picture = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(picture)) { g.Clear(Color.Transparent); Draw(g, new Rectangle(0, 0, size, size), i == 1); }
                images.Images.Add(picture);
            }
            return images;
        }
    }

    // The speakers' list: larger words and tick boxes than Windows gives a list of this kind (it draws its own rows).
    public class BigCheckList : CheckedListBox
    {
        public int BoxSize = 16, RowGap = 6;
        public Color SelectedBack = SystemColors.Highlight, SelectedText = SystemColors.HighlightText;
        public override int ItemHeight { get { return Math.Max(Font.Height, BoxSize) + RowGap; } set { } }
        // Where a row's words start, from its left edge.
        public int TextLeft { get { return 3 + BoxSize + 5; } }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) { using (SolidBrush fill = new SolidBrush(BackColor)) { e.Graphics.FillRectangle(fill, e.Bounds); } return; }
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush fill = new SolidBrush(selected ? SelectedBack : BackColor)) { e.Graphics.FillRectangle(fill, e.Bounds); }
            Rectangle box = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + (e.Bounds.Height - BoxSize) / 2, BoxSize, BoxSize);
            Boxes.Draw(e.Graphics, box, GetItemChecked(e.Index));
            Rectangle words = new Rectangle(e.Bounds.X + TextLeft, e.Bounds.Y, Math.Max(0, e.Bounds.Width - TextLeft - 2), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(Items[e.Index]), Font, words, selected ? SelectedText : ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public class Slider
    {
        public TrackBar Bar;
        public Label Name, Value, Example;
    }

    // A row of the Voice manager's list: a voice (model) or one of its installed speakers.
    public class InstalledRow
    {
        public string Key, Dir, Title, Speaker;
        public bool Shared;
        public Entry Entry;
        // What its cells show (and are sorted by): the voice, its nickname, accent and gender, its size, and
        // whether it is Windows' default. Order: its place in the list as installed (the order with no
        // sorting); First: the voice's first row (a voice of several speakers shows its name, size and
        // folder on that row only, while its rows stand together).
        public string Voice, Nick, Accent, Gender;
        public long SizeMb;
        public bool IsDefault, First;
        public int Order;
    }

    // Sorts a list by a column (Column -1: as listed), A to Z or (Down) Z to A, by what each row's Key gives
    // for that column: words, or numbers where the column holds sizes and counts. Rows that come out the
    // same keep the order they are listed in.
    public class RowSorter : System.Collections.IComparer
    {
        public delegate IComparable KeyOf(ListViewItem item, int column);
        public delegate int OrderOf(ListViewItem item);
        public int Column = -1;
        public bool Down;
        public KeyOf Key;
        public OrderOf Order;
        public string[] Names;      // the columns' names (what is kept for the next start)

        public int Compare(object x, object y)
        {
            ListViewItem a = (ListViewItem)x, b = (ListViewItem)y;
            int c = 0;
            if (Column >= 0)
            {
                IComparable ka = Key(a, Column), kb = Key(b, Column);
                string sa = ka as string, sb = kb as string;
                if (sa != null && sb != null) { c = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase); }
                else if (ka != null && kb != null) { c = ka.CompareTo(kb); }
                if (Down) { c = -c; }
            }
            if (c == 0) { c = Order(a).CompareTo(Order(b)); }
            return c;
        }

        // "Accent up": the column's name and which way; "" for as listed.
        public string Saved
        {
            get { return Column < 0 || Column >= Names.Length ? "" : Names[Column] + "|" + (Down ? "down" : "up"); }
            set
            {
                Column = -1; Down = false;
                string[] parts = (value ?? "").Split('|');
                if (parts.Length != 2) { return; }
                Column = Array.IndexOf(Names, parts[0]);
                Down = parts[1] == "down";
            }
        }
    }

    // The look of a right-click menu when the window is dark (light: Windows' own).
    public class DarkMenuColours : ProfessionalColorTable
    {
        readonly Palette p;
        public DarkMenuColours(Palette palette) { p = palette; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return p.Raised; } }
        public override Color ImageMarginGradientBegin { get { return p.Raised; } }
        public override Color ImageMarginGradientMiddle { get { return p.Raised; } }
        public override Color ImageMarginGradientEnd { get { return p.Raised; } }
        public override Color MenuBorder { get { return p.Border; } }
        public override Color MenuItemBorder { get { return p.Selected; } }
        public override Color MenuItemSelected { get { return p.Selected; } }
        public override Color MenuItemSelectedGradientBegin { get { return p.Selected; } }
        public override Color MenuItemSelectedGradientEnd { get { return p.Selected; } }
        public override Color MenuItemPressedGradientBegin { get { return p.Selected; } }
        public override Color MenuItemPressedGradientEnd { get { return p.Selected; } }
        public override Color CheckBackground { get { return p.Raised; } }
        public override Color CheckSelectedBackground { get { return p.Selected; } }
        public override Color CheckPressedBackground { get { return p.Selected; } }
        public override Color SeparatorDark { get { return p.Border; } }
        public override Color SeparatorLight { get { return p.Border; } }
    }
    public class DarkMenuLook : ToolStripProfessionalRenderer
    {
        readonly Palette p;
        public DarkMenuLook(Palette palette) : base(new DarkMenuColours(palette)) { p = palette; RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? p.Text : p.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = p.Text;
            base.OnRenderArrow(e);
        }
        // A tick drawn in the text's colour (Windows' own is black, unseen on a dark menu).
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            System.Drawing.Drawing2D.SmoothingMode was = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float w = r.Width, h = r.Height;
            using (Pen tick = new Pen(e.Item.Enabled ? p.Text : p.Muted, Math.Max(1.6f, w / 9f)))
            {
                tick.StartCap = System.Drawing.Drawing2D.LineCap.Round; tick.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                tick.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                e.Graphics.DrawLines(tick, new PointF[] { new PointF(r.X + w * 0.22f, r.Y + h * 0.52f), new PointF(r.X + w * 0.42f, r.Y + h * 0.72f),
                    new PointF(r.X + w * 0.78f, r.Y + h * 0.30f) });
            }
            e.Graphics.SmoothingMode = was;
        }
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
        bool tickByHand;      // (a voice's box in Add or remove voices clicked, or Space pressed on it, just now)
        float scale = 1f;
        Font boldFont, listFont, listBold, speakerFont;
        Panel headerLine;
        public Button ReaderButton, PlayButton;
        public Label MuteIcon, AboutButton;
        public Label SavedNote;
        public FlowLayoutPanel DefaultBox;      // Windows' default voice, in its box
        public TableLayoutPanel SoundFrame, PauseFrame, GlobalGrid, AdvancedGrid;      // the framed sections, and the Speed tab's sliders
        public TableLayoutPanel OutLine;
        public int NameInset;
        int labelColumn, valueColumn, previewColumn;      // the sliders' columns, the same on both tabs
        public Slider OwnVariation;          // the selected voice's own variation (with its own pauses)
        // Each installed voice's language, by the name programs see (for the preview text in its language).
        readonly Dictionary<string, string> languageOfVoice = new Dictionary<string, string>();
        int unmutedVolume = 100;            // the output volume to go back to when unmuted
        Palette palette;
        bool? shownDark;
        public int PageIndex;
        Process previewProcess;
        string talking;                                     // what a Preview is saying now: the voice's name ("hear:" + it for Hear it)
        public static string SpeakHow = "--speak";          // (the build's test has it speak to nowhere)
        readonly object applyLock = new object();
        List<string> applyHad = new List<string>();         // the voices installed when Apply changes was pressed
        string applyDefault = "";                           // and Windows' default voice (its token) then
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
        // The Voice manager list's columns (their places as made; the person may drag them into another order).
        public const int VoiceColumn = 0, SpeakerColumn = 1, NickColumn = 2, AccentColumn = 3, GenderColumn = 4, SizeColumn = 5, DefaultColumn = 6, FolderColumn = 7;
        public RowSorter InstalledSorter, AddSorter;
        readonly Dictionary<ListView, RowSorter> sorters = new Dictionary<ListView, RowSorter>();
        readonly Dictionary<ColumnHeader, int> columnWidths = new Dictionary<ColumnHeader, int>();      // as made (a column no longer last goes back to it)
        readonly Dictionary<ListView, ColumnHeader> filledColumn = new Dictionary<ListView, ColumnHeader>();
        readonly HashSet<ListView> firstMade = new HashSet<ListView>();
        readonly Dictionary<ListViewItem, int> addOrder = new Dictionary<ListViewItem, int>();          // Add or remove voices' rows as listed
        // The Voice manager list's right-click menu.
        public ContextMenuStrip VoiceMenu;
        public ToolStripMenuItem MenuNickname, MenuGender, MenuFemale, MenuMale, MenuOverride, MenuDefault;
        public BigCheckList SpeakerList;
        public TextBox NickBox, PreviewBox, WordFrom, WordTo;
        public CheckBox OwnBox, UsBox, WordWhole, WordCase, OnlyTickedBox;
        // Every voice of the catalogue as a row, shown or not: the list shows those the "Show" box lets
        // through, and a row keeps its tick while it is not shown. What is ticked is always read from here.
        public readonly List<ListViewItem> VoiceItems = new List<ListViewItem>();
        public ComboBox VoiceFilterBox;
        bool filtering;                                     // the list is being refilled for the "Show" box
        public Button LanguagesButton;
        public Label QualityNote;      // (the voices vary in quality: hear them first)
        public ComboBox WordLanguageBox;                    // whose rules the Pronunciation tab lists
        public CheckBox WordListOnBox;                      // that list in use, or switched off as a whole
        string wordFamily = "en";
        public Button DoneButton;
        public ComboBox PreviewVoiceBox;
        public TrackBar SpeedBar;
        public TrackBar VariationBar;       // Speed and pauses, Advanced: how much Piper varies each saying
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
        // A slider's own line sits near the top of its control, above the middle of the words beside it.
        // Lowered: the control is shorter by as much as it is moved down, so its row is as tall as before
        // and the words stay where they were, with the slider's line level with their middle.
        void Lower(TrackBar bar)
        {
            int drop = Px(13);
            bar.AutoSize = false;
            bar.Height = Math.Max(Px(24), bar.PreferredSize.Height - drop);
            bar.Margin = new Padding(0, drop, 0, 0);
        }
        Slider AddSlider(TableLayoutPanel grid, string title, int min, int max, int small, int large, int tick, bool lowered = false)
        {
            int row = GridRow(grid);
            Label label = Beside(title);
            TrackBar bar = new TrackBar();
            bar.Maximum = max; bar.Minimum = min; bar.SmallChange = small; bar.LargeChange = large; bar.TickFrequency = tick;
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0);
            if (lowered) { Lower(bar); }
            // The mouse wheel over a slider scrolls the tab, not the slider (it moves by dragging, clicking or the arrow keys).
            bar.MouseWheel += SliderWheel;
            Label value = Beside("");
            value.MinimumSize = new Size(Px(150), 0);
            grid.Controls.Add(label, 0, row); grid.Controls.Add(bar, 1, row); grid.Controls.Add(value, 2, row);
            Slider s = new Slider();
            s.Bar = bar; s.Value = value; s.Name = label;
            return s;
        }
        static void AddNote(TableLayoutPanel grid, Control note)
        {
            int row = GridRow(grid);
            grid.Controls.Add(note, 1, row);
        }
        // A slider's description, shown when the pointer rests on the slider or on its name.
        void Describe(Slider slider, string text)
        {
            tips.SetToolTip(slider.Bar, text);
            tips.SetToolTip(slider.Name, text);
        }
        // A section with a line drawn around it: its rows, top to bottom.
        TableLayoutPanel Frame()
        {
            TableLayoutPanel frame = new TableLayoutPanel();
            frame.ColumnCount = 1;
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            frame.AutoSize = true; frame.AutoSizeMode = AutoSizeMode.GrowAndShrink; frame.Dock = DockStyle.Fill;
            frame.Padding = new Padding(FramePad, Px(8), FramePad, Px(10));      // (FramePad: see there)
            frame.Tag = "frame";
            frame.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (palette == null) { return; }
                using (Pen edge = new Pen(palette.Border)) { e.Graphics.DrawRectangle(edge, 0, 0, frame.Width - 1, frame.Height - 1); }
            };
            frame.Resize += delegate { frame.Invalidate(); };
            return frame;
        }
        // A frame's line is FrameGap from the words inside it: from the sliders' names on its left, and from
        // the end of the widest value (ValueFit) on its right. Inside the line, FramePad: the gap less the
        // label's own margin and the room before its words.
        public int FrameGap { get { return 2 * (Px(10) + 3 + NameInset); } }
        int FramePad { get { return FrameGap - 3 - NameInset; } }
        // The value column of a framed section: the widest value its sliders show (a value wider than that,
        // the Volume's "off (as Piper made it)", goes onto a second line).
        public static readonly string[] ValueSamples = { "100%", "150%", "200%", "4.0x", "none", "+3000 ms", "none added", "none (as said)" };
        public string WidestValue
        {
            get
            {
                string widest = "";
                foreach (string sample in ValueSamples) { if (TextRenderer.MeasureText(sample, Font).Width > TextRenderer.MeasureText(widest, Font).Width) { widest = sample; } }
                return widest;
            }
        }
        // (A value label as wide as the widest value needs, and its margins.)
        int ValueLabelWidth
        {
            get { using (Label sample = new Label()) { sample.AutoSize = true; sample.Font = Font; sample.UseMnemonic = false; sample.Text = WidestValue; return sample.PreferredSize.Width; } }
        }
        int ValueFit { get { return 6 + ValueLabelWidth; } }
        // Inside the frame's right line: what is left of FrameGap after the widest value's words end (its
        // label's right margin, and the room after its words inside the label).
        int FramePadRight
        {
            get
            {
                int words = TextRenderer.MeasureText(WidestValue, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                return Math.Max(0, FrameGap - 3 - (ValueLabelWidth - NameInset - words));
            }
        }
        // A framed section of sliders: its columns, and its place (the sliders end where the output volume
        // slider does, so the frame stops FramePad after the value column, and its line FrameGap after the
        // widest value's end).
        void FrameSliders(TableLayoutPanel frame, TableLayoutPanel grid, int sectionLeft, int top)
        {
            grid.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, labelColumn);
            grid.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, ValueFit);
            foreach (Control c in grid.Controls)
            {
                if (grid.GetColumn(c) == 2 && c is Label) { c.MinimumSize = Size.Empty; c.MaximumSize = new Size(ValueLabelWidth, 0); }
            }
            // The frame's line is in the middle of the page (as far from its left edge as from its right),
            // while what is inside it stays where it was: the line moves by 'shift', the room inside it the
            // other way. (His ask in 0.18.4; until then the line was FrameGap from the words on both sides.)
            int left = sectionLeft - FramePad, right = Math.Max(0, valueColumn - FramePadRight - ValueFit);
            int margin = (left + right) / 2, shift = left - margin;
            frame.Padding = new Padding(FramePad + shift, frame.Padding.Top, Math.Max(0, FramePadRight - shift), frame.Padding.Bottom);
            frame.Margin = new Padding(margin, top, margin, 0);
        }
        // The sliders of both tabs stand in one column, in the middle of the window: their names start where
        // the Voice manager's preview box starts, and the sliders end where its output volume slider ends.
        // A grid of sliders is given those columns ('inset': what its section's frame already takes up).
        void Centre(TableLayoutPanel grid, int inset)
        {
            grid.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, labelColumn);
            grid.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, Math.Max(Px(40), valueColumn - inset));
            foreach (Control c in grid.Controls)
            {
                if (grid.GetColumn(c) == 2 && c is Label) { c.MinimumSize = Size.Empty; }
            }
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
            v.Font = listFont;
            if (boxes) { v.StateImageList = Boxes.Images(Px(16)); }
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
            // The lists' words are a size larger than the rest (and their tick boxes: see Boxes); the speakers' larger still.
            listFont = new Font("Segoe UI", 10f);
            listBold = new Font(listFont, FontStyle.Bold);
            speakerFont = new Font("Segoe UI", 12f);
            tips.AutoPopDelay = 20000;

            // The tabs: a button each and a page each (drawn by the program, so that they can be dark too).
            pageHost = new Panel();
            pageHost.Dock = DockStyle.Fill;
            Controls.Add(pageHost);
            // The strip across the top: the tabs' buttons at the left and, at the far right, the moon.
            // Under it, a gap (as there was between the tabs and the first list): together a header. The
            // pages, and their scroll bars, start below it. (0.12.6 drew a line at its foot; taken out in 0.12.7.)
            headerLine = new Panel();
            headerLine.Dock = DockStyle.Top; headerLine.Height = Math.Max(1, Px(1));     // (no longer drawn: it keeps the header's spacing)
            Controls.Add(headerLine);
            tabStrip = Line("auto", "fill", "auto", "auto");       // tabs, a stretch, About, the moon
            tabStrip.Dock = DockStyle.Top;
            tabStrip.Padding = new Padding(0, 0, 0, Px(6) + 3 - headerLine.Height);
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
                tabButton.FlatStyle = FlatStyle.Flat; tabButton.FlatAppearance.BorderSize = 1; tabButton.TabStop = false;
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
            // About, left of the moon.
            AboutButton = Words("About", true);
            AboutButton.Dock = DockStyle.None; AboutButton.Anchor = AnchorStyles.Right; AboutButton.Cursor = Cursors.Hand;
            AboutButton.Margin = new Padding(Px(4), 0, Px(8), 0);
            if (moon.Name == "Segoe MDL2 Assets") { AboutButton.Font = moon; AboutButton.Text = "\uE946"; }      // an i in a ring
            tips.SetToolTip(AboutButton, "About Modern Voices, and the projects it is made from");
            AboutButton.Click += delegate { using (Form about = AboutWindow()) { about.ShowDialog(this); } };
            tabStrip.Controls.Add(ThemeButton, 3, 0);
            tabStrip.Controls.Add(AboutButton, 2, 0);
            // (The window is never narrower than its header needs, the selected tab's bold words included, and
            // a gap before the moon.)
            int headerNeeds = Px(24) + ThemeButton.PreferredSize.Width + ThemeButton.Margin.Horizontal + AboutButton.PreferredSize.Width + AboutButton.Margin.Horizontal;
            foreach (Button tabButton in TabButtons)
            {
                Font plain = tabButton.Font;
                tabButton.Font = boldFont;
                headerNeeds += tabButton.PreferredSize.Width + tabButton.Margin.Horizontal;
                tabButton.Font = plain;
            }
            headerNeeds += Padding.Horizontal + (Width - ClientSize.Width) + Px(4);
            MinimumSize = new Size(Math.Min(Math.Max(MinimumSize.Width, headerNeeds), area.Width), MinimumSize.Height);

            // The bottom of the window: progress while applying changes, what is happening; Apply changes and
            // Done (on Add or remove voices only), Open Reader and Close.
            TableLayoutPanel bottom = Line("fill", "auto", "auto", "auto", "auto");
            bottom.Dock = DockStyle.Bottom;
            bottom.RowStyles.Clear();
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(20)));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowCount = 2;
            Progress = new ProgressBar();
            Progress.Dock = DockStyle.Fill; Progress.Visible = false;
            Progress.Margin = new Padding(3, Px(6), 3, 0);
            bottom.Controls.Add(Progress, 0, 0);
            bottom.SetColumnSpan(Progress, 5);
            StatusLabel = Words("", true);
            StatusLabel.MinimumSize = new Size(0, Px(34));
            ApplyButton = Btn("Apply changes");
            CloseButton = Btn("Close");
            // Done (on Add or remove voices only): back to the Voice manager.
            DoneButton = Btn("Done");
            DoneButton.Visible = false;
            DoneButton.Click += delegate { if (!Busy) { ShowPage(0); } };
            bottom.Controls.Add(StatusLabel, 0, 1); bottom.Controls.Add(ApplyButton, 1, 1); bottom.Controls.Add(DoneButton, 2, 1);
            ReaderButton = Btn("Open Reader");
            tips.SetToolTip(ReaderButton, "Opens Modern Voices: Reader");
            ReaderButton.Click += delegate { OpenReader(); };
            bottom.Controls.Add(ReaderButton, 3, 1);
            bottom.Controls.Add(CloseButton, 4, 1);
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
                foreach (ColumnHeader column in view.Columns) { columnWidths[column] = column.Width; }
                FillLast(view);
                view.Resize += ListResized;
            }
            // (The sort arrow in a heading, once Windows has made the list.)
            // (A list sorted before Windows made it is sorted again once it is made: after, not while Windows
            // makes it, when its rows are only half there. The Voice manager's first row is then the one
            // selected, as it would be.)
            foreach (ListView view in new ListView[] { InstalledList, AddList })
            {
                view.HandleCreated += delegate(object made, EventArgs e)
                {
                    ListView list = (ListView)made;
                    try { BeginInvoke((MethodInvoker)delegate { ListMade(list); }); } catch (Exception) { }
                };
            }
            ApplyTheme();
            // Windows' speed can be changed in its Speech settings, and the theme in the Reader, while the
            // window is open: looked at again whenever it comes to the front.
            Activated += delegate { UpdateWindowsRate(); UpdateOutput(); ApplyTheme(); };
            ShowPage(InstalledList.Items.Count == 0 ? 3 : 0);
        }

        void ListMade(ListView list)
        {
            if (list.IsDisposed || !list.IsHandleCreated) { return; }
            RowSorter sorter = sorters[list];
            if (sorter.Column >= 0)
            {
                bool was = filtering;
                filtering = true;
                try { list.Sort(); } finally { filtering = was; }
                if (list == InstalledList) { ShowInstalledCells(); }
                if (list == InstalledList && firstMade.Add(list) && list.Items.Count > 0 && list.Items[0] != null && !list.Items[0].Selected)
                {
                    SelectInstalledId(null);
                }
            }
            ShowSortArrow(list);
        }

        void ListResized(object sender, EventArgs e)
        {
            ListView resized = sender as ListView;
            if (resized != null && resized.Columns.Count > 0) { FillLast(resized); }
        }

        // The columns as they stand, left to right.
        static List<ColumnHeader> ColumnOrder(ListView view)
        {
            List<ColumnHeader> order = new List<ColumnHeader>();
            foreach (ColumnHeader column in view.Columns) { order.Add(column); }
            order.Sort(delegate(ColumnHeader a, ColumnHeader b) { return a.DisplayIndex.CompareTo(b.DisplayIndex); });
            return order;
        }
        // The column at the right takes the width left over; one that was there before and has been dragged
        // elsewhere gets its own width back.
        void FillLast(ListView view)
        {
            List<ColumnHeader> order = ColumnOrder(view);
            if (order.Count == 0) { return; }
            ColumnHeader last = order[order.Count - 1], before;
            int width;
            if (filledColumn.TryGetValue(view, out before) && before != last && columnWidths.TryGetValue(before, out width)) { before.Width = width; }
            filledColumn[view] = last;
            last.Width = -2;
        }
        // The Voice manager's columns in the order they were left in last time.
        public void RestoreColumnOrder()
        {
            string[] names = ListLayout.Get("VoiceColumns").Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            int place = 0;
            foreach (string name in names)
            {
                foreach (ColumnHeader column in InstalledList.Columns)
                {
                    if (column.Text == name) { column.DisplayIndex = place++; break; }
                }
            }
        }

        // The Voice manager list sorted by a column (that column again: the other way), kept for next time.
        public void SortInstalled(int column)
        {
            if (InstalledSorter.Column == column) { InstalledSorter.Down = !InstalledSorter.Down; }
            else { InstalledSorter.Column = column; InstalledSorter.Down = false; }
            ListLayout.Set("VoiceSort", InstalledSorter.Saved);
            string id = SelectedInstalledId();
            InstalledList.BeginUpdate();
            try { InstalledList.Sort(); ShowInstalledCells(); }
            finally { InstalledList.EndUpdate(); }
            ShowSortArrow(InstalledList);
            SelectInstalledId(id);
        }
        // Both lists as listed, unsorted (the build's test puts them back so).
        public void SortInstalledAsListed()
        {
            InstalledSorter.Column = -1; InstalledSorter.Down = false;
            ListLayout.Set("VoiceSort", "");
            string id = SelectedInstalledId();
            InstalledList.BeginUpdate();
            try { InstalledList.Sort(); ShowInstalledCells(); }
            finally { InstalledList.EndUpdate(); }
            ShowSortArrow(InstalledList);
            SelectInstalledId(id);
        }
        public void SortAddAsListed()
        {
            AddSorter.Column = -1; AddSorter.Down = false;
            ResortAdd();
        }
        public void FillLastColumn(ListView view) { FillLast(view); }

        // Add or remove voices' list sorted the same way (its "Show" box and ticks are as they were).
        public void SortAdd(int column)
        {
            if (AddSorter.Column == column) { AddSorter.Down = !AddSorter.Down; }
            else { AddSorter.Column = column; AddSorter.Down = false; }
            ResortAdd();
        }
        void ResortAdd()
        {
            ListLayout.Set("AddSort", AddSorter.Saved);
            ListViewItem selected = AddList.SelectedItems.Count > 0 ? AddList.SelectedItems[0] : null;
            Dictionary<ListViewItem, bool> ticks = new Dictionary<ListViewItem, bool>();
            foreach (ListViewItem item in VoiceItems) { ticks[item] = item.Checked; }
            filtering = true;
            AddList.BeginUpdate();
            try
            {
                AddList.Sort();
                foreach (ListViewItem item in VoiceItems) { if (item.Checked != ticks[item]) { item.Checked = ticks[item]; } }
            }
            finally { AddList.EndUpdate(); filtering = false; }
            if (selected != null && selected.ListView == AddList) { selected.EnsureVisible(); }
            ShowSortArrow(AddList);
        }
        void ShowSortArrow(ListView view)
        {
            RowSorter sorter;
            if (!sorters.TryGetValue(view, out sorter)) { return; }
            try { Native.SortArrows(view, sorter.Column, sorter.Down); } catch (Exception) { }
            if (view.IsHandleCreated && view.OwnerDraw) { view.Invalidate(); }
        }

        // What the Voice manager list sorts each column by.
        static IComparable InstalledKey(ListViewItem item, int column)
        {
            InstalledRow row = (InstalledRow)item.Tag;
            switch (column)
            {
                case VoiceColumn: return row.Voice;
                case SpeakerColumn: return row.Speaker ?? "";
                case NickColumn: return row.Nick;
                case AccentColumn: return row.Accent;
                case GenderColumn: return row.Gender;
                case SizeColumn: return row.SizeMb;
                case DefaultColumn: return row.IsDefault ? 0 : 1;      // (the default first)
                default: return row.Key;
            }
        }
        // And Add or remove voices' list.
        static IComparable AddKey(ListViewItem item, int column)
        {
            Entry v = (Entry)item.Tag;
            switch (column)
            {
                case 0: return v.Display;
                case 1: return Math.Max(1, v.SpeakerNames.Count);
                case 2: return v.Accent;
                case 3: return v.Quality == "high" ? 1 : 0;      // (medium, then high)
                case 4: return v.SizeMb;
                default: return v.Key;
            }
        }

        // The Voice manager's cells. A voice of several speakers shows its name, size and folder on its first
        // row only while its rows stand together (as installed, or sorted by voice); sorted by anything
        // else, on every row, so that each row says whose speaker it is.
        void ShowInstalledCells()
        {
            bool grouped = InstalledSorter.Column < 0 || InstalledSorter.Column == VoiceColumn;
            foreach (ListViewItem item in InstalledList.Items)
            {
                InstalledRow row = (InstalledRow)item.Tag;
                bool all = row.First || !grouped;
                SetCell(item, VoiceColumn, all ? row.Voice : "");
                SetCell(item, SizeColumn, all ? row.SizeMb + " MB" : "");
                SetCell(item, FolderColumn, all ? row.Key : "");
            }
        }
        static void SetCell(ListViewItem item, int column, string text)
        {
            if (item.SubItems[column].Text != text) { item.SubItems[column].Text = text; }
        }

        // ---- the Voice manager list's right-click menu

        void BuildVoiceMenu()
        {
            VoiceMenu = new ContextMenuStrip();
            MenuNickname = new ToolStripMenuItem("Set nickname");
            MenuGender = new ToolStripMenuItem("Set gender");
            MenuFemale = new ToolStripMenuItem("Female");
            MenuMale = new ToolStripMenuItem("Male");
            MenuGender.DropDownItems.Add(MenuFemale); MenuGender.DropDownItems.Add(MenuMale);
            MenuGender.ToolTipText = "Click the ticked one again to leave both blank.";
            MenuOverride = new ToolStripMenuItem("Override defaults");
            MenuOverride.ToolTipText = "Override pause duration and variation for the selected voice";
            MenuDefault = new ToolStripMenuItem("Make default voice");
            VoiceMenu.Items.Add(MenuNickname); VoiceMenu.Items.Add(MenuGender); VoiceMenu.Items.Add(MenuOverride); VoiceMenu.Items.Add(MenuDefault);
            VoiceMenu.ShowItemToolTips = true;
            MenuGender.DropDown.ShowItemToolTips = true;
            InstalledList.ContextMenuStrip = VoiceMenu;
            // A right click selects the row under the pointer (Windows does), and the menu is its voice's.
            VoiceMenu.Opening += delegate(object sender, CancelEventArgs e)
            {
                if (Busy || InstalledList.SelectedItems.Count == 0) { e.Cancel = true; return; }
                if (CurrentInstalled != InstalledList.SelectedItems[0].Tag) { UpdateInstalled(); }
                UpdateVoiceMenu();
            };
            MenuNickname.Click += delegate
            {
                // (After the menu has closed: it gives the focus back to the list as it goes.)
                BeginInvoke((MethodInvoker)delegate { if (NickBox.Enabled) { NickBox.Focus(); NickBox.SelectAll(); } });
            };
            MenuFemale.Click += delegate { SetGender(MenuFemale.Checked ? "" : "Female"); };
            MenuMale.Click += delegate { SetGender(MenuMale.Checked ? "" : "Male"); };
            MenuOverride.Click += delegate
            {
                if (!OwnBox.Enabled) { return; }
                OwnBox.Checked = !OwnBox.Checked;
                if (OwnBox.Checked) { BeginInvoke((MethodInvoker)delegate { Pages[0].ScrollControlIntoView(PauseFrame); }); }
            };
            MenuDefault.Click += delegate { SetDefaultVoice(); };
        }
        // The menu's ticks: the selected voice's gender, override and whether it is the default.
        public void UpdateVoiceMenu()
        {
            InstalledRow row = CurrentInstalled;
            bool on = row != null;
            CurrentVoice voice = GetCurrentVoice();
            string gender = voice != null ? Voices.Gender(voice.Id) : "";
            MenuNickname.Enabled = on && row.Entry != null;
            MenuGender.Enabled = on;
            MenuFemale.Checked = gender == "Female"; MenuMale.Checked = gender == "Male";
            MenuOverride.Enabled = on && OwnBox.Enabled;
            MenuOverride.Checked = on && OwnBox.Checked;
            bool isDefault = voice != null && string.Equals(Voices.DefaultToken, voice.Token, StringComparison.OrdinalIgnoreCase);
            MenuDefault.Checked = isDefault;
            MenuDefault.Enabled = on && !isDefault;
        }
        void ThemeMenu()
        {
            if (VoiceMenu == null) { return; }
            ToolStripRenderer look = palette.Dark ? (ToolStripRenderer)new DarkMenuLook(palette) : new ToolStripProfessionalRenderer();
            VoiceMenu.Renderer = look;
            MenuGender.DropDown.Renderer = look;
            VoiceMenu.Font = Font;
            MenuGender.DropDown.Font = Font;
        }

        // Gives the selected row's voice a gender where Windows lists it: "Female", "Male" or "" (neither).
        // Programs that choose a voice by gender go by it. The administrator's to do (see Elevate).
        public void SetGender(string gender)
        {
            CurrentVoice voice = GetCurrentVoice();
            if (voice == null) { return; }
            if (Voices.Gender(voice.Id) == gender) { return; }
            if (!Elevate()) { return; }
            try
            {
                Answer answer = Elevated.Ask("gender " + B64.To(voice.Id) + " " + B64.To(gender), null);
                if (answer.Code != 0) { throw new InvalidOperationException(answer.Message ?? "it was refused."); }
            }
            catch (Exception e) { MessageBox.Show(this, "Could not set the voice's gender: " + e.Message, App.Name); return; }
            UpdateInstalledList();
            SelectInstalledId(voice.Id);
            StatusLabel.Text = voice.Voice + (gender.Length > 0 ? " is now " + gender.ToLowerInvariant() + " to Windows." : " has no gender given to Windows now.") +
                " Restart programs that were open to see it.";
        }

        void BuildVoiceManager(Panel tab)
        {
            TableLayoutPanel page = Rows(tab);
            // (The list's top is the page's top, right under the header's line, where the page's scroll bar starts.)
            page.Padding = new Padding(Px(6), 0, Px(6), Px(6));
            InstalledList = NewList(false);
            InstalledList.Margin = new Padding(3, 0, 3, 3);
            InstalledList.Height = Px(240);
            InstalledList.MinimumSize = new Size(0, Px(240));
            InstalledList.Columns.Add("Voice", Px(160));
            InstalledList.Columns.Add("Speaker", Px(80));
            InstalledList.Columns[SpeakerColumn].TextAlign = HorizontalAlignment.Center;
            InstalledList.Columns.Add("Nickname", Px(150));
            InstalledList.Columns.Add("Accent", Px(110));
            InstalledList.Columns.Add("Gender", Px(75));
            InstalledList.Columns.Add("On disk", Px(75));
            InstalledList.Columns.Add("Windows default", Px(125));
            InstalledList.Columns.Add("Folder", Px(170));
            Row(page, InstalledList);
            // Its columns can be dragged into another order (kept for next time), and it sorts by the column
            // whose heading is clicked (clicked again, the other way; kept too).
            InstalledList.AllowColumnReorder = true;
            InstalledSorter = new RowSorter();
            InstalledSorter.Names = new string[] { "Voice", "Speaker", "Nickname", "Accent", "Gender", "On disk", "Windows default", "Folder" };
            InstalledSorter.Key = InstalledKey;
            InstalledSorter.Order = delegate(ListViewItem item) { return ((InstalledRow)item.Tag).Order; };
            InstalledSorter.Saved = ListLayout.Get("VoiceSort");
            sorters[InstalledList] = InstalledSorter;
            InstalledList.ListViewItemSorter = InstalledSorter;
            RestoreColumnOrder();
            InstalledList.ColumnClick += delegate(object sender, ColumnClickEventArgs e) { SortInstalled(e.Column); };
            InstalledList.ColumnReordered += delegate(object sender, ColumnReorderedEventArgs e)
            {
                // (Raised before the columns move: the new order is worked out from where this one goes.)
                List<ColumnHeader> order = ColumnOrder(InstalledList);
                order.Remove(e.Header);
                order.Insert(Math.Max(0, Math.Min(order.Count, e.NewDisplayIndex)), e.Header);
                List<string> names = new List<string>();
                foreach (ColumnHeader column in order) { names.Add(column.Text); }
                ListLayout.Set("VoiceColumns", string.Join("|", names.ToArray()));
                BeginInvoke((MethodInvoker)delegate { FillLast(InstalledList); });
            };
            BuildVoiceMenu();

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
            string nickTip = "Give the selected voice a name of your own, shown in brackets instead of its accent:\r\n" +
                "VCTK p262 (British) becomes VCTK p262 (Irish Female).\r\n" +
                "Leave the box empty and press Set nickname to remove it.\r\n" +
                "Windows asks for permission. Programs that were open show the new name after a restart.";
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
            // Twice the gap there was between it and the buttons above.
            previewLine.Margin = new Padding(previewLine.Margin.Left, previewLine.Margin.Top + Px(9), previewLine.Margin.Right, previewLine.Margin.Bottom);
            Row(page, previewLine);
            // The columns the sliders below stand in (see Centre): their names start where the preview box
            // does, and they end where the output volume slider does.
            previewColumn = previewLabel.PreferredSize.Width + previewLabel.Margin.Horizontal;
            previewLine.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, previewColumn);
            // (A label's words start a little inside it: the sections are set that much to the left, so
            // that the words themselves, not their labels, start at the preview box's edge.)
            // (3 px at 100%, seen in the pictures: it grows with the text. Measuring it with TextRenderer gave 0.)
            NameInset = Math.Max(1, (int)Math.Round(Font.Height / 5.0));
            int sectionLeft = previewColumn - NameInset;
            labelColumn = 0;
            foreach (string sliderName in new string[] { "Volume", "Boost", "Dictation speed", "Sentence pause", "Comma pause", "Paragraph gap", "Voice variation" })
            {
                labelColumn = Math.Max(labelColumn, TextRenderer.MeasureText(sliderName, Font).Width);
            }
            labelColumn += 6 + Px(10);
            valueColumn = Px(146);

            // Windows' default voice, in a box of its own; beside it the output volume, for every voice.
            TableLayoutPanel outLine = Line("auto", "auto", "fill", "auto");
            OutLine = outLine;
            outLine.ColumnStyles[3] = new ColumnStyle(SizeType.Absolute, valueColumn);
            outLine.Margin = new Padding(0, Px(16), 0, Px(12));
            FlowLayoutPanel defaultBox = Flow();
            defaultBox.WrapContents = false; defaultBox.Dock = DockStyle.None; defaultBox.Anchor = AnchorStyles.Left; defaultBox.Tag = "box";
            defaultBox.Padding = new Padding(Px(8), Px(5), Px(8), Px(5));
            defaultBox.Margin = new Padding(3, 0, Px(18), 0);      // (its left: the frames' line, once they are made)
            DefaultBox = defaultBox;
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
            bool hasSymbols = symbols.Name == "Segoe MDL2 Assets";
            if (hasSymbols) { outputIcon.Font = symbols; outputIcon.Text = "\uE767"; }      // a speaker
            // The speaker mutes the output volume, and clicked again puts it back as it was.
            outputIcon.Cursor = Cursors.Hand;
            MuteIcon = outputIcon;
            outputIcon.Click += delegate { ToggleMute(); };
            OutputBar = new TrackBar();
            OutputBar.Maximum = 100; OutputBar.Minimum = 0; OutputBar.SmallChange = 5; OutputBar.LargeChange = 10; OutputBar.TickFrequency = 10;
            OutputBar.Value = 100;
            OutputBar.Dock = DockStyle.Fill;
            OutputBar.Margin = new Padding(0);
            Lower(OutputBar);
            OutputBar.MouseWheel += SliderWheel;
            OutputValue = Beside("100%");
            string outputTip = "The volume of all these voices, in every program. It changes straight away, even mid-sentence.";
            tips.SetToolTip(outputIcon, "Mute or unmute all these voices");
            tips.SetToolTip(OutputBar, outputTip);
            outLine.Controls.Add(defaultBox, 0, 0); outLine.Controls.Add(outputIcon, 1, 0); outLine.Controls.Add(OutputBar, 2, 0); outLine.Controls.Add(OutputValue, 3, 0);
            Row(page, outLine);
            OutputBar.ValueChanged += delegate
            {
                OutputValue.Text = OutputBar.Value == 0 ? "0% (muted)" : OutputBar.Value + "%";
                ShowMute();
                if (!Filling) { outputTimer.Stop(); outputTimer.Start(); }
            };

            // How the selected voice sounds: a framed section, in the middle of the window.
            TableLayoutPanel soundLine = Line("auto", "auto", "fill");
            SoundLabel = Words("Select a voice above to set how it sounds.", false, true);
            SoundLabel.Dock = DockStyle.None; SoundLabel.Anchor = AnchorStyles.Left;
            ResetButton = Btn("Reset");
            ResetButton.Enabled = false;
            ResetButton.Margin = new Padding(Px(10), ResetButton.Margin.Top, ResetButton.Margin.Right, ResetButton.Margin.Bottom);
            soundLine.Controls.Add(SoundLabel, 0, 0); soundLine.Controls.Add(ResetButton, 1, 0);
            TableLayoutPanel soundGrid = Line("auto", "fill", "auto");
            Sliders["Volume"] = AddSlider(soundGrid, "Volume", 0, (int)(Store.MaxPeak * 100), 10, 10, 10, true);
            Sliders["Boost"] = AddSlider(soundGrid, "Boost", 10, 40, 1, 5, 5, true);
            Sliders["Speed"] = AddSlider(soundGrid, "Dictation speed", 25, 200, 1, 5, 25, true);
            // (What each does is said when the pointer rests on the slider or its name.)
            Describe(Sliders["Volume"], "How loud this voice is.");
            Describe(Sliders["Boost"], "Makes the voice's quieter parts louder.");
            Describe(Sliders["Speed"], "Makes this voice faster or slower than the others.\r\nThe speed for all voices is on the Speed and pauses tab.");
            SoundFrame = Frame();
            FrameSliders(SoundFrame, soundGrid, sectionLeft, 0);
            Row(SoundFrame, soundLine);
            Row(SoundFrame, soundGrid);
            Row(page, SoundFrame);

            // Its own pauses and variation: a framed section too, with room above it.
            OwnBox = new CheckBox();
            OwnBox.Text = "Override pause duration and variation for the selected voice";
            OwnBox.AutoSize = true; OwnBox.Enabled = false; OwnBox.Checked = false;
            OwnBox.Margin = new Padding(3 + NameInset, 3, 3, 3);      // (its box, not its words, starts at that edge)
            gapGrid = Line("auto", "fill", "auto");
            Sliders["Sentence gap"] = AddSlider(gapGrid, "Sentence pause", 0, Store.SentenceLimit, 50, 250, 250, true);
            Sliders["Comma gap"] = AddSlider(gapGrid, "Comma pause", 0, Store.CommaLimit, 50, 250, 250, true);
            Sliders["Paragraph gap"] = AddSlider(gapGrid, "Paragraph gap", 0, Store.ParagraphLimit, 50, 250, 250, true);
            // Its own variation too (Speed and pauses has the one for all voices).
            OwnVariation = AddSlider(gapGrid, "Voice variation", 0, Store.VariationLimit, 5, 25, 25, true);
            Describe(Sliders["Sentence gap"], "Extra silence after each sentence, for this voice.");
            Describe(Sliders["Comma gap"], "Extra silence at each comma, semicolon and colon, for this voice.");
            Describe(Sliders["Paragraph gap"], "Extra silence when a new line or paragraph starts, for this voice.");
            Describe(OwnVariation, "How much this voice changes its tone and rhythm each time it speaks.\r\n" +
                "Programs that are already open use a new value after you restart them.");
            OwnVariation.Bar.Enabled = false;
            OwnVariation.Bar.ValueChanged += delegate
            {
                TrackBar bar = OwnVariation.Bar;
                int snapped = (int)(Math.Round(bar.Value / 5.0, MidpointRounding.AwayFromZero) * 5);
                if (bar.Value != snapped) { bar.Value = snapped; return; }
                OwnVariation.Value.Text = bar.Value + "%";
                if (!Filling) { saveTimer.Stop(); saveTimer.Start(); }
            };
            gapGrid.Visible = false;
            PauseFrame = Frame();
            FrameSliders(PauseFrame, gapGrid, sectionLeft, Px(16));
            // The default voice's box starts where the frames' lines do; the speaker and the output volume
            // slider move with it (the slider still ends where the sliders below end).
            DefaultBox.Margin = new Padding(PauseFrame.Margin.Left, 0, DefaultBox.Margin.Right, 0);
            Row(PauseFrame, OwnBox);
            Row(PauseFrame, gapGrid);
            Row(page, PauseFrame);
            // Volume moves in tens only (0, 10, ... 100): dragged, it snaps to the nearest.
            Sliders["Volume"].Bar.ValueChanged += delegate
            {
                TrackBar volume = Sliders["Volume"].Bar;
                int snapped = (int)(Math.Round(volume.Value / 10.0, MidpointRounding.AwayFromZero) * 10);
                if (volume.Value != snapped) { volume.Value = snapped; }
            };
            // Under the sections, starting where the sliders' names do, with room above.
            SavedNote = Words("Changes are saved straight away: press Preview to hear them.", true);
            SavedNote.Margin = new Padding(sectionLeft + 3, Px(16), SavedNote.Margin.Right, SavedNote.Margin.Bottom);
            Row(page, SavedNote);

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
            Row(page, Words("Speed and pauses for all voices", false, true));
            FlowLayoutPanel hearLine = Flow();
            PreviewVoiceBox = new ComboBox();
            PreviewVoiceBox.DropDownStyle = ComboBoxStyle.DropDownList; PreviewVoiceBox.Width = Px(280); PreviewVoiceBox.Anchor = AnchorStyles.Left;
            GlobalPreview = Btn("Preview");
            GlobalPreview.Enabled = false;
            hearLine.Controls.Add(Beside("Preview with")); hearLine.Controls.Add(PreviewVoiceBox); hearLine.Controls.Add(GlobalPreview);
            Row(page, hearLine);
            KeepWidth(GlobalPreview);
            TableLayoutPanel globalGrid = Line("auto", "fill", "auto");
            Slider speed = AddSlider(globalGrid, "Dictation speed", 25, 200, 5, 25, 25, true);
            SpeedBar = speed.Bar; SpeedValue = speed.Value;
            SpeedValue.Font = boldFont;
            AddNote(globalGrid, Words("Change how fast all voices speak. You can then adjust each voice individually in voice manager, to account for any differences.", true));
            RateWarn = Words("");
            RateWarn.ForeColor = Color.Firebrick; RateWarn.Tag = "warn";
            AddNote(globalGrid, RateWarn);
            RateReset = Btn("Set Windows' speed to 0");
            RateReset.Visible = false;
            AddNote(globalGrid, RateReset);
            string[][] specs = {
                new string[] { "Sentence gap", "Sentence pause", "Extra silence after each sentence." },
                new string[] { "Comma gap", "Comma pause", "Extra silence at each comma, semicolon and colon." },
                new string[] { "Paragraph gap", "Paragraph gap", "Extra silence when a new line or paragraph starts." } };
            int[] limits = { Store.SentenceLimit, Store.CommaLimit, Store.ParagraphLimit };
            for (int i = 0; i < specs.Length; i++)
            {
                Slider gap = AddSlider(globalGrid, specs[i][1], 0, limits[i], 50, 250, 250, true);
                gap.Value.Font = boldFont;
                AddNote(globalGrid, Words(specs[i][2], true));
                gap.Example = Words("");
                AddNote(globalGrid, gap.Example);
                GlobalBars[specs[i][0]] = gap;
            }
            // The sliders stand in the middle of the window, in the same columns as the Voice manager's; what
            // follows them starts where their names do.
            int sectionLeft = previewColumn - NameInset;
            Centre(globalGrid, 0);
            globalGrid.Margin = new Padding(sectionLeft, 0, 0, 0);
            GlobalGrid = globalGrid;
            Row(page, globalGrid);
            Label pausesNote = Words("Pauses get shorter when speech is faster and longer when it is slower. A long comma pause can make sentences " +
                "sound a little less smooth.", true);
            pausesNote.Margin = new Padding(sectionLeft + 3, pausesNote.Margin.Top, pausesNote.Margin.Right, pausesNote.Margin.Bottom);
            Row(page, pausesNote);
            TableLayoutPanel resetLine = Line("auto", "fill");
            resetLine.Margin = new Padding(sectionLeft, Px(18), resetLine.Margin.Right, resetLine.Margin.Bottom);
            Button pausesReset = Btn("Reset to defaults");
            resetLine.Controls.Add(pausesReset, 0, 0);
            // (Its words level with the button's: in the middle of the row's height.)
            Label defaultsNote = Words("Defaults: speed 100%, sentence 500 ms, comma 60 ms, paragraph 1200 ms, variation 60%. Changes are saved straight away.", true);
            defaultsNote.Dock = DockStyle.None; defaultsNote.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            defaultsNote.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            resetLine.Controls.Add(defaultsNote, 1, 0);
            Row(page, resetLine);

            // Advanced: how much the voices vary from one saying to the next.
            Label advanced = Words("Advanced setting", false, true);
            advanced.Margin = new Padding(sectionLeft + 3, Px(22), advanced.Margin.Right, advanced.Margin.Bottom);
            Row(page, advanced);
            TableLayoutPanel advancedGrid = Line("auto", "fill", "auto");
            Slider variation = AddSlider(advancedGrid, "Voice variation", 0, Store.VariationLimit, 5, 25, 25, true);
            VariationBar = variation.Bar;
            variation.Value.Font = boldFont;
            AddNote(advancedGrid, Words("How much a voice changes its tone and rhythm each time it speaks. Lower is steadier and more even, higher is " +
                "livelier but less predictable. 100% is how the voices were made; 60% is this program's default.", true));
            AddNote(advancedGrid, Words("Preview uses the new value straight away. Programs that are already open use it after you restart them.", true));
            Centre(advancedGrid, 0);
            advancedGrid.Margin = new Padding(sectionLeft, 0, 0, 0);
            AdvancedGrid = advancedGrid;
            Row(page, advancedGrid);
            EventHandler variationShown = delegate
            {
                int percent = VariationBar.Value;
                variation.Value.Text = percent + "%" + (percent == Store.VariationDefault ? " (default)" : percent == 100 ? " (as made)" : percent == 0 ? " (none)" : "");
            };
            Filling = true;
            try { VariationBar.Value = Clamp(Math.Round(Store.GetVariation(App.SettingsFile) / 5.0) * 5, 0, Store.VariationLimit); }
            catch (Exception) { VariationBar.Value = Store.VariationDefault; }
            finally { Filling = false; }
            variationShown(null, EventArgs.Empty);
            VariationBar.ValueChanged += delegate
            {
                int snapped = (int)(Math.Round(VariationBar.Value / 5.0, MidpointRounding.AwayFromZero) * 5);
                if (VariationBar.Value != snapped) { VariationBar.Value = snapped; return; }
                variationShown(null, EventArgs.Empty);
                if (!Filling) { globalTimer.Stop(); globalTimer.Start(); }
            };

            GlobalPreview.Click += delegate { string chosen = PreviewVoiceBox.SelectedItem as string; if (!string.IsNullOrEmpty(chosen)) { TogglePreview(chosen, chosen, null); } };
            PreviewVoiceBox.SelectedIndexChanged += delegate { UpdatePreviewButtons(); };
            pausesReset.Click += delegate { ResetGlobalPauses(); };
            RateReset.Click += delegate { Voices.WindowsRateStored = 0; UpdateWindowsRate(); };
        }

        void BuildPronunciation(Panel tab)
        {
            TableLayoutPanel page = Rows(tab);
            Row(page, Words("Words and names said your way, in every program that uses these voices", false, true));
            // Each language has its own rules: its voices use them, and no other language's.
            TableLayoutPanel rulesLine = Line("auto", "auto", "auto", "fill");
            WordLanguageBox = new ComboBox();
            WordLanguageBox.DropDownStyle = ComboBoxStyle.DropDownList; WordLanguageBox.Width = Px(200); WordLanguageBox.Anchor = AnchorStyles.Left;
            WordListOnBox = new CheckBox();
            WordListOnBox.Text = "Use these rules"; WordListOnBox.AutoSize = true; WordListOnBox.Checked = true;
            WordListOnBox.Anchor = AnchorStyles.Left; WordListOnBox.Margin = new Padding(Px(12), 0, Px(12), 0);
            Label rulesNote = Words("Each voice uses the rules for its own language. Untick to turn this whole list off.", true);
            rulesNote.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            rulesLine.Controls.Add(Beside("Rules for"), 0, 0); rulesLine.Controls.Add(WordLanguageBox, 1, 0);
            rulesLine.Controls.Add(WordListOnBox, 2, 0); rulesLine.Controls.Add(rulesNote, 3, 0);
            Row(page, rulesLine);
            WordLanguageBox.SelectedIndexChanged += delegate
            {
                LanguageChoice rulesFor = WordLanguageBox.SelectedItem as LanguageChoice;
                if (fillingChoices || rulesFor == null || rulesFor.Family == wordFamily) { return; }
                wordFamily = rulesFor.Family;
                UpdateWordList();
            };
            WordListOnBox.CheckedChanged += delegate { if (!Filling) { SaveWords(); } };
            WordList = NewList(true);
            WordList.Height = Px(390);
            WordList.MinimumSize = new Size(0, Px(390));
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
            // One instruction a paragraph, with an empty line between them, so that each can be read on its own.
            Row(page, Words("To add a rule, type a word and how it should sound (for example Thx as \"Thanks\"), then press Add rule.\r\n\r\n" +
                "To change a rule, select it, edit it and press Save rule.\r\n\r\n" +
                "To have something not read at all, type it as the word or phrase and leave \"Say it as\" empty, then press Add rule " +
                "(for example *** is then skipped).\r\n\r\n" +
                "Untick a rule to turn it off.\r\n\r\n" +
                "Hear it plays your spelling, so you can try it first.\r\n\r\n" +
                "Whole words only: \"cat\" won't change \"catalogue\". Match capitals: \"US\" won't change \"us\".", true));

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
            // Which voices the list shows (what it was on last time). The additional languages chosen have
            // their voices in the list, and a choice here each.
            TableLayoutPanel filterLine = Line("auto", "auto", "auto", "fill");
            VoiceFilterBox = new ComboBox();
            VoiceFilterBox.DropDownStyle = ComboBoxStyle.DropDownList; VoiceFilterBox.Width = Px(200); VoiceFilterBox.Anchor = AnchorStyles.Left;
            LanguagesButton = Btn("Additional languages...");
            LanguagesButton.Anchor = AnchorStyles.Left; LanguagesButton.Margin = new Padding(Px(12), 0, 0, 0);
            LanguagesButton.Click += delegate { ChooseLanguages(); };
            filterLine.Controls.Add(Beside("Show"), 0, 0); filterLine.Controls.Add(VoiceFilterBox, 1, 0); filterLine.Controls.Add(LanguagesButton, 2, 0);
            Row(addGrid, filterLine);
            // The instructions under them, with room above.
            Label addHow = Words("Tick the voices to install and untick the ones to remove, then press Apply changes.");
            addHow.Margin = new Padding(addHow.Margin.Left, Px(12), addHow.Margin.Right, 0);
            Row(addGrid, addHow);
            // On a line of its own under them: the voices vary in quality, so hear them first.
            QualityNote = Words("(Some voices sound much more natural than others. Play a sample before you install a voice.)", true);
            QualityNote.Margin = new Padding(QualityNote.Margin.Left, Px(2), QualityNote.Margin.Right, Px(4));
            Row(addGrid, QualityNote);
            AddList = NewList(true);
            AddList.Columns.Add("Voice", Px(220));
            AddList.Columns.Add("Speakers", Px(105));
            AddList.Columns.Add("Accent or language", Px(150));
            AddList.Columns.Add("Quality", Px(75));
            AddList.Columns.Add("Download", Px(90));
            AddList.Columns.Add("Catalogue key", Px(220));
            Dictionary<string, List<string>> speakersOf = SpeakersInstalled(null);
            foreach (KeyValuePair<string, List<string>> had in speakersOf)
            {
                if (had.Value.Count > 0) { Speakers[had.Key] = string.Join(", ", had.Value.ToArray()); }
            }
            foreach (Entry v in CatalogueList)
            {
                ListViewItem item = new ListViewItem(v.Display);
                item.SubItems.Add(v.Several ? v.SpeakerNames.Count + " speakers" : "1");
                item.SubItems.Add(v.Accent);
                item.SubItems.Add(v.Quality);
                item.SubItems.Add(v.SizeMb + " MB");
                item.SubItems.Add(v.Key);
                item.Tag = v;
                item.Checked = speakersOf.ContainsKey(v.Key);
                if (item.Checked) { item.Font = listBold; }
                VoiceItems.Add(item);
            }
            // It sorts by the column whose heading is clicked, as the Voice manager's does (kept for next time).
            for (int i = 0; i < VoiceItems.Count; i++) { addOrder[VoiceItems[i]] = i; }
            AddSorter = new RowSorter();
            AddSorter.Names = new string[] { "Voice", "Speakers", "Accent or language", "Quality", "Download", "Catalogue key" };
            AddSorter.Key = AddKey;
            AddSorter.Order = delegate(ListViewItem item) { int at; return addOrder.TryGetValue(item, out at) ? at : int.MaxValue; };
            AddSorter.Saved = ListLayout.Get("AddSort");
            sorters[AddList] = AddSorter;
            AddList.ListViewItemSorter = AddSorter;
            AddList.ColumnClick += delegate(object sender, ColumnClickEventArgs e) { SortAdd(e.Column); };
            FillLanguageChoices();
            // "Show" starts on what it was on last time (or what the installer's language choice set), if that is still a choice.
            LanguageChoice showFirst = null;
            string showWas = Languages.Show;
            fillingChoices = true;
            try
            {
                foreach (object choice in VoiceFilterBox.Items) { if (showWas.Length > 0 && choice.ToString() == showWas) { VoiceFilterBox.SelectedItem = choice; showFirst = choice as LanguageChoice; } }
            }
            finally { fillingChoices = false; }
            foreach (ListViewItem item in VoiceItems)
            {
                Entry listed = (Entry)item.Tag;
                if (FilterShows(showFirst, listed) && LanguageShown(listed.Family)) { AddList.Items.Add(item); }
            }
            AddList.Sort();
            // (The Pronunciation tab lists the rules of the first language there is: English, unless it is not listed.)
            LanguageChoice rulesFirst = WordLanguageBox.SelectedItem as LanguageChoice;
            if (rulesFirst != null) { wordFamily = rulesFirst.Family; }
            // The room left over is shared by the two lists: the voices' two fifths, the speakers' three.
            Row(addGrid, AddList, true);
            addGrid.RowStyles[addGrid.RowCount - 1] = new RowStyle(SizeType.Percent, 42f);
            TableLayoutPanel playLine = Line("auto", "auto", "fill");
            Button play = Btn("Play sample");
            play.MinimumSize = new Size(Math.Max(play.PreferredSize.Width, TextRenderer.MeasureText("Stop sample", play.Font).Width + Px(24)), 0);
            // Off at every start (not remembered): lists only the ticked speakers, to find the installed ones.
            OnlyTickedBox = new CheckBox();
            OnlyTickedBox.Text = "Only show selected speakers";
            OnlyTickedBox.AutoSize = true; OnlyTickedBox.Checked = false;
            OnlyTickedBox.Anchor = AnchorStyles.Left;
            SpeakerLabel = Words("Select a voice to see its speakers: click a name to hear it, tick its box to install it.");
            // All three centred on the row's middle, so the words line up whether the label takes one line or two.
            play.Anchor = AnchorStyles.Left; SpeakerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            playLine.Controls.Add(play, 0, 0); playLine.Controls.Add(OnlyTickedBox, 1, 0); playLine.Controls.Add(SpeakerLabel, 2, 0);
            Row(addGrid, playLine);
            SpeakerList = new BigCheckList();
            SpeakerList.Font = speakerFont; SpeakerList.BoxSize = Px(19); SpeakerList.RowGap = Px(6);
            SpeakerList.MultiColumn = true; SpeakerList.ColumnWidth = Px(120); SpeakerList.CheckOnClick = true;
            SpeakerList.IntegralHeight = false; SpeakerList.Enabled = false;
            SpeakerList.Dock = DockStyle.Fill;
            SpeakerList.MinimumSize = new Size(0, Px(110));
            Row(addGrid, SpeakerList, true);
            addGrid.RowStyles[addGrid.RowCount - 1] = new RowStyle(SizeType.Percent, 58f);
            SizeLabel = Words("");
            Row(addGrid, SizeLabel);
            WarnLabel = Words("");
            WarnLabel.ForeColor = Color.Firebrick; WarnLabel.Tag = "warn";
            Row(addGrid, WarnLabel);
            UsBox = new CheckBox();
            UsBox.Text = "Also list British voices as US English (for programs that only show US voices)";
            UsBox.AutoSize = true; UsBox.Checked = UsTags;
            Row(addGrid, UsBox);
            Row(addGrid, Words("Extra speakers of a voice take no extra space. Medium and high quality versions of a voice are separate " +
                "downloads. Some programs open more slowly when many voices are installed, so install only the ones you use.", true));

            AddList.SelectedIndexChanged += delegate { if (!filtering) { UpdateSelection(); } };
            // A click on a voice's box (or Space) ticks it by hand: a voice of several speakers then has all
            // its speakers ticked, or none. A click on its name plays a one-speaker voice's sample; a voice of
            // several plays nothing (its speakers below are clicked to hear them).
            AddList.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                ListViewHitTestInfo hit = AddList.HitTest(e.Location);
                tickByHand = e.Button == MouseButtons.Left && hit.Item != null && hit.Location == ListViewHitTestLocations.StateImage;
            };
            AddList.MouseUp += delegate { BeginInvoke((MethodInvoker)delegate { tickByHand = false; }); };
            AddList.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space) { tickByHand = true; } };
            AddList.KeyUp += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space) { BeginInvoke((MethodInvoker)delegate { tickByHand = false; }); } };
            AddList.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) { return; }
                ListViewHitTestInfo hit = AddList.HitTest(e.Location);
                if (hit.Item != null && hit.Location != ListViewHitTestLocations.StateImage)
                {
                    ListViewItem clicked = hit.Item;
                    BeginInvoke((MethodInvoker)delegate { VoiceNameClicked(clicked); });
                }
            };
            AddList.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
            {
                if (Filling || filtering || !listsReady.Contains(AddList)) { return; }
                if (e.Item != null && ((Entry)e.Item.Tag).Several && (tickByHand || PointerOnBox(e.Item)))
                {
                    // Ticked: with more than two speakers, asked first (after the click, not inside it).
                    ListViewItem ticked = e.Item;
                    if (ticked.Checked && ((Entry)ticked.Tag).SpeakerNames.Count > 2) { BeginInvoke((MethodInvoker)delegate { AskAllSpeakers(ticked); }); }
                    else { AllSpeakers(ticked, ticked.Checked); }
                }
                UpdateSummary();
                // The selected voice's speaker box follows its tick: a one-speaker voice's box is the voice's own
                // tick, and the filtered list gains or loses the voice's speakers. After the event, not inside it.
                if (e.Item != null && AddList.SelectedItems.Count > 0 && AddList.SelectedItems[0] == e.Item &&
                    (OnlyTickedBox.Checked || !((Entry)e.Item.Tag).Several))
                {
                    BeginInvoke((MethodInvoker)delegate { UpdateSelection(); });
                }
            };
            OnlyTickedBox.CheckedChanged += delegate { UpdateSelection(); };
            VoiceFilterBox.SelectedIndexChanged += delegate
            {
                if (fillingChoices) { return; }
                // (Kept for the next start.)
                LanguageChoice shown = VoiceFilterBox.SelectedItem as LanguageChoice;
                Languages.Show = shown == null || shown.Text == Languages.AllVoices ? "" : shown.Text;
                ApplyVoiceFilter();
            };
            // A click on a speaker's name only selects it (to play its sample); its box is what ticks it.
            SpeakerList.MouseDown += SpeakerMouseDown;
            SpeakerList.MouseUp += delegate { nameClick = false; };
            SpeakerList.ItemCheck += SpeakerTicking;
            // Play sample stops a sample still playing (its words say so meanwhile); else it plays one.
            play.Click += delegate
            {
                if (Sample.Playing) { Sample.Stop(); } else { PlaySample(); }
                play.Text = Sample.Playing ? "Stop sample" : "Play sample";
            };
            PlayButton = play;
            Timer sampleWatch = new Timer();
            sampleWatch.Interval = 200;
            sampleWatch.Tick += delegate
            {
                Sample.Check();
                string words = Sample.Playing ? "Stop sample" : "Play sample";
                if (play.Text != words) { play.Text = words; }
            };
            sampleWatch.Start();
            // Selecting a speaker plays its sample (Play sample plays it again, or a voice with one speaker).
            SpeakerList.SelectedIndexChanged += delegate { if (!Filling && SpeakerList.SelectedIndex >= 0) { PlaySample(); } };
            tab.Resize += delegate { UpdateAddLayout(); };
            tab.ClientSizeChanged += delegate { UpdateAddLayout(); };
        }

        // Whether the "Show" box's choice lets a voice through.
        public static bool FilterShows(LanguageChoice choice, Entry v)
        {
            if (choice == null) { return true; }
            return (choice.Family == null || v.Family == choice.Family) && (choice.Code == null || v.Code == choice.Code);
        }
        // Whether a language's voices are listed: one that is chosen (English is unless switched off, another
        // when it is one of the additional languages), or has a voice ticked (an installed one, say: never
        // hidden). English is listed too while nothing else is: the list is never empty.
        public bool LanguageShown(string family)
        {
            List<string> chosen = Languages.Enabled;
            if (family == "en")
            {
                if (Languages.English) { return true; }
                bool another = false;
                foreach (Entry v in CatalogueList) { if (v.Family != "en" && chosen.Contains(v.Family)) { another = true; break; } }
                if (!another) { return true; }
            }
            else if (chosen.Contains(family)) { return true; }
            foreach (ListViewItem item in VoiceItems) { if (item.Checked && ((Entry)item.Tag).Family == family) { return true; } }
            return false;
        }
        // The catalogue's languages beside English, each with its name, in name order.
        public List<KeyValuePair<string, string>> OtherLanguages()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            foreach (Entry v in CatalogueList)
            {
                if (v.Family == "en") { continue; }
                bool had = false;
                foreach (KeyValuePair<string, string> pair in found) { if (pair.Key == v.Family) { had = true; } }
                if (!had) { found.Add(new KeyValuePair<string, string>(v.Family, v.Language)); }
            }
            found.Sort(delegate(KeyValuePair<string, string> x, KeyValuePair<string, string> y) { return string.Compare(x.Value, y.Value, StringComparison.CurrentCultureIgnoreCase); });
            return found;
        }
        // The choices of the "Show" box and of the Pronunciation tab's "Rules for": English's, and one for
        // each other language listed. What was chosen stays chosen if it is still there (else the first).
        bool fillingChoices;
        public void FillLanguageChoices()
        {
            fillingChoices = true;
            try
            {
                List<LanguageChoice> shows = new List<LanguageChoice>();
                shows.Add(new LanguageChoice(Languages.AllVoices, null, null));
                List<LanguageChoice> rules = new List<LanguageChoice>();
                if (LanguageShown("en"))
                {
                    shows.Add(new LanguageChoice("English only", "en", null));
                    shows.Add(new LanguageChoice(Languages.AmericanOnly, "en", "en_US"));
                    shows.Add(new LanguageChoice(Languages.BritishOnly, "en", "en_GB"));
                    rules.Add(new LanguageChoice("English", "en", null));
                }
                foreach (KeyValuePair<string, string> language in OtherLanguages())
                {
                    if (!LanguageShown(language.Key)) { continue; }
                    shows.Add(new LanguageChoice(language.Value + " only", language.Key, null));
                    rules.Add(new LanguageChoice(language.Value, language.Key, null));
                }
                if (rules.Count == 0) { rules.Add(new LanguageChoice("English", "en", null)); }
                Refill(VoiceFilterBox, shows);
                if (WordLanguageBox != null) { Refill(WordLanguageBox, rules); }
            }
            finally { fillingChoices = false; }
        }
        static void Refill(ComboBox box, List<LanguageChoice> choices)
        {
            string was = box.SelectedItem == null ? null : box.SelectedItem.ToString();
            box.BeginUpdate();
            box.Items.Clear();
            int chosen = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                box.Items.Add(choices[i]);
                if (choices[i].Text == was) { chosen = i; }
            }
            box.SelectedIndex = chosen;
            box.EndUpdate();
        }
        // The additional languages are set (kept for next time): the lists and their choices follow.
        public void SetLanguages(List<string> families, bool? english = null)
        {
            Languages.Enabled = families;
            if (english.HasValue) { Languages.English = english.Value; }
            LanguagesChanged();
        }
        void LanguagesChanged()
        {
            FillLanguageChoices();
            ApplyVoiceFilter();
            LanguageChoice rulesFor = WordLanguageBox.SelectedItem as LanguageChoice;
            if (rulesFor != null && rulesFor.Family != wordFamily) { wordFamily = rulesFor.Family; UpdateWordList(); }
        }
        // The "Additional languages" window: the catalogue's other languages, each with a box. A language
        // with a voice ticked on Add or remove voices (an installed one, say) is listed whatever is chosen
        // here, and says so. 'list' gets its list (a row's language is 'families' at the same place).
        public Form LanguagesDialog(out CheckedListBox list, out List<string> families)
        {
            Form dialog = new Form();
            dialog.Text = "Additional languages"; dialog.Font = Font; dialog.Icon = Icon;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = false; dialog.MinimizeBox = false;
            dialog.StartPosition = FormStartPosition.CenterParent; dialog.ShowInTaskbar = false;
            dialog.ClientSize = new Size(Px(440), Px(520)); dialog.Padding = new Padding(Px(10));
            TableLayoutPanel rows = new TableLayoutPanel();
            rows.Dock = DockStyle.Fill; rows.ColumnCount = 1;
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            dialog.Controls.Add(rows);
            Row(rows, Words("Tick the languages whose voices you want listed on Add or remove voices."));
            BigCheckList boxes = new BigCheckList();
            boxes.Font = listFont; boxes.BoxSize = Px(16); boxes.RowGap = Px(6);
            boxes.CheckOnClick = true; boxes.IntegralHeight = false; boxes.Dock = DockStyle.Fill;
            boxes.SelectedBack = palette.Selected; boxes.SelectedText = palette.Dark ? palette.Text : SystemColors.HighlightText;
            families = new List<string>();
            List<string> chosen = Languages.Enabled;
            List<int> kept = new List<int>();
            // English first (it can be unticked while another language is ticked), then the others by name.
            List<KeyValuePair<string, string>> offered = new List<KeyValuePair<string, string>>();
            offered.Add(new KeyValuePair<string, string>("en", "English"));
            offered.AddRange(OtherLanguages());
            foreach (KeyValuePair<string, string> language in offered)
            {
                int voices = 0;
                bool ticked = false;
                foreach (ListViewItem item in VoiceItems)
                {
                    if (((Entry)item.Tag).Family != language.Key) { continue; }
                    voices++;
                    if (item.Checked) { ticked = true; }
                }
                if (ticked) { kept.Add(families.Count); }
                families.Add(language.Key);
                boxes.Items.Add(language.Value + " (" + voices + (voices == 1 ? " voice" : " voices") + (ticked ? ", some ticked: stays listed)" : ")"),
                    ticked || (language.Key == "en" ? LanguageShown("en") : chosen.Contains(language.Key)));
            }
            boxes.ItemCheck += delegate(object sender, ItemCheckEventArgs e) { if (kept.Contains(e.Index)) { e.NewValue = CheckState.Checked; } };
            Row(rows, boxes, true);
            Row(rows, Words("This program itself stays in English. Each language has its own pronunciation rules on the Pronunciation tab. " +
                "Voices in other languages have been tested much less than the English ones. With nothing ticked, English is listed.", true));
            FlowLayoutPanel buttons = Flow();
            buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Dock = DockStyle.Fill;
            Button cancel = Btn("Cancel");
            Button ok = Btn("OK");
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
            Row(rows, buttons);
            dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            ThemeOn(rows, palette);
            dialog.BackColor = palette.Back; dialog.ForeColor = palette.Text;
            list = boxes;
            return dialog;
        }
        void ChooseLanguages()
        {
            CheckedListBox list;
            List<string> families;
            using (Form dialog = LanguagesDialog(out list, out families))
            {
                if (families.Count <= 1) { StatusLabel.Text = "The voice list has no other languages (it could not be read)."; return; }
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                List<string> chosen = new List<string>();
                for (int i = 0; i < families.Count; i++) { if (list.GetItemChecked(i)) { chosen.Add(families[i]); } }
                bool english = chosen.Remove("en");
                SetLanguages(chosen, english);
            }
        }
        // The list shows the voices the "Show" box lets through. Rows not shown keep their ticks, and the
        // voice selected stays selected if it is still shown.
        public void ApplyVoiceFilter()
        {
            LanguageChoice filter = VoiceFilterBox.SelectedItem as LanguageChoice;
            ListViewItem selected = AddList.SelectedItems.Count > 0 ? AddList.SelectedItems[0] : null;
            // (A row's tick is remembered apart from the list, in case Windows' list loses it on the way out or in.)
            Dictionary<ListViewItem, bool> ticks = new Dictionary<ListViewItem, bool>();
            foreach (ListViewItem item in VoiceItems) { ticks[item] = item.Checked; }
            SaveSpeakers();
            filtering = true;
            AddList.BeginUpdate();
            try
            {
                AddList.Items.Clear();
                foreach (ListViewItem item in VoiceItems)
                {
                    Entry voice = (Entry)item.Tag;
                    if (FilterShows(filter, voice) && LanguageShown(voice.Family)) { AddList.Items.Add(item); }
                }
                AddList.Sort();      // (in the order chosen by clicking a heading)
                foreach (ListViewItem item in VoiceItems)
                {
                    if (item.Checked != ticks[item]) { item.Checked = ticks[item]; }
                }
                if (selected != null && selected.ListView == AddList) { selected.Selected = true; selected.EnsureVisible(); }
            }
            finally { AddList.EndUpdate(); filtering = false; }
            UpdateSelection();
            UpdateSummary();
        }

        void SpeakerMouseDown(object sender, MouseEventArgs e)
        {
            int index = SpeakerList.IndexFromPoint(e.Location);
            nameClick = index >= 0 && (e.X - SpeakerList.GetItemRectangle(index).Left) > SpeakerList.TextLeft - 2;
        }
        void SpeakerTicking(object sender, ItemCheckEventArgs e)
        {
            if (nameClick) { e.NewValue = e.CurrentValue; return; }
            if (Filling || AddList.SelectedItems.Count == 0) { return; }
            ListViewItem voice = AddList.SelectedItems[0];
            // A one-speaker voice: its speaker's box is the voice's own tick, both ways (nothing goes in Speakers).
            if (!((Entry)voice.Tag).Several) { voice.Checked = e.NewValue == CheckState.Checked; return; }
            if (e.NewValue == CheckState.Checked) { voice.Checked = true; }
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
            ThemeMenu();
            ShowSortArrow(InstalledList); ShowSortArrow(AddList);
            SpeakerList.SelectedBack = palette.Selected;
            SpeakerList.SelectedText = dark ? palette.Text : SystemColors.HighlightText;
            if (ThemeButton.Text.Length > 1) { ThemeButton.Text = dark ? "Light" : "Dark"; }
            ShowPage(PageIndex);
            ResumeLayout();
            Refresh();
        }

        // The speaker beside the output volume: muted (0) or not, it shows which.
        public void ToggleMute()
        {
            if (OutputBar.Value > 0) { unmutedVolume = OutputBar.Value; OutputBar.Value = 0; }
            else { OutputBar.Value = unmutedVolume > 0 ? unmutedVolume : 100; }
        }
        void ShowMute()
        {
            if (MuteIcon == null || MuteIcon.Font.Name != "Segoe MDL2 Assets") { return; }
            string glyph = OutputBar.Value == 0 ? "\uE74F" : "\uE767";      // a speaker crossed out, or not
            if (MuteIcon.Text != glyph) { MuteIcon.Text = glyph; }
        }
        public int HeaderLineBottom { get { return headerLine.PointToScreen(new Point(0, headerLine.Height)).Y; } }
        // The About window: what the program is put together from (each with its link), who did what, and
        // a message from Lumoti.
        public Form AboutWindow()
        {
            Form about = new Form();
            about.Text = "About " + App.Name; about.Font = Font; about.Icon = Icon;
            about.FormBorderStyle = FormBorderStyle.FixedDialog; about.MaximizeBox = false; about.MinimizeBox = false;
            about.StartPosition = FormStartPosition.CenterParent; about.ShowInTaskbar = false;
            about.ClientSize = new Size(Px(560), Px(640)); about.Padding = new Padding(Px(14), Px(12), Px(6), Px(10));
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill; scroll.AutoScroll = true;
            TableLayoutPanel rows = new TableLayoutPanel();
            rows.ColumnCount = 1; rows.AutoSize = true; rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rows.Dock = DockStyle.Top; rows.Padding = new Padding(0, 0, Px(8), 0);
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            int width = Px(560) - Px(14) - Px(6) - Px(8) - SystemInformation.VerticalScrollBarWidth - 8;
            Func<string, bool, bool, int, Label> line = delegate(string text, bool muted, bool bold, int above)
            {
                Label l = Words(text, muted, bold);
                l.Dock = DockStyle.None; l.MaximumSize = new Size(width, 0); l.Margin = new Padding(3, above, 3, 2);
                Row(rows, l);
                return l;
            };
            Label title = line(App.Name + " " + App.VersionText, false, true, 0);
            title.Font = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold);
            line("Modern, natural-sounding voices for any Windows program that can speak. Free and open source.", false, false, Px(2));
            AddLink(rows, Credit.Home, Px(2));
            line("What it is made from", false, true, Px(16));
            line("This software does not make the voices or the speech engine itself. It takes the open source projects below and combines " +
                "them into one program that is simple to install and use.", false, false, Px(2));
            foreach (Credit credit in Credit.All)
            {
                line(credit.Name, false, true, Px(12));
                line("By " + credit.By + ". " + credit.Does, false, false, 0);
                AddLink(rows, credit.Link, 0);
            }
            line(Credit.Thanks, false, false, Px(14));
            line("Who did what", false, true, Px(16));
            line(Credit.Coding, false, false, Px(2));
            line("A message from Lumoti:", false, true, Px(16));
            line(Credit.FromLumoti, false, false, Px(2));
            scroll.Controls.Add(rows);
            FlowLayoutPanel buttons = Flow();
            buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Dock = DockStyle.Bottom;
            buttons.Padding = new Padding(0, Px(8), Px(8), 0);
            Button close = Btn("Close");
            close.DialogResult = DialogResult.OK;
            buttons.Controls.Add(close);
            about.Controls.Add(scroll); about.Controls.Add(buttons);
            about.AcceptButton = close; about.CancelButton = close;
            ThemeOn(scroll, palette); ThemeOn(buttons, palette);
            about.BackColor = palette.Back; about.ForeColor = palette.Text;
            foreach (Control c in rows.Controls)
            {
                LinkLabel link = c as LinkLabel;
                if (link != null) { link.LinkColor = palette.Dark ? Color.FromArgb(110, 180, 255) : Color.FromArgb(0, 90, 180); link.ActiveLinkColor = link.LinkColor; link.BackColor = palette.Back; }
            }
            return about;
        }
        void AddLink(TableLayoutPanel rows, string address, int above)
        {
            LinkLabel link = new LinkLabel();
            link.Text = address; link.AutoSize = true; link.Margin = new Padding(3, above, 3, 2);
            link.LinkBehavior = LinkBehavior.HoverUnderline; link.TabStop = false;
            link.LinkClicked += delegate
            {
                try { Process.Start(address); } catch (Exception e) { StatusLabel.Text = "Could not open " + address + ": " + e.Message; }
            };
            Row(rows, link);
        }

        // The "Powered by" window, the first time Modern Voices Config opens: the program's name, and what
        // it runs on, each with whose it is. A click anywhere on it (or its X, or a key) closes it.
        public Form PoweredByWindow()
        {
            Form window = new Form();
            window.Text = App.Name; window.Font = Font;
            window.FormBorderStyle = FormBorderStyle.None; window.ShowInTaskbar = false;
            window.StartPosition = FormStartPosition.CenterParent; window.KeyPreview = true;
            window.BackColor = palette.Input; window.ForeColor = palette.Text;
            window.Padding = new Padding(Px(2));
            window.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen edge = new Pen(palette.TabOn, Px(2))) { e.Graphics.DrawRectangle(edge, Px(1), Px(1), window.ClientSize.Width - Px(2), window.ClientSize.Height - Px(2)); }
            };
            int width = Px(460);
            TableLayoutPanel rows = new TableLayoutPanel();
            rows.ColumnCount = 1; rows.AutoSize = true; rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rows.Location = new Point(Px(2), Px(2)); rows.Padding = new Padding(Px(24), Px(30), Px(24), Px(26));
            rows.BackColor = palette.Input;
            Func<string, float, FontStyle, Color, int, Label> line = delegate(string text, float size, FontStyle style, Color colour, int above)
            {
                Label l = new Label();
                l.Text = text; l.AutoSize = false; l.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
                l.Font = new Font(Font.FontFamily, size, style); l.ForeColor = colour; l.BackColor = palette.Input;
                l.Margin = new Padding(0, above, 0, 0);
                l.Size = new Size(width, TextRenderer.MeasureText(text, l.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter).Height + 2);
                rows.RowCount++;
                rows.Controls.Add(l, 0, rows.RowCount - 1);
                return l;
            };
            line("MODERN VOICES", Font.Size + 9f, FontStyle.Bold, palette.Text, 0);
            line("Powered By...", Font.Size + 2f, FontStyle.Regular, palette.Muted, Px(4));
            bool first = true;
            foreach (Credit credit in Credit.All)
            {
                line(credit.Name, Font.Size + 3f, FontStyle.Bold, palette.Text, first ? Px(34) : Px(24));
                line(credit.By, Font.Size, FontStyle.Regular, palette.Muted, Px(3));
                first = false;
            }
            line(Credit.Thanks, Font.Size, FontStyle.Italic, palette.Text, Px(34));
            window.Controls.Add(rows);
            Size inner = rows.PreferredSize;
            window.ClientSize = new Size(inner.Width + Px(4), inner.Height + Px(4));
            // The X, in the top right corner (over the rows).
            Label x = new Label();
            x.Text = "\u2715"; x.AutoSize = false; x.Size = new Size(Px(34), Px(30)); x.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            x.Font = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold); x.ForeColor = palette.Text; x.BackColor = palette.Input; x.Cursor = Cursors.Hand;
            // (Windows' own close symbol where it has its symbols; else the letter.)
            Font closeSymbols = new Font("Segoe MDL2 Assets", Font.Size + 1f);
            if (closeSymbols.Name == "Segoe MDL2 Assets") { x.Font = closeSymbols; x.Text = "\uE8BB"; } else { x.Text = "X"; }
            x.Name = "close";
            x.Location = new Point(window.ClientSize.Width - x.Width - Px(4), Px(4));
            x.MouseEnter += delegate { x.BackColor = Color.FromArgb(196, 43, 28); x.ForeColor = Color.White; };
            x.MouseLeave += delegate { x.BackColor = palette.Input; x.ForeColor = palette.Text; };
            window.Controls.Add(x);
            x.BringToFront();
            EventHandler shut = delegate { window.DialogResult = DialogResult.OK; window.Close(); };
            window.Click += shut; rows.Click += shut; x.Click += shut;
            foreach (Control c in rows.Controls) { c.Click += shut; c.Cursor = Cursors.Hand; }
            rows.Cursor = Cursors.Hand; window.Cursor = Cursors.Hand;
            window.KeyDown += delegate { shut(null, EventArgs.Empty); };
            return window;
        }

        // Over Add or remove voices whenever Config opens with no voice installed (after "Powered by", the
        // first time): where to start. OK, its X, Enter or Esc closes it.
        public const string FirstVoicesWords = "Select the voices you wish to download and install, then click Apply changes.\r\n(Voice quality varies. Sample before downloading.)";
        // The same in the language chosen when installing (shown above the English; Claude's translations,
        // not checked by a speaker of each, like the installer's note). The button's name stays English, as
        // the button is.
        public static readonly Dictionary<string, string> FirstVoicesTranslated = new Dictionary<string, string>
        {
            { "bg", "Изберете гласовете, които искате да изтеглите и инсталирате, след което щракнете върху „Apply changes“.\r\n(Качеството на гласовете е различно. Чуйте мостра, преди да изтеглите.)" },
            { "ca", "Seleccioneu les veus que voleu baixar i instal·lar i, després, feu clic a «Apply changes».\r\n(La qualitat de les veus varia. Escolteu una mostra abans de baixar-les.)" },
            { "cs", "Vyberte hlasy, které chcete stáhnout a nainstalovat, a potom klikněte na „Apply changes“.\r\n(Kvalita hlasů se liší. Před stažením si poslechněte ukázku.)" },
            { "cy", "Dewiswch y lleisiau rydych am eu llwytho i lawr a'u gosod, yna cliciwch “Apply changes”.\r\n(Mae ansawdd y lleisiau'n amrywio. Gwrandewch ar sampl cyn llwytho i lawr.)" },
            { "da", "Vælg de stemmer, du vil hente og installere, og klik derefter på »Apply changes«.\r\n(Stemmernes kvalitet varierer. Lyt til en prøve, før du henter.)" },
            { "de", "Wählen Sie die Stimmen aus, die Sie herunterladen und installieren möchten, und klicken Sie dann auf „Apply changes“.\r\n(Die Qualität der Stimmen ist unterschiedlich. Hören Sie vor dem Herunterladen eine Hörprobe an.)" },
            { "el", "Επιλέξτε τις φωνές που θέλετε να κατεβάσετε και να εγκαταστήσετε και, στη συνέχεια, κάντε κλικ στο «Apply changes».\r\n(Η ποιότητα των φωνών διαφέρει. Ακούστε ένα δείγμα πριν από τη λήψη.)" },
            { "es", "Seleccione las voces que desea descargar e instalar y, después, haga clic en «Apply changes».\r\n(La calidad de las voces varía. Escuche una muestra antes de descargarlas.)" },
            { "eu", "Hautatu deskargatu eta instalatu nahi dituzun ahotsak, eta egin klik «Apply changes» botoian.\r\n(Ahotsen kalitatea aldakorra da. Entzun lagin bat deskargatu aurretik.)" },
            { "fi", "Valitse äänet, jotka haluat ladata ja asentaa, ja napsauta sitten ”Apply changes”.\r\n(Äänten laatu vaihtelee. Kuuntele näyte ennen lataamista.)" },
            { "fr", "Sélectionnez les voix que vous souhaitez télécharger et installer, puis cliquez sur « Apply changes ».\r\n(La qualité des voix varie. Écoutez un extrait avant de les télécharger.)" },
            { "gl", "Seleccione as voces que desexa descargar e instalar e, despois, prema en «Apply changes».\r\n(A calidade das voces varía. Escoite unha mostra antes de descargalas.)" },
            { "hu", "Jelölje ki a letölteni és telepíteni kívánt hangokat, majd kattintson az „Apply changes” gombra.\r\n(A hangok minősége eltérő. Letöltés előtt hallgasson meg egy mintát.)" },
            { "is", "Veldu raddirnar sem þú vilt sækja og setja upp og smelltu svo á „Apply changes“.\r\n(Gæði raddanna eru misjöfn. Hlustaðu á sýnishorn áður en þú sækir.)" },
            { "it", "Seleziona le voci che vuoi scaricare e installare, poi fai clic su «Apply changes».\r\n(La qualità delle voci varia. Ascolta un campione prima di scaricarle.)" },
            { "lb", "Wielt d'Stëmmen aus, déi Dir eroflueden an installéiere wëllt, a klickt dann op „Apply changes“.\r\n(D'Qualitéit vun de Stëmmen ass ënnerschiddlech. Lauschtert eng Prouf, ier Dir eroflued.)" },
            { "lv", "Atlasiet balsis, kuras vēlaties lejupielādēt un instalēt, un pēc tam noklikšķiniet uz „Apply changes”.\r\n(Balsu kvalitāte atšķiras. Pirms lejupielādes noklausieties paraugu.)" },
            { "nl", "Selecteer de stemmen die u wilt downloaden en installeren en klik daarna op ‘Apply changes’.\r\n(De kwaliteit van de stemmen verschilt. Luister naar een voorbeeld voordat u downloadt.)" },
            { "no", "Velg stemmene du vil laste ned og installere, og klikk deretter på «Apply changes».\r\n(Kvaliteten på stemmene varierer. Lytt til en prøve før du laster ned.)" },
            { "pl", "Zaznacz głosy, które chcesz pobrać i zainstalować, a następnie kliknij „Apply changes”.\r\n(Jakość głosów jest różna. Przed pobraniem odsłuchaj próbkę.)" },
            { "pt", "Selecione as vozes que pretende transferir e instalar e, depois, clique em «Apply changes».\r\n(A qualidade das vozes varia. Ouça uma amostra antes de transferir.)" },
            { "ro", "Selectați vocile pe care doriți să le descărcați și să le instalați, apoi faceți clic pe „Apply changes”.\r\n(Calitatea vocilor variază. Ascultați o mostră înainte de descărcare.)" },
            { "ru", "Выберите голоса, которые хотите загрузить и установить, а затем нажмите «Apply changes».\r\n(Качество голосов различается. Прослушайте образец перед загрузкой.)" },
            { "sk", "Vyberte hlasy, ktoré chcete stiahnuť a nainštalovať, a potom kliknite na „Apply changes“.\r\n(Kvalita hlasov sa líši. Pred stiahnutím si vypočujte ukážku.)" },
            { "sl", "Izberite glasove, ki jih želite prenesti in namestiti, nato kliknite »Apply changes«.\r\n(Kakovost glasov se razlikuje. Pred prenosom poslušajte vzorec.)" },
            { "sr", "Izaberite glasove koje želite da preuzmete i instalirate, a zatim kliknite na „Apply changes“.\r\n(Kvalitet glasova se razlikuje. Poslušajte uzorak pre preuzimanja.)" },
            { "sv", "Markera de röster du vill hämta och installera och klicka sedan på ”Apply changes”.\r\n(Röstkvaliteten varierar. Lyssna på ett prov innan du hämtar.)" },
            { "tr", "İndirip yüklemek istediğiniz sesleri seçin, ardından “Apply changes” düğmesine tıklayın.\r\n(Ses kalitesi farklılık gösterir. İndirmeden önce bir örnek dinleyin.)" },
            { "uk", "Виберіть голоси, які хочете завантажити та встановити, а потім натисніть «Apply changes».\r\n(Якість голосів різна. Прослухайте зразок перед завантаженням.)" }
        };
        public Form FirstVoicesWindow()
        {
            Form window = new Form();
            window.Text = App.Name; window.Font = Font;
            window.FormBorderStyle = FormBorderStyle.None; window.ShowInTaskbar = false;
            window.StartPosition = FormStartPosition.CenterParent; window.KeyPreview = true;
            window.BackColor = palette.Input; window.ForeColor = palette.Text;
            window.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen edge = new Pen(palette.TabOn, Px(2))) { e.Graphics.DrawRectangle(edge, Px(1), Px(1), window.ClientSize.Width - Px(2), window.ClientSize.Height - Px(2)); }
            };
            int width = Px(340);
            // (Another language chosen when installing: its words first, then the English.)
            string own;
            string text = FirstVoicesTranslated.TryGetValue(Languages.InstallLanguage, out own) ? own + "\r\n\r\n" + FirstVoicesWords : FirstVoicesWords;
            if (text != FirstVoicesWords) { width = Px(400); }
            Label words = new Label();
            words.Name = "words"; words.UseMnemonic = false;
            words.Text = text; words.AutoSize = false; words.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            words.Font = new Font(Font.FontFamily, Font.Size + 1.5f); words.ForeColor = palette.Text; words.BackColor = palette.Input;
            words.Size = new Size(width, TextRenderer.MeasureText(text, words.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter).Height + Px(4));
            words.Location = new Point(Px(30), Px(40));
            window.Controls.Add(words);
            Button ok = Btn("OK");
            ok.Name = "ok"; ok.MinimumSize = new Size(Px(90), 0);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Left;      // (stays put when the window is sized below)
            ThemeOn(ok, palette);
            ok.DialogResult = DialogResult.OK;
            window.Controls.Add(ok);
            Size okSize = ok.PreferredSize;
            ok.Size = new Size(Math.Max(okSize.Width, Px(90)), okSize.Height);
            ok.Location = new Point(Px(30) + (width - ok.Width) / 2, words.Bottom + Px(22));
            window.ClientSize = new Size(width + Px(60), ok.Bottom + Px(24));
            window.AcceptButton = ok; window.CancelButton = ok;
            // The X, in the top right corner, as on "Powered by".
            Label x = new Label();
            x.AutoSize = false; x.Size = new Size(Px(34), Px(30)); x.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            x.ForeColor = palette.Text; x.BackColor = palette.Input; x.Cursor = Cursors.Hand;
            Font closeSymbols = new Font("Segoe MDL2 Assets", Font.Size + 1f);
            if (closeSymbols.Name == "Segoe MDL2 Assets") { x.Font = closeSymbols; x.Text = "\uE8BB"; }
            else { x.Font = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold); x.Text = "X"; }
            x.Name = "close";
            x.Location = new Point(window.ClientSize.Width - x.Width - Px(4), Px(4));
            x.MouseEnter += delegate { x.BackColor = Color.FromArgb(196, 43, 28); x.ForeColor = Color.White; };
            x.MouseLeave += delegate { x.BackColor = palette.Input; x.ForeColor = palette.Text; };
            x.Click += delegate { window.DialogResult = DialogResult.OK; window.Close(); };
            window.Controls.Add(x);
            x.BringToFront();
            window.Shown += delegate { ok.Focus(); };
            return window;
        }

        public static string ReaderExe { get { return Path.Combine(Path.Combine(App.Folder, "reader"), "ModernVoicesReader.exe"); } }
        void OpenReader()
        {
            string exe = ReaderExe;
            if (!File.Exists(exe)) { StatusLabel.Text = "The Reader is not installed (" + exe + " is missing)."; return; }
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(exe);
                start.UseShellExecute = false; start.WorkingDirectory = Path.GetDirectoryName(exe);
                using (Process.Start(start)) { }
            }
            catch (Exception e) { StatusLabel.Text = "Could not open the Reader: " + e.Message; }
        }

        // Shows one of the tabs' pages; its button stands out.
        public void ShowPage(int index)
        {
            PageIndex = index;
            // Apply changes and Done are for Add or remove voices: nothing else needs applying.
            if (DoneButton != null) { DoneButton.Visible = index == 3; ApplyButton.Visible = index == 3; }
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].Visible = i == index;
                Button b = TabButtons[i];
                b.Font = i == index ? boldFont : Font;
                if (palette != null)
                {
                    b.BackColor = i == index ? palette.Input : palette.Tab;
                    b.ForeColor = i == index ? palette.Text : palette.TabText;
                    b.FlatAppearance.BorderColor = i == index ? palette.TabOn : palette.TabEdge;
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
            // The column sorted by has a small arrow at the top of its heading, as Windows draws it: up for A
            // to Z, down for Z to A.
            RowSorter sorter;
            if (sorters.TryGetValue(view, out sorter) && sorter.Column == e.ColumnIndex)
            {
                float mid = e.Bounds.X + e.Bounds.Width / 2f, w = Math.Max(3, Px(4)), top = e.Bounds.Y + Math.Max(1, Px(1)), h = Math.Max(2, Px(3));
                PointF[] arrow = sorter.Down
                    ? new PointF[] { new PointF(mid - w, top), new PointF(mid + w, top), new PointF(mid, top + h) }
                    : new PointF[] { new PointF(mid - w, top + h), new PointF(mid + w, top + h), new PointF(mid, top) };
                System.Drawing.Drawing2D.SmoothingMode was = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush mark = new SolidBrush(palette.Muted)) { e.Graphics.FillPolygon(mark, arrow); }
                e.Graphics.SmoothingMode = was;
            }
            Rectangle inside = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, view.Font, inside, palette.Text, flags);
        }
        void DrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView view = (ListView)sender;
            ColumnHeader column = view.Columns[e.ColumnIndex];
            int x = e.Bounds.X;
            // (Windows gives the first column's cell as the whole row: once that column has been dragged
            // elsewhere, its place is after the columns now left of it.)
            if (e.ColumnIndex == 0 && column.DisplayIndex > 0)
            {
                x = e.Item.Bounds.X;
                foreach (ColumnHeader other in view.Columns) { if (other.DisplayIndex < column.DisplayIndex) { x += other.Width; } }
            }
            Rectangle cell = new Rectangle(x, e.Bounds.Y, column.Width, e.Bounds.Height);
            using (SolidBrush fill = new SolidBrush(e.Item.Selected ? palette.Selected : palette.Input)) { e.Graphics.FillRectangle(fill, cell); }
            int left = cell.X + 4;
            if (e.ColumnIndex == 0 && view.CheckBoxes)
            {
                int box = view.StateImageList != null ? view.StateImageList.ImageSize.Width : Px(16);
                Boxes.Draw(e.Graphics, new Rectangle(left, cell.Y + (cell.Height - box) / 2, box, box), e.Item.Checked);
                left += box + 4;
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
            foreach (string k in GapKeys)
            {
                Sliders[k].Value.Text = FormatGap(RoundedMs(Sliders[k].Bar.Value), k);
                Slider g = GlobalBars[k];
                int ms = RoundedMs(g.Bar.Value);
                g.Value.Text = FormatGap(ms, k);
                // (What is heard at the speed set; a program's own speed changes it again.)
                g.Example.Text = ms > 0 && SpeedBar.Value != 100 ? "At " + SpeedBar.Value + "% speed you hear about " + Math.Round(ms / speed) + " ms." : "";
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
                // (Written only once it differs from what is there: an untouched file keeps the default.)
                if (Math.Abs(Store.GetVariation(App.SettingsFile) - VariationBar.Value) > 0.01) { Store.SetVariation(App.SettingsFile, VariationBar.Value); }
                Store.SetGlobalPauses(App.SettingsFile, RoundedMs(GlobalBars["Sentence gap"].Bar.Value), RoundedMs(GlobalBars["Comma gap"].Bar.Value),
                    RoundedMs(GlobalBars["Paragraph gap"].Bar.Value));
            }
            catch (Exception e) { MessageBox.Show(this, "Could not save the speed and pauses: " + e.Message, App.Name); }
        }

        void ResetGlobalPauses()
        {
            SpeedBar.Value = Store.SpeedDefault;
            VariationBar.Value = Store.VariationDefault;
            GlobalBars["Sentence gap"].Bar.Value = Store.SentenceDefault;
            GlobalBars["Comma gap"].Bar.Value = Store.CommaDefault;
            GlobalBars["Paragraph gap"].Bar.Value = Store.ParagraphDefault;
        }

        public void UpdateWindowsRate()
        {
            int rate = Voices.WindowsRate;
            if (rate == 0) { RateWarn.Text = ""; RateReset.Visible = false; return; }
            RateWarn.Text = "Windows' own speech speed is set to " + (rate > 0 ? "+" : "") + rate + ", so in many programs these voices are " +
                (rate > 0 ? "faster" : "slower") + " than shown here.";
            RateReset.Visible = true;
        }

        // The selected voice's pauses: its own (ticked: its three sliders show), else those for every voice.
        public void UpdateOwnPauses()
        {
            bool own = OwnBox.Checked;
            foreach (string k in GapKeys) { Sliders[k].Bar.Enabled = CurrentInstalled != null && own; }
            OwnVariation.Bar.Enabled = CurrentInstalled != null && own;
            gapGrid.Visible = own;
            if (!Filling && CurrentInstalled != null) { saveTimer.Stop(); saveTimer.Start(); }
        }
        public bool OwnPausesShown { get { return gapGrid.Visible; } }
        // (For a picture of the override sliders: shown or hidden, nothing saved.)
        public void PeekOwnPauses(bool on) { gapGrid.Visible = on; }
        public string TipOf(Control c) { return tips.GetToolTip(c) ?? ""; }

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
            int order = 0;
            foreach (string key in keys)
            {
                Entry entry;
                ByKey.TryGetValue(key, out entry);
                string title = entry != null ? entry.Display + " (" + entry.Accent + ")" : key;
                List<string> names = speakersOf[key];
                string dir = Path.Combine(App.VoiceRoot, key);
                long size = FolderMb(dir);
                int rows = Math.Max(1, names.Count);
                for (int r = 0; r < rows; r++)
                {
                    string speaker = names.Count > 0 ? names[r] : null;
                    string id = Voices.IdOf(key, speaker);
                    string nick = Voices.Nickname(id);
                    bool isDefault = string.Equals(Voices.TokenName(id), defaultToken, StringComparison.OrdinalIgnoreCase);
                    string shown = entry != null ? Voices.DisplayName(entry, speaker) : key;
                    if (isDefault && defaultName.Length == 0) { defaultName = shown; }
                    InstalledRow row = new InstalledRow();
                    row.Key = key; row.Dir = dir; row.Title = title; row.Shared = names.Count > 1; row.Speaker = speaker; row.Entry = entry;
                    // (The accent has a column of its own: the Voice column has the voice's name only.)
                    row.Voice = entry != null ? entry.Display : key;
                    row.Nick = nick; row.Accent = entry != null ? entry.Accent : ""; row.Gender = Voices.Gender(id);
                    row.SizeMb = size; row.IsDefault = isDefault; row.First = r == 0; row.Order = order++;
                    ListViewItem item = new ListViewItem(row.Voice);
                    item.SubItems.Add(speaker ?? "n/a");
                    item.SubItems.Add(nick.Length > 0 ? "(" + nick + ")" : "");
                    item.SubItems.Add(row.Accent);
                    item.SubItems.Add(row.Gender);
                    item.SubItems.Add(size + " MB");
                    item.SubItems.Add(isDefault ? "yes" : "");
                    item.SubItems.Add(key);
                    item.Tag = row;
                    InstalledList.Items.Add(item);
                    allVoices.Add(shown);
                    languageOfVoice[shown] = PreviewTexts.LanguageOfKey(key);
                }
            }
            InstalledList.Sort();
            ShowInstalledCells();
            InstalledList.EndUpdate();
            // The Speed and pauses tab's Preview: any installed voice (Windows' default first, if it is one).
            PreviewVoiceBox.Items.Clear();
            foreach (string n in allVoices) { PreviewVoiceBox.Items.Add(n); }
            if (allVoices.Count > 0) { PreviewVoiceBox.SelectedIndex = Math.Max(0, allVoices.IndexOf(defaultName)); }
            GlobalPreview.Enabled = allVoices.Count > 0;
            // (With none of these voices installed it is one of Windows' own, whichever that is.)
            DefaultLabel.Text = defaultName.Length > 0 ? defaultName : InstalledList.Items.Count == 0 ? NoVoicesDefault
                : defaultToken.Length > 0 ? defaultToken + " (not one of these)" : "not set";
            if (InstalledList.Items.Count > 0) { InstalledList.Items[0].Selected = true; }
            else { SoundLabel.Text = "No voices installed yet: add some on the Add or remove voices tab."; }
            UpdateInstalled();
        }

        public const string NoVoicesDefault = "Default windows voice";

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
            OwnVariation.Bar.Enabled = false;
            PreviewButton.Enabled = on; ResetButton.Enabled = on; DefaultButton.Enabled = on;
            if (!on) { return; }
            InstalledRow row = (InstalledRow)InstalledList.SelectedItems[0].Tag;
            // The preview text, unless typed over, in the voice's language.
            if (PreviewTexts.IsOneOfThem(PreviewBox.Text))
            {
                string inLanguage = PreviewTexts.For(PreviewTexts.LanguageOfKey(row.Key));
                if (PreviewBox.Text != inLanguage) { PreviewBox.Text = inLanguage; }
            }
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
                // (Its own variation; with none set yet, the one for all voices.)
                double? ownVaried = Store.GetVoiceVariation(row.Dir);
                OwnVariation.Bar.Value = Clamp(Math.Round((ownVaried ?? Store.GetVariation(App.SettingsFile)) / 5.0) * 5, 0, Store.VariationLimit);
                OwnVariation.Value.Text = OwnVariation.Bar.Value + "%";
                Sliders["Volume"].Bar.Value = Clamp(Math.Round(peak * 10, MidpointRounding.AwayFromZero) * 10, 0, (int)(Store.MaxPeak * 100));
                Sliders["Boost"].Bar.Value = Clamp(Math.Round(boost * 10), 10, 40);
                Sliders["Speed"].Bar.Value = Clamp(Math.Round(100 / lengthScale), 25, 200);
                UpdateSliderLabels();
            }
            finally { Filling = false; }
            CurrentInstalled = row;
            foreach (string k in GapKeys) { Sliders[k].Bar.Enabled = OwnBox.Checked; }
            OwnVariation.Bar.Enabled = OwnBox.Checked;
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
                // (Its own variation is written once it overrides, or once one is there to keep in step.)
                if (OwnBox.Checked || Store.GetVoiceVariation(row.Dir).HasValue) { Store.SetVoiceVariation(row.Dir, OwnVariation.Bar.Value); }
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

        // The row of a voice (its id) selected again, after the list was made afresh or sorted: wherever it
        // is now (the first row when it is gone).
        public void SelectInstalledId(string id)
        {
            ListViewItem found = null;
            foreach (ListViewItem item in InstalledList.Items)
            {
                InstalledRow row = (InstalledRow)item.Tag;
                if (id != null && string.Equals(Voices.IdOf(row.Key, row.Speaker), id, StringComparison.OrdinalIgnoreCase)) { found = item; break; }
            }
            if (found == null && InstalledList.Items.Count > 0) { found = InstalledList.Items[0]; }
            if (found == null) { return; }
            found.Selected = true; found.Focused = true;
            found.EnsureVisible();
            UpdateInstalled();
        }
        string SelectedInstalledId()
        {
            if (InstalledList.SelectedItems.Count == 0) { return null; }
            InstalledRow row = (InstalledRow)InstalledList.SelectedItems[0].Tag;
            return Voices.IdOf(row.Key, row.Speaker);
        }

        // Makes the selected voice SAPI's default: what programs that don't choose a voice speak with (the
        // setting in sapi.cpl, Windows' Speech control panel).
        public void SetDefaultVoice()
        {
            CurrentVoice voice = GetCurrentVoice();
            if (voice == null) { return; }
            try { Voices.DefaultToken = voice.Token; }
            catch (Exception e) { MessageBox.Show(this, "Could not set the default voice: " + e.Message, App.Name); return; }
            string id = voice.Id;
            UpdateInstalledList();
            SelectInstalledId(id);
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
            UpdateInstalledList();
            SelectInstalledId(voice.Id);
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
            // The preview box's words; as given (not typed over), in the voice's own language.
            string language;
            languageOfVoice.TryGetValue(voiceName ?? "", out language);
            string text = !string.IsNullOrEmpty(say) ? say : !PreviewTexts.IsOneOfThem(PreviewBox.Text) ? PreviewBox.Text : PreviewTexts.For(language);
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
        // What a rule's "Said as" cell shows: nothing to say means it is not read at all.
        public const string NotRead = "(not read)";
        static string SaidAs(string to) { return string.IsNullOrEmpty(to) ? NotRead : to; }
        ListViewItem AddWordRow(Rule rule)
        {
            ListViewItem item = new ListViewItem(rule.From);
            item.SubItems.Add(SaidAs(rule.To));
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
                wordRules = Store.GetDictionary(App.DictionaryFor(wordFamily));
                WordListOnBox.Checked = !Store.DictionaryOff(App.DictionaryFor(wordFamily));
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
            // (A language's list with no rules and not switched off needs no file: none is left behind.)
            string file = App.DictionaryFor(wordFamily);
            try
            {
                if (wordFamily != "en" && wordRules.Count == 0 && WordListOnBox.Checked) { if (File.Exists(file)) { File.Delete(file); } }
                else { Store.SetDictionary(file, wordRules, !WordListOnBox.Checked); }
            }
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
        public void ClearWordFields()
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
                    target.Text = from; target.SubItems[1].Text = SaidAs(to); target.SubItems[2].Text = WordMatch(rule);
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
            foreach (ListViewItem item in VoiceItems) { if (item.Checked) { ticked.Add((Entry)item.Tag); } }
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
                if (AddList.SelectedItems.Count == 0)
                {
                    SpeakerLabel.Text = "Select a voice above to see its speakers.";
                    return;
                }
                bool voiceTicked = AddList.SelectedItems[0].Checked;
                bool onlyTicked = OnlyTickedBox.Checked;
                Entry v = (Entry)AddList.SelectedItems[0].Tag;
                Current = v;
                if (!v.Several)
                {
                    // Its one speaker, named by the catalogue or else by the voice; its box is the voice's tick.
                    string only = OnlySpeaker(v);
                    if (onlyTicked && !voiceTicked)
                    {
                        SpeakerLabel.Text = v.Display + " is not ticked.";
                        return;
                    }
                    SpeakerList.Items.Add(only, voiceTicked);
                    SpeakerList.Enabled = true;
                    SpeakerLabel.Text = "Click the name to hear it. Tick the box to install it.";
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
                if (onlyTicked)
                {
                    // Only the ticked ones. With none ticked, a ticked voice installs its first speaker: that one shows.
                    List<string> shown = new List<string>();
                    foreach (string n in names) { if (ticked.Contains(n)) { shown.Add(n); } }
                    if (shown.Count == 0 && voiceTicked && v.FirstSpeaker != null) { shown.Add(v.FirstSpeaker); }
                    if (shown.Count == 0)
                    {
                        SpeakerLabel.Text = "None of " + v.Display + "'s " + names.Count + " speakers are ticked.";
                        return;
                    }
                    foreach (string n in shown) { SpeakerList.Items.Add(n, true); }
                    SpeakerList.Enabled = true;
                    SpeakerLabel.Text = shown.Count + " of " + names.Count + " speakers ticked. Click a name to hear it.";
                    return;
                }
                foreach (string n in names) { SpeakerList.Items.Add(n, ticked.Contains(n)); }
                SpeakerList.Enabled = true;
                // (With none ticked, the first speaker is the one installed.)
                SpeakerLabel.Text = names.Count + " speakers. Click a name to hear it. Tick the ones to install: each becomes a voice of its own.";
            }
            finally
            {
                // (Columns as wide as the longest name shown needs, and no narrower than a short one.)
                int widest = Px(120);
                foreach (object shownName in SpeakerList.Items)
                {
                    widest = Math.Max(widest, TextRenderer.MeasureText(Convert.ToString(shownName), SpeakerList.Font).Width + SpeakerList.TextLeft + Px(14));
                }
                if (SpeakerList.ColumnWidth != widest) { SpeakerList.ColumnWidth = widest; }
                SpeakerList.EndUpdate();
                Filling = false;
            }
        }
        // The name of a one-speaker voice's speaker: the catalogue's, if it names one; else the voice's own.
        public static string OnlySpeaker(Entry v)
        {
            return v.FirstSpeaker ?? Catalogue.PrettyName(v.Name);
        }
        static string Padded(string name)
        {
            return Regex.Replace(name, @"\d+", delegate(Match m) { return m.Value.PadLeft(10, '0'); });
        }

        // A voice's name clicked in Add or remove voices: a one-speaker voice plays its sample, as its speaker's
        // name below would; one of several speakers plays nothing.
        public void VoiceNameClicked(ListViewItem item)
        {
            if (item == null || item.ListView != AddList || !item.Selected || ((Entry)item.Tag).Several) { return; }
            PlaySample();
        }
        // A voice of several speakers ticked or unticked by hand: all its speakers with it.
        public void AllSpeakers(ListViewItem item, bool on)
        {
            Entry v = (Entry)item.Tag;
            List<string> names = new List<string>(v.SpeakerNames);
            if (Current == v)
            {
                // (Shown below: their boxes, then kept as the list's ticks are.)
                if (OnlyTickedBox.Checked) { Speakers[v.Key] = on ? string.Join(", ", names.ToArray()) : ""; }
                else
                {
                    Filling = true;
                    try { for (int i = 0; i < SpeakerList.Items.Count; i++) { SpeakerList.SetItemChecked(i, on); } }
                    finally { Filling = false; }
                    SaveSpeakers();
                }
                BeginInvoke((MethodInvoker)delegate { Current = null; UpdateSelection(); });
            }
            else { Speakers[v.Key] = on ? string.Join(", ", names.ToArray()) : ""; }
        }
        // Whether the pointer is on a voice's box (Windows may tick it before the click is reported).
        bool PointerOnBox(ListViewItem item)
        {
            try
            {
                if (!AddList.IsHandleCreated) { return false; }
                ListViewHitTestInfo hit = AddList.HitTest(AddList.PointToClient(Cursor.Position));
                return hit.Item == item && hit.Location == ListViewHitTestLocations.StateImage;
            }
            catch (Exception) { return false; }
        }
        // A voice of more than two speakers ticked by hand: "Select all" ticks them all; "Choose" (or closing
        // the window) leaves the voice unticked and selected, its speakers below to tick one by one.
        public delegate bool AllSpeakersQuestion(Entry voice);
        public AllSpeakersQuestion AskAll;      // (the build's test answers it; else the window below)
        void AskAllSpeakers(ListViewItem item)
        {
            if (!item.Checked || item.ListView != AddList) { return; }
            Entry v = (Entry)item.Tag;
            bool all;
            if (AskAll != null) { all = AskAll(v); }
            else { using (Form ask = AllSpeakersWindow(v)) { all = ask.ShowDialog(this) == DialogResult.Yes; } }
            if (all) { AllSpeakers(item, true); return; }
            item.Checked = false;
            if (!item.Selected) { item.Selected = true; }
            item.EnsureVisible();
            BeginInvoke((MethodInvoker)delegate { if (SpeakerList.Enabled) { SpeakerList.Focus(); } });
        }
        public static string AllSpeakersWords(Entry v)
        {
            int n = v.SpeakerNames.Count;
            return "Ticking " + v.Display + " (" + v.Accent + ") installs all " + n + " of its speakers, each as a voice of its own: " + n +
                " more voices in every program's list, and programs can be slow to open with so many.\r\n\r\n" +
                "We recommend choosing the speakers you want from the list below instead.";
        }
        public Form AllSpeakersWindow(Entry v)
        {
            Form window = new Form();
            window.Text = App.Name; window.Font = Font;
            window.FormBorderStyle = FormBorderStyle.FixedDialog; window.MaximizeBox = false; window.MinimizeBox = false;
            window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.CenterParent;
            window.BackColor = palette.Back; window.ForeColor = palette.Text;
            int width = Px(420);
            Label words = new Label();
            words.Name = "words"; words.UseMnemonic = false; words.AutoSize = false;
            words.Text = AllSpeakersWords(v);
            words.ForeColor = palette.Text; words.BackColor = palette.Back;
            words.Font = Font;
            // (As tall as the label itself wraps its words: measured otherwise, it came out a line short.)
            words.Size = new Size(width, words.GetPreferredSize(new Size(width, 0)).Height + Px(4));
            words.Location = new Point(Px(20), Px(20));
            window.Controls.Add(words);
            Button all = Btn("Select all"), choose = Btn("Choose");
            all.Name = "all"; choose.Name = "choose";
            all.DialogResult = DialogResult.Yes; choose.DialogResult = DialogResult.No;
            foreach (Button b in new Button[] { all, choose })
            {
                ThemeOn(b, palette);
                window.Controls.Add(b);
                Size preferred = b.PreferredSize;
                b.Size = new Size(Math.Max(preferred.Width, Px(100)), preferred.Height);
            }
            // (Choose, the recommended one, on the right and the default: Enter and Esc both choose it.)
            choose.Location = new Point(Px(20) + width - choose.Width, words.Bottom + Px(18));
            all.Location = new Point(choose.Left - Px(10) - all.Width, choose.Top);
            window.AcceptButton = choose; window.CancelButton = choose;
            window.ClientSize = new Size(width + Px(40), choose.Bottom + Px(18));
            window.Shown += delegate { choose.Focus(); };
            // (A dark title bar when the window is dark, as the main window has.)
            window.HandleCreated += delegate
            {
                int on = palette.Dark ? 1 : 0;
                try { if (Native.DwmSetWindowAttribute(window.Handle, 20, ref on, 4) != 0) { Native.DwmSetWindowAttribute(window.Handle, 19, ref on, 4); } } catch (Exception) { }
            };
            return window;
        }

        // (For the build's test: a voice's box clicked by hand.)
        public void TickByHand(ListViewItem item, bool on)
        {
            tickByHand = true;
            try { item.Checked = on; } finally { tickByHand = false; }
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

        public bool NothingToApply(List<string> wanted, bool us)
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
            // (A sample still playing stops: Apply changes is pressed when the choosing is done.)
            Sample.Stop();
            SaveSpeakers();
            List<string> wanted = PickedIds();
            bool us = UsBox.Checked;
            if (NothingToApply(wanted, us)) { StatusLabel.Text = "Nothing to change."; return; }
            if (!Elevate()) { return; }
            string request = "apply " + (us ? "1" : "0") + " " + B64.To(string.Join(",", wanted.ToArray()));
            lock (applyLock) { ApplyLines.Clear(); }
            applyUsTags = us;
            applyHad = new List<string>(Installed);
            applyDefault = Voices.DefaultToken;
            ProgressSeen = 0;
            Busy = true;
            pageHost.Enabled = false; tabStrip.Enabled = false; ApplyButton.Enabled = false; DoneButton.Enabled = false; CloseButton.Enabled = false;
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
        // (Once a default voice is gone, Windows names one of its own as soon as anything speaks, as the
        // installer's test of the voices does: so what the default was is taken from before the changes.)
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
            bool removed = applyDefault.StartsWith("ModernVoices_", StringComparison.OrdinalIgnoreCase);
            foreach (string id in Installed)
            {
                if (string.Equals(Voices.TokenName(id), applyDefault, StringComparison.OrdinalIgnoreCase)) { removed = false; }
            }
            if (!added && !removed) { return; }
            try { Voices.DefaultToken = Voices.TokenName(Installed[0]); } catch (Exception) { }
        }

        // 0: done and every voice passed its test; 2: done, but a voice failed its test; else it stopped
        // ('stopped' says why).
        void ApplyFinished(int code, string stopped)
        {
            Busy = false;
            Progress.Visible = false;
            pageHost.Enabled = true; tabStrip.Enabled = true; ApplyButton.Enabled = true; DoneButton.Enabled = true; CloseButton.Enabled = true;
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
                foreach (ListViewItem item in VoiceItems)
                {
                    string key = ((Entry)item.Tag).Key;
                    bool isIn = false;
                    foreach (string id in Installed) { if (string.Equals(Voices.KeyOf(id), key, StringComparison.OrdinalIgnoreCase)) { isIn = true; break; } }
                    item.Checked = isIn;
                    item.Font = isIn ? listBold : listFont;
                }
            }
            finally { Filling = false; }
            // (A language listed only for its installed voices goes from the lists with the last of them.)
            int choicesBefore = VoiceFilterBox.Items.Count;
            FillLanguageChoices();
            if (VoiceFilterBox.Items.Count != choicesBefore) { LanguagesChanged(); }
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
        // Whether a sample is playing (until it ends, or is stopped).
        public static bool Playing { get { return player != null; } }
        // A sample that has played to its end is let go (the Play sample button says "Play sample" again).
        public static void Check()
        {
            if (player == null) { return; }
            try { if (Ended()) { Stop(); } } catch (Exception) { }
        }
        static bool Ended()
        {
            System.Windows.Media.MediaPlayer p = (System.Windows.Media.MediaPlayer)player;
            return p.NaturalDuration.HasTimeSpan && p.Position >= p.NaturalDuration.TimeSpan;
        }

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
            else if (parts.Length == 3 && parts[0] == "gender")
            {
                string id = B64.From(parts[1]), gender = B64.From(parts[2]);
                if (!GoodId.IsMatch(id) || (gender != "" && gender != "Female" && gender != "Male"))
                {
                    throw new ArgumentException("That is not a gender a voice can be given.");
                }
                Voices.SetGender(id, gender);
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
            Startup.Begin();
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
            // The busy pointer over the program until its window is up.
            Application.UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            List<Entry> catalogue = Catalogue.Load(out problem);
            Startup.Mark("voice list read");
            Languages.TakeInstallChoice();
            ConfigForm form = new ConfigForm(catalogue, Voices.InstalledIds(), Voices.UsEnglishTags);
            Startup.Mark("window built");
            if (problem != null) { form.StatusLabel.Text = problem; }
            form.Shown += delegate
            {
                Application.UseWaitCursor = false;
                Cursor.Current = Cursors.Default;
                Startup.Mark("window shown"); Startup.Save();
                // The first time: "Powered by", over the window, until it is clicked. Then, with no voice
                // installed (the window opens on Add or remove voices), where to start.
                bool welcome = !Welcome.Seen;
                if (welcome) { Welcome.Seen = true; }
                if (welcome || form.InstalledList.Items.Count == 0)
                {
                    form.BeginInvoke((MethodInvoker)delegate
                    {
                        if (welcome) { using (Form powered = form.PoweredByWindow()) { powered.ShowDialog(form); } }
                        if (form.InstalledList.Items.Count == 0 && !form.IsDisposed)
                        {
                            using (Form start = form.FirstVoicesWindow()) { start.ShowDialog(form); }
                        }
                    });
                }
            };
            Application.Run(form);
            GC.KeepAlive(running);
            return 0;
        }

        // How long starting took, step by step, kept in a small file (the last 30 starts): if the window is
        // ever slow to open, it says whether the wait was before this program ran at all (Windows checking
        // or loading it) or in a step of its own.
        public static class Startup
        {
            static readonly Stopwatch watch = new Stopwatch();
            static readonly StringBuilder steps = new StringBuilder();
            public static int BeforeMain = -1;      // ms from Windows starting the program to its first line
            public static string LogFile { get { return Path.Combine(Path.GetTempPath(), "ModernVoicesConfig-start.log"); } }

            public static void Begin()
            {
                watch.Start();
                try { BeforeMain = (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; } catch (Exception) { }
                steps.Append("Windows to program " + BeforeMain + " ms");
            }
            public static void Mark(string step)
            {
                steps.Append(", " + step + " at " + watch.ElapsedMilliseconds + " ms");
            }
            public static void Save()
            {
                try
                {
                    List<string> lines = new List<string>();
                    if (File.Exists(LogFile)) { lines.AddRange(File.ReadAllLines(LogFile)); }
                    lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + App.VersionText + "  " + steps);
                    if (lines.Count > 30) { lines.RemoveRange(0, lines.Count - 30); }
                    File.WriteAllLines(LogFile, lines.ToArray());
                }
                catch (Exception) { }
            }
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

        // A picture of a window that is not on the screen for long (shown, drawn, hidden).
        static void SavePicture(Form window, string file)
        {
            try
            {
                window.StartPosition = FormStartPosition.Manual; window.Location = new Point(40, 40);
                window.Show();
                Settle(200);
                using (Bitmap shot = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                    shot.Save(file, ImageFormat.Png);
                }
                window.Hide();
            }
            catch (Exception e) { Note("no picture " + Path.GetFileName(file) + ": " + e.Message); }
        }

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
            // (No additional languages to begin with, whatever the person at this PC has chosen: theirs is left alone.)
            Languages.Override = "";
            // (The lists as made, unsorted, in their first column order; the person's own arrangement is left alone.)
            ListLayout.Override = new Dictionary<string, string>();
            List<Entry> everything = Catalogue.Load(out problem);
            List<Entry> catalogue = everything.FindAll(delegate(Entry v) { return v.Family == "en"; });
            Check(catalogue.Count > 20 && everything.Count > catalogue.Count, "the catalogue is read: " + catalogue.Count + " English voices, " +
                (everything.Count - catalogue.Count) + " of other languages" + (problem == null ? "" : " (" + problem + ")"));
            List<string> real = Voices.InstalledIds();
            List<string> ids = real.Count > 0 ? real : new List<string>(new string[] { "en_GB-alba-medium", "en_GB-vctk-medium#p239" });
            // Opening with both lists sorted as left last time (0.18.2 failed so, as Windows made the list).
            if (real.Count > 1)
            {
                ListLayout.Override["VoiceSort"] = "On disk|down";
                ListLayout.Override["AddSort"] = "Download|up";
                ListLayout.Override["VoiceColumns"] = "Nickname|Voice|Speaker|Accent|Gender|On disk|Windows default|Folder";
                using (ConfigForm sortedFirst = new ConfigForm(everything, ids, Voices.UsEnglishTags))
                {
                    sortedFirst.Show();
                    Settle(300);
                    IntPtr listMade = sortedFirst.AddList.Handle;
                    Settle(200);
                    bool byDisk = Sorted(sortedFirst.InstalledList, ConfigForm.SizeColumn, true), byDownload = AddSorted(sortedFirst.AddList, false);
                    bool firstSelected = sortedFirst.InstalledList.Items.Count > 0 && sortedFirst.InstalledList.Items[0].Selected;
                    bool nickFirst = sortedFirst.InstalledList.Columns[ConfigForm.NickColumn].DisplayIndex == 0;
                    Check(byDisk && byDownload && firstSelected && nickFirst, "opened with the lists sorted and the columns moved as left last time: the Voice manager by On disk, Z to A (" +
                        byDisk + "), its first row selected (" + firstSelected + "), Nickname first (" + nickFirst + "); Add or remove voices by Download (" + byDownload + ")");
                    sortedFirst.StopTimers();
                    sortedFirst.Close();
                }
                ListLayout.Override.Clear();
                Settle(100);
            }
            DateTime started = DateTime.UtcNow;
            ConfigForm ui = new ConfigForm(everything, ids, Voices.UsEnglishTags);
            Check(ui.InstalledList.Items.Count > 0 ? ui.DefaultLabel.Text != ConfigForm.NoVoicesDefault : ui.DefaultLabel.Text == ConfigForm.NoVoicesDefault,
                "the default voice box says \"" + ConfigForm.NoVoicesDefault + "\" only while none of these voices is installed (" + ui.DefaultLabel.Text + ")");
            Note("the window was built in " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms; Windows took " + Program.Startup.BeforeMain +
                " ms to start the program");
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
            Settle(50);

            // "Only show selected speakers": off as the window opens; on, only VCTK's ticked speakers show (its
            // first, when none is ticked and the voice is); off again, all of them.
            Check(!ui.OnlyTickedBox.Checked, "\"Only show selected speakers\" is off as the window opens");
            List<string> tickedVctk = TickedSpeakers(ui);
            int expected = tickedVctk.Count > 0 ? tickedVctk.Count : (vctk.Checked ? 1 : 0);
            ui.OnlyTickedBox.Checked = true;
            Settle(50);
            Note("only selected: " + ui.SpeakerList.Items.Count + " shown, " + ui.SpeakerList.CheckedItems.Count + " ticked; " + ui.SpeakerLabel.Text);
            Check(ui.SpeakerList.Items.Count == expected && ui.SpeakerList.CheckedItems.Count == expected,
                "\"Only show selected speakers\" on: only the ticked speakers are listed (" + expected + ")");
            List<string> pickedFiltered = ui.PickedIds();
            ui.OnlyTickedBox.Checked = false;
            Settle(50);
            Check(ui.SpeakerList.Items.Count == vctkEntry.SpeakerNames.Count && string.Join(" ", ui.PickedIds().ToArray()) == string.Join(" ", pickedFiltered.ToArray()),
                "and off again: all " + vctkEntry.SpeakerNames.Count + " are listed, with the same ones picked");

            // VCTK's own box ticked by hand: all its speakers ticked; unticked by hand, none. Its name clicked:
            // no sample (its speakers are clicked to hear them). Then as it was.
            {
                bool vctkWas = vctk.Checked;
                string speakersWas = ui.Speakers.ContainsKey(vctkEntry.Key) ? ui.Speakers[vctkEntry.Key] : null;
                List<string> pickedWas = ui.PickedIds();
                Sample.Stop();
                ui.VoiceNameClicked(vctk);
                bool vctkSilent = !Sample.Playing;
                if (vctk.Checked) { ui.TickByHand(vctk, false); Settle(50); }
                // Asked first (more than two speakers): "Choose" leaves it unticked, nothing ticked.
                string asked = null;
                ui.AskAll = delegate(Entry v) { asked = v.Key; return false; };
                ui.TickByHand(vctk, true);
                Settle(100);
                bool chose = asked == vctkEntry.Key && !vctk.Checked && TickedSpeakers(ui).Count == 0 && vctk.Selected;
                Check(chose, "VCTK's box ticked by hand asks first (" + (asked ?? "not asked") + "); \"Choose\" leaves it unticked, selected, no speaker ticked");
                using (Form ask = ui.AllSpeakersWindow(vctkEntry))
                {
                    Control askAll = ask.Controls["all"], askChoose = ask.Controls["choose"];
                    Check(askAll != null && askAll.Text == "Select all" && askChoose != null && askChoose.Text == "Choose" && ask.AcceptButton == askChoose &&
                        ask.CancelButton == askChoose && ask.Controls["words"].Text.Contains("all " + vctkEntry.SpeakerNames.Count + " of its speakers") &&
                        askChoose.Top >= ask.Controls["words"].Bottom && ask.Controls["words"].Height >= ask.Controls["words"].GetPreferredSize(new Size(ask.Controls["words"].Width, 0)).Height,
                        "its question: \"" + ConfigForm.AllSpeakersWords(vctkEntry).Replace("\r\n\r\n", " ") + "\" with Select all and Choose (Enter and Esc: Choose)");
                    if (pictures)
                    {
                        ask.Show(); Settle(200);
                        using (Bitmap shot = new Bitmap(ask.Width, ask.Height))
                        {
                            ask.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                            shot.Save(Path.Combine(folder, "all-speakers-question.png"), ImageFormat.Png);
                        }
                        ask.Close();
                    }
                }
                // "Select all": all of them.
                ui.AskAll = delegate(Entry v) { return true; };
                ui.TickByHand(vctk, true);
                Settle(150);
                int allTicked = TickedSpeakers(ui).Count, allPicked = 0;
                foreach (string id in ui.PickedIds()) { if (id.StartsWith("en_GB-vctk-medium#", StringComparison.Ordinal)) { allPicked++; } }
                ui.TickByHand(vctk, false);
                Settle(100);
                int noneTicked = TickedSpeakers(ui).Count, nonePicked = 0;
                foreach (string id in ui.PickedIds()) { if (id.StartsWith("en_GB-vctk-medium#", StringComparison.Ordinal)) { nonePicked++; } }
                Check(vctkSilent && allTicked == vctkEntry.SpeakerNames.Count && allPicked == vctkEntry.SpeakerNames.Count && noneTicked == 0 && nonePicked == 0,
                    "VCTK's box ticked by hand ticks all " + allTicked + " of its speakers (" + allPicked + " picked), unticked none (" + noneTicked +
                    "); its name clicked plays nothing (" + vctkSilent + ")");
                ui.AskAll = null;
                if (speakersWas == null) { ui.Speakers.Remove(vctkEntry.Key); } else { ui.Speakers[vctkEntry.Key] = speakersWas; }
                ui.Current = null;
                vctk.Checked = vctkWas;
                ui.UpdateSelection();
                Settle(50);
                Check(string.Join(" ", ui.PickedIds().ToArray()) == string.Join(" ", pickedWas.ToArray()), "and put back as it was (a tick not by hand ticks no speakers)");
            }

            // A voice with one speaker: its name is listed, its box is the voice's own tick (both ways), and
            // nothing goes into the speakers ticked.
            ListViewItem single = null;
            foreach (ListViewItem item in ui.AddList.Items) { if (!((Entry)item.Tag).Several) { single = item; break; } }
            Check(single != null, "the catalogue has a voice with one speaker");
            if (single != null)
            {
                Entry singleEntry = (Entry)single.Tag;
                bool was = single.Checked;
                single.Selected = true;
                ui.UpdateSelection();
                Settle(50);
                Note("one speaker: " + singleEntry.Key + " lists " + (ui.SpeakerList.Items.Count > 0 ? Convert.ToString(ui.SpeakerList.Items[0]) : "nothing") + "; " + ui.SpeakerLabel.Text);
                Check(ui.SpeakerList.Items.Count == 1 && Convert.ToString(ui.SpeakerList.Items[0]) == ConfigForm.OnlySpeaker(singleEntry) &&
                    ui.SpeakerList.GetItemChecked(0) == was && ui.SpeakerList.Enabled, "a voice with one speaker lists its speaker, ticked as the voice is");
                Sample.Stop();
                ui.VoiceNameClicked(single);
                bool singlePlays = Sample.Playing;
                Sample.Stop();
                Check(singlePlays, "its name clicked plays its sample, as its speaker's name below does");
                ui.SpeakerList.SetItemChecked(0, !was);
                Settle(50);
                Check(single.Checked == !was && ui.SpeakerList.Items.Count == 1 && ui.SpeakerList.GetItemChecked(0) == !was && !ui.Speakers.ContainsKey(singleEntry.Key),
                    "ticking its speaker's box ticks the voice (and keeps no speaker list for it)");
                single.Checked = was;
                Settle(50);
                Check(ui.SpeakerList.Items.Count == 1 && ui.SpeakerList.GetItemChecked(0) == was, "and the voice's own tick is shown in the speaker's box");
                ui.OnlyTickedBox.Checked = true;
                Settle(50);
                Check(ui.SpeakerList.Items.Count == (was ? 1 : 0), "with \"Only show selected speakers\" on, it shows only if the voice is ticked");
                ui.OnlyTickedBox.Checked = false;
                Settle(50);
            }

            // The "Show" box: all voices as the window opens; American only lists those, British only those, and
            // a voice not shown keeps its tick (what would be installed is the same throughout). Done is on this
            // tab only and goes back to the Voice manager.
            {
                int showAmerican = 0, showBritish = 0;
                foreach (Entry v in catalogue) { if (v.Code == "en_US") { showAmerican++; } else if (v.Code == "en_GB") { showBritish++; } }
                Check(ui.VoiceFilterBox.SelectedIndex == 0 && ui.AddList.Items.Count == catalogue.Count, "\"Show\" is on All voices as the window opens");
                ListViewItem showGb = null;
                foreach (ListViewItem item in ui.VoiceItems) { if (((Entry)item.Tag).Code == "en_GB" && !((Entry)item.Tag).Several && !item.Checked) { showGb = item; break; } }
                Check(showGb != null, "the catalogue has a British voice not ticked");
                if (showGb != null) { showGb.Checked = true; Settle(50); }
                string showBefore = string.Join(" ", ui.PickedIds().ToArray());
                ui.VoiceFilterBox.SelectedIndex = 2;
                Settle(50);
                bool showAllAmerican = true;
                foreach (ListViewItem item in ui.AddList.Items) { if (((Entry)item.Tag).Code != "en_US") { showAllAmerican = false; } }
                Note("show American only: " + ui.AddList.Items.Count + " of " + catalogue.Count + " listed; " + ui.SizeLabel.Text);
                Check(showAmerican > 0 && ui.AddList.Items.Count == showAmerican && showAllAmerican, "American only: the " + showAmerican + " American voices are listed");
                Check(string.Join(" ", ui.PickedIds().ToArray()) == showBefore && (showGb == null || showGb.Checked), "voices not shown keep their ticks");
                ui.VoiceFilterBox.SelectedIndex = 3;
                Settle(50);
                Check(ui.AddList.Items.Count == showBritish && (showGb == null || (showGb.ListView == ui.AddList && showGb.Checked)) && string.Join(" ", ui.PickedIds().ToArray()) == showBefore,
                    "British only: the " + showBritish + " British voices, ticked as before");
                ui.VoiceFilterBox.SelectedIndex = 1;
                Settle(50);
                Check(ui.AddList.Items.Count == catalogue.Count, "English only: every voice (all are English for now)");
                ui.VoiceFilterBox.SelectedIndex = 0;
                Settle(50);
                // (The voices listed: the English ones, which come first of them all.)
                bool showSameOrder = ui.AddList.Items.Count == catalogue.Count;
                for (int i = 0; showSameOrder && i < catalogue.Count; i++) { if (ui.AddList.Items[i] != ui.VoiceItems[i]) { showSameOrder = false; } }
                Check(showSameOrder && string.Join(" ", ui.PickedIds().ToArray()) == showBefore, "All voices again: the whole list in its order, ticked as before");
                if (showGb != null) { showGb.Checked = false; Settle(50); }

                int showPage = ui.PageIndex;
                ui.ShowPage(3);
                Settle(50);
                bool showOnAdd = ui.DoneButton.Visible;
                ui.DoneButton.PerformClick();
                Settle(50);
                Check(showOnAdd && ui.PageIndex == 0 && !ui.DoneButton.Visible, "Done shows on Add or remove voices only, and opens the Voice manager");
                ui.ShowPage(showPage);
                Settle(50);
            }

            // Open Reader is left of Close; Apply changes shows on Add or remove voices only; the speaker mutes and
            // unmutes; the preview box lines up with the sliders. The header: the Voice manager's list starts
            // right under it.
            {
                int readerRight = ui.ReaderButton.PointToScreen(new Point(ui.ReaderButton.Width, 0)).X;
                int closeLeft = ui.CloseButton.PointToScreen(Point.Empty).X;
                int readerTop = ui.ReaderButton.PointToScreen(Point.Empty).Y, closeTop = ui.CloseButton.PointToScreen(Point.Empty).Y;
                Check(ui.ReaderButton.Visible && readerRight <= closeLeft && closeLeft - readerRight < 20 && readerTop == closeTop, "Open Reader is just left of Close");
                int applyPage = ui.PageIndex;
                bool[] applyShown = new bool[4];
                for (int applyTab = 0; applyTab < 4; applyTab++) { ui.ShowPage(applyTab); Settle(20); applyShown[applyTab] = ui.ApplyButton.Visible; }
                ui.ShowPage(applyPage);
                Settle(20);
                Check(!applyShown[0] && !applyShown[1] && !applyShown[2] && applyShown[3], "Apply changes shows on Add or remove voices only");
                int volumeWas = ui.OutputBar.Value;
                bool settingsHad = File.Exists(App.SettingsFile);
                ui.OutputBar.Value = 70;
                ui.ToggleMute();
                bool muted = ui.OutputBar.Value == 0 && ui.MuteIcon.Text != "\uE767";
                ui.ToggleMute();
                Check(muted && ui.OutputBar.Value == 70, "the speaker mutes the output volume, and clicked again puts it back (70%)");
                ui.OutputBar.Value = volumeWas;
                // (Nothing saved by it: a settings file the test made would hide the installer's defaults.)
                ui.StopTimers();
                if (!settingsHad && File.Exists(App.SettingsFile)) { File.Delete(App.SettingsFile); }
                ui.ShowPage(0);
                Settle(100);
                // The sections stand in the middle: the sliders' names start where the preview box does, and the
                // sliders end where the output volume slider does; each section has a frame around it.
                // (The box's outer edge: PointToScreen(Point.Empty) is inside its border.)
                int boxLeft = ui.PreviewBox.Parent.PointToScreen(ui.PreviewBox.Location).X;
                int nameLeft = ui.Sliders["Volume"].Name.PointToScreen(Point.Empty).X;
                int tickLeft = ui.OwnBox.PointToScreen(Point.Empty).X;
                TrackBar volumeBar = ui.Sliders["Volume"].Bar;
                int sliderRight = volumeBar.PointToScreen(new Point(volumeBar.Width, 0)).X;
                int outputRight = ui.OutputBar.PointToScreen(new Point(ui.OutputBar.Width, 0)).X;
                Note("preview box starts at " + boxLeft + ", the sliders' names at " + nameLeft + ", the override box at " + tickLeft +
                    "; the sliders end at " + sliderRight + ", the output volume at " + outputRight);
                Check(Math.Abs(boxLeft - ui.NameInset - nameLeft) <= 1 && Math.Abs(boxLeft - tickLeft) <= 1 && ui.NameInset >= 1 && ui.NameInset <= 8,
                    "the sliders' names (their words, " + ui.NameInset + " px inside their labels) and the override box start where the preview box does");
                Check(Math.Abs(sliderRight - outputRight) <= 1, "the sliders end where the output volume slider ends");
                Check(volumeBar.Width > 200, "the sliders have room (" + volumeBar.Width + " px)");
                // Each frame's line is as far from the words inside it on its right (the end of the widest
                // value) as on its left (the sliders' names): twice what it was in 0.16.
                int frameLeft = ui.SoundFrame.PointToScreen(Point.Empty).X, frameRight = frameLeft + ui.SoundFrame.Width;
                int leftGap = nameLeft + ui.NameInset - frameLeft;
                Label volumeValue = ui.Sliders["Volume"].Value;
                int valueEnd = volumeValue.PointToScreen(Point.Empty).X + ui.NameInset +
                    TextRenderer.MeasureText(ui.WidestValue, volumeValue.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                int rightGap = frameRight - valueEnd;
                int pauseLeft = ui.PauseFrame.PointToScreen(Point.Empty).X;
                Note("the frames' gaps: " + leftGap + " px on the left, " + rightGap + " px on the right (after \"" + ui.WidestValue + "\"); meant to be " + ui.FrameGap);
                // (Since 0.18.4 the lines are in the middle of the page, the words inside where they were.)
                Control pageGrid = ui.SoundFrame.Parent;
                int pageLeft = pageGrid.PointToScreen(Point.Empty).X, pageRight = pageLeft + pageGrid.ClientSize.Width;
                int boxLeft2 = ui.DefaultBox.PointToScreen(Point.Empty).X;
                Check(Math.Abs((frameLeft - pageLeft) - (pageRight - frameRight)) <= 1 && pauseLeft == frameLeft && ui.PauseFrame.Width == ui.SoundFrame.Width &&
                    leftGap >= ui.FrameGap && rightGap > 0,
                    "the frames' lines are in the middle of the page (" + (frameLeft - pageLeft) + " px from its left, " + (pageRight - frameRight) + " px from its right), the words inside them " +
                    leftGap + " and " + rightGap + " px from them");
                Check(boxLeft2 == frameLeft && ui.MuteIcon.PointToScreen(Point.Empty).X > boxLeft2 + ui.DefaultBox.Width,
                    "the default voice's box starts where the frames' lines do (" + boxLeft2 + " and " + frameLeft + "), the speaker after it");
                Check(ui.SoundFrame.Contains(volumeBar) && ui.SoundFrame.Contains(ui.ResetButton) && ui.PauseFrame.Contains(ui.OwnBox) &&
                    ui.PauseFrame.Contains(ui.OwnVariation.Bar) && ui.PauseFrame.Top - ui.SoundFrame.Bottom >= 10,
                    "\"How it sounds\" and the override section each have a frame, with a gap between them");
                Check(ui.TipOf(volumeBar).Length > 0 && ui.TipOf(volumeBar) == ui.TipOf(ui.Sliders["Volume"].Name) && ui.TipOf(ui.OwnVariation.Name).Length > 0 &&
                    ui.SoundFrame.Controls.Count == 2, "a slider's description shows when the pointer rests on it or its name (none under the sliders)");
                ui.ShowPage(1);
                Settle(100);
                int speedName = ui.GlobalBars["Sentence gap"].Name.PointToScreen(Point.Empty).X;
                int speedRight = ui.SpeedBar.PointToScreen(new Point(ui.SpeedBar.Width, 0)).X;
                int variationRight = ui.VariationBar.PointToScreen(new Point(ui.VariationBar.Width, 0)).X;
                Note("Speed and pauses: names at " + speedName + ", sliders end at " + speedRight + " and " + variationRight);
                Check(Math.Abs(speedName - nameLeft) <= 1 && Math.Abs(speedRight - variationRight) <= 1 && Math.Abs(speedRight - sliderRight) <= SystemInformation.VerticalScrollBarWidth + 1,
                    "Speed and pauses: its sliders stand in the same columns");
                ui.ShowPage(0);
                Settle(50);
                ui.ShowPage(applyPage);
                Settle(20);
                int headerPage = ui.PageIndex;
                ui.ShowPage(0);
                Settle(50);
                int lineBottom = ui.HeaderLineBottom;
                int listTop = ui.InstalledList.Parent.PointToScreen(ui.InstalledList.Location).Y;      // (its outer edge)
                int pageTop = ui.Pages[0].PointToScreen(Point.Empty).Y;
                Note("header line ends at " + lineBottom + "; the Voice manager's page starts at " + pageTop + ", its list at " + listTop);
                Check(lineBottom == pageTop && listTop == pageTop, "the Voice manager's list starts right under the header's line, where its scroll bar does");
                ui.ShowPage(headerPage);
                Settle(50);
            }

            // Add or remove voices: Speakers is the second column; Play sample stops a sample playing.
            Check(ui.AddList.Columns[1].Text == "Speakers" && ui.AddList.Columns[2].Text == "Accent or language", "Speakers is the second column, before the accent");
            {
                string fakeSample = Path.Combine(Path.GetTempPath(), "modernvoices_selftest_silence.wav");
                try
                {
                    // Two seconds of silence, as a sample to play.
                    using (FileStream wav = File.Create(fakeSample))
                    using (BinaryWriter w = new BinaryWriter(wav))
                    {
                        int bytes = 22050 * 2 * 2;
                        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
                        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes); w.Write(new byte[bytes]);
                    }
                    int samplePage = ui.PageIndex;
                    ui.ShowPage(3);       // (a button clicks only while it is shown)
                    Sample.Play(fakeSample);
                    Settle(300);
                    bool saysStop = ui.PlayButton.Text == "Stop sample";
                    ui.PlayButton.PerformClick();
                    Settle(300);
                    Note("Play sample: said \"" + (saysStop ? "Stop sample" : "Play sample") + "\" while playing; now \"" + ui.PlayButton.Text + "\"");
                    Check(saysStop && !Sample.Playing && ui.PlayButton.Text == "Play sample", "Play sample says Stop sample while one plays, and pressed then, stops it");
                    // Apply changes stops it too (with nothing to change, it does nothing else).
                    Sample.Play(fakeSample);
                    Settle(300);
                    bool playedAgain = Sample.Playing;
                    bool nothing = ui.NothingToApply(ui.PickedIds(), ui.UsBox.Checked);
                    if (nothing) { ui.Apply(); }
                    Settle(100);
                    Note("Apply changes while a sample played: " + (nothing ? (Sample.Playing ? "still playing" : "stopped") : "not tried (there were changes to apply)"));
                    Check(!nothing || (playedAgain && !Sample.Playing), "Apply changes stops a sample that is playing");
                    ui.ShowPage(samplePage);
                    Settle(50);
                }
                finally { Sample.Stop(); try { File.Delete(fakeSample); } catch (Exception) { } }
            }

            // Additional languages: none to begin with (English voices only); one chosen, its voices are listed
            // after the English ones and it has a choice in "Show" and in the Pronunciation tab's "Rules for";
            // its rules are its own file, which can be switched off as a whole; unchosen, all as before.
            {
                List<KeyValuePair<string, string>> offeredLanguages = ui.OtherLanguages();
                string germanName = null;
                foreach (KeyValuePair<string, string> offeredLanguage in offeredLanguages) { if (offeredLanguage.Key == "de") { germanName = offeredLanguage.Value; } }
                Note("other languages offered: " + offeredLanguages.Count + (germanName == null ? " (no German)" : ""));
                Check(offeredLanguages.Count >= 10 && germanName != null, "the catalogue's other languages are offered (" + offeredLanguages.Count + ")");
                bool noneBadly = true;
                foreach (Entry v in everything) { if (v.Family == "ar" || v.Family == "fa" || v.Family == "zh" || v.Family == "he") { noneBadly = false; } }
                Check(noneBadly, "languages written right to left or without spaces are not offered");
                Check(ui.AddList.Items.Count == catalogue.Count && ui.VoiceFilterBox.Items.Count == 4 && ui.WordLanguageBox.Items.Count == 1,
                    "with no additional language chosen, only English voices and choices are listed");
                CheckedListBox languageBoxes;
                List<string> languageFamilies;
                using (Form languageDialog = ui.LanguagesDialog(out languageBoxes, out languageFamilies))
                {
                    Check(languageBoxes.Items.Count == offeredLanguages.Count + 1 && languageBoxes.CheckedItems.Count == 1 && languageBoxes.GetItemChecked(0) &&
                        languageFamilies[0] == "en", "the Additional languages window lists English (ticked) and the others (not)");
                    if (pictures)
                    {
                        languageDialog.StartPosition = FormStartPosition.Manual; languageDialog.Location = new Point(40, 40);
                        languageDialog.Show();
                        Settle(200);
                        try
                        {
                            using (Bitmap shot = new Bitmap(languageDialog.Width, languageDialog.Height))
                            {
                                languageDialog.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                                shot.Save(Path.Combine(folder, "languages.png"), ImageFormat.Png);
                            }
                        }
                        catch (Exception pictureError) { Note("no picture of the languages window: " + pictureError.Message); }
                        languageDialog.Hide();
                    }
                }
                if (germanName != null)
                {
                    int germanVoices = 0;
                    foreach (Entry v in everything) { if (v.Family == "de") { germanVoices++; } }
                    ui.SetLanguages(new List<string>(new string[] { "de" }));
                    Settle(50);
                    LanguageChoice lastShow = ui.VoiceFilterBox.Items[ui.VoiceFilterBox.Items.Count - 1] as LanguageChoice;
                    Note("German chosen: " + ui.AddList.Items.Count + " voices listed (" + germanVoices + " German); Show has \"" + (lastShow == null ? "?" : lastShow.Text) + "\"");
                    Check(ui.AddList.Items.Count == catalogue.Count + germanVoices && ui.VoiceFilterBox.Items.Count == 5 && lastShow != null && lastShow.Text == germanName + " only",
                        "German chosen: its " + germanVoices + " voices are listed, and Show has \"" + germanName + " only\"");
                    Check(((Entry)ui.AddList.Items[0].Tag).Family == "en" && ((Entry)ui.AddList.Items[ui.AddList.Items.Count - 1].Tag).Family == "de", "the English voices come first");
                    ui.VoiceFilterBox.SelectedIndex = 4;
                    Settle(50);
                    Check(ui.AddList.Items.Count == germanVoices, "\"" + germanName + " only\" lists just those");
                    if (pictures) { ui.ShowPage(3); Settle(100); ui.Picture(Path.Combine(folder, "german.png")); }
                    ui.VoiceFilterBox.SelectedIndex = 0;
                    Settle(50);

                    // Its rules: its own file, apart from the English one.
                    string germanFile = App.DictionaryFor("de");
                    bool hadGermanFile = File.Exists(germanFile);
                    string keptGerman = hadGermanFile ? File.ReadAllText(germanFile) : null;
                    try
                    {
                        if (hadGermanFile) { File.Delete(germanFile); }
                        int englishRules = ui.WordList.Items.Count;
                        string englishText = File.Exists(App.DictionaryFile) ? File.ReadAllText(App.DictionaryFile) : "";
                        Check(ui.WordLanguageBox.Items.Count == 2, "the Pronunciation tab has rules for English and " + germanName);
                        ui.WordLanguageBox.SelectedIndex = 1;
                        Settle(50);
                        Check(ui.WordList.Items.Count == 0 && ui.WordListOnBox.Checked && !File.Exists(germanFile), germanName + "'s rules start empty, in use, with no file");
                        Store.SetDictionary(germanFile, new Rule[] { new Rule { On = true, Whole = true, Case = false, From = "zB", To = "zum Beispiel" } });
                        ui.UpdateWordList();
                        Settle(50);
                        Check(ui.WordList.Items.Count == 1 && Path.GetFileName(germanFile) == "dictionary-de.txt", "a " + germanName + " rule is listed from dictionary-de.txt");
                        ui.WordListOnBox.Checked = false;
                        Settle(50);
                        Check(Store.DictionaryOff(germanFile) && Store.GetDictionary(germanFile).Count == 1, "\"Use these rules\" unticked: the list is switched off, its rule kept");
                        ui.WordListOnBox.Checked = true;
                        Settle(50);
                        Check(!Store.DictionaryOff(germanFile) && Store.GetDictionary(germanFile).Count == 1, "ticked again: in use");
                        ui.WordLanguageBox.SelectedIndex = 0;
                        Settle(50);
                        Check(ui.WordList.Items.Count == englishRules && (File.Exists(App.DictionaryFile) ? File.ReadAllText(App.DictionaryFile) : "") == englishText,
                            "the English rules are listed again, untouched");
                    }
                    finally
                    {
                        try { if (keptGerman != null) { File.WriteAllText(germanFile, keptGerman); } else if (File.Exists(germanFile)) { File.Delete(germanFile); } } catch (Exception) { }
                    }
                    ui.SetLanguages(new List<string>());
                    Settle(50);
                    Check(ui.AddList.Items.Count == catalogue.Count && ui.VoiceFilterBox.Items.Count == 4 && ui.WordLanguageBox.Items.Count == 1,
                        "German unchosen: the lists are as they were");

                    // English switched off (the installer's language choice does it, or the Additional languages
                    // window): only the other language is listed, unless English has a voice ticked, or nothing
                    // else is listed.
                    bool englishTicked = false;
                    foreach (ListViewItem voiceRow in ui.VoiceItems) { if (voiceRow.Checked && ((Entry)voiceRow.Tag).Family == "en") { englishTicked = true; } }
                    ui.SetLanguages(new List<string>(new string[] { "de" }), false);
                    Settle(50);
                    LanguageChoice rulesNow = ui.WordLanguageBox.SelectedItem as LanguageChoice;
                    Note("English switched off, German on: " + ui.AddList.Items.Count + " voices listed, " + ui.VoiceFilterBox.Items.Count + " choices in Show, rules for " +
                        (rulesNow == null ? "?" : rulesNow.Text) + (englishTicked ? " (English has voices ticked: it stays)" : ""));
                    if (englishTicked)
                    {
                        Check(ui.LanguageShown("en") && ui.AddList.Items.Count == catalogue.Count + germanVoices, "English switched off with English voices ticked: they stay listed");
                    }
                    else
                    {
                        Check(!ui.LanguageShown("en") && ui.AddList.Items.Count == germanVoices && ui.VoiceFilterBox.Items.Count == 2 &&
                            ui.WordLanguageBox.Items.Count == 1 && rulesNow != null && rulesNow.Family == "de", "English switched off: only " + germanName + "'s voices, choice and rules are listed");
                    }
                    ui.SetLanguages(new List<string>(), false);
                    Settle(50);
                    Check(ui.LanguageShown("en") && ui.AddList.Items.Count == catalogue.Count, "English switched off and nothing else chosen: English is listed all the same");
                    ui.SetLanguages(new List<string>(), true);
                    Settle(50);
                    LanguageChoice rulesBack = ui.WordLanguageBox.SelectedItem as LanguageChoice;
                    Check(ui.VoiceFilterBox.Items.Count == 4 && rulesBack != null && rulesBack.Family == "en", "English switched back on: the lists are as they were");
                    // The installer's language choice, as the window takes it up.
                    bool british = Languages.Apply("en_GB") && Languages.English && Languages.Show == Languages.BritishOnly;
                    bool american = Languages.Apply("en_US") && Languages.English && Languages.Show == Languages.AmericanOnly;
                    bool german = Languages.Apply("de") && !Languages.English && Languages.Show == "" && Languages.Enabled.Contains("de");
                    bool english = Languages.Apply("en") && Languages.English && Languages.Show == "";
                    bool unknown = !Languages.Apply("zh") && !Languages.Apply("") && Languages.English;
                    Check(british && american && german && english && unknown, "the installer's language choice: British or American sets Show, another language is listed without English");
                    Languages.Override = "";
                    Languages.English = true; Languages.Show = "";
                    // A window opened with no voice installed, after another language was chosen at install: only
                    // that language is listed, its rules are the ones shown, and the default voice box says so.
                    Languages.Override = "de";
                    Languages.English = false;
                    using (ConfigForm bare = new ConfigForm(everything, new List<string>(), Voices.UsEnglishTags))
                    {
                        bare.StopTimers();
                        LanguageChoice bareRules = bare.WordLanguageBox.SelectedItem as LanguageChoice;
                        Note("no voices installed, German chosen at install: " + bare.AddList.Items.Count + " voices listed, " + bare.VoiceFilterBox.Items.Count +
                            " choices in Show, rules for " + (bareRules == null ? "?" : bareRules.Text) + ", default voice box \"" + bare.DefaultLabel.Text + "\"");
                        Check(!bare.LanguageShown("en") && bare.AddList.Items.Count == germanVoices && bare.VoiceFilterBox.Items.Count == 2 &&
                            bare.WordLanguageBox.Items.Count == 1 && bareRules != null && bareRules.Family == "de",
                            "another language chosen at install, nothing installed: only " + germanName + "'s voices, choice and rules are listed");
                        Check(bare.DefaultLabel.Text == ConfigForm.NoVoicesDefault, "with no voice installed the default voice box says \"" + ConfigForm.NoVoicesDefault + "\"");
                        bare.StopTimers();
                    }
                    Languages.Override = "";
                    Languages.English = true; Languages.Show = "";
                    // The file the installer leaves is taken up once, and deleted (where there is a settings folder).
                    string choiceFile = Path.Combine(App.SettingsFolder, Languages.ChoiceFile);
                    if (Directory.Exists(App.SettingsFolder) && !File.Exists(choiceFile))
                    {
                        File.WriteAllText(choiceFile, "de");
                        Languages.TakeInstallChoice();
                        Check(!File.Exists(choiceFile) && !Languages.English && Languages.Enabled.Contains("de") && Languages.InstallLanguage == "de",
                            "the installer's language file is taken up (and remembered for the start note), and deleted");
                        Languages.InstallLanguage = "";
                        Languages.Override = "";
                        Languages.English = true; Languages.Show = "";
                    }
                }
            }

            // The preview text: in each language offered, three lines; swapped for a voice's language unless
            // typed over.
            {
                bool allThree = true;
                foreach (string offered in Catalogue.Offered)
                {
                    string text;
                    if (!PreviewTexts.ByLanguage.TryGetValue(offered, out text) || text.Split('\n').Length != 3 || text.IndexOf(',') < 0) { allThree = false; }
                }
                Check(allThree && PreviewTexts.ByLanguage.Count == Catalogue.Offered.Length + 1, "a preview text in each language offered, in three lines with commas");
                Check(PreviewTexts.For("de").StartsWith("Hier", StringComparison.Ordinal) && PreviewTexts.For("xx") == App.PreviewText &&
                    PreviewTexts.LanguageOfKey("de_DE-thorsten-medium") == "de" && PreviewTexts.IsOneOfThem(PreviewTexts.For("fr")) &&
                    PreviewTexts.IsOneOfThem(App.OldPreviewText) && !PreviewTexts.IsOneOfThem("my own words"), "a voice's language picks its preview text; typed text is kept");
            }

            // Voice variation (Speed and pauses, Advanced): 60% unless set; moved, it is saved for the engine.
            {
                bool hadSettings = File.Exists(App.SettingsFile);
                string settingsWere = hadSettings ? File.ReadAllText(App.SettingsFile) : null;
                double variationWas = Store.GetVariation(App.SettingsFile);
                ui.VariationBar.Value = 120;
                ui.StopTimers(); ui.SaveGlobalSettings();
                double variationNow = Store.GetVariation(App.SettingsFile);
                Note("voice variation: " + variationWas + "% -> " + variationNow + "%");
                Check(variationNow == 120 && File.ReadAllText(App.SettingsFile).Contains("\"variation_percent\""), "Voice variation is saved as it is moved (variation_percent)");
                ui.VariationBar.Value = (int)variationWas;
                ui.StopTimers();
                if (hadSettings) { File.WriteAllText(App.SettingsFile, settingsWere); } else if (File.Exists(App.SettingsFile)) { File.Delete(App.SettingsFile); }
            }

            // About and "Powered by": every project is named, with whose it is; About has their links, who did
            // what and Lumoti's message.
            {
                using (Form aboutForm = ui.AboutWindow())
                {
                    int links = 0;
                    StringBuilder aboutWords = new StringBuilder();
                    foreach (Control c in aboutForm.Controls[0].Controls[0].Controls) { if (c is LinkLabel) { links++; } aboutWords.AppendLine(c.Text); }
                    string aboutText = aboutWords.ToString();
                    bool allNamed = true;
                    foreach (Credit credit in Credit.All) { if (!aboutText.Contains(credit.Name) || !aboutText.Contains(credit.By) || !aboutText.Contains(credit.Link)) { allNamed = false; } }
                    Check(allNamed && links == Credit.All.Length + 1 && aboutText.Contains(Credit.Coding) && aboutText.Contains("Kindness is free! <3") && aboutText.Contains("A message from Lumoti:"),
                        "About names each project with its author and link, who did what, and Lumoti's message");
                    if (pictures) { SavePicture(aboutForm, Path.Combine(folder, "about.png")); }
                }
                using (Form powered = ui.PoweredByWindow())
                {
                    StringBuilder poweredWords = new StringBuilder();
                    foreach (Control c in powered.Controls) { foreach (Control inner in c.Controls) { poweredWords.AppendLine(inner.Text); } }
                    string poweredText = poweredWords.ToString();
                    bool allNamed = poweredText.Contains("MODERN VOICES") && poweredText.Contains("Powered By...") && poweredText.Contains(Credit.Thanks);
                    foreach (Credit credit in Credit.All) { if (!poweredText.Contains(credit.Name) || !poweredText.Contains(credit.By)) { allNamed = false; } }
                    Check(allNamed, "\"Powered by\" names the program and each project with its author, and thanks the voices");
                    // (In front of the rest: first of the window's controls; and where the top right corner is.)
                    Control corner = powered.Controls[0];
                    Check(corner.Name == "close" && corner.Text.Length == 1 && corner.Bounds.Contains(new Point(powered.ClientSize.Width - 16, 16)),
                        "its X is in the top right corner, over the rest");
                    if (pictures) { SavePicture(powered, Path.Combine(folder, "powered-by.png")); }
                }
                // In the language chosen when installing, above the English (each one offered has its words).
                string installWas = Languages.InstallLanguage;
                string overrideWas = Languages.Override;
                Languages.Override = Languages.Override ?? "";
                Languages.InstallLanguage = "es";
                using (Form translated = ui.FirstVoicesWindow())
                {
                    Control translatedWords = translated.Controls["words"], translatedOk = translated.Controls["ok"];
                    translated.Show(); Application.DoEvents();
                    Note("select voices, Spanish chosen: " + translatedWords.Text.Replace("\r\n\r\n", " / ") + "; words " + translatedWords.Bounds + ", OK " + translatedOk.Bounds);
                    Check(translatedWords.Text.StartsWith(ConfigForm.FirstVoicesTranslated["es"]) && translatedWords.Text.EndsWith(ConfigForm.FirstVoicesWords) &&
                        translatedOk.Top >= translatedWords.Bottom + 10 && translatedOk.Bottom < translated.ClientSize.Height,
                        "with Spanish chosen when installing, the note is in Spanish above the English, and fits");
                    bool everyOne = true;
                    foreach (string offered in Catalogue.Offered) { if (offered != "en" && !ConfigForm.FirstVoicesTranslated.ContainsKey(offered)) { everyOne = false; Note("  no words for " + offered); } }
                    Check(everyOne, "every language offered when installing has its words");
                    if (pictures) { SavePicture(translated, Path.Combine(folder, "select-voices-es.png")); }
                    translated.Hide();
                }
                Languages.InstallLanguage = "en_GB";
                using (Form english = ui.FirstVoicesWindow())
                {
                    Check(english.Controls["words"].Text == ConfigForm.FirstVoicesWords, "English chosen when installing: the note is in English only");
                }
                Languages.InstallLanguage = installWas;
                Languages.Override = overrideWas;
                using (Form start = ui.FirstVoicesWindow())
                {
                    Control startWords = start.Controls["words"], startOk = start.Controls["ok"], startX = start.Controls["close"];
                    Check(startWords != null && startWords.Text == "Select the voices you wish to download and install, then click Apply changes.\r\n(Voice quality varies. Sample before downloading.)" &&
                        startOk is Button && startOk.Text == "OK" && start.AcceptButton == startOk && start.CancelButton == startOk &&
                        startX != null && startX.Bounds.Contains(new Point(start.ClientSize.Width - 16, 16)) && start.Controls[0] == startX,
                        "with no voice installed: \"Select the voices...\", with OK and an X in the top right corner");
                    start.Show(); Application.DoEvents();
                    Note("select voices: words " + startWords.Bounds + ", OK " + startOk.Bounds + ", window " + start.ClientSize);
                    Check(startOk.Top >= startWords.Bottom + 10 && startOk.Bottom < start.ClientSize.Height && startWords.Top >= startX.Bottom - 2,
                        "its words, then OK under them, fit inside it, under the X");
                    start.Hide();
                    if (pictures) { SavePicture(start, Path.Combine(folder, "select-voices.png")); }
                }
                Check(ui.AboutButton.Visible && ui.AboutButton.PointToScreen(Point.Empty).X < ui.ThemeButton.PointToScreen(Point.Empty).X, "About is in the header, left of the moon");
                int notePage = ui.PageIndex;
                ui.ShowPage(0);
                Settle(150);
                int savedLeft = ui.SavedNote.PointToScreen(Point.Empty).X, noteLeft = ui.Sliders["Volume"].Name.PointToScreen(Point.Empty).X;
                Note("\"Changes are saved\" starts at " + savedLeft + ", the sliders' names at " + noteLeft);
                Check(Math.Abs(savedLeft - noteLeft) <= 1, "\"Changes are saved straight away\" lines up with the sliders' names");
                ui.ShowPage(notePage);
                Settle(20);
            }

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
                Check(ui.Sliders["Volume"].Bar.Maximum == 100, "the Volume slider ends at " + ui.Sliders["Volume"].Bar.Maximum + "%");
                // A voice left above 100% by 0.12.0 shows as 100%.
                File.WriteAllText(App.VoiceFile(dir), File.ReadAllText(App.VoiceFile(dir)).Replace("\"peak\": 0.6", "\"peak\": 1.7"));
                ui.UpdateInstalled();
                Check(ui.Sliders["Volume"].Bar.Value == 100, "a voice saved at 170% by 0.12.0 shows as " + ui.Sliders["Volume"].Bar.Value + "%");
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
                Check(ui.GlobalBars["Sentence gap"].Example.Text == "At 150% speed you hear about 500 ms.", "the line under a pause shows what you hear at the speed set");
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
                Check(outputWas == 100 && outputNow == 40 && Store.GetOutput(App.SettingsFile) == 0 && silent.Contains("muted") && Store.GetGlobalSpeed(App.SettingsFile) == speedBefore,
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
                bool variationOffered = ui.OwnVariation.Bar.Enabled;
                ui.OwnVariation.Bar.Value = 40;
                ui.SaveSettings();
                ui.StopTimers();
                Pauses own = Store.GetVoicePauses(dir);
                Check(variationOffered && Store.GetVoiceVariation(dir) == 40, "a voice's own variation is saved with its override (own_variation_percent 40)");
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

                ListsAndGender(ui, folder, pictures);

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
                // A rule with nothing to say: kept as such, listed as not read.
                ui.ClearWordFields();
                ui.WordFrom.Text = "***"; ui.WordTo.Text = "";
                ui.SaveWord();
                List<Rule> skip = Rules("***");
                string skipShown = ui.WordList.SelectedItems.Count == 1 ? ui.WordList.SelectedItems[0].SubItems[1].Text : "(none selected)";
                ui.RemoveWord();
                Check(skip.Count == 1 && skip[0].To == "" && skip[0].On && skipShown == ConfigForm.NotRead && Rules("***").Count == 0,
                    "pronunciation: a rule with \"Say it as\" empty is saved with nothing to say, listed as '" + skipShown + "', and removed again");
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
                string marked = ui.InstalledList.Items[0].SubItems[ConfigForm.DefaultColumn].Text;
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
                // A gender chosen in the Voice manager is kept when the installer registers the voices again.
                string gendered = had[0];
                string genderWas = Voices.Gender(gendered);
                string genderChosen = genderWas == "Male" ? "Female" : "Male";
                ui.ShowPage(0);
                Settle(50);
                ui.SelectInstalledId(gendered);
                ui.SetGender(genderChosen);
                ui.ShowPage(3);
                Settle(50);
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
                Check(Voices.Gender(gendered) == genderChosen, "the gender chosen for " + gendered + " (" + genderChosen + ", was '" + genderWas +
                    "') is still Windows' after the installer registered the voices again ('" + Voices.Gender(gendered) + "')");
                if (download)
                {
                    Check(after.Contains("en_GB-alan-medium") && File.Exists(Path.Combine(alanFolder, "model.onnx")) && ui.ProgressSeen >= 90,
                        "the new voice was downloaded, with the bar moving (it reached " + ui.ProgressSeen + "%)");
                }
                Check(Voices.DefaultToken == Voices.TokenName(after[0]) && ui.InstalledList.Items[0].SubItems[ConfigForm.DefaultColumn].Text == "yes",
                    "Windows' default was one of its own voices: the first voice of the list is now the default (" + ui.DefaultLabel.Text + ")");
                Note("after apply: page " + ui.PageIndex + ", Apply changes lit " + ui.ApplyButton.Enabled + ", Close " + ui.CloseButton.Enabled + ", progress shown " + ui.Progress.Visible +
                    "; ticked: " + string.Join(", ", ui.PickedIds().ToArray()) + "; installed: " + string.Join(", ", ui.Installed.ToArray()));
                Check(ui.InstalledList.Items.Count == after.Count && ui.PageIndex == 0 && !ui.ApplyButton.Enabled && ui.CloseButton.Enabled && !ui.Progress.Visible,
                    "afterwards the Voice manager shows them (" + ui.InstalledList.Items.Count + " rows), with nothing left to apply");
                // And "neither": none given, even where the installer knows one.
                ui.ShowPage(0);
                Settle(50);
                ui.SelectInstalledId(gendered);
                ui.SetGender("");
                ui.ShowPage(3);
                Settle(50);
                ui.SpeakerList.SetItemChecked(extraIndex, false);
                if (download) { alan.Checked = false; }
                ui.Apply();
                finished = WaitWhile(delegate { return ui.Busy; }, 1200);
                after = Voices.InstalledIds();
                Note("apply (remove them again): " + ui.ApplyLines.Count + " lines from the installer; status: " + ui.StatusLabel.Text);
                Check(finished && after.Count == had.Count && !after.Contains(extra) && !after.Contains("en_GB-alan-medium") && ui.StatusLabel.Text.StartsWith("Done", StringComparison.Ordinal),
                    "Apply changes removes them again: " + after.Count + " voices installed");
                Check(Voices.Gender(gendered) == "", "a voice given neither gender has none after the installer registered the voices again ('" + Voices.Gender(gendered) + "')");
                ForgetGender(gendered, genderWas);
                if (download) { Check(!Directory.Exists(alanFolder), "the removed voice's files are gone"); }
                Check(Voices.DefaultToken == Voices.TokenName(after[0]), "the default voice was one of those removed: the first voice left is the default now (" + ui.DefaultLabel.Text + ")");
                // A Modern Voice that is the default stays so when voices are added.
                Voices.DefaultTokenId = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\" + Voices.TokenName(after[after.Count - 1]);
                ui.ShowPage(3);
                Settle(50);
                ui.SpeakerList.SetItemChecked(extraIndex, true);
                ui.Apply();
                finished = WaitWhile(delegate { return ui.Busy; }, 1200);
                Check(finished && Voices.InstalledIds().Count == had.Count + 1 && Voices.DefaultToken == Voices.TokenName(after[after.Count - 1]),
                    "a Modern Voice that is Windows' default stays the default when a voice is added (" + ui.DefaultLabel.Text + ")");
                ui.SpeakerList.SetItemChecked(extraIndex, false);
                ui.Apply();
                finished = WaitWhile(delegate { return ui.Busy; }, 1200);
                Check(finished && Voices.InstalledIds().Count == had.Count, "and the voices are as they were again");
                Check(Elevated.Ready, "the administrator's part was started once and is still there (nothing asks twice)");
                Voices.DefaultTokenId = defaultBefore;
            }
            ui.StopTimers();
            ui.Close();
            ui.Dispose();
        }

        // The Voice manager's columns, sorting both lists, the column order kept, the right-click menu and a
        // voice's gender (set through the administrator's part, as a person would; put back afterwards).
        static void ListsAndGender(ConfigForm ui, string folder, bool pictures)
        {
            ui.ShowPage(0);
            Settle(50);
            ListView list = ui.InstalledList;
            List<string> names = new List<string>();
            foreach (ColumnHeader column in list.Columns) { names.Add(column.Text); }
            Check(string.Join("|", names.ToArray()) == "Voice|Speaker|Nickname|Accent|Gender|On disk|Windows default|Folder" && list.AllowColumnReorder,
                "the Voice manager's columns: " + string.Join(", ", names.ToArray()) + " (they can be dragged into another order: " + list.AllowColumnReorder + ")");
            bool cellsRight = list.Items.Count > 0;
            foreach (ListViewItem item in list.Items)
            {
                InstalledRow row = (InstalledRow)item.Tag;
                string id = Voices.IdOf(row.Key, row.Speaker);
                if (row.Entry != null && item.SubItems[ConfigForm.AccentColumn].Text != row.Entry.Accent) { cellsRight = false; }
                if (item.SubItems[ConfigForm.GenderColumn].Text != Voices.Gender(id)) { cellsRight = false; }
            }
            Check(cellsRight, "each row's accent is the catalogue's, as Add or remove voices shows it, and its gender is the one Windows has now (" +
                (list.Items.Count > 0 ? list.Items[0].SubItems[ConfigForm.AccentColumn].Text + ", '" + list.Items[0].SubItems[ConfigForm.GenderColumn].Text + "'" : "") + ")");

            // A gender: set and cleared through the administrator's part, for 64- and 32-bit programs, and kept.
            list.Items[0].Selected = true;
            ui.UpdateInstalled();
            CurrentVoice voice = ui.GetCurrentVoice();
            string was = Voices.Gender(voice.Id);
            string wanted = was == "Female" ? "Male" : "Female";
            ui.SetGender(wanted);
            string now = Voices.Gender(voice.Id), now32 = Gender32(voice.Token), kept = KeptGender(voice.Id);
            string cell = ui.InstalledList.SelectedItems.Count > 0 ? ui.InstalledList.SelectedItems[0].SubItems[ConfigForm.GenderColumn].Text : "(none selected)";
            Check(now == wanted && (now32 == null || now32 == wanted) && kept == wanted && cell == wanted,
                "gender: " + voice.Voice + " set to " + wanted + ": Windows has '" + now + "' (32-bit: '" + now32 + "'), kept '" + kept + "', the list shows '" + cell + "'");
            ui.UpdateVoiceMenu();
            bool menuTicks = (wanted == "Female" ? ui.MenuFemale.Checked && !ui.MenuMale.Checked : ui.MenuMale.Checked && !ui.MenuFemale.Checked);
            ui.SetGender("");
            now = Voices.Gender(voice.Id); now32 = Gender32(voice.Token); kept = KeptGender(voice.Id);
            ui.UpdateVoiceMenu();
            Check(now == "" && (now32 == null || now32 == "") && kept == "" && menuTicks && !ui.MenuFemale.Checked && !ui.MenuMale.Checked,
                "gender cleared: Windows has none (32-bit: '" + now32 + "'), kept as '' (so the installer gives it none); the menu ticked the one set (" + menuTicks + "), and now neither");
            ForgetGender(voice.Id, was);
            Check(Voices.Gender(voice.Id) == was && KeptGender(voice.Id) == null, "and put back as it was ('" + was + "'), with no choice kept");

            // The right-click menu: its four choices, the selected voice's state ticked.
            ui.UpdateVoiceMenu();
            List<string> offered = new List<string>();
            foreach (ToolStripItem item in ui.VoiceMenu.Items) { offered.Add(item.Text); }
            bool isDefault = string.Equals(Voices.DefaultToken, voice.Token, StringComparison.OrdinalIgnoreCase);
            Check(string.Join("|", offered.ToArray()) == "Set nickname|Set gender|Override defaults|Make default voice" &&
                ui.MenuGender.DropDownItems.Count == 2 && ui.MenuDefault.Checked == isDefault && ui.MenuDefault.Enabled == !isDefault && ui.MenuOverride.Checked == ui.OwnBox.Checked,
                "the right-click menu offers " + string.Join(", ", offered.ToArray()) + " (Female and Male under Set gender); default ticked " + ui.MenuDefault.Checked);
            bool ownWas = ui.OwnBox.Checked;
            ui.MenuOverride.PerformClick();
            bool flipped = ui.OwnBox.Checked != ownWas && ui.OwnPausesShown == ui.OwnBox.Checked;
            ui.MenuOverride.PerformClick();
            ui.SaveSettings();
            ui.StopTimers();
            Check(flipped && ui.OwnBox.Checked == ownWas, "the menu's Override defaults ticks and unticks the override box (" + flipped + ")");
            ui.MenuNickname.PerformClick();
            Settle(100);
            Check(ui.NickBox.Focused || !ui.ContainsFocus, "the menu's Set nickname puts the cursor in the nickname box (focused: " + ui.NickBox.Focused + ")");
            if (pictures)
            {
                foreach (string look in new string[] { "light", "dark" })
                {
                    try
                    {
                        Theme.Override = look;
                        ui.ApplyTheme();
                        ui.UpdateVoiceMenu();
                        ui.VoiceMenu.Show(list, new Point(Math.Min(list.Width / 3, 200), 30));
                        ui.MenuGender.ShowDropDown();
                        Settle(200);
                        SaveControl(ui.VoiceMenu, Path.Combine(folder, "voice-menu-" + look + ".png"));
                        SaveControl(ui.MenuGender.DropDown, Path.Combine(folder, "voice-menu-gender-" + look + ".png"));
                    }
                    catch (Exception e) { Note("(no picture of the right-click menu, " + look + ": " + e.Message + ")"); }
                    finally { ui.VoiceMenu.Close(); }
                }
                Theme.Override = null;
                ui.ApplyTheme();
            }

            // (Pictures from here on: the window at its usual size, dark, each tab at its top.)
            Rectangle boundsWere = ui.Bounds;
            if (pictures)
            {
                Theme.Override = "dark";
                ui.ApplyTheme();
                ui.Bounds = ConfigForm.WindowBounds(ConfigForm.ScreenScale());
                ui.Pages[0].AutoScrollPosition = Point.Empty;
                Settle(100);
            }

            // Sorting the Voice manager: by gender, then the other way, by size as numbers; each kept for next time.
            ui.SortInstalled(ConfigForm.GenderColumn);
            bool up = Sorted(list, ConfigForm.GenderColumn, false);
            string keptUp = ListLayout.Get("VoiceSort");
            ui.SortInstalled(ConfigForm.GenderColumn);
            bool down = Sorted(list, ConfigForm.GenderColumn, true);
            Check(up && down && keptUp == "Gender|up" && ListLayout.Get("VoiceSort") == "Gender|down",
                "the Voice manager sorts by Gender A to Z (" + up + "), clicked again Z to A (" + down + "); kept as '" + ListLayout.Get("VoiceSort") + "'");
            bool everyRowNamed = true;
            foreach (ListViewItem item in list.Items) { if (item.Text.Length == 0) { everyRowNamed = false; } }
            ui.SortInstalled(ConfigForm.SizeColumn);
            bool bySize = Sorted(list, ConfigForm.SizeColumn, false);
            Check(bySize && everyRowNamed, "sorted by On disk the sizes go up as numbers (" + bySize + "); sorted by anything but the voice, every row names its voice (" + everyRowNamed + ")");
            if (pictures) { ui.Picture(Path.Combine(folder, "voice-manager-sorted.png")); }
            ui.SortInstalledAsListed();
            bool grouped = true;
            foreach (ListViewItem item in list.Items) { if (((InstalledRow)item.Tag).First != (item.Text.Length > 0)) { grouped = false; } }
            Check(grouped && ListLayout.Get("VoiceSort") == "", "as installed again, a voice of several speakers names itself on its first row only (" + grouped + ")");

            // The column order: as it was left last time (Nickname dragged to the left), the last one filling the row.
            ListLayout.Set("VoiceColumns", "Nickname|Voice|Speaker|Accent|Gender|On disk|Windows default|Folder");
            ui.RestoreColumnOrder();
            ui.FillLastColumn(list);
            bool nickFirst = list.Columns[ConfigForm.NickColumn].DisplayIndex == 0 && list.Columns[ConfigForm.VoiceColumn].DisplayIndex == 1;
            if (pictures) { ui.Picture(Path.Combine(folder, "voice-manager-nickname-first.png")); }
            ListLayout.Set("VoiceColumns", string.Join("|", names.ToArray()));
            ui.RestoreColumnOrder();
            ui.FillLastColumn(list);
            bool back = true;
            for (int i = 0; i < list.Columns.Count; i++) { if (list.Columns[i].DisplayIndex != i) { back = false; } }
            Check(nickFirst && back, "the columns come up in the order they were left in: Nickname first (" + nickFirst + "), and back again (" + back + ")");

            // Add or remove voices: sorted by download size, then the other way; ticks and the "Show" choice untouched.
            ui.ShowPage(3);
            Settle(50);
            int tickedBefore = 0, shownBefore = ui.AddList.Items.Count;
            foreach (ListViewItem item in ui.VoiceItems) { if (item.Checked) { tickedBefore++; } }
            ui.SortAdd(4);
            bool addUp = AddSorted(ui.AddList, false);
            ui.SortAdd(4);
            bool addDown = AddSorted(ui.AddList, true);
            int tickedAfter = 0;
            foreach (ListViewItem item in ui.VoiceItems) { if (item.Checked) { tickedAfter++; } }
            ui.ApplyVoiceFilter();      // (refilled, as a change of the "Show" box does: still sorted)
            bool keptSorted = AddSorted(ui.AddList, true);
            Check(addUp && addDown && keptSorted && tickedAfter == tickedBefore && ui.AddList.Items.Count == shownBefore && ListLayout.Get("AddSort") == "Download|down",
                "Add or remove voices sorts by Download as numbers, up (" + addUp + ") and down (" + addDown + "), still sorted after the list is refilled (" + keptSorted +
                "); " + tickedAfter + " ticked before and after, " + ui.AddList.Items.Count + " shown");
            if (pictures)
            {
                ui.Pages[3].AutoScrollPosition = Point.Empty;
                ui.SortAdd(2);      // (by accent: a column in view, so its arrow shows)
                Settle(100);
                ui.Picture(Path.Combine(folder, "add-sorted.png"));
                Theme.Override = null;
                ui.ApplyTheme();
                ui.Bounds = boundsWere;
            }
            ui.SortAddAsListed();
            ui.ShowPage(0);
            Settle(50);
            ui.SelectInstalledId(voice.Id);
        }
        static bool Sorted(ListView list, int column, bool down)
        {
            for (int i = 1; i < list.Items.Count; i++)
            {
                InstalledRow a = (InstalledRow)list.Items[i - 1].Tag, b = (InstalledRow)list.Items[i].Tag;
                int c = column == ConfigForm.SizeColumn ? a.SizeMb.CompareTo(b.SizeMb) : string.Compare(a.Gender, b.Gender, StringComparison.CurrentCultureIgnoreCase);
                if (down ? c < 0 : c > 0) { return false; }
            }
            return true;
        }
        static bool AddSorted(ListView list, bool down)
        {
            for (int i = 1; i < list.Items.Count; i++)
            {
                int c = ((Entry)list.Items[i - 1].Tag).SizeMb.CompareTo(((Entry)list.Items[i].Tag).SizeMb);
                if (down ? c < 0 : c > 0) { return false; }
            }
            return true;
        }
        // The gender the 32-bit programs' token has (null: no such token).
        static string Gender32(string token)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens\" + token + @"\Attributes"))
            {
                return key == null ? null : Voices.NormalGender(key.GetValue("Gender") as string);
            }
        }
        // The choice kept for the installer (null: none).
        static string KeptGender(string id)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Modern Voices for Windows\Genders"))
            {
                return key == null ? null : key.GetValue(id) as string;
            }
        }
        // A voice's gender put back as it was before the test, with no choice kept (the test runs as an administrator).
        static void ForgetGender(string id, string was)
        {
            try
            {
                Voices.SetGender(id, was);
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Modern Voices for Windows\Genders", true)) { if (key != null) { key.DeleteValue(id, false); } }
            }
            catch (Exception e) { Note("(the test's gender was not put back: " + e.Message + ")"); }
        }
        static void SaveControl(Control control, string file)
        {
            using (Bitmap shot = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height)))
            {
                control.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                shot.Save(file, ImageFormat.Png);
            }
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
                    // The dark ones again, scrolled to the foot of a tab that doesn't fit (the Voice manager's with
                    // the override sliders showing).
                    if (shape[0] != "dark") { continue; }
                    bool peeked = i == 0 && !ui.OwnPausesShown;
                    if (peeked) { ui.PeekOwnPauses(true); Settle(100); }
                    if (ui.Pages[i].DisplayRectangle.Height > ui.Pages[i].ClientSize.Height)
                    {
                        ui.Pages[i].AutoScrollPosition = new Point(0, 20000);
                        Settle(200);
                        ui.Picture(Path.Combine(folder, shape[0] + "-" + (i + 1) + "-lower.png"));
                        ui.Pages[i].AutoScrollPosition = new Point(0, 0);
                    }
                    if (peeked) { ui.PeekOwnPauses(false); }
                    Settle(100);
                }
            }
            Theme.Override = null;
            ui.ApplyTheme();
            ui.ShowPage(page);
            Note("pictures of the window: " + folder);
        }
    }
}
