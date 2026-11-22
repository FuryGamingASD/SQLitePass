# Interventi successivi all'audit SQLitePass

> Mappa dei 49 rilievi e delle tranche di lavoro, aggiornata per la 1.1.3. Le evidenze della build pubblicata e i collaudi rinviati sono riportati nel [consuntivo della release](../releases/RELEASE_OUTCOME_1.1.3.md).

> Per lo stato aggiornato al passaggio ad Avalonia vedere il [consuntivo 1.2.0](../releases/RELEASE_OUTCOME_1.2.0.md) e lo [stato dell'audit](../AUDIT_STATUS.md).

> Dettagli storici 1.1.0: [RELEASE_READINESS_2026-11-17.md](../releases/RELEASE_READINESS_2026-11-17.md). Esito 1.1.0: [RELEASE_OUTCOME_2026-11-17.md](../releases/RELEASE_OUTCOME_2026-11-17.md). I 32 punti classificati corretti non equivalgono al completamento di tutte le raccomandazioni narrative o dei collaudi Windows nativi.

L'audit del **16 novembre 2026** riguarda il commit `55fc41a044ad77042c7b6c1733fe7b863deae795` della versione 1.0.2. Gli interventi sono iniziati sul branch `codex/audit-remediation` per la 1.1.0, con il formato vault 2. I risultati della prima tranche sono conservati nei rapporti storici collegati sopra.

La 1.1.3 è pubblicata senza firma. CI, pacchetti e feed pubblico sono stati verificati. Le prove nella VM hanno confermato l'accesso con password e key file, il cambio credenziali, la migrazione e il ripristino su disco locale e la riapertura del vault dopo reinstallazione. Restano le prove V5–V8, il confronto dei file in V1 e parte del rework. [Checklist Windows](../releases/RELEASE_READINESS_1.1.3.md)

L'[audit originale](AUDIT_2026-11-16.md) e le [15 riproduzioni iniziali](AUDIT_EVIDENCE_2026-11-16.json) restano conservati come evidenza della baseline. Questa tabella descrive il codice successivo, senza riscrivere retroattivamente i risultati dell'audit.

## Stato dei 49 punti

**Corretto** indica che il difetto concreto è stato affrontato nel codice e verificato con build e, dove presenti, regressioni automatiche. **Mitigato** indica una correzione significativa con limiti o verifiche operative ancora necessarie. **Parziale** indica che una parte sostanziale dell'intervento resta da completare. Questi stati non sono una certificazione indipendente.

| ID | Stato | Intervento e limite residuo |
| --- | --- | --- |
| A01 | Corretto | La lettura non produce più password placeholder o allegati vuoti su errori crittografici. Il controllo del manifest precede l'uso dei dati; letture degradate bloccano le scritture e il backup automatico. Test su payload alterato e backup invariato. Non è disponibile un recupero parziale forense. |
| A02 | Corretto | Recovery con lease esclusivo, validazione del candidato e del suo contenuto, sostituzione atomica e conservazione di main/WAL/SHM precedenti. Test con un WAL reale lasciato da un processo sintetico separato, credenziale errata e sessione attiva. |
| A03 | Corretto | Epoch della chiave e revisione del record verificate nella transazione. Le sessioni obsolete non possono riscrivere dati dopo un rekey o sovrascrivere una revisione più recente. |
| A04 | Corretto | Formato 2 con AAD legata a vault, tipo di record, ID, parent e revisione; manifest autenticato di header, schema e righe cifrate. Test di trapianto, cancellazione e trigger aggiunti. Anche cancellare tutte le righe e azzerare la firma viene rifiutato all'apertura. Il rollback dell'intero file resta fuori da questo modello di fiducia. |
| A05 | Corretto | La cifratura completa dei payload è obbligatoria nel formato 2: cestino, batch e salvataggio singolo condividono la stessa serializzazione. Il parametro legacy non può indebolire la policy. |
| A06 | Corretto | Il cambio credenziale richiede il key file attuale quando usato e consente un fattore nuovo distinto. Il key file viene letto in streaming. I test automatici coprono anche file di oltre 2 MB e key file senza password. Il flusso di cambio password e key file è passato nella VM. |
| A07 | Mitigato, rischio P1 mantenuto | Auto-Type richiede conferma del destinatario e mostra titolo e nome del processo. In caso di corrispondenza ambigua richiede una scelta esplicita. Controlla finestra, processo e titolo dopo la ricerca asincrona e prima e dopo ciascun evento. Titolo e processo sono indicatori da verificare. Resta la race tra verifica e `SendInput`, che usa il flusso globale di Windows. I test con adattatore finto coprono i cambi del destinatario. Le prove desktop sono rinviate. |
| A08 | Mitigato | Il blocco invalida la sessione, annulla Auto-Type, chiude e pulisce i dialoghi, disabilita i comandi e rilascia la chiave. La 1.1.3 annulla Auto-Type anche quando la finestra viene ridotta a icona, pure con il blocco alla minimizzazione disattivato. Il blocco e lo sblocco manuali sono passati nella VM. Restano da verificare Win+L durante operazioni e dialoghi e la cancellazione in un flusso Auto-Type desktop. Stringhe .NET e buffer OS non sono azzerabili con garanzie complete. |
| A09 | Mitigato | Generatore, copia ordinaria e copia nei controlli passano dal servizio clipboard condiviso. Policy e scadenza sono unificate; comportamento effettivo di Windows history/cloud ancora da verificare. |
| A10 | Corretto | Creazione in staging con inizializzazione completa e rename senza overwrite; il file esistente viene rifiutato. Bootstrap consentito solo al provider che ha creato il nuovo file. |
| A11 | Mitigato | Backup verificato prima della sostituzione, quattro generazioni e stato distinto dal salvataggio. I test coprono errori simulati e un errore OS di `File.Replace`. La 1.1.3 pulisce i journal SQLite delle copie temporanee. Restano da verificare perdita di alimentazione e guasti su dispositivi reali. |
| A12 | Corretto | Rimosso il test che chiamava SendInput sul desktop. Le regressioni Auto-Type usano solo FakeInput e non digitano nella finestra attiva. Il messaggio involontario osservato durante l'audit era conseguenza del vecchio test. |
| A13 | Corretto | Migrazione a .NET 10, SDK fissato e publish self-contained Windows x64. Il runtime effettivamente distribuito va mantenuto aggiornato nelle release successive. |
| B01 | Corretto | Titoli, gruppi, URL storici, campi, nomi degli allegati e contenuti sono dentro payload cifrati. README e SECURITY elencano i metadata ancora visibili e i limiti delle vecchie copie. |
| B02 | Mitigato | Buffer derivati e temporanei azzerati nei finally; ownership del motore esplicita in creazione/rekey, cleanup su rollback e allegati. Ridotte le copie AES-GCM e di serializzazione; dettagli pesanti caricati su richiesta e bozze rilasciate prima del refresh. Pulizia estesa anche a sequenze Auto-Type letterali e titoli storici. Restano copie gestite, GC e librerie native: nessuna promessa di purge totale della RAM. |
| B03 | Corretto | Salt e parametri Argon2 limitati prima della derivazione: 16–64 byte, 1–20 iterazioni, 8–512 MB, 1–16 lane. Derivazione fuori dal dispatcher; nessuna cancellazione interna della libreria Argon2. |
| B04 | Mitigato | Avviso sul CSV in chiaro e sulle omissioni, cestino escluso e percorsi ricorsivi. Il backup cifrato conserva il vault completo. Il CSV resta deliberatamente un formato parziale; avviso anche sulle formule nei fogli di calcolo. |
| B05 | Corretto | Log senza titoli, nomi delle cartelle o messaggi grezzi delle eccezioni; diagnostica locale opt-in dalle impostazioni, con rotazione. La scrittura del logger resta sincrona. |
| B06 | Mitigato | Windows CI ha verificato la presenza dei formati di esclusione, la scadenza e la conservazione di un valore copiato successivamente. Restano da verificare Win+V e la sincronizzazione in una sessione Windows interattiva. |
| B07 | Mitigato | Impostazioni applicate alle finestre aperte, controllo del risultato API e diagnostica del fallback. La protezione cattura resta best-effort e richiede prove native. |
| B08 | Corretto | Snapshot temporanei unici, lease di backup tra processi, verifica della sessione sotto transazione writer durante la pubblicazione del backup primario, rotazione atomica e filename secondario basato sul vault ID. Regressione con provider concorrenti. Destinazione secondaria non disponibile segnalata. |
| B09 | Corretto | Connessioni WAL con synchronous=FULL, foreign key attive e flush delle copie. Rimosse le promesse di immunità alla corruzione; non è una prova di durabilità di ogni hardware. |
| C01 | Corretto | Elimina cartella sposta le voci discendenti nel cestino, con conferma; radice e cestino sono protetti. L'API di cancellazione definitiva elimina ricorsivamente in transazione. La gerarchia rimossa non viene conservata come oggetto recuperabile. |
| C02 | Corretto | Editor su deep clone: campi, allegati e storia non condividono gli oggetti del modello attivo. Annulla pulisce la bozza. |
| C03 | Corretto | Nuove revisioni con snapshot completo; ripristino di campi, allegati, tag, scadenza e Auto-Type. Retention di 50 revisioni e limite di 64 MB per record, con rifiuto esplicito dei record troppo grandi. La cronologia incompleta prodotta dalle vecchie versioni non è ricostruibile. |
| C04 | Corretto | Duplicazione completa con nuovi ID, revisione iniziale e buffer/collezioni indipendenti. |
| C05 | Corretto | Singolo e batch condividono il writer; rimozione delle collezioni persistita, cronologia inclusa e transazione unica per il batch. |
| C06 | Mitigato | Validazione Bitwarden, cartelle e campi conosciuti; JSON originale della voce conservato in un campo protetto. Carte/passkey e altri tipi non diventano funzionalità native. Allegati assenti dall'export non ricostruibili. |
| C07 | Corretto | Parser CSV a stati con multilinea/quote/BOM, intestazioni riconosciute, numero colonne e limiti. Input malformato o sconosciuto rifiutato, con regressioni dedicate. |
| C08 | Corretto | Schema versionato, header letto senza inizializzare file esistenti, autenticazione prima della migrazione, snapshot legacy e conversione transazionale. Versioni future, dati corrotti o identità legacy ambigue vengono rifiutati. |
| C09 | Corretto | Root/trash ID autenticati; logica basata su ruoli, gerarchia verificata contro cicli/orfani e spostamenti della radice. Solo nella migrazione si riconoscono i vecchi nomi/icone, rifiutando ambiguità. |
| C10 | Corretto | Lease OS per lettura/modifica/scrittura tra istanze, merge delle modifiche indipendenti e rifiuto dei conflitti sullo stesso valore. L'acquisizione del lock usa backoff con jitter e timeout esplicito di 30 secondi, evitando il fallimento prematuro osservato con writer concorrenti sotto carico. JSON atomico con flush e backup leggibile conservato anche dopo recupero. Il fallimento del registro Windows viene distinto dalle preferenze già salvate; i due sistemi non vengono presentati come un'unica transazione. Regressioni su concorrenza, merge, conflitti e recovery. |
| C11 | Corretto | Opzione backup applicata al provider, avvio --minimized in tray bloccata e comando per rimuovere la directory secondaria. Il percorso di avvio automatico richiede una prova di installazione. |
| C12 | Mitigato | Identità catturate prima di refresh/conferme, risultati async invalidati dalla generazione di sessione, bozze per rinomina/spostamenti e aggiornamento UI dopo commit. Date nel fuso/cultura del sistema. LastAccessAt non è ancora un contatore affidabile di ogni utilizzo; restano i collaudi interattivi delle selezioni. |
| C13 | Parziale | Lista paginata a 200 voci e budget stimato di 8 MiB, cursori avanti/indietro legati a query e snapshot, ricerca/ordinamento globali e niente cache delle pagine visitate. Export, Auto-Type e cestino interrogano tutto il vault; Auto-Type carica i campi completi dopo conferma e verifica revisione. Migrazione legacy in due passaggi streaming, con controllo preventivo di tutti i record e rollback. Anteprime limitate e stima qualità saltata oltre 4.096 caratteri. Restano autenticazione e scansione/decifratura O(N), singoli record potenzialmente grandi, campi scalari della pagina residenti, record e backup completi, separazione dei segreti su richiesta e prove su dispositivi lenti. |
| C14 | Mitigato | Un'eccezione dispatcher blocca subito la sessione prima di essere marcata gestita; se non c'è una finestra vault attiva o il lock fallisce, l'applicazione si chiude. Gli errori di task non osservate programmano la stessa risposta lock-or-shutdown e vengono marcati osservati solo dopo aver accodato il controllo sul dispatcher. Gli errori di verifica canary considerano non valida la credenziale solo per formato Base64 o autenticazione crittografica non validi, mentre errori inattesi come motore già disposed propagano. I conflitti di revisione/sessione espongono `VaultConflictException`. Errori imprevisti di drag-and-drop raggiungono il dispatcher che blocca la sessione; il fallback icona tray registra i tipi di errore attesi. Il rework completo verso errori tipizzati e comandi/servizi separati non è concluso; permangono altri catch generici e ownership incompleta delle task. |
| C15 | Corretto | Il formato 2 non usa ENC: come discriminatore dei valori. Il reader legacy interpreta il prefisso solo secondo la policy precedente, senza sostituire fallimenti con placeholder. Regressione su username ENC: legittimo. |
| D01 | Corretto | Shortcut dichiarati gestiti da PreviewKeyDown, inclusi Ctrl+H/F5, con Ctrl+V lasciato ai campi testuali. Registrazione Win32 verificata, MOD_NOREPEAT e feedback sul conflitto; il comando manuale non arma un Auto-Type che non può partire. Rimangono opportuni comandi WPF con CanExecute e prove native della scorciatoia. |
| D02 | Corretto | Trigger del template nasconde i figli quando l'albero collassa. Espansione ricordata per utente/vault nelle preferenze, con scritture raggruppate fuori dal dispatcher e flush alla chiusura, senza riscrivere il vault. Le preferenze conservano al massimo 4.096 stati identificati da ID opachi; nomi e credenziali non vengono copiati. |
| D03 | Corretto | Catalogo in TranslationCatalog con risorse JSON incorporate (it, en, fr, de, es), binding dinamici in XAML e codice e controlli automatici di parità delle chiavi e dei segnaposto. Resta il collaudo visivo con stringhe lunghe, per esempio in tedesco, su Windows nativo. |
| D04 | Corretto | Indicatore rinominato come stima euristica 0–100, con penalità per ripetizioni/pattern. Nessuna etichetta di bit di entropia per password umane. Validazione delle opzioni del generatore, CSPRNG mantenuto. |
| D05 | Mitigato, verifica finale aperta | Layout adattivo e dialoghi ridimensionabili/scorrevoli, dimensioni delle finestre persistite e clamped allʼarea di lavoro. Una precedente esecuzione verificò 1080 rendering in memoria in cinque lingue, due temi e una palette ad alto contrasto simulata; l'ultima esecuzione completa è fallita durante la chiusura sintetica di un editor sporco. La chiusura idempotente è stata corretta ma non ancora riverificata. Le scale raster non costituiscono una prova DPI nativa: restano monitor 100–200%, testo ingrandito e collaudo interattivo. |
| D06 | Mitigato | Misurate 105 coppie testo/sfondo nei due temi e nella palette ad alto contrasto simulata, tutte almeno 4,5:1; corretto il colore hover scuro e mantenuti override high contrast. Non sono state verificate tutte le combinazioni reali dei controlli o le impostazioni high contrast native. |
| D07 | Mitigato | AppIcon vettoriale anche nei converter di albero/elenco, nomi accessibili, focus e live region. Corrette le combinazioni della riga e cartella selezionate anche con palette ad alto contrasto simulata; etichetta visibile Nuova voce. Restano verifiche con tastiera/Narrator e high contrast nativo: il rendering in memoria non le sostituisce. |
| E01 | Corretto | Aggiornati SDK, runner e dipendenze dei test; i due pacchetti legacy vulnerabili non sono più nel grafo. Scansione NuGet finale senza segnalazioni sui cinque progetti, incluse dipendenze transitive. Non copre tutte le vulnerabilità possibili del runtime o dei binari nativi. |
| E02 | Corretto | SDK e tool fissati, restore locked, workflow Windows, Dependabot, test prima del packaging, provenienza e checksum. Verify #140 su `main` ha validato il pacchetto pubblicato della 1.1.3. |
| E03 | Mitigato | Applicazione e installer 1.1.3 senza firma. Installazione, avvio, aggiornamento e disinstallazione verificati in CI. In VM è riuscita la riapertura del vault locale dopo reinstallazione. Il feed pubblico è stato verificato. Rimane possibile un avviso di reputazione di Windows. |
| E04 | Mitigato | Il pacchetto include GPL-3.0, SECURITY, inventario e licenze delle 11 dipendenze applicative. Verify #140 ha convalidato i file presenti nel pacchetto 1.1.3. I limiti di provenienza sono documentati in LICENSE-SOURCES.md. |
| E05 | Corretto | README, About e changelog riscritti con proprietà/limiti reali; release notes 1.0 marcate storiche. Versione About letta dall'assembly. Rimosse garanzie assolute di recupero/privacy/purge. |

Classificazione dei 49 rilievi: **32 corretti, 16 mitigati, 1 parziale**. Un rilievo mitigato di priorità P1 resta da trattare; la pubblicazione non chiude lʼaudit.

## Seconda tranche di interventi

Dopo la prima verifica da 55 test sono stati completati il controllo di concorrenza delle preferenze e la persistenza dell'espansione, oltre alla nuova importazione atomica. Le prove aggiuntive verificano errore tardivo e annullamento dell'importazione con rollback di tutte le cartelle/voci, backup invariato, percorsi ambigui e rifiuto del cestino. La validazione CSV ora controlla tutte e sei le intestazioni KeePass nel loro ordine e rifiuta gli alias browser duplicati, evitando scambi silenziosi dei campi.

Il flusso di importazione è separato in un servizio Core e in una parte UI dedicata; le preferenze della vista sono anch'esse separate dal code-behind principale. Questo riduce la duplicazione ma non equivale a una migrazione completa a MVVM.

## Terza tranche: dettagli su richiesta e memoria

La UI carica ora un elenco senza materializzare allegati, campi personalizzati e cronologia. Modifica e duplicazione richiedono il record completo con verifica della revisione; una vista parziale non può essere salvata o clonata. Gli spostamenti preservano i dati non caricati in transazione. Il cambio chiave, il cestino delle cartelle e la validazione di recovery non trattengono più tutti i record completi contemporaneamente.

Il manifest usa hashing incrementale e streaming BLOB senza cambiare il formato autenticato. Regressioni indipendenti verificano compatibilità del layout AES-GCM e della canonicalizzazione formato 2. Sono stati ridotti i buffer intermedi e le copie dei figli durante la serializzazione; bozze e ripristino revisioni vengono preparati su worker.

Gli allegati vengono letti con limite di 20 MB sullo stream aperto e salvati con sostituzione atomica. Chiusura/lock invalidano gli esiti tardivi, le operazioni file ricevono cancellazione e il salvataggio dell'editor attende che siano terminate. I test verificano rifiuto dei file grandi, annullamento e destinazione originale conservata su errore.

Nel [benchmark documentato](../PERFORMANCE_2026-11-16.md), 96 voci con allegati/revisioni mantengono **44,29 MiB** con lo snapshot completo contro **62,55 KiB** con quello leggero. È la memoria gestita dello snapshot, non il consumo totale del processo. Il punto C13 resta parziale per i limiti esplicitati nel benchmark e nelle priorità residue.

## Quarta tranche: localizzazione e accessibilità

Tutte le stringhe di interfaccia utente, i messaggi operativi e i testi dei dialoghi (inclusi i dialoghi generati a codice come `CustomFieldDialog`, `PasswordConfirmationDialog`, `AutoTypeConfirmDialog` e `OperationProgressDialog`) sono stati migrati in `TranslationCatalog`, alimentato da file JSON incorporati nell'assembly (`it.json`, `en.json`, `fr.json`, `de.json`, `es.json`). L'applicazione supporta il cambio lingua a runtime tramite `{DynamicResource}` in tutti i file XAML e `LocalizationManager.GetString` nel code-behind. Una suite di test dedicata (`LocalizationTests.cs`) valida che ogni chiave esista in modo non vuoto in tutte e cinque le lingue, che i segnaposto compositi di formattazione corrispondano per tipo e numero, e che non vi siano riferimenti mancanti da XAML o codice sorgente.

Sul piano dell'accessibilità, i glifi testuali basati su emoji sono stati sostituiti dal controllo vettoriale `AppIcon`, che sopprime l'automation peer per gli elementi meramente decorativi evitando letture ridondanti o errate da parte degli screen reader. A tutti i controlli interattivi (campi di input, selettori, pulsanti con sole icone, live region di avanzamento) sono stati associati esplicitamente `AutomationProperties.Name` e indicatori visivi di focus per tastiera.

È stata inoltre uniformata e resa rigorosa la terminologia relativa alla sicurezza: la protezione dalla cattura dello schermo è documentata come richiesta best-effort all'OS (`SetWindowDisplayAffinity`), e lo stato del database distingue chiaramente tra la presenza fisica del file `.backup` e l'effettiva verifica di integrità.

## Verifiche effettuate

- Restore della solution con `--locked-mode` riuscito.
- Build Release della solution: **0 errori, 0 avvisi**.
- Suite: **152 test superati, 0 falliti, 0 ignorati**; nessuna chiamata SendInput nativa nei test.
- Copertura storica del Core prima di questa prosecuzione: **91,59% righe, 71,37% branch**; non ricalcolata per la nuova candidata. La UI WPF non era inclusa in quella misura.
- Test su soli vault sintetici: manipolazione dei payload/schema/manifest, cancellazione dei record, revisione obsoleta, rekey e rollback, migrazione con/senza key file, recovery con WAL reale, credenziale errata, backup concorrente/secondario fallito, cestino, clone, CSV, impostazioni, parità delle 5 lingue e Auto-Type finto.
- `dotnet list SQLitePass.slnx package --vulnerable --include-transitive --format json`: nessuna segnalazione nel catalogo NuGet consultato, su tutti e cinque i progetti.
- Packaging locale Windows x64 self-contained con tool Velopack fissato, test obbligatori e modalità `-AllowUnsigned`. Il packaging non installa né pubblica il prodotto.

Risultati dettagliati locali e ignorati da Git: `artifacts/verification/`. La copertura automatica non dimostra completezza della sicurezza e non comprende un percorso manuale delle finestre WPF. Non sono stati aperti, migrati o alterati vault reali dell'utente.

## Pacchetto locale verificato

Build finale locale: `1.1.0-25a2ca09f51e45f8a9a981328b6790ea`. Applicazione `1.1.0.0`, runtime Microsoft.NETCore.App e Microsoft.WindowsDesktop.App **10.0.15**, Windows x64. I binari e i log di verifica erano output locali ignorati da Git e non sono distribuiti insieme a questo report. Per gli asset pubblicati e i digest verificati consultare l'[esito 1.1.0](../releases/RELEASE_OUTCOME_2026-11-17.md); per lo stato attuale consultare lo [stato dell'audit](../AUDIT_STATUS.md).

