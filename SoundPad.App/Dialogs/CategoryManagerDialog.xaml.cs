using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SoundPad.App.Models;

namespace SoundPad.App.Dialogs;

public partial class CategoryManagerDialog : Wpf.Ui.Controls.FluentWindow
{
    private class CategoryEntry
    {
        public string Name      { get; set; } = "";
        public string CountText { get; set; } = "";
    }

    private static readonly HashSet<string> VirtualCategories =
        new(StringComparer.OrdinalIgnoreCase) { "All", "Favorites", "Recent" };

    // Current list of real categories shown in the dialog.
    private readonly List<CategoryEntry> _entries = new();

    // current working name → original library name (handles chained renames).
    private readonly Dictionary<string, string> _workingToOriginal =
        new(StringComparer.OrdinalIgnoreCase);

    // original library name → final name (what MainWindow uses to remap sounds).
    private readonly Dictionary<string, string> _originalToFinal =
        new(StringComparer.OrdinalIgnoreCase);

    // Sound count per working name (so the list shows "3 sounds" etc).
    private readonly Dictionary<string, int> _soundCounts =
        new(StringComparer.OrdinalIgnoreCase);

    private enum EditMode { None, Create, Rename, Delete }
    private EditMode _editMode = EditMode.None;

    // --- Results ---
    public List<string>               FinalCategories    { get; private set; } = new();
    public Dictionary<string, string> SoundCategoryRemap { get; private set; } = new();

    public CategoryManagerDialog(Window owner,
        IReadOnlyList<SoundItem> library,
        IReadOnlyList<string>    customCategories)
    {
        InitializeComponent();
        Owner = owner;

        foreach (var s in library)
        {
            var cat = string.IsNullOrWhiteSpace(s.Category) ? "General" : s.Category;
            if (VirtualCategories.Contains(cat)) continue;
            _soundCounts[cat] = _soundCounts.TryGetValue(cat, out var c) ? c + 1 : 1;
        }

        var realCats = library
            .Select(s => string.IsNullOrWhiteSpace(s.Category) ? "General" : s.Category)
            .Where(c => !VirtualCategories.Contains(c))
            .Concat(customCategories.Where(c => !VirtualCategories.Contains(c)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var cat in realCats)
        {
            _workingToOriginal[cat] = cat;
            _entries.Add(new CategoryEntry { Name = cat, CountText = FormatCount(cat) });
        }

        RefreshList();
    }

    private void RefreshList()
    {
        CategoryListBox.ItemsSource = null;
        CategoryListBox.ItemsSource = _entries;
    }

    private string FormatCount(string workingName)
    {
        if (!_soundCounts.TryGetValue(workingName, out var n) || n == 0)
            return "empty";
        return n == 1 ? "1 sound" : $"{n} sounds";
    }

    private CategoryEntry? SelectedEntry => CategoryListBox.SelectedItem as CategoryEntry;

    private void CategoryListBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        bool sel = SelectedEntry is not null;
        RenameButton.IsEnabled = sel;
        DeleteButton.IsEnabled = sel;
        CloseZone();
    }

    // ── Zone helpers ──────────────────────────────────────────────────────────

    private void CloseZone()
    {
        EditZone.Visibility      = Visibility.Collapsed;
        EditErrorText.Visibility = Visibility.Collapsed;
        MoveToLabel.Visibility   = Visibility.Collapsed;
        MoveToCombo.Visibility   = Visibility.Collapsed;
        EditBox.Visibility       = Visibility.Visible;
        EditBox.Text             = "";
        MoveToCombo.ItemsSource  = null;
        _editMode                = EditMode.None;
    }

