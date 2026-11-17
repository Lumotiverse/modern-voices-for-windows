# Writes installer/Languages.ps1: the installer's language choice (names, and the note shown for a
# language other than English), as JSON in plain ASCII (\u escapes) inside a here-string.
import json, sys
P, A = "Pronunciation", "Additional languages"
L = {}
def add(code, name, native, title, lines):
    assert len(lines) == 5
    L[code] = {"name": name, "native": native, "title": title, "lines": lines}

add("bg", "Bulgarian", "Български", "Моля, обърнете внимание", [
 "Прозорците и бутоните на тази програма са само на английски език.",
 "Малкото текстове на вашия език са преведени от компютър и не са проверени. Възможно е да съдържат грешки.",
 "Гласовете на този език са тествани много по-малко от английските гласове.",
 "За този език няма готови правила за произношение. Можете да добавите свои в раздела „%s“." % P,
 "Английските гласове няма да бъдат показани. Можете да ги добавите по-късно с бутона „%s“." % A])
add("ca", "Catalan", "Català", "Tingueu en compte", [
 "Les finestres i els botons d'aquest programa només estan en anglès.",
 "Els pocs textos en la vostra llengua els ha traduït un ordinador i no s'han revisat. Poden contenir errors.",
 "Les veus en aquesta llengua s'han provat molt menys que les veus en anglès.",
 "No hi ha regles de pronunciació ja fetes per a aquesta llengua. Podeu afegir les vostres a la pestanya «%s»." % P,
 "Les veus en anglès no es mostraran. Les podeu afegir més endavant amb el botó «%s»." % A])
add("cs", "Czech", "Čeština", "Upozornění", [
 "Okna a tlačítka tohoto programu jsou pouze v angličtině.",
 "Těch několik textů ve vašem jazyce přeložil počítač a nikdo je nezkontroloval. Mohou obsahovat chyby.",
 "Hlasy v tomto jazyce byly testovány mnohem méně než anglické hlasy.",
 "Pro tento jazyk nejsou připravena žádná pravidla výslovnosti. Vlastní můžete přidat na kartě „%s“." % P,
 "Anglické hlasy nebudou v seznamu. Později je můžete přidat tlačítkem „%s“." % A])
add("cy", "Welsh", "Cymraeg", "Sylwch", [
 "Mae ffenestri a botymau'r rhaglen hon yn Saesneg yn unig.",
 "Cyfieithwyd yr ychydig destunau yn eich iaith gan gyfrifiadur ac nid ydynt wedi'u gwirio. Gallant gynnwys camgymeriadau.",
 "Mae'r lleisiau yn yr iaith hon wedi'u profi lawer llai na'r lleisiau Saesneg.",
 "Nid oes rheolau ynganu parod ar gyfer yr iaith hon. Gallwch ychwanegu eich rhai eich hun ar y tab “%s”." % P,
 "Ni fydd lleisiau Saesneg yn cael eu rhestru. Gallwch eu hychwanegu yn nes ymlaen gyda'r botwm “%s”." % A])
add("da", "Danish", "Dansk", "Bemærk", [
 "Programmets vinduer og knapper er kun på engelsk.",
 "De få tekster på dit sprog er oversat af en computer og er ikke blevet kontrolleret. De kan indeholde fejl.",
 "Stemmerne på dette sprog er testet meget mindre end de engelske stemmer.",
 "Der er ingen færdige udtaleregler til dette sprog. Du kan tilføje dine egne på fanen »%s«." % P,
 "Engelske stemmer bliver ikke vist. Du kan tilføje dem senere med knappen »%s«." % A])
add("de", "German", "Deutsch", "Bitte beachten", [
 "Die Fenster und Schaltflächen dieses Programms sind nur auf Englisch.",
 "Die wenigen Texte in Ihrer Sprache wurden von einem Computer übersetzt und nicht geprüft. Sie können Fehler enthalten.",
 "Die Stimmen in dieser Sprache wurden viel weniger getestet als die englischen Stimmen.",
 "Für diese Sprache gibt es keine fertigen Ausspracheregeln. Eigene Regeln können Sie auf der Registerkarte „%s“ hinzufügen." % P,
 "Englische Stimmen werden nicht aufgelistet. Sie können sie später über die Schaltfläche „%s“ hinzufügen." % A])
