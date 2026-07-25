using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GraphSketcher.App.Controls;
using GraphSketcher.App.Dialogs;
using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;
using GraphPoint = GraphSketcher.Core.Models.DataPoint;

namespace GraphSketcher.App;

public sealed partial class MainWindow : Window
{
    private static readonly string[] SeriesPalette =
    [
        "#2563EB",
        "#DC2626",
        "#059669",
        "#7C3AED",
        "#D97706",
        "#0891B2",
        "#DB2777",
        "#4B5563",
    ];

    private GraphDocument _document = CreateNewDocument();
    private HistoryManager _history;
    private string? _currentPath;
    private bool _isDirty;
    private bool _updatingInspector;
    private bool _allowClose;

    public MainWindow()
    {
        _history = new HistoryManager(_document);
        InitializeComponent();

        Canvas.Document = _document;
        Canvas.DocumentChanged += Canvas_DocumentChanged;
        Canvas.SelectionChanged += (_, _) => UpdateStatusForSelection();
        Canvas.CoordinatesChanged += (_, e) =>
            CoordinateText.Text = $"x: {FormatCoordinate(e.X)}    y: {FormatCoordinate(e.Y)}";
        Canvas.AnnotationRequested += Canvas_AnnotationRequested;

        Closing += MainWindow_Closing;
        KeyDown += MainWindow_KeyDown;
        RefreshAll();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        UndoMenuItem = Required<MenuItem>(nameof(UndoMenuItem));
        RedoMenuItem = Required<MenuItem>(nameof(RedoMenuItem));
        SelectToolButton = Required<RadioButton>(nameof(SelectToolButton));
        Canvas = Required<GraphCanvas>(nameof(Canvas));
        InspectorTabs = Required<TabControl>(nameof(InspectorTabs));
        SeriesList = Required<ListBox>(nameof(SeriesList));
        SeriesNameBox = Required<TextBox>(nameof(SeriesNameBox));
        SeriesColorBox = Required<TextBox>(nameof(SeriesColorBox));
        SeriesLineWidthBox = Required<NumericUpDown>(nameof(SeriesLineWidthBox));
        SeriesLineModeBox = Required<ComboBox>(nameof(SeriesLineModeBox));
        SeriesMarkerBox = Required<ComboBox>(nameof(SeriesMarkerBox));
        SeriesMarkerSizeBox = Required<NumericUpDown>(nameof(SeriesMarkerSizeBox));
        SeriesFillBox = Required<CheckBox>(nameof(SeriesFillBox));
        SeriesStatsText = Required<TextBlock>(nameof(SeriesStatsText));
        AxisSelector = Required<ComboBox>(nameof(AxisSelector));
        AxisTitleBox = Required<TextBox>(nameof(AxisTitleBox));
        AxisMinimumBox = Required<NumericUpDown>(nameof(AxisMinimumBox));
        AxisMaximumBox = Required<NumericUpDown>(nameof(AxisMaximumBox));
        AxisScaleBox = Required<ComboBox>(nameof(AxisScaleBox));
        AxisTickSpacingBox = Required<NumericUpDown>(nameof(AxisTickSpacingBox));
        AxisVisibleBox = Required<CheckBox>(nameof(AxisVisibleBox));
        AxisGridBox = Required<CheckBox>(nameof(AxisGridBox));
        AxisTicksBox = Required<CheckBox>(nameof(AxisTicksBox));
        GraphTitleBox = Required<TextBox>(nameof(GraphTitleBox));
        BackgroundColorBox = Required<TextBox>(nameof(BackgroundColorBox));
        ShowLegendBox = Required<CheckBox>(nameof(ShowLegendBox));
        LegendPositionBox = Required<ComboBox>(nameof(LegendPositionBox));
        CanvasWidthBox = Required<NumericUpDown>(nameof(CanvasWidthBox));
        CanvasHeightBox = Required<NumericUpDown>(nameof(CanvasHeightBox));
        AnnotationList = Required<ListBox>(nameof(AnnotationList));
        StatusText = Required<TextBlock>(nameof(StatusText));
        CoordinateText = Required<TextBlock>(nameof(CoordinateText));

        TControl Required<TControl>(string name)
            where TControl : Control
        {
            return this.FindControl<TControl>(name)
                   ?? throw new InvalidOperationException(
                       $"The required control '{name}' was not created from MainWindow.axaml.");
        }
    }

