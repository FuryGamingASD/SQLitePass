
            CREATE TABLE IF NOT EXISTS kp_vault_metadata (
                vault_id TEXT PRIMARY KEY,
                vault_name TEXT NOT NULL,
                salt_base64 TEXT NOT NULL,
                kdf_iterations INTEGER NOT NULL DEFAULT 3,
                kdf_memory_kb INTEGER NOT NULL DEFAULT 65536,
                kdf_parallelism INTEGER NOT NULL DEFAULT 2,
                verification_canary TEXT NOT NULL,
                encrypt_username_url INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS kp_groups (
                id TEXT PRIMARY KEY,
                parent_id TEXT NULL,
                name TEXT NOT NULL,
                icon_index INTEGER NOT NULL DEFAULT 48,
                is_expanded INTEGER NOT NULL DEFAULT 1,
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS kp_entries (
                id TEXT PRIMARY KEY,
                group_id TEXT NOT NULL,
                title TEXT NOT NULL,
                username TEXT NOT NULL,
                password_enc TEXT NOT NULL,
                url TEXT NOT NULL,
                notes_enc TEXT NOT NULL,
                tags TEXT NULL,
                icon_index INTEGER NOT NULL DEFAULT 0,
                expiry_date TEXT NULL,
                autotype_enabled INTEGER NOT NULL DEFAULT 1,
                autotype_seq TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_access_at TEXT NOT NULL,
                FOREIGN KEY (group_id) REFERENCES kp_groups(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS kp_custom_fields (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                field_key TEXT NOT NULL,
                field_val_enc TEXT NOT NULL,
                is_protected INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (entry_id) REFERENCES kp_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS kp_attachments (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                filename TEXT NOT NULL,
                filesize INTEGER NOT NULL,
                data_enc BLOB NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (entry_id) REFERENCES kp_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS kp_history (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                title TEXT NOT NULL,
                username TEXT NOT NULL,
                password_enc TEXT NOT NULL,
                url TEXT NOT NULL,
                notes_enc TEXT NOT NULL,
                saved_at TEXT NOT NULL,
                FOREIGN KEY (entry_id) REFERENCES kp_entries(id) ON DELETE CASCADE
            );

            -- High-Performance Indexes for Foreign Keys & Hierarchies
            CREATE INDEX IF NOT EXISTS idx_entries_group_id ON kp_entries(group_id);
            CREATE INDEX IF NOT EXISTS idx_groups_parent_id ON kp_groups(parent_id);
            CREATE INDEX IF NOT EXISTS idx_custom_fields_entry_id ON kp_custom_fields(entry_id);
            CREATE INDEX IF NOT EXISTS idx_attachments_entry_id ON kp_attachments(entry_id);
            CREATE INDEX IF NOT EXISTS idx_history_entry_id ON kp_history(entry_id);