add("el", "Greek", "Ελληνικά", "Σημείωση", [
 "Τα παράθυρα και τα κουμπιά αυτού του προγράμματος είναι μόνο στα αγγλικά.",
 "Τα λίγα κείμενα στη γλώσσα σας μεταφράστηκαν από υπολογιστή και δεν έχουν ελεγχθεί. Μπορεί να περιέχουν λάθη.",
 "Οι φωνές σε αυτή τη γλώσσα έχουν δοκιμαστεί πολύ λιγότερο από τις αγγλικές φωνές.",
 "Δεν υπάρχουν έτοιμοι κανόνες προφοράς για αυτή τη γλώσσα. Μπορείτε να προσθέσετε δικούς σας στην καρτέλα «%s»." % P,
 "Οι αγγλικές φωνές δεν θα εμφανίζονται. Μπορείτε να τις προσθέσετε αργότερα με το κουμπί «%s»." % A])
add("es", "Spanish", "Español", "Tenga en cuenta", [
 "Las ventanas y los botones de este programa están solo en inglés.",
 "Los pocos textos en su idioma fueron traducidos por un ordenador y no se han revisado. Pueden contener errores.",
 "Las voces en este idioma se han probado mucho menos que las voces en inglés.",
 "No hay reglas de pronunciación ya preparadas para este idioma. Puede añadir las suyas en la pestaña «%s»." % P,
 "Las voces en inglés no aparecerán en la lista. Puede añadirlas más adelante con el botón «%s»." % A])
add("eu", "Basque", "Euskara", "Kontuan izan", [
 "Programa honen leihoak eta botoiak ingelesez bakarrik daude.",
 "Zure hizkuntzan dauden testu apurrak ordenagailu batek itzuli ditu eta ez dira berrikusi. Akatsak izan ditzakete.",
 "Hizkuntza honetako ahotsak ingelesezkoak baino askoz gutxiago probatu dira.",
 "Ez dago hizkuntza honetarako prestatutako ahoskera-araurik. Zureak gehi ditzakezu «%s» fitxan." % P,
 "Ingelesezko ahotsak ez dira zerrendan agertuko. Geroago gehi ditzakezu «%s» botoiarekin." % A])
add("fi", "Finnish", "Suomi", "Huomaa", [
 "Tämän ohjelman ikkunat ja painikkeet ovat vain englanniksi.",
 "Harvat omalla kielelläsi olevat tekstit on kääntänyt tietokone, eikä niitä ole tarkistettu. Niissä voi olla virheitä.",
 "Tämän kielen ääniä on testattu paljon vähemmän kuin englanninkielisiä ääniä.",
 "Tälle kielelle ei ole valmiita ääntämissääntöjä. Voit lisätä omia sääntöjä välilehdellä ”%s”." % P,
 "Englanninkielisiä ääniä ei näytetä luettelossa. Voit lisätä ne myöhemmin painikkeella ”%s”." % A])
add("fr", "French", "Français", "À noter", [
 "Les fenêtres et les boutons de ce programme sont uniquement en anglais.",
 "Les quelques textes dans votre langue ont été traduits par un ordinateur et n'ont pas été vérifiés. Ils peuvent contenir des erreurs.",
 "Les voix dans cette langue ont été beaucoup moins testées que les voix anglaises.",
 "Il n'existe pas de règles de prononciation toutes faites pour cette langue. Vous pouvez ajouter les vôtres dans l'onglet « %s »." % P,
 "Les voix anglaises ne seront pas affichées. Vous pourrez les ajouter plus tard avec le bouton « %s »." % A])
