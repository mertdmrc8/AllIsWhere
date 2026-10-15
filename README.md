# AllIsWhere

Windows 10/11 x64 için .NET 10 WPF masaüstü dosya arama uygulaması. Kurulumdan sonra program adı **AllIsWhere** olarak görünür.

## Özellikler

- `FSCTL_ENUM_USN_DATA` ile sabit NTFS volume'larda MFT kayıtlarından başlangıç indeksi oluşturur.
- `FSCTL_READ_USN_JOURNAL` ile NTFS volume değişikliklerini izler.
- **Klasör ekle…** penceresine yerel klasör, harici disk klasörü, eşlenmiş ağ sürücüsü (ör. `Z:\Belgeler`) veya UNC (`\\sunucu\paylaşım\...`) yolu yazılabilir ya da Gözat ile seçilebilir. Gözat, geçerli yol erişilebilirse o konumdan başlar. Harfli ağ sürücüleri etkin Windows eşlemelerinden ve kullanıcı profilindeki kayıtlı eşlemelerden bulunur; seçilen eşleme UNC kökünden indekslenir.
- Eklenen klasörleri arka planda dolaşır ve `FileSystemWatcher` ile canlı değişiklikleri indeksler.
- Birden fazla disk ve klasör kaynağında arar; kaynaklar listesinden seçip kaldırabilirsiniz.
- Eklediğiniz kaynak yollarını kullanıcı ayarlarında saklar ve uygulama yeniden açıldığında tekrar indeksler.
- Pencere kapatıldığında bildirim alanında arka planda çalışır; simge menüsünden yeniden açılabilir veya tamamen kapatılabilir.
- Yazarken arama, iptal edilebilir arama işleri ve kısa debounce uygular.
- WPF sonuç listesinde öğe sanallaştırma ve recycling kullanır.
- Dosya/klasör sonucuna çift tıklayınca açar.
- Açık tema varsayılandır; **Ayarlar** bölümünden tema, sonuç yazı boyutu, sonuç üst sınırı ve arama gecikmesi değiştirilebilir.
- Arama sonuçları tüm türler, yalnızca dosyalar veya yalnızca klasörler olarak daraltılabilir; açılır uzantı filtresinden yaygın uzantılar seçilebilir veya özel uzantılar girilebilir. Kaynak filtresi ve gizli klasörleri göster seçeneği de vardır.
- Windows oturum açılışında çalıştırma Ayarlar'dan açılıp kapatılabilir ve yalnızca geçerli kullanıcı için kaydedilir. Yeni kurulumda varsayılan olarak açılır.
- Klasör taramaları aynı anda birer tane yürütülür, arka plan thread önceliği düşürülür ve tarama döngüsünde kısa aralıklar bırakılır.
- **Ayarlar > Tarama performansı** seçeneğinde Hızlı, Dengeli veya Düşük kaynak modunu seçebilirsiniz. Dengeli mod varsayılandır.
- Uygulama EXE'si anahtar simgesi taşır. Klasör kaynağı taranırken pencerenin alt çubuğunda taranan kök klasör ve görülen öğe sayısı gösterilir.
- Ayarlar `%LOCALAPPDATA%\AllIsWhere\settings.json` dosyasında saklanır; önceki sürümün ayarları ilk açılışta aktarılır.

## Derleme ve çalıştırma

Visual Studio 2022'de `AllIsWhere.csproj` dosyasını açıp çalıştırın veya .NET 10 SDK bulunan Windows makinede:

```powershell
dotnet run --project .\AllIsWhere.csproj
```

## Kurulum paketi

Release derlemesinden sonra proje kökünde şu komutla kurulum EXE'sini üretin:

```powershell
dotnet build .\AllIsWhere.csproj --configuration Release
.\installer\Build-Setup.ps1
```

Paket `outputs\AllIsWhere-Setup-Update8.exe` konumuna yazılır. Kurulum arayüzünde hedef klasörü, masaüstü/başlat menüsü kısayollarını ve Windows açılışında başlatmayı seçebilirsiniz. Uygulama her açılışta Windows UAC üzerinden yönetici izni ister; açılışta başlatma seçiliyse oturum açılırken de bu izin sorulur. Uygulama .NET 10 Windows Desktop Runtime x64 gerektirir. Runtime bulunmazsa kurulum Microsoft'un resmi .NET indirme sayfasını açar. Kaldırma, Windows Uygulamalar listesine eklenen **AllIsWhere** girdisinden yapılır.

Uygulama manifesti her açılışta Windows UAC üzerinden yönetici izni ister. Uygulama USN Journal'ı kendisi oluşturmaz; var olan günlüğü okur.

## Mimari

- `Interop/NativeMethods.cs`: Win32 P/Invoke ve NTFS yapıları.
- `Engine/NtfsVolumeReader.cs`: MFT başlangıç taraması.
- `Engine/UsnJournalWatcher.cs`: journal değişikliklerini indekse uygulama.
- `Engine/FolderIndexingSession.cs`: klasör/ağ yolu taraması ve canlı izleme.
- `Engine/SearchIndex.cs`: File ID ve yol indeksleri, paralel arama.
- `MainWindow.xaml`: sanallaştırılmış WPF arayüzü.

NTFS MFT yolu USN v2 kayıtlarını işler; ReFS, USN v3 ve hard link'lerin ayrı yolları henüz desteklenmez. Ağ/diğer klasör kaynaklarında ilk tarama paylaşımın hızına ve erişim izinlerine bağlıdır. Bazı NAS veya ağ sağlayıcıları değişiklik bildirimini desteklemeyebilir; bu durumda ilk indeks yine oluşturulur, ancak canlı güncelleme sınırlı olabilir. İzleme tamponu taşarsa klasör kaynağı yeniden taranır.

## Antivirüs ve dağıtım

Uygulamanın veya kurulum paketinin Kaspersky ya da başka bir antivirüs tarafından nasıl sınıflandırılacağını garanti etmek mümkün değildir. Paket şu anda Authenticode ile imzalanmamıştır. Dağıtımda yayıncı kimliği sağlamak için güvenilir bir Authenticode sertifikasıyla imzalayın. Algılama olursa dosyayı Kaspersky'nin [Threat Intelligence Portal](https://opentip.kaspersky.com/) üzerinden incelemeye gönderin. Antivirüs korumasını kapatmayın ve uygulama için dışlama oluşturmayın.
## Kurulum dosyasını oluşturma

Release sürümünü derledikten sonra `installer/Build-Setup.ps1` çalıştırın. Üretilen update setup, Windows Forms kurulum penceresi açar; mevcut kurulum konumunu algılar ve kurulum konumu, masaüstü ve Başlat menüsü kısayolları ile Windows açılışında başlatma seçeneklerini sunar. Önceki setup dosyaları korunur. Güncellemeden önce AllIsWhere'i tamamen kapatın. Kurulum ekranı PowerShell betiği çalıştırmaz. Uygulama yönetici izni ister ve .NET 10 Desktop Runtime (x64) gerektirir.


