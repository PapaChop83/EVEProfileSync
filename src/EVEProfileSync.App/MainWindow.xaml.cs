using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace EVEProfileSync.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        _viewModel.BrowseForFolder();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            this,
            "This will apply the selected changes to the chosen targets. Continue?",
            "Confirm Sync",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _viewModel.SyncAsync();
            System.Windows.MessageBox.Show(this, _viewModel.StatusText, "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select backup export",
                Filter = "EVE Profile Sync Backup (*.eveprofilesyncbackup)|*.eveprofilesyncbackup|All files (*.*)|*.*",
                InitialDirectory = _viewModel.ExportFolderPath,
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var preview = await _viewModel.LoadRestorePreviewAsync(dialog.FileName);
            if (!ShowRestoreConfirmation(preview.PathsToRestore))
            {
                return;
            }

            await _viewModel.RestoreFromExportAsync(preview);
            System.Windows.MessageBox.Show(this, _viewModel.StatusText, "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ShowRestoreConfirmation(IReadOnlyList<string> pathsToRestore)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Confirm Restore",
            Width = 760,
            Height = 460,
            MinWidth = 620,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var layout = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new System.Windows.Controls.Button { Content = "Cancel", Width = 92, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var restoreButton = new System.Windows.Controls.Button { Content = "Restore", Width = 92, IsDefault = true };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;
        restoreButton.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(restoreButton);

        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);

        var header = new TextBlock
        {
            Text = "Restore will overwrite every file listed below.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var pathList = new System.Windows.Controls.ListBox
        {
            ItemsSource = pathsToRestore,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
        };
        layout.Children.Add(pathList);

        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var exportPath = await _viewModel.ExportCurrentProfileAsync();
            System.Windows.MessageBox.Show(
                this,
                $"Export created:{Environment.NewLine}{exportPath}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnSourceProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await _viewModel.RefreshCharacterNamesForCurrentProfileAsync();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnSelectAllTargetsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllTargets();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnDeselectAllTargetsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.DeselectAllTargets();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnSelectCharacterTargetsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectCharacterTargets();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnDeselectCharacterTargetsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.DeselectCharacterTargets();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnSelectAccountTargetsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAccountTargets();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnDeselectAccountTargetsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.DeselectAccountTargets();
        await _viewModel.RefreshPreviewAsync();
    }

    private async void OnRefreshAccountOverviewClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void OnSaveAccountLabelClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is AccountOverviewItemViewModel account)
        {
            _viewModel.SaveAccountLabel(account);
            await _viewModel.RefreshPreviewAsync();
        }
    }

    private async void OnSyncLayoutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SyncLayoutAsync();
            System.Windows.MessageBox.Show(this, _viewModel.StatusText, "UI Layout Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "UI Layout Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnSyncNeocomClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SyncNeocomAsync();
            System.Windows.MessageBox.Show(this, _viewModel.StatusText, "NEOCOM Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "NEOCOM Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
