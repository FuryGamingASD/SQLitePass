# KeePass MySQL (KeePassDB)

Un software moderno per la gestione sicura delle password (.NET 8 C# + WPF) ispirato alle funzionalità di **KeePass 2**, ma progettato nativamente per salvare e sincronizzare tutte le informazioni su un database **MySQL o MariaDB** (sia locale che remoto).

---

## 🔒 Modello di Sicurezza Zero-Knowledge

La sicurezza è il principio fondamentale del software:
1. **Crittografia Client-Side (AES-256-GCM):**
   * Ogni singolo campo sensibile (Titolo, Username, Password, URL, Note, Tag, Campi Personalizzati, File Allegati) viene cifrato sul client prima di essere inviato a MySQL.
   * Il server MySQL e chiunque abbia accesso al database vedono esclusivamente payload cifrati.
   * Ogni record utilizza un nonce/IV univoco di 12 byte generato crittograficamente e un tag di autenticazione a 128 bit (integrità garantita contro manomissioni).
2. **Key Derivation Function (KDF):**
   * Utilizza **Argon2id** (lo standard moderno di KeePass KDBX4), resistente ad attacchi brute-force con GPU ed ASIC.
   * Salt casuale univoco a 128 bit salvato nei metadati del vault.
3. **Master Key Flessibile:**
   * Supporta **Master Password**, **Key File** opzionale (file binario/chiave `.key`) o la combinazione di entrambi (tramite doppio hashing SHA-256 composito).
4. **Protezione Locale DPAPI:**
   * La password di connessione al database MySQL viene memorizzata in locale protetta tramite **Windows DPAPI** (`DataProtectionScope.CurrentUser`).

---

## 🚀 Funzionalità KeePass 2 Incluse

* **Gestione Gerarchica dei Gruppi:** Albero dei gruppi annidati (General, Windows, Network, Internet, eMail, Banking, Recycle Bin, ecc.) con creazione di sottogruppi, rinomina e cancellazione a cascata.
* **Voci Complete:**
  * Titolo, Nome Utente, Password (mascherata con toggle mostra/nascondi), URL, Note formattate, Tag.
  * Date di scadenza con avviso e calendario.
  * Cronologia delle modifiche per ogni voce (versioning con ripristino istantaneo di vecchie revisioni).
  * Campi personalizzati stringa (protetti o visibili).
  * File allegati binari cifrati (aggiunta, download ed esportazione).
* **Generatore di Password Avanzato:**
  * Lunghezza personalizzabile (fino a 64 caratteri).
  * Inclusione di Maiuscole, Minuscole, Cifre, Simboli Speciali, Parentesi.
  * Esclusione caratteri ambigui (`Il1O0|`).
  * Calcolo in tempo reale dell'**entropia in bit** e barra grafica della qualità (Rosso -> Giallo -> Verde -> Ciano).
* **Auto-Type Globale:**
  * Emulazione battitura nativa Windows tramite Win32 API (`SendInput`).
  * Supporto per sequenze personalizzate (es. `{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{DELAY 500}`, placeholder per campi personalizzati `{S:Campo}`).
* **Protezione Appunti (Clipboard):**
  * Copia rapida di Username e Password con **cancellazione automatica degli appunti dopo 12 secondi** e countdown nella barra di stato.
* **Ricerca Istantanea:**
  * Barra di ricerca in tempo reale su tutte le voci (titolo, username, url, note, tag).
* **Import / Export:**
  * Compatibilità con file **CSV** standard KeePass.

---

## 🗄️ Schema del Database MySQL

All'avvio o alla creazione del vault, l'applicazione crea automaticamente il database e le seguenti tabelle:

| Tabella | Descrizione |
| :--- | :--- |
| `kp_vault_metadata` | Metadati del vault, salt Argon2id, parametri di memoria e token canary di verifica |
| `kp_groups` | Gruppi gerarchici (con parent_id e nome cifrato) |
| `kp_entries` | Voci con titolo, credenziali, note e date (tutti cifrati client-side) |
| `kp_custom_fields` | Campi personalizzati aggiuntivi cifrati associati alle voci |
| `kp_attachments` | File allegati binari cifrati in formato BLOB |
| `kp_history` | Snapshot delle versioni precedenti delle voci per audit e rollback |

---

## 📦 Installer (Setup.exe) & Auto-Updater

Il progetto include il supporto completo a **Velopack** per la creazione dell'installer e per la gestione automatica degli aggiornamenti via **GitHub Releases**.

### File generati nella cartella `./releases/`
* **`KeePassMySQL-win-Setup.exe`**: Il file di installazione Windows auto-estraente. Crea i collegamenti nel Desktop e nel Menu Start e gestisce l'aggiornamento automatico.
* **`KeePassMySQL-win-Portable.zip`**: Versione portabile per chi non desidera installare nulla.
* **`KeePassMySQL-x.x.x-full.nupkg` + `RELEASES`**: Pacchetti utilizzati dal meccanismo di Auto-Update.

### Come pubblicare una nuova versione con Auto-Update:
1. Imposta il tuo repository GitHub nel file `KeePassMySQL.UI/Services/UpdateService.cs`:
   ```csharp
   public static string GitHubRepoUrl { get; set; } = "https://github.com/TUO_UTENTE/TUO_REPO";
   ```
2. Genera il setup della nuova versione eseguendo lo script:
   ```powershell
   .\build-installer.ps1 -Version "1.0.1"
   ```
3. Vai sul tuo repository GitHub, crea una nuova **Release** (es. tag `v1.0.1`) e trascina tutti i file generati nella cartella `./releases/`.
4. All'avvio dell'applicazione (o tramite il menu **Help -> Verifica Aggiornamenti...**), il programma rileverà la nuova versione e chiederà all'utente se desidera aggiornare e riavviare con un click!

---

## 🛠️ Come Compilare ed Eseguire

### Requisiti
* Windows 10 / 11 o Windows Server
* **.NET 8 SDK** (già installato)
* Un'istanza **MySQL** (v5.7, 8.0, 8.4+) o **MariaDB** (locale su localhost o remota)

### Esecuzione
Dalla root del repository:

```powershell
# Esecuzione dei test unitari (Crittografia, KDF, Password Gen, CSV)
dotnet test

# Avvio dell'applicazione
dotnet run --project KeePassMySQL.UI
```

Al primo avvio:
1. Configura l'indirizzo del server MySQL (es. `127.0.0.1` o IP remoto), porta (`3306`), utente e password del DB.
2. Clicca **Test Connection** per verificare la raggiungibilità del database.
3. Inserisci la tua **Master Password** (e facoltativamente un **Key File**).
4. Clicca **Create New Vault** per inizializzare il database e creare il vault. Dalle volte successive basterà cliccare **Open Vault**.
"# SQLitePass" 
"# SQLitePass" 