add("gl", "Galician", "Galego", "Teña en conta", [
 "As ventás e os botóns deste programa están só en inglés.",
 "Os poucos textos na súa lingua foron traducidos por un ordenador e non se revisaron. Poden conter erros.",
 "As voces nesta lingua probáronse moito menos ca as voces en inglés.",
 "Non hai regras de pronuncia xa preparadas para esta lingua. Pode engadir as súas na lapela «%s»." % P,
 "As voces en inglés non aparecerán na lista. Pode engadilas máis adiante co botón «%s»." % A])
add("hu", "Hungarian", "Magyar", "Figyelem", [
 "A program ablakai és gombjai csak angol nyelvűek.",
 "Az Ön nyelvén megjelenő néhány szöveget számítógép fordította, és senki sem ellenőrizte. Hibák lehetnek bennük.",
 "Az ezen a nyelven beszélő hangokat sokkal kevesebbet tesztelték, mint az angol hangokat.",
 "Ehhez a nyelvhez nincsenek kész kiejtési szabályok. Sajátokat a „%s” lapon adhat hozzá." % P,
 "Az angol hangok nem jelennek meg a listában. Később az „%s” gombbal adhatja hozzá őket." % A])
add("is", "Icelandic", "Íslenska", "Athugið", [
 "Gluggar og hnappar þessa forrits eru eingöngu á ensku.",
 "Þeir fáu textar sem eru á þínu tungumáli voru þýddir af tölvu og hafa ekki verið yfirfarnir. Í þeim geta verið villur.",
 "Raddir á þessu tungumáli hafa verið prófaðar mun minna en ensku raddirnar.",
 "Engar tilbúnar framburðarreglur eru til fyrir þetta tungumál. Þú getur bætt við þínum eigin á flipanum „%s“." % P,
 "Enskar raddir verða ekki á listanum. Þú getur bætt þeim við síðar með hnappinum „%s“." % A])
add("it", "Italian", "Italiano", "Nota", [
 "Le finestre e i pulsanti di questo programma sono solo in inglese.",
 "I pochi testi nella tua lingua sono stati tradotti da un computer e non sono stati controllati. Possono contenere errori.",
 "Le voci in questa lingua sono state provate molto meno delle voci inglesi.",
 "Non ci sono regole di pronuncia già pronte per questa lingua. Puoi aggiungere le tue nella scheda «%s»." % P,
 "Le voci inglesi non saranno elencate. Puoi aggiungerle più tardi con il pulsante «%s»." % A])
add("lb", "Luxembourgish", "Lëtzebuergesch", "Opgepasst", [
 "D'Fënsteren an d'Knäppercher vun dësem Programm sinn nëmmen op Englesch.",
 "Déi puer Texter an Ärer Sprooch goufe vun engem Computer iwwersat an net iwwerpréift. Si kënne Feeler enthalen.",
 "D'Stëmmen an dëser Sprooch goufe vill manner getest wéi déi englesch Stëmmen.",
 "Fir dës Sprooch gëtt et keng fäerdeg Aussproochreegelen. Dir kënnt Är eegen am Tab „%s“ derbäisetzen." % P,
 "Englesch Stëmme ginn net opgelëscht. Dir kënnt se méi spéit mam Knäppchen „%s“ derbäisetzen." % A])
add("lv", "Latvian", "Latviešu", "Lūdzu, ņemiet vērā", [
 "Šīs programmas logi un pogas ir tikai angļu valodā.",
 "Dažus tekstus jūsu valodā ir tulkojis dators, un tie nav pārbaudīti. Tajos var būt kļūdas.",
 "Balsis šajā valodā ir pārbaudītas daudz mazāk nekā angļu balsis.",
 "Šai valodai nav gatavu izrunas noteikumu. Savus varat pievienot cilnē „%s”." % P,
 "Angļu balsis sarakstā netiks rādītas. Tās varat pievienot vēlāk ar pogu „%s”." % A])
