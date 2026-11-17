# Pubblicazione SQLitePass 1.1.0

La release 1.1.0 è stata pubblicata il 17 novembre 2026 dopo lʼautorizzazione dellʼutente, mantenendo la firma Authenticode esclusa. Job CI, asset e feed pubblico sono verificati nel [report di esito](RELEASE_OUTCOME_2026-11-17.md). Restano aperti i collaudi nativi e la revisione indipendente dellʼaudit.

Il successivo hotfix [SQLitePass 1.1.1](RELEASE_OUTCOME_2026-11-17_1.1.1.md) corregge lʼattivazione della finestra Windows e risulta pubblicato con feed verificato; la prova interattiva di apertura sul desktop resta ancora da eseguire.

Il workflow [Verify](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml) esegue restore locked, build con warning bloccanti, regressioni con copertura, verifiche WPF isolate, scansione NuGet, packaging, controlli di runtime/licenze/hash e installazione/aggiornamento/disinstallazione silenziosi. I risultati sono conservati come artefatti del run.

Un push su main il cui messaggio contiene `[release]` abilita il job di pubblicazione soltanto dopo il successo del job Windows. Il job verifica che gli artefatti provengano dal commit pulito corrente, crea una bozza, carica gli asset e confronta ogni digest SHA-256 remoto prima della pubblicazione. Una release pubblicata esistente non viene sovrascritta. Il tag deve corrispondere al commit verificato.

La release include `release-provenance.json` con commit/tree, SDK, run CI, stato della firma e limiti dei collaudi. `checksums.json` copre gli asset e il file di provenienza. Il token di pubblicazione è temporaneo, fornito da GitHub Actions con permesso contents:write soltanto al job di release; i controlli ordinari hanno contents:read.

Dopo la pubblicazione il verificatore usa GithubSource di Velopack senza autenticazione, come il client pubblico, per leggere il feed e scaricare il pacchetto completo. Controlla versione, dimensione e digest senza avviare lʼapp o aprire vault.

La pubblicazione non chiude automaticamente lʼaudit. Win+L, clipboard/cloud, Auto-Type e accessibilità/DPI nativi, guasti hardware e revisione indipendente restano distinti dalle verifiche automatiche. Il formato 2 richiede SQLitePass 1.1 o successivo; conservare copie indipendenti prima di migrare file 1.0.
