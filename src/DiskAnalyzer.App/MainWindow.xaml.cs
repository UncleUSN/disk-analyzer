using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DiskAnalyzer.Core;
using Microsoft.Win32;

namespace DiskAnalyzer;

public partial class MainWindow : Window
{
    private const string AllExtensions = "Все";
    private const string NoExtension = "(без расширения)";
    private const int MinAgeDays = 1;
    private const int MaxAgeDays = 3650;
    private const int MinSizeMb = 1;
    private const int MaxSizeMb = 10240;

    private readonly ObservableCollection<FileCandidate> _results = new();
    private readonly HashSet<string> _extensionLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICollectionView _view;
    private CancellationTokenSource? _scanCts;
    private ExclusionRules _rules;
    private string? _rulesWarning;
    private bool _initializing = true;
    private bool _ready;
    private bool _syncingAge;
    private bool _syncingSize;
    private bool _updatingExtensions;
    private string _filterText = string.Empty;
    private string _selectedExtension = AllExtensions;

    public MainWindow()
    {
        InitializeComponent();
        _rules = LoadRules();
        ResultsGrid.ItemsSource = _results;
        _view = CollectionViewSource.GetDefaultView(_results);
        _view.Filter = FilterCandidate;
        _view.SortDescriptions.Add(new SortDescription(nameof(FileCandidate.SizeBytes), ListSortDirection.Descending));
        ExtensionBox.Items.Add(AllExtensions);
        ExtensionBox.SelectedIndex = 0;
        LoadDrives();
        _initializing = false;
        _ready = true;
        UpdateTotals();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var probe = await Task.Run(LastAccessPolicy.Detect);
            if (!IsLoaded)
                return;
            ShowLastAccessNotice(probe);
        }
        catch (Exception ex)
        {
            if (!IsLoaded)
                return;
            NoticeText.Text = "Не удалось проверить, обновляется ли время последнего доступа: " + ex.Message;
            NoticePanel.Visibility = Visibility.Visible;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _scanCts?.Cancel();
    }

    private void LoadDrives()
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                    continue;

                string label;
                try
                {
                    label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name + " (" + drive.VolumeLabel + ")"
                        : drive.Name;
                    if (!drive.IsReady)
                        continue;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                DriveBox.Items.Add(new DriveChoice(drive.Name, label));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (DriveBox.Items.Cast<DriveChoice>().All(choice => !choice.Root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase)))
            DriveBox.Items.Insert(0, new DriveChoice(@"C:\", @"C:\"));

        var system = DriveBox.Items.Cast<DriveChoice>()
            .FirstOrDefault(choice => choice.Root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase));
        if (system is not null)
            DriveBox.SelectedItem = system;
    }

