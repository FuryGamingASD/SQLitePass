# Release readiness SQLitePass 1.1.2

Verifica aggiornata al 18 novembre 2026 sul commit base `acca23bd0c114ca6343a2732feb4b7017ec6e41d` di `main`; gli interventi successivi sono sulla candidata PR #7.

## Esito

La versione 1.1.2, il changelog e le note sono integrati in `main` dalla PR [#6](https://github.com/FuryGamingASD/SQLitePass/pull/6), merge commit `acca23b`. La CI sul merge commit, [Verify 44187232756](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44187232756), è riuscita. Non esiste ancora un tag, un pacchetto pubblico o una release 1.1.2.

L’utente conferma che la 1.1.1 si apre e funziona correttamente. Ritiene probabile che anche la 1.1.0 funzionasse: il problema osservato era la finestra non visibile durante una sessione remota senza essere davanti al PC. La diagnosi precedente di un difetto applicativo non è quindi confermata. Restano da svolgere o classificare le altre verifiche native e i residui tecnici dell’audit.

**Gate di sicurezza residuo:** A07 (Auto-Type) è ancora classificato **P1 mitigato**, non corretto, nella remediation. La candidata conserva il PID dall'hotkey iniziale, mostra il nome dell'app nella conferma e ricontrolla foreground/HWND/PID/titolo dopo la ricerca async e prima/dopo ciascun input; i test sintetici coprono cambi focus/processo/titolo anche sull'ultimo tasto o carattere e rifiuto di HWND riutilizzato. Questo riduce l'esposizione e rende il destinatario più visibile, ma nome processo e titolo non autenticano l'origine e non si elimina la race tra verifica e `SendInput`. Quest'ultimo immette eventi nello stream globale di tastiera, non in un HWND specifico. La CI usa input finti e non verifica i flussi nativi. Il rilascio non può essere descritto come chiusura dell'audit: prima della pubblicazione occorre chiudere A07 con prove adeguate oppure accettare esplicitamente questo rischio residuo.

## Stato verificato dopo i merge

- Le PR Dependabot #1–#5 e la PR release-prep #6 sono integrate; non risultano altre PR aperte.
- Il commit base prima della preparazione era `72edb5f`, verificato da [44178968940](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44178968940). La versione 1.1.2 è ora su `main` al commit `acca23b`.
- La CI sul merge commit [44187232756](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44187232756) ha completato build senza warning/errori, due esecuzioni della suite da 152 test, audit NuGet con `KnownNugetVulnerabilities: 0`, packaging, verifica artefatti e smoke test installer. Installazione pulita, aggiornamento locale da 1.0.2 e disinstallazione sono riusciti senza avviare l’applicazione. Il job `release` è stato saltato perché il commit non era una pubblicazione.
- Gli aggiornamenti Actions sono ora integrati e fissati a SHA: checkout 7.0.1, setup-dotnet 6.1.0, upload-artifact 7.0.2, download-artifact 8.0.2. La vecchia nota di manutenzione dei pin Node 20→24 nel report di remediation è superata da questi merge.
- Microsoft.Data.Sqlite è 11.0.0; i lock file dei cinque progetti e l’inventario/licenze sono stati aggiornati, compresa la nuova dipendenza SQLite.
- La release più recente pubblicata resta [1.1.1](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.1.1). `main` dichiara versione 1.1.2; non è stata pubblicata.
- La CI della candidata ha prodotto assembly `1.1.2.0`, build senza warning/errori, 152 test superati in ciascuna delle due esecuzioni, zero vulnerabilità NuGet note, packaging e verifica degli artefatti riusciti; installazione pulita, aggiornamento locale da 1.0.2 e disinstallazione silenziosi superati. Il test non avvia l’applicazione.
- Il primo run candidato [44183263335](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44183263335) ha intercettato un `AssemblyVersion` UI rimasto a 1.1.1. Corretto nel commit `3ea588b`; il run successivo è riuscito. Nessuna release è stata pubblicata durante questi run.
- Nel lavoro successivo su C14, i conflitti di revisione/sessione sono stati tipizzati come `VaultConflictException` e mappati esplicitamente dalla UI. Restore locked, build locale della solution (zero warning/errori) e 152 test sono riusciti. La CI Windows sul candidato `583e3bb` è riuscita nei run [44193383263](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44193383263) e [44193373481](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44193373481); il job di pubblicazione è stato saltato perché la PR è draft.
- Ulteriore mitigazione A07: PID catturato prima delle operazioni async, nome del processo mostrato nella conferma e revalidazione del foreground dopo la ricerca. HWND/PID/titolo sono verificati prima e dopo ogni input; il parsing delle credenziali avviene dopo la verifica iniziale. Le regressioni coprono cambio focus/processo/titolo anche sull'ultimo evento e rifiuto di HWND riutilizzato. C14: la verifica canary non converte più eccezioni inattese del motore in credenziale errata; gli errori imprevisti di drag-and-drop raggiungono il safe-lock. `dotnet test SQLitePass.slnx -c Release --no-restore`: 159/159; build Release: zero warning/errori. La CI Windows di queste modifiche è da eseguire.
- Run Windows [44197330969](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44197330969): build senza warning/errori e 159/159 test superati, ma il gate finale artefatti ha rilevato che il conteggio atteso era ancora 152. Non c'erano test falliti; il candidato aggiorna il validatore a 159, mantenendo il controllo contro deriva nella discovery.

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
| Auto-Type, blocco sessione e clipboard Windows | A07–A09, B06 | HWND/PID/titolo ricontrollati prima e dopo ogni input; resta la race tra controllo e `SendInput` globale e il titolo euristico. Dialoghi/Win+L, finestre elevate, cronologia e clipboard cloud non verificati nativamente. |
| Durabilità e backup | A11 | Mancano fault injection su filesystem, alimentazione e dispositivi reali. I fault test software non provano ogni comportamento hardware. |
| Segreti in memoria | B02 | Copie gestite, GC e librerie native impediscono una promessa di purge completa della RAM. |
| Cattura schermo | B07 | `SetWindowDisplayAffinity` è best-effort; servono prove native e non è una protezione da malware. |
| Esportazione/importazione | B04, C06 | CSV resta parziale e in chiaro; l’import Bitwarden conserva dati originali non supportati, ma carte/passkey e allegati non diventano funzioni native. |
| Coerenza sessione e architettura | C12, C14 | Con questa candidata, i conflitti espongono `VaultConflictException`; la verifica canary intercetta solo mismatch crittografici/di formato; errori imprevisti di drag-and-drop propagano al safe-lock e gli errori attesi di caricamento icona sono registrati. Restano limiti sulle date di ultimo accesso, altri catch generici, task non osservate e il rework verso una separazione completa di comandi/servizi. |
| Scalabilità | C13 | La paginazione riduce i dati trattenuti, ma ogni query autentica/decifra e scansiona O(N); scalari segreti restano in memoria e scritture/backup sono completi. Mancano segreti su richiesta, scritture differenziali e benchmark su dispositivi lenti/record grandi. |
| Restyle e accessibilità Windows | D03, D05–D07 | Rendering e contrasto sono stati simulati; mancano prove native di DPI 100–200%, schermi piccoli, testo ingrandito, tastiera, Narrator e high contrast. Il restyle e l’overflow comandi non sono uniformati completamente. |
| Revisione indipendente e resistenza ai guasti | A04, A11, E04 | Il formato 2 e migrazione/recovery non hanno revisione crittografica/penetration test indipendente; guasti fisici non attestati; provenienza licenze non equivale a certificazione legale. |

La firma Authenticode (E03) è **esclusa esplicitamente dall’utente** e non va riaperta come requisito per questa release. I pacchetti restano NotSigned.

La tabella originale dei 49 rilievi e i limiti dettagliati restano in [remediation](REMEDIATION_2026-11-16.md), [readiness 1.1.0 del 17 novembre](RELEASE_READINESS_2026-11-17.md), [esito 1.1.1](RELEASE_OUTCOME_2026-11-17_1.1.1.md) e [benchmark](PERFORMANCE_2026-11-16.md). Questi documenti sono storici; questo riepilogo aggiorna lo stato dei merge e della CI, senza riclassificare i rilievi non provati come risolti.

## Passi release 1.1.2

1. Registrare gli esiti delle verifiche Windows ancora aperte sopra.
2. Per ogni residuo, scegliere tra implementazione/verifica prima della release oppure accettazione esplicita come limite noto; non descrivere come completato ciò che non è stato provato.
3. Versioni, changelog e note 1.1.2 sono aggiornati nella candidata; verificare che contengano solo cambiamenti inclusi.
4. CI candidata e CI sul merge commit di `main` sono riuscite.
5. Dopo la revisione dei residui e l’approvazione esplicita alla pubblicazione, integrare il commit `[release] Publish SQLitePass 1.1.2` con squash. Quel push su `main` avvierà la pubblicazione; verificare poi provenienza, digest e feed pubblico e aggiornare il report finale.
