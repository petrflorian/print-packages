using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PdfiumViewer;
using System.IO;

namespace PrintPackages;

public partial class MainWindow : Window
{
    private readonly LibraryStore _library = new();
    private readonly WindowsPrinterService _printer = new();
    public MainWindow() { InitializeComponent(); _library.CreateFolder(""); RefreshList(); }

    private sealed record PackageRow(string Path, string DisplayName);
    private PackageRow? Selected => Packages.SelectedItem as PackageRow;

    private void RefreshList()
    {
        Packages.ItemsSource = _library.Find(SearchBox?.Text ?? "")
            .Select(path => new PackageRow(path, System.IO.Path.GetRelativePath(_library.Root, path))).ToList();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshList();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf" };
        if (picker.ShowDialog() != true) return;
        try
        {
            var pdf = File.ReadAllBytes(picker.FileName);
            if (!pdf.AsSpan().StartsWith("%PDF"u8)) throw new InvalidDataException("Vybraný soubor není PDF.");
            var (printer, devMode) = _printer.CaptureSettings();
            var name = System.IO.Path.GetFileNameWithoutExtension(picker.FileName);
            var manifest = new PrintPackageManifest("1", Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow,
                PackageArchive.Hash(pdf), PackageArchive.Hash(devMode), printer, new Dictionary<string, string>());
            var package = new PrintPackage(manifest, pdf, devMode, CreatePreview(picker.FileName));
            var folder = _library.CreateFolder("Nedávné");
            PackageArchive.Save(System.IO.Path.Combine(folder, SafeFileName(name) + PackageArchive.Extension), package);
            RefreshList();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex); }
    }
    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = InputPrompt.Ask("Název složky", "Nová složka");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { _library.CreateFolder(SafeFileName(name)); RefreshList(); } catch (Exception ex) { ShowError(ex); }
    }

    private static byte[]? CreatePreview(string pdfPath)
    {
        using var doc = PdfDocument.Load(pdfPath);
        using var image = doc.Render(0, 600, 850, 144, 144, PdfRenderFlags.Annotations);
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
    private static string SafeFileName(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void Packages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Preview.Source = null; Details.Text = "";
        if (Selected is null) return;
        try
        {
            var p = PackageArchive.Load(Selected.Path);
            Details.Text = $"{p.Manifest.Name}\n\nTiskárna: {p.Manifest.Printer.QueueName}\nModel: {p.Manifest.Printer.Model}\nOvladač: {p.Manifest.Printer.DriverName} ({p.Manifest.Printer.DriverVersion})\nVytvořeno: {p.Manifest.CreatedAt.LocalDateTime:g}";
            if (p.Preview is not null) { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(p.Preview); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze(); Preview.Source = bitmap; }
        }
        catch (Exception ex) { Details.Text = "Balíček nelze načíst: " + ex.Message; }
    }
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null || !int.TryParse(CopiesBox.Text, out var copies) || copies is < 1 or > short.MaxValue) { MessageBox.Show("Vyberte balíček a zadejte platný počet kopií."); return; }
        try { _printer.Print(PackageArchive.Load(Selected.Path), copies); MessageBox.Show("Úloha byla předána do Windows tiskové fronty.", "Odesláno"); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Tiskový balíček (*.printpkg)|*.printpkg" }; if (picker.ShowDialog() != true) return;
        try { PackageArchive.Load(picker.FileName); var folder = _library.CreateFolder("Importované"); File.Copy(picker.FileName, System.IO.Path.Combine(folder, System.IO.Path.GetFileName(picker.FileName)), false); RefreshList(); } catch (Exception ex) { ShowError(ex); }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return; var picker = new SaveFileDialog { Filter = "Tiskový balíček (*.printpkg)|*.printpkg", FileName = System.IO.Path.GetFileName(Selected.Path) }; if (picker.ShowDialog() == true) File.Copy(Selected.Path, picker.FileName, true);
    }
    private void Copy_Click(object sender, RoutedEventArgs e) { if (Selected is null) return; var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Selected.Path)!, System.IO.Path.GetFileNameWithoutExtension(Selected.Path) + " - kopie" + PackageArchive.Extension); try { _library.Copy(Selected.Path, target); RefreshList(); } catch (Exception ex) { ShowError(ex); } }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var name = InputPrompt.Ask("Nový název balíčku", System.IO.Path.GetFileNameWithoutExtension(Selected.Path));
        if (string.IsNullOrWhiteSpace(name)) return;
        try { _library.Move(Selected.Path, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Selected.Path)!, SafeFileName(name) + PackageArchive.Extension)); RefreshList(); } catch (Exception ex) { ShowError(ex); }
    }
    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var destination = InputPrompt.Ask("Cílová složka v knihovně", "Nedávné");
        if (string.IsNullOrWhiteSpace(destination)) return;
        var folder = Path.GetFullPath(Path.Combine(_library.Root, destination));
        if (!folder.StartsWith(Path.GetFullPath(_library.Root), StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("Cílová složka musí být v knihovně tisků."); return; }
        try { Directory.CreateDirectory(folder); _library.Move(Selected.Path, Path.Combine(folder, Path.GetFileName(Selected.Path))); RefreshList(); } catch (Exception ex) { ShowError(ex); }
    }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (Selected is null) return; if (MessageBox.Show($"Smazat {Selected.DisplayName}?", "Potvrdit", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _library.Delete(Selected.Path); RefreshList(); } }
    private void ShowError(Exception ex) => MessageBox.Show(ex.Message, "Tiskové balíčky", MessageBoxButton.OK, MessageBoxImage.Error);
}

internal static class InputPrompt
{
    public static string? Ask(string title, string initial)
    {
        var input = new TextBox { Text = initial, Margin = new Thickness(14), MinWidth = 300 };
        var ok = new Button { Content = "Uložit", IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = "Zrušit", IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        var panel = new StackPanel(); panel.Children.Add(input); var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        var dialog = new Window { Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize };
        ok.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
