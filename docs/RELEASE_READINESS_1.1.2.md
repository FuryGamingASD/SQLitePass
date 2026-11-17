# Release readiness SQLitePass 1.1.2

Verifica aggiornata al 17 novembre 2026 sul commit `72edb5f80269fa6246c653183b4a3e5884b9c66e` di `main`.

## Esito

La candidata 1.1.2 è preparata sul ramo remoto `codex/release-readiness-1.1.2`; versionamento, changelog e note sono aggiornati. Il workflow [Verify 44184108761](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44184108761) sul commit candidato `3ea588b` è riuscito. Non esiste ancora un tag, un pacchetto pubblico o una release 1.1.2.

L’utente conferma che la 1.1.1 si apre e funziona correttamente. Ritiene probabile che anche la 1.1.0 funzionasse: il problema osservato era la finestra non visibile durante una sessione remota senza essere davanti al PC. La diagnosi precedente di un difetto applicativo non è quindi confermata. Restano da svolgere o classificare le altre verifiche native e i residui tecnici dell’audit.

## Stato verificato dopo i merge

- Le PR Dependabot #1–#5 sono tutte integrate; non risultano PR aperte.
- Il commit finale è `72edb5f` e il workflow [Verify 44178968940](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44178968940) è riuscito il 17 novembre.
- Il job Windows ha completato build senza warning/errori, due esecuzioni della suite da 152 test, audit NuGet con `KnownNugetVulnerabilities: 0`, packaging, verifica artefatti e smoke test installer. Il job `release` è stato saltato perché il commit non era una pubblicazione.
- Gli aggiornamenti Actions sono ora integrati e fissati a SHA: checkout 7.0.1, setup-dotnet 6.1.0, upload-artifact 7.0.2, download-artifact 8.0.2. La vecchia nota di manutenzione dei pin Node 20→24 nel report di remediation è superata da questi merge.
- Microsoft.Data.Sqlite è 11.0.0; i lock file dei cinque progetti e l’inventario/licenze sono stati aggiornati, compresa la nuova dipendenza SQLite.
- La release più recente pubblicata resta [1.1.1](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.1.1). Il commit base dichiara versione 1.1.1; la candidata in questo ramo dichiara 1.1.2 e non è stata pubblicata.
- La CI della candidata ha prodotto assembly `1.1.2.0`, build senza warning/errori, 152 test superati in ciascuna delle due esecuzioni, zero vulnerabilità NuGet note, packaging e verifica degli artefatti riusciti; installazione pulita, aggiornamento locale da 1.0.2 e disinstallazione silenziosi superati. Il test non avvia l’applicazione.
- Il primo run candidato [44183263335](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44183263335) ha intercettato un `AssemblyVersion` UI rimasto a 1.1.1. Corretto nel commit `3ea588b`; il run successivo è riuscito. Nessuna release è stata pubblicata durante questi run.

Questi risultati chiudono la validazione CI delle PR. La conferma dell’utente chiude il dubbio sull’avvio della 1.1.1; la pipeline stessa resta non interattiva e non prova i flussi desktop elencati sotto.

## Verifiche native ancora da eseguire

- [x] L’utente conferma che la finestra della 1.1.1 appare e che l’applicazione funziona correttamente.
- [ ] Provare secondo avvio, ripristino da tray/minimizzazione e riapertura dopo aggiornamento.
- [ ] Con vault sintetico, provare creazione, sblocco, migrazione, cambio credenziale/key file, blocco Win+L durante operazioni e dialoghi.
- [ ] Provare copia da controlli, scadenza clipboard, Win+V e cronologia/cloud di Windows.
- [ ] Provare Auto-Type con cambi di focus, modificatori, finestre elevate e match ambiguo; confermare anche il comportamento dichiarato della protezione screenshot.

I test automatici usano vault sintetici e adattatori finti. La conferma di avvio è un riscontro dell’utente; non equivale alla verifica dei singoli flussi della checklist. In questa sessione non è stata fatta automazione desktop dell’applicazione.

## Residui degli audit da chiudere o accettare esplicitamente

| Area | Riferimenti | Stato residuo |
| --- | --- | --- |
| Auto-Type, blocco sessione e clipboard Windows | A07–A09, B06 | Race foreground e titolo euristico; dialoghi/Win+L; cronologia e clipboard cloud non verificati nativamente. |
| Durabilità e backup | A11 | Mancano fault injection su filesystem, alimentazione e dispositivi reali. I fault test software non provano ogni comportamento hardware. |
| Segreti in memoria | B02 | Copie gestite, GC e librerie native impediscono una promessa di purge completa della RAM. |
| Cattura schermo | B07 | `SetWindowDisplayAffinity` è best-effort; servono prove native e non è una protezione da malware. |
| Esportazione/importazione | B04, C06 | CSV resta parziale e in chiaro; l’import Bitwarden conserva dati originali non supportati, ma carte/passkey e allegati non diventano funzioni native. |
| Coerenza sessione e architettura | C12, C14 | Restano limiti sulle date di ultimo accesso, catch generici e rework incompleto verso errori tipizzati e separazione completa di comandi/servizi. |
| Scalabilità | C13 | La paginazione riduce i dati trattenuti, ma ogni query autentica/decifra e scansiona O(N); scalari segreti restano in memoria e scritture/backup sono completi. Mancano segreti su richiesta, scritture differenziali e benchmark su dispositivi lenti/record grandi. |
| Restyle e accessibilità Windows | D03, D05–D07 | Rendering e contrasto sono stati simulati; mancano prove native di DPI 100–200%, schermi piccoli, testo ingrandito, tastiera, Narrator e high contrast. Il restyle e l’overflow comandi non sono uniformati completamente. |
| Revisione indipendente e resistenza ai guasti | A04, A11, E04 | Il formato 2 e migrazione/recovery non hanno revisione crittografica/penetration test indipendente; guasti fisici non attestati; provenienza licenze non equivale a certificazione legale. |

La firma Authenticode (E03) è **esclusa esplicitamente dall’utente** e non va riaperta come requisito per questa release. I pacchetti restano NotSigned.

La tabella originale dei 49 rilievi e i limiti dettagliati restano in [remediation](REMEDIATION_2026-11-16.md), [readiness 1.1.0 del 17 novembre](RELEASE_READINESS_2026-11-17.md), [esito 1.1.1](RELEASE_OUTCOME_2026-11-17_1.1.1.md) e [benchmark](PERFORMANCE_2026-11-16.md). Questi documenti sono storici; questo riepilogo aggiorna lo stato dei merge e della CI, senza riclassificare i rilievi non provati come risolti.

## Passi release 1.1.2

1. Registrare gli esiti delle verifiche Windows ancora aperte sopra.
2. Per ogni residuo, scegliere tra implementazione/verifica prima della release oppure accettazione esplicita come limite noto; non descrivere come completato ciò che non è stato provato.
3. Versioni, changelog e note 1.1.2 sono aggiornati nella candidata; verificare che contengano solo cambiamenti inclusi.
4. La CI sul commit candidato è riuscita; far rieseguire i controlli sul merge commit di `main`.
5. Dopo revisione dei residui e successo dei gate, creare il commit di release e verificare provenienza, digest e feed pubblico; quindi aggiornare il report finale.
