using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppShelf.App.Services;
using AppShelf.Core.Detection;
using AppShelf.Core.Models;
using AppShelf.Core.Process;
using Forms = System.Windows.Forms;

namespace AppShelf.App.Dialogs;

/// <summary>Add/Edit dialog (spec §5.2): Local folder (with detection), Web URL, or Manual
/// command. Assigns a clash-free reserved port and persists via the GUI service.</summary>
public partial class AppEditWindow : Window
{
    private const int ModeLocal = 0;
    private const int ModeWebUrl = 1;

    private readonly GuiAppService _service;
    private readonly AppEntry? _existing;
    private string? _framework;

    public AppEditWindow(GuiAppService service, AppEntry? existing)
    {
        InitializeComponent();
        _service = service;
        _existing = existing;

        if (existing is not null)
        {
            Title = "Edit app";
            OkButton.Content = "Save changes";
            Prefill(existing);
        }

        ApplyMode();
    }

    private void Prefill(AppEntry e)
    {
        ModeCombo.SelectedIndex = e.IsUrlOnly ? ModeWebUrl : ModeLocal;
        NameBox.Text = e.Name;
        DirBox.Text = e.Dir ?? "";
        CmdBox.Text = e.Cmd ?? "";
        UrlBox.Text = e.Url;
        InstallBox.Text = e.InstallCmd ?? "";
        TagsBox.Text = string.Join(", ", e.Tags);
        PortBox.Text = e.Port?.ToString() ?? "";
        FixedCheck.IsChecked = e.PortFixed;
        FavoriteCheck.IsChecked = e.Favorite;
        _framework = e.Framework;
        OpenCombo.SelectedIndex = e.Open == OpenTargets.WebView ? 1 : 0;
        GroupBox.Text = e.Group ?? "";
        RoleCombo.SelectedIndex = e.Role switch
        {
            AppRoles.Backend => 1,
            AppRoles.Frontend => 2,
            _ => 0,
        };
    }

    private string SelectedRole => (RoleCombo.SelectedItem as ComboBoxItem)?.Content as string ?? AppRoles.Other;

    private string? GroupValue =>
        string.IsNullOrWhiteSpace(GroupBox.Text) ? null : GroupBox.Text.Trim();

    private void ModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyMode();

    private void ApplyMode()
    {
        // Guard: SelectionChanged can fire during XAML init before named elements exist.
        if (DirPanel is null)
            return;

        var urlOnly = ModeCombo.SelectedIndex == ModeWebUrl;
        var collapsedIfUrlOnly = urlOnly ? Visibility.Collapsed : Visibility.Visible;

        DirPanel.Visibility = collapsedIfUrlOnly;
        CmdPanel.Visibility = collapsedIfUrlOnly;
        InstallPanel.Visibility = collapsedIfUrlOnly;
        PortPanel.Visibility = collapsedIfUrlOnly;
        DetectButton.Visibility = ModeCombo.SelectedIndex == ModeLocal ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(DirBox.Text) && Directory.Exists(DirBox.Text))
            dialog.SelectedPath = DirBox.Text;

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            DirBox.Text = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = new DirectoryInfo(dialog.SelectedPath).Name;
        }
    }

    private void DetectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dir = DirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            ErrorText.Text = "Choose an existing folder first.";
            return;
        }

        var detected = new StackDetector().Detect(dir);
        if (detected is null)
        {
            ErrorText.Text =
                $"No known framework detected in \"{new DirectoryInfo(dir).Name}\" " +
                "(looks for package.json with a dev/start script, or a top-level .py using " +
                "streamlit/FastAPI/Flask/gradio). Switch Mode to \"Manual command\" and enter the " +
                "command + URL yourself.";
            return;
        }

        CmdBox.Text = detected.Cmd;
        UrlBox.Text = detected.Url;
        if (string.IsNullOrWhiteSpace(InstallBox.Text))
            InstallBox.Text = detected.InstallCmd ?? "";
        _framework = detected.Framework;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = new DirectoryInfo(dir).Name; // name from the browsed root

        var inSubfolder = !string.Equals(detected.Dir, dir, StringComparison.OrdinalIgnoreCase);
        if (inSubfolder)
            DirBox.Text = detected.Dir; // run from the matched subfolder (e.g. frontend/)

        var where = inSubfolder ? $" in {Path.GetFileName(detected.Dir)}/" : "";
        ErrorText.Text = $"Detected {detected.Framework}{where} → cmd: {detected.Cmd}, url: {detected.Url}.";
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var entry = BuildEntry();
            if (_existing is not null)
            {
                entry.Id = _existing.Id;
                entry.CreatedAt = _existing.CreatedAt;
                entry.LastLaunchedAt = _existing.LastLaunchedAt;
                _service.Update(entry);
            }
            else
            {
                _service.Add(entry);
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private AppEntry BuildEntry()
    {
        var open = (OpenCombo.SelectedIndex == 1) ? OpenTargets.WebView : OpenTargets.Browser;
        var tags = ParseTags(TagsBox.Text);
        var favorite = FavoriteCheck.IsChecked == true;

        if (ModeCombo.SelectedIndex == ModeWebUrl)
        {
            var url = UrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("URL is required.");
            var urlName = NameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(urlName))
                urlName = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

            return new AppEntry { Name = urlName, Url = url, Cmd = null, Dir = null, Open = open, Tags = tags, Favorite = favorite, Group = GroupValue, Role = SelectedRole };
        }

        var dir = DirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("Folder is required.");
        var cmd = CmdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(cmd))
            throw new InvalidOperationException("Command is required — use Detect or type it.");
        var url2 = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url2))
            throw new InvalidOperationException("URL is required.");

        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = new DirectoryInfo(dir).Name;

        var (port, portFixed) = ResolvePort(url2);
        url2 = UrlPort.WithPort(url2, port);

        return new AppEntry
        {
            Name = name,
            Dir = Path.GetFullPath(dir),
            Cmd = cmd,
            Url = url2,
            InstallCmd = string.IsNullOrWhiteSpace(InstallBox.Text) ? null : InstallBox.Text.Trim(),
            Framework = _framework,
            Port = port,
            PortFixed = portFixed,
            Open = open,
            Tags = tags,
            Favorite = favorite,
            Group = GroupValue,
            Role = SelectedRole,
        };
    }

    private (int Port, bool Fixed) ResolvePort(string url)
    {
        if (!string.IsNullOrWhiteSpace(PortBox.Text))
        {
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Port must be a number between 1 and 65535.");

            var clash = _service.LoadApps().FirstOrDefault(a => a.Port == port && a.Id != _existing?.Id);
            if (clash is not null)
                throw new InvalidOperationException($"Port {port} is already reserved by '{clash.Name}'.");

            return (port, FixedCheck.IsChecked == true);
        }

        var allocated = new PortAllocator().Allocate(_service.ReservedPorts(_existing?.Id), UrlPort.FromUrl(url));
        return (allocated, false);
    }

    private static List<string> ParseTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
