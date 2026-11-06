using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Core.Ids;

namespace TaskPane.App.Windows;

public partial class TaskPickerDialog : Window
{
    private readonly TaskPickerDialogModel _model;

    public TaskPickerDialog(
        string title,
        string prompt,
        IReadOnlyList<TaskPickerItem> items)
    {
        InitializeComponent();
        _model = new TaskPickerDialogModel(title, prompt, items);
        DataContext = _model;
    }

    public TaskPickerItem? SelectedItem { get; private set; }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedItem is null)
        {
            return;
        }

        SelectedItem = _model.SelectedItem;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class TaskPickerItem
{
    public required TaskId Id { get; init; }
    public required string Title { get; init; }
}

public partial class TaskPickerDialogModel : ObservableObject
{
    private readonly IReadOnlyList<TaskPickerItem> _all;

    public TaskPickerDialogModel(string title, string prompt, IReadOnlyList<TaskPickerItem> items)
    {
        Title = title;
        Prompt = prompt;
        _all = items;
        FilteredItems = new ObservableCollection<TaskPickerItem>(items);
    }

    public string Title { get; }

    public string Prompt { get; }

    public ObservableCollection<TaskPickerItem> FilteredItems { get; }

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private TaskPickerItem? _selectedItem;

    partial void OnFilterTextChanged(string value)
    {
        var term = value.Trim();
        FilteredItems.Clear();
        foreach (var item in _all)
        {
            if (term.Length == 0
                || item.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }

        if (SelectedItem is not null && !FilteredItems.Contains(SelectedItem))
        {
            SelectedItem = null;
        }
    }
}
