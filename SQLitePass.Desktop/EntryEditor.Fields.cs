using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Models;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor
{
    private void InitializeCustomFields()
    {
        FieldNameLabel.Text = _text("FieldName", []);
        FieldValueLabel.Text = _text("FieldValue", []);
        FieldProtected.Content = _text("FieldMask", []);
        AddField.Content = _text("AddField", []);
        SaveField.Content = _text("SaveField", []);
        RemoveField.Content = _text("RemoveField", []);
        CustomFields.ItemsSource = _draft.CustomFields;
        RemoveField.IsEnabled = false;
    }

    private void CustomFields_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        HideSensitiveValue();
        var field = CustomFields.SelectedItem as PwCustomField;
        FieldName.Text = field?.Key ?? "";
        FieldValue.Text = field?.Value ?? "";
        FieldProtected.IsChecked = field?.IsProtected ?? true;
        RemoveField.IsEnabled = field != null;
    }

    private void AddField_Click(object? sender, RoutedEventArgs e)
    {
        if (!CommitField()) return;
        CustomFields.SelectedItem = null;
        FieldName.Clear();
        FieldValue.Clear();
        FieldProtected.IsChecked = true;
        FieldName.Focus();
    }

    private void SaveField_Click(object? sender, RoutedEventArgs e) => CommitField();

    private bool CommitField()
    {
        var selected = CustomFields.SelectedItem as PwCustomField;
        string name = FieldName.Text?.Trim() ?? "";
        string value = FieldValue.Text ?? "";
        if (selected == null && name.Length == 0 && value.Length == 0) return true;
        if (name.Length == 0)
        {
            ShowValidation("FieldNameRequired");
            FieldName.Focus();
            return false;
        }

        var field = selected ?? new PwCustomField { EntryId = _draft.Id };
        field.Key = name;
        field.Value = value;
        field.IsProtected = FieldProtected.IsChecked == true;
        if (selected == null)
        {
            _draft.CustomFields.Add(field);
            CustomFields.SelectedItem = field;
        }
        ValidationMessage.IsVisible = false;
        return true;
    }

    private void RemoveField_Click(object? sender, RoutedEventArgs e)
    {
        if (CustomFields.SelectedItem is not PwCustomField field) return;
        field.Value = "";
        _draft.CustomFields.Remove(field);
        CustomFields.SelectedItem = null;
        FieldName.Clear();
        FieldValue.Clear();
    }
}
