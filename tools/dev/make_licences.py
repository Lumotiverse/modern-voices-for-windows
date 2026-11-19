#!/usr/bin/env python3
"""Writes VOICE-LICENCES.md and the voice licence table in config/Config.cs (between the lines
'// ---- voice licences (made by ...' and '// ---- end of voice licences') from tools/voice-licences.json.
That file was made from each voice's MODEL_CARD in the Piper collection (version 1.0.0), fetched by
.github/workflows/licences.yml (tools/dev/fetch_licences.py), and classified by hand: edit it, then
run  python3 tools/dev/make_licences.py"""
import json, os, re
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
data = json.load(open(os.path.join(ROOT, 'tools', 'voice-licences.json'), encoding='utf-8'))

GROUPS = [
    ('Public domain', 'Public domain',
     'Public domain, CC0 or the Unlicense, and not trained from another voice with stricter terms. '
     'Anyone may use these voices and the speech they make for anything, including commercially, with '
     'no credit needed.'),
    ('Free, give credit', 'Free to use, with credit',
     'CC BY or Apache 2.0, and not trained from another voice with stricter terms. Free for any use, '
     'including commercially, as long as you credit the source (the voice\'s dataset, named on its page).'),
    ('Free, share alike', 'Free to use, share alike',
     'A "share alike" or copyleft licence (here, AGPLv3). Free to use with credit, but anything you '
     'make from it must be shared under the same licence.'),
    ('Non-commercial', 'Non-commercial only',
     'CC BY-NC: free for personal and other non-commercial use, with credit. Not for anything that is '
     'sold or makes money.'),
    ('Unsettled (lessac)', 'Unsettled: trained from the lessac voice',
     'These voices were trained starting from Piper\'s American "lessac" voice (or from a voice that '
     'was itself trained from it). Their own licence, shown in brackets, is often generous, but the '
     'recordings lessac was made from are licensed for research only, with no commercial use and no '
     'passing them on without permission. Nobody has settled whether those terms carry over to voices '
     'trained from it; Piper\'s maintainer has said it is for each user to judge. They are fine for '
     'personal use at home. Be careful about using their speech commercially or publishing it.'),
    ('Research only', 'Research only',
     'Lessac itself: the Blizzard Challenge 2013 licence allows research and exploration only. No '
     'commercial use, and no passing the recordings on without permission.'),
    ('Unclear', 'Unclear',
     'The voice\'s page does not give a licence, only a link to its dataset. Check there before using '
     'its speech for anything beyond personal use.'),
]
ORDER = [g[0] for g in GROUPS]
LANG = {'en': 'English', 'ca': 'Catalan', 'cs': 'Czech', 'cy': 'Welsh', 'da': 'Danish', 'de': 'German',
        'es': 'Spanish', 'fi': 'Finnish', 'fr': 'French', 'hu': 'Hungarian', 'is': 'Icelandic', 'it': 'Italian',
        'lb': 'Luxembourgish', 'lv': 'Latvian', 'nl': 'Dutch', 'no': 'Norwegian', 'pl': 'Polish', 'pt': 'Portuguese',
        'ro': 'Romanian', 'ru': 'Russian', 'sk': 'Slovak', 'sl': 'Slovenian', 'sr': 'Serbian', 'sv': 'Swedish',
        'tr': 'Turkish', 'uk': 'Ukrainian'}

NAMES = {'ljspeech': 'LJSpeech', 'rdh': 'RDH', 'nst': 'NST', 'ukrainian_tts': 'Ukrainian TTS', 'mls': 'MLS',
         'bu_tts': 'Bu TTS', 'l2arctic': 'L2Arctic', 'hfc_female': 'HFC Female', 'hfc_male': 'HFC Male',
         'libritts': 'LibriTTS', 'libritts_r': 'LibriTTS-R', 'upmc': 'UPMC', 'dfki': 'DFKI', 'mc_speech': 'MC Speech',
         'upc_ona': 'UPC Ona', 'vctk': 'VCTK', 'jenny_dioco': 'Jenny Dioco', 'serbski_institut': 'Serbski Institut',
         'tug\u00e3o': 'Tug\u00e3o'}

def name(key):
    code, voice, quality = key.split('-')
    return NAMES.get(voice, voice.replace('_', ' ').title()), code, quality