    private void DriveBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        if (DriveBox.SelectedItem is DriveChoice choice)
            PathBox.Text = choice.Root;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите диск или папку",
            Multiselect = false
        };
        if (Directory.Exists(PathBox.Text))
            dialog.InitialDirectory = PathBox.Text;

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            PathBox.Text = dialog.FolderName;
    }

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ScanButton.IsEnabled)
        {
            e.Handled = true;
            Scan_Click(sender, e);
        }
    }

    private void PathBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void PathBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var path = files[0];
        if (Directory.Exists(path))
            PathBox.Text = path;
        else if (File.Exists(path))
            PathBox.Text = Path.GetDirectoryName(path) ?? path;
    }

    private void AgeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingAge || AgeBox is null)
            return;
        _syncingAge = true;
        AgeBox.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture);
        _syncingAge = false;
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSize || SizeBox is null)
            return;
        _syncingSize = true;
        SizeBox.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture);
        _syncingSize = false;
    }

    private void AgeBox_LostFocus(object sender, RoutedEventArgs e) => CommitAgeBox();

    private void SizeBox_LostFocus(object sender, RoutedEventArgs e) => CommitSizeBox();

    private void AgeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CommitAgeBox();
    }

    private void SizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CommitSizeBox();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RunScanAsync();
        }
        catch (Exception ex)
        {
            if (IsLoaded)
                StatusText.Text = "Ошибка сканирования: " + ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Останавливаем сканирование… Уже найденные файлы останутся в таблице.";
    }

    internal async Task RunScanAsync()
    {
        if (!CommitAgeBox() || !CommitSizeBox())
            return;

        // Поля окна принадлежат потоку UI. В фоновый поток уходят только обычные значения.
        var age = (int)AgeSlider.Value;
        var sizeMb = (int)SizeSlider.Value;
        var rootPath = PathBox.Text.Trim();
        _rules = LoadRules();

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        _results.Clear();
        _extensionLabels.Clear();
        _selectedExtension = AllExtensions;
        _updatingExtensions = true;
        ExtensionBox.Items.Clear();
        ExtensionBox.Items.Add(AllExtensions);
        ExtensionBox.SelectedIndex = 0;
        _updatingExtensions = false;
        OpenButton.IsEnabled = false;
        UpdateTotals();
        SetScanning(true);
        StatusText.Text = "Сканирование… просмотрено файлов: 0, найдено: 0.";

        var criteria = new ScanCriteria(age, sizeMb * ScanCriteria.BytesPerMegabyte);
        var progress = new Progress<ScanProgress>(ApplyProgress);
        var scanner = new DiskScanner();
        var rules = _rules;

        ScanResult result;
        try
        {
            result = await Task.Run(() => scanner.ScanAsync(rootPath, criteria, rules, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            if (!IsLoaded)
                return;
            StatusText.Text = "Сканирование отменено.";
            SetScanning(false);
            return;
        }

        if (!IsLoaded)
            return;

        ApplyFinalStatus(result);
        SetScanning(false);
    }

    private void ApplyProgress(ScanProgress progress)
    {
        // Progress<T> обычно сам возвращает вызов в поток окна.
        // Если контекста синхронизации нет, коллекция и текстовые поля всё равно обновляются здесь.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyProgress(progress));
            return;
        }

        if (Dispatcher.HasShutdownStarted || !IsLoaded)
            return;

        if (progress.NewCandidates.Count > 0)
        {
            using (_view.DeferRefresh())
            {
                foreach (var candidate in progress.NewCandidates)
                    _results.Add(candidate);
            }

            AddExtensionLabels(progress.NewCandidates);
        }

        var folder = string.IsNullOrWhiteSpace(progress.CurrentPath) ? string.Empty : " Текущая папка: " + progress.CurrentPath;
        StatusText.Text = "Сканирование… просмотрено файлов: " + progress.FilesEnumerated.ToString("N0")
            + ", найдено: " + progress.CandidatesFound.ToString("N0") + "." + folder;
        StatusText.ToolTip = progress.CurrentPath;
        UpdateTotals();
    }

    private void ApplyFinalStatus(ScanResult result)
    {
        var seen = result.FilesEnumerated.ToString("N0");
        var found = result.Candidates.Count.ToString("N0");
        var skipped = result.DirectoriesSkipped.ToString("N0");
        StatusText.Text = result.Status switch
        {
            ScanStatus.FolderNotFound => "Папка не найдена. Проверьте путь.",
            ScanStatus.AccessDenied => "Нет доступа к выбранной папке. Попробуйте запустить программу от имени администратора.",
            ScanStatus.InvalidPath => "Не удалось понять путь. Укажите, например, C:\\ или D:\\Фото.",
            ScanStatus.Cancelled => "Сканирование отменено. Показаны файлы, найденные до остановки. Просмотрено: "
                + seen + ", найдено: " + found + ", пропущено папок: " + skipped + ".",
            ScanStatus.Completed when result.RootExcluded =>
                "Эта папка входит в список исключений и не сканируется. Список можно поправить в exclusions.json.",
            _ => "Готово. Просмотрено файлов: " + seen + ", найдено: " + found + ", пропущено папок: " + skipped + "."
        };

        if (!string.IsNullOrWhiteSpace(result.Warning)
            && result.Status is ScanStatus.Completed or ScanStatus.Cancelled)
            StatusText.Text += " Предупреждение: " + result.Warning;
        if (!string.IsNullOrWhiteSpace(_rulesWarning))
            StatusText.Text += " " + _rulesWarning;

        UpdateTotals();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var rows = _view.Cast<FileCandidate>().ToList();
        if (rows.Count == 0)
        {
            StatusText.Text = "В таблице нет строк для экспорта. Снимите фильтр или дождитесь результатов.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить результаты",
            Filter = "CSV (*.csv)|*.csv",
            FileName = "disk-analyzer-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".csv",
            AddExtension = true,
            DefaultExt = ".csv",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            CsvExporter.Export(dialog.FileName, rows);
            StatusText.Text = "Экспортировано строк: " + rows.Count.ToString("N0") + ". Файл: " + dialog.FileName
                + ". Разделитель — точка с запятой, кодировка UTF-8.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Не удалось сохранить CSV: " + ex.Message;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not FileCandidate candidate)
        {
            StatusText.Text = "Выберите файл в таблице.";
            return;
        }

        OpenCandidate(candidate);
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var row = ItemsControl.ContainerFromElement(ResultsGrid, source) as DataGridRow;
        if (row?.Item is FileCandidate candidate)
            OpenCandidate(candidate);
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenButton.IsEnabled = ResultsGrid.SelectedItem is FileCandidate;
    }

    private void OpenCandidate(FileCandidate candidate)
    {
        try
        {
            ExplorerSelector.OpenContainingFolder(candidate.FullPath);
            StatusText.Text = "Открыта папка с выделенным файлом: " + candidate.FullPath;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось открыть проводник: " + ex.Message;
        }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterText = FilterBox.Text.Trim();
        if (!_ready)
            return;
        _view.Refresh();
        UpdateTotals();
    }

    private void ExtensionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingExtensions)
            return;
        _selectedExtension = ExtensionBox.SelectedItem as string ?? AllExtensions;
        if (!_ready)
            return;
        _view.Refresh();
        UpdateTotals();
    }

    private bool FilterCandidate(object item)
    {
        if (item is not FileCandidate file)
            return false;

        var selected = _selectedExtension;
        if (!selected.Equals(AllExtensions, StringComparison.Ordinal))
        {
            var label = file.Extension.Length == 0 ? NoExtension : file.Extension;
            if (!label.Equals(selected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var text = _filterText;
        if (text.Length == 0)
            return true;

        return file.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || file.Directory.Contains(text, StringComparison.OrdinalIgnoreCase)
            || file.Extension.Contains(text, StringComparison.OrdinalIgnoreCase)
            || file.FullPath.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void AddExtensionLabels(IEnumerable<FileCandidate> files)
    {
        var added = false;
        foreach (var file in files)
        {
            var label = file.Extension.Length == 0 ? NoExtension : file.Extension;
            if (_extensionLabels.Add(label))
                added = true;
        }

        if (!added)
            return;

        var selected = ExtensionBox.SelectedItem as string ?? AllExtensions;
        _updatingExtensions = true;
        ExtensionBox.Items.Clear();
        ExtensionBox.Items.Add(AllExtensions);
        foreach (var label in _extensionLabels.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            ExtensionBox.Items.Add(label);
        ExtensionBox.SelectedItem = ExtensionBox.Items.Contains(selected) ? selected : AllExtensions;
        _updatingExtensions = false;
    }

    private void UpdateTotals()
    {
        var allBytes = _results.Sum(item => item.SizeBytes);
        var shown = _view.Cast<FileCandidate>().ToList();
        var shownBytes = shown.Sum(item => item.SizeBytes);
        TotalsText.Text = shown.Count == _results.Count
            ? "Найдено: " + _results.Count.ToString("N0") + " файлов, общий размер " + SizeFormatter.Format(allBytes)
            : "Найдено: " + _results.Count.ToString("N0") + " файлов, общий размер " + SizeFormatter.Format(allBytes)
                + ". Показано: " + shown.Count.ToString("N0") + ", размер " + SizeFormatter.Format(shownBytes);
        ExportButton.IsEnabled = shown.Count > 0;
    }

    private bool CommitAgeBox()
    {
        if (!int.TryParse(AgeBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && !int.TryParse(AgeBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            StatusText.Text = "Укажите возраст целым числом дней от " + MinAgeDays + " до " + MaxAgeDays + ".";
            AgeBox.Text = ((int)AgeSlider.Value).ToString(CultureInfo.InvariantCulture);
            return false;
        }

        value = Math.Clamp(value, MinAgeDays, MaxAgeDays);

        _syncingAge = true;
        AgeSlider.Value = value;
        AgeBox.Text = value.ToString(CultureInfo.InvariantCulture);
        _syncingAge = false;
        return true;
    }

    private bool CommitSizeBox()
    {
        if (!int.TryParse(SizeBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && !int.TryParse(SizeBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            StatusText.Text = "Укажите минимальный размер целым числом мегабайт от " + MinSizeMb + " до " + MaxSizeMb + ".";
            SizeBox.Text = ((int)SizeSlider.Value).ToString(CultureInfo.InvariantCulture);
            return false;
        }

        value = Math.Clamp(value, MinSizeMb, MaxSizeMb);

        _syncingSize = true;
        SizeSlider.Value = value;
        SizeBox.Text = value.ToString(CultureInfo.InvariantCulture);
        _syncingSize = false;
        return true;
    }

    private void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
        BrowseButton.IsEnabled = !scanning;
        DriveBox.IsEnabled = !scanning;
        PathBox.IsEnabled = !scanning;
        AgeSlider.IsEnabled = !scanning;
        AgeBox.IsEnabled = !scanning;
        SizeSlider.IsEnabled = !scanning;
        SizeBox.IsEnabled = !scanning;
        ScanProgress.IsIndeterminate = scanning;
        ScanProgress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
    }

    private ExclusionRules LoadRules()
    {
        var path = ExclusionFileLocation.FindExisting() ?? ExclusionFileLocation.TryWriteDefaultBesideExe();
        RulesPathText.Text = path is null
            ? "Используются встроенные правила исключений."
            : "Файл исключений (его можно править, изменения применятся при следующем сканировании): " + path;

        _rulesWarning = null;
        if (path is null)
            return ExclusionRules.LoadDefault();

        try
        {
            return ExclusionRules.Load(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _rulesWarning = "Не удалось прочитать exclusions.json (" + ex.Message + "). Используются встроенные правила.";
            StatusText.Text = _rulesWarning;
            return ExclusionRules.LoadDefault();
        }
    }

    private void ShowLastAccessNotice(LastAccessProbeResult probe)
    {
        switch (probe.Reliability)
        {
            case LastAccessReliability.UpdatesDisabled:
                NoticeText.Text = "Внимание: Windows не обновляет время последнего доступа к файлам"
                    + Detail(probe)
                    + ". «Последнее использование» — это более поздняя из даты доступа и даты изменения, но дата доступа часто остаётся старой. Файл, который недавно открывали и не сохраняли, может попасть в список как давно неиспользуемый. Программа эту настройку Windows не меняет.";
                NoticePanel.Visibility = Visibility.Visible;
                break;
            case LastAccessReliability.Unknown:
                NoticeText.Text = "Внимание: не удалось проверить, обновляет ли Windows время последнего доступа (реестр NtfsDisableLastAccessUpdate и команда fsutil behavior query disablelastaccess). Если обновление выключено, недавно открытые файлы могут выглядеть как давно неиспользуемые.";
                NoticePanel.Visibility = Visibility.Visible;
                break;
            default:
                NoticePanel.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private static string Detail(LastAccessProbeResult probe)
    {
        if (probe.Source is null || probe.RawValue is null)
            return string.Empty;

        var mode = probe.RawValue.Value & 0x3;
        var source = probe.Source == "registry"
            ? "параметр реестра NtfsDisableLastAccessUpdate"
            : "команда fsutil behavior query disablelastaccess";
        return " (" + source + " = " + probe.RawValue.Value.ToString(CultureInfo.InvariantCulture) + ", режим " + mode.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private sealed record DriveChoice(string Root, string Label)
    {
        public override string ToString() => Label;
    }
}
