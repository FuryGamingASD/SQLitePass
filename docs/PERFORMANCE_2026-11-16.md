# Memoria e caricamento del vault

> Le sezioni iniziali conservano misure e limiti storici del 16 novembre. Le ripetizioni successive, datate nel testo e basate su fixture sintetiche, descrivono il codice di paging misurato in quel momento e un metodo di misura affinato; non rappresentano una misura della release pubblicata o del processo completo.

Misura locale del **16 novembre 2026**, linea di sviluppo 1.1.0, formato vault 2. Runtime .NET 10.0.14, architettura x64. Questa prova usa esclusivamente dati sintetici e non avvia la UI.

## Modifica implementata

La lista riceve ora voci leggere senza materializzare allegati, campi personalizzati e cronologia. Password, username, note, URL e le altre proprietà necessarie a ricerca, anteprima e Auto-Type restano disponibili. Quando si modifica o duplica una voce, il provider carica i dettagli di quella voce e verifica la revisione attesa. Una vista parziale è marcata e **non può essere salvata o clonata come se fosse completa**. Gli spostamenti ricaricano e preservano i contenuti nella stessa transazione della modifica.

Il manifest continua ad autenticare header, schema e tutte le righe cifrate. Il suo hash è accumulato senza costruire una stringa proporzionale al numero delle righe. La query include `rowid`, necessario affinché `GetStream` usi lo streaming BLOB anziché materializzare l'intero valore; è il comportamento documentato da [Microsoft per Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/blob-io). La sequenza di byte autenticata è identica al formato 2 precedente, verificata con una regressione separata basata sulla canonicalizzazione originale.

AES-GCM opera su porzioni del buffer esistente, evitando copie intermedie del ciphertext. La serializzazione per la scrittura usa un header separato e prende in prestito i dati figli durante l'operazione; non azzera né modifica i buffer del chiamante. Gli snapshot escludono la storia prima della serializzazione. Rekey, cestino delle cartelle e validazione completa per recovery elaborano un record alla volta; il reader legacy per la migrazione iniziale conserva ancora il caricamento completo.

## Protocollo della prova

Comando riproducibile:

```powershell
dotnet run --project SQLitePass.TestHost -c Release -- --benchmark
```

Il comando non accetta percorsi o credenziali. Crea una directory temporanea con nome univoco, un vault sintetico con **96 voci**, un allegato casuale di **128 KiB** e **una revisione completa per voce**, quindi elimina i dati creati. I backup sono disabilitati. I parametri KDF della fixture sono ridotti e non fanno parte dei tempi di caricamento.

Prima delle misure vengono eseguiti un caricamento completo e uno leggero per riscaldare i percorsi di serializzazione e crittografia. Ogni misura rileva tempo, byte gestiti allocati cumulativamente e memoria gestita ancora raggiungibile mantenendo vivo lo snapshot, dopo una raccolta GC completa. Le collezioni vengono poi svuotate. Non sono misurati working set totale, memoria nativa, avvio WPF, KDF, rendering, latenza di rete o costo del backup.

## Risultato locale

Database e sidecar: **35.241.984 byte**, circa 33,61 MiB.

| Misura | Snapshot completo | Snapshot leggero |
| --- | ---: | ---: |
| Voci | 96 | 96 |
| Buffer allegati mantenuti | 96 | 0 |
| Revisioni mantenute | 96 | 0 |
| Memoria gestita mantenuta | 46.441.696 B · 44,29 MiB | 64.048 B · 62,55 KiB |
| Byte gestiti allocati durante il caricamento | 117.001.552 B · 111,58 MiB | 70.575.528 B · 67,31 MiB |
| Tempo osservato | 144,83 ms | 118,93 ms |

In questa fixture la memoria gestita mantenuta dallo snapshot scende del **99,86%** e le allocazioni cumulative del **39,68%**. Il [JSON grezzo della misura](evidence/vault-memory-benchmark.json) è incluso nel repository.

Queste percentuali **non descrivono il consumo totale dell'applicazione**. I tempi derivano da una misura locale dopo riscaldamento e non sono una promessa di latenza, una media statistica o una prova del caso peggiore.

## Limiti ancora presenti

- Ogni caricamento autentica tutto il ciphertext e decifra temporaneamente ciascun record completo. La riduzione riguarda principalmente i dati trattenuti e le copie evitate; il costo I/O resta proporzionale al vault.
- La lista conserva ancora tutti i campi scalari delle voci, inclusi password e note. Non è una paginazione completa o un caricamento su richiesta di ogni segreto.
- Il limite di 64 MB vale per l'intero record cifrato, comprendendo i dati codificati e la cronologia. Durante l'editing di un record grande restano necessarie copie indipendenti della bozza e dello snapshot precedente.
- La scrittura ricifra il record completo e il backup automatico resta una copia completa. Non sono state introdotte scritture differenziali, un nuovo formato su disco o un cache di integrità che salti controlli.
- La migrazione legacy può ancora richiedere memoria proporzionale a tutti i contenuti. KDF, caricamento, import e I/O file non hanno tutti lo stesso livello di cancellazione o progresso.
- Servono benchmark ripetuti su grandi volumi, record vicini al limite, dispositivi lenti e prova interattiva di reattività/lock su Windows.

