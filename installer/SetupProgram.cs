using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class SetupProgram
{
	private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr state);

	private const string AppName = "AllIsWhere";

	private const string RegistryRunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string UninstallKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllIsWhere";

	private const uint WmClose = 16u;

	private static readonly string[] PayloadNames = new string[4] { "AllIsWhere.exe", "AllIsWhere.dll", "AllIsWhere.deps.json", "AllIsWhere.runtimeconfig.json" };

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

	[STAThread]
	private static void Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		try
		{
			if (args.Length > 0 && string.Equals(args[0], "/remove", StringComparison.OrdinalIgnoreCase))
			{
				RemoveInstalledFiles((args.Length > 1) ? args[1] : string.Empty);
			}
			else if (args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
			{
				StartUninstall();
			}
			else if (args.Length > 0 && string.Equals(args[0], "/update", StringComparison.OrdinalIgnoreCase))
			{
				WaitForApplicationExit((args.Length > 1) ? args[1] : string.Empty);
				ShowSetup();
			}
			else
			{
				ShowSetup();
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("İşlem tamamlanamadı.\r\n\r\n" + ex.Message, "AllIsWhere", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private static void ShowSetup()
	{
		string text = GetExistingInstallLocation() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AllIsWhere");
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string settingsPath = Path.Combine(folderPath, "AllIsWhere", "settings.json");
		if (!File.Exists(settingsPath))
		{
			settingsPath = Path.Combine(folderPath, "EverythingLite", "settings.json");
		}
		bool flag = ReadStartupSetting(settingsPath);
		Form form = new Form();
		try
		{
			using Label label = new Label();
			using Label label2 = new Label();
			TextBox folder = new TextBox();
			try
			{
				using Button button = new Button();
				CheckBox desktop = new CheckBox();
				try
				{
					CheckBox startMenu = new CheckBox();
					try
					{
						CheckBox startup = new CheckBox();
						try
						{
							using Label label3 = new Label();
							using Button button2 = new Button();
							using Button button3 = new Button();
							form.Text = "AllIsWhere Kurulumu";
							form.StartPosition = FormStartPosition.CenterScreen;
							form.FormBorderStyle = FormBorderStyle.FixedDialog;
							form.MaximizeBox = false;
							form.MinimizeBox = false;
							form.ClientSize = new Size(600, 300);
							form.Font = new Font("Segoe UI", 9f);
							label.Text = "AllIsWhere kurulumu";
							label.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
							label.SetBounds(22, 18, 540, 34);
							label2.Text = "Kurulum klasörü:";
							label2.SetBounds(24, 68, 180, 22);
							folder.Text = text;
							folder.SetBounds(24, 94, 440, 26);
							button.Text = "Gözat...";
							button.SetBounds(476, 92, 100, 30);
							button.Click += (object param0, EventArgs param1) =>
							{
								using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
								folderBrowserDialog.Description = "AllIsWhere uygulamasının kurulacağı klasörü seçin.";
								folderBrowserDialog.ShowNewFolderButton = true;
								try
								{
									folderBrowserDialog.SelectedPath = Path.GetDirectoryName(Path.GetFullPath(folder.Text));
								}
								catch
								{
								}
								if (folderBrowserDialog.ShowDialog(form) == DialogResult.OK)
								{
									folder.Text = folderBrowserDialog.SelectedPath;
								}
							};
							desktop.Text = "Masaüstüne kısayol oluştur";
							desktop.Checked = true;
							desktop.SetBounds(26, 142, 260, 24);
							startMenu.Text = "Başlat menüsüne kısayol oluştur";
							startMenu.Checked = true;
							startMenu.SetBounds(300, 142, 280, 24);
							startup.Text = "Windows oturum açılışında başlat";
							startup.Checked = flag;
							startup.SetBounds(26, 176, 290, 24);
							label3.Text = "Uygulama başlarken Windows yönetici izni ister. .NET 10 Desktop Runtime (x64) gereklidir.";
							label3.SetBounds(26, 211, 550, 38);
							button2.Text = "Kur";
							button2.SetBounds(366, 252, 96, 34);
							button2.Click += (object param0, EventArgs param1) =>
							{
								if (!TryNormalizeInstallPath(folder.Text, out var path))
								{
									MessageBox.Show("Geçerli bir kurulum klasörü seçin. Sürücü köküne doğrudan kurulum yapılamaz.", "AllIsWhere", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
									return;
								}
								try
								{
									Install(path, desktop.Checked, startMenu.Checked, startup.Checked, settingsPath);
									MessageBox.Show("AllIsWhere kuruldu.\r\n\r\nKurulum konumu:\r\n" + path + "\r\n\r\nMasaüstü kısayolu: " + (desktop.Checked ? "Evet" : "Hayır") + "\r\nBaşlat menüsü kısayolu: " + (startMenu.Checked ? "Evet" : "Hayır"), "Kurulum tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
									form.DialogResult = DialogResult.OK;
									form.Close();
								}
								catch (Exception ex)
								{
									MessageBox.Show("Kurulum yapılamadı. Başka bir klasör seçin veya kurulum dosyasını yönetici olarak çalıştırın.\r\n\r\n" + ex.Message, "AllIsWhere", MessageBoxButtons.OK, MessageBoxIcon.Hand);
								}
							};
							button3.Text = "İptal";
							button3.SetBounds(480, 252, 96, 34);
							button3.DialogResult = DialogResult.Cancel;
							form.Controls.AddRange(new Control[10] { label, label2, folder, button, desktop, startMenu, startup, label3, button2, button3 });
							form.AcceptButton = button2;
							form.CancelButton = button3;
							form.ShowDialog();
						}
						finally
						{
							if (startup != null)
							{
								((IDisposable)startup).Dispose();
							}
						}
					}
					finally
					{
						if (startMenu != null)
						{
							((IDisposable)startMenu).Dispose();
						}
					}
				}
				finally
				{
					if (desktop != null)
					{
						((IDisposable)desktop).Dispose();
					}
				}
			}
			finally
			{
				if (folder != null)
				{
					((IDisposable)folder).Dispose();
				}
			}
		}
		finally
		{
			if (form != null)
			{
				((IDisposable)form).Dispose();
			}
		}
	}

	private static void WaitForApplicationExit(string processIdText)
	{
		if (!int.TryParse(processIdText, out var result) || result <= 0)
		{
			return;
		}
		try
		{
			using Process process = Process.GetProcessById(result);
			if (!process.WaitForExit(60000))
			{
				throw new TimeoutException("AllIsWhere did not close in time to install the update.");
			}
		}
		catch (ArgumentException)
		{
		}
	}

	private static bool TryNormalizeInstallPath(string input, out string path)
	{
		path = null;
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}
		try
		{
			string text = Path.GetFullPath(input.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string b = Path.GetPathRoot(text).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (string.Equals(text, b, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			path = text;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool ReadStartupSetting(string settingsPath)
	{
		try
		{
			if (!File.Exists(settingsPath))
			{
				return true;
			}
			object obj = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(settingsPath, Encoding.UTF8));
			if (obj is Dictionary<string, object> dictionary && dictionary.ContainsKey("StartWithWindows"))
			{
				return Convert.ToBoolean(dictionary["StartWithWindows"]);
			}
		}
		catch
		{
		}
		return true;
	}

	private static string GetExistingInstallLocation()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllIsWhere");
			string text = ((registryKey == null) ? null : Convert.ToString(registryKey.GetValue("InstallLocation", string.Empty)));
			return (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text)) ? text : null;
		}
		catch
		{
			return null;
		}
	}

	private static string DeployPayload(string target)
	{
		Directory.CreateDirectory(target);
		string path = Path.Combine(target, ".all-is-where-write-check");
		File.WriteAllText(path, string.Empty);
		File.Delete(path);
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		string[] payloadNames = PayloadNames;
		foreach (string text in payloadNames)
		{
			using Stream stream = executingAssembly.GetManifestResourceStream(text);
			if (stream == null)
			{
				throw new InvalidOperationException("Kurulum paketi eksik dosya içeriyor: " + text);
			}
			using FileStream destination = File.Create(Path.Combine(target, text));
			stream.CopyTo(destination);
		}
		string text2 = Path.Combine(target, "AllIsWhereUninstall.exe");
		File.Copy(executingAssembly.Location, text2, overwrite: true);
		return text2;
	}

	private static void Install(string target, bool desktop, bool startMenu, bool startup, string settingsPath)
	{
		RequestRunningApplicationExit();
		Process[] processesByName = Process.GetProcessesByName("AllIsWhere");
		foreach (Process process in processesByName)
		{
			using (process)
			{
				if (process.Id != Process.GetCurrentProcess().Id)
				{
					throw new InvalidOperationException("Güncellemeden önce AllIsWhere'i tamamen kapatıp setup'ı yeniden çalıştırın. Yeni sürümde kapatmak için bildirim alanı simgesine sağ tıklayıp 'Tamamen kapat' seçin.");
				}
			}
		}
		string text2 = DeployPayload(target);
		string text3 = Path.Combine(target, "AllIsWhere.exe");
		string text4 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AllIsWhere.lnk");
		string text5 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "AllIsWhere.lnk");
		if (desktop)
		{
			CreateShortcut(text4, text3, target);
		}
		else
		{
			SafeDeleteShortcut(text4);
		}
		if (startMenu)
		{
			CreateShortcut(text5, text3, target);
		}
		else
		{
			SafeDeleteShortcut(text5);
		}
		WriteStartupSetting(settingsPath, startup);
		using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
		{
			if (startup)
			{
				registryKey.SetValue("AllIsWhere", "\"" + text3 + "\"", RegistryValueKind.String);
			}
			else
			{
				registryKey.DeleteValue("AllIsWhere", throwOnMissingValue: false);
			}
			registryKey.DeleteValue("EverythingLite", throwOnMissingValue: false);
		}
		using (RegistryKey registryKey2 = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllIsWhere"))
		{
			registryKey2.SetValue("DisplayName", "AllIsWhere", RegistryValueKind.String);
			string value = AssemblyName.GetAssemblyName(Path.Combine(target, "AllIsWhere.dll")).Version.ToString(3);
			registryKey2.SetValue("DisplayVersion", value, RegistryValueKind.String);
			registryKey2.SetValue("Publisher", "AllIsWhere", RegistryValueKind.String);
			registryKey2.SetValue("InstallLocation", target, RegistryValueKind.String);
			registryKey2.SetValue("UninstallString", "\"" + text2 + "\" /uninstall", RegistryValueKind.String);
			registryKey2.SetValue("DisplayIcon", text3, RegistryValueKind.String);
		}
		try
		{
			Registry.CurrentUser.DeleteSubKeyTree("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\EverythingLite", throwOnMissingSubKey: false);
		}
		catch
		{
		}
	}

	private static void WriteStartupSetting(string path, bool startup)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		try
		{
			if (File.Exists(path) && javaScriptSerializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) is Dictionary<string, object> dictionary2)
			{
				dictionary = dictionary2;
			}
		}
		catch
		{
		}
		dictionary["StartWithWindows"] = startup;
		File.WriteAllText(path, javaScriptSerializer.Serialize(dictionary), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
		Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
		if (typeFromProgID == null)
		{
			throw new InvalidOperationException("Windows kısayol bileşeni kullanılamıyor.");
		}
		object obj = Activator.CreateInstance(typeFromProgID);
		object obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { shortcutPath });
		Type type = obj2.GetType();
		type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, obj2, new object[1] { targetPath });
		type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, obj2, new object[1] { workingDirectory });
		type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, obj2, new object[1] { targetPath + ",0" });
		type.InvokeMember("Description", BindingFlags.SetProperty, null, obj2, new object[1] { "AllIsWhere dosya ve klasör arama" });
		type.InvokeMember("Save", BindingFlags.InvokeMethod, null, obj2, null);
		if (Marshal.IsComObject(obj2))
		{
			Marshal.FinalReleaseComObject(obj2);
		}
		if (Marshal.IsComObject(obj))
		{
			Marshal.FinalReleaseComObject(obj);
		}
	}

	private static void SafeDeleteShortcut(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static void StartUninstall()
	{
		string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
		string text;
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllIsWhere"))
		{
			text = ((registryKey == null) ? directoryName : Convert.ToString(registryKey.GetValue("InstallLocation", directoryName)));
		}
		if (!string.Equals(Path.GetFullPath(directoryName).TrimEnd('\\'), Path.GetFullPath(text).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Kaldırma konumu kayıtlı kurulum klasörüyle eşleşmiyor.");
		}
		string text2 = Path.Combine(Path.GetTempPath(), "AllIsWhereUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
		File.Copy(Application.ExecutablePath, text2, overwrite: true);
		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		processStartInfo.FileName = text2;
		processStartInfo.Arguments = "/remove \"" + text + "\"";
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		Process.Start(processStartInfo);
	}

	private static void RemoveInstalledFiles(string target)
	{
		if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
		{
			string text = Path.GetFullPath(target).TrimEnd('\\');
			string[] payloadNames = PayloadNames;
			foreach (string path in payloadNames)
			{
				TryDelete(Path.Combine(text, path));
			}
			TryDelete(Path.Combine(text, "AllIsWhereUninstall.exe"));
			SafeDeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AllIsWhere.lnk"));
			SafeDeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "AllIsWhere.lnk"));
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true))
			{
				registryKey?.DeleteValue("AllIsWhere", throwOnMissingValue: false);
			}
			try
			{
				Registry.CurrentUser.DeleteSubKeyTree("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllIsWhere", throwOnMissingSubKey: false);
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: false);
			}
			catch
			{
			}
			MessageBox.Show("AllIsWhere kaldırıldı.\r\n\r\nKullanıcı ayarlarınız korundu.", "AllIsWhere kaldırma", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	private static void RequestRunningApplicationExit()
	{
		Process[] processesByName = Process.GetProcessesByName("AllIsWhere");
		foreach (Process process in processesByName)
		{
			using (process)
			{
				if (process.Id == Process.GetCurrentProcess().Id)
				{
					continue;
				}
				int processId = process.Id;
				bool closeRequested = false;
				EnumWindows((IntPtr windowHandle, IntPtr state) =>
				{
					GetWindowThreadProcessId(windowHandle, out var processId2);
					if (processId2 == processId)
					{
						closeRequested |= PostMessage(windowHandle, 16u, IntPtr.Zero, IntPtr.Zero);
					}
					return true;
				}, IntPtr.Zero);
				if (!closeRequested)
				{
					process.CloseMainWindow();
				}
				if (process.WaitForExit(15000))
				{
					continue;
				}
				throw new TimeoutException("AllIsWhere uygulaması kapatılamadı. Uygulamayı kapatıp yeniden deneyin.");
			}
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
