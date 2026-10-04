using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PulseOverlay;

/// <summary>
/// UI strings in Turkish and English. XAML binds through <see cref="LExtension"/> ({local:L key}),
/// code uses <see cref="T"/> / <see cref="F"/>. Switching language updates bound text immediately.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public static string Language { get; private set; } = "tr";

    /// <summary>Raised after the language changed, for text that code sets directly.</summary>
    public static event Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => T(key);

    public static string T(string key) =>
        (Language == "en" ? En : Tr).TryGetValue(key, out var text) ? text
        : Tr.TryGetValue(key, out var fallback) ? fallback : key;

    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    /// <summary>Turkish on Turkish Windows, English everywhere else.</summary>
    public static string DefaultLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    public static void Set(string language)
    {
        Language = language == "en" ? "en" : "tr";
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    static readonly Dictionary<string, string> Tr = new()
    {
        ["app.tagline"] = "Saatinden canlı nabız → OBS overlay",
        ["app.alreadyRunning"] = "Pulse Overlay zaten çalışıyor.",
        ["app.unexpected"] = "Beklenmeyen hata: {0}",
        ["lang.tip"] = "Arayüz dili",

        ["server.starting"] = "Sunucu başlatılıyor…",
        ["server.ready"] = "Sunucu hazır · localhost:{0}",
        ["server.failed"] = "Sunucu başlatılamadı",
        ["server.portBusy"] = "Port {0} kullanılamıyor ({1}). Başka bir program bu portu kullanıyor olabilir.",
        ["obs.clients"] = "OBS bağlantısı:",

        ["hero.noWatch"] = "Henüz saat eklenmedi",
        ["hero.notConnected"] = "Saat bağlı değil",
        ["hero.stats"] = "Min {0}   ·   Ort {1}   ·   Maks {2}",
        ["hero.records"] = "😱 Bu yayında {0} korku   ·   👑 Rekor {1}",
        ["hero.recordsSim"] = "😱 Bu yayında {0} korku   ·   simülatör rekor kaydetmez",
        ["sim.label"] = "Simülatör",
        ["sim.tip"] = "Saat olmadan overlay'i denemek için sahte nabız üretir",
        ["btn.newStream"] = "Yeni yayın başlat",
        ["btn.newStream.tip"] = "Min / ort / maks, grafik, yayın rekoru ve korku sayacını sıfırlar",
        ["btn.resetRecord"] = "Rekoru sıfırla",
        ["btn.resetRecord.tip"] = "Tüm zamanların rekorunu siler (records.json)",

        ["saved.title"] = "KAYITLI SAATLER",
        ["saved.subtitle"] = "Bağlantı koparsa otomatik olarak yeniden bağlanır.",
        ["saved.empty"] = "Henüz kayıtlı saat yok. Sağdaki “Tara” ile saatini bul ve bağlan.",
        ["row.connect"] = "Bağlan",
        ["row.disconnect"] = "Kes",
        ["row.overlayUrl"] = "Overlay adresi",
        ["row.overlayUrl.tip"] = "Sadece bu saati gösteren overlay adresini kopyala",
        ["row.forget"] = "Unut",
        ["row.forget.tip"] = "Bu saati listeden kaldır",

        ["state.live"] = "Canlı",
        ["state.waiting"] = "Veri bekleniyor",
        ["state.connecting"] = "Bağlanıyor",
        ["state.lost"] = "Sinyal yok",
        ["state.reconnecting"] = "Yeniden bağlanıyor",
        ["state.off"] = "Bağlı değil",
        ["stateLong.live"] = "Canlı · {0}",
        ["stateLong.liveSim"] = "Canlı · sahte veri",
        ["stateLong.waiting"] = "Bağlandı · saatten veri bekleniyor",
        ["stateLong.connecting"] = "Bağlanıyor…",
        ["stateLong.lost"] = "Sinyal yok · veri bekleniyor",
        ["stateLong.reconnecting"] = "Yeniden bağlanıyor · {0}. deneme",
        ["stateLong.starting"] = "Başlatılıyor…",
        ["stateLong.off"] = "Bağlı değil",

        ["scan.title"] = "SAAT BUL",
        ["scan.hint"] = "Saatte: Ayarlar → HR veri yayını → Aç. Sonra “Tara”ya bas.",
        ["scan.hintScanning"] = "Taranıyor… Saatin listede görünmüyorsa HR veri yayınını kapatıp tekrar aç.",
        ["scan.scan"] = "Tara",
        ["scan.stop"] = "Durdur",
        ["scan.emptyIdle"] = "Bulunan cihazlar burada listelenir.",
        ["scan.searching"] = "Aranıyor…",
        ["scan.noWearable"] = "Saat gibi görünen bir cihaz yok. “Tüm cihazları göster”i dene.",
        ["scan.saved"] = "Kayıtlı",
        ["scan.hr"] = "♥ Nabız",
        ["scan.showAll"] = "Tüm cihazları göster",
        ["scan.connect"] = "Bağlan",
        ["scan.hrDevice"] = "Nabız cihazı ({0})",
        ["scan.err.start"] = "Bluetooth taraması başlatılamadı: {0}",
        ["scan.err.radio"] = "Bluetooth kapalı ya da bulunamadı. Windows'ta Bluetooth'u aç.",
        ["scan.err.policy"] = "Bluetooth erişimi Windows ayarlarında kapalı.",
        ["scan.err.busy"] = "Bluetooth başka bir uygulama tarafından kullanılıyor.",
        ["scan.err.other"] = "Bluetooth taraması durdu ({0}).",

        ["overlay.title"] = "OBS OVERLAY",
        ["overlay.howto"] = "OBS → Kaynaklar → + → Tarayıcı, sonra bu adresi yapıştır.",
        ["overlay.copy"] = "Kopyala",
        ["overlay.customize"] = "Overlay'i özelleştir",
        ["overlay.customize.tip"] = "Tema, renk, grafik ve efektleri tarayıcıda canlı önizlemeyle ayarla",
        ["overlay.openBrowser"] = "Tarayıcıda aç",

        ["status.preparing"] = "Hazırlanıyor…",
        ["status.startSaved"] = "Saatte HR veri yayınını aç, sonra kayıtlı saatinde “Bağlan”a bas.",
        ["status.startNew"] = "Başlamak için “Tara”ya bas ve saatini seç. Saatte HR veri yayını açık olmalı.",
        ["status.langChanged"] = "Dil Türkçe olarak ayarlandı.",
        ["status.reconnected"] = "{0}: yeniden bağlandı, veri geliyor ✓",
        ["status.live"] = "{0}: canlı nabız geliyor ✓",
        ["status.signalBack"] = "{0}: sinyal geri geldi ✓",
        ["status.signalLost"] = "{0}: birkaç saniyedir veri yok, bekleniyor…",
        ["status.waiting"] = "{0}: bağlandı, saatten veri bekleniyor…",
        ["status.dropped"] = "{0}: {1}. Otomatik yeniden bağlanılıyor…",
        ["status.droppedDefault"] = "bağlantı koptu",
        ["status.seenAs"] = "{0}: “{1}” olarak görüldü, bağlanılıyor…",
        ["status.scare"] = "😱 {0}: korku anı! Nabız birden {1} arttı ({2} BPM).",
        ["status.recordAll"] = "👑 {0}: tüm zamanların rekoru, {1} BPM!",
        ["status.recordSession"] = "🏆 {0}: yayının yeni rekoru, {1} BPM.",
        ["status.connecting"] = "{0}: bağlanılıyor…",
        ["status.disconnecting"] = "{0}: bağlantı kesiliyor…",
        ["status.disconnected"] = "{0}: bağlantı kesildi.",
        ["status.urlCopiedDevice"] = "{0} için overlay adresi kopyalandı.",
        ["status.forgotten"] = "{0} unutuldu.",
        ["status.simOn"] = "Simülatör açık: overlay sahte nabız gösteriyor.",
        ["status.simOff"] = "Simülatör kapatıldı.",
        ["status.newStream"] = "Yeni yayın: istatistikler, grafik, yayın rekoru ve korku sayacı sıfırlandı.",
        ["status.recordReset"] = "Tüm zamanların rekoru sıfırlandı.",
        ["status.urlCopied"] = "Adres kopyalandı. OBS'te Tarayıcı kaynağına yapıştır.",
        ["status.browserFailed"] = "Tarayıcı açılamadı: {0}",
        ["status.clipboardBusy"] = "Pano şu an meşgul, tekrar dene.",
        ["status.closing"] = "Kapatılıyor…",
        ["confirm.forget"] = "“{0}” unutulsun mu?",
        ["confirm.resetRecord"] = "Tüm zamanların nabız rekoru silinsin mi?\nBu oturumdaki değerler yeni rekor olarak sessizce kaydedilir.",

        ["err.notFound"] = "Saat bulunamadı (kapsama dışında ya da HR yayını kapalı)",
        ["err.unreachableHr"] = "Saate ulaşılamadı, saatte HR veri yayını açık mı?",
        ["err.unreachable"] = "Saate ulaşılamadı ({0})",
        ["err.noService"] = "Nabız servisi yok, saatte HR veri yayını açık mı?",
        ["err.noCharacteristic"] = "Nabız ölçüm verisi bulunamadı",
        ["err.notify"] = "Bildirimler açılamadı ({0})",
        ["err.refused"] = "Saat bağlantıyı kabul etmedi, saatte HR veri yayını açık mı?",
        ["err.outOfRange"] = "Saat kapsama dışında",
        ["err.notFoundShort"] = "Saat bulunamadı",
        ["err.btOff"] = "Bluetooth kapalı",
        ["err.closed"] = "Bağlantı kapandı",
        ["err.dropped"] = "Bağlantı koptu",
        ["err.noData"] = "Saatten veri gelmedi",
        ["err.dataStopped"] = "Veri akışı durdu",
        ["gatt.unreachable"] = "erişilemiyor",
        ["gatt.denied"] = "erişim reddedildi",
        ["gatt.protocol"] = "protokol hatası",
    };

    static readonly Dictionary<string, string> En = new()
    {
        ["app.tagline"] = "Live heart rate from your watch → OBS overlay",
        ["app.alreadyRunning"] = "Pulse Overlay is already running.",
        ["app.unexpected"] = "Unexpected error: {0}",
        ["lang.tip"] = "Interface language",

        ["server.starting"] = "Starting server…",
        ["server.ready"] = "Server ready · localhost:{0}",
        ["server.failed"] = "Server failed to start",
        ["server.portBusy"] = "Port {0} is unavailable ({1}). Another program may be using it.",
        ["obs.clients"] = "OBS connections:",

        ["hero.noWatch"] = "No watch added yet",
        ["hero.notConnected"] = "Watch not connected",
        ["hero.stats"] = "Min {0}   ·   Avg {1}   ·   Max {2}",
        ["hero.records"] = "😱 {0} scares this stream   ·   👑 Record {1}",
        ["hero.recordsSim"] = "😱 {0} scares this stream   ·   the simulator doesn't set records",
        ["sim.label"] = "Simulator",
        ["sim.tip"] = "Generates fake heart rate to try the overlay without a watch",
        ["btn.newStream"] = "Start new stream",
        ["btn.newStream.tip"] = "Resets min / avg / max, graph, stream record and scare counter",
        ["btn.resetRecord"] = "Reset record",
        ["btn.resetRecord.tip"] = "Deletes the all-time record (records.json)",

        ["saved.title"] = "SAVED WATCHES",
        ["saved.subtitle"] = "Reconnects automatically if the connection drops.",
        ["saved.empty"] = "No saved watches yet. Find your watch with “Scan” on the right and connect.",
        ["row.connect"] = "Connect",
        ["row.disconnect"] = "Disconnect",
        ["row.overlayUrl"] = "Overlay URL",
        ["row.overlayUrl.tip"] = "Copy the overlay URL that shows only this watch",
        ["row.forget"] = "Forget",
        ["row.forget.tip"] = "Remove this watch from the list",

        ["state.live"] = "Live",
        ["state.waiting"] = "Waiting for data",
        ["state.connecting"] = "Connecting",
        ["state.lost"] = "No signal",
        ["state.reconnecting"] = "Reconnecting",
        ["state.off"] = "Not connected",
        ["stateLong.live"] = "Live · {0}",
        ["stateLong.liveSim"] = "Live · fake data",
        ["stateLong.waiting"] = "Connected · waiting for data from the watch",
        ["stateLong.connecting"] = "Connecting…",
        ["stateLong.lost"] = "No signal · waiting for data",
        ["stateLong.reconnecting"] = "Reconnecting · attempt {0}",
        ["stateLong.starting"] = "Starting…",
        ["stateLong.off"] = "Not connected",

        ["scan.title"] = "FIND WATCH",
        ["scan.hint"] = "On the watch: Settings → HR data broadcasts → On. Then press “Scan”.",
        ["scan.hintScanning"] = "Scanning… If your watch doesn't show up, turn HR data broadcasts off and on again.",
        ["scan.scan"] = "Scan",
        ["scan.stop"] = "Stop",
        ["scan.emptyIdle"] = "Devices found will be listed here.",
        ["scan.searching"] = "Searching…",
        ["scan.noWearable"] = "Nothing that looks like a watch. Try “Show all devices”.",
        ["scan.saved"] = "Saved",
        ["scan.hr"] = "♥ Heart rate",
        ["scan.showAll"] = "Show all devices",
        ["scan.connect"] = "Connect",
        ["scan.hrDevice"] = "Heart rate device ({0})",
        ["scan.err.start"] = "Couldn't start the Bluetooth scan: {0}",
        ["scan.err.radio"] = "Bluetooth is off or missing. Turn on Bluetooth in Windows.",
        ["scan.err.policy"] = "Bluetooth access is turned off in Windows settings.",
        ["scan.err.busy"] = "Bluetooth is being used by another app.",
        ["scan.err.other"] = "Bluetooth scan stopped ({0}).",

        ["overlay.title"] = "OBS OVERLAY",
        ["overlay.howto"] = "OBS → Sources → + → Browser, then paste this URL.",
        ["overlay.copy"] = "Copy",
        ["overlay.customize"] = "Customize overlay",
        ["overlay.customize.tip"] = "Set theme, colors, graph and effects in the browser with a live preview",
        ["overlay.openBrowser"] = "Open in browser",

        ["status.preparing"] = "Getting ready…",
        ["status.startSaved"] = "Turn on HR data broadcasts on the watch, then press “Connect” on your saved watch.",
        ["status.startNew"] = "To start, press “Scan” and pick your watch. HR data broadcasts must be on.",
        ["status.langChanged"] = "Language set to English.",
        ["status.reconnected"] = "{0}: reconnected, data is flowing ✓",
        ["status.live"] = "{0}: live heart rate is coming in ✓",
        ["status.signalBack"] = "{0}: signal is back ✓",
        ["status.signalLost"] = "{0}: no data for a few seconds, waiting…",
        ["status.waiting"] = "{0}: connected, waiting for data from the watch…",
        ["status.dropped"] = "{0}: {1}. Reconnecting automatically…",
        ["status.droppedDefault"] = "connection lost",
        ["status.seenAs"] = "{0}: seen as “{1}”, connecting…",
        ["status.scare"] = "😱 {0}: scare moment! Heart rate jumped by {1} ({2} BPM).",
        ["status.recordAll"] = "👑 {0}: all-time record, {1} BPM!",
        ["status.recordSession"] = "🏆 {0}: new stream record, {1} BPM.",
        ["status.connecting"] = "{0}: connecting…",
        ["status.disconnecting"] = "{0}: disconnecting…",
        ["status.disconnected"] = "{0}: disconnected.",
        ["status.urlCopiedDevice"] = "Overlay URL for {0} copied.",
        ["status.forgotten"] = "{0} forgotten.",
        ["status.simOn"] = "Simulator on: the overlay shows fake heart rate.",
        ["status.simOff"] = "Simulator off.",
        ["status.newStream"] = "New stream: stats, graph, stream record and scare counter reset.",
        ["status.recordReset"] = "All-time record reset.",
        ["status.urlCopied"] = "URL copied. Paste it into a Browser source in OBS.",
        ["status.browserFailed"] = "Couldn't open the browser: {0}",
        ["status.clipboardBusy"] = "The clipboard is busy, try again.",
        ["status.closing"] = "Closing…",
        ["confirm.forget"] = "Forget “{0}”?",
        ["confirm.resetRecord"] = "Delete the all-time heart rate record?\nThis session's values will quietly become the new record.",

        ["err.notFound"] = "Watch not found (out of range or HR broadcast off)",
        ["err.unreachableHr"] = "Couldn't reach the watch. Is HR data broadcast on?",
        ["err.unreachable"] = "Couldn't reach the watch ({0})",
        ["err.noService"] = "No heart rate service. Is HR data broadcast on?",
        ["err.noCharacteristic"] = "Heart rate measurement not found",
        ["err.notify"] = "Couldn't enable notifications ({0})",
        ["err.refused"] = "The watch refused the connection. Is HR data broadcast on?",
        ["err.outOfRange"] = "Watch out of range",
        ["err.notFoundShort"] = "Watch not found",
        ["err.btOff"] = "Bluetooth is off",
        ["err.closed"] = "Connection closed",
        ["err.dropped"] = "Connection lost",
        ["err.noData"] = "No data from the watch",
        ["err.dataStopped"] = "Data stopped flowing",
        ["gatt.unreachable"] = "unreachable",
        ["gatt.denied"] = "access denied",
        ["gatt.protocol"] = "protocol error",
    };
}

/// <summary>XAML: Text="{local:L scan.title}" — stays in sync with the selected language.</summary>
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }
    public LExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