Verificati sei hash del manifest e i 495 elementi dello ZIP: runtime e documenti di licenza/policy presenti, assembly test/TestHost e relativi runner assenti. **Applicazione e setup risultano NotSigned**, coerentemente con la modalità QA scelta. Installer e updater sono stati collaudati in modalità silenziosa in directory temporanee, poi disinstallati. Lʼapp non è stata avviata per una prova interattiva. Questi sono risultati storici della candidata locale, non artefatti riproducibili conservati nel repository.

## Compatibilità e passi necessari prima della release

Il formato 2 cifra l'intero contenuto dei record e autentica schema e righe. **Richiede SQLitePass 1.1 o successivo.** Una migrazione viene eseguita solo dopo conferma e autenticazione, con snapshot `.pre-v2-...backup`; prima di provarla occorre una copia indipendente a applicazioni chiuse. Vecchi backup e snapshot conservano la vecchia chiave e le proprietà del vecchio formato. Una password già sovrascritta da una versione precedente non viene ricostruita dalla migrazione.

Priorità residue:

1. Verificare che V1 lasci intatti i file dopo gli errori. Completare Win+L durante operazioni e dialoghi, copia da ogni controllo, Win+V e cloud, protezione cattura e prove Auto-Type desktop in una VM con dati sintetici.
2. La firma è esclusa. Il pacchetto installato e il feed della 1.1.3 sono stati verificati. I risultati e i limiti della verifica sono nel consuntivo di release.
3. Completare localizzazione, layout a DPI elevati e tastiera/Narrator/high contrast. La persistenza dell'espansione è implementata; il restyle resta mirato e non verificato visivamente in modo completo.
4. Completare separazione di comandi/servizi, errori di dominio, segreti scalari su richiesta e scritture differenziali; ampliare progressi/cancellazione e benchmark su grandi volumi. Dettagli pesanti su richiesta e importazione atomica sono implementati; valgono i limiti descritti nel report prestazioni.
5. Valutare una revisione indipendente del nuovo formato e del protocollo di migrazione/recovery, con fault injection. La suite locale non sostituisce un audit crittografico o un penetration test esterno.

