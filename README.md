# SQLitePass 🛡️

Un password manager moderno, leggero e ultra-resiliente per Windows (.NET 8 C# + WPF) ispirato alle funzionalità di **KeePass 2**, ma riprogettato dalle fondamenta per **eliminare per sempre il rischio di corruzione dei dati**.

---

## 💡 Perché SQLitePass invece di KeePass?

Nei file `.kdbx` tradizionali di KeePass, l'intero database è un unico blob monolitico cifrato. Se un aggiornamento di Windows, un riavvio forzato o una mancanza di corrente interrompono la scrittura, anche solo 1 byte corrotto o l'header alterato rendono **l'intero archivio irrecuperabile al 100%** (errore *"Header corrupt"* o *"HMAC mismatch"*).

### La soluzione architetturale di SQLitePass:
1. **Database SQLite Non Cifrato a Livello di File:**
   * Il file `.kpd` è un database SQLite standard, ispezionabile e riparabile in qualunque momento (anche con software open-source terzi come *DB Browser for SQLite*).
   * **Cifratura Zero-Knowledge a livello di campo (Field-Level Encryption):** Solo i dati sensibili (Password, Note, Allegati, Campi personalizzati) sono cifrati individualmente con **AES-256-GCM** e chiave derivata da **Argon2id**.
   * *Il vantaggio:* Anche in caso di settori fisici danneggiati sull'hard disk, si perde al massimo quel singolo byte, mentre **tutte le altre password rimangono perfettamente integre e decifrabili**.
2. **Modalità WAL (Write-Ahead Logging):**
   * Transazioni ACID native con `PRAGMA journal_mode = WAL;` e `PRAGMA synchronous = NORMAL;`. Le modifiche vengono scritte in append su un log dedicato prima del commit: anche se salta la corrente durante la scrittura, al riavvio SQLite ripristina istantaneamente l'ultimo stato valido.
3. **Dual-Backup Atomico e Isolato (`.kpd.backup`):**
   * Il programma tiene aperto in memoria solo il file principale.
   * Ad ogni aggiunta, modifica o eliminazione, viene eseguito uno snapshot tramite l'API online di SQLite su un file temporaneo, poi sostituito atomicamente con `File.Replace` in `.kpd.backup`.
   * Il file di backup **non rimane mai aperto**, azzerando il rischio di corruzione simultanea.
   * Funzione **"Ripristina da .backup"** direttamente nella schermata iniziale.
4. **Zero Installazione Server:**
   * SQLite è una libreria incorporata: nessun server MySQL da installare, nessun servizio in background o porta aperta.

---

## 🚀 Funzionalità KeePass 2 Incluse

* **Doppio Click Intelligente sulle Colonne:**
  * **Password:** Copia istantaneamente la password negli appunti (senza mostrarla) e attiva il countdown di cancellazione automatica (default 12s).
  * **User Name:** Copia il nome utente negli appunti.
  * **URL:** Apre direttamente il sito web nel browser predefinito.
  * **Titolo / Riga:** Apre la finestra di modifica completa della voce.
* **Global Auto-Type (`Ctrl+Alt+A`):**
  * Scorciatoia globale Windows (`SendInput`): rileva il titolo della finestra o del browser attivo e digita automaticamente credenziali e sequenze personalizzate (es. `{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{DELAY}`).
* **Area di Notifica (System Tray):**
  * Icona nell'area di notifica di Windows con menu contestuale rapido.
  * Opzione *Riduci a icona nella Tray*.
  * Opzione *Il pulsante di chiusura (X) riduce nella Tray invece di uscire*.
* **Blocco Automatico Configurabile:**
  * Blocco per inattività (1, 3, 5, 10, 15, 30 minuti).
  * Blocco automatico alla riduzione a icona.
  * Blocco automatico al blocco della postazione Windows (`Win+L`).
* **Generatore di Password:**
  * Calcolo in tempo reale dell'**entropia in bit** e barra grafica della qualità.
  * Maiuscole, Minuscole, Cifre, Simboli, Parentesi ed esclusione caratteri ambigui (`Il1O0|`).
* **Cronologia e Versioning:**
  * Salva le versioni precedenti di ogni voce per audit e ripristino di vecchie password.
* **Allegati e Campi Personalizzati:**
  * Supporto per allegati binari cifrati e campi chiave/valore extra protetti.
* **Import / Export CSV:**
  * Compatibile con il formato di esportazione standard di KeePass.
* **Backup Secondario Esterno:**
  * Possibilità di indicare nelle Impostazioni una cartella secondaria (es. chiavetta USB o secondo hard disk) per una copia ridondante istantanea.

---

## 🛠️ Come Compilare ed Eseguire

### Requisiti
* Windows 10 / 11 o Windows Server
* **.NET 8 SDK** (già installato)

```powershell
# Esegui i test unitari (Crittografia, KDF, SQLite, Backup atomico, CSV)
dotnet test SQLitePass.slnx

# Avvia l'applicazione direttamente da codice
dotnet run --project SQLitePass.UI
```

---

## 📦 Installer e Auto-Update

Per generare il pacchetto di installazione e la versione portatile:

```powershell
.\build-installer.ps1 -Version "1.0.0"
```

I file generati nella cartella `./releases/`:
* **`SQLitePass-win-Setup.exe`**: Installer autonomo con creazione automatica delle icone su Desktop e Menu Start e supporto agli aggiornamenti automatici da GitHub Releases.
* **`SQLitePass-win-Portable.zip`**: Versione portatile autonoma.
* **`SQLitePass-x.x.x-full.nupkg` + `RELEASES`**: Pacchetti utilizzati dal modulo di auto-update.
