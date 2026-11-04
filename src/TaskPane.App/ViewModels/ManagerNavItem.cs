using Wpf.Ui.Controls;

namespace TaskPane.App.ViewModels;

public enum ManagerNavSection
{
    Inbox = 0,
    Today = 1,
    Next = 2,
    Someday = 3,
    Projects = 4,
    Sleeping = 5,
    Completed = 6,
    RecycleBin = 7,
    Reports = 8,
    History = 9
}

public sealed class ManagerNavItem
{
    public ManagerNavItem(ManagerNavSection section, string title, SymbolRegular symbol)
    {
        Section = section;
        Title = title;
        Symbol = symbol;
    }

    public ManagerNavSection Section { get; }
    public string Title { get; }
    public SymbolRegular Symbol { get; }
}
