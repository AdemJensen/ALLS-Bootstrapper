using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Alls.Bootstrapper.Models;
using Alls.Configurator.Services;
using Microsoft.Win32;

namespace Alls.Configurator;

public partial class MainWindow : Window
{
    private sealed record UpdateSourceReference(int Index, string Id, bool IsValid)
    {
        public string DisplayText => IsValid ? Id : $"{Id}（更新源不存在）";
        public string ToolTip => IsValid ? $"使用更新源“{Id}”" : $"找不到 ID 为“{Id}”的更新源；请移除或重新添加";
    }

    private readonly ConfigurationDocumentService documents = new();
    private LauncherSettings settings = new();
    private LauncherSettings? baselineSettings;
    private string? currentPath;
    private bool hasDocument;
    private bool isDirty;
    private bool suppressChanges;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnEditorChanged));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnEditorChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnEditorChanged));
        AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(OnComboChanged));

        CloseDocumentCore();
    }

    public void OpenFromCommandLine(string path)
    {
        try
        {
            OpenDocument(Path.GetFullPath(path));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法打开配置文件：\n{exception.Message}", "ALLS Configurator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static LauncherSettings CreateNewSettings() => new();

    private void SetDocument(LauncherSettings value, string? path, bool dirty)
    {
        suppressChanges = true;
        settings = value;
        ConfigurationDocumentService.Normalize(settings);
        baselineSettings = documents.Clone(settings);
        currentPath = path;
        hasDocument = true;
        DataContext = null;
        DataContext = settings;

        SectionList.SelectedIndex = 0;
        EditorTabs.SelectedIndex = 0;
        GamesList.SelectedIndex = settings.Games.Count > 0 ? 0 : -1;
        DevicesList.SelectedIndex = settings.Input.Devices.Count > 0 ? 0 : -1;
        SourcesList.SelectedIndex = settings.UpdateSources.Count > 0 ? 0 : -1;
        OperationsList.SelectedIndex = settings.Operations.Count > 0 ? 0 : -1;
        UpdateGameEditorState();
        UpdateSourceEditorState();
        UpdateOperationEditorState();
        isDirty = dirty;
        DirtyIndicator.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        UpdateDocumentUi();
        StatusText.Text = path is null ? "已创建新配置" : "已打开配置";

        Dispatcher.InvokeAsync(() => suppressChanges = false, DispatcherPriority.ContextIdle);
    }

    private void OpenDocument(string path)
    {
        var loaded = documents.Load(path);
        SetDocument(loaded, Path.GetFullPath(path), false);
    }

    private void UpdateDocumentChrome()
    {
        SidebarFileName.Text = !hasDocument ? "未打开文件" : currentPath is null ? "未命名配置" : Path.GetFileName(currentPath);
        SidebarFileName.ToolTip = !hasDocument ? "请选择配置文件或新建配置" : currentPath ?? "尚未保存";
        PathText.Text = !hasDocument ? "没有打开的配置文件" : currentPath ?? "尚未选择保存位置";
        Title = $"ALLS Configurator — {SidebarFileName.Text}{(isDirty ? " *" : string.Empty)}";
    }

    private void UpdateDocumentUi()
    {
        EditorTabs.Visibility = hasDocument ? Visibility.Visible : Visibility.Collapsed;
        EmptyDocumentPanel.Visibility = hasDocument ? Visibility.Collapsed : Visibility.Visible;
        SectionList.IsEnabled = hasDocument;
        CloseFileButton.IsEnabled = hasDocument;
        SaveButton.IsEnabled = hasDocument;
        SaveAsButton.IsEnabled = hasDocument;
        DiscardChangesButton.IsEnabled = hasDocument && isDirty;
        ValidateButton.IsEnabled = hasDocument;
        UpdateDocumentChrome();
    }

    private void CloseDocumentCore()
    {
        suppressChanges = true;
        settings = new LauncherSettings();
        baselineSettings = null;
        currentPath = null;
        hasDocument = false;
        isDirty = false;
        DataContext = null;
        GamesList.SelectedIndex = -1;
        DevicesList.SelectedIndex = -1;
        SourcesList.SelectedIndex = -1;
        OperationsList.SelectedIndex = -1;
        SectionList.SelectedIndex = -1;
        ValidationGrid.ItemsSource = null;
        ValidationSummary.Text = "尚未检查";
        ValidationSummary.Foreground = Brushes.Black;
        JsonPreview.Clear();
        DirtyIndicator.Visibility = Visibility.Collapsed;
        UpdateGameEditorState();
        UpdateSourceEditorState();
        UpdateOperationEditorState();
        UpdateDocumentUi();
        StatusText.Text = "请选择配置文件或新建配置";
        Dispatcher.InvokeAsync(() => suppressChanges = false, DispatcherPriority.ContextIdle);
    }

    private void SetDirty(bool value = true)
    {
        if (!hasDocument || (suppressChanges && value)) return;
        isDirty = value;
        DirtyIndicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpdateDocumentUi();
    }

    private void OnEditorChanged(object sender, RoutedEventArgs e)
    {
        if (!hasDocument) return;
        if (e.OriginalSource == JsonPreview || (e.OriginalSource is TextBox textBox && textBox.IsReadOnly)) return;
        SetDirty();
        if (e.OriginalSource is TextBox editor)
        {
            var property = editor.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path;
            if (property is "Title" or "Name" or "Id")
            {
                Dispatcher.InvokeAsync(RefreshRootLists, DispatcherPriority.Background);
            }
        }
    }

    private void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is ComboBox && hasDocument) SetDirty();
    }

    private bool ConfirmDiscardOrSave()
    {
        if (!hasDocument || !isDirty) return true;
        var answer = MessageBox.Show(this, "当前配置有未保存的修改。是否先保存？", "ALLS Configurator",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch
        {
            MessageBoxResult.Yes => SaveDocument(false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscardOrSave()) SetDocument(CreateNewSettings(), null, true);
    }

    private void CloseFile_Click(object sender, RoutedEventArgs e)
    {
        if (!hasDocument || !ConfirmDiscardOrSave()) return;
        CloseDocumentCore();
    }

    private void DiscardChanges_Click(object sender, RoutedEventArgs e)
    {
        if (!hasDocument || !isDirty || baselineSettings is null) return;
        var answer = MessageBox.Show(this,
            "确定放弃自上次保存或打开以来的全部修改吗？\n\n此操作无法撤销。",
            "放弃所有修改", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var restored = documents.Clone(baselineSettings);
        SetDocument(restored, currentPath, false);
        StatusText.Text = currentPath is null ? "已恢复新建配置的初始内容" : "已放弃所有未保存修改";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardOrSave()) return;
        var dialog = new OpenFileDialog
        {
            Title = "打开 ALLS 配置",
            Filter = "ALLS 配置 (alls-launcher.json)|alls-launcher.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            OpenDocument(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"配置文件无法读取：\n{exception.Message}\n\n当前编辑内容没有被替换。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument(false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveDocument(true);

    private bool SaveDocument(bool saveAs)
    {
        if (!hasDocument) return false;
        CommitPendingEdits();
        if (HasBindingError(this))
        {
            StatusText.Text = "有字段无法转换为目标类型";
            MessageBox.Show(this, "有字段包含无效内容（例如数值字段中输入了文字）。请修正红框字段后再保存。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var issues = RunValidationAndPreview();
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            SectionList.SelectedIndex = 6;
            MessageBox.Show(this, "配置中仍有错误。请在“校验与 JSON”页修正后再保存。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var targetPath = currentPath;
        if (saveAs || string.IsNullOrWhiteSpace(targetPath))
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存 ALLS 配置",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                FileName = currentPath is null ? "alls-launcher.json" : Path.GetFileName(currentPath),
                InitialDirectory = currentPath is null ? null : Path.GetDirectoryName(currentPath),
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(this) != true) return false;
            targetPath = dialog.FileName;
        }

        try
        {
            documents.Save(targetPath!, settings);
            currentPath = Path.GetFullPath(targetPath!);
            baselineSettings = documents.Clone(settings);
            SetDirty(false);
            StatusText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"保存失败：\n{exception.Message}", "ALLS Configurator", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void CommitPendingEdits()
    {
        if (!hasDocument) return;
        DefaultTimelineGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        DefaultTimelineGrid.CommitEdit(DataGridEditingUnit.Row, true);
        GameTimelineGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        GameTimelineGrid.CommitEdit(DataGridEditingUnit.Row, true);
        FocusManager.SetFocusedElement(this, this);
        Keyboard.ClearFocus();
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (!hasDocument) return;
        CommitPendingEdits();
        RunValidationAndPreview();
        SectionList.SelectedIndex = 6;
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!hasDocument) return;
        CommitPendingEdits();
        RunValidationAndPreview();
    }

    private IReadOnlyList<ValidationIssue> RunValidationAndPreview()
    {
        if (!hasDocument) return [];
        var issues = ConfigurationValidator.Validate(settings);
        ValidationGrid.ItemsSource = issues;
        var errors = issues.Count(issue => issue.Severity == ValidationSeverity.Error);
        var warnings = issues.Count - errors;
        ValidationSummary.Text = issues.Count == 0 ? "✓ 配置检查通过，没有发现问题" : $"{errors} 个错误，{warnings} 个警告";
        ValidationSummary.Foreground = errors > 0
            ? System.Windows.Media.Brushes.Crimson
            : warnings > 0 ? System.Windows.Media.Brushes.DarkOrange : System.Windows.Media.Brushes.SeaGreen;
        JsonPreview.Text = documents.Serialize(settings);
        StatusText.Text = issues.Count == 0 ? "配置检查通过" : $"检查完成：{errors} 个错误，{warnings} 个警告";
        return issues;
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!hasDocument || EditorTabs is null || SectionList.SelectedIndex < 0) return;
        EditorTabs.SelectedIndex = SectionList.SelectedIndex;
        if (SectionList.SelectedIndex == 6) RunValidationAndPreview();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardOrSave()) e.Cancel = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        switch (e.Key)
        {
            case Key.S when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                SaveDocument(true); e.Handled = true; break;
            case Key.S:
                SaveDocument(false); e.Handled = true; break;
            case Key.O:
                Open_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.N:
                New_Click(this, new RoutedEventArgs()); e.Handled = true; break;
        }
    }

    private static bool HasBindingError(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (HasBindingError(VisualTreeHelper.GetChild(element, index))) return true;
        }
        return false;
    }

    private string ToConfigRelativePath(string path)
    {
        if (currentPath is null) return path;
        var directory = Path.GetDirectoryName(currentPath);
        return directory is null ? path : Path.GetRelativePath(directory, path).Replace('\\', '/');
    }

    private string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (currentPath is not null) dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        return dialog.ShowDialog(this) == true ? ToConfigRelativePath(dialog.FileName) : null;
    }

    private string? PickDirectory(string title, string? configuredPath)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        var baseDirectory = currentPath is null
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(currentPath) ?? Environment.CurrentDirectory;
        var candidate = baseDirectory;
        try
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                candidate = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.GetFullPath(configuredPath, baseDirectory);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An invalid value should not prevent the user from replacing it with a browsed directory.
        }
        if (Directory.Exists(candidate)) dialog.InitialDirectory = candidate;
        return dialog.ShowDialog(this) == true ? ToConfigRelativePath(dialog.FolderName) : null;
    }

    private void BrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile("选择 Logo 图片", "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*");
        if (path is not null) LogoPathBox.Text = path;
    }

    private void BrowseLoading_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile("选择加载动画", "GIF 动画|*.gif|图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*");
        if (path is not null) LoadingPathBox.Text = path;
    }

    private void BrowseGameWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = PickDirectory("选择游戏工作目录", SelectedGame?.Launch.WorkingDirectory);
        if (path is not null) GameWorkingDirectoryBox.Text = path;
    }

    private void BrowseGameUpdateTargetDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = PickDirectory("选择游戏更新目标目录", SelectedGame?.Update.TargetDirectory);
        if (path is not null) GameUpdateTargetDirectoryBox.Text = path;
    }

    private void BrowseUpdateSourcePath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSource is not { Kind: UpdateSourceKind.Usb })
        {
            MessageBox.Show(this, "HTTP 更新源的目录是相对于根地址的 URL 路径，请直接输入；浏览功能仅适用于 U 盘 / 本地目录。",
                "无法浏览 HTTP 目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var path = PickDirectory("选择更新源目录", SelectedSource?.Path);
        if (path is not null) UpdateSourcePathBox.Text = path;
    }

    private void BrowseOperationWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = PickDirectory("选择命令工作目录", SelectedOperation?.Command.WorkingDirectory);
        if (path is not null) OperationWorkingDirectoryBox.Text = path;
    }

    private static T CloneItem<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException("无法复制项目。");
    }

    private static string UniqueId(string prefix, IEnumerable<string> existing)
    {
        var used = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = prefix;
        var suffix = 2;
        while (used.Contains(id)) id = $"{prefix}-{suffix++}";
        return id;
    }

    private void RefreshRootLists()
    {
        GamesList.Items.Refresh();
        DevicesList.Items.Refresh();
        SourcesList.Items.Refresh();
        OperationsList.Items.Refresh();
        DefaultGameCombo.Items.Refresh();
        RefreshGameUpdateSources();
    }

    private static void MoveItem<T>(List<T> list, T? item, int delta, ItemsControl control)
        where T : class
    {
        if (item is null) return;
        var index = list.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        list.RemoveAt(index);
        list.Insert(target, item);
        control.Items.Refresh();
        if (control is Selector selector) selector.SelectedItem = item;
    }

    // Games
    private GameSettings? SelectedGame => GamesList.SelectedItem as GameSettings;

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateGameEditorState();

    private void UpdateGameEditorState()
    {
        if (GameFields is null) return;
        var game = SelectedGame;
        NoGameHint.Visibility = game is null ? Visibility.Visible : Visibility.Collapsed;
        GameFields.Visibility = game is null ? Visibility.Collapsed : Visibility.Visible;
        suppressChanges = true;
        GameLayoutOverride.IsChecked = game?.LayoutMode is not null;
        GameLayoutCombo.IsEnabled = game?.LayoutMode is not null;
        if (game?.LayoutMode is not null) GameLayoutCombo.SelectedValue = game.LayoutMode.Value;
        RefreshGameUpdateSources();
        Dispatcher.InvokeAsync(() => suppressChanges = false, DispatcherPriority.ContextIdle);
    }

    private void GameLayoutOverride_Changed(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game is null) return;
        var enabled = GameLayoutOverride.IsChecked == true;
        GameLayoutCombo.IsEnabled = enabled;
        if (enabled && game.LayoutMode is null)
        {
            game.LayoutMode = settings.Display.LayoutMode;
            GameLayoutCombo.SelectedValue = game.LayoutMode.Value;
        }
        else if (!enabled)
        {
            game.LayoutMode = null;
            GameLayoutCombo.SelectedValue = null;
        }
        SetDirty();
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var id = UniqueId("new-game", settings.Games.Select(game => game.Id));
        var game = GameSettings.CreateDefault();
        game.Id = id; game.Title = "新游戏"; game.Description = string.Empty;
        settings.Games.Add(game); GamesList.Items.Refresh(); DefaultGameCombo.Items.Refresh(); GamesList.SelectedItem = game; SetDirty();
    }

    private void DuplicateGame_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } selected) return;
        var clone = CloneItem(selected);
        clone.Id = UniqueId(selected.Id + "-copy", settings.Games.Select(game => game.Id));
        clone.Title += "（副本）";
        settings.Games.Insert(settings.Games.IndexOf(selected) + 1, clone); GamesList.Items.Refresh(); DefaultGameCombo.Items.Refresh(); GamesList.SelectedItem = clone; SetDirty();
    }

    private void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } selected) return;
        if (MessageBox.Show(this, $"确定删除游戏“{selected.Title}”吗？", "删除游戏", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var index = settings.Games.IndexOf(selected); settings.Games.Remove(selected); GamesList.Items.Refresh(); DefaultGameCombo.Items.Refresh(); GamesList.SelectedIndex = Math.Min(index, settings.Games.Count - 1); SetDirty();
    }

    private void MoveGameUp_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Games, SelectedGame, -1, GamesList); SetDirty(); }
    private void MoveGameDown_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Games, SelectedGame, 1, GamesList); SetDirty(); }

    private void RefreshGameUpdateSources()
    {
        if (UpdateSourceIdsList is null || AvailableUpdateSourceCombo is null) return;
        var game = SelectedGame;
        if (game is null)
        {
            UpdateSourceIdsList.ItemsSource = null;
            AvailableUpdateSourceCombo.ItemsSource = null;
            return;
        }

        var validIds = settings.UpdateSources
            .Select(source => source.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        UpdateSourceIdsList.ItemsSource = game.Update.SourceIds
            .Select((id, index) => new UpdateSourceReference(index, id, validIds.Contains(id)))
            .ToList();

        var referencedIds = game.Update.SourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        AvailableUpdateSourceCombo.ItemsSource = settings.UpdateSources
            .Where(source => !string.IsNullOrWhiteSpace(source.Id) && !referencedIds.Contains(source.Id))
            .ToList();
        AvailableUpdateSourceCombo.SelectedIndex = AvailableUpdateSourceCombo.Items.Count > 0 ? 0 : -1;
    }

    private void AddGameUpdateSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game || AvailableUpdateSourceCombo.SelectedItem is not UpdateSourceSettings source) return;
        if (!game.Update.SourceIds.Contains(source.Id, StringComparer.OrdinalIgnoreCase))
        {
            game.Update.SourceIds.Add(source.Id);
            RefreshGameUpdateSources();
            SetDirty();
        }
    }

    private void RemoveGameUpdateSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game || UpdateSourceIdsList.SelectedItem is not UpdateSourceReference reference) return;
        game.Update.SourceIds.RemoveAt(reference.Index);
        RefreshGameUpdateSources();
        SetDirty();
    }

    private void MoveGameUpdateSourceUp_Click(object sender, RoutedEventArgs e) => MoveGameUpdateSource(-1);
    private void MoveGameUpdateSourceDown_Click(object sender, RoutedEventArgs e) => MoveGameUpdateSource(1);

    private void MoveGameUpdateSource(int delta)
    {
        if (SelectedGame is not { } game || UpdateSourceIdsList.SelectedItem is not UpdateSourceReference reference) return;
        var target = reference.Index + delta;
        if (target < 0 || target >= game.Update.SourceIds.Count) return;
        var id = game.Update.SourceIds[reference.Index];
        game.Update.SourceIds.RemoveAt(reference.Index);
        game.Update.SourceIds.Insert(target, id);
        RefreshGameUpdateSources();
        UpdateSourceIdsList.SelectedIndex = target;
        SetDirty();
    }

    private void AddGamePhase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game) return;
        var phase = NewPhase(game.Timeline); game.Timeline.Add(phase); GameTimelineGrid.Items.Refresh(); GameTimelineGrid.SelectedItem = phase; SetDirty();
    }
    private void DeleteGamePhase_Click(object sender, RoutedEventArgs e) { if (SelectedGame is { } game && GameTimelineGrid.SelectedItem is BootPhase phase) { game.Timeline.Remove(phase); GameTimelineGrid.Items.Refresh(); SetDirty(); } }
    private void MoveGamePhaseUp_Click(object sender, RoutedEventArgs e) { if (SelectedGame is { } game) { MoveItem(game.Timeline, GameTimelineGrid.SelectedItem as BootPhase, -1, GameTimelineGrid); SetDirty(); } }
    private void MoveGamePhaseDown_Click(object sender, RoutedEventArgs e) { if (SelectedGame is { } game) { MoveItem(game.Timeline, GameTimelineGrid.SelectedItem as BootPhase, 1, GameTimelineGrid); SetDirty(); } }

    // Default timeline
    private static BootPhase NewPhase(IReadOnlyCollection<BootPhase> phases)
    {
        var step = phases.Count == 0 ? 1 : Math.Min(99, phases.Max(phase => phase.Step) + 1);
        return new BootPhase { Step = step, MessageKey = $"STEP_{step:00}_MESSAGE", DurationMs = 1500 };
    }
    private void AddDefaultPhase_Click(object sender, RoutedEventArgs e) { var phase = NewPhase(settings.Timeline); settings.Timeline.Add(phase); DefaultTimelineGrid.Items.Refresh(); DefaultTimelineGrid.SelectedItem = phase; SetDirty(); }
    private void DeleteDefaultPhase_Click(object sender, RoutedEventArgs e) { if (DefaultTimelineGrid.SelectedItem is BootPhase phase) { settings.Timeline.Remove(phase); DefaultTimelineGrid.Items.Refresh(); SetDirty(); } }
    private void MoveDefaultPhaseUp_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Timeline, DefaultTimelineGrid.SelectedItem as BootPhase, -1, DefaultTimelineGrid); SetDirty(); }
    private void MoveDefaultPhaseDown_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Timeline, DefaultTimelineGrid.SelectedItem as BootPhase, 1, DefaultTimelineGrid); SetDirty(); }
    private void ResetDefaultTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "用内置的四阶段时间线替换当前默认时间线？", "恢复默认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        settings.Timeline = LauncherSettings.CreateDefaultTimeline(); DefaultTimelineGrid.ItemsSource = settings.Timeline; SetDirty();
    }

    // Input devices
    private HidDeviceSettings? SelectedDevice => DevicesList.SelectedItem as HidDeviceSettings;
    private void DevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void AddDevice_Click(object sender, RoutedEventArgs e) { var item = new HidDeviceSettings { Name = "新 HID 设备", VendorId = "0x0000", ProductIds = ["0x0000"] }; settings.Input.Devices.Add(item); DevicesList.Items.Refresh(); DevicesList.SelectedItem = item; SetDirty(); }
    private void DuplicateDevice_Click(object sender, RoutedEventArgs e) { if (SelectedDevice is not { } selected) return; var clone = CloneItem(selected); clone.Name += "（副本）"; settings.Input.Devices.Insert(settings.Input.Devices.IndexOf(selected) + 1, clone); DevicesList.Items.Refresh(); DevicesList.SelectedItem = clone; SetDirty(); }
    private void DeleteDevice_Click(object sender, RoutedEventArgs e) { if (SelectedDevice is not { } selected) return; var index = settings.Input.Devices.IndexOf(selected); settings.Input.Devices.Remove(selected); DevicesList.Items.Refresh(); DevicesList.SelectedIndex = Math.Min(index, settings.Input.Devices.Count - 1); SetDirty(); }
    private void MoveDeviceUp_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Input.Devices, SelectedDevice, -1, DevicesList); SetDirty(); }
    private void MoveDeviceDown_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Input.Devices, SelectedDevice, 1, DevicesList); SetDirty(); }

    // Update sources
    private UpdateSourceSettings? SelectedSource => SourcesList.SelectedItem as UpdateSourceSettings;
    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSourceEditorState();
    private void UpdateSourceKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var kind = sender is ComboBox { SelectedValue: UpdateSourceKind selectedKind }
            ? selectedKind
            : SelectedSource?.Kind;
        UpdateSourceEditorState(kind);
    }

    private void UpdateSourceEditorState(UpdateSourceKind? selectedKind = null)
    {
        var kind = selectedKind ?? SelectedSource?.Kind;
        var httpVisibility = kind == UpdateSourceKind.Http ? Visibility.Visible : Visibility.Collapsed;
        var usbVisibility = kind == UpdateSourceKind.Usb ? Visibility.Visible : Visibility.Collapsed;
        SetElementsVisibility(httpVisibility,
            HttpBaseUrlLabel, HttpBaseUrlEditor,
            HttpUsernameLabel, HttpUsernameEditor,
            HttpPasswordLabel, HttpPasswordEditor,
            HttpTimeoutLabel, HttpTimeoutEditor);
        SetElementsVisibility(usbVisibility, UsbDriveLabel, UsbDriveEditor, BrowseUpdateSourcePathButton);
    }
    private void AddSource_Click(object sender, RoutedEventArgs e) { var item = new UpdateSourceSettings { Id = UniqueId("update-source", settings.UpdateSources.Select(source => source.Id)), Enabled = true, Kind = UpdateSourceKind.Http }; settings.UpdateSources.Add(item); SourcesList.Items.Refresh(); SourcesList.SelectedItem = item; RefreshGameUpdateSources(); SetDirty(); }
    private void DuplicateSource_Click(object sender, RoutedEventArgs e) { if (SelectedSource is not { } selected) return; var clone = CloneItem(selected); clone.Id = UniqueId(selected.Id + "-copy", settings.UpdateSources.Select(source => source.Id)); settings.UpdateSources.Insert(settings.UpdateSources.IndexOf(selected) + 1, clone); SourcesList.Items.Refresh(); SourcesList.SelectedItem = clone; RefreshGameUpdateSources(); SetDirty(); }
    private void DeleteSource_Click(object sender, RoutedEventArgs e) { if (SelectedSource is not { } selected) return; var index = settings.UpdateSources.IndexOf(selected); settings.UpdateSources.Remove(selected); SourcesList.Items.Refresh(); SourcesList.SelectedIndex = Math.Min(index, settings.UpdateSources.Count - 1); RefreshGameUpdateSources(); SetDirty(); }
    private void MoveSourceUp_Click(object sender, RoutedEventArgs e) { MoveItem(settings.UpdateSources, SelectedSource, -1, SourcesList); SetDirty(); }
    private void MoveSourceDown_Click(object sender, RoutedEventArgs e) { MoveItem(settings.UpdateSources, SelectedSource, 1, SourcesList); SetDirty(); }

    // Operations
    private OperationSettings? SelectedOperation => OperationsList.SelectedItem as OperationSettings;
    private void OperationsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOperationEditorState();
    private void OperationKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var kind = sender is ComboBox { SelectedValue: OperationKind selectedKind }
            ? selectedKind
            : SelectedOperation?.Kind;
        UpdateOperationEditorState(kind);
    }

    private void UpdateOperationEditorState(OperationKind? selectedKind = null)
    {
        var kind = selectedKind ?? SelectedOperation?.Kind;
        var visibility = kind == OperationKind.Command ? Visibility.Visible : Visibility.Collapsed;
        SetElementsVisibility(visibility, OperationCommandGroup);
    }

    private static void SetElementsVisibility(Visibility visibility, params UIElement?[] elements)
    {
        foreach (var element in elements)
        {
            if (element is not null) element.Visibility = visibility;
        }
    }
    private void AddOperation_Click(object sender, RoutedEventArgs e) { var item = new OperationSettings { Id = UniqueId("custom-command", settings.Operations.Select(operation => operation.Id)), Title = "新操作", Kind = OperationKind.Command, Command = new LaunchSettings { Enabled = true } }; settings.Operations.Add(item); OperationsList.Items.Refresh(); OperationsList.SelectedItem = item; SetDirty(); }
    private void DuplicateOperation_Click(object sender, RoutedEventArgs e) { if (SelectedOperation is not { } selected) return; var clone = CloneItem(selected); clone.Id = UniqueId(selected.Id + "-copy", settings.Operations.Select(operation => operation.Id)); clone.Title += "（副本）"; settings.Operations.Insert(settings.Operations.IndexOf(selected) + 1, clone); OperationsList.Items.Refresh(); OperationsList.SelectedItem = clone; SetDirty(); }
    private void DeleteOperation_Click(object sender, RoutedEventArgs e) { if (SelectedOperation is not { } selected) return; var index = settings.Operations.IndexOf(selected); settings.Operations.Remove(selected); OperationsList.Items.Refresh(); OperationsList.SelectedIndex = Math.Min(index, settings.Operations.Count - 1); SetDirty(); }
    private void MoveOperationUp_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Operations, SelectedOperation, -1, OperationsList); SetDirty(); }
    private void MoveOperationDown_Click(object sender, RoutedEventArgs e) { MoveItem(settings.Operations, SelectedOperation, 1, OperationsList); SetDirty(); }
}