    private async void New_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        SetDocument(CreateNewDocument(), null, isDirty: false);
        StatusText.Text = "New graph";
    }

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open graph",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("GraphSketcher documents")
                {
                    Patterns = ["*.graphsketch", "*.ograph"],
                },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        try
        {
            GraphDocument opened;
            var warnings = Array.Empty<string>();
            var openedLegacyDocument = false;
            await using var stream = await file.OpenReadAsync();
            if (string.Equals(Path.GetExtension(file.Name), ".ograph", StringComparison.OrdinalIgnoreCase))
            {
                var result = OGraphImporter.Import(stream);
                opened = result.Document;
                warnings = result.Warnings.ToArray();
                openedLegacyDocument = true;
            }
            else
            {
                opened = await DocumentSerializer.LoadAsync(stream);
            }

            SetDocument(
                opened,
                openedLegacyDocument || !file.Path.IsFile ? null : file.Path.LocalPath,
                isDirty: openedLegacyDocument);
            StatusText.Text = warnings.Length == 0
                ? $"Opened {file.Name}"
                : $"Opened {file.Name} with {warnings.Length} compatibility warning(s)";

            if (warnings.Length > 0)
            {
                await MessageDialog.ShowAsync(
                    this,
                    "Legacy document compatibility",
                    "The graph was opened, but some original features are not editable yet:\n\n" +
                    string.Join("\n", warnings.Take(8).Select(warning => $"• {warning}")) +
                    (warnings.Length > 8 ? $"\n• …and {warnings.Length - 8} more" : string.Empty));
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Could not open graph", exception.Message);
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentAsync(forcePicker: false);
    }

    private async void SaveAs_Click(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentAsync(forcePicker: true);
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new DataImportDialog();
        var text = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var result = DelimitedDataImporter.Import(text);
            if (result.Series.Count == 0)
            {
                await MessageDialog.ShowAsync(
                    this,
                    "No numeric data found",
                    "GraphSketcher could not find numeric columns to plot. Include at least one numeric column.");
                return;
            }

            var shouldReplaceDefault =
                _document.Series.Count == 1 &&
                _document.Series[0].Points.Count == 0 &&
                _document.Annotations.Count == 0;

            if (shouldReplaceDefault)
            {
                _document.Series.Clear();
            }

            for (var index = 0; index < result.Series.Count; index++)
            {
                var series = result.Series[index];
                series.Color = SeriesPalette[(_document.Series.Count + index) % SeriesPalette.Length];
                _document.Series.Add(series);
            }

            if (_document.XAxis.Title is "X" or "")
            {
                _document.XAxis.Title = "X";
            }

            Canvas.SelectedSeriesIndex = Math.Max(0, _document.Series.Count - result.Series.Count);
            ScaleToFitCore(recordHistory: false);
            CommitDocumentChange();
            RefreshAll();

            var issueText = result.Issues.Count > 0
                ? $" {result.Issues.Count} row or cell warning(s) were skipped."
                : string.Empty;
            StatusText.Text =
                $"Imported {result.Series.Count} series from {result.RowsRead} row(s).{issueText}";
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            await MessageDialog.ShowAsync(this, "Could not import data", exception.Message);
        }
    }

    private async void ExportSvg_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export SVG",
            SuggestedFileName = $"{SafeFileName(_document.Title)}.svg",
            DefaultExtension = "svg",
            FileTypeChoices =
            [
                new FilePickerFileType("Scalable Vector Graphics") { Patterns = ["*.svg"] },
            ],
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await SvgExporter.ExportAsync(_document, stream);
            StatusText.Text = $"Exported {file.Name}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Could not export SVG", exception.Message);
        }
    }

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export data as CSV",
            SuggestedFileName = $"{SafeFileName(_document.Title)}.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("Comma-separated values") { Patterns = ["*.csv"] },
            ],
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteLineAsync("series,x,y,x_error,y_error,label");
            foreach (var series in _document.Series)
            {
                foreach (var point in series.Points)
                {
                    await writer.WriteLineAsync(string.Join(
                        ",",
                        Csv(series.Name),
                        point.X.ToString("G17", CultureInfo.InvariantCulture),
                        point.Y.ToString("G17", CultureInfo.InvariantCulture),
                        point.XError?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                        point.YError?.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                        Csv(point.Label ?? string.Empty)));
                }
            }

            StatusText.Text = $"Exported {file.Name}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Could not export CSV", exception.Message);
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        if (!_history.CanUndo)
        {
            StatusText.Text = "Nothing to undo";
            return;
        }

        var previous = _history.Undo();
        ApplyHistoryDocument(previous, "Undo");
    }

    private void Redo_Click(object? sender, RoutedEventArgs e)
    {
        if (!_history.CanRedo)
        {
            StatusText.Text = "Nothing to redo";
            return;
        }

        var next = _history.Redo();
        ApplyHistoryDocument(next, "Redo");
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        Canvas.SelectAllPoints();
    }

    private void DeleteSelection_Click(object? sender, RoutedEventArgs e)
    {
        Canvas.DeleteSelection();
    }

    private void ScaleToFit_Click(object? sender, RoutedEventArgs e)
    {
        ScaleToFitCore(recordHistory: true);
        RefreshAll();
        StatusText.Text = "Scaled axes to fit all visible data";
    }

    private void ToggleGrid_Click(object? sender, RoutedEventArgs e)
    {
        var show = !(_document.XAxis.ShowGridLines && _document.YAxis.ShowGridLines);
        _document.XAxis.ShowGridLines = show;
        _document.YAxis.ShowGridLines = show;
        CommitDocumentChange();
        RefreshAll();
        StatusText.Text = show ? "Grid shown" : "Grid hidden";
    }

    private async void BestFit_Click(object? sender, RoutedEventArgs e)
    {
        var series = SelectedSeries;
        if (series is null || series.Points.Count < 2)
        {
            await MessageDialog.ShowAsync(
                this,
                "Best-fit line",
                "Select a series containing at least two points.");
            return;
        }

        try
        {
            var fit = GraphStatistics.LinearRegression(series.Points);
            var xMinimum = series.Points.Min(point => point.X);
            var xMaximum = series.Points.Max(point => point.X);
            if (Math.Abs(xMaximum - xMinimum) < double.Epsilon)
            {
                await MessageDialog.ShowAsync(
                    this,
                    "Best-fit line",
                    "A linear fit requires at least two different X values.");
                return;
            }

            var fitSeries = new GraphSeries
            {
                Name = $"Fit: y = {fit.Slope:G4}x {(fit.Intercept < 0 ? "−" : "+")} {Math.Abs(fit.Intercept):G4}  (R² {fit.RSquared:G4})",
                Color = SeriesPalette[_document.Series.Count % SeriesPalette.Length],
                LineMode = LineMode.Straight,
                LineStyle = LineStyle.Dashed,
                MarkerShape = MarkerShape.None,
                StrokeWidth = 2,
                Points =
                [
                    new GraphPoint(xMinimum, fit.Predict(xMinimum)),
                    new GraphPoint(xMaximum, fit.Predict(xMaximum)),
                ],
            };
            _document.Series.Add(fitSeries);
            Canvas.SelectedSeriesIndex = _document.Series.Count - 1;
            CommitDocumentChange();
            RefreshAll();
            StatusText.Text = $"Added best-fit line; R² = {fit.RSquared:G5}";
        }
        catch (ArgumentException exception)
        {
            await MessageDialog.ShowAsync(this, "Best-fit line", exception.Message);
        }
    }

    private void AddSeries_Click(object? sender, RoutedEventArgs e)
    {
        var series = new GraphSeries
        {
            Name = $"Series {_document.Series.Count + 1}",
            Color = SeriesPalette[_document.Series.Count % SeriesPalette.Length],
        };
        _document.Series.Add(series);
        Canvas.SelectedSeriesIndex = _document.Series.Count - 1;
        CommitDocumentChange();
        RefreshAll();
        StatusText.Text = $"Added {series.Name}";
    }

    private void RemoveSeries_Click(object? sender, RoutedEventArgs e)
    {
        if (SeriesList.SelectedIndex < 0 || SeriesList.SelectedIndex >= _document.Series.Count)
        {
            return;
        }

        var name = _document.Series[SeriesList.SelectedIndex].Name;
        _document.Series.RemoveAt(SeriesList.SelectedIndex);
        if (_document.Series.Count == 0)
        {
            _document.Series.Add(new GraphSeries
            {
                Name = "Series 1",
                Color = SeriesPalette[0],
            });
        }

        Canvas.SelectedSeriesIndex = Math.Clamp(SeriesList.SelectedIndex, 0, _document.Series.Count - 1);
        CommitDocumentChange();
        RefreshAll();
        StatusText.Text = $"Removed {name}";
    }

    private async void ClearData_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await MessageDialog.ShowAsync(
            this,
            "Clear all data?",
            "This removes every data series and annotation from the graph. You can undo the change.",
            isConfirmation: true);
        if (!confirmed)
        {
            return;
        }

        _document.Series =
        [
            new GraphSeries { Name = "Series 1", Color = SeriesPalette[0] },
        ];
        _document.Annotations.Clear();
        Canvas.SelectedSeriesIndex = 0;
        CommitDocumentChange();
        RefreshAll();
        StatusText.Text = "Cleared graph data";
    }

    private void LightTheme_Click(object? sender, RoutedEventArgs e)
    {
        SetTheme(ThemeVariant.Light);
    }

    private void DarkTheme_Click(object? sender, RoutedEventArgs e)
    {
        SetTheme(ThemeVariant.Dark);
    }

    private void SystemTheme_Click(object? sender, RoutedEventArgs e)
    {
        SetTheme(ThemeVariant.Default);
    }

    private async void Shortcuts_Click(object? sender, RoutedEventArgs e)
    {
        await MessageDialog.ShowAsync(
            this,
            "Keyboard shortcuts",
            """
            Ctrl+N  New graph        Ctrl+O  Open
            Ctrl+S  Save             Ctrl+I  Import data
            Ctrl+E  Export SVG       Ctrl+0  Scale to fit
            Ctrl+Z  Undo             Ctrl+Y  Redo
            Ctrl+A  Select points    Delete  Remove selection

            V  Select tool     P  Point tool
            D  Draw tool       T  Text tool
            Escape  Clear selection
            """);
    }

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        await MessageDialog.ShowAsync(
            this,
            "About GraphSketcher for Linux",
            """
            GraphSketcher for Linux
            Preview 0.1.0

            An independent community port of the open-source GraphSketcher app.

            Graph Sketcher was created by Robin Stewart in 2007. The Omni Group further developed the Mac and iPad applications. Original code and behavior are used under the Omni Source License 2007.

            This port is not currently endorsed by or affiliated with the original maintainers or The Omni Group.
            """);
    }

    private void ToolMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<CanvasTool>(tag, ignoreCase: true, out var tool))
        {
            return;
        }

        Canvas.Tool = tool;
        StatusText.Text = tool switch
        {
            CanvasTool.Select => "Select and drag points; drag empty space for a marquee",
            CanvasTool.Point => "Click the graph to add a point",
            CanvasTool.Draw => "Click to add connected points; double-click to finish",
            CanvasTool.Text => "Click the graph to place text",
            _ => "Ready",
        };
    }

    private void SeriesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingInspector || SeriesList.SelectedIndex < 0)
        {
            return;
        }

        Canvas.SelectedSeriesIndex = SeriesList.SelectedIndex;
        PopulateSeriesInspector();
    }

    private void SeriesEditor_Changed(object? sender, FocusChangedEventArgs e) =>
        ApplySeriesEditor();

    private void SeriesEditor_Changed(object? sender, NumericUpDownValueChangedEventArgs e) =>
        ApplySeriesEditor();

    private void SeriesEditor_Changed(object? sender, SelectionChangedEventArgs e) =>
        ApplySeriesEditor();

    private void SeriesEditor_Changed(object? sender, RoutedEventArgs e) =>
        ApplySeriesEditor();

    private void ApplySeriesEditor()
    {
        if (_updatingInspector || SelectedSeries is not { } series)
        {
            return;
        }

        var name = SeriesNameBox.Text?.Trim();
        series.Name = string.IsNullOrEmpty(name) ? "Series" : name;

        var color = NormalizeColor(SeriesColorBox.Text);
        if (color is not null)
        {
            series.Color = color;
        }

        series.StrokeWidth = DecimalToDouble(SeriesLineWidthBox.Value, series.StrokeWidth);
        series.MarkerSize = DecimalToDouble(SeriesMarkerSizeBox.Value, series.MarkerSize);
        series.LineMode = SelectedEnum(SeriesLineModeBox, series.LineMode);
        series.MarkerShape = SelectedEnum(SeriesMarkerBox, series.MarkerShape);
        if (series.LineMode == LineMode.None)
        {
            series.LineStyle = LineStyle.None;
        }
        else if (series.LineStyle == LineStyle.None)
        {
            series.LineStyle = LineStyle.Solid;
        }
        series.FillArea = SeriesFillBox.IsChecked == true;

        CommitDocumentChange();
        RefreshSeriesList();
        Canvas.Refresh();
        PopulateSeriesInspector();
    }

    private void AxisSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingInspector)
        {
            PopulateAxisInspector();
        }
    }

    private void AxisEditor_Changed(object? sender, FocusChangedEventArgs e) =>
        ApplyAxisEditor();

    private void AxisEditor_Changed(object? sender, NumericUpDownValueChangedEventArgs e) =>
        ApplyAxisEditor();

    private void AxisEditor_Changed(object? sender, SelectionChangedEventArgs e) =>
        ApplyAxisEditor();

    private void AxisEditor_Changed(object? sender, RoutedEventArgs e) =>
        ApplyAxisEditor();

    private void ApplyAxisEditor()
    {
        if (_updatingInspector)
        {
            return;
        }

        var axis = SelectedAxis;
        axis.Title = AxisTitleBox.Text?.Trim() ?? string.Empty;
        axis.Scale = SelectedEnum(AxisScaleBox, axis.Scale);
        axis.ShowAxisLine = AxisVisibleBox.IsChecked == true;
        axis.ShowGridLines = AxisGridBox.IsChecked == true;
        axis.ShowTickLabels = AxisTicksBox.IsChecked == true;
        axis.TickSpacing = DecimalToNullableDouble(AxisTickSpacingBox.Value);

        var minimum = DecimalToNullableDouble(AxisMinimumBox.Value);
        var maximum = DecimalToNullableDouble(AxisMaximumBox.Value);
        if (minimum is { } low && maximum is { } high && low < high)
        {
            axis.Minimum = low;
            axis.Maximum = high;
        }

        if (axis.Scale == AxisScale.Logarithmic)
        {
            if (axis.Minimum is null or <= 0)
            {
                axis.Minimum = 0.1;
            }

            if (axis.Maximum is null or <= 0 || axis.Maximum <= axis.Minimum)
            {
                axis.Maximum = axis.Minimum * 100;
            }
        }

        CommitDocumentChange();
        Canvas.Refresh();
    }

    private void GraphEditor_Changed(object? sender, FocusChangedEventArgs e) =>
        ApplyGraphEditor();

    private void GraphEditor_Changed(object? sender, NumericUpDownValueChangedEventArgs e) =>
        ApplyGraphEditor();

    private void GraphEditor_Changed(object? sender, SelectionChangedEventArgs e) =>
        ApplyGraphEditor();

    private void GraphEditor_Changed(object? sender, RoutedEventArgs e) =>
        ApplyGraphEditor();

    private void ApplyGraphEditor()
    {
        if (_updatingInspector)
        {
            return;
        }

        var title = GraphTitleBox.Text?.Trim();
        _document.Title = string.IsNullOrEmpty(title) ? "Untitled Graph" : title;
        var color = NormalizeColor(BackgroundColorBox.Text);
        if (color is not null)
        {
            _document.Canvas.BackgroundColor = color;
        }

        _document.Canvas.ShowLegend = ShowLegendBox.IsChecked == true;
        _document.Canvas.LegendPosition = SelectedEnum(
            LegendPositionBox,
            _document.Canvas.LegendPosition);
        _document.Canvas.Width = DecimalToDouble(CanvasWidthBox.Value, _document.Canvas.Width);
        _document.Canvas.Height = DecimalToDouble(CanvasHeightBox.Value, _document.Canvas.Height);

        CommitDocumentChange();
        Canvas.Refresh();
        UpdateWindowTitle();
    }

    private void RemoveAnnotation_Click(object? sender, RoutedEventArgs e)
    {
        if (AnnotationList.SelectedIndex < 0 ||
            AnnotationList.SelectedIndex >= _document.Annotations.Count)
        {
            return;
        }

        _document.Annotations.RemoveAt(AnnotationList.SelectedIndex);
        CommitDocumentChange();
        RefreshAll();
        StatusText.Text = "Removed annotation";
    }

    private async void Canvas_AnnotationRequested(object? sender, AnnotationRequestedEventArgs e)
    {
        var dialog = new TextPromptDialog("Add text", "Annotation text");
        var text = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _document.Annotations.Add(new GraphAnnotation
        {
            Kind = AnnotationKind.Text,
            CoordinateSpace = AnnotationCoordinateSpace.Data,
            X = e.X,
            Y = e.Y,
            Text = text,
        });
        CommitDocumentChange();
        RefreshAll();
        SelectToolButton.IsChecked = true;
        StatusText.Text = "Added text annotation";
    }

    private void Canvas_DocumentChanged(object? sender, EventArgs e)
    {
        CommitDocumentChange();
        RefreshSeriesList();
        PopulateSeriesInspector();
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None ||
            FocusManager?.GetFocusedElement() is TextBox or NumericUpDown)
        {
            return;
        }

        var tag = e.Key switch
        {
            Key.V => "Select",
            Key.P => "Point",
            Key.D => "Draw",
            Key.T => "Text",
            _ => null,
        };
        if (tag is null)
        {
            return;
        }

        var button = this.GetVisualDescendants()
            .OfType<RadioButton>()
            .FirstOrDefault(candidate => Equals(candidate.Tag, tag));
        if (button is not null)
        {
            button.IsChecked = true;
            e.Handled = true;
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || !_isDirty)
        {
            return;
        }

        e.Cancel = true;
        if (await ConfirmDiscardChangesAsync())
        {
            _allowClose = true;
            Close();
        }
    }

    private async Task<bool> SaveCurrentAsync(bool forcePicker)
    {
        var targetPath = forcePicker ? null : _currentPath;
        if (targetPath is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save graph",
                SuggestedFileName = $"{SafeFileName(_document.Title)}.graphsketch",
                DefaultExtension = "graphsketch",
                FileTypeChoices =
                [
                    new FilePickerFileType("GraphSketcher document")
                    {
                        Patterns = ["*.graphsketch"],
                    },
                ],
            });
            if (file is null)
            {
                return false;
            }

            targetPath = file.Path.IsFile ? file.Path.LocalPath : null;
            if (targetPath is null)
            {
                try
                {
                    await using var stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    await DocumentSerializer.SaveAsync(_document, stream);
                    _isDirty = false;
                    UpdateWindowTitle();
                    StatusText.Text = $"Saved {file.Name}";
                    return true;
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    await MessageDialog.ShowAsync(this, "Could not save graph", exception.Message);
                    return false;
                }
            }
        }

        try
        {
            await DocumentSerializer.SaveAsync(_document, targetPath);
            _currentPath = targetPath;
            _isDirty = false;
            UpdateWindowTitle();
            StatusText.Text = $"Saved {Path.GetFileName(targetPath)}";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Could not save graph", exception.Message);
            return false;
        }
    }

    public async Task OpenDocumentPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var openedLegacyDocument = string.Equals(
                Path.GetExtension(path),
                ".ograph",
                StringComparison.OrdinalIgnoreCase);
            GraphDocument opened;
            IReadOnlyList<string> warnings = [];
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (openedLegacyDocument)
            {
                var result = OGraphImporter.Import(stream);
                opened = result.Document;
                warnings = result.Warnings;
            }
            else
            {
                opened = await DocumentSerializer.LoadAsync(stream);
            }

            SetDocument(opened, openedLegacyDocument ? null : Path.GetFullPath(path), openedLegacyDocument);
            StatusText.Text = warnings.Count == 0
                ? $"Opened {Path.GetFileName(path)}"
                : $"Opened {Path.GetFileName(path)} with {warnings.Count} compatibility warning(s)";
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Could not open graph", exception.Message);
        }
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_isDirty)
        {
            return true;
        }

        return await MessageDialog.ShowAsync(
            this,
            "Discard unsaved changes?",
            "This graph has unsaved changes. Continue without saving?",
            isConfirmation: true);
    }

    private void SetDocument(GraphDocument document, string? path, bool isDirty)
    {
        _document = document;
        _currentPath = path;
        _isDirty = isDirty;
        _history.Reset(_document);
        Canvas.Document = _document;
        Canvas.SelectedSeriesIndex = 0;
        RefreshAll();
    }

    private void ApplyHistoryDocument(GraphDocument document, string status)
    {
        _document = document;
        Canvas.Document = _document;
        _isDirty = true;
        RefreshAll();
        StatusText.Text = status;
    }

    private void CommitDocumentChange()
    {
        _document.EnsureValid();
        _history.Record(_document);
        _isDirty = true;
        UpdateWindowTitle();
        UpdateHistoryControls();
        Canvas.Refresh();
    }

    private void ScaleToFitCore(bool recordHistory)
    {
        var visiblePoints = _document.Series
            .Where(series => series.IsVisible)
            .SelectMany(series => series.Points)
            .ToList();
        if (visiblePoints.Count == 0)
        {
            return;
        }

        ApplyRange(_document.XAxis, visiblePoints.Select(point => point.X));
        ApplyRange(_document.YAxis, visiblePoints.Select(point => point.Y));
        if (recordHistory)
        {
            CommitDocumentChange();
        }
    }

    private static void ApplyRange(AxisSettings axis, IEnumerable<double> values)
    {
        var range = GraphMath.AutoScale(
            values,
            axis.Scale,
            axis.DesiredTickCount,
            includeZero: axis.Scale == AxisScale.Linear);
        axis.Minimum = range.Minimum;
        axis.Maximum = range.Maximum;
        axis.TickSpacing = null;
    }

    private void RefreshAll()
    {
        _updatingInspector = true;
        try
        {
            RefreshSeriesList();
            PopulateSeriesInspector();
            PopulateAxisInspector();
            PopulateGraphInspector();
            AnnotationList.ItemsSource = null;
            AnnotationList.ItemsSource = _document.Annotations;
        }
        finally
        {
            _updatingInspector = false;
        }

        Canvas.Refresh();
        UpdateWindowTitle();
        UpdateHistoryControls();
    }

    private void RefreshSeriesList()
    {
        var selectedIndex = Canvas.SelectedSeriesIndex;
        SeriesList.ItemsSource = null;
        SeriesList.ItemsSource = _document.Series;
        SeriesList.SelectedIndex = _document.Series.Count == 0
            ? -1
            : Math.Clamp(selectedIndex, 0, _document.Series.Count - 1);
    }

    private void PopulateSeriesInspector()
    {
        var wasUpdating = _updatingInspector;
        _updatingInspector = true;
        try
        {
            var series = SelectedSeries;
            var enabled = series is not null;
            SeriesNameBox.IsEnabled = enabled;
            SeriesColorBox.IsEnabled = enabled;
            SeriesLineWidthBox.IsEnabled = enabled;
            SeriesLineModeBox.IsEnabled = enabled;
            SeriesMarkerBox.IsEnabled = enabled;
            SeriesMarkerSizeBox.IsEnabled = enabled;
            SeriesFillBox.IsEnabled = enabled;

            if (series is null)
            {
                SeriesNameBox.Text = string.Empty;
                SeriesStatsText.Text = "No series selected";
                return;
            }

            SeriesNameBox.Text = series.Name;
            SeriesColorBox.Text = series.Color;
            SeriesLineWidthBox.Value = ToDecimal(series.StrokeWidth);
            SeriesMarkerSizeBox.Value = ToDecimal(series.MarkerSize);
            SelectTag(SeriesLineModeBox, series.LineMode.ToString());
            SelectTag(SeriesMarkerBox, series.MarkerShape.ToString());
            SeriesFillBox.IsChecked = series.FillArea;
            SeriesStatsText.Text = DescribeSeries(series);
        }
        finally
        {
            _updatingInspector = wasUpdating;
        }
    }

    private void PopulateAxisInspector()
    {
        var wasUpdating = _updatingInspector;
        _updatingInspector = true;
        try
        {
            var axis = SelectedAxis;
            AxisTitleBox.Text = axis.Title;
            AxisMinimumBox.Value = ToDecimal(axis.Minimum);
            AxisMaximumBox.Value = ToDecimal(axis.Maximum);
            AxisTickSpacingBox.Value = ToDecimal(axis.TickSpacing ?? 0);
            AxisVisibleBox.IsChecked = axis.ShowAxisLine;
            AxisGridBox.IsChecked = axis.ShowGridLines;
            AxisTicksBox.IsChecked = axis.ShowTickLabels;
            SelectTag(AxisScaleBox, axis.Scale.ToString());
        }
        finally
        {
            _updatingInspector = wasUpdating;
        }
    }

    private void PopulateGraphInspector()
    {
        GraphTitleBox.Text = _document.Title;
        BackgroundColorBox.Text = _document.Canvas.BackgroundColor;
        ShowLegendBox.IsChecked = _document.Canvas.ShowLegend;
        CanvasWidthBox.Value = ToDecimal(_document.Canvas.Width);
        CanvasHeightBox.Value = ToDecimal(_document.Canvas.Height);
        SelectTag(LegendPositionBox, _document.Canvas.LegendPosition.ToString());
    }

    private void UpdateHistoryControls()
    {
        UndoMenuItem.IsEnabled = _history.CanUndo;
        RedoMenuItem.IsEnabled = _history.CanRedo;
    }

    private void UpdateStatusForSelection()
    {
        if (SelectedSeries is not { } series)
        {
            StatusText.Text = "Ready";
            return;
        }

        StatusText.Text = Canvas.SelectedPointIndices.Count switch
        {
            0 => $"{series.Name}: {series.Points.Count} point(s)",
            1 => $"{series.Name}: 1 point selected",
            var count => $"{series.Name}: {count} points selected",
        };
        PopulateSeriesInspector();
    }

    private void UpdateWindowTitle()
    {
        var fileName = _currentPath is null ? _document.Title : Path.GetFileName(_currentPath);
        Title = $"{(_isDirty ? "● " : string.Empty)}{fileName} — GraphSketcher for Linux";
    }

    private GraphSeries? SelectedSeries =>
        SeriesList.SelectedIndex >= 0 && SeriesList.SelectedIndex < _document.Series.Count
            ? _document.Series[SeriesList.SelectedIndex]
            : null;

    private AxisSettings SelectedAxis =>
        SelectedTag(AxisSelector) == "Y" ? _document.YAxis : _document.XAxis;

    private static GraphDocument CreateNewDocument()
    {
        return new GraphDocument
        {
            Title = "Untitled Graph",
            Canvas = new CanvasSettings
            {
                ShowLegend = true,
                LegendPosition = LegendPosition.TopRight,
            },
            XAxis = new AxisSettings
            {
                Title = "X",
                ShowGridLines = true,
            },
            YAxis = new AxisSettings
            {
                Title = "Y",
                ShowGridLines = true,
            },
            Series =
            [
                new GraphSeries
                {
                    Name = "Series 1",
                    Color = SeriesPalette[0],
                },
            ],
        };
    }

    private static string DescribeSeries(GraphSeries series)
    {
        if (series.Points.Count == 0)
        {
            return "No points yet. Choose Point or Draw, then click the graph.";
        }

        var xMinimum = series.Points.Min(point => point.X);
        var xMaximum = series.Points.Max(point => point.X);
        var yMinimum = series.Points.Min(point => point.Y);
        var yMaximum = series.Points.Max(point => point.Y);
        var summary =
            $"{series.Points.Count} point(s) · X {xMinimum:G4}–{xMaximum:G4} · Y {yMinimum:G4}–{yMaximum:G4}";

        if (series.Points.Count >= 2 && xMinimum != xMaximum)
        {
            try
            {
                var fit = GraphStatistics.LinearRegression(series.Points);
                summary += $"\nLinear fit: y = {fit.Slope:G4}x {(fit.Intercept < 0 ? "−" : "+")} {Math.Abs(fit.Intercept):G4}; R² = {fit.RSquared:G4}";
            }
            catch (ArgumentException)
            {
                // A descriptive summary is still useful if fitting is degenerate.
            }
        }

        return summary;
    }

    private static void SelectTag(SelectingItemsControl control, string tag)
    {
        for (var index = 0; index < control.ItemCount; index++)
        {
            if (control.Items[index] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                control.SelectedIndex = index;
                return;
            }
        }
    }

    private static string? SelectedTag(SelectingItemsControl control)
    {
        return control.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null;
    }

    private static TEnum SelectedEnum<TEnum>(SelectingItemsControl control, TEnum fallback)
        where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(SelectedTag(control), ignoreCase: true, out var value)
            ? value
            : fallback;
    }

    private static string? NormalizeColor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (!text.StartsWith('#'))
        {
            text = $"#{text}";
        }

        return text.Length is 4 or 5 or 7 or 9 &&
               text.Skip(1).All(Uri.IsHexDigit)
            ? text.ToUpperInvariant()
            : null;
    }

    private static decimal? ToDecimal(double? value)
    {
        if (value is null || !double.IsFinite(value.Value) ||
            value.Value < (double)decimal.MinValue ||
            value.Value > (double)decimal.MaxValue)
        {
            return null;
        }

        return (decimal)value.Value;
    }

    private static double DecimalToDouble(decimal? value, double fallback)
    {
        return value is null ? fallback : (double)value.Value;
    }

    private static double? DecimalToNullableDouble(decimal? value)
    {
        return value is null ? null : (double)value.Value;
    }

    private static string FormatCoordinate(double value)
    {
        return Math.Abs(value) is > 999_999 or (< 0.0001 and > 0)
            ? value.ToString("0.####E+0", CultureInfo.CurrentCulture)
            : value.ToString("0.####", CultureInfo.CurrentCulture);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "Untitled Graph" : result;
    }

    private static string Csv(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void SetTheme(ThemeVariant theme)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = theme;
        }
    }
}