add("nl", "Dutch", "Nederlands", "Let op", [
 "De vensters en knoppen van dit programma zijn alleen in het Engels.",
 "De paar teksten in uw taal zijn door een computer vertaald en niet gecontroleerd. Ze kunnen fouten bevatten.",
 "De stemmen in deze taal zijn veel minder getest dan de Engelse stemmen.",
 "Er zijn geen kant-en-klare uitspraakregels voor deze taal. U kunt uw eigen regels toevoegen op het tabblad ‘%s’." % P,
 "Engelse stemmen worden niet getoond. U kunt ze later toevoegen met de knop ‘%s’." % A])
add("no", "Norwegian", "Norsk", "Merk", [
 "Vinduene og knappene i dette programmet er bare på engelsk.",
 "De få tekstene på ditt språk er oversatt av en datamaskin og er ikke kontrollert. De kan inneholde feil.",
 "Stemmene på dette språket er testet mye mindre enn de engelske stemmene.",
 "Det finnes ingen ferdige uttaleregler for dette språket. Du kan legge til dine egne på fanen «%s»." % P,
 "Engelske stemmer blir ikke vist. Du kan legge dem til senere med knappen «%s»." % A])
add("pl", "Polish", "Polski", "Uwaga", [
 "Okna i przyciski tego programu są tylko w języku angielskim.",
 "Nieliczne teksty w Twoim języku zostały przetłumaczone przez komputer i nie zostały sprawdzone. Mogą zawierać błędy.",
 "Głosy w tym języku były testowane znacznie mniej niż głosy angielskie.",
 "Dla tego języka nie ma gotowych reguł wymowy. Własne możesz dodać na karcie „%s”." % P,
 "Głosy angielskie nie będą wyświetlane. Możesz je dodać później przyciskiem „%s”." % A])
add("pt", "Portuguese", "Português", "Atenção", [
 "As janelas e os botões deste programa estão apenas em inglês.",
 "Os poucos textos no seu idioma foram traduzidos por um computador e não foram revistos. Podem conter erros.",
 "As vozes neste idioma foram muito menos testadas do que as vozes em inglês.",
 "Não há regras de pronúncia já prontas para este idioma. Pode adicionar as suas na aba «%s»." % P,
 "As vozes em inglês não serão listadas. Pode adicioná-las mais tarde com o botão «%s»." % A])
add("ro", "Romanian", "Română", "Rețineți", [
 "Ferestrele și butoanele acestui program sunt numai în limba engleză.",
 "Puținele texte în limba dumneavoastră au fost traduse de un calculator și nu au fost verificate. Pot conține greșeli.",
 "Vocile în această limbă au fost testate mult mai puțin decât vocile în engleză.",
 "Nu există reguli de pronunție gata făcute pentru această limbă. Le puteți adăuga pe ale dumneavoastră în fila „%s”." % P,
 "Vocile în engleză nu vor fi afișate. Le puteți adăuga mai târziu cu butonul „%s”." % A])
add("ru", "Russian", "Русский", "Обратите внимание", [
 "Окна и кнопки этой программы только на английском языке.",
 "Немногие тексты на вашем языке переведены компьютером и не проверены. В них могут быть ошибки.",
 "Голоса на этом языке проверены гораздо меньше, чем английские голоса.",
 "Для этого языка нет готовых правил произношения. Свои правила можно добавить на вкладке «%s»." % P,
 "Английские голоса не будут показаны в списке. Их можно добавить позже кнопкой «%s»." % A])
add("sk", "Slovak", "Slovenčina", "Upozornenie", [
 "Okná a tlačidlá tohto programu sú iba v angličtine.",
 "Tých niekoľko textov vo vašom jazyku preložil počítač a nikto ich neskontroloval. Môžu obsahovať chyby.",
 "Hlasy v tomto jazyku boli testované oveľa menej ako anglické hlasy.",
 "Pre tento jazyk nie sú pripravené žiadne pravidlá výslovnosti. Vlastné môžete pridať na karte „%s“." % P,
 "Anglické hlasy nebudú v zozname. Neskôr ich môžete pridať tlačidlom „%s“." % A])
