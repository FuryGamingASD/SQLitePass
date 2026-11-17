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
            return group.IsRecycleBin;
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
            var parent = rootGroups.FirstOrDefault(g => g.IsRoot) ?? rootGroups.FirstOrDefault();
            var newTrash = new PwGroup
            {
                Id = Guid.NewGuid(),
                ParentId = parent?.Id,
                Name = LocalizationManager.GetString("RecycleBin"),
                IconIndex = RecycleBinIconIndex,
                IsRecycleBin = true,
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
                long revision = await storage.MoveEntryAsync(entry.Id, entry.Revision, trashGroup.Id, crypto);
                entry.GroupId = trashGroup.Id; entry.Revision = revision;
                AppLogger.LogInfo("Entry moved to recycle bin.");
                return (movedToTrash: true, permanentlyDeleted: false);
            }

            // Entry is already in Trash -> Permanent Delete with confirmation
            var confirm = MessageBox.Show(
                ownerWindow,
                LocalizationManager.GetString("TrashPermanentConfirm"),
                LocalizationManager.GetString("MenuDeleteEntry"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (confirm == MessageBoxResult.Yes)
            {
                await storage.DeleteEntryAsync(entry.Id, entry.Revision);
                AppLogger.LogInfo("Entry permanently deleted from recycle bin.");
                return (movedToTrash: false, permanentlyDeleted: true);
            }

            return (movedToTrash: false, permanentlyDeleted: false);
        }

        public static async Task<int> EmptyRecycleBinAsync(
            SqliteStorageProvider storage,
            CryptoEngine crypto,
            IList<PwGroup> rootGroups,
            Window ownerWindow)
        {
            var trashGroup = await GetOrCreateRecycleBinGroupAsync(storage, crypto, rootGroups);
            var ids = await storage.LoadEntryRevisionsAsync(trashGroup.Id, crypto);

            if (ids.Count == 0)
            {
                MessageBox.Show(ownerWindow, LocalizationManager.GetString("TrashEmpty"), LocalizationManager.GetString("RecycleBin"), MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            var confirm = MessageBox.Show(
                ownerWindow,
                LocalizationManager.GetString("TrashEmptyConfirm", ids.Count),
                LocalizationManager.GetString("MenuEmptyTrash"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (confirm != MessageBoxResult.Yes) return 0;

            await storage.DeleteEntriesBatchAsync(ids);

            AppLogger.LogInfo($"Emptied recycle bin: {ids.Count} entries permanently deleted.");
            return ids.Count;
        }
    }
}
