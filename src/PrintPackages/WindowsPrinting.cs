using System.Drawing.Printing;
using System.Printing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Interop;
using PdfiumViewer;

namespace PrintPackages;

public sealed class WindowsPrinterService
{
    private static string DriverVersion(PrintDriver driver)
    {
        var name = driver.Name.Replace("'", "''", StringComparison.Ordinal);
        using var searcher = new ManagementObjectSearcher($"SELECT DriverVersion FROM Win32_PrinterDriver WHERE Name = '{name}'");
        foreach (ManagementObject result in searcher.Get())
            return result["DriverVersion"]?.ToString() ?? "unknown";
        return "unknown";
    }

    private static bool IsSupportedPcl6(PrintQueue queue) =>
        (queue.QueueDriver.Name.Contains("PCL6", StringComparison.OrdinalIgnoreCase) ||
         queue.QueueDriver.Name.Contains("PCL XL", StringComparison.OrdinalIgnoreCase));

    public (PrinterIdentity identity, byte[] devMode) CaptureSettings()
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true || dialog.PrintQueue is null) throw new OperationCanceledException();
        var queue = dialog.PrintQueue;
        if (!IsSupportedPcl6(queue))
            throw new InvalidOperationException("Vyberte tiskárnu s nainstalovaným ovladačem PCL6/PCL XL (např. OKI C844 nebo Canon i-SENSYS X 1533P II).");
        var bytes = OpenDriverProperties(queue.FullName);
        return (new PrinterIdentity(queue.FullName, queue.QueueDriver.Name, DriverVersion(queue.QueueDriver), queue.Name), bytes);
    }

    private static byte[] OpenDriverProperties(string printerName)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new InvalidOperationException("Nelze otevřít vybranou tiskárnu.");
        try
        {
            var size = DocumentProperties(OwnerHandle(), handle, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0) throw new InvalidOperationException("Ovladač neposkytl nastavení tisku.");
            var devMode = Marshal.AllocHGlobal(size);
            try
            {
                const int DM_OUT_BUFFER = 0x00000002;
                const int DM_IN_PROMPT = 0x00000004;
                if (DocumentProperties(OwnerHandle(), handle, printerName, devMode, IntPtr.Zero, DM_OUT_BUFFER | DM_IN_PROMPT) <= 0)
                    throw new OperationCanceledException();
                var bytes = new byte[size];
                Marshal.Copy(devMode, bytes, 0, size);
                return bytes;
            }
            finally { Marshal.FreeHGlobal(devMode); }
        }
        finally { ClosePrinter(handle); }
    }

    private static IntPtr OwnerHandle() => System.Windows.Application.Current?.MainWindow is { } window
        ? new WindowInteropHelper(window).Handle : IntPtr.Zero;

    public void Validate(PrinterIdentity expected)
    {
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueue(expected.QueueName);
        queue.Refresh();
        if (!IsSupportedPcl6(queue))
            throw new InvalidOperationException("Tiskárna nepoužívá požadovaný ovladač PCL6/PCL XL.");
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

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printerHandle);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(IntPtr hwnd, IntPtr printerHandle, string printerName, IntPtr outputDevMode, IntPtr inputDevMode, int mode);
}
