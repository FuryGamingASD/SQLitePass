# Pubblicazione SQLitePass 1.1.0

La pubblicazione è stata autorizzata dallʼutente il 17 novembre 2026, mantenendo la firma Authenticode esclusa. Questo processo non attesta i collaudi nativi o la revisione indipendente ancora aperti nel rapporto di audit.

Il workflow [Verify](https://github.com/FuryGamingASD/SQLitePass/actions/workflows/verify.yml) esegue restore locked, build con warning bloccanti, regressioni con copertura, verifiche WPF isolate, scansione NuGet, packaging, controlli di runtime/licenze/hash e installazione/aggiornamento/disinstallazione silenziosi. I risultati sono conservati come artefatti del run.

Un push su main il cui messaggio contiene `[release]` abilita il job di pubblicazione soltanto dopo il successo del job Windows. Il job verifica che gli artefatti provengano dal commit pulito corrente, crea una bozza, carica gli asset e confronta ogni digest SHA-256 remoto prima della pubblicazione. Una release pubblicata esistente non viene sovrascritta. Il tag deve corrispondere al commit verificato.

La release include `release-provenance.json` con commit/tree, SDK, run CI, stato della firma e limiti dei collaudi. `checksums.json` copre gli asset e il file di provenienza. Il token di pubblicazione è temporaneo, fornito da GitHub Actions con permesso contents:write soltanto al job di release; i controlli ordinari hanno contents:read.

Dopo la pubblicazione il verificatore usa GithubSource di Velopack senza autenticazione, come il client pubblico, per leggere il feed e scaricare il pacchetto completo. Controlla versione, dimensione e digest senza avviare lʼapp o aprire vault.

La pubblicazione non chiude automaticamente lʼaudit. Win+L, clipboard/cloud, Auto-Type e accessibilità/DPI nativi, guasti hardware e revisione indipendente restano distinti dalle verifiche automatiche. Il formato 2 richiede SQLitePass 1.1 o successivo; conservare copie indipendenti prima di migrare file 1.0.
