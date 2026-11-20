# AllIsWhere

Windows 10/11 x64 için .NET 10 WPF dosya arama uygulaması.

- NTFS diskleri MFT üzerinden indekslenir ve USN Journal ile güncellenir.
- Yerel klasörler ve SMB/NAS paylaşımları arka planda taranır. İlk taramadan sonra klasör indeksi yerel önbelleğe alınır. Sonraki açılışlarda önbellekten arama yapılırken arka planda kaynak kontrol edilir.
- Ctrl+C seçili dosya ve klasörleri kopyalar. Ctrl+Shift+C seçili öğelerin yollarını metin olarak kopyalar.
- Arama, dosya türü, uzantı, kaynak, değiştirilme tarihi, minimum boyut ve hariç tutulan klasörlerle daraltılabilir. Aramalar kaydedilebilir.

## Derleme

Windows üzerinde .NET 10 SDK ile proje kökünde şu komutları çalıştırın.

```powershell
dotnet build .\AllIsWhere.csproj -c Release
dotnet build .\installer\AllIsWhere-Setup.csproj -c Release
```

Kurulum çıktısı `installer/bin/Release/net40/AllIsWhere-Setup.exe` yolundadır. Uygulama .NET 10 Windows Desktop Runtime x64 gerektirir ve NTFS disklerini okumak için yönetici izni ister.

[Yayımlanan güncellemeler](https://github.com/mertdmrc8/AllIsWhere-Updates/releases)
