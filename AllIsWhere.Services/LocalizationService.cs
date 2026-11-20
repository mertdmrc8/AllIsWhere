using System;
using System.Collections.Generic;
using System.Globalization;

namespace AllIsWhere.Services;

public static class LocalizationService
{
	private static string _language = "tr";

	private static readonly IReadOnlyDictionary<string, (string Turkish, string English)> Strings = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
	{
		["AppName"] = ("AllIsWhere", "AllIsWhere"),
		["Tagline"] = ("Yerel disk, klasör ve ağ paylaşımında anlık arama", "Instant search across local drives, folders, and network shares"),
		["AddFolder"] = ("Klasör veya ağ ekle…", "Add folder or network…"),
		["Settings"] = ("Ayarlar", "Settings"),
		["CheckUpdates"] = ("Güncellemeleri denetle", "Check for updates"),
		["UpdateChecking"] = ("Güncelleme denetleniyor…", "Checking for updates…"),
		["UpdateCurrent"] = ("AllIsWhere güncel.", "AllIsWhere is up to date."),
		["UpdateCheckFailed"] = ("Güncelleme denetlenemedi: {0}", "Could not check for updates: {0}"),
		["UpdateAvailable"] = ("AllIsWhere {0} sürümü bulundu.\n\n{1}\n\nSetup şimdi indirilsin ve kurulum açılsın mı?", "AllIsWhere version {0} is available.\n\n{1}\n\nDownload the setup and open the installer now?"),
		["UpdateDownloading"] = ("AllIsWhere {0} indiriliyor…", "Downloading AllIsWhere {0}…"),
		["UpdateStarting"] = ("Kurulum açılıyor. AllIsWhere kurulum tamamlanana kadar kapanacak.", "Starting setup. AllIsWhere will close while the update is installed."),
		["UpdateDownloadFailed"] = ("Güncelleme indirilemedi veya doğrulanamadı: {0}", "The update could not be downloaded or verified: {0}"),
		["Sources"] = ("Kaynaklar", "Sources"),
		["ResultsHeading"] = ("Arama sonuçları", "Search results"),
		["ColumnName"] = ("Ad", "Name"),
		["ColumnPath"] = ("Konum", "Location"),
		["ColumnType"] = ("Tür", "Type"),
		["SortName"] = ("Ad · A–Z", "Name · A–Z"),
		["SortPath"] = ("Yol", "Path"),
		["SortType"] = ("Tür", "Type"),
		["FileType"] = ("Dosya", "File"),
		["FolderType"] = ("Klasör", "Folder"),
		["EmptyNoSourcesTitle"] = ("Önce bir kaynak ekleyin", "Add a source to get started"),
		["EmptyNoSourcesDescription"] = ("Yerel klasör, disk veya ağ konumu ekleyerek aramaya başlayın.", "Add a local folder, drive, or network location to start searching."),
		["EmptySearchTitle"] = ("Aramaya başlayın", "Start searching"),
		["EmptySearchDescription"] = ("Dosya veya klasör adını yazın. Ctrl+L arama alanına odaklanır.", "Type a file or folder name. Ctrl+L focuses the search box."),
		["EmptyNoResultsTitle"] = ("Sonuç bulunamadı", "No results found"),
		["EmptyNoResultsDescription"] = ("Farklı bir ad veya filtre deneyin.", "Try a different name or filter."),
		["IndexingTitle"] = ("Kaynaklar hazırlanıyor", "Preparing sources"),
		["IndexingDescription"] = ("İndeksleme tamamlandıkça arama sonuçları görünecek.", "Search results will appear as indexing completes."),
		["SearchingTitle"] = ("Aranıyor…", "Searching…"),
		["SearchingDescription"] = ("Sonuçlar hazır olduğunda burada görünecek.", "Results will appear here when ready."),
		["SearchSort"] = ("Sırala", "Sort"),
		["SearchHint"] = ("Dosya veya klasör adı yazın", "Type a file or folder name"),
		["SearchResultCount"] = ("{0:N0} sonuç", "{0:N0} results"),
		["RemoveSelected"] = ("Seçileni kaldır", "Remove selected"),
		["ConfirmRemoveMultiple"] = ("Seçili kaynakları kaldırmak istediğinizden emin misiniz?\n\n{0}", "Are you sure you want to remove the selected sources?\n\n{0}"),
		["SourcesRemoved"] = ("{0} kaynak kaldırıldı.", "{0} sources removed."),
		["SearchTooltip"] = ("Dosya adı yazın; ext:pdf, path:Belgeler veya name:rapor kullanabilirsiniz.", "Type a name; use ext:pdf, path:Documents, or name:report."),
		["RefreshSelected"] = ("Seçileni yenile", "Refresh selected"),
		["CacheLoaded"] = ("Önbellekten yüklendi · değişiklikler izleniyor", "Loaded from cache · watching for changes"),
		["Filter"] = ("Tür:", "Type:"),
		["AllTypes"] = ("Tüm türler", "All types"),
		["Files"] = ("Dosyalar", "Files"),
		["Folders"] = ("Klasörler", "Folders"),
		["ExtensionsAll"] = ("Uzantı: Tümü", "Extension: All"),
		["ExtensionsSelected"] = ("Uzantı: {0} seçili", "Extension: {0} selected"),
		["ExtensionPopupTitle"] = ("Uzantı filtresi", "Extension filter"),
		["CustomExtensions"] = ("Özel uzantılar (pdf; docx)", "Custom extensions (pdf; docx)"),
		["HiddenFolders"] = ("Gizli klasörleri göster", "Show hidden folders"),
		["ModifiedFilter"] = ("Değiştirilme tarihi", "Modified date"),
		["SizeFilter"] = ("Dosya boyutu", "File size"),
		["ModifiedAny"] = ("Her zaman", "Any time"),
		["ModifiedDay"] = ("Son 24 saat", "Last 24 hours"),
		["ModifiedWeek"] = ("Son 7 gün", "Last 7 days"),
		["ModifiedMonth"] = ("Son 30 gün", "Last 30 days"),
		["ModifiedYear"] = ("Son 365 gün", "Last 365 days"),
		["SizeAny"] = ("Her boyut", "Any size"),
		["SizeOver1Mb"] = ("1 MB üzeri", "Over 1 MB"),
		["SizeOver10Mb"] = ("10 MB üzeri", "Over 10 MB"),
		["SizeOver100Mb"] = ("100 MB üzeri", "Over 100 MB"),
		["SizeOver1Gb"] = ("1 GB üzeri", "Over 1 GB"),
		["SavedSearches"] = ("Kayıtlı aramalar", "Saved searches"),
		["SaveSearch"] = ("Aramayı kaydet", "Save search"),
		["RemoveSavedSearch"] = ("Kayıtlı aramayı sil", "Remove saved search"),
		["ConfirmRemoveSavedSearch"] = ("'{0}' kayıtlı aramasını silmek istiyor musunuz?", "Remove the saved search '{0}'?"),
		["SaveSearchPrompt"] = ("Bu arama için bir ad girin:", "Enter a name for this search:"),
		["ExcludedFolders"] = ("Hariç tutulan klasörler", "Excluded folders"),
		["ExcludedFoldersDescription"] = ("Her satıra bir klasör yolu yazın. Bu yolların içeriği arama sonuçlarında gösterilmez.", "Enter one folder path per line. Items inside these paths will be excluded from search results."),
		["AllSources"] = ("Tüm kaynaklar", "All sources"),
		["CopyItem"] = ("Kopyala", "Copy"),
        ["CopyItemSuccess"] = ("Öğeler panoya kopyalandı.", "Items copied to clipboard."),
        ["CopyItemFailed"] = ("Öğeler kopyalanamadı: {0}", "Could not copy items: {0}"),
        ["CopyPath"] = ("Yolu kopyala", "Copy path"),
		["OpenLocation"] = ("Dosya konumunu aç", "Open file location"),
		["Open"] = ("Aç", "Open"),
		["SettingsTitle"] = ("Görünüm ve arama", "Appearance and search"),
		["SettingsDescription"] = ("Değişiklikler bu bilgisayardaki kullanıcı hesabınıza kaydedilir.", "Changes are saved for your Windows user account."),
		["AppearanceSection"] = ("Görünüm", "Appearance"),
		["SearchSection"] = ("Arama ve tarama", "Search and indexing"),
		["StartupSection"] = ("Başlangıç ve dil", "Startup and language"),
		["Theme"] = ("Tema", "Theme"),
		["ThemeDescription"] = ("Açık tema daha yüksek okunabilirlik sunar.", "The light theme offers higher readability."),
		["Light"] = ("Açık", "Light"),
		["Dark"] = ("Koyu", "Dark"),
		["ResultFont"] = ("Sonuç yazı boyutu", "Result font size"),
		["Small"] = ("Küçük", "Small"),
		["Normal"] = ("Normal", "Normal"),
		["Large"] = ("Büyük", "Large"),
		["ExtraLarge"] = ("Çok büyük", "Extra large"),
		["MaxResults"] = ("En fazla sonuç", "Maximum results"),
		["MaxResultsDescription"] = ("Arayüzde gösterilecek üst sınır.", "Maximum number shown in the results list."),
		["SearchDelay"] = ("Arama gecikmesi", "Search delay"),
		["SearchDelayDescription"] = ("Yazarken aramadan önce beklenecek süre.", "Delay before searching while typing."),
		["Instant"] = ("Anında", "Instant"),
		["ScanPerformance"] = ("Tarama performansı", "Scan performance"),
		["ScanPerformanceDescription"] = ("Düşük kaynak kullanımı için tarama yavaşlayabilir.", "Scanning may slow down to reduce resource use."),
		["Fast"] = ("Hızlı", "Fast"),
		["Balanced"] = ("Dengeli", "Balanced"),
		["LowResource"] = ("Düşük kaynak", "Low resource"),
		["StartWithWindows"] = ("Windows oturum açılışında çalıştır", "Run when I sign in to Windows"),
		["Language"] = ("Dil", "Language"),
		["ShowHiddenSetting"] = ("Gizli klasörleri göster", "Show hidden folders"),
		["Cancel"] = ("Vazgeç", "Cancel"),
		["Save"] = ("Kaydet", "Save"),
		["AddSourceTitle"] = ("Klasör veya ağ kaynağı ekle", "Add folder or network source"),
		["AddSourceDescription"] = ("Yerel yol, bağlı sürücü (ör. Z:\\Belgeler) veya UNC ağ yolu kullanabilirsiniz.", "Enter a local path, mapped drive (e.g. Z:\\Documents), or UNC network path."),
		["Browse"] = ("Gözat…", "Browse…"),
		["PathHint"] = ("Örnek: \\\\sunucu\\paylaşım\\klasör veya Z:\\Belgeler", "Example: \\\\server\\share\\folder or Z:\\Documents"),
		["AddSource"] = ("Kaynağı ekle", "Add source"),
		["PathEmpty"] = ("Bir klasör yolu girin veya Gözat düğmesiyle seçin.", "Enter a folder path or choose one with Browse."),
		["PathEmptyTitle"] = ("Kaynak ekle", "Add source"),
		["NoSource"] = ("Disk seçin veya Klasör ekle ile bir kaynak ekleyin.", "Select a drive or add a folder source."),
		["CopySuccess"] = ("Yol panoya kopyalandı.", "Path copied to clipboard."),
		["MissingItem"] = ("Öğe artık mevcut değil.", "The item no longer exists."),
		["ConfirmRemoveTitle"] = ("Kaynağı kaldır", "Remove source"),
		["ConfirmRemove"] = ("'{0}' kaynağını listeden kaldırmak istediğinizden emin misiniz?\n\nKaynak yeniden eklenene kadar bu konum aranmayacak.", "Are you sure you want to remove '{0}' from the source list?\n\nThis location will not be searched until it is added again."),
		["TrayRunning"] = ("AllIsWhere arka planda çalışıyor", "AllIsWhere is running in the background"),
		["TrayOpen"] = ("AllIsWhere'i aç", "Open AllIsWhere"),
		["TrayExit"] = ("Tamamen kapat", "Exit completely"),
		["TrayNotice"] = ("Uygulama arka planda çalışmaya devam ediyor. Tamamen kapatmak için simgeye sağ tıklayın.", "The app is still running in the background. Right-click its icon to exit completely."),
		["FolderPickerTitle"] = ("Eklenecek yerel veya ağ klasörünü seçin", "Select a local or network folder to add"),
		["FolderPlaceholder"] = ("Örnek: \\\\sunucu\\paylaşım\\klasör", "Example: \\\\server\\share\\folder"),
		["RootPreparing"] = ("MFT indeksi hazırlanıyor…", "Preparing MFT index…"),
		["RootReady"] = ("hazır", "ready"),
		["RootScan"] = ("taranıyor…", "scanning…"),
		["RootIndexFailed"] = ("indekslenemedi: {0}", "index failed: {0}"),
		["FolderNotFound"] = ("Klasör bulunamadı veya erişilemiyor: {0}", "Folder not found or inaccessible: {0}"),
		["FolderScanFailed"] = ("taranamadı: {0}", "scan failed: {0}"),
		["NetworkAccessFailed"] = ("Ağ yoluna erişilemedi: {0}. Sunucu/paylaşım adını, bağlantıyı ve Windows erişim izinlerini kontrol edin. {1}", "Cannot access network path: {0}. Check the server/share name, connection, and Windows permissions. {1}"),
		["NetworkPathQueued"] = ("Ağ yolu kaynaklara eklendi: {0}, ancak şu an okunamıyor ({1}). Bağlantı ve izinleri denetleyip uygulamayı yeniden başlatın.", "Network path added: {0}, but it is not readable right now ({1}). Check the connection and permissions, then restart the app."),
		["NetworkPathUnavailable"] = ("Ağ erişimi bekleniyor", "Waiting for network access"),
		["NetworkDisconnected"] = ("Bağlantı kesildi", "Connection lost"),
		["FolderAccessFailed"] = ("Klasöre erişilemedi: {0}. Yolu ve erişim izinlerini kontrol edin. {1}", "Cannot access folder: {0}. Check the path and permissions. {1}"),
		["DriveUnavailable"] = ("Sürücüye erişilemiyor: {0}. Ağ bağlantısını denetleyin.", "Cannot access drive {0}. Check the network connection."),
		["DriveDisconnected"] = ("Bağlantı yok", "Disconnected"),
		["DriveInaccessible"] = ("Erişilemiyor", "Unavailable"),
		["DriveAccessDenied"] = ("Erişim yok", "Access denied"),
		["DriveStatusLabel"] = ("{0}: {1} · {2}", "{0}: {1} · {2}"),
		["NetworkDriveLabel"] = ("{0}:  {1} · Ağ sürücüsü", "{0}:  {1} · Network drive"),
		["DriveLabel"] = ("{0}: {1} · {2}", "{0}: {1} · {2}"),
		["InvalidFolderPath"] = ("Klasör yolu geçersiz: {0}", "Invalid folder path: {0}"),
		["MftPreparing"] = ("{0} MFT indeksi hazırlanıyor…", "Preparing MFT index for {0}…"),
		["MftReady"] = ("{0} hazır", "{0} ready"),
		["MftFailed"] = ("{0} indekslenemedi: {1}", "Could not index {0}: {1}"),
		["FolderScanning"] = ("{0} taranıyor…", "Scanning {0}…"),
		["FolderRootScanFailed"] = ("{0} taranamadı: {1}", "Could not scan {0}: {1}"),
		["Ready"] = ("Hazır", "Ready"),
		["Error"] = ("Hata", "Error"),
		["ItemOpenFailed"] = ("Öğe açılamadı: {0}", "Could not open item: {0}"),
		["TrayBalloon"] = ("Uygulama arka planda çalışmaya devam ediyor. Tamamen kapatmak için simgeye sağ tıklayın.", "The app keeps running in the background. Right-click its icon to exit completely."),
		["SourceRemoved"] = ("Kaynak kaldırıldı: {0}", "Source removed: {0}"),
		["SourceSaveFailed"] = ("Kaynak listesi kaydedilemedi: {0}", "Could not save source list: {0}"),
		["SettingsSaveFailed"] = ("Ayarlar uygulandı ancak kaydedilemedi: {0}", "Settings were applied but could not be saved: {0}"),
		["StartupApplyFailed"] = ("Başlangıç ayarı uygulanamadı: {0}", "Could not apply startup setting: {0}"),
		["StartupSaveFailed"] = ("Başlangıç ayarı Windows'a kaydedilemedi: {0}", "Could not save startup setting to Windows: {0}"),
		["FileOpenFailed"] = ("Öğe açılamadı: {0}", "Could not open item: {0}"),
		["LocationOpenFailed"] = ("Dosya konumu açılamadı: {0}", "Could not open file location: {0}"),
		["CopyFailed"] = ("Yol panoya kopyalanamadı: {0}", "Could not copy path: {0}"),
		["VolumeStatus"] = ("{0}: {1}", "{0}: {1}"),
		["ScanProgress"] = ("{0} taranıyor · {1:N0} öğe", "Scanning {0} · {1:N0} items"),
		["ResultCount"] = ("{0:N0} sonuç · {1} kaynak", "{0:N0} results · {1} sources"),
		["RecordCount"] = ("{0:N0} kayıt · {1} kaynak", "{0:N0} items · {1} sources"),
		["SourceCount"] = ("{0} kaynak", "{0} sources"),
		["IndexChanged"] = ("{0}: {1}", "{0}: {1}"),
		["WatcherStopped"] = ("{0} canlı güncelleme durdu: {1}", "{0} live updates stopped: {1}"),
		["WatcherError"] = ("Güncelleme durdu", "Updates stopped"),
		["MftUsn"] = ("MFT + USN", "MFT + USN"),
		["FolderSource"] = ("Klasör", "Folder"),
		["Preparing"] = ("Hazırlanıyor", "Preparing"),
		["LiveWatch"] = ("Canlı izleme", "Live monitoring"),
		["LiveWatchUnavailable"] = ("Canlı izleme yok", "Live monitoring unavailable"),
		["FolderWatching"] = ("Klasör izleniyor", "Monitoring folder"),
		["FolderWatchUnavailable"] = ("Canlı izleme açılamadı; ilk tarama sürüyor ({0})", "Live monitoring could not start; initial scan is continuing ({0})"),
		["FolderReadyWatching"] = ("Hazır · canlı izleme", "Ready · live monitoring"),
		["FolderScanComplete"] = ("Tarama tamamlandı · canlı izleme yok", "Scan complete · live monitoring unavailable"),
		["FolderReadWarning"] = ("Bazı klasörler okunamadı: {0}", "Some folders could not be read: {0}"),
		["WatcherOverflow"] = ("Dosya değişiklik kuyruğu taştı; indeks yeniden taranıyor.", "Change notification queue overflowed; rebuilding the index."),
		["ScanSettingFailed"] = ("Tarama ayarı uygulanamadı: {0}", "Could not apply scan setting: {0}"),
		["RemoveHelp"] = ("Kaynak yeniden eklenene kadar bu konum aranmayacak.", "This location will not be searched until it is added again.")
	};

	public static string Language => _language;

	public static void SetLanguage(string? language)
	{
		_language = (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr");
	}

	public static string Get(string key, params object?[] arguments)
	{
		if (!Strings.TryGetValue(key, out (string, string) value))
		{
			return key;
		}
		string text;
		if (!(_language == "en"))
		{
			(text, _) = value;
		}
		else
		{
			text = value.Item2;
		}
		string text2 = text;
		if (arguments.Length != 0)
		{
			return string.Format(CultureInfo.CurrentCulture, text2, arguments);
		}
		return text2;
	}
}