Il [consuntivo della 1.1.3](../releases/RELEASE_OUTCOME_1.1.3.md) dettaglia la pubblicazione e le prove rinviate.

## Prosecuzione del 17 novembre 2026

Il [rapporto storico della candidata 1.1.0](../releases/RELEASE_READINESS_2026-11-17.md) dettaglia ripristino dal cestino, cancellazioni con controllo revisione, esportazione CSV atomica/cancellabile, patch runtime, test, rendering WPF, misure di contrasto, prove installer/updater e limiti ancora aperti. Per i residui attuali consultare lo [stato dell'audit](../AUDIT_STATUS.md). La firma resta esclusa per decisione dell'utente.


## Quinta tranche: pagine limitate e migrazione streaming

La UI usa il nuovo provider paginato anche per ordinamento e ricerca globale. I cursori verificano query e snapshot autenticato; una modifica concorrente invalida la navigazione precedente. Il budget in byte è una stima dei campi scalari, non un limite del processo: una voce più grande viene mostrata da sola per evitare salti. Le pagine precedenti non vengono conservate e il lock elimina i cursori, che possono contenere un valore di ordinamento sensibile. Il CSV viene scritto direttamente da una lettura autenticata coerente; cestino e Auto-Type non dipendono dalle 200 righe visibili.

Il reader legacy valida tutti i record prima di creare schema e snapshot di migrazione, poi ripete la lettura un record alla volta nella stessa transazione. Conserva la policy precedente di username/URL, gli owner GUID non canonici e le ultime 50 revisioni, validando anche quelle scartate. Rifiuta record oltre i limiti supportati. Il requisito del key file viene ricavato dal fattore realmente usato nella derivazione e preservato nella migrazione. Le regressioni aggiunte comprendono errore nellʼultima voce e fault injection prima del commit, con sorgente invariato.


## Sesta tranche: editor, filtri e feedback

Aggiunti percorso della cartella, filtri paginati per tag esatto e scadenza, scope di ricerca visibile e stato operativo distinto dagli appunti. Lʼeditor mostra errori inline e stato della bozza, conferma lo scarto su chiusura e confronta i dati sul worker prima del salvataggio: una voce esistente invariata non viene riscritta e non crea storia. I cambiamenti alla sola storia restano persistibili. Il reveal si chiude con deadline monotona di 15 secondi, disattivazione e lock; il controllo plaintext nascosto viene svuotato. La chiusura per lock bypassa la conferma di scarto.

Il verificatore WPF ora pompa il dispatcher senza avviare Application.Run o mostrare finestre. Verificati caricamento asincrono reale, filtri combinati, breadcrumb selezionato, controlli dellʼeditor, scadenza simulata del reveal e cronologia aggiornata sul contesto UI. Matrice visiva: 1.080 casi con cinque lingue, tre palette e quattro scale raster. Restano i limiti nativi e architetturali nel rapporto aggiornato.

## Aggiornamento 1.2.0

- **A07.** Le prove native con processi separati verificano la digitazione e l'interruzione al cambio di focus su Windows, X11 e XWayland. Il rischio P1 tra controllo e input Windows resta documentato.
- **A08.** Il test Win+L nella VM Windows ha bloccato il vault. Su Linux sono passati logind, sospensione e ripresa, e il blocco grafico Xfce dopo la correzione. Restano le prove Windows con operazioni o dialoghi aperti.
- **A09 e B06.** Sono verificati lo scambio con un'altra applicazione, la scadenza, la cancellazione quando il vault si blocca e il rispetto di un contenuto copiato successivamente. Le prove di cronologia e sincronizzazione cloud di Windows restano aperte.
- **C14.** Il test desktop Avalonia inietta un errore del dispatcher durante l'uso di un vault sintetico e richiede che il vault si blocchi.
- **E03.** L'installer Windows rimane senza firma. La pipeline verifica gli SHA-256 dei pacchetti e la corrispondenza dei feed pubblici. [Consuntivo della 1.2.0](../releases/RELEASE_OUTCOME_1.2.0.md)
