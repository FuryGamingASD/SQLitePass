using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services
{
    /// <summary>
    /// Encapsulates vault-level business operations including Soft-Delete / Recycle Bin management,
    /// batch operations, and bulk import handlers.
    /// </summary>
    public static class VaultOperationHandler
    {
        public const int RecycleBinIconIndex = 43;

        public static bool IsRecycleBinGroup(PwGroup? group)
        {
            if (group == null) return false;
            return group.IconIndex == RecycleBinIconIndex ||
                   string.Equals(group.Name, "Cestino", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(group.Name, "Trash", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(group.Name, "Corbeille", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(group.Name, "Papierkorb", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(group.Name, "Papelera", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<PwGroup> GetOrCreateRecycleBinGroupAsync(
            SqliteStorageProvider storage,
            CryptoEngine crypto,
            IList<PwGroup> rootGroups)
        {
            // Search existing groups recursively
            PwGroup? found = FindRecycleBinRecursive(rootGroups);
            if (found != null) return found;

            // Create new Recycle Bin group under the first root group
            var parent = rootGroups.FirstOrDefault();
            var newTrash = new PwGroup
            {
                Id = Guid.NewGuid(),
                ParentId = parent?.Id,
                Name = "Cestino",
                IconIndex = RecycleBinIconIndex,
                IsExpanded = true
            };

            await storage.SaveGroupAsync(newTrash, crypto);
            if (parent != null)
            {
                parent.Children.Add(newTrash);
                parent.IsExpanded = true;
            }
            else
            {
                rootGroups.Add(newTrash);
            }

            AppLogger.LogInfo("Recycle Bin group initialized in vault.");
            return newTrash;
        }

        private static PwGroup? FindRecycleBinRecursive(IEnumerable<PwGroup> groups)
        {
            foreach (var g in groups)
            {
                if (IsRecycleBinGroup(g)) return g;
                var inChild = FindRecycleBinRecursive(g.Children);
                if (inChild != null) return inChild;
            }
            return null;
        }

        public static async Task<(bool movedToTrash, bool permanentlyDeleted)> DeleteOrTrashEntryAsync(
            PwEntry entry,
            SqliteStorageProvider storage,
            CryptoEngine crypto,
            IList<PwGroup> rootGroups,
            Window ownerWindow)
        {
            var trashGroup = await GetOrCreateRecycleBinGroupAsync(storage, crypto, rootGroups);

            // If entry is not yet in trash -> Soft Delete (move to Trash)
            if (entry.GroupId != trashGroup.Id)
            {
                entry.GroupId = trashGroup.Id;
                await storage.SaveEntryAsync(entry, crypto);
                AppLogger.LogInfo($"Entry '{entry.Title}' ({entry.Id}) soft-deleted to Recycle Bin.");
                return (movedToTrash: true, permanentlyDeleted: false);
            }

            // Entry is already in Trash -> Permanent Delete with confirmation
            var confirm = MessageBox.Show(
                ownerWindow,
                $"Eliminare definitivamente la voce '{entry.Title}' dal Cestino?\n\nQuesta operazione non può essere annullata.",
                "Conferma Eliminazione Definitiva",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (confirm == MessageBoxResult.Yes)
            {
                await storage.DeleteEntryAsync(entry.Id);
                AppLogger.LogInfo($"Entry '{entry.Title}' ({entry.Id}) permanently deleted from Recycle Bin.");
                return (movedToTrash: false, permanentlyDeleted: true);
            }

            return (movedToTrash: false, permanentlyDeleted: false);
        }

        public static async Task<int> EmptyRecycleBinAsync(
            SqliteStorageProvider storage,
            CryptoEngine crypto,
            IList<PwGroup> rootGroups,
            ICollection<PwEntry> allEntries,
            Window ownerWindow)
        {
            var trashGroup = await GetOrCreateRecycleBinGroupAsync(storage, crypto, rootGroups);
            var trashEntries = allEntries.Where(e => e.GroupId == trashGroup.Id).ToList();

            if (trashEntries.Count == 0)
            {
                MessageBox.Show(ownerWindow, "Il Cestino è già vuoto.", "Cestino Vuoto", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            var confirm = MessageBox.Show(
                ownerWindow,
                $"Sei sicuro di voler eliminare definitivamente tutte le {trashEntries.Count} voci presenti nel Cestino?\n\nI dati non potranno più essere recuperati.",
                "Svuota Cestino",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (confirm != MessageBoxResult.Yes) return 0;

            var ids = trashEntries.Select(e => e.Id).ToList();
            await storage.DeleteEntriesBatchAsync(ids);

            foreach (var entry in trashEntries)
            {
                allEntries.Remove(entry);
            }

            AppLogger.LogInfo($"Emptied recycle bin: {ids.Count} entries permanently deleted.");
            return ids.Count;
        }
    }
}
