# Esito pubblicazione SQLitePass 1.1.0

Data verifica: 17 novembre 2026. La release stabile 1.1.0 è pubblicata ed è stata riletta senza autenticazione dal feed pubblico. La firma Authenticode è stata esclusa su indicazione esplicita dellʼutente. La pubblicazione non chiude le attività native e indipendenti dellʼaudit.

## Provenienza

- [Release pubblica v1.1.0](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.1.0), pubblicata alle 14:34:40 UTC (15:34:40 Europe/Rome).
- [Workflow GitHub Actions 44103412187](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44103412187): conclusione `success` per i job `windows` e `release`.
- Commit sorgente: `8e3b6836719d4844b242f362c9abc023145d62c1`; tag `v1.1.0` riferito a questo commit. Provenienza: build `1.1.0-c2247392c15b4103a14291ebd3c02c70`, tree `4d8c976dd882e3442c577def20e8a230bc349ea8`, working tree pulito.
- SDK di build 10.0.404; runtime .NET e Windows Desktop distribuiti 10.0.15; Windows x64 self-contained.

## Verifiche CI e distribuzione

- Restore bloccato, build Release con zero errori e zero avvisi, 152 test superati, zero falliti o ignorati.
- Scansione NuGet su cinque progetti senza vulnerabilità note.
- Verifica WPF in memoria: 1.080 casi e 105 coppie di contrasto, minimo 4,5669:1.
- Installer: installazione pulita, aggiornamento locale 1.0.2→1.1.0 e disinstallazione silenziosi superati. Le preferenze esistenti non sono state alterate.
- Feed pubblico Velopack: lettura anonima riuscita tramite la libreria dellʼapplicazione; versione 1.1.0, download anonimo del pacchetto completo e verifica SHA-256 riusciti. Dimensione del pacchetto completo: 81.657.814 byte; SHA-256 `0FB1C92901C0F379B93508226A0463B27D3E2DB94CC5F49DE9BD191F801DD2A3`.
- Otto asset pubblici e relativi digest registrati dal servizio GitHub:

| Asset | SHA-256 |
| --- | --- |
| `SQLitePass-win-Setup.exe` | `E192CD35F0C4270D83F26BE0ADF37C5E51211E5633F3E8899DDB4472FB7396B1` |
| `SQLitePass-win-Portable.zip` | `98BFD85DA6B61831D2DC99153E16DEF59BC4C435E00B402A8B024CE843DFD07B` |
| `SQLitePass-1.1.0-full.nupkg` | `0FB1C92901C0F379B93508226A0463B27D3E2DB94CC5F49DE9BD191F801DD2A3` |
| `RELEASES` | `131E3BD1791276FF8FCADA337A5F66F88E49B4407B175AF6C9EE78F148434465` |
| `releases.win.json` | `6D8DCE1093A8D13755030D630A6D2B59C61FBA51F1B619CC29DC8DC6CD2329EE` |
| `assets.win.json` | `5634D2881AB8C67E45ABFCD8FF74BDDC60401E6FE54AE005DD72E30EDF825DE2` |
| `release-provenance.json` | `8365E597C6B60723EA1233CDE0FA0C7165BE97EB1DDEE0178B39525F18700FD4` |
| `checksums.json` | `67AD1FA43AE17637973E874A96BA0316CB4F288C8AE8DC81F283F308D055B00B` |

Il digest degli asset coincide con quello esposto dalla release GitHub; il manifest pubblicato contiene i checksum degli asset di release e della provenienza. Lʼapplicazione non è stata avviata e non è stato aperto alcun vault durante i controlli CI/feed.

## Limiti e lavoro successivo

- Applicazione e installer sono `NotSigned`, come concordato. Gli utenti Windows potrebbero vedere avvisi di provenienza o reputazione; la firma Authenticode richiede una release successiva con certificato autorizzato.
- Nessuna prova interattiva nativa è attestata: flussi di sblocco/migrazione/rekey, Win+L, clipboard e cloud clipboard, Auto-Type con focus/modificatori/app elevate, tastiera/Narrator, DPI e protezione cattura. I rendering e i test automatizzati non sostituiscono questi collaudi.
- Nessuna revisione indipendente del formato/migrazione/recovery o prova di guasto hardware è stata effettuata. C13 resta parziale; il costo di scansione/autenticazione è O(N), i dati scalari di pagina restano residenti e scritture/backup completi. Restano inoltre il rework di architettura e il restyle sistematico descritti nellʼ[audit](../audit/AUDIT_2026-11-16.md).
- Il workflow ha emesso avvisi di manutenzione perché alcune action fissate usano ancora target Node.js 20 mentre GitHub annuncia il passaggio forzato a Node.js 24. Non ha bloccato i job; aggiornare e riverificare i pin delle action.

Gli artefatti e i report scaricati dal run sono conservati localmente in `artifacts/verification/published-1.1.0/` e sono ignorati da Git; non fanno parte della documentazione versionata. Per la mappa completa tra audit, correzioni e limiti consultare [REMEDIATION_2026-11-16.md](../audit/REMEDIATION_2026-11-16.md), la [verifica storica della candidata 1.1.0](RELEASE_READINESS_2026-11-17.md), l'[esito 1.1.2](RELEASE_OUTCOME_1.1.2.md) e la [readiness 1.1.3](RELEASE_READINESS_1.1.3.md).
