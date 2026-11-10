# SQLitePass v1.0.0 — Prima Release Ufficiale 🚀

**SQLitePass** è un moderno password manager desktop per Windows, progettato per offrire un'alternativa sicura, reattiva e anti-corruzione al tradizionale formato monolitico `.kdbx` di KeePass.

---

### 🛡️ Perché SQLitePass?

* **Architettura Anti-Corruzione a Due Livelli**:
  * Il database principale SQLite (`.kpd`) opera in modalità **WAL (Write-Ahead Logging)**, garantendo integrità atomica delle transazioni anche in caso di crash o spegnimento improvviso di Windows.
  * **Dual Isolated Backup**: a ogni salvataggio viene generata o aggiornata una copia esatta `[vault].backup` completamente disconnessa e chiusa, azzerando il rischio di corruzione simultanea.
* **Crittografia Zero-Knowledge a livello di campo**:
  * Il database SQLite rimane ispezionabile e riparabile a livello strutturale con qualsiasi client SQL standard, mentre le password, le note e gli allegati sensibili sono cifrati individualmente con **AES-256-GCM** e chiave derivata tramite **Argon2id**.
* **Parità Funzionale con KeePass 2**:
  * **Auto-Type Globale** (`Ctrl+Alt+A`) con rilevamento della finestra attiva.
  * **Doppio click mirato**: copia rapida del nome utente, copia della password con cancellazione automatica degli appunti in 12 secondi, apertura immediata dell'URL nel browser.
  * **Generatore di Password Sicure** con calcolo istantaneo dell'entropia in bit.
  * **Auto-Lock Intelligente**: blocco automatico su inattività, alla riduzione a icona e al blocco della sessione Windows (`Win+L`).
  * **Integrazione System Tray**: riduzione e chiusura nell'area di notifica di Windows.
  * **Import / Export CSV** compatibile con KeePass 2.
* **Graphic Design & Temi**:
  * Tema Chiaro luminoso e pulito (predominanza bianco puro `#FFFFFF` con righe alternate soft-sky e icone di categoria vivaci).
  * Tema Scuro *Dark Obsidian* per lavoro notturno a basso affaticamento visivo.

---

### 📦 File Inclusi nel Download

| File | Descrizione |
|---|---|
| **`SQLitePass-win-Setup.exe`** | **Installer Ufficiale** (Consigliato): installa l'app sul PC, crea i collegamenti sul Desktop/Menu Start e supporta gli aggiornamenti automatici istantanei in un click. |
| **`SQLitePass-win-Portable.zip`** | **Versione Portabile**: non richiede installazione. Basta estrarre lo zip su qualsiasi cartella o chiavetta USB e avviare `SQLitePass.exe`. |
| `SQLitePass-1.0.0-full.nupkg` | Pacchetto delta per il motore di auto-aggiornamento Velopack. |
| `RELEASES` & `releases.win.json` | Indici di versione utilizzati dall'app per rilevare automaticamente nuove release. |

---

### ⚖️ Licenza
Distribuito con licenza **GNU General Public License v3.0 (GPLv3)**.
Software libero, aperto e protetto da copyleft forte.
