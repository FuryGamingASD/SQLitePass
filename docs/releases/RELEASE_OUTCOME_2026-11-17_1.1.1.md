# Esito pubblicazione SQLitePass 1.1.1

Verifica del 17 novembre 2026. La release 1.1.1 corregge l’attivazione della finestra Windows quando SQLitePass rimane nell’area notifiche o minimizzato senza una finestra accessibile. La firma Authenticode era fuori dal perimetro della release.

## Pubblicazione

- Release: [SQLitePass 1.1.1](https://github.com/FuryGamingASD/SQLitePass/releases/tag/v1.1.1), pubblicata il 17 novembre alle 17:00:59 UTC.
- Tag e commit: `v1.1.1` → `cbe664dbf73fed35be291992fbbcd04c70af38ed` (`[release] Publish SQLitePass 1.1.1 window activation hotfix`).
- Provenienza verificata: build `1.1.1-da0e920ff3a944edb484b3b46711691d`, albero sorgente `096c1daa781d70a78ae44320d8ff3f5296673752`, working tree pulito, SDK 10.0.404.
- Workflow [44130997786](https://github.com/FuryGamingASD/SQLitePass/actions/runs/44130997786): job Windows e job release riusciti. Il job release ha verificato hash e dimensioni degli asset pubblicati e ha scaricato anonimamente il feed e il pacchetto tramite la libreria Velopack dell’applicazione.
- Pacchetto completo: 81.659.667 byte; SHA-256 `002CB198E6978F7359D0C0FE7479484E58723075CF2D33025D278721E1A5F3FD`.
- [Installer Windows](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.1.1/SQLitePass-win-Setup.exe), [ZIP portable](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.1.1/SQLitePass-win-Portable.zip), [pacchetto completo](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.1.1/SQLitePass-1.1.1-full.nupkg).

## Correzione

- L’avvio ripetuto segnala l’attivazione all’istanza esistente: la finestra di sblocco viene mostrata e ripristinata se minimizzata, mentre la finestra del vault viene ripristinata dal tray/minimizzazione.
- L’avvio con `--minimized` conserva l’avvio nel tray; un successivo avvio normale apre la finestra di sblocco nell’istanza esistente.
- I callback Velopack per aggiornamento e disinstallazione chiedono all’istanza attiva di chiudersi e attendono il rilascio del mutex fino a 10 secondi.
- Formato vault, dati del vault e preferenze utente non sono stati modificati.

## Verifiche e limiti

- 152 test CI superati; 5 progetti NuGet analizzati, nessuna vulnerabilità nota rilevata.
- Installazione pulita, aggiornamento locale 1.0.2 → 1.1.1 e disinstallazione silenziosi superati; il report indica che le preferenze sono rimaste invariate.
- I test d’installazione **non avviano l’applicazione** e non aprono vault reali.
- Alla pubblicazione, il rapporto di provenienza riportava `NativeInteractiveQaVerified: false`. Il collaudo desktop dell’avvio è stato completato dopo la pubblicazione ed è documentato sotto.
- Applicazione e installer sono `NotSigned`. La revisione di sicurezza indipendente e le verifiche native con DPI e Narrator restano aperte.

Per riprovare, installare il nuovo [SQLitePass-win-Setup.exe](https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.1.1/SQLitePass-win-Setup.exe) e avviare SQLitePass dal menu Start. Alla prima apertura deve comparire la schermata di sblocco. Se rimane soltanto l’icona nell’area notifiche, la correzione non è confermata sul sistema interessato e serve raccogliere nuovi dettagli diagnostici prima di dichiarare il problema risolto.

## Verifica desktop dopo la pubblicazione

Il collaudo desktop dopo la pubblicazione ha confermato che SQLitePass 1.1.1 si apre e funziona. La finestra della 1.1.0 risultava invisibile durante una sessione remota. Per quella versione il corretto funzionamento sul desktop locale resta plausibile e la regressione di avvio resta da confermare. La verifica successiva ha riguardato l’apertura della finestra. Gli altri flussi nativi dell’audit richiedevano prove dedicate.
