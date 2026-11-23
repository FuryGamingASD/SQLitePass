# Parità funzionale di SQLitePass dalla 1.0.0 alla 1.2.0

La **1.2.0 pubblicata conserva il motore del vault e i principali flussi di gestione dei dati, ma perde diverse funzioni desktop della 1.1.3**. La migrazione ad Avalonia ha sostituito la superficie desktop WPF. La presenza del sorgente WPF nel repository non rende i suoi controlli disponibili nei pacchetti 1.2.0.

Il confronto usa le note di rilascio, il changelog e il codice dei tag. La colonna sul ramo di ripristino descrive il lavoro successivo alla 1.2.0 pubblicata. I risultati in CI e quelli interattivi sono indicati con il loro perimetro. [Note di rilascio](../../RELEASE_NOTES.md) · [Changelog](../../CHANGELOG.md) · [Esito 1.2.0](../releases/RELEASE_OUTCOME_1.2.0.md)

| Versione | Commit | Funzioni introdotte o consolidate |
| --- | --- | --- |
| 1.0.0 | `1ba39ed` | Vault cifrato SQLite, backup, storico, allegati, campi personalizzati, clipboard, generatore, Auto-Type globale, cinque lingue, installer e portatile. |
| 1.0.1 | `acb6599` | Card di sblocco, palette chiare e scure, toolbar, sidebar, ricerca immediata, anteprima, comandi contestuali e blocco in background. |
| 1.0.2 | `d4695b3` | Cestino, import Bitwarden/browser, verifiche di integrità, protezione cattura Windows, log, ordinamento per colonna e drag-and-drop. |
| 1.1.0 | `8e3b683` | Vault Format 2, migrazione e recupero autenticati, revisioni, filtri e paginazione, editor a bozze, reveal temporizzati, espansione dei gruppi, accessibilità e verifiche di release. |
| 1.1.1 | `cbe664d` | Attivazione della copia già in esecuzione su Windows, aggiornamento e disinstallazione con chiusura dell'app. |
| 1.1.2 | `f8c3783` | Aggiornamento dipendenze, conflitti tipizzati, retry delle preferenze e controlli Auto-Type prima e dopo l'input. |
| 1.1.3 | `72c43c5` | Cancellazione Auto-Type alla minimizzazione, blocco per errori inattesi, chiusura sicura dell'editor, pulizia dei journal e startup Windows. |
| 1.2.0 | `87b6e8e` | Applicazione Avalonia condivisa, pacchetti Windows/Linux e due feed. Baseline effettivamente distribuita oggetto dell'audit. |

## Vault, dati e distribuzione

| Area | 1.2.0 pubblicata | Evidenza o differenza |
| --- | --- | --- |
| Crittografia e autenticazione | Presente | AES-256-GCM, Argon2id, password, file chiave e file chiave senza password. Il Core condiviso conserva Format 2, revisioni, epoche della chiave, contesto autenticato e manifest. |
| Compatibilità 1.0.x e cambio credenziale | Presente | Migrazione legacy con conferma e snapshot, verifica credenziali, cambio password e rotazione del file chiave nel Core. La pipeline verifica anche l'aggiornamento del pacchetto da 1.1.3. |
| Backup, journal e ripristino | Presente con flusso diverso | Backup verificati, più generazioni e destinazione secondaria. Il ripristino Avalonia scrive a un percorso nuovo. I flussi WPF di sostituzione e conservazione dei journal restano verificati nel Core e descritti come comportamento storico. |
| Voci, gruppi, cestino | Presente | Creazione, modifica, duplicazione, spostamento, cancellazione, vuotamento e ripristino verso un gruppo scelto. Test desktop e test del Core verificano revisioni, operazioni concorrenti e contenuti dei sottogruppi. |
| Campi personalizzati, allegati, storico e scadenza | Presente | Schede di modifica e operazioni sugli allegati, campi protetti, ripristino e cancellazione dello storico, scadenze. I test controllano recupero di revisioni con allegati e campi. La visualizzazione della dimensione era assente nella lista e torna nel ramo di ripristino. |
| Importazione ed esportazione | Presente | Import KeePass CSV, Bitwarden JSON e CSV browser. Conferma, progresso e annullamento. Esportazione CSV delle voci attive con conferma per l'output in chiaro. La suite verifica atomicità e preservazione della destinazione su errori. |
| Ricerca, filtri e memoria | Presente con interfaccia ridotta | Ricerca globale, ambito cestino, tag esatto, scadenza, ordinamento dal selettore e cursori. Rimangono il limite di pagina, la validazione dei record e il caricamento separato dei dati pesanti. |
| Clipboard e segreti | Presente con differenze per piattaforma | Scadenza, ownership, pulizia al lock e protezioni richieste al sistema. I probe verificano un secondo processo, scadenza e preservazione del contenuto sostituito. Cronologia e cloud Windows dipendono dalla configurazione. |
| Generatore e localizzazione | Presente | Generatore con classi di caratteri e indicatore di qualità. Le cinque lingue sono incluse e i test verificano la parità delle chiavi. |
| Sessione ed errori | Presente con una perdita desktop | Lock, inattività, minimizzazione configurabile, Win+L, logind/sospensione e Xfce. Il tag 1.2.0 testa il blocco dopo un errore del dispatcher. Mancava la conferma per la bozza dell'editor. Il ramo ne verifica la chiusura durante un blocco. |
| Cattura dello schermo | Presente su Windows | La verifica nativa controlla l'affinità della finestra. Il test non equivale a un collaudo di tutti i percorsi di cattura. La protezione Windows non ha un equivalente implementato per Linux. |
| Preferenze, confezionamento e ciclo di vita | Presente con differenze per piattaforma | Preferenze con conflitti e retry, verifica aggiornamento/installer/disinstallazione Windows, AppImage e aggiornamento in-place Linux, feed separati. Istanza singola e registrazione all'avvio sono funzioni Windows. |