def card_url(key):
    code, voice, quality = key.split('-')
    return 'https://huggingface.co/rhasspy/piper-voices/blob/v1.0.0/%s/%s/%s/%s/MODEL_CARD' % (code.split('_')[0], code, voice, quality)

md = ['# Voice licences', '',
      'Modern Voices for Windows contains no voices. The voices you tick in Modern Voices Config are',
      'downloaded straight from the [Piper voice collection](https://huggingface.co/rhasspy/piper-voices)',
      '(version 1.0.0), and each one keeps its own licence. The licence covers the voice, and usually',
      'the speech you make with it too.', '',
      'This page groups the voices Modern Voices offers (medium and high quality) by what their',
      'licences allow. Each voice is taken from its own page in the collection (its MODEL_CARD, linked',
      'below), which names the recordings it was made from, their licence, and what it was trained from.',
      'The same group shows in the **Licence** column of *Add or remove voices*; hover over a voice there',
      'to see its licence and what it was trained from.', '',
      '**For using a voice at home** (reading to yourself, in a game, with a screen reader) any of them',
      'is fine. The groups matter if you publish or sell speech made with a voice: a video, a podcast,',
      'an audiobook, a product.', '',
      '*This is a plain-language summary to help you choose, not legal advice. Where it matters, read the',
      'voice\'s own page and its dataset\'s licence.*', '']
for gid, title, text in GROUPS:
    keys = [k for k in data if data[k]['group'] == gid]
    if not keys:
        continue
    md += ['## %s' % title, '', text, '', '| Voice | Language | Quality | Licence on its page | Trained from |', '|---|---|---|---|---|']
    for k in sorted(keys, key=lambda k: (k.split('_')[0] != 'en', LANG.get(k.split('_')[0], k), k)):
        n, code, q = name(k)
        x = data[k]
        sp = ' (%d speakers)' % x['speakers'] if x['speakers'] and x['speakers'] > 1 else ''
        md.append('| [%s](%s)%s | %s (%s) | %s | %s | %s |' % (n, card_url(k), sp, LANG.get(code.split('_')[0], code), code, q, x['licence'], x['trained']))
    md.append('')
md += ['## How this list was made', '',
       '`tools/dev/fetch_licences.py`, run by the *voice licences* workflow on GitHub, reads every',
       'voice\'s MODEL_CARD at version 1.0.0. `tools/voice-licences.json` holds the result, grouped by',
       'hand, and `tools/dev/make_licences.py` writes this page and the column in Config from it.',
       'Corrections are welcome as issues or pull requests.', '',
       'Not yet checked beyond the card: what four voices were trained from, where the card only points',
       'to a web page (Claude, Spanish; Tom, French; Aivars, Latvian; Artur, Slovenian); and the',
       'Icelandic dataset\'s licence. Artur\'s card says CC BY 4.0 while its dataset\'s page says CC BY 3.0.', '']
open(os.path.join(ROOT, 'VOICE-LICENCES.md'), 'w', encoding='utf-8', newline='\n').write('\n'.join(md))

def cs(s):
    out = ''
    for ch in s:
        if ord(ch) > 126: out += '\\u%04x' % ord(ch)
        elif ch in '"\\': out += '\\' + ch
        else: out += ch
    return out
rows = ['        // ---- voice licences (made by tools/dev/make_licences.py from tools/voice-licences.json: edit there)',
        '        public static readonly string[] Groups = { %s };' % ', '.join('"%s"' % g for g in ORDER),
        '        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {']
for k in sorted(data):
    x = data[k]
    rows.append('            { "%s", new string[] { "%s", "%s", "%s" } },' % (cs(k), cs(x['group']), cs(x['licence']), cs(x['trained'])))
rows += ['        };', '        // ---- end of voice licences']
path = os.path.join(ROOT, 'config', 'Config.cs')
src = open(path, encoding='utf-8').read()
new, n = re.subn(r'        // ---- voice licences \(made by.*?        // ---- end of voice licences', lambda m: '\n'.join(rows), src, flags=re.S)
if n != 1:
    raise SystemExit('Config.cs has no voice licence table to replace (put the marker lines in VoiceLicences first)')
open(path, 'w', encoding='utf-8', newline='').write(new)
print('wrote VOICE-LICENCES.md and', len(data), 'voices into Config.cs')