Le regressioni verificano anche che i dettagli non caricati vengano preservati negli spostamenti, che una lettura singola continui ad autenticare gli altri record, che la validazione completa rilevi dettagli malformati ignorati dalla vista leggera e che l'annullamento non trasformi il vault in sola lettura.

## Ripetizione su Windows VM, 17 novembre 2026

Runtime .NET 10.0.15, x64. I dati grezzi sono la [fixture da 96 voci](evidence/resume-memory-benchmark.json) e la [fixture da 10.000 voci](evidence/resume-volume-benchmark.json). La seconda modalità si riproduce con:

    dotnet run --project SQLitePass.TestHost -c Release --no-build --no-restore -- --benchmark-many

| Fixture | Snapshot completo mantenuto | Snapshot leggero mantenuto | Tempo completo | Tempo leggero |
| --- | ---: | ---: | ---: | ---: |
| 96 voci, allegato 128 KiB e una revisione | 46.439.824 B | 64.816 B | 226,80 ms | 216,57 ms |
| 10.000 voci senza allegati/storia | 8.951.776 B | 6.783.552 B | 412,64 ms | 300,26 ms |

Sono misure della memoria gestita dello snapshot e singole osservazioni locali dopo riscaldamento. Non includono working set/native memory/UI/backup. La VM e il runtime sono diversi dall'ambiente della misura precedente: non usare il confronto dei tempi per inferire una regressione o un miglioramento del codice. Una prova da 10.000 voci piccole non valida 10.000 record vicini al limite dimensionale. Restano lettura/autenticazione O(N), campi scalari residenti, record e backup completi, migrazione legacy eager e i limiti descritti sopra.


## Paginazione e migrazione streaming, 17 novembre 2026

Questa prova successiva usa una pagina massima di 200 voci e un budget stimato di 8 MiB. Una voce sovradimensionata resta visitabile da sola. Non vengono conservate pagine già visitate. La UI interroga il provider per ricerca e ordinamento globali; export, Auto-Type e cestino non dipendono dalla pagina. Il reader legacy ora valida e converte un record alla volta in due passaggi.

Il metodo del heap è stato affinato: misura mentre il risultato è mantenuto e dopo pulizia/rilascio, con GC completo a entrambi i punti. Le differenze rispetto ai numeri storici non sono una misura diretta di regressione/miglioramento del codice.

| Fixture | Snapshot completo mantenuto | Elenco leggero mantenuto | Pagina mantenuta | Voci nella pagina | Tempo pagina |
| --- | ---: | ---: | ---: | ---: | ---: |
| 96 voci con allegati/storia | 46.442.328 B | 64.688 B | 72.912 B | 96 | 172,72 ms |
| 10.000 voci piccole | 8.943.456 B | 6.791.736 B | 135.920 B | 200 | 301,07 ms |

[JSON 96 voci](evidence/paging-memory-benchmark.json), [JSON 10.000 voci](evidence/paging-volume-benchmark.json). Le allocazioni cumulative restano proporzionali al vault: tutta la cifratura viene autenticata e i record decifrati transitoriamente a ogni query. Non sono working set/native memory/UI/backup, medie statistiche o worst case. Restano scalari della pagina residenti, record e backup completi, editing di record grandi e prove su dispositivi lenti.


## Ripetizione dopo editor e filtri, 17 novembre 2026

Nuova misura con il metodo di rilascio descritto sopra. [96 voci](evidence/editing-memory-benchmark.json), [10.000 voci](evidence/editing-volume-benchmark.json). La pagina da 200 voci piccole trattiene 135.920 B contro 6.799.960 B dellʼelenco leggero completo; tempo osservato 332,88 ms. La fixture con allegati/storia trattiene 64.664 B nella pagina da 96 voci. Sono singole osservazioni del heap gestito, non working set o benchmark nativi della UI.

Il confronto finale della bozza e la validazione Auto-Type sono ora su worker; lʼaggiornamento della cronologia rimane sul contesto del chiamante perché la collezione può essere collegata alla UI. Il breadcrumb usa solo gli antenati selezionati e limita segmenti/caratteri. Autenticazione/scansione O(N), record e backup completi e gli altri limiti restano invariati.
