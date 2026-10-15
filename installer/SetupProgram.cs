using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Diagnostics;

internal static class SetupProgram
{
    private const string AppName = "AllIsWhere";
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllIsWhere";
    private static readonly string[] PayloadNames = { "AllIsWhere.exe", "AllIsWhere.dll", "AllIsWhere.deps.json", "AllIsWhere.runtimeconfig.json" };

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Length > 0 && String.Equals(args[0], "/remove", StringComparison.OrdinalIgnoreCase))
            {
                RemoveInstalledFiles(args.Length > 1 ? args[1] : String.Empty);
                return;
            }
            if (args.Length > 0 && String.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
            {
                StartUninstall();
                return;
            }
            ShowSetup();
        }
        catch (Exception ex)
        {
            MessageBox.Show("İşlem tamamlanamadı.\r\n\r\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ShowSetup()
    {
        string defaultDir = GetExistingInstallLocation() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EverythingLite", "settings.json");
        bool launchAtStartup = ReadStartupSetting(settingsPath);

        using (Form form = new Form())
        using (Label title = new Label())
        using (Label folderLabel = new Label())
        using (TextBox folder = new TextBox())
        using (Button browse = new Button())
        using (CheckBox desktop = new CheckBox())
        using (CheckBox startMenu = new CheckBox())
        using (CheckBox startup = new CheckBox())
        using (Label note = new Label())
        using (Button install = new Button())
        using (Button cancel = new Button())
        {
            form.Text = AppName + " Kurulumu";
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ClientSize = new Size(600, 300);
            form.Font = new Font("Segoe UI", 9F);

            title.Text = AppName + " kurulumu";
            title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            title.SetBounds(22, 18, 540, 34);
            folderLabel.Text = "Kurulum klasörü:";
            folderLabel.SetBounds(24, 68, 180, 22);
            folder.Text = defaultDir;
            folder.SetBounds(24, 94, 440, 26);
            browse.Text = "Gözat...";
            browse.SetBounds(476, 92, 100, 30);
            browse.Click += delegate
            {
                using (FolderBrowserDialog picker = new FolderBrowserDialog())
                {
                    picker.Description = "AllIsWhere uygulamasının kurulacağı klasörü seçin.";
                    picker.ShowNewFolderButton = true;
                    try { picker.SelectedPath = Path.GetDirectoryName(Path.GetFullPath(folder.Text)); } catch { }
                    if (picker.ShowDialog(form) == DialogResult.OK) folder.Text = picker.SelectedPath;
                }
            };
            desktop.Text = "Masaüstüne kısayol oluştur";
            desktop.Checked = true;
            desktop.SetBounds(26, 142, 260, 24);
            startMenu.Text = "Başlat menüsüne kısayol oluştur";
            startMenu.Checked = true;
            startMenu.SetBounds(300, 142, 280, 24);
            startup.Text = "Windows oturum açılışında başlat";
            startup.Checked = launchAtStartup;
            startup.SetBounds(26, 176, 290, 24);
            note.Text = "Uygulama başlarken Windows yönetici izni ister. .NET 10 Desktop Runtime (x64) gereklidir.";
            note.SetBounds(26, 211, 550, 38);
            install.Text = "Kur";
            install.SetBounds(366, 252, 96, 34);
            install.Click += delegate
            {
                string target;
                if (!TryNormalizeInstallPath(folder.Text, out target))
                {
                    MessageBox.Show("Geçerli bir kurulum klasörü seçin. Sürücü köküne doğrudan kurulum yapılamaz.", AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try
                {
                    Install(target, desktop.Checked, startMenu.Checked, startup.Checked, settingsPath);
                    MessageBox.Show("AllIsWhere kuruldu.\r\n\r\nKurulum konumu:\r\n" + target +
                        "\r\n\r\nMasaüstü kısayolu: " + (desktop.Checked ? "Evet" : "Hayır") +
                        "\r\nBaşlat menüsü kısayolu: " + (startMenu.Checked ? "Evet" : "Hayır"),
                        "Kurulum tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Kurulum yapılamadı. Başka bir klasör seçin veya kurulum dosyasını yönetici olarak çalıştırın.\r\n\r\n" + ex.Message,
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            cancel.Text = "İptal";
            cancel.SetBounds(480, 252, 96, 34);
            cancel.DialogResult = DialogResult.Cancel;

            form.Controls.AddRange(new Control[] { title, folderLabel, folder, browse, desktop, startMenu, startup, note, install, cancel });
            form.AcceptButton = install;
            form.CancelButton = cancel;
            form.ShowDialog();
        }
    }

    private static bool TryNormalizeInstallPath(string input, out string path)
    {
        path = null;
        if (String.IsNullOrWhiteSpace(input)) return false;
        try
        {
            string full = Path.GetFullPath(input.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;
            path = full;
            return true;
        }
        catch { return false; }
    }

    private static bool ReadStartupSetting(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return true;
            object parsed = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(settingsPath, Encoding.UTF8));
            var values = parsed as System.Collections.Generic.Dictionary<string, object>;
            if (values != null && values.ContainsKey("StartWithWindows")) return Convert.ToBoolean(values["StartWithWindows"]);
        }
        catch { }
        return true;
    }

    private static string GetExistingInstallLocation()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                string path = key == null ? null : Convert.ToString(key.GetValue("InstallLocation", String.Empty));
                return !String.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;
            }
        }
        catch { return null; }
    }

    private static void Install(string target, bool desktop, bool startMenu, bool startup, string settingsPath)
    {
        // Replace files after the app exits because tray processes may lock its assemblies.
        foreach (Process process in Process.GetProcessesByName("AllIsWhere"))
        {
            using (process)
            {
                if (process.Id != Process.GetCurrentProcess().Id)
                    throw new InvalidOperationException("Güncellemeden önce AllIsWhere'i tamamen kapatıp setup'ı yeniden çalıştırın. Yeni sürümde kapatmak için bildirim alanı simgesine sağ tıklayıp 'Tamamen kapat' seçin.");
            }
        }
        Directory.CreateDirectory(target);
        string writeTest = Path.Combine(target, ".all-is-where-write-check");
        File.WriteAllText(writeTest, String.Empty);
        File.Delete(writeTest);
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (string name in PayloadNames)
        {
            // Build-Setup.ps1 embeds resources without prefixes, using their exact file names.
            using (Stream resource = assembly.GetManifestResourceStream(name))
            {
                if (resource == null) throw new InvalidOperationException("Kurulum paketi eksik dosya içeriyor: " + name);
                using (FileStream output = File.Create(Path.Combine(target, name))) resource.CopyTo(output);
            }
        }
        string uninstallPath = Path.Combine(target, "AllIsWhereUninstall.exe");
        File.Copy(Application.ExecutablePath, uninstallPath, true);
        string appPath = Path.Combine(target, "AllIsWhere.exe");
        string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
        string menuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", AppName + ".lnk");
        if (desktop) CreateShortcut(desktopPath, appPath, target); else SafeDeleteShortcut(desktopPath);
        if (startMenu) CreateShortcut(menuPath, appPath, target); else SafeDeleteShortcut(menuPath);
        WriteStartupSetting(settingsPath, startup);

        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RegistryRunKey))
        {
            if (startup) run.SetValue(AppName, "\"" + appPath + "\"", RegistryValueKind.String);
            else run.DeleteValue(AppName, false);
            run.DeleteValue("EverythingLite", false);
        }
        using (RegistryKey uninstall = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            uninstall.SetValue("DisplayName", AppName, RegistryValueKind.String);
            uninstall.SetValue("DisplayVersion", "1.0.0", RegistryValueKind.String);
            uninstall.SetValue("Publisher", AppName, RegistryValueKind.String);
            uninstall.SetValue("InstallLocation", target, RegistryValueKind.String);
            uninstall.SetValue("UninstallString", "\"" + uninstallPath + "\" /uninstall", RegistryValueKind.String);
            uninstall.SetValue("DisplayIcon", appPath, RegistryValueKind.String);
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\EverythingLite", false); } catch { }
    }

    private static void WriteStartupSetting(string path, bool startup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var serializer = new JavaScriptSerializer();
        var values = new System.Collections.Generic.Dictionary<string, object>();
        try
        {
            if (File.Exists(path))
            {
                var existing = serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as System.Collections.Generic.Dictionary<string, object>;
                if (existing != null) values = existing;
            }
        }
        catch { }
        values["StartWithWindows"] = startup;
        File.WriteAllText(path, serializer.Serialize(values), new UTF8Encoding(false));
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) throw new InvalidOperationException("Windows kısayol bileşeni kullanılamıyor.");
        object shell = Activator.CreateInstance(shellType);
        object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
        shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "AllIsWhere dosya ve klasör arama" });
        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        if (Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
        if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
    }

    private static void SafeDeleteShortcut(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void StartUninstall()
    {
        string sourceDir = Path.GetDirectoryName(Application.ExecutablePath);
        string target;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey)) target = key == null ? sourceDir : Convert.ToString(key.GetValue("InstallLocation", sourceDir));
        if (!String.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kaldırma konumu kayıtlı kurulum klasörüyle eşleşmiyor.");
        string tempCopy = Path.Combine(Path.GetTempPath(), "AllIsWhereUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Application.ExecutablePath, tempCopy, true);
        Process.Start(new ProcessStartInfo { FileName = tempCopy, Arguments = "/remove \"" + target + "\"", UseShellExecute = false, CreateNoWindow = true });
    }

    private static void RemoveInstalledFiles(string target)
    {
        if (String.IsNullOrWhiteSpace(target) || !Directory.Exists(target)) return;
        string full = Path.GetFullPath(target).TrimEnd('\\');
        foreach (string name in PayloadNames) TryDelete(Path.Combine(full, name));
        TryDelete(Path.Combine(full, "AllIsWhereUninstall.exe"));
        SafeDeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));
        SafeDeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", AppName + ".lnk"));
        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true)) if (run != null) run.DeleteValue(AppName, false);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { Directory.Delete(full, false); } catch { }
        MessageBox.Show("AllIsWhere kaldırıldı.\r\n\r\nKullanıcı ayarlarınız korundu.", "AllIsWhere kaldırma", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}