[Verifica del tag 1.2.0](https://github.com/FuryGamingASD/SQLitePass/actions/runs/45067716207) · [Provenienza dei pacchetti pubblici](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.0/release-provenance.json)

## Regressioni dell'interfaccia e stato del ripristino

| Funzione della 1.1.3 | Cosa fa la 1.2.0 pubblicata | Ramo di ripristino Avalonia |
| --- | --- | --- |
| Sblocco a card, logo e palette curate | Mostra un form Fluent generico. | Card centrata, asset del progetto, campi allineati, palette Zinc e Slate riprese dalla 1.1.3. |
| Elenco dei vault recenti e indicazione del backup | Mostra un percorso testuale. Non aggiorna la lista dei recenti. | Selettore dei recenti aggiornato dopo lo sblocco e indicazione di presenza del backup. Il recupero ne valida autenticità e integrità. |
| File chiave opzionale e reveal della password di sblocco | I campi sono sempre presenti. Manca il reveal. | Checkbox, campo a richiesta e reveal di 15 secondi, annullato su deattivazione, autenticazione e blocco. |
| Impostazioni e cambio tema | Impostazioni nel menu File e nella schermata di sblocco. Tema selezionabile dalle impostazioni. Manca l'azione diretta. | Impostazioni visibili in entrambe le schermate e cambio chiaro/scuro immediato con salvataggio della preferenza. |
| Menu, barra con icone e comandi contestuali | Menu ridotti, azioni principali distribuite in più righe. | Barra riordinata con icone originali, Azioni file/modifica/visualizza/strumenti e menu contestuali per voci e gruppi. |
| Sidebar dei gruppi, espansione, percorso | Combobox con percorsi, senza albero. | Albero espandibile con icone, cestino, percorso e preferenze per vault. |
| Drag-and-drop e riordino | Spostamento tramite dialoghi e riordino da menu. Manca il trascinamento. | Trascinamento in-process di voci e gruppi, ordinamento relativo e spostamento alla radice. Restano da provare le gesture native del drag-and-drop su Windows e Linux. |
| Intestazioni cliccabili, lista più ricca | Elenco a tre colonne, sorting da selettore. | Titolo, utente, password, URL, note e ultima modifica con intestazioni ordinabili. Note e data si nascondono quando lo spazio è ridotto. |
| Ricerca mentre si digita | Invio o comando di ricerca. | Ricerca ritardata durante la digitazione, con i filtri e le pagine esistenti. |
| Doppio clic e anteprima | Mancano le azioni sul doppio clic, la colonna password e il pannello dedicato. | Doppio clic apre la scheda su titolo, copia su utente/password e apre l'URL. Tornano anteprima, copia rapida, qualità e reveal temporizzati. |
| Modifiche non salvate | Salvataggio su bozza, validazione e confronto finale. Chiusura senza indicatore o conferma. | Indicatore e conferma sul comando di chiusura. Il lock può chiudere la scheda con la conferma già aperta. |
| Scorciatoie desktop | Auto-Type dalla voce selezionata. Mancano vari comandi a tastiera. | Comandi locali tra cui Ctrl+H, F5, Ctrl+I, Ctrl+G e Ctrl+L. Su Windows torna la registrazione Ctrl+Alt+A con verifica del target e conferma. La prova nativa della scorciatoia globale resta aperta. |
| Accessibilità, geometria e rifiniture WPF | Etichette Avalonia e controllo da tastiera di base. La matrice di layout e contrasto WPF non misura la UI Avalonia. | Etichette e tooltips, layout compatto e chiaro/scuro, palette ad alto contrasto su richiesta del sistema e dimensioni delle finestre salvate. Restano le prove native della matrice DPI, Narrator e contrasto. |

Il confronto evidenzia le perdite di interfaccia e i cambiamenti volontari di piattaforma. Le verifiche della release 1.2.0 coprivano compatibilità del vault, packaging e alcune integrazioni native. Il piano di verifica del ramo aggiunge navigazione dell'albero, doppi clic su voci sintetiche, tema e chiusura dell'editor durante il lock.
