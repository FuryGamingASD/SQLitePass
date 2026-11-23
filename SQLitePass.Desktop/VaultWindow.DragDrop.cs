using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private sealed record WorkspaceDrag(VaultSession Session, object Row, PointerPressedEventArgs Press, Point Origin);
    private enum DropPlacement { Inside, Before, After }
    private sealed record FolderDrop(WorkspaceDrag Drag, PwGroup Target, DropPlacement Placement);

    private static readonly DataFormat<WorkspaceDrag> WorkspaceDragFormat =
        DataFormat.CreateInProcessFormat<WorkspaceDrag>("sqlitepass.workspace-drag");

    private WorkspaceDrag? _pendingWorkspaceDrag;
    private WorkspaceDrag? _activeWorkspaceDrag;

    private void InitializeWorkspaceDrag()
    {
        AddHandler(PointerMovedEvent, WorkspacePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, WorkspacePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void EntryRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: EntryRow row }) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) Entries.SelectedItem = row;
        if (!_query.OnlyRecycleBin) PrepareWorkspaceDrag(row, e);
    }

    private void GroupRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GroupChoice choice }) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) Groups.SelectedItem = choice;
        if (choice.Folder is { IsRoot: false, IsRecycleBin: false } group) PrepareWorkspaceDrag(group, e);
    }

    private void PrepareWorkspaceDrag(object row, PointerPressedEventArgs e)
    {
        _pendingWorkspaceDrag = null;
        var point = e.GetCurrentPoint(this);
        if (e.ClickCount == 1 && point.Properties.IsLeftButtonPressed &&
            _session is { Storage.IsReadOnly: false } session && _operation == null)
            _pendingWorkspaceDrag = new WorkspaceDrag(session, row, e, point.Position);
    }

    private async void WorkspacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingWorkspaceDrag is not { } drag || e.Pointer != drag.Press.Pointer) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            await FinishWorkspacePressAsync(drag);
            return;
        }
        var offset = point.Position - drag.Origin;
        if (Math.Abs(offset.X) < 6 && Math.Abs(offset.Y) < 6) return;
        _pendingWorkspaceDrag = null;
        if (_operation != null || !ReferenceEquals(_session, drag.Session)) return;
        _activeWorkspaceDrag = drag;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(WorkspaceDragFormat, drag));
            await DragDrop.DoDragDropAsync(drag.Press, transfer, DragDropEffects.Move);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            AppLogger.LogError("Could not start a workspace drag", error);
            if (_session != null) StatusText.Text = Text("ErrorUnexpected");
        }
        finally { _activeWorkspaceDrag = null; }
        await BrowsePressedFolderAsync(drag);
    }

    private async void WorkspacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pendingWorkspaceDrag is { } drag && e.Pointer == drag.Press.Pointer && e.InitialPressMouseButton == MouseButton.Left)
            await FinishWorkspacePressAsync(drag);
    }

    private Task FinishWorkspacePressAsync(WorkspaceDrag drag)
    {
        _pendingWorkspaceDrag = null;
        return BrowsePressedFolderAsync(drag);
    }

    private Task BrowsePressedFolderAsync(WorkspaceDrag drag) =>
        ReferenceEquals(_session, drag.Session) && drag.Row is PwGroup folder &&
        Groups.SelectedItem is GroupChoice selected && selected.Id == folder.Id && _query.GroupId != folder.Id
            ? SelectGroupAsync(selected)
            : Task.CompletedTask;

    private FolderDrop? DropForTarget(object? sender, DragEventArgs e)
    {
        if (sender is not Control source || e.DataTransfer.TryGetValue(WorkspaceDragFormat) is not { } drag ||
            !ReferenceEquals(_activeWorkspaceDrag, drag) || !ReferenceEquals(_session, drag.Session) || _operation != null)
            return null;
        var target = (source.DataContext as GroupChoice)?.Folder;
        if (target == null && drag.Row is PwGroup)
            target = FolderChoices.FirstOrDefault(choice => choice.Folder?.IsRoot == true)?.Folder;
        if (target == null) return null;
        var placement = DropPlacement.Inside;
        if (drag.Row is PwGroup group)
        {
            if (target.IsRecycleBin) return null;
            for (PwGroup? parent = target; parent != null; parent = FolderChoices.FirstOrDefault(choice => choice.Id == parent.ParentId)?.Folder)
                if (group.Id == parent.Id) return null;
            if (!target.IsRoot && source.Bounds.Height > 0)
            {
                double position = e.GetPosition(source).Y / source.Bounds.Height;
                if (position < 0.28) placement = DropPlacement.Before;
                else if (position > 0.72) placement = DropPlacement.After;
            }
        }
        return new FolderDrop(drag, target, placement);
    }

    private void GroupRow_DragOver(object? sender, DragEventArgs e)
    {
        var drop = DropForTarget(sender, e);
        e.DragEffects = drop != null ? DragDropEffects.Move : DragDropEffects.None;
        if (sender is Control row && row != Groups)
        {
            ClearDropIndicator(row);
            if (drop != null) row.Classes.Add("drop-" + drop.Placement.ToString().ToLowerInvariant());
        }
        e.Handled = true;
    }

    private void GroupRow_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control row) ClearDropIndicator(row);
    }

    private static void ClearDropIndicator(Control row)
    {
        row.Classes.Remove("drop-inside");
        row.Classes.Remove("drop-before");
        row.Classes.Remove("drop-after");
    }

    private async void GroupRow_Drop(object? sender, DragEventArgs e)
    {
        if (sender is Control control) ClearDropIndicator(control);
        var drop = DropForTarget(sender, e);
        e.DragEffects = drop != null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (drop == null) return;
        await RunOperationAsync(async token =>
        {
            if (!ReferenceEquals(_session, drop.Drag.Session)) return;
            var session = drop.Drag.Session;
            switch (drop.Drag.Row)
            {
                case EntryRow row:
                    await session.Storage.MoveEntryAsync(row.Id, row.Revision, drop.Target.Id, session.Crypto);
                    token.ThrowIfCancellationRequested();
                    await RefreshEntryQueryAsync(session, _query with
                    {
                        GroupId = drop.Target.Id,
                        OnlyRecycleBin = drop.Target.IsRecycleBin,
                        SearchText = ""
                    }, token);
                    break;
                case PwGroup group:
                    if (drop.Placement == DropPlacement.Inside)
                        await session.Storage.MoveGroupAsync(group.Id, group.Revision, drop.Target.Id, session.Crypto);
                    else
                        await PlaceFolderAsync(session, group, drop.Target, drop.Placement == DropPlacement.Before);
                    token.ThrowIfCancellationRequested();
                    await ReloadFolderAsync(session, group.Id, token);
                    break;
            }
        });
    }

    private Task PlaceFolderAsync(VaultSession session, PwGroup folder, PwGroup reference, bool before)
    {
        var siblings = FolderChoices.Select(choice => choice.Folder)
            .OfType<PwGroup>()
            .Where(group => group.ParentId == reference.ParentId && !group.IsRoot && !group.IsRecycleBin && group.Id != folder.Id)
            .Select(group => group.DeepClone()).ToList();
        int referenceIndex = siblings.FindIndex(item => item.Id == reference.Id);
        if (referenceIndex < 0) throw new VaultConflictException("The reference folder changed.");
        var moved = folder.DeepClone();
        moved.ParentId = reference.ParentId;
        siblings.Insert(referenceIndex + (before ? 0 : 1), moved);
        for (int index = 0; index < siblings.Count; index++) siblings[index].SortOrder = index;
        return session.Storage.SaveGroupsBatchAsync(siblings, session.Crypto);
    }
}
