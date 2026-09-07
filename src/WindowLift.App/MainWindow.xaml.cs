using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using WindowLift.App.Services;
using WindowLift.Core;

namespace WindowLift.App;

public partial class MainWindow : Window
{
    private readonly WindowMonitor _monitor;

    public MainWindow()
    {
        InitializeComponent();

        _monitor = new WindowMonitor(
            new WindowsWindowService(),
            Dispatcher,
            GetOptions);
        _monitor.Updated += OnMonitorUpdated;

        TaskbarOverlapCheckBox.Checked += Option_OnChanged;
        TaskbarOverlapCheckBox.Unchecked += Option_OnChanged;
        TitleBarLiftCheckBox.Checked += Option_OnChanged;
        TitleBarLiftCheckBox.Unchecked += Option_OnChanged;
        ShrinkToFitCheckBox.Checked += Option_OnChanged;
        ShrinkToFitCheckBox.Unchecked += Option_OnChanged;
        Loaded += MainWindow_OnLoaded;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _monitor.Start();
            UpdateButtons();
        }
        catch (Win32Exception ex)
        {
            SetStatus(ex.Message, isError: true);
            UpdateButtons();
        }
    }

    private void MonitorButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_monitor.IsRunning)
            {
                _monitor.Stop();
            }
            else
            {
                _monitor.Start();
            }
        }
        catch (Win32Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }

        UpdateButtons();
    }

    private void ScanButton_OnClick(object sender, RoutedEventArgs e) => _monitor.ScanNow();

    private void Option_OnChanged(object sender, RoutedEventArgs e) => _monitor.OptionsChanged();

    private WindowAdjustmentOptions GetOptions() =>
        new(
            AllowTaskbarOverlap: TaskbarOverlapCheckBox.IsChecked == true,
            AllowTitleBarLift: TitleBarLiftCheckBox.IsChecked == true,
            ShrinkToFit: ShrinkToFitCheckBox.IsChecked == true,
            TitleBarHeight: 0);

    private void OnMonitorUpdated(WindowMonitorUpdate update) =>
        SetStatus(update.Message, update.IsError);

    private void UpdateButtons()
    {
        MonitorButton.Content = _monitor.IsRunning ? "Stop monitoring" : "Start monitoring";
        ScanButton.IsEnabled = _monitor.IsRunning;
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : Brushes.Black;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _monitor.Dispose();
        base.OnClosing(e);
    }
}
