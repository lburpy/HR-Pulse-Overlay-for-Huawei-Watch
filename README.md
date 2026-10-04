# Pulse Overlay

Huawei saatinden (Watch Fit 5 ve standart BLE nabız servisi sunan her cihaz) canlı nabzı okuyup
OBS'te özelleştirilebilir bir overlay olarak gösteren Windows uygulaması.

## Özellikler

**Kendini toparlayan bağlantı**
- Bağlantı koparsa ya da saat veri göndermeyi bırakırsa (Huawei'nin HR yayını bazen sessizce durur)
  otomatik olarak yeniden bağlanır: 2 → 4 → 8 → 15 → 30 sn aralıklarla, durdurulana kadar.
- Saat yeniden görünür görünmez (Bluetooth reklamı yakalanınca) beklemeyi atlayıp hemen bağlanır.
- 5 sn veri gelmezse "Sinyal yok", 20 sn gelmezse bağlantı baştan kurulur.
- Saatler hatırlanır; uygulama açılınca listede durur, "Bağlan"a basınca bağlanır
  (açılışta kendiliğinden bağlanmaz).
- Huawei saatler HR yayınında ayrı bir adresle görünür; bağlantı koparsa uygulama saati bu
  adresten de tanıyıp yeniden bağlanır.
- Bağlantı adımları ve hatalar `%APPDATA%\PulseOverlay\log.txt` dosyasına yazılır.

**Korku anı ve rekorlar**
- Nabız ~10 saniyede 20+ artarsa "😱 Korktu!" efekti ve yayın boyunca artan korku sayacı.
- Yayın rekoru (ilk 2 dakikadan sonra, ortalamanın 15 üstünde) ve tüm zamanların rekoru için
  altın rozet. Tüm zamanların rekoru yalnızca bu bilgisayarda, `%APPDATA%\PulseOverlay\records.json`
  dosyasında tutulur; "Rekoru sıfırla" ile silinir. "Yeni yayın başlat" yayın rekorunu ve sayacı sıfırlar.
- Overlay her durumu gösterir: Canlı / Sinyal yok / Yeniden bağlanıyor / Bağlı değil / Uygulama kapalı.

**Overlay**
- 5 tema: Kart, Sade (oyun üstü), Neon, Gösterge (dairesel), EKG monitörü
- Canlı nabız grafiği (30 sn – 5 dk), min / ort / maks, 5'li nabız bölgesi çubuğu
- Renk nabız bölgesine göre değişir ya da sabit renk; yazı tipi, boyut, arka plan, köşe, hizalama
- Kalp atışı nabızla senkron atar; sayı yumuşak geçer
- Uyarı eşiği: nabız X'i geçince parlama ve/veya titreme
- Sinyal yokken overlay'i otomatik gizleme
- Türkçe / İngilizce; internet gerektirmez (sistem fontları, SVG kalp)

## Kullanım

1. Saatte **Ayarlar → HR veri yayını → Aç**.
2. Uygulamada **Tara** → saatini seç → **Bağlan**.
3. **Overlay'i özelleştir** → tarayıcıda düzenleyici açılır. Tema ve ayarları seç, **Adresi kopyala**.
4. OBS → Kaynaklar → **+** → **Tarayıcı** → adresi yapıştır, genişlik/yüksekliği düzenleyicinin
   önerdiği boyuta ayarla.

Saat olmadan denemek için uygulamada **Simülatör**'ü aç ya da düzenleyicide **Demo veri** kullan.

## Adresler

| Adres | İçerik |
|-------|--------|
| `http://localhost:8790/` | Overlay (son aktif saat) |
| `http://localhost:8790/overlay/<ID>` | Sadece o saatin overlay'i |
| `http://localhost:8790/editor` | Overlay düzenleyici |
| `http://localhost:8790/api/state` | Tüm cihazların anlık durumu (JSON) |
| `ws://localhost:8790/ws` | Canlı veri akışı |

Overlay seçenekleri URL parametreleridir (`theme`, `color`, `accent`, `graph`, `stats`, `zonebar`,
`maxhr`, `alert`, `hideoff` …); hepsini düzenleyici üretir.
Ayarlar `%APPDATA%\PulseOverlay\settings.json` dosyasında tutulur.

## Derleme

.NET 10 SDK gerekir.

```powershell
dotnet run

# Tek dosya EXE (.NET çalışma zamanı dahil)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Overlay/düzenleyici dosyalarını değiştirirken yeniden derlemeden denemek için
`PULSEOVERLAY_WWWROOT` ortam değişkenini `wwwroot` klasörüne ayarla; sunucu dosyaları diskten okur.

## Yapı

```
Core/
  DeviceConnection.cs   Tek saat bağlantısı: bağlan, dinle, kopunca yeniden dene
  BleScanner.cs         Tarama + yeniden bağlanırken arka planda saati bekleme
  HeartRateParser.cs    Bluetooth nabız paketini (0x2A37) çözme
  HeartRateHub.cs       Tüm cihazların durumu, istatistik, geçmiş → overlay mesajları
  Simulator.cs          Sahte nabız kaynağı
  AppSettings.cs        Kayıtlı saatler
Server/OverlayServer.cs Tek port: overlay, düzenleyici, JSON API, WebSocket
wwwroot/                overlay.html/css/js, editor.html/css/js (EXE'ye gömülü)
MainWindow.xaml(.cs)    Kontrol paneli
```
