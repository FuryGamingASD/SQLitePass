# SQLitePass 1.1.0

Windows x64, self-contained .NET 10.0.15, formato vault 2. La firma Authenticode è esclusa su indicazione dellʼutente. La pubblicazione è autorizzata dopo i controlli automatici; i collaudi Windows nativi e la revisione indipendente ancora aperti sono descritti nel [rapporto di verifica](https://github.com/FuryGamingASD/SQLitePass/blob/v1.1.0/docs/RELEASE_READINESS_2026-11-17.md).

Scaricare `SQLitePass-win-Setup.exe` oppure estrarre `SQLitePass-win-Portable.zip`. `checksums.json` contiene i digest SHA-256; `release-provenance.json` identifica commit, tree, SDK e run CI degli asset.

## Modifiche principali

- Percorso cartella, filtri per tag/scadenza e feedback operativi separati dagli appunti.
- Validazione inline, stato modifica, conferma di scarto e confronto finale su worker; le voci invariate non vengono riscritte.
- Reveal temporaneo di 15 secondi, chiusura alla disattivazione/lock e controllo plaintext nascosto svuotato; dimensioni reali degli allegati.

- Payload di gruppi e voci cifrati e autenticati insieme a identità, gerarchia e revisioni; manifest dellʼintero vault.
- Migrazione autenticata con copia preventiva, recovery con conservazione del file originale e dei journal, backup con versioni precedenti e stato verificato.
- Protezione dai writer obsoleti dopo rekey, cancellazioni con controllo revisione e ripristino dal cestino verso una cartella scelta.
- Bozze indipendenti, cronologia completa, pagine a 200 voci/budget stimato di 8 MiB, dettagli su richiesta e pulizia al blocco. Ricerca/ordinamento, export, cestino e Auto-Type operano anche fuori dalla pagina. Auto-Type carica i campi personalizzati della revisione confermata.
- Migrazione legacy streaming con validazione preventiva, rollback e requisito key file preservato.
- Importazioni atomiche con progressi/annullamento; esportazione CSV in background con sostituzione atomica.
- Clipboard coordinata, conferma e controlli del destinatario Auto-Type, preferenze concorrenti protette.
- Cinque cataloghi linguistici, icone vettoriali, nomi accessibili, focus e layout adattivi; miglioramenti del contrasto.
- SDK e dipendenze fissati, verifiche CI predisposte, licenze e manifest SHA-256 inclusi negli output.

## Prima di aprire un vault 1.0

Con tutte le applicazioni che usano il vault chiuse, conservare una copia indipendente del database e dei relativi file. La migrazione richiede conferma e credenziale originale e conserva uno snapshot precedente. Il formato 2 richiede SQLitePass 1.1 o successivo: non riaprire il file migrato con una versione 1.0. Vecchie copie conservano la chiave precedente e le proprietà di privacy del vecchio formato. Credenziali già perse non possono essere ricostruite.

CSV e allegati esportati sono file in chiaro; il CSV omette allegati, campi personalizzati, storia e cestino. Per una copia completa usare un backup cifrato verificato e conservarne una copia indipendente su altro dispositivo.

## Verifiche e limiti

152 test superati, build senza errori/avvisi, 1080 rendering in memoria (anche lista popolata e palette ad alto contrasto simulata), 105 coppie di contrasto e prove silenziose di installazione, aggiornamento locale 1.0.2→1.1.0 e disinstallazione. Nessun vault reale è stato aperto.

Restano collaudo interattivo Windows, DPI/Narrator/high contrast, Win+V/cloud e Auto-Type nativi, percorso del feed GitHub e il rework descritto nel rapporto. Lʼesclusione dalle catture è best-effort; non è una difesa da un sistema operativo compromesso. Backup e test non garantiscono immunità da guasti o perdita di dati. Leggere [SECURITY.md](https://github.com/FuryGamingASD/SQLitePass/blob/v1.1.0/SECURITY.md).