    private void ShowError(string msg)
    {
        EditErrorText.Text       = msg;
        EditErrorText.Visibility = Visibility.Visible;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        CloseZone();
        _editMode           = EditMode.Create;
        EditZoneLabel.Text  = "New category name:";
        EditBox.Text        = "";
        EditZone.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => EditBox.Focus()));
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry is null) return;
        CloseZone();
        _editMode           = EditMode.Rename;
        EditZoneLabel.Text  = $"Rename \"{entry.Name}\" to:";
        EditBox.Text        = entry.Name;
        EditZone.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => { EditBox.SelectAll(); EditBox.Focus(); }));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry is null) return;
        CloseZone();
        _editMode          = EditMode.Delete;
        EditBox.Visibility = Visibility.Collapsed;

        int n = _soundCounts.TryGetValue(entry.Name, out var cnt) ? cnt : 0;
        if (n > 0)
        {
            EditZoneLabel.Text = $"Delete \"{entry.Name}\" — move {n} sound{(n == 1 ? "" : "s")} to:";
            var targets = _entries
                .Where(x => !x.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Name)
                .ToList();
            if (targets.Count == 0)
                targets.Add("General");
            MoveToCombo.ItemsSource   = targets;
            MoveToCombo.SelectedIndex = 0;
            MoveToLabel.Visibility    = Visibility.Visible;
            MoveToCombo.Visibility    = Visibility.Visible;
        }
        else
        {
            EditZoneLabel.Text = $"Delete empty category \"{entry.Name}\"?";
        }
        EditZone.Visibility = Visibility.Visible;
    }

    // ── Edit zone events ──────────────────────────────────────────────────────

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { e.Handled = true; EditConfirm_Click(sender, e); }
        if (e.Key == Key.Escape) { e.Handled = true; EditCancel_Click(sender, e);  }
    }

    private void EditCancel_Click(object sender, RoutedEventArgs e) => CloseZone();

    private void EditConfirm_Click(object sender, RoutedEventArgs e)
    {
        EditErrorText.Visibility = Visibility.Collapsed;
        switch (_editMode)
        {
            case EditMode.Create: CommitCreate(); break;
            case EditMode.Rename: CommitRename(); break;
            case EditMode.Delete: CommitDelete(); break;
        }
    }

    private void CommitCreate()
    {
        var name = EditBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            { ShowError("Category name cannot be empty."); return; }
        if (VirtualCategories.Contains(name))
            { ShowError($"\"{name}\" is a system category and cannot be used."); return; }
        if (_entries.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { ShowError($"\"{name}\" already exists."); return; }

        _workingToOriginal[name] = name;
        _entries.Add(new CategoryEntry { Name = name, CountText = "empty" });
        RefreshList();
        CloseZone();
    }

    private void CommitRename()
    {
        var entry = SelectedEntry;
        if (entry is null) return;

        var newName = EditBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            { ShowError("Category name cannot be empty."); return; }
        if (VirtualCategories.Contains(newName))
            { ShowError($"\"{newName}\" is a system category and cannot be used."); return; }
        if (!newName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)
            && _entries.Any(x => x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            { ShowError($"\"{newName}\" already exists."); return; }

        var original = _workingToOriginal.TryGetValue(entry.Name, out var o) ? o : entry.Name;
        _originalToFinal[original] = newName;

        _workingToOriginal.Remove(entry.Name);
        _workingToOriginal[newName] = original;

        if (_soundCounts.TryGetValue(entry.Name, out var n))
        {
            _soundCounts.Remove(entry.Name);
            _soundCounts[newName] = n;
        }

        entry.Name      = newName;
        entry.CountText = FormatCount(newName);
        RefreshList();
        CloseZone();
    }

    private void CommitDelete()
    {
        var entry = SelectedEntry;
        if (entry is null) return;

        int n      = _soundCounts.TryGetValue(entry.Name, out var cnt) ? cnt : 0;
        var moveTo = n > 0 ? MoveToCombo.SelectedItem as string : null;

        if (n > 0 && string.IsNullOrEmpty(moveTo))
            { ShowError("Select a category to move sounds to."); return; }

        var original = _workingToOriginal.TryGetValue(entry.Name, out var o) ? o : entry.Name;
        _originalToFinal[original] = moveTo ?? "General";

        if (moveTo is not null)
        {
            var target = _entries.FirstOrDefault(x =>
                x.Name.Equals(moveTo, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                _soundCounts[target.Name] =
                    (_soundCounts.TryGetValue(target.Name, out var tc) ? tc : 0) + n;
                target.CountText = FormatCount(target.Name);
            }
        }

        _workingToOriginal.Remove(entry.Name);
        _soundCounts.Remove(entry.Name);
        _entries.Remove(entry);
        RefreshList();
        CloseZone();
    }

    // ── Done ─────────────────────────────────────────────────────────────────

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        FinalCategories    = _entries.Select(x => x.Name).ToList();
        SoundCategoryRemap = new Dictionary<string, string>(_originalToFinal,
            StringComparer.OrdinalIgnoreCase);
        DialogResult = true;
    }
}
