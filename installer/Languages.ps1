# The languages a fresh install can be set up for, beside English (Install.ps1's first window): each
# language's name in English and in itself, and the note shown when it is chosen (a heading and five
# lines; the English of it is $LanguageNoteEnglish in Install.ps1, shown underneath). The same
# languages as Modern Voices Config offers (Catalogue.Offered in config\Config.cs): keep them in step.
# The translations are Claude's, not checked by a speaker of each. Dot-sourced by Install.ps1.
# Plain ASCII: the letters are written as JSON \u escapes. Made by tools/dev/make_languages.py: change
# the words there, then run it again.
$LanguageNotesJson = @'
{
 "bg": {
  "lines": [
   "\u041f\u0440\u043e\u0437\u043e\u0440\u0446\u0438\u0442\u0435 \u0438 \u0431\u0443\u0442\u043e\u043d\u0438\u0442\u0435 \u043d\u0430 \u0442\u0430\u0437\u0438 \u043f\u0440\u043e\u0433\u0440\u0430\u043c\u0430 \u0441\u0430 \u0441\u0430\u043c\u043e \u043d\u0430 \u0430\u043d\u0433\u043b\u0438\u0439\u0441\u043a\u0438 \u0435\u0437\u0438\u043a.",
   "\u041c\u0430\u043b\u043a\u043e\u0442\u043e \u0442\u0435\u043a\u0441\u0442\u043e\u0432\u0435 \u043d\u0430 \u0432\u0430\u0448\u0438\u044f \u0435\u0437\u0438\u043a \u0441\u0430 \u043f\u0440\u0435\u0432\u0435\u0434\u0435\u043d\u0438 \u043e\u0442 \u043a\u043e\u043c\u043f\u044e\u0442\u044a\u0440 \u0438 \u043d\u0435 \u0441\u0430 \u043f\u0440\u043e\u0432\u0435\u0440\u0435\u043d\u0438. \u0412\u044a\u0437\u043c\u043e\u0436\u043d\u043e \u0435 \u0434\u0430 \u0441\u044a\u0434\u044a\u0440\u0436\u0430\u0442 \u0433\u0440\u0435\u0448\u043a\u0438.",
   "\u0413\u043b\u0430\u0441\u043e\u0432\u0435\u0442\u0435 \u043d\u0430 \u0442\u043e\u0437\u0438 \u0435\u0437\u0438\u043a \u0441\u0430 \u0442\u0435\u0441\u0442\u0432\u0430\u043d\u0438 \u043c\u043d\u043e\u0433\u043e \u043f\u043e-\u043c\u0430\u043b\u043a\u043e \u043e\u0442 \u0430\u043d\u0433\u043b\u0438\u0439\u0441\u043a\u0438\u0442\u0435 \u0433\u043b\u0430\u0441\u043e\u0432\u0435.",
   "\u0417\u0430 \u0442\u043e\u0437\u0438 \u0435\u0437\u0438\u043a \u043d\u044f\u043c\u0430 \u0433\u043e\u0442\u043e\u0432\u0438 \u043f\u0440\u0430\u0432\u0438\u043b\u0430 \u0437\u0430 \u043f\u0440\u043e\u0438\u0437\u043d\u043e\u0448\u0435\u043d\u0438\u0435. \u041c\u043e\u0436\u0435\u0442\u0435 \u0434\u0430 \u0434\u043e\u0431\u0430\u0432\u0438\u0442\u0435 \u0441\u0432\u043e\u0438 \u0432 \u0440\u0430\u0437\u0434\u0435\u043b\u0430 \u201ePronunciation\u201c.",
   "\u0410\u043d\u0433\u043b\u0438\u0439\u0441\u043a\u0438\u0442\u0435 \u0433\u043b\u0430\u0441\u043e\u0432\u0435 \u043d\u044f\u043c\u0430 \u0434\u0430 \u0431\u044a\u0434\u0430\u0442 \u043f\u043e\u043a\u0430\u0437\u0430\u043d\u0438. \u041c\u043e\u0436\u0435\u0442\u0435 \u0434\u0430 \u0433\u0438 \u0434\u043e\u0431\u0430\u0432\u0438\u0442\u0435 \u043f\u043e-\u043a\u044a\u0441\u043d\u043e \u0441 \u0431\u0443\u0442\u043e\u043d\u0430 \u201eAdditional languages\u201c."
  ],
  "name": "Bulgarian",
  "native": "\u0411\u044a\u043b\u0433\u0430\u0440\u0441\u043a\u0438",
  "title": "\u041c\u043e\u043b\u044f, \u043e\u0431\u044a\u0440\u043d\u0435\u0442\u0435 \u0432\u043d\u0438\u043c\u0430\u043d\u0438\u0435"
 },
 "ca": {
  "lines": [
   "Les finestres i els botons d'aquest programa nom\u00e9s estan en angl\u00e8s.",
   "Els pocs textos en la vostra llengua els ha tradu\u00eft un ordinador i no s'han revisat. Poden contenir errors.",
   "Les veus en aquesta llengua s'han provat molt menys que les veus en angl\u00e8s.",
   "No hi ha regles de pronunciaci\u00f3 ja fetes per a aquesta llengua. Podeu afegir les vostres a la pestanya \u00abPronunciation\u00bb.",
   "Les veus en angl\u00e8s no es mostraran. Les podeu afegir m\u00e9s endavant amb el bot\u00f3 \u00abAdditional languages\u00bb."
  ],
  "name": "Catalan",
  "native": "Catal\u00e0",
  "title": "Tingueu en compte"
 },
 "cs": {
  "lines": [
   "Okna a tla\u010d\u00edtka tohoto programu jsou pouze v angli\u010dtin\u011b.",
   "T\u011bch n\u011bkolik text\u016f ve va\u0161em jazyce p\u0159elo\u017eil po\u010d\u00edta\u010d a nikdo je nezkontroloval. Mohou obsahovat chyby.",
   "Hlasy v tomto jazyce byly testov\u00e1ny mnohem m\u00e9n\u011b ne\u017e anglick\u00e9 hlasy.",
   "Pro tento jazyk nejsou p\u0159ipravena \u017e\u00e1dn\u00e1 pravidla v\u00fdslovnosti. Vlastn\u00ed m\u016f\u017eete p\u0159idat na kart\u011b \u201ePronunciation\u201c.",
   "Anglick\u00e9 hlasy nebudou v seznamu. Pozd\u011bji je m\u016f\u017eete p\u0159idat tla\u010d\u00edtkem \u201eAdditional languages\u201c."
  ],
  "name": "Czech",
  "native": "\u010ce\u0161tina",
  "title": "Upozorn\u011bn\u00ed"
 },
 "cy": {
  "lines": [
   "Mae ffenestri a botymau'r rhaglen hon yn Saesneg yn unig.",
   "Cyfieithwyd yr ychydig destunau yn eich iaith gan gyfrifiadur ac nid ydynt wedi'u gwirio. Gallant gynnwys camgymeriadau.",
   "Mae'r lleisiau yn yr iaith hon wedi'u profi lawer llai na'r lleisiau Saesneg.",
   "Nid oes rheolau ynganu parod ar gyfer yr iaith hon. Gallwch ychwanegu eich rhai eich hun ar y tab \u201cPronunciation\u201d.",
   "Ni fydd lleisiau Saesneg yn cael eu rhestru. Gallwch eu hychwanegu yn nes ymlaen gyda'r botwm \u201cAdditional languages\u201d."
  ],
  "name": "Welsh",
  "native": "Cymraeg",
  "title": "Sylwch"
 },
 "da": {
  "lines": [
   "Programmets vinduer og knapper er kun p\u00e5 engelsk.",
   "De f\u00e5 tekster p\u00e5 dit sprog er oversat af en computer og er ikke blevet kontrolleret. De kan indeholde fejl.",
   "Stemmerne p\u00e5 dette sprog er testet meget mindre end de engelske stemmer.",
   "Der er ingen f\u00e6rdige udtaleregler til dette sprog. Du kan tilf\u00f8je dine egne p\u00e5 fanen \u00bbPronunciation\u00ab.",
   "Engelske stemmer bliver ikke vist. Du kan tilf\u00f8je dem senere med knappen \u00bbAdditional languages\u00ab."
  ],
  "name": "Danish",
  "native": "Dansk",
  "title": "Bem\u00e6rk"
 },
 "de": {
  "lines": [
   "Die Fenster und Schaltfl\u00e4chen dieses Programms sind nur auf Englisch.",
   "Die wenigen Texte in Ihrer Sprache wurden von einem Computer \u00fcbersetzt und nicht gepr\u00fcft. Sie k\u00f6nnen Fehler enthalten.",
   "Die Stimmen in dieser Sprache wurden viel weniger getestet als die englischen Stimmen.",
   "F\u00fcr diese Sprache gibt es keine fertigen Ausspracheregeln. Eigene Regeln k\u00f6nnen Sie auf der Registerkarte \u201ePronunciation\u201c hinzuf\u00fcgen.",
   "Englische Stimmen werden nicht aufgelistet. Sie k\u00f6nnen sie sp\u00e4ter \u00fcber die Schaltfl\u00e4che \u201eAdditional languages\u201c hinzuf\u00fcgen."
  ],
  "name": "German",
  "native": "Deutsch",
  "title": "Bitte beachten"
 },
 "el": {
  "lines": [
   "\u03a4\u03b1 \u03c0\u03b1\u03c1\u03ac\u03b8\u03c5\u03c1\u03b1 \u03ba\u03b1\u03b9 \u03c4\u03b1 \u03ba\u03bf\u03c5\u03bc\u03c0\u03b9\u03ac \u03b1\u03c5\u03c4\u03bf\u03cd \u03c4\u03bf\u03c5 \u03c0\u03c1\u03bf\u03b3\u03c1\u03ac\u03bc\u03bc\u03b1\u03c4\u03bf\u03c2 \u03b5\u03af\u03bd\u03b1\u03b9 \u03bc\u03cc\u03bd\u03bf \u03c3\u03c4\u03b1 \u03b1\u03b3\u03b3\u03bb\u03b9\u03ba\u03ac.",
   "\u03a4\u03b1 \u03bb\u03af\u03b3\u03b1 \u03ba\u03b5\u03af\u03bc\u03b5\u03bd\u03b1 \u03c3\u03c4\u03b7 \u03b3\u03bb\u03ce\u03c3\u03c3\u03b1 \u03c3\u03b1\u03c2 \u03bc\u03b5\u03c4\u03b1\u03c6\u03c1\u03ac\u03c3\u03c4\u03b7\u03ba\u03b1\u03bd \u03b1\u03c0\u03cc \u03c5\u03c0\u03bf\u03bb\u03bf\u03b3\u03b9\u03c3\u03c4\u03ae \u03ba\u03b1\u03b9 \u03b4\u03b5\u03bd \u03ad\u03c7\u03bf\u03c5\u03bd \u03b5\u03bb\u03b5\u03b3\u03c7\u03b8\u03b5\u03af. \u039c\u03c0\u03bf\u03c1\u03b5\u03af \u03bd\u03b1 \u03c0\u03b5\u03c1\u03b9\u03ad\u03c7\u03bf\u03c5\u03bd \u03bb\u03ac\u03b8\u03b7.",
   "\u039f\u03b9 \u03c6\u03c9\u03bd\u03ad\u03c2 \u03c3\u03b5 \u03b1\u03c5\u03c4\u03ae \u03c4\u03b7 \u03b3\u03bb\u03ce\u03c3\u03c3\u03b1 \u03ad\u03c7\u03bf\u03c5\u03bd \u03b4\u03bf\u03ba\u03b9\u03bc\u03b1\u03c3\u03c4\u03b5\u03af \u03c0\u03bf\u03bb\u03cd \u03bb\u03b9\u03b3\u03cc\u03c4\u03b5\u03c1\u03bf \u03b1\u03c0\u03cc \u03c4\u03b9\u03c2 \u03b1\u03b3\u03b3\u03bb\u03b9\u03ba\u03ad\u03c2 \u03c6\u03c9\u03bd\u03ad\u03c2.",
   "\u0394\u03b5\u03bd \u03c5\u03c0\u03ac\u03c1\u03c7\u03bf\u03c5\u03bd \u03ad\u03c4\u03bf\u03b9\u03bc\u03bf\u03b9 \u03ba\u03b1\u03bd\u03cc\u03bd\u03b5\u03c2 \u03c0\u03c1\u03bf\u03c6\u03bf\u03c1\u03ac\u03c2 \u03b3\u03b9\u03b1 \u03b1\u03c5\u03c4\u03ae \u03c4\u03b7 \u03b3\u03bb\u03ce\u03c3\u03c3\u03b1. \u039c\u03c0\u03bf\u03c1\u03b5\u03af\u03c4\u03b5 \u03bd\u03b1 \u03c0\u03c1\u03bf\u03c3\u03b8\u03ad\u03c3\u03b5\u03c4\u03b5 \u03b4\u03b9\u03ba\u03bf\u03cd\u03c2 \u03c3\u03b1\u03c2 \u03c3\u03c4\u03b7\u03bd \u03ba\u03b1\u03c1\u03c4\u03ad\u03bb\u03b1 \u00abPronunciation\u00bb.",
   "\u039f\u03b9 \u03b1\u03b3\u03b3\u03bb\u03b9\u03ba\u03ad\u03c2 \u03c6\u03c9\u03bd\u03ad\u03c2 \u03b4\u03b5\u03bd \u03b8\u03b1 \u03b5\u03bc\u03c6\u03b1\u03bd\u03af\u03b6\u03bf\u03bd\u03c4\u03b1\u03b9. \u039c\u03c0\u03bf\u03c1\u03b5\u03af\u03c4\u03b5 \u03bd\u03b1 \u03c4\u03b9\u03c2 \u03c0\u03c1\u03bf\u03c3\u03b8\u03ad\u03c3\u03b5\u03c4\u03b5 \u03b1\u03c1\u03b3\u03cc\u03c4\u03b5\u03c1\u03b1 \u03bc\u03b5 \u03c4\u03bf \u03ba\u03bf\u03c5\u03bc\u03c0\u03af \u00abAdditional languages\u00bb."
  ],
  "name": "Greek",
  "native": "\u0395\u03bb\u03bb\u03b7\u03bd\u03b9\u03ba\u03ac",
  "title": "\u03a3\u03b7\u03bc\u03b5\u03af\u03c9\u03c3\u03b7"
 },
 "es": {
  "lines": [
   "Las ventanas y los botones de este programa est\u00e1n solo en ingl\u00e9s.",
   "Los pocos textos en su idioma fueron traducidos por un ordenador y no se han revisado. Pueden contener errores.",
   "Las voces en este idioma se han probado mucho menos que las voces en ingl\u00e9s.",
   "No hay reglas de pronunciaci\u00f3n ya preparadas para este idioma. Puede a\u00f1adir las suyas en la pesta\u00f1a \u00abPronunciation\u00bb.",
   "Las voces en ingl\u00e9s no aparecer\u00e1n en la lista. Puede a\u00f1adirlas m\u00e1s adelante con el bot\u00f3n \u00abAdditional languages\u00bb."
  ],
  "name": "Spanish",
  "native": "Espa\u00f1ol",
  "title": "Tenga en cuenta"
 },
 "eu": {
  "lines": [
   "Programa honen leihoak eta botoiak ingelesez bakarrik daude.",
   "Zure hizkuntzan dauden testu apurrak ordenagailu batek itzuli ditu eta ez dira berrikusi. Akatsak izan ditzakete.",
   "Hizkuntza honetako ahotsak ingelesezkoak baino askoz gutxiago probatu dira.",
   "Ez dago hizkuntza honetarako prestatutako ahoskera-araurik. Zureak gehi ditzakezu \u00abPronunciation\u00bb fitxan.",
   "Ingelesezko ahotsak ez dira zerrendan agertuko. Geroago gehi ditzakezu \u00abAdditional languages\u00bb botoiarekin."
  ],
  "name": "Basque",
  "native": "Euskara",
  "title": "Kontuan izan"
 },
 "fi": {
  "lines": [
   "T\u00e4m\u00e4n ohjelman ikkunat ja painikkeet ovat vain englanniksi.",
   "Harvat omalla kielell\u00e4si olevat tekstit on k\u00e4\u00e4nt\u00e4nyt tietokone, eik\u00e4 niit\u00e4 ole tarkistettu. Niiss\u00e4 voi olla virheit\u00e4.",
   "T\u00e4m\u00e4n kielen \u00e4\u00e4ni\u00e4 on testattu paljon v\u00e4hemm\u00e4n kuin englanninkielisi\u00e4 \u00e4\u00e4ni\u00e4.",
   "T\u00e4lle kielelle ei ole valmiita \u00e4\u00e4nt\u00e4miss\u00e4\u00e4nt\u00f6j\u00e4. Voit lis\u00e4t\u00e4 omia s\u00e4\u00e4nt\u00f6j\u00e4 v\u00e4lilehdell\u00e4 \u201dPronunciation\u201d.",
   "Englanninkielisi\u00e4 \u00e4\u00e4ni\u00e4 ei n\u00e4ytet\u00e4 luettelossa. Voit lis\u00e4t\u00e4 ne my\u00f6hemmin painikkeella \u201dAdditional languages\u201d."
  ],
  "name": "Finnish",
  "native": "Suomi",
  "title": "Huomaa"
 },
 "fr": {
  "lines": [
   "Les fen\u00eatres et les boutons de ce programme sont uniquement en anglais.",
   "Les quelques textes dans votre langue ont \u00e9t\u00e9 traduits par un ordinateur et n'ont pas \u00e9t\u00e9 v\u00e9rifi\u00e9s. Ils peuvent contenir des erreurs.",
   "Les voix dans cette langue ont \u00e9t\u00e9 beaucoup moins test\u00e9es que les voix anglaises.",
   "Il n'existe pas de r\u00e8gles de prononciation toutes faites pour cette langue. Vous pouvez ajouter les v\u00f4tres dans l'onglet \u00ab Pronunciation \u00bb.",
   "Les voix anglaises ne seront pas affich\u00e9es. Vous pourrez les ajouter plus tard avec le bouton \u00ab Additional languages \u00bb."
  ],
  "name": "French",
  "native": "Fran\u00e7ais",
  "title": "\u00c0 noter"
 },
 "gl": {
  "lines": [
   "As vent\u00e1s e os bot\u00f3ns deste programa est\u00e1n s\u00f3 en ingl\u00e9s.",
   "Os poucos textos na s\u00faa lingua foron traducidos por un ordenador e non se revisaron. Poden conter erros.",
   "As voces nesta lingua prob\u00e1ronse moito menos ca as voces en ingl\u00e9s.",
   "Non hai regras de pronuncia xa preparadas para esta lingua. Pode engadir as s\u00faas na lapela \u00abPronunciation\u00bb.",
   "As voces en ingl\u00e9s non aparecer\u00e1n na lista. Pode engadilas m\u00e1is adiante co bot\u00f3n \u00abAdditional languages\u00bb."
  ],
  "name": "Galician",
  "native": "Galego",
  "title": "Te\u00f1a en conta"
 },
 "hu": {
  "lines": [
   "A program ablakai \u00e9s gombjai csak angol nyelv\u0171ek.",
   "Az \u00d6n nyelv\u00e9n megjelen\u0151 n\u00e9h\u00e1ny sz\u00f6veget sz\u00e1m\u00edt\u00f3g\u00e9p ford\u00edtotta, \u00e9s senki sem ellen\u0151rizte. Hib\u00e1k lehetnek benn\u00fck.",
   "Az ezen a nyelven besz\u00e9l\u0151 hangokat sokkal kevesebbet tesztelt\u00e9k, mint az angol hangokat.",
   "Ehhez a nyelvhez nincsenek k\u00e9sz kiejt\u00e9si szab\u00e1lyok. Saj\u00e1tokat a \u201ePronunciation\u201d lapon adhat hozz\u00e1.",
   "Az angol hangok nem jelennek meg a list\u00e1ban. K\u00e9s\u0151bb az \u201eAdditional languages\u201d gombbal adhatja hozz\u00e1 \u0151ket."
  ],
  "name": "Hungarian",
  "native": "Magyar",
  "title": "Figyelem"
 },
 "is": {
  "lines": [
   "Gluggar og hnappar \u00feessa forrits eru eing\u00f6ngu \u00e1 ensku.",
   "\u00deeir f\u00e1u textar sem eru \u00e1 \u00fe\u00ednu tungum\u00e1li voru \u00fe\u00fdddir af t\u00f6lvu og hafa ekki veri\u00f0 yfirfarnir. \u00cd \u00feeim geta veri\u00f0 villur.",
   "Raddir \u00e1 \u00feessu tungum\u00e1li hafa veri\u00f0 pr\u00f3fa\u00f0ar mun minna en ensku raddirnar.",
   "Engar tilb\u00fanar frambur\u00f0arreglur eru til fyrir \u00feetta tungum\u00e1l. \u00de\u00fa getur b\u00e6tt vi\u00f0 \u00fe\u00ednum eigin \u00e1 flipanum \u201ePronunciation\u201c.",
   "Enskar raddir ver\u00f0a ekki \u00e1 listanum. \u00de\u00fa getur b\u00e6tt \u00feeim vi\u00f0 s\u00ed\u00f0ar me\u00f0 hnappinum \u201eAdditional languages\u201c."
  ],
  "name": "Icelandic",
  "native": "\u00cdslenska",
  "title": "Athugi\u00f0"
 },
 "it": {
  "lines": [
   "Le finestre e i pulsanti di questo programma sono solo in inglese.",
   "I pochi testi nella tua lingua sono stati tradotti da un computer e non sono stati controllati. Possono contenere errori.",
   "Le voci in questa lingua sono state provate molto meno delle voci inglesi.",
   "Non ci sono regole di pronuncia gi\u00e0 pronte per questa lingua. Puoi aggiungere le tue nella scheda \u00abPronunciation\u00bb.",
   "Le voci inglesi non saranno elencate. Puoi aggiungerle pi\u00f9 tardi con il pulsante \u00abAdditional languages\u00bb."
  ],
  "name": "Italian",
  "native": "Italiano",
  "title": "Nota"
 },
 "lb": {
  "lines": [
   "D'F\u00ebnsteren an d'Kn\u00e4ppercher vun d\u00ebsem Programm sinn n\u00ebmmen op Englesch.",
   "D\u00e9i puer Texter an \u00c4rer Sprooch goufe vun engem Computer iwwersat an net iwwerpr\u00e9ift. Si k\u00ebnne Feeler enthalen.",
   "D'St\u00ebmmen an d\u00ebser Sprooch goufe vill manner getest w\u00e9i d\u00e9i englesch St\u00ebmmen.",
   "Fir d\u00ebs Sprooch g\u00ebtt et keng f\u00e4erdeg Aussproochreegelen. Dir k\u00ebnnt \u00c4r eegen am Tab \u201ePronunciation\u201c derb\u00e4isetzen.",
   "Englesch St\u00ebmme ginn net opgel\u00ebscht. Dir k\u00ebnnt se m\u00e9i sp\u00e9it mam Kn\u00e4ppchen \u201eAdditional languages\u201c derb\u00e4isetzen."
  ],
  "name": "Luxembourgish",
  "native": "L\u00ebtzebuergesch",
  "title": "Opgepasst"
 },
 "lv": {
  "lines": [
   "\u0160\u012bs programmas logi un pogas ir tikai ang\u013cu valod\u0101.",
   "Da\u017eus tekstus j\u016bsu valod\u0101 ir tulkojis dators, un tie nav p\u0101rbaud\u012bti. Tajos var b\u016bt k\u013c\u016bdas.",
   "Balsis \u0161aj\u0101 valod\u0101 ir p\u0101rbaud\u012btas daudz maz\u0101k nek\u0101 ang\u013cu balsis.",
   "\u0160ai valodai nav gatavu izrunas noteikumu. Savus varat pievienot ciln\u0113 \u201ePronunciation\u201d.",
   "Ang\u013cu balsis sarakst\u0101 netiks r\u0101d\u012btas. T\u0101s varat pievienot v\u0113l\u0101k ar pogu \u201eAdditional languages\u201d."
  ],
  "name": "Latvian",
  "native": "Latvie\u0161u",
  "title": "L\u016bdzu, \u0146emiet v\u0113r\u0101"
 },
 "nl": {
  "lines": [
   "De vensters en knoppen van dit programma zijn alleen in het Engels.",
   "De paar teksten in uw taal zijn door een computer vertaald en niet gecontroleerd. Ze kunnen fouten bevatten.",
   "De stemmen in deze taal zijn veel minder getest dan de Engelse stemmen.",
   "Er zijn geen kant-en-klare uitspraakregels voor deze taal. U kunt uw eigen regels toevoegen op het tabblad \u2018Pronunciation\u2019.",
   "Engelse stemmen worden niet getoond. U kunt ze later toevoegen met de knop \u2018Additional languages\u2019."
  ],
  "name": "Dutch",
  "native": "Nederlands",
  "title": "Let op"
 },
 "no": {
  "lines": [
   "Vinduene og knappene i dette programmet er bare p\u00e5 engelsk.",
   "De f\u00e5 tekstene p\u00e5 ditt spr\u00e5k er oversatt av en datamaskin og er ikke kontrollert. De kan inneholde feil.",
   "Stemmene p\u00e5 dette spr\u00e5ket er testet mye mindre enn de engelske stemmene.",
   "Det finnes ingen ferdige uttaleregler for dette spr\u00e5ket. Du kan legge til dine egne p\u00e5 fanen \u00abPronunciation\u00bb.",
   "Engelske stemmer blir ikke vist. Du kan legge dem til senere med knappen \u00abAdditional languages\u00bb."
  ],
  "name": "Norwegian",
  "native": "Norsk",
  "title": "Merk"
 },
 "pl": {
  "lines": [
   "Okna i przyciski tego programu s\u0105 tylko w j\u0119zyku angielskim.",
   "Nieliczne teksty w Twoim j\u0119zyku zosta\u0142y przet\u0142umaczone przez komputer i nie zosta\u0142y sprawdzone. Mog\u0105 zawiera\u0107 b\u0142\u0119dy.",
   "G\u0142osy w tym j\u0119zyku by\u0142y testowane znacznie mniej ni\u017c g\u0142osy angielskie.",
   "Dla tego j\u0119zyka nie ma gotowych regu\u0142 wymowy. W\u0142asne mo\u017cesz doda\u0107 na karcie \u201ePronunciation\u201d.",
   "G\u0142osy angielskie nie b\u0119d\u0105 wy\u015bwietlane. Mo\u017cesz je doda\u0107 p\u00f3\u017aniej przyciskiem \u201eAdditional languages\u201d."
  ],
  "name": "Polish",
  "native": "Polski",
  "title": "Uwaga"
 },
 "pt": {
  "lines": [
   "As janelas e os bot\u00f5es deste programa est\u00e3o apenas em ingl\u00eas.",
   "Os poucos textos no seu idioma foram traduzidos por um computador e n\u00e3o foram revistos. Podem conter erros.",
   "As vozes neste idioma foram muito menos testadas do que as vozes em ingl\u00eas.",
   "N\u00e3o h\u00e1 regras de pron\u00fancia j\u00e1 prontas para este idioma. Pode adicionar as suas na aba \u00abPronunciation\u00bb.",
   "As vozes em ingl\u00eas n\u00e3o ser\u00e3o listadas. Pode adicion\u00e1-las mais tarde com o bot\u00e3o \u00abAdditional languages\u00bb."
  ],
  "name": "Portuguese",
  "native": "Portugu\u00eas",
  "title": "Aten\u00e7\u00e3o"
 },
 "ro": {
  "lines": [
   "Ferestrele \u0219i butoanele acestui program sunt numai \u00een limba englez\u0103.",
   "Pu\u021binele texte \u00een limba dumneavoastr\u0103 au fost traduse de un calculator \u0219i nu au fost verificate. Pot con\u021bine gre\u0219eli.",
   "Vocile \u00een aceast\u0103 limb\u0103 au fost testate mult mai pu\u021bin dec\u00e2t vocile \u00een englez\u0103.",
   "Nu exist\u0103 reguli de pronun\u021bie gata f\u0103cute pentru aceast\u0103 limb\u0103. Le pute\u021bi ad\u0103uga pe ale dumneavoastr\u0103 \u00een fila \u201ePronunciation\u201d.",
   "Vocile \u00een englez\u0103 nu vor fi afi\u0219ate. Le pute\u021bi ad\u0103uga mai t\u00e2rziu cu butonul \u201eAdditional languages\u201d."
  ],
  "name": "Romanian",
  "native": "Rom\u00e2n\u0103",
  "title": "Re\u021bine\u021bi"
 },
 "ru": {
  "lines": [
   "\u041e\u043a\u043d\u0430 \u0438 \u043a\u043d\u043e\u043f\u043a\u0438 \u044d\u0442\u043e\u0439 \u043f\u0440\u043e\u0433\u0440\u0430\u043c\u043c\u044b \u0442\u043e\u043b\u044c\u043a\u043e \u043d\u0430 \u0430\u043d\u0433\u043b\u0438\u0439\u0441\u043a\u043e\u043c \u044f\u0437\u044b\u043a\u0435.",
   "\u041d\u0435\u043c\u043d\u043e\u0433\u0438\u0435 \u0442\u0435\u043a\u0441\u0442\u044b \u043d\u0430 \u0432\u0430\u0448\u0435\u043c \u044f\u0437\u044b\u043a\u0435 \u043f\u0435\u0440\u0435\u0432\u0435\u0434\u0435\u043d\u044b \u043a\u043e\u043c\u043f\u044c\u044e\u0442\u0435\u0440\u043e\u043c \u0438 \u043d\u0435 \u043f\u0440\u043e\u0432\u0435\u0440\u0435\u043d\u044b. \u0412 \u043d\u0438\u0445 \u043c\u043e\u0433\u0443\u0442 \u0431\u044b\u0442\u044c \u043e\u0448\u0438\u0431\u043a\u0438.",
   "\u0413\u043e\u043b\u043e\u0441\u0430 \u043d\u0430 \u044d\u0442\u043e\u043c \u044f\u0437\u044b\u043a\u0435 \u043f\u0440\u043e\u0432\u0435\u0440\u0435\u043d\u044b \u0433\u043e\u0440\u0430\u0437\u0434\u043e \u043c\u0435\u043d\u044c\u0448\u0435, \u0447\u0435\u043c \u0430\u043d\u0433\u043b\u0438\u0439\u0441\u043a\u0438\u0435 \u0433\u043e\u043b\u043e\u0441\u0430.",
   "\u0414\u043b\u044f \u044d\u0442\u043e\u0433\u043e \u044f\u0437\u044b\u043a\u0430 \u043d\u0435\u0442 \u0433\u043e\u0442\u043e\u0432\u044b\u0445 \u043f\u0440\u0430\u0432\u0438\u043b \u043f\u0440\u043e\u0438\u0437\u043d\u043e\u0448\u0435\u043d\u0438\u044f. \u0421\u0432\u043e\u0438 \u043f\u0440\u0430\u0432\u0438\u043b\u0430 \u043c\u043e\u0436\u043d\u043e \u0434\u043e\u0431\u0430\u0432\u0438\u0442\u044c \u043d\u0430 \u0432\u043a\u043b\u0430\u0434\u043a\u0435 \u00abPronunciation\u00bb.",
   "\u0410\u043d\u0433\u043b\u0438\u0439\u0441\u043a\u0438\u0435 \u0433\u043e\u043b\u043e\u0441\u0430 \u043d\u0435 \u0431\u0443\u0434\u0443\u0442 \u043f\u043e\u043a\u0430\u0437\u0430\u043d\u044b \u0432 \u0441\u043f\u0438\u0441\u043a\u0435. \u0418\u0445 \u043c\u043e\u0436\u043d\u043e \u0434\u043e\u0431\u0430\u0432\u0438\u0442\u044c \u043f\u043e\u0437\u0436\u0435 \u043a\u043d\u043e\u043f\u043a\u043e\u0439 \u00abAdditional languages\u00bb."
  ],
  "name": "Russian",
  "native": "\u0420\u0443\u0441\u0441\u043a\u0438\u0439",
  "title": "\u041e\u0431\u0440\u0430\u0442\u0438\u0442\u0435 \u0432\u043d\u0438\u043c\u0430\u043d\u0438\u0435"
 },
 "sk": {
  "lines": [
   "Okn\u00e1 a tla\u010didl\u00e1 tohto programu s\u00fa iba v angli\u010dtine.",
   "T\u00fdch nieko\u013eko textov vo va\u0161om jazyku prelo\u017eil po\u010d\u00edta\u010d a nikto ich neskontroloval. M\u00f4\u017eu obsahova\u0165 chyby.",
   "Hlasy v tomto jazyku boli testovan\u00e9 ove\u013ea menej ako anglick\u00e9 hlasy.",
   "Pre tento jazyk nie s\u00fa pripraven\u00e9 \u017eiadne pravidl\u00e1 v\u00fdslovnosti. Vlastn\u00e9 m\u00f4\u017eete prida\u0165 na karte \u201ePronunciation\u201c.",
   "Anglick\u00e9 hlasy nebud\u00fa v zozname. Nesk\u00f4r ich m\u00f4\u017eete prida\u0165 tla\u010didlom \u201eAdditional languages\u201c."
  ],
  "name": "Slovak",
  "native": "Sloven\u010dina",
  "title": "Upozornenie"
 },
 "sl": {
  "lines": [
   "Okna in gumbi tega programa so samo v angle\u0161\u010dini.",
   "Tistih nekaj besedil v va\u0161em jeziku je prevedel ra\u010dunalnik in niso bila preverjena. Lahko vsebujejo napake.",
   "Glasovi v tem jeziku so bili preizku\u0161eni veliko manj kot angle\u0161ki glasovi.",
   "Za ta jezik ni pripravljenih pravil izgovorjave. Svoja lahko dodate na zavihku \u00bbPronunciation\u00ab.",
   "Angle\u0161ki glasovi ne bodo prikazani. Pozneje jih lahko dodate z gumbom \u00bbAdditional languages\u00ab."
  ],
  "name": "Slovenian",
  "native": "Sloven\u0161\u010dina",
  "title": "Upo\u0161tevajte"
 },
 "sr": {
  "lines": [
   "Prozori i dugmad ovog programa su samo na engleskom jeziku.",
   "Ono malo tekstova na va\u0161em jeziku preveo je ra\u010dunar i nisu provereni. Mogu da sadr\u017ee gre\u0161ke.",
   "Glasovi na ovom jeziku su testirani mnogo manje od engleskih glasova.",
   "Za ovaj jezik nema gotovih pravila izgovora. Svoja mo\u017eete dodati na kartici \u201ePronunciation\u201c.",
   "Engleski glasovi ne\u0107e biti prikazani. Mo\u017eete ih dodati kasnije pomo\u0107u dugmeta \u201eAdditional languages\u201c."
  ],
  "name": "Serbian",
  "native": "Srpski",
  "title": "Napomena"
 },
 "sv": {
  "lines": [
   "Programmets f\u00f6nster och knappar finns bara p\u00e5 engelska.",
   "De f\u00e5 texterna p\u00e5 ditt spr\u00e5k har \u00f6versatts av en dator och har inte kontrollerats. De kan inneh\u00e5lla fel.",
   "R\u00f6sterna p\u00e5 det h\u00e4r spr\u00e5ket har testats mycket mindre \u00e4n de engelska r\u00f6sterna.",
   "Det finns inga f\u00e4rdiga uttalsregler f\u00f6r det h\u00e4r spr\u00e5ket. Du kan l\u00e4gga till egna p\u00e5 fliken \u201dPronunciation\u201d.",
   "Engelska r\u00f6ster visas inte i listan. Du kan l\u00e4gga till dem senare med knappen \u201dAdditional languages\u201d."
  ],
  "name": "Swedish",
  "native": "Svenska",
  "title": "Observera"
 },
 "tr": {
  "lines": [
   "Bu program\u0131n pencereleri ve d\u00fc\u011fmeleri yaln\u0131zca \u0130ngilizcedir.",
   "Dilinizdeki az say\u0131daki metin bir bilgisayar taraf\u0131ndan \u00e7evrilmi\u015ftir ve kontrol edilmemi\u015ftir. Hatalar i\u00e7erebilir.",
   "Bu dildeki sesler, \u0130ngilizce seslere g\u00f6re \u00e7ok daha az test edilmi\u015ftir.",
   "Bu dil i\u00e7in haz\u0131r telaffuz kurallar\u0131 yoktur. Kendi kurallar\u0131n\u0131z\u0131 \u201cPronunciation\u201d sekmesinden ekleyebilirsiniz.",
   "\u0130ngilizce sesler listelenmeyecektir. Bunlar\u0131 daha sonra \u201cAdditional languages\u201d d\u00fc\u011fmesiyle ekleyebilirsiniz."
  ],
  "name": "Turkish",
  "native": "T\u00fcrk\u00e7e",
  "title": "L\u00fctfen dikkat"
 },
 "uk": {
  "lines": [
   "\u0412\u0456\u043a\u043d\u0430 \u0442\u0430 \u043a\u043d\u043e\u043f\u043a\u0438 \u0446\u0456\u0454\u0457 \u043f\u0440\u043e\u0433\u0440\u0430\u043c\u0438 \u043b\u0438\u0448\u0435 \u0430\u043d\u0433\u043b\u0456\u0439\u0441\u044c\u043a\u043e\u044e \u043c\u043e\u0432\u043e\u044e.",
   "\u041d\u0435\u0447\u0438\u0441\u043b\u0435\u043d\u043d\u0456 \u0442\u0435\u043a\u0441\u0442\u0438 \u0432\u0430\u0448\u043e\u044e \u043c\u043e\u0432\u043e\u044e \u043f\u0435\u0440\u0435\u043a\u043b\u0430\u0434\u0435\u043d\u043e \u043a\u043e\u043c\u043f'\u044e\u0442\u0435\u0440\u043e\u043c \u0456 \u043d\u0435 \u043f\u0435\u0440\u0435\u0432\u0456\u0440\u0435\u043d\u043e. \u0423 \u043d\u0438\u0445 \u043c\u043e\u0436\u0443\u0442\u044c \u0431\u0443\u0442\u0438 \u043f\u043e\u043c\u0438\u043b\u043a\u0438.",
   "\u0413\u043e\u043b\u043e\u0441\u0438 \u0446\u0456\u0454\u044e \u043c\u043e\u0432\u043e\u044e \u043f\u0435\u0440\u0435\u0432\u0456\u0440\u0435\u043d\u043e \u043d\u0430\u0431\u0430\u0433\u0430\u0442\u043e \u043c\u0435\u043d\u0448\u0435, \u043d\u0456\u0436 \u0430\u043d\u0433\u043b\u0456\u0439\u0441\u044c\u043a\u0456 \u0433\u043e\u043b\u043e\u0441\u0438.",
   "\u0414\u043b\u044f \u0446\u0456\u0454\u0457 \u043c\u043e\u0432\u0438 \u043d\u0435\u043c\u0430\u0454 \u0433\u043e\u0442\u043e\u0432\u0438\u0445 \u043f\u0440\u0430\u0432\u0438\u043b \u0432\u0438\u043c\u043e\u0432\u0438. \u0421\u0432\u043e\u0457 \u043f\u0440\u0430\u0432\u0438\u043b\u0430 \u043c\u043e\u0436\u043d\u0430 \u0434\u043e\u0434\u0430\u0442\u0438 \u043d\u0430 \u0432\u043a\u043b\u0430\u0434\u0446\u0456 \u00abPronunciation\u00bb.",
   "\u0410\u043d\u0433\u043b\u0456\u0439\u0441\u044c\u043a\u0456 \u0433\u043e\u043b\u043e\u0441\u0438 \u043d\u0435 \u0431\u0443\u0434\u0435 \u043f\u043e\u043a\u0430\u0437\u0430\u043d\u043e \u0443 \u0441\u043f\u0438\u0441\u043a\u0443. \u0407\u0445 \u043c\u043e\u0436\u043d\u0430 \u0434\u043e\u0434\u0430\u0442\u0438 \u043f\u0456\u0437\u043d\u0456\u0448\u0435 \u043a\u043d\u043e\u043f\u043a\u043e\u044e \u00abAdditional languages\u00bb."
  ],
  "name": "Ukrainian",
  "native": "\u0423\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430",
  "title": "\u0417\u0432\u0435\u0440\u043d\u0456\u0442\u044c \u0443\u0432\u0430\u0433\u0443"
 }
}
'@
