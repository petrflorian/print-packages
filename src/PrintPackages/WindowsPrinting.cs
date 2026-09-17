using System.Drawing.Printing;
using System.Printing;
using System.Printing.Interop;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using PdfiumViewer;

namespace PrintPackages;

public sealed class WindowsPrinterService
{
    private static string DriverVersion(PrintDriver driver) =>
        string.IsNullOrWhiteSpace(driver.DriverPath) ? "unknown" : FileVersionInfo.GetVersionInfo(driver.DriverPath).FileVersion ?? "unknown";

    private static bool IsSupportedOkiPcl6(PrintQueue queue) =>
        queue.QueueDriver.Name.Contains("OKI", StringComparison.OrdinalIgnoreCase) &&
        (queue.QueueDriver.Name.Contains("PCL6", StringComparison.OrdinalIgnoreCase) ||
         queue.QueueDriver.Name.Contains("PCL XL", StringComparison.OrdinalIgnoreCase));

    public (PrinterIdentity identity, byte[] devMode) CaptureSettings()
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true || dialog.PrintQueue is null || dialog.PrintTicket is null) throw new OperationCanceledException();
        var queue = dialog.PrintQueue;
        if (!IsSupportedOkiPcl6(queue))
            throw new InvalidOperationException("Vyberte tiskárnu OKI C844 s nainstalovaným ovladačem PCL6/PCL XL.");
        using var converter = new PrintTicketConverter(queue.FullName, PrintTicketConverter.MaxPrintSchemaVersion);
        var bytes = converter.ConvertPrintTicketToDevMode(dialog.PrintTicket, BaseDevModeType.UserDefault);
        if (bytes.Length == 0) throw new InvalidOperationException("Ovladač nevrátil nastavení DEVMODE.");
        return (new PrinterIdentity(queue.FullName, queue.QueueDriver.Name, DriverVersion(queue.QueueDriver), queue.Name), bytes);
    }

    public void Validate(PrinterIdentity expected)
    {
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueue(expected.QueueName);
        queue.Refresh();
        if (!IsSupportedOkiPcl6(queue))
            throw new InvalidOperationException("Tiskárna nepoužívá požadovaný OKI PCL6/PCL XL ovladač.");
        if (!string.Equals(queue.QueueDriver.Name, expected.DriverName, StringComparison.Ordinal) ||
            !string.Equals(DriverVersion(queue.QueueDriver), expected.DriverVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Nalezená tiskárna nemá stejný OKI ovladač/verzi jako balíček.");
        if (queue.IsOffline) throw new InvalidOperationException("Tiskárna je offline.");
    }

    public void Print(PrintPackage package, int copies)
    {
        Validate(package.Manifest.Printer);
        var pdf = Path.Combine(Path.GetTempPath(), package.Manifest.Id + ".pdf");
        File.WriteAllBytes(pdf, package.Pdf);
        try
        {
            using var document = PdfDocument.Load(pdf);
            using var print = document.CreatePrintDocument(PdfPrintMode.CutMargin);
            print.PrinterSettings.PrinterName = package.Manifest.Printer.QueueName;
            print.PrinterSettings.Copies = checked((short)copies);
            var hDevMode = Marshal.AllocHGlobal(package.DevMode.Length);
            try { Marshal.Copy(package.DevMode, 0, hDevMode, package.DevMode.Length); print.PrinterSettings.SetHdevmode(hDevMode); print.PrintController = new StandardPrintController(); print.Print(); }
            finally { Marshal.FreeHGlobal(hDevMode); }
        }
        finally { File.Delete(pdf); }
    }
}
