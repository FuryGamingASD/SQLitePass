using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace SQLitePass.UI.Services
{
    public static class LocalizationManager
    {
        public static string CurrentLanguage { get; private set; } = "it";

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
        {
            ["it"] = new Dictionary<string, string>
            {
                // General & Common
                ["AppTitle"] = "SQLitePass",
                ["Ok"] = "OK",
                ["Cancel"] = "Annulla",
                ["Save"] = "Salva",
                ["Exit"] = "Esci",
                ["Browse"] = "Sfoglia...",
                ["Copy"] = "Copia",
                ["Copied"] = "Copiato!",
                ["Delete"] = "Elimina",
                ["Edit"] = "Modifica",
                ["Duplicate"] = "Duplica",
                ["Close"] = "Chiudi",
                ["SearchPlaceholder"] = "Cerca nelle voci...",
                ["EntriesCount"] = "{0} voci",
                ["ClipboardClearedIn"] = "Appunti cancellati tra {0}s",
                ["VaultStatusDualBackup"] = "Vault SQLite (.kpd) • Dual Backup Attivo",

                // Menu
                ["MenuFile"] = "_File",
                ["MenuAddEntry"] = "_Aggiungi Voce...",
                ["MenuAddGroup"] = "_Aggiungi Gruppo...",
                ["MenuLockVault"] = "_Blocca Vault",
                ["MenuImportCsv"] = "_Importa CSV KeePass...",
                ["MenuExportCsv"] = "_Esporta CSV KeePass...",
                ["MenuSettings"] = "_Impostazioni...",
                ["MenuExit"] = "_Esci",
                ["MenuEdit"] = "_Modifica",
                ["MenuEditEntry"] = "_Modifica Voce",
                ["MenuDuplicateEntry"] = "_Duplica Voce",
                ["MenuDeleteEntry"] = "_Elimina Voce",
                ["MenuRenameGroup"] = "Rinomina Gruppo...",
                ["MenuDeleteGroup"] = "Elimina Gruppo",
                ["MenuView"] = "_Visualizza",
                ["MenuToggleTheme"] = "_Alterna Tema Chiaro / Scuro",
                ["MenuTogglePasswords"] = "_Mostra/Nascondi Password",
                ["MenuRefresh"] = "_Aggiorna",
                ["MenuTools"] = "_Strumenti",
                ["MenuPasswordGenerator"] = "_Generatore Password...",
                ["MenuAutoType"] = "_Auto-Type Voce Selezionata",
                ["MenuClearClipboard"] = "_Cancella Appunti Subito",
                ["MenuHelp"] = "_Aiuto",
                ["MenuCheckUpdates"] = "_Verifica Aggiornamenti...",
                ["MenuAbout"] = "_Informazioni su SQLitePass",

                // Toolbar
                ["TbNewEntry"] = "Nuova Voce",
                ["TbEdit"] = "Modifica",
                ["TbDelete"] = "Elimina",
                ["TbCopyUser"] = "Copia User",
                ["TbCopyPassword"] = "Copia Password",
                ["TbOpenUrl"] = "Apri URL",
                ["TbAutoType"] = "Auto-Type",
                ["TbGenerator"] = "Generatore",
                ["TbSettings"] = "Opzioni",
                ["TbLock"] = "Blocca",

                // Sidebar
                ["FoldersAndGroups"] = "Cartelle e Gruppi",
                ["NewGroup"] = "Nuovo Gruppo",
                ["AddSubgroup"] = "Aggiungi Sottogruppo...",

                // DataGrid Columns
                ["ColTitle"] = "Titolo",
                ["ColUserName"] = "User Name",
                ["ColPassword"] = "Password",
                ["ColUrl"] = "URL",
                ["ColNotes"] = "Note",
                ["ColUpdatedAt"] = "Ultima Modifica",

                // Preview Panel
                ["PrevUser"] = "Utente:",
                ["PrevPassword"] = "Password:",
                ["PrevEntropy"] = "Entropia:",
                ["PrevUrl"] = "URL:",
                ["PrevModified"] = "Modificato:",
                ["PrevNotes"] = "Note:",

                // Login Window
                ["LoginTitle"] = "SQLitePass - Sblocco Vault",
                ["LoginHeaderSubtitle"] = "Zero-Knowledge Vault • Anti-Corruzione con Dual Backup",
                ["LoginDatabaseFile"] = "File Database del Vault (*.kpd):",
                ["LoginBackupPresent"] = "✓ File .backup presente e sincronizzato",
                ["LoginRestoreBackup"] = "Ripristina da .backup",
                ["LoginMasterPassword"] = "Master Password:",
                ["LoginUseKeyFile"] = "Utilizza anche un Key File (.key opzionale)",
                ["LoginNewVault"] = "+ Nuovo Vault...",
                ["LoginUnlock"] = "Sblocca Vault",

                // Settings Dialog
                ["SettingsTitle"] = "Impostazioni - SQLitePass",
                ["SettingsHeading"] = "⚙️ Impostazioni del Programma",
                ["SettingsSectionAppearance"] = "🎨 Aspetto e Lingua",
                ["SettingsLanguage"] = "Lingua interfaccia:",
                ["SettingsDarkMode"] = "Attiva Tema Scuro (Dark Obsidian Mode)",
                ["SettingsShowPasswordDefault"] = "Mostra colonna password in chiaro all'apertura",
                ["SettingsSectionSecurity"] = "🔒 Sicurezza e Blocco Automatico",
                ["SettingsClipboardTimeout"] = "Cancella appunti (clipboard) dopo:",
                ["SettingsSeconds"] = "secondi",
                ["SettingsAutoLockTimeout"] = "Blocca vault per inattività dopo:",
                ["SettingsLockOnMinimize"] = "Blocca automaticamente il vault alla riduzione a icona",
                ["SettingsLockOnWindowsLock"] = "Blocca il vault al blocco della sessione Windows (Win+L)",
                ["SettingsSectionTray"] = "🖥️ Finestra e Area di Notifica (System Tray)",
                ["SettingsMinimizeToTray"] = "Riduci a icona nell'area di notifica (System Tray)",
                ["SettingsCloseToTray"] = "Il pulsante di chiusura (X) riduce nella tray invece di chiudere",
                ["SettingsAlwaysOnTop"] = "Mantieni la finestra sempre in primo piano",
                ["SettingsStartWithWindows"] = "Avvia SQLitePass all'avvio di Windows",
                ["SettingsSectionBackup"] = "🛡️ Affidabilità e Backup Automatico",
                ["SettingsDualFileBackup"] = "Crea e aggiorna il file .backup duplicato e isolato",
                ["SettingsDualFileDesc"] = "Il file .backup non resta mai aperto durante il lavoro, azzerando qualsiasi rischio di corruzione.",
                ["SettingsSecondaryDir"] = "Cartella backup secondaria opzionale (es. USB o disco esterno):",

                // Entry Edit Dialog
                ["EntryEditTitle"] = "Modifica Voce - SQLitePass",
                ["EntryNewTitle"] = "Nuova Voce - SQLitePass",
                ["TabCredentials"] = "Credenziali",
                ["TabHistory"] = "Cronologia",
                ["LblTitle"] = "Titolo:",
                ["LblUserName"] = "Nome Utente:",
                ["LblPassword"] = "Password:",
                ["LblQuality"] = "Qualità:",
                ["BtnGenerate"] = "⚡ Genera",
                ["LblUrl"] = "URL:",
                ["LblExpiration"] = "Scadenza:",
                ["ChkEnableExpiry"] = "Abilita data di scadenza",
                ["LblTags"] = "Tag:",
                ["LblNotes"] = "Note:",

                // Group Edit Dialog
                ["GroupEditTitle"] = "Modifica Gruppo",
                ["GroupNewTitle"] = "Nuovo Gruppo",
                ["LblGroupName"] = "Nome Gruppo:",

                // Password Generator Dialog
                ["GeneratorTitle"] = "Generatore Password Sicure",
                ["GenLength"] = "Lunghezza password:",
                ["GenUpper"] = "Lettere maiuscole (A-Z)",
                ["GenLower"] = "Lettere minuscole (a-z)",
                ["GenDigits"] = "Numeri (0-9)",
                ["GenSymbols"] = "Simboli speciali (!@#$%^&*)",
                ["GenExcludeAmbiguous"] = "Escludi caratteri ambigui (0, O, 1, l, I)",
                ["GenResult"] = "Password Generata:",
                ["GenRegenerate"] = "⚡ Rigenera",
                ["GenCopyAndClose"] = "Copia e Usa"
            },

            ["en"] = new Dictionary<string, string>
            {
                // General & Common
                ["AppTitle"] = "SQLitePass",
                ["Ok"] = "OK",
                ["Cancel"] = "Cancel",
                ["Save"] = "Save",
                ["Exit"] = "Exit",
                ["Browse"] = "Browse...",
                ["Copy"] = "Copy",
                ["Copied"] = "Copied!",
                ["Delete"] = "Delete",
                ["Edit"] = "Edit",
                ["Duplicate"] = "Duplicate",
                ["Close"] = "Close",
                ["SearchPlaceholder"] = "Search entries...",
                ["EntriesCount"] = "{0} entries",
                ["ClipboardClearedIn"] = "Clipboard clears in {0}s",
                ["VaultStatusDualBackup"] = "SQLite Vault (.kpd) • Dual Backup Active",

                // Menu
                ["MenuFile"] = "_File",
                ["MenuAddEntry"] = "_Add Entry...",
                ["MenuAddGroup"] = "_Add Group...",
                ["MenuLockVault"] = "_Lock Vault",
                ["MenuImportCsv"] = "_Import KeePass CSV...",
                ["MenuExportCsv"] = "_Export KeePass CSV...",
                ["MenuSettings"] = "_Settings...",
                ["MenuExit"] = "E_xit",
                ["MenuEdit"] = "_Edit",
                ["MenuEditEntry"] = "_Edit Entry",
                ["MenuDuplicateEntry"] = "_Duplicate Entry",
                ["MenuDeleteEntry"] = "_Delete Entry",
                ["MenuRenameGroup"] = "Rename Group...",
                ["MenuDeleteGroup"] = "Delete Group",
                ["MenuView"] = "_View",
                ["MenuToggleTheme"] = "_Toggle Light / Dark Theme",
                ["MenuTogglePasswords"] = "_Show/Hide Passwords",
                ["MenuRefresh"] = "_Refresh",
                ["MenuTools"] = "_Tools",
                ["MenuPasswordGenerator"] = "_Password Generator...",
                ["MenuAutoType"] = "_Auto-Type Selected Entry",
                ["MenuClearClipboard"] = "_Clear Clipboard Now",
                ["MenuHelp"] = "_Help",
                ["MenuCheckUpdates"] = "_Check for Updates...",
                ["MenuAbout"] = "_About SQLitePass",

                // Toolbar
                ["TbNewEntry"] = "New Entry",
                ["TbEdit"] = "Edit",
                ["TbDelete"] = "Delete",
                ["TbCopyUser"] = "Copy User",
                ["TbCopyPassword"] = "Copy Password",
                ["TbOpenUrl"] = "Open URL",
                ["TbAutoType"] = "Auto-Type",
                ["TbGenerator"] = "Generator",
                ["TbSettings"] = "Settings",
                ["TbLock"] = "Lock",

                // Sidebar
                ["FoldersAndGroups"] = "Folders & Groups",
                ["NewGroup"] = "New Group",
                ["AddSubgroup"] = "Add Subgroup...",

                // DataGrid Columns
                ["ColTitle"] = "Title",
                ["ColUserName"] = "User Name",
                ["ColPassword"] = "Password",
                ["ColUrl"] = "URL",
                ["ColNotes"] = "Notes",
                ["ColUpdatedAt"] = "Last Modified",

                // Preview Panel
                ["PrevUser"] = "User:",
                ["PrevPassword"] = "Password:",
                ["PrevEntropy"] = "Entropy:",
                ["PrevUrl"] = "URL:",
                ["PrevModified"] = "Modified:",
                ["PrevNotes"] = "Notes:",

                // Login Window
                ["LoginTitle"] = "SQLitePass - Unlock Vault",
                ["LoginHeaderSubtitle"] = "Zero-Knowledge Vault • Anti-Corruption with Dual Backup",
                ["LoginDatabaseFile"] = "Vault Database File (*.kpd):",
                ["LoginBackupPresent"] = "✓ .backup file present and synchronized",
                ["LoginRestoreBackup"] = "Restore from .backup",
                ["LoginMasterPassword"] = "Master Password:",
                ["LoginUseKeyFile"] = "Also use a Key File (optional .key)",
                ["LoginNewVault"] = "+ New Vault...",
                ["LoginUnlock"] = "Unlock Vault",

                // Settings Dialog
                ["SettingsTitle"] = "Settings - SQLitePass",
                ["SettingsHeading"] = "⚙️ Application Settings",
                ["SettingsSectionAppearance"] = "🎨 Appearance & Language",
                ["SettingsLanguage"] = "Interface Language:",
                ["SettingsDarkMode"] = "Enable Dark Obsidian Mode",
                ["SettingsShowPasswordDefault"] = "Show passwords unmasked by default",
                ["SettingsSectionSecurity"] = "🔒 Security & Auto-Lock",
                ["SettingsClipboardTimeout"] = "Clear clipboard after:",
                ["SettingsSeconds"] = "seconds",
                ["SettingsAutoLockTimeout"] = "Lock vault on inactivity after:",
                ["SettingsLockOnMinimize"] = "Lock vault when minimized to tray",
                ["SettingsLockOnWindowsLock"] = "Lock vault on Windows workstation lock (Win+L)",
                ["SettingsSectionTray"] = "🖥️ Window & System Tray",
                ["SettingsMinimizeToTray"] = "Minimize to notification area (System Tray)",
                ["SettingsCloseToTray"] = "Close button (X) minimizes to tray instead of exiting",
                ["SettingsAlwaysOnTop"] = "Keep window always on top",
                ["SettingsStartWithWindows"] = "Launch SQLitePass on Windows startup",
                ["SettingsSectionBackup"] = "🛡️ Reliability & Dual Backup",
                ["SettingsDualFileBackup"] = "Create and maintain isolated duplicate .backup file",
                ["SettingsDualFileDesc"] = "The .backup copy is never kept open during use, eliminating corruption risks.",
                ["SettingsSecondaryDir"] = "Optional secondary backup directory (e.g. USB flash drive):",

                // Entry Edit Dialog
                ["EntryEditTitle"] = "Edit Entry - SQLitePass",
                ["EntryNewTitle"] = "New Entry - SQLitePass",
                ["TabCredentials"] = "Credentials",
                ["TabHistory"] = "History",
                ["LblTitle"] = "Title:",
                ["LblUserName"] = "User Name:",
                ["LblPassword"] = "Password:",
                ["LblQuality"] = "Quality:",
                ["BtnGenerate"] = "⚡ Generate",
                ["LblUrl"] = "URL:",
                ["LblExpiration"] = "Expiration:",
                ["ChkEnableExpiry"] = "Enable expiration date",
                ["LblTags"] = "Tags:",
                ["LblNotes"] = "Notes:",

                // Group Edit Dialog
                ["GroupEditTitle"] = "Edit Group",
                ["GroupNewTitle"] = "New Group",
                ["LblGroupName"] = "Group Name:",

                // Password Generator Dialog
                ["GeneratorTitle"] = "Secure Password Generator",
                ["GenLength"] = "Password length:",
                ["GenUpper"] = "Uppercase letters (A-Z)",
                ["GenLower"] = "Lowercase letters (a-z)",
                ["GenDigits"] = "Digits (0-9)",
                ["GenSymbols"] = "Special symbols (!@#$%^&*)",
                ["GenExcludeAmbiguous"] = "Exclude ambiguous characters (0, O, 1, l, I)",
                ["GenResult"] = "Generated Password:",
                ["GenRegenerate"] = "⚡ Regenerate",
                ["GenCopyAndClose"] = "Copy & Use"
            },

            ["fr"] = new Dictionary<string, string>
            {
                // General & Common
                ["AppTitle"] = "SQLitePass",
                ["Ok"] = "OK",
                ["Cancel"] = "Annuler",
                ["Save"] = "Enregistrer",
                ["Exit"] = "Quitter",
                ["Browse"] = "Parcourir...",
                ["Copy"] = "Copier",
                ["Copied"] = "Copié !",
                ["Delete"] = "Supprimer",
                ["Edit"] = "Modifier",
                ["Duplicate"] = "Dupliquer",
                ["Close"] = "Fermer",
                ["SearchPlaceholder"] = "Rechercher des entrées...",
                ["EntriesCount"] = "{0} entrées",
                ["ClipboardClearedIn"] = "Presse-papiers effacé dans {0}s",
                ["VaultStatusDualBackup"] = "Coffre SQLite (.kpd) • Double sauvegarde active",

                // Menu
                ["MenuFile"] = "_Fichier",
                ["MenuAddEntry"] = "_Ajouter une entrée...",
                ["MenuAddGroup"] = "_Ajouter un groupe...",
                ["MenuLockVault"] = "_Verrouiller le coffre",
                ["MenuImportCsv"] = "_Importer CSV KeePass...",
                ["MenuExportCsv"] = "_Exporter CSV KeePass...",
                ["MenuSettings"] = "_Paramètres...",
                ["MenuExit"] = "_Quitter",
                ["MenuEdit"] = "_Édition",
                ["MenuEditEntry"] = "_Modifier l'entrée",
                ["MenuDuplicateEntry"] = "_Dupliquer l'entrée",
                ["MenuDeleteEntry"] = "_Supprimer l'entrée",
                ["MenuRenameGroup"] = "Renommer le groupe...",
                ["MenuDeleteGroup"] = "Supprimer le groupe",
                ["MenuView"] = "_Affichage",
                ["MenuToggleTheme"] = "_Basculer thème Clair / Sombre",
                ["MenuTogglePasswords"] = "_Afficher/Masquer mots de passe",
                ["MenuRefresh"] = "_Actualiser",
                ["MenuTools"] = "_Outils",
                ["MenuPasswordGenerator"] = "_Générateur de mot de passe...",
                ["MenuAutoType"] = "_Saisie automatique sélectionnée",
                ["MenuClearClipboard"] = "_Vider le presse-papiers",
                ["MenuHelp"] = "_Aide",
                ["MenuCheckUpdates"] = "_Vérifier les mises à jour...",
                ["MenuAbout"] = "_À propos de SQLitePass",

                // Toolbar
                ["TbNewEntry"] = "Nouvelle entrée",
                ["TbEdit"] = "Modifier",
                ["TbDelete"] = "Supprimer",
                ["TbCopyUser"] = "Copier nom",
                ["TbCopyPassword"] = "Copier mot de passe",
                ["TbOpenUrl"] = "Ouvrir l'URL",
                ["TbAutoType"] = "Auto-Type",
                ["TbGenerator"] = "Générateur",
                ["TbSettings"] = "Options",
                ["TbLock"] = "Verrouiller",

                // Sidebar
                ["FoldersAndGroups"] = "Dossiers & Groupes",
                ["NewGroup"] = "Nouveau groupe",
                ["AddSubgroup"] = "Ajouter un sous-groupe...",

                // DataGrid Columns
                ["ColTitle"] = "Titre",
                ["ColUserName"] = "Nom d'utilisateur",
                ["ColPassword"] = "Mot de passe",
                ["ColUrl"] = "URL",
                ["ColNotes"] = "Notes",
                ["ColUpdatedAt"] = "Dernière modification",

                // Preview Panel
                ["PrevUser"] = "Utilisateur :",
                ["PrevPassword"] = "Mot de passe :",
                ["PrevEntropy"] = "Entropie :",
                ["PrevUrl"] = "URL :",
                ["PrevModified"] = "Modifié :",
                ["PrevNotes"] = "Notes :",

                // Login Window
                ["LoginTitle"] = "SQLitePass - Déverrouiller le coffre",
                ["LoginHeaderSubtitle"] = "Coffre Zero-Knowledge • Anti-Corruption avec Double Sauvegarde",
                ["LoginDatabaseFile"] = "Fichier base de données (*.kpd) :",
                ["LoginBackupPresent"] = "✓ Fichier .backup présent et synchronisé",
                ["LoginRestoreBackup"] = "Restaurer depuis .backup",
                ["LoginMasterPassword"] = "Mot de passe maître :",
                ["LoginUseKeyFile"] = "Utiliser aussi un fichier clé (.key facultatif)",
                ["LoginNewVault"] = "+ Nouveau coffre...",
                ["LoginUnlock"] = "Déverrouiller le coffre",

                // Settings Dialog
                ["SettingsTitle"] = "Paramètres - SQLitePass",
                ["SettingsHeading"] = "⚙️ Paramètres du programme",
                ["SettingsSectionAppearance"] = "🎨 Apparence & Langue",
                ["SettingsLanguage"] = "Langue de l'interface :",
                ["SettingsDarkMode"] = "Activer le mode sombre (Dark Obsidian)",
                ["SettingsShowPasswordDefault"] = "Afficher les mots de passe en clair à l'ouverture",
                ["SettingsSectionSecurity"] = "🔒 Sécurité & Verrouillage automatique",
                ["SettingsClipboardTimeout"] = "Effacer le presse-papiers après :",
                ["SettingsSeconds"] = "secondes",
                ["SettingsAutoLockTimeout"] = "Verrouiller le coffre après inactivité :",
                ["SettingsLockOnMinimize"] = "Verrouiller lors de la réduction en barre d'état",
                ["SettingsLockOnWindowsLock"] = "Verrouiller lors du verrouillage Windows (Win+L)",
                ["SettingsSectionTray"] = "🖥️ Fenêtre & Barre des tâches",
                ["SettingsMinimizeToTray"] = "Réduire dans la zone de notification (Tray)",
                ["SettingsCloseToTray"] = "Le bouton de fermeture (X) réduit dans le tray",
                ["SettingsAlwaysOnTop"] = "Garder la fenêtre toujours au premier plan",
                ["SettingsStartWithWindows"] = "Lancer SQLitePass au démarrage de Windows",
                ["SettingsSectionBackup"] = "🛡️ Fiabilité & Double Sauvegarde",
                ["SettingsDualFileBackup"] = "Créer et maintenir un fichier .backup dupliqué et isolé",
                ["SettingsDualFileDesc"] = "Le fichier .backup n'est jamais laissé ouvert en cours d'utilisation.",
                ["SettingsSecondaryDir"] = "Dossier de sauvegarde secondaire facultatif (ex. clé USB) :",

                // Entry Edit Dialog
                ["EntryEditTitle"] = "Modifier l'entrée - SQLitePass",
                ["EntryNewTitle"] = "Nouvelle entrée - SQLitePass",
                ["TabCredentials"] = "Identifiants",
                ["TabHistory"] = "Historique",
                ["LblTitle"] = "Titre :",
                ["LblUserName"] = "Nom d'utilisateur :",
                ["LblPassword"] = "Mot de passe :",
                ["LblQuality"] = "Qualité :",
                ["BtnGenerate"] = "⚡ Générer",
                ["LblUrl"] = "URL :",
                ["LblExpiration"] = "Expiration :",
                ["ChkEnableExpiry"] = "Activer la date d'expiration",
                ["LblTags"] = "Tags :",
                ["LblNotes"] = "Notes :",

                // Group Edit Dialog
                ["GroupEditTitle"] = "Modifier le groupe",
                ["GroupNewTitle"] = "Nouveau groupe",
                ["LblGroupName"] = "Nom du groupe :",

                // Password Generator Dialog
                ["GeneratorTitle"] = "Générateur de mot de passe sécurisé",
                ["GenLength"] = "Longueur du mot de passe :",
                ["GenUpper"] = "Majuscules (A-Z)",
                ["GenLower"] = "Minuscules (a-z)",
                ["GenDigits"] = "Chiffres (0-9)",
                ["GenSymbols"] = "Symboles spéciaux (!@#$%^&*)",
                ["GenExcludeAmbiguous"] = "Exclure les caractères ambigus (0, O, 1, l, I)",
                ["GenResult"] = "Mot de passe généré :",
                ["GenRegenerate"] = "⚡ Régénérer",
                ["GenCopyAndClose"] = "Copier et utiliser"
            },

            ["de"] = new Dictionary<string, string>
            {
                // General & Common
                ["AppTitle"] = "SQLitePass",
                ["Ok"] = "OK",
                ["Cancel"] = "Abbrechen",
                ["Save"] = "Speichern",
                ["Exit"] = "Beenden",
                ["Browse"] = "Durchsuchen...",
                ["Copy"] = "Kopieren",
                ["Copied"] = "Kopiert!",
                ["Delete"] = "Löschen",
                ["Edit"] = "Bearbeiten",
                ["Duplicate"] = "Duplizieren",
                ["Close"] = "Schließen",
                ["SearchPlaceholder"] = "Einträge durchsuchen...",
                ["EntriesCount"] = "{0} Einträge",
                ["ClipboardClearedIn"] = "Zwischenablage wird in {0}s geleert",
                ["VaultStatusDualBackup"] = "SQLite-Tresor (.kpd) • Dual-Backup aktiv",

                // Menu
                ["MenuFile"] = "_Datei",
                ["MenuAddEntry"] = "_Eintrag hinzufügen...",
                ["MenuAddGroup"] = "_Gruppe hinzufügen...",
                ["MenuLockVault"] = "_Tresor sperren",
                ["MenuImportCsv"] = "_KeePass CSV importieren...",
                ["MenuExportCsv"] = "_KeePass CSV exportieren...",
                ["MenuSettings"] = "_Einstellungen...",
                ["MenuExit"] = "_Beenden",
                ["MenuEdit"] = "_Bearbeiten",
                ["MenuEditEntry"] = "_Eintrag bearbeiten",
                ["MenuDuplicateEntry"] = "_Eintrag duplizieren",
                ["MenuDeleteEntry"] = "_Eintrag löschen",
                ["MenuRenameGroup"] = "Gruppe umbenennen...",
                ["MenuDeleteGroup"] = "Gruppe löschen",
                ["MenuView"] = "_Ansicht",
                ["MenuToggleTheme"] = "_Helles / Dunkles Design umschalten",
                ["MenuTogglePasswords"] = "_Passwörter anzeigen/verbergen",
                ["MenuRefresh"] = "_Aktualisieren",
                ["MenuTools"] = "_Werkzeuge",
                ["MenuPasswordGenerator"] = "_Passwort-Generator...",
                ["MenuAutoType"] = "_Auto-Type für Auswahl ausführen",
                ["MenuClearClipboard"] = "_Zwischenablage sofort leeren",
                ["MenuHelp"] = "_Hilfe",
                ["MenuCheckUpdates"] = "_Nach Updates suchen...",
                ["MenuAbout"] = "_Über SQLitePass",

                // Toolbar
                ["TbNewEntry"] = "Neuer Eintrag",
                ["TbEdit"] = "Bearbeiten",
                ["TbDelete"] = "Löschen",
                ["TbCopyUser"] = "Benutzer kopieren",
                ["TbCopyPassword"] = "Passwort kopieren",
                ["TbOpenUrl"] = "URL öffnen",
                ["TbAutoType"] = "Auto-Type",
                ["TbGenerator"] = "Generator",
                ["TbSettings"] = "Optionen",
                ["TbLock"] = "Sperren",

                // Sidebar
                ["FoldersAndGroups"] = "Ordner & Gruppen",
                ["NewGroup"] = "Neue Gruppe",
                ["AddSubgroup"] = "Untergruppe hinzufügen...",

                // DataGrid Columns
                ["ColTitle"] = "Titel",
                ["ColUserName"] = "Benutzername",
                ["ColPassword"] = "Passwort",
                ["ColUrl"] = "URL",
                ["ColNotes"] = "Notizen",
                ["ColUpdatedAt"] = "Zuletzt geändert",

                // Preview Panel
                ["PrevUser"] = "Benutzer:",
                ["PrevPassword"] = "Passwort:",
                ["PrevEntropy"] = "Entropie:",
                ["PrevUrl"] = "URL:",
                ["PrevModified"] = "Geändert:",
                ["PrevNotes"] = "Notizen:",

                // Login Window
                ["LoginTitle"] = "SQLitePass - Tresor entsperren",
                ["LoginHeaderSubtitle"] = "Zero-Knowledge-Tresor • Anti-Korruption mit Dual-Backup",
                ["LoginDatabaseFile"] = "Tresor-Datenbankdatei (*.kpd):",
                ["LoginBackupPresent"] = "✓ .backup-Datei vorhanden und synchronisiert",
                ["LoginRestoreBackup"] = "Aus .backup wiederherstellen",
                ["LoginMasterPassword"] = "Master-Passwort:",
                ["LoginUseKeyFile"] = "Zusätzlich Schlüsseldatei verwenden (optional .key)",
                ["LoginNewVault"] = "+ Neuer Tresor...",
                ["LoginUnlock"] = "Tresor entsperren",

                // Settings Dialog
                ["SettingsTitle"] = "Einstellungen - SQLitePass",
                ["SettingsHeading"] = "⚙️ Programmeinstellungen",
                ["SettingsSectionAppearance"] = "🎨 Erscheinungsbild & Sprache",
                ["SettingsLanguage"] = "Oberflächensprache:",
                ["SettingsDarkMode"] = "Dunkles Design aktivieren (Dark Obsidian Mode)",
                ["SettingsShowPasswordDefault"] = "Passwörter beim Start im Klartext anzeigen",
                ["SettingsSectionSecurity"] = "🔒 Sicherheit & Automatische Sperre",
                ["SettingsClipboardTimeout"] = "Zwischenablage leeren nach:",
                ["SettingsSeconds"] = "Sekunden",
                ["SettingsAutoLockTimeout"] = "Tresor bei Inaktivität sperren nach:",
                ["SettingsLockOnMinimize"] = "Tresor beim Minimieren in den Infobereich sperren",
                ["SettingsLockOnWindowsLock"] = "Tresor sperren bei Windows-Sitzungssperre (Win+L)",
                ["SettingsSectionTray"] = "🖥️ Fenster & Infobereich (System Tray)",
                ["SettingsMinimizeToTray"] = "In den Infobereich minimieren (System Tray)",
                ["SettingsCloseToTray"] = "Schließen-Schaltfläche (X) minimiert in den Infobereich",
                ["SettingsAlwaysOnTop"] = "Fenster immer im Vordergrund halten",
                ["SettingsStartWithWindows"] = "SQLitePass mit Windows starten",
                ["SettingsSectionBackup"] = "🛡️ Zuverlässigkeit & Dual-Backup",
                ["SettingsDualFileBackup"] = "Isolierte Duplikat-.backup-Datei erstellen und aktualisieren",
                ["SettingsDualFileDesc"] = "Die .backup-Datei bleibt im laufenden Betrieb geschlossen.",
                ["SettingsSecondaryDir"] = "Optionaler sekundärer Backup-Ordner (z. B. USB-Stick):",

                // Entry Edit Dialog
                ["EntryEditTitle"] = "Eintrag bearbeiten - SQLitePass",
                ["EntryNewTitle"] = "Neuer Eintrag - SQLitePass",
                ["TabCredentials"] = "Anmeldedaten",
                ["TabHistory"] = "Verlauf",
                ["LblTitle"] = "Titel:",
                ["LblUserName"] = "Benutzername:",
                ["LblPassword"] = "Passwort:",
                ["LblQuality"] = "Qualität:",
                ["BtnGenerate"] = "⚡ Generieren",
                ["LblUrl"] = "URL:",
                ["LblExpiration"] = "Ablaufdatum:",
                ["ChkEnableExpiry"] = "Ablaufdatum aktivieren",
                ["LblTags"] = "Tags:",
                ["LblNotes"] = "Notizen:",

                // Group Edit Dialog
                ["GroupEditTitle"] = "Gruppe bearbeiten",
                ["GroupNewTitle"] = "Neue Gruppe",
                ["LblGroupName"] = "Gruppenname:",

                // Password Generator Dialog
                ["GeneratorTitle"] = "Sicherer Passwort-Generator",
                ["GenLength"] = "Passwortlänge:",
                ["GenUpper"] = "Großbuchstaben (A-Z)",
                ["GenLower"] = "Kleinbuchstaben (a-z)",
                ["GenDigits"] = "Ziffern (0-9)",
                ["GenSymbols"] = "Sonderzeichen (!@#$%^&*)",
                ["GenExcludeAmbiguous"] = "Mehrdeutige Zeichen ausschließen (0, O, 1, l, I)",
                ["GenResult"] = "Generiertes Passwort:",
                ["GenRegenerate"] = "⚡ Neu generieren",
                ["GenCopyAndClose"] = "Kopieren & Verwenden"
            },

            ["es"] = new Dictionary<string, string>
            {
                // General & Common
                ["AppTitle"] = "SQLitePass",
                ["Ok"] = "Aceptar",
                ["Cancel"] = "Cancelar",
                ["Save"] = "Guardar",
                ["Exit"] = "Salir",
                ["Browse"] = "Examinar...",
                ["Copy"] = "Copiar",
                ["Copied"] = "¡Copiado!",
                ["Delete"] = "Eliminar",
                ["Edit"] = "Modificar",
                ["Duplicate"] = "Duplicar",
                ["Close"] = "Cerrar",
                ["SearchPlaceholder"] = "Buscar en las entradas...",
                ["EntriesCount"] = "{0} entradas",
                ["ClipboardClearedIn"] = "Portapapeles borrado en {0}s",
                ["VaultStatusDualBackup"] = "Bóveda SQLite (.kpd) • Copia de seguridad dual activa",

                // Menu
                ["MenuFile"] = "_Archivo",
                ["MenuAddEntry"] = "_Añadir entrada...",
                ["MenuAddGroup"] = "_Añadir grupo...",
                ["MenuLockVault"] = "_Bloquear bóveda",
                ["MenuImportCsv"] = "_Importar CSV KeePass...",
                ["MenuExportCsv"] = "_Exportar CSV KeePass...",
                ["MenuSettings"] = "_Configuración...",
                ["MenuExit"] = "_Salir",
                ["MenuEdit"] = "_Edición",
                ["MenuEditEntry"] = "_Modificar entrada",
                ["MenuDuplicateEntry"] = "_Duplicar entrada",
                ["MenuDeleteEntry"] = "_Eliminar entrada",
                ["MenuRenameGroup"] = "Renombrar grupo...",
                ["MenuDeleteGroup"] = "Eliminar grupo",
                ["MenuView"] = "_Ver",
                ["MenuToggleTheme"] = "_Alternar tema Claro / Oscuro",
                ["MenuTogglePasswords"] = "_Mostrar/Ocultar contraseñas",
                ["MenuRefresh"] = "_Actualizar",
                ["MenuTools"] = "_Herramientas",
                ["MenuPasswordGenerator"] = "_Generador de contraseñas...",
                ["MenuAutoType"] = "_Auto-Type para entrada seleccionada",
                ["MenuClearClipboard"] = "_Borrar portapapeles ahora",
                ["MenuHelp"] = "_Ayuda",
                ["MenuCheckUpdates"] = "_Buscar actualizaciones...",
                ["MenuAbout"] = "_Acerca de SQLitePass",

                // Toolbar
                ["TbNewEntry"] = "Nueva entrada",
                ["TbEdit"] = "Modificar",
                ["TbDelete"] = "Eliminar",
                ["TbCopyUser"] = "Copiar usuario",
                ["TbCopyPassword"] = "Copiar contraseña",
                ["TbOpenUrl"] = "Abrir URL",
                ["TbAutoType"] = "Auto-Type",
                ["TbGenerator"] = "Generador",
                ["TbSettings"] = "Opciones",
                ["TbLock"] = "Bloquear",

                // Sidebar
                ["FoldersAndGroups"] = "Carpetas y Grupos",
                ["NewGroup"] = "Nuevo grupo",
                ["AddSubgroup"] = "Añadir subgrupo...",

                // DataGrid Columns
                ["ColTitle"] = "Título",
                ["ColUserName"] = "Nombre de usuario",
                ["ColPassword"] = "Contraseña",
                ["ColUrl"] = "URL",
                ["ColNotes"] = "Notas",
                ["ColUpdatedAt"] = "Última modificación",

                // Preview Panel
                ["PrevUser"] = "Usuario:",
                ["PrevPassword"] = "Contraseña:",
                ["PrevEntropy"] = "Entropía:",
                ["PrevUrl"] = "URL:",
                ["PrevModified"] = "Modificado:",
                ["PrevNotes"] = "Notas:",

                // Login Window
                ["LoginTitle"] = "SQLitePass - Desbloquear bóveda",
                ["LoginHeaderSubtitle"] = "Bóveda Zero-Knowledge • Anti-Corrupción con Copia Dual",
                ["LoginDatabaseFile"] = "Archivo de base de datos (*.kpd):",
                ["LoginBackupPresent"] = "✓ Archivo .backup presente y sincronizado",
                ["LoginRestoreBackup"] = "Restaurar desde .backup",
                ["LoginMasterPassword"] = "Contraseña maestra:",
                ["LoginUseKeyFile"] = "Usar también archivo de clave (.key opcional)",
                ["LoginNewVault"] = "+ Nueva bóveda...",
                ["LoginUnlock"] = "Desbloquear bóveda",

                // Settings Dialog
                ["SettingsTitle"] = "Configuración - SQLitePass",
                ["SettingsHeading"] = "⚙️ Configuración del programa",
                ["SettingsSectionAppearance"] = "🎨 Apariencia e Idioma",
                ["SettingsLanguage"] = "Idioma de la interfaz:",
                ["SettingsDarkMode"] = "Activar modo oscuro (Dark Obsidian)",
                ["SettingsShowPasswordDefault"] = "Mostrar contraseñas en texto claro al abrir",
                ["SettingsSectionSecurity"] = "🔒 Seguridad y Bloqueo automático",
                ["SettingsClipboardTimeout"] = "Borrar portapapeles después de:",
                ["SettingsSeconds"] = "segundos",
                ["SettingsAutoLockTimeout"] = "Bloquear bóveda por inactividad tras:",
                ["SettingsLockOnMinimize"] = "Bloquear automáticamente al minimizar",
                ["SettingsLockOnWindowsLock"] = "Bloquear al bloquear la sesión de Windows (Win+L)",
                ["SettingsSectionTray"] = "🖥️ Ventana y Área de notificación (Tray)",
                ["SettingsMinimizeToTray"] = "Minimizar en el área de notificación (System Tray)",
                ["SettingsCloseToTray"] = "El botón de cerrar (X) minimiza en lugar de salir",
                ["SettingsAlwaysOnTop"] = "Mantener ventana siempre en primer plano",
                ["SettingsStartWithWindows"] = "Iniciar SQLitePass al iniciar Windows",
                ["SettingsSectionBackup"] = "🛡️ Fiabilidad y Copia de seguridad dual",
                ["SettingsDualFileBackup"] = "Crear y mantener archivo .backup duplicado y aislado",
                ["SettingsDualFileDesc"] = "El archivo .backup nunca se mantiene abierto durante el trabajo.",
                ["SettingsSecondaryDir"] = "Carpeta de copia secundaria opcional (ej. memoria USB):",

                // Entry Edit Dialog
                ["EntryEditTitle"] = "Modificar entrada - SQLitePass",
                ["EntryNewTitle"] = "Nueva entrada - SQLitePass",
                ["TabCredentials"] = "Credenciales",
                ["TabHistory"] = "Historial",
                ["LblTitle"] = "Título:",
                ["LblUserName"] = "Nombre de usuario:",
                ["LblPassword"] = "Contraseña:",
                ["LblQuality"] = "Calidad:",
                ["BtnGenerate"] = "⚡ Generar",
                ["LblUrl"] = "URL:",
                ["LblExpiration"] = "Caducidad:",
                ["ChkEnableExpiry"] = "Habilitar fecha de caducidad",
                ["LblTags"] = "Etiquetas:",
                ["LblNotes"] = "Notas:",

                // Group Edit Dialog
                ["GroupEditTitle"] = "Modificar grupo",
                ["GroupNewTitle"] = "Nuevo grupo",
                ["LblGroupName"] = "Nombre del grupo:",

                // Password Generator Dialog
                ["GeneratorTitle"] = "Generador de contraseñas seguras",
                ["GenLength"] = "Longitud de la contraseña:",
                ["GenUpper"] = "Letras mayúsculas (A-Z)",
                ["GenLower"] = "Letras minúsculas (a-z)",
                ["GenDigits"] = "Números (0-9)",
                ["GenSymbols"] = "Símbolos especiales (!@#$%^&*)",
                ["GenExcludeAmbiguous"] = "Excluir caracteres ambiguos (0, O, 1, l, I)",
                ["GenResult"] = "Contraseña generada:",
                ["GenRegenerate"] = "⚡ Regenerar",
                ["GenCopyAndClose"] = "Copiar y usar"
            }
        };

        public static void ApplyLanguage(string? langCode)
        {
            if (string.IsNullOrWhiteSpace(langCode) || !Translations.ContainsKey(langCode.ToLowerInvariant()))
            {
                // Auto-detect from system culture if not explicitly set
                string sysLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                langCode = Translations.ContainsKey(sysLang) ? sysLang : "en";
            }

            CurrentLanguage = langCode.ToLowerInvariant();
            var dict = Translations[CurrentLanguage];

            var appRes = Application.Current?.Resources;
            if (appRes != null)
            {
                foreach (var (key, value) in dict)
                {
                    appRes[key] = value;
                }
            }
        }

        public static string GetString(string key, params object[] args)
        {
            if (Translations.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(key, out var val))
            {
                return args.Length > 0 ? string.Format(val, args) : val;
            }

            if (Translations["en"].TryGetValue(key, out var fallback))
            {
                return args.Length > 0 ? string.Format(fallback, args) : fallback;
            }

            return key;
        }
    }
}