add("sl", "Slovenian", "Slovenščina", "Upoštevajte", [
 "Okna in gumbi tega programa so samo v angleščini.",
 "Tistih nekaj besedil v vašem jeziku je prevedel računalnik in niso bila preverjena. Lahko vsebujejo napake.",
 "Glasovi v tem jeziku so bili preizkušeni veliko manj kot angleški glasovi.",
 "Za ta jezik ni pripravljenih pravil izgovorjave. Svoja lahko dodate na zavihku »%s«." % P,
 "Angleški glasovi ne bodo prikazani. Pozneje jih lahko dodate z gumbom »%s«." % A])
add("sr", "Serbian", "Srpski", "Napomena", [
 "Prozori i dugmad ovog programa su samo na engleskom jeziku.",
 "Ono malo tekstova na vašem jeziku preveo je računar i nisu provereni. Mogu da sadrže greške.",
 "Glasovi na ovom jeziku su testirani mnogo manje od engleskih glasova.",
 "Za ovaj jezik nema gotovih pravila izgovora. Svoja možete dodati na kartici „%s“." % P,
 "Engleski glasovi neće biti prikazani. Možete ih dodati kasnije pomoću dugmeta „%s“." % A])
add("sv", "Swedish", "Svenska", "Observera", [
 "Programmets fönster och knappar finns bara på engelska.",
 "De få texterna på ditt språk har översatts av en dator och har inte kontrollerats. De kan innehålla fel.",
 "Rösterna på det här språket har testats mycket mindre än de engelska rösterna.",
 "Det finns inga färdiga uttalsregler för det här språket. Du kan lägga till egna på fliken ”%s”." % P,
 "Engelska röster visas inte i listan. Du kan lägga till dem senare med knappen ”%s”." % A])
add("tr", "Turkish", "Türkçe", "Lütfen dikkat", [
 "Bu programın pencereleri ve düğmeleri yalnızca İngilizcedir.",
 "Dilinizdeki az sayıdaki metin bir bilgisayar tarafından çevrilmiştir ve kontrol edilmemiştir. Hatalar içerebilir.",
 "Bu dildeki sesler, İngilizce seslere göre çok daha az test edilmiştir.",
 "Bu dil için hazır telaffuz kuralları yoktur. Kendi kurallarınızı “%s” sekmesinden ekleyebilirsiniz." % P,
 "İngilizce sesler listelenmeyecektir. Bunları daha sonra “%s” düğmesiyle ekleyebilirsiniz." % A])
add("uk", "Ukrainian", "Українська", "Зверніть увагу", [
 "Вікна та кнопки цієї програми лише англійською мовою.",
 "Нечисленні тексти вашою мовою перекладено комп'ютером і не перевірено. У них можуть бути помилки.",
 "Голоси цією мовою перевірено набагато менше, ніж англійські голоси.",
 "Для цієї мови немає готових правил вимови. Свої правила можна додати на вкладці «%s»." % P,
 "Англійські голоси не буде показано у списку. Їх можна додати пізніше кнопкою «%s»." % A])

offered = "bg ca cs cy da de el es eu fi fr gl hu is it lb lv nl no pl pt ro ru sk sl sr sv tr uk".split()
assert sorted(L) == sorted(offered), set(offered) ^ set(L)
body = json.dumps(L, ensure_ascii=True, indent=1, sort_keys=True)
assert "'@" not in body and all(ord(c) < 128 for c in body)
head = """# The languages a fresh install can be set up for, beside English (Install.ps1's first window): each
# language's name in English and in itself, and the note shown when it is chosen (a heading and five
# lines; the English of it is $LanguageNoteEnglish in Install.ps1, shown underneath). The same
# languages as Modern Voices Config offers (Catalogue.Offered in config\\Config.cs): keep them in step.
# The translations are Claude's, not checked by a speaker of each. Dot-sourced by Install.ps1.
# Plain ASCII: the letters are written as JSON \\u escapes. Made by tools/dev/make_languages.py: change
# the words there, then run it again.
$LanguageNotesJson = @'
"""
out = head + body + "\n'@\n"
open(sys.argv[1], "w", newline="\r\n").write(out)
print(len(L), "languages,", len(out), "bytes")
