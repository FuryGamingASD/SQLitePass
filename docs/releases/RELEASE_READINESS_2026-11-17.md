# Verifica della candidata SQLitePass 1.1.0

> Questo documento descrive la candidata locale 1.1.0 prima della pubblicazione ed è storico. La release è pubblicata: vedere lʼ[esito definitivo](RELEASE_OUTCOME_2026-11-17.md). Per la versione corrente e i residui consultare lo [stato dell'audit](../AUDIT_STATUS.md).

Verifica del 17 novembre 2026 sugli interventi dell’audit per la 1.1.0. L’unica esclusione dal perimetro era la firma Authenticode. A questa data risultavano corrette le regressioni di compilazione e Auto-Type. [Audit del 16 novembre](../audit/AUDIT_2026-11-16.md) · [Stato precedente delle 02:19](../archive/STATUS_2026-11-17.md)

**Lʼintero audit non è ancora chiuso.** I risultati qui sotto descrivono una candidata locale pre-pubblicazione e restano utili come evidenze tecniche. La release pubblicata è tracciata nel report di esito. Rimangono i collaudi Windows nativi, parte del rework e le raccomandazioni elencate sotto. Nessun vault reale è stato aperto o modificato.

## Modifiche completate

- Editor con errori inline per titolo, scadenza e Auto-Type, stato di modifica e conferma di scarto alla chiusura. Il pulsante Annulla scarta direttamente. Lʼindicatore segnala che la bozza è stata modificata; il confronto finale sul worker riconosce anche modifiche poi annullate e non riscrive una voce esistente rimasta identica. La sola cancellazione della cronologia resta salvabile. La preparazione aggiorna le collezioni sul contesto UI dopo il confronto asincrono.
- Reveal di password editor/anteprima/elenco e valori dei campi personalizzati con scadenza monotona di 15 secondi, chiusura anticipata alla disattivazione e al lock. Il controllo plaintext nascosto dellʼeditor è svuotato, con undo disabilitato. Il timer dipende dal dispatcher: una UI bloccata può ritardare lʼaggiornamento visibile.
- Percorso della cartella selezionata calcolato dai soli antenati, con limiti di segmenti e caratteri. Filtri per tag esatto (separatore virgola/punto e virgola), voci scadute e prossimi 30 giorni, applicati insieme a ricerca/ordinamento/paginazione. La data odierna è inclusa nella finestra futura; il riferimento di calendario resta fisso nella query e si rinnova con nuova query/F5.
- Scope di ricerca esplicito, pulsante Tutte le voci attive, esiti operativi separati dal countdown degli appunti. Gli allegati mostrano la lunghezza effettiva dei byte caricati; nomi e valori non protetti nelle liste hanno anteprime limitate.

- Paginazione effettiva della UI: al massimo 200 voci e budget stimato di 8 MiB dei campi scalari. Una singola voce più grande viene ammessa per evitare salti. Cursori avanti/indietro vincolati a query e snapshot autenticato, invalidati da modifiche concorrenti. Le pagine già visitate non vengono conservate; il lock elimina anche i cursori, che possono contenere dati di ordinamento sensibili.
- Ricerca globale e ordinamento su tutte le voci attive, con scope separato per il cestino. Conteggi, intervallo visibile e stato vuoto con azione. Dopo modifica/eliminazione viene ricaricata una pagina coerente con la selezione.
- Export CSV direttamente dalla lettura autenticata del provider, riga per riga su worker, con progressi, annullamento e sostituzione atomica. Opera su tutte le voci attive, indipendentemente dalla pagina visibile. Rifiuta il vault e le sue copie note come destinazione e rifiuta anche un database SQLite riconosciuto dal contenuto prima della sostituzione. Non equivale a una difesa da un processo ostile che modifica contemporaneamente i percorsi. Il CSV resta in chiaro e parziale.
- Svuota cestino interroga tutte le revisioni del gruppo; cancellazione singola/batch vincolata alle revisioni confermate. Ripristino esplicito con scelta della cartella.
- Auto-Type cerca anche fuori dalla pagina visibile, rifiuta risultati ambigui e carica i dettagli della revisione confermata prima del parser/invio. La regressione con sequenze come {S:token} è coperta senza input desktop.
- Migrazione legacy in due letture streaming nella stessa transazione: tutti i record vengono validati prima della creazione dello schema/snapshot, poi convertiti un record alla volta. Preservati owner GUID non canonici, policy legacy di username/URL, dati figli e requisito del key file realmente usato. Conservate le ultime 50 revisioni, validando anche quelle scartate; record fuori limite rifiutati. Prove di errore sullʼultima voce e rollback prima del commit con sorgente invariato.
- Icone vettoriali anche in albero/elenco; anteprime testuali limitate e valutazione qualità saltata oltre 4.096 caratteri. Copia, export e modifica mantengono i dati completi. Etichetta visibile Nuova voce e colori di selezione corretti per righe/cartelle.
- Override coerenti dei token con high contrast; verifica in memoria anche con palette simulata nero/bianco/giallo. Il renderer controlla lo sfondo effettivo della riga selezionata. Non sono state cambiate impostazioni di accessibilità di Windows.
- SDK 10.0.404 e runtime distribuiti 10.0.15. Manifest delle dieci dipendenze applicative e testi di licenza inclusi. Script di verifica compatibili anche con percorsi UNC.

## Esiti verificati

| Controllo | Esito |
| --- | --- |
| Restore con lock NuGet e build Release | Riusciti; 0 errori, 0 avvisi |
| Suite automatica | 152 superati, 0 falliti, 0 ignorati |
| WPF in memoria | 1080 casi: 18 finestre/modalità, 5 lingue, 3 palette, 4 scale raster |
| Contrasto dei token testo/sfondo | 105 coppie, minimo 4.5669:1 |
| Blocco sessione/editor | Caricamento provider e filtri reali nel dispatcher; editor modificato/reveal ripuliti, chiave disposta e dialogo chiuso |
| Editor e reveal | Errori inline, stato modifica, cronologia sul dispatcher e scadenza monotona verificati |
| Scansione NuGet con transitive | 0 segnalazioni note; 5 progetti |
| Portable | 495 elementi; test/TestHost/Verification assenti |
| Hash degli artefatti | 6 SHA-256 verificati |
| Dipendenze applicative con licenza/copyright | 10; inventario confrontato con deps.json distribuito |
| Installazione silenziosa pulita | Superata in directory temporanea |
| Aggiornamento locale 1.0.2 → 1.1.0 | Superato con Update.exe, senza riavvio dellʼapp |
| Disinstallazione di entrambe le copie | Superata; registrazione e collegamenti rimossi |
| Preferenze utente durante le prove installer | Hash invariato; app mai avviata, nessun vault aperto |
| Firma applicazione/setup | `NotSigned` per entrambi, firma fuori dal perimetro di release |

Le finestre del verificatore non vengono mostrate, attivate o comandate tramite input desktop. Il verificatore esegue ora un dispatcher STA senza Application.Run: carica davvero dal provider in modo asincrono e controlla filtri combinati, percorso selezionato, editor modificato con reveal e pulizia al blocco. Non mostra finestre, non avvia il login del prodotto e non verifica Win+L o un flusso desktop completo. Un clock finto prova il limite del reveal senza attendere 15 secondi; le verifiche native restano distinte. Le scale raster non equivalgono al cambio DPI nativo. Le 105 coppie non certificano ogni stato e ogni controllo dellʼinterfaccia. In seguito, il workflow GitHub sul commit/tag definitivo è terminato con successo; i dettagli sono nel report di esito.

## Memoria e tempi osservati

| Fixture | Snapshot completo | Snapshot leggero completo | Pagina trattenuta | Tempo pagina |
| --- | ---: | ---: | ---: | ---: |
| 96 voci con allegato 128 KiB e revisione | 46.442.424 B | 68.880 B | 64.664 B, 96 voci | 216,68 ms |
| 10.000 voci piccole senza allegati/storia | 8.951.680 B | 6.799.960 B | 135.920 B, 200 voci | 332,88 ms |

Metodo affinato: differenza del heap gestito mantenendo il risultato e dopo averlo pulito/rilasciato, con GC completo a entrambi i punti. Evita di attribuire al risultato oggetti liberati da una misura precedente. Resta una stima locale, non working set totale, memoria nativa, media statistica o prova del caso peggiore. Il benchmark di volume non contiene record grandi. **Ogni query continua ad autenticare tutto il ciphertext e a scansionare/decifrare transitoriamente i record: costo O(N)**. Scritture e backup restano completi. [Protocollo e misure storiche](../PERFORMANCE_2026-11-16.md).

## Artefatti della candidata

Build **1.1.0-25a2ca09f51e45f8a9a981328b6790ea**, Windows x64 self-contained, applicazione 1.1.0.0, formato vault 2.

Gli output locali di build, installazione, TRX, rendering e verifica erano candidati pre-pubblicazione ignorati da Git; non sono parte di questo repository. Gli asset pubblicati, i digest e la provenienza sono riportati nellʼ[esito 1.1.0](RELEASE_OUTCOME_2026-11-17.md). I benchmark sintetici conservati sono in [docs/evidence](../evidence/README.md). I binari restano non firmati.

## Requisiti ancora aperti

1. Flussi Windows interattivi: creazione/sblocco/migrazione/rekey/key file, Win+L con operazioni e dialoghi aperti, copie da tutti i controlli e Win+V/cloud, Auto-Type con focus/modificatori/app elevate, protezione cattura. I test con adattatori finti e oggetti WPF non li sostituiscono.
2. Tastiera/Narrator, high contrast e testo ingrandito nativi, monitor al 100–200% DPI e display piccoli. Le immagini generate coprono solo layout e palette del verificatore.
3. Parte del rework: autorità unica della sessione, separazione completa di modelli/servizi/comandi e adattatori Windows, errori di dominio tipizzati; segreti scalari della pagina su richiesta, scritture e backup differenziali. Il formato 2 non è stato sostituito con un formato non verificato per eliminare il controllo O(N).
4. Restyle e comandi: percorso, filtri, validazione inline, stato della bozza, reveal temporizzato, dimensioni allegati e separazione dei feedback sono implementati. Restano lʼuniformazione completa dei componenti, lʼorganizzazione sistematica dei comandi/overflow e i collaudi assistivi nativi. Passphrase/TOTP/controllo riuso erano miglioramenti successivi e restano non implementati.
5. La CI sulla revisione definitiva, la tracciabilità commit/tag e il feed pubblico sono verificati. Rimangono lʼavvio interattivo portable/installer e il percorso manuale completo dei flussi applicativi.
6. Guasti fisici a dischi/USB/alimentazione e revisione indipendente del formato/migrazione/recovery: non attestati. Fault injection software e WAL reale non equivalgono a queste prove.

Non sono stati eseguiti test interattivi nativi dell'applicazione. Le verifiche in memoria e gli adattatori fake non sostituiscono i flussi Windows; la [readiness 1.1.3](RELEASE_READINESS_1.1.3.md) contiene la checklist manuale aggiornata.

## Riproduzione

Prerequisito: SDK .NET 10.0.404 come indicato in global.json.

    dotnet restore SQLitePass.slnx --locked-mode
    dotnet build SQLitePass.slnx -c Release --no-restore
    dotnet test SQLitePass.Tests -c Release --no-build --no-restore --logger "trx;LogFileName=editing-tests.trx" --results-directory artifacts/verification
    dotnet run --project SQLitePass.UI.Verification -c Release --no-build --no-restore -- artifacts/verification/ui-editing
    dotnet run --project SQLitePass.TestHost -c Release --no-build --no-restore -- --benchmark
    dotnet run --project SQLitePass.TestHost -c Release --no-build --no-restore -- --benchmark-many
    dotnet list SQLitePass.slnx package --vulnerable --include-transitive --format json --no-restore
    .\build-installer.ps1 -Version 1.1.0 -AllowUnsigned
    .\scripts\Verify-ReleaseArtifacts.ps1 -BuildId 1.1.0-25a2ca09f51e45f8a9a981328b6790ea

Lo script Verify-ReleaseArtifacts richiede i JSON/TRX nelle posizioni documentate. La prova Verify-WindowsPackage è mutante e destinata a una macchina di verifica: rifiuta installazioni/processi/collegamenti esistenti, installa solo proprie copie temporanee e le disinstalla. La verifica ha usato un SDK temporaneo con SHA-512 verificato contro il valore ufficiale. L’SDK e il PATH globali sono rimasti invariati.
