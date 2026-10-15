// Ahmetview (Mac): sade fotoğraf ve video görüntüleyici.
//
// Windows sürümünün Mac karşılığı. Xcode projesi yok: GitHub'ın Mac
// makinesinde swiftc ile derlenip .app paketine konuyor (bkz. derle.sh).
// Ekranın tamamını kaplayan çerçevesiz bir pencere; fotoğraf ortada,
// çevresi buzlu cam. Fotoğraf ve video katmanları ekran kartında
// birleştiriliyor, sürüklerken her karede yeniden çizim yok.

import AppKit
import AVFoundation
import AVKit
import ImageIO
import QuickLookThumbnailing
import UniformTypeIdentifiers

let fotoUzantilari: Set<String> = ["jpg", "jpeg", "png", "webp", "heic", "heif"]
// Mac'in kendi oynatıcısı MKV, AVI ve WebM açamıyor; listede yoklar.
let videoUzantilari: Set<String> = ["mp4", "m4v", "mov"]

func videoMu(_ url: URL) -> Bool { videoUzantilari.contains(url.pathExtension.lowercased()) }

func destekleniyor(_ url: URL) -> Bool {
    let u = url.pathExtension.lowercased()
    return fotoUzantilari.contains(u) || videoUzantilari.contains(u)
}

// MARK: - Uygulama

final class Temsilci: NSObject, NSApplicationDelegate {
    var pencere: AnaPencere?
    var sahne: Sahne?
    var bekleyen: URL?

    func applicationDidFinishLaunching(_ n: Notification) {
        menuKur()
        let fare = NSEvent.mouseLocation
        let ekran = NSScreen.screens.first { NSMouseInRect(fare, $0.frame, false) } ?? NSScreen.main!
        let p = AnaPencere(contentRect: ekran.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.isReleasedWhenClosed = false
        p.title = "Ahmetview"
        let s = Sahne(frame: NSRect(origin: .zero, size: ekran.frame.size))
        p.contentView = s
        p.setFrame(ekran.frame, display: false)
        p.makeKeyAndOrderFront(nil)
        p.makeFirstResponder(s)
        pencere = p
        sahne = s
        NSApp.activate(ignoringOtherApps: true)
        if let u = bekleyen {
            s.ac(u)
        } else {
            // Finder'dan çift tıklanan dosya bazen pencere açıldıktan hemen
            // sonra geliyor; kart o arada bir an görünmesin diye biraz
            // bekleniyor, dosya gelirse hiç gösterilmiyor.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                s.tanitimHazir = true
                s.yerlestir()
            }
        }
        Varsayilan.ilkSefer()
    }

    // Finder'dan çift tıklama ve "Birlikte Aç" buraya geliyor; uygulama
    // açılırken gelirse pencere henüz yok, bekletiliyor.
    func application(_ sender: NSApplication, open urls: [URL]) {
        guard let u = urls.first else { return }
        if let s = sahne { s.ac(u) } else { bekleyen = u }
    }

    // Menü çubuğu ve Dock, Ahmetview öndeyken gizleniyor; ekranın tamamı
    // buzlu cam olsun diye.
    func applicationDidBecomeActive(_ n: Notification) {
        NSApp.presentationOptions = [.autoHideMenuBar, .autoHideDock]
    }

    func applicationWillResignActive(_ n: Notification) {
        NSApp.presentationOptions = []
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ s: NSApplication) -> Bool { true }

    func menuKur() {
        let ana = NSMenu()
        let uygulamaOgesi = NSMenuItem()
        ana.addItem(uygulamaOgesi)
        let um = NSMenu()
        um.addItem(withTitle: "Varsayılan Uygulama Yap", action: #selector(varsayilanYap), keyEquivalent: "")
        um.addItem(.separator())
        um.addItem(withTitle: "Ahmetview'dan Çık", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        uygulamaOgesi.submenu = um
        let dosyaOgesi = NSMenuItem()
        ana.addItem(dosyaOgesi)
        let dm = NSMenu(title: "Dosya")
        dm.addItem(withTitle: "Aç…", action: #selector(dosyaAc), keyEquivalent: "o")
        dosyaOgesi.submenu = dm
        NSApp.mainMenu = ana
    }

    @objc func dosyaAc() { sahne?.dosyaSec() }
    @objc func varsayilanYap() { Varsayilan.yap() }
}

// Çerçevesiz pencere normalde klavye almıyor; Esc ve oklar için gerekli.
final class AnaPencere: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

// Yalnız görüntü olan katmanlar: tıklamalar altlarındaki sahneye gitsin.
class GecirgenGorunum: NSView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

final class GecirgenBulanik: NSVisualEffectView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

// MARK: - Varsayılan uygulama

enum Varsayilan {
    static var turler: [UTType] {
        var t: [UTType] = [.jpeg, .png, .webP, .heic, .mpeg4Movie, .quickTimeMovie]
        if let m4v = UTType("com.apple.m4v-video") { t.append(m4v) }
        return t
    }

    static func yap() {
        let uygulama = Bundle.main.bundleURL
        for t in turler {
            NSWorkspace.shared.setDefaultApplication(at: uygulama, toOpen: t) { _ in }
        }
    }

    // Yalnız bir kez soruluyor; sonradan menüden ya da sağ tıkla yapılabilir.
    static func ilkSefer() {
        let d = UserDefaults.standard
        if d.bool(forKey: "soruldu") { return }
        d.set(true, forKey: "soruldu")
        let a = NSAlert()
        a.messageText = "Fotoğraflar ve videolar Ahmetview'da açılsın mı?"
        a.informativeText = "JPG, PNG, WebP, HEIC, MP4 ve MOV dosyalarına çift tıklayınca Ahmetview açılır. Sonradan sağ tık menüsünden de yapılabilir."
        a.addButton(withTitle: "Evet")
        a.addButton(withTitle: "Hayır")
        if a.runModal() == .alertFirstButtonReturn { yap() }
    }
}

// MARK: - Sahne

final class Sahne: NSView {
    let acilisOrani: CGFloat = 0.8
    let kucukEn: CGFloat = 128, kucukBoy: CGFloat = 72, kucukAralik: CGFloat = 10
    let seritAlt: CGFloat = 20, seritOrani: CGFloat = 0.6
    var seritYeri: CGFloat { kucukBoy + seritAlt + 24 }
    let kapatBoyut: CGFloat = 36, kapatPay: CGFloat = 14

    var dosyalar: [URL] = []
    var sira = -1
    var resim: CGImage?
    var video = false
    // Gösterilenin boyutu, ekran noktası cinsinden; ölçek 1 iken bir resim
    // pikseli bir ekran pikseline düşüyor.
    var icerik = CGSize(width: 1280, height: 720)
    var olcek: CGFloat = 1
    var konum = CGPoint.zero
    var sigdir = true
    var hata: String?
    var yuklemeNo = 0
    var tanitimHazir = false

    let bulanik = GecirgenBulanik()
    let karartma = GecirgenGorunum()
    let foto = GecirgenGorunum()
    let videoKutu = NSView()
    let videoKapak = NSView()
    var oynatici: AVPlayer?
    var oynaticiGorunum: AVPlayerView?
    var sonGozlemci: NSObjectProtocol?
    let serit = SeritGorunum()
    let kapat = KapatGorunum()
    let oran = OranGorunum()
    let tanitim = TanitimGorunum()
    let hataYazisi = NSTextField(labelWithString: "")
    var oranIsi: DispatchWorkItem?

    var kucukler: [URL: CGImage] = [:]
    var istenen: Set<URL> = []
    var seritKayma: CGFloat = 0
    var kaymaBaslangic: CGFloat = 0
    var animBaslangic: CFTimeInterval = 0
    var animZamani: Timer?

    var basildi = false
    var basma = CGPoint.zero
    var basmaKonum = CGPoint.zero
    var suruklendi = false
    var sagTekerKullanildi = false
    var tekerBirikim: CGFloat = 0
    var jestKullanildi = false
    var sonYanaGecis: TimeInterval = 0

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    var olcekKatsayisi: CGFloat { window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2 }
    var icerikVar: Bool { resim != nil || video }
    var icerikAlani: CGRect {
        CGRect(x: konum.x, y: konum.y, width: icerik.width * olcek, height: icerik.height * olcek)
    }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true

        bulanik.frame = bounds
        bulanik.autoresizingMask = [.width, .height]
        bulanik.material = .hudWindow
        bulanik.blendingMode = .behindWindow
        bulanik.state = .active
        bulanik.appearance = NSAppearance(named: .darkAqua)
        addSubview(bulanik)

        karartma.frame = bounds
        karartma.autoresizingMask = [.width, .height]
        karartma.wantsLayer = true
        karartma.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.3).cgColor
        addSubview(karartma)

        foto.wantsLayer = true
        foto.layer?.contentsGravity = .resize
        foto.layer?.minificationFilter = .trilinear
        golgeVer(foto)
        foto.isHidden = true
        addSubview(foto)

        videoKutu.wantsLayer = true
        videoKutu.layer?.backgroundColor = NSColor.black.cgColor
        golgeVer(videoKutu)
        videoKutu.isHidden = true
        addSubview(videoKutu)

        addSubview(tanitim)

        hataYazisi.textColor = NSColor.white.withAlphaComponent(0.9)
        hataYazisi.font = NSFont.systemFont(ofSize: 15)
        hataYazisi.alignment = .center
        hataYazisi.isHidden = true
        addSubview(hataYazisi)

        serit.sahne = self
        serit.isHidden = true
        addSubview(serit)

        oran.alphaValue = 0
        addSubview(oran)

        addSubview(kapat)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) { fatalError() }

    func golgeVer(_ v: NSView) {
        guard let k = v.layer else { return }
        k.masksToBounds = false
        k.shadowColor = NSColor.black.cgColor
        k.shadowOpacity = 0.55
        k.shadowRadius = 22
        k.shadowOffset = CGSize(width: 0, height: -6)
    }

    // MARK: Dosyalar

    func ac(_ url: URL) {
        let hedef = url.resolvingSymlinksInPath().standardizedFileURL
        let klasor = hedef.deletingLastPathComponent()
        let icindekiler = (try? FileManager.default.contentsOfDirectory(
            at: klasor, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])) ?? []
        // Finder'ın sıralaması: "foto2" "foto10"dan önce.
        dosyalar = icindekiler.filter(destekleniyor).map { $0.standardizedFileURL }.sorted {
            $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending
        }
        // Klasör okunamadıysa (izin verilmediyse) en azından açılan dosya.
        if dosyalar.isEmpty && destekleniyor(hedef) { dosyalar = [hedef] }
        sira = dosyalar.firstIndex { $0.path == hedef.path } ?? (dosyalar.isEmpty ? -1 : 0)
        kucukler.removeAll()
        istenen.removeAll()
        seritKayma = 0
        yukle()
    }

    func gec(_ yon: Int) {
        guard !dosyalar.isEmpty else { return }
        siraDegistir((sira + yon + dosyalar.count) % dosyalar.count)
    }

    func siraDegistir(_ yeni: Int) {
        let fark = yeni - sira
        sira = yeni
        // Baştan sona sarmada şerit uzun yol kaymasın, olduğu yere atlasın.
        if fark != 0 && abs(fark) <= 3 {
            seritKayma += CGFloat(fark) * (kucukEn + kucukAralik)
            kaymaBaslangic = seritKayma
            animBaslangic = CACurrentMediaTime()
            animBaslat()
        } else {
            seritKayma = 0
        }
        yukle()
    }

    func yukle() {
        yuklemeNo += 1
        let no = yuklemeNo
        hata = nil
        videoKapat()
        guard sira >= 0 && sira < dosyalar.count else {
            resim = nil
            video = false
            yerlestir()
            return
        }
        let url = dosyalar[sira]
        window?.title = url.lastPathComponent
        if videoMu(url) {
            resim = nil
            video = true
            icerik = CGSize(width: 1280, height: 720)
            videoAc(url, no)
            sigdirHesapla()
            yerlestir()
            return
        }
        video = false
        // Büyük bir fotoğrafı çözmek onlarca milisaniye sürüyor; arka
        // planda yapılıyor ki tekerlekle hızlı geçişte arayüz takılmasın.
        DispatchQueue.global(qos: .userInitiated).async {
            let r = Sahne.resimOku(url)
            DispatchQueue.main.async {
                guard no == self.yuklemeNo else { return }
                if let r = r {
                    self.resim = r
                    let k = self.olcekKatsayisi
                    self.icerik = CGSize(width: CGFloat(r.width) / k, height: CGFloat(r.height) / k)
                    self.foto.layer?.contents = r
                } else {
                    self.resim = nil
                    self.hata = url.lastPathComponent + " açılamadı."
                }
                self.sigdirHesapla()
                self.yerlestir()
            }
        }
        yerlestir()
    }

    // Telefon fotoğraflarının dönüşü (EXIF) burada uygulanıyor. Ekran
    // kartının doku sınırı yüzünden uzun kenar en çok 8192 piksel.
    static func resimOku(_ url: URL) -> CGImage? {
        guard let kaynak = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let ozellik = CGImageSourceCopyPropertiesAtIndex(kaynak, 0, nil) as? [CFString: Any]
        let en = ozellik?[kCGImagePropertyPixelWidth] as? Int ?? 0
        let boy = ozellik?[kCGImagePropertyPixelHeight] as? Int ?? 0
        let secenek: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: min(max(en, boy, 1), 8192),
            kCGImageSourceShouldCacheImmediately: true,
        ]
        return CGImageSourceCreateThumbnailAtIndex(kaynak, 0, secenek as CFDictionary)
    }

    func dosyaSec() {
        let p = NSOpenPanel()
        p.allowedContentTypes = Varsayilan.turler
        p.allowsMultipleSelection = false
        p.begin { [weak self] cevap in
            if cevap == .OK, let u = p.url { self?.ac(u) }
        }
    }

    // MARK: Video

    func videoAc(_ url: URL, _ no: Int) {
        let o = AVPlayer(url: url)
        oynatici = o
        let g = AVPlayerView(frame: videoKutu.bounds)
        g.autoresizingMask = [.width, .height]
        g.controlsStyle = .floating
        g.showsFullScreenToggleButton = false
        g.videoGravity = .resizeAspect
        g.player = o
        videoKutu.addSubview(g)
        oynaticiGorunum = g
        // Oynatıcının üstünde, alttaki denetim çubuğu hariç her yeri örten
        // boş bir katman: tıklama, sürükleme ve tekerlek sahneye gitsin.
        videoKapak.frame = NSRect(x: 0, y: 70, width: videoKutu.bounds.width,
                                  height: max(0, videoKutu.bounds.height - 70))
        videoKapak.autoresizingMask = [.width, .height]
        videoKutu.addSubview(videoKapak)
        // Sonuna gelince baştan.
        sonGozlemci = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: o.currentItem, queue: .main
        ) { [weak o] _ in
            o?.seek(to: .zero)
            o?.play()
        }
        o.play()

        // Gerçek boyut ve dönüş (dikey telefon videosu) dosyadan okunuyor.
        let varlik = AVURLAsset(url: url)
        Task { [weak self] in
            guard let iz = try? await varlik.loadTracks(withMediaType: .video).first,
                  let bilgi = try? await iz.load(.naturalSize, .preferredTransform) else { return }
            let r = CGRect(origin: .zero, size: bilgi.0).applying(bilgi.1)
            await MainActor.run {
                guard let s = self, no == s.yuklemeNo else { return }
                let k = s.olcekKatsayisi
                s.icerik = CGSize(width: abs(r.width) / k, height: abs(r.height) / k)
                s.sigdirHesapla()
                s.yerlestir()
            }
        }
    }

    func videoKapat() {
        oynatici?.pause()
        oynatici = nil
        oynaticiGorunum?.removeFromSuperview()
        oynaticiGorunum = nil
        videoKapak.removeFromSuperview()
        if let g = sonGozlemci {
            NotificationCenter.default.removeObserver(g)
            sonGozlemci = nil
        }
    }

    func oynatDuraklat() {
        guard let o = oynatici else { return }
        if o.rate == 0 { o.play() } else { o.pause() }
    }

    // MARK: Yerleşim

    func sigdirHesapla() {
        sigdir = true
        guard icerikVar, icerik.width > 0, icerik.height > 0 else { return }
        // Şerit varken fotoğraf ona binmesin: alttan ve (ortalı dursun diye)
        // üstten şeridin yüksekliği kadar pay.
        let pay: CGFloat = dosyalar.count > 1 ? 2 * seritYeri : 0
        let alanBoy = min(bounds.height * acilisOrani, bounds.height - pay)
        let s = min(bounds.width * acilisOrani / icerik.width, alanBoy / icerik.height)
        // Küçük fotoğraf büyütülüp bulanıklaştırılmıyor.
        olcek = min(s, 1)
        ortala()
    }

    func ortala() {
        konum = CGPoint(x: (bounds.width - icerik.width * olcek) / 2,
                        y: (bounds.height - icerik.height * olcek) / 2)
    }

    func yerlestir() {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let alan = icerikAlani

        foto.isHidden = video || resim == nil
        if !foto.isHidden {
            foto.frame = alan
            foto.layer?.shadowPath = CGPath(rect: foto.bounds, transform: nil)
            foto.layer?.magnificationFilter = olcek >= 3 ? .nearest : .linear
        }
        videoKutu.isHidden = !video
        if video {
            videoKutu.frame = alan
            videoKutu.layer?.shadowPath = CGPath(rect: videoKutu.bounds, transform: nil)
        }

        // Kart yalnız hiç dosya istenmediyse; dosya yüklenirken görünmesin.
        tanitim.isHidden = !tanitimHazir || icerikVar || hata != nil || !dosyalar.isEmpty
        if !tanitim.isHidden {
            let b = tanitim.boyut
            tanitim.frame = NSRect(x: (bounds.width - b.width) / 2, y: (bounds.height - b.height) / 2,
                                   width: b.width, height: b.height)
        }
        hataYazisi.isHidden = icerikVar || hata == nil
        if let h = hata {
            hataYazisi.stringValue = h
            hataYazisi.frame = NSRect(x: 40, y: bounds.height / 2 - 20, width: bounds.width - 80, height: 40)
        }

        seritYerlestir()
        kapat.frame = kapatAlani()
        kapat.isHidden = false
        CATransaction.commit()
    }

    // Gösterilenin (ya da tanıtım kartının) sağ köşesinin biraz üstünde.
    // Üstte yer yoksa (büyütülüp ekranın tepesine dayandıysa) içeriye.
    func kapatAlani() -> NSRect {
        let f: CGRect = icerikVar ? icerikAlani : (tanitim.isHidden ? .zero : tanitim.frame)
        if f.isEmpty {
            return NSRect(x: bounds.width - kapatPay - kapatBoyut, y: kapatPay, width: kapatBoyut, height: kapatBoyut)
        }
        var x = f.maxX - kapatBoyut
        var y = f.minY - kapatBoyut - 10
        if y < kapatPay {
            x = f.maxX - kapatPay - kapatBoyut
            y = f.minY + kapatPay
        }
        x = max(kapatPay, min(x, bounds.width - kapatPay - kapatBoyut))
        y = max(kapatPay, min(y, bounds.height - kapatPay - kapatBoyut))
        return NSRect(x: x, y: y, width: kapatBoyut, height: kapatBoyut)
    }

    func seritYerlestir() {
        let n = dosyalar.count
        guard n >= 2 else {
            serit.isHidden = true
            return
        }
        let adim = kucukEn + kucukAralik
        var sigan = Int((bounds.width * seritOrani + kucukAralik) / adim)
        if sigan % 2 == 0 { sigan -= 1 }          // ortası olsun diye tek
        sigan = max(1, min(sigan, 2 * n - 1))
        serit.sigan = sigan
        let en = CGFloat(sigan) * adim - kucukAralik
        serit.frame = NSRect(x: (bounds.width - en) / 2 - 8, y: bounds.height - seritAlt - kucukBoy - 8,
                             width: en + 16, height: kucukBoy + 16)
        serit.isHidden = false
        // Önce açık olana en yakın olanlar istensin.
        let yarim = sigan / 2
        for d in 0...(yarim + 1) {
            for i in [sira + d, sira - d] where i >= 0 && i < n {
                kucukIste(dosyalar[i])
            }
        }
        serit.needsDisplay = true
    }

    // Önizlemeler Finder'ın kendi küçük resim servisinden: fotoğraf ve
    // video için aynı yol, Windows'taki gibi ayrı iş parçacığı gerekmiyor.
    func kucukIste(_ url: URL) {
        guard !istenen.contains(url) else { return }
        istenen.insert(url)
        let istek = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 256, height: 256),
                                                 scale: olcekKatsayisi, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: istek) { [weak self] temsil, _ in
            guard let r = temsil?.cgImage else { return }
            DispatchQueue.main.async {
                self?.kucukler[url] = r
                self?.serit.needsDisplay = true
            }
        }
    }

    func animBaslat() {
        if animZamani != nil { return }
        let t = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] t in
            guard let s = self else {
                t.invalidate()
                return
            }
            let g = (CACurrentMediaTime() - s.animBaslangic) / 0.32
            if g >= 1 {
                s.seritKayma = 0
                t.invalidate()
                s.animZamani = nil
            } else {
                // Yavaşlayarak duran kayma (kübik).
                let k = CGFloat(1 - g)
                s.seritKayma = s.kaymaBaslangic * k * k * k
            }
            s.serit.needsDisplay = true
        }
        RunLoop.main.add(t, forMode: .common)
        animZamani = t
    }

    // MARK: Zum

    func yakinlas(_ carpan: CGFloat, _ merkez: CGPoint) {
        guard icerikVar else { return }
        let yeni = max(0.01, min(50, olcek * carpan))
        let k = yeni / olcek
        konum = CGPoint(x: merkez.x - (merkez.x - konum.x) * k, y: merkez.y - (merkez.y - konum.y) * k)
        olcek = yeni
        sigdir = false
        yerlestir()
        oranGoster()
    }

    func gercekBoyut() {
        guard icerikVar else { return }
        olcek = 1
        sigdir = false
        ortala()
        yerlestir()
        oranGoster()
    }

    // Ekranın ortasında, zum değişince bir süre görünüp kayboluyor.
    func oranGoster() {
        oran.yazi = "%\(Int((olcek * 100).rounded()))"
        let b = oran.boyut
        oran.frame = NSRect(x: (bounds.width - b.width) / 2, y: (bounds.height - b.height) / 2,
                            width: b.width, height: b.height)
        oran.needsDisplay = true
        oran.alphaValue = 1
        oranIsi?.cancel()
        let isi = DispatchWorkItem { [weak self] in
            NSAnimationContext.runAnimationGroup { c in
                c.duration = 0.35
                self?.oran.animator().alphaValue = 0
            }
        }
        oranIsi = isi
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2, execute: isi)
    }

    // Sığdırılmışsa gerçek boyuta (tıklanan nokta yerinde kalarak),
    // yakınlaştırılmışsa ilk görünüme.
    func ciftTik(_ p: CGPoint) {
        if !icerikVar {
            dosyaSec()
            return
        }
        // Çift tıkın ilk tıkı videoyu duraklatıp açtı; geri alınıyor.
        if video { oynatDuraklat() }
        if sigdir {
            yakinlas(1 / olcek, p)
        } else {
            sigdirHesapla()
            yerlestir()
            oranGoster()
        }
    }

    // MARK: Fare ve klavye

    override func mouseDown(with e: NSEvent) {
        let p = convert(e.locationInWindow, from: nil)
        if e.clickCount == 2 {
            basildi = false
            ciftTik(p)
            return
        }
        basildi = true
        basma = p
        basmaKonum = konum
        suruklendi = false
    }

    override func mouseDragged(with e: NSEvent) {
        guard basildi else { return }
        let p = convert(e.locationInWindow, from: nil)
        if !suruklendi && hypot(p.x - basma.x, p.y - basma.y) < 4 { return }
        suruklendi = true
        guard icerikVar else { return }
        konum = CGPoint(x: basmaKonum.x + p.x - basma.x, y: basmaKonum.y + p.y - basma.y)
        sigdir = false
        yerlestir()
    }

    // Kıpırdatmadan bırakılan tık: dumanlı alandaysa kapatıyor, videonun
    // üstündeyse duraklatıyor.
    override func mouseUp(with e: NSEvent) {
        guard basildi else { return }
        basildi = false
        if suruklendi { return }
        let alan: CGRect = icerikVar ? icerikAlani : (tanitim.isHidden ? .zero : tanitim.frame)
        if alan.isEmpty { return }
        if !alan.contains(basma) {
            NSApp.terminate(nil)
        } else if video {
            oynatDuraklat()
        }
    }

    override func scrollWheel(with e: NSEvent) {
        let p = convert(e.locationInWindow, from: nil)
        // Cihazın kendi yönü: "doğal kaydırma" açık olsa da tekerleği
        // yukarı çevirmek önceki fotoğraf olsun.
        var dy = e.scrollingDeltaY, dx = e.scrollingDeltaX
        if e.isDirectionInvertedFromDevice {
            dy = -dy
            dx = -dx
        }
        let sagBasili = NSEvent.pressedMouseButtons & 0x2 != 0
        if e.modifierFlags.contains(.command) || e.modifierFlags.contains(.control) || sagBasili {
            if sagBasili { sagTekerKullanildi = true }
            let adim = e.hasPreciseScrollingDeltas ? dy / 40 : dy
            if adim != 0 { yakinlas(pow(1.2, adim), p) }
            return
        }
        if e.hasPreciseScrollingDeltas {
            // İzleme dörtgeni: bir kaydırma hareketi tek dosya geçirsin;
            // ardından gelen savrulma (momentum) olayları yok sayılıyor.
            if e.phase.contains(.began) {
                tekerBirikim = 0
                jestKullanildi = false
            }
            if jestKullanildi { return }
            tekerBirikim += abs(dx) > abs(dy) ? -dx : dy
            if tekerBirikim > 40 {
                jestKullanildi = true
                gec(-1)
            } else if tekerBirikim < -40 {
                jestKullanildi = true
                gec(1)
            }
            return
        }
        // Tekerleği yana yatırmak: basılı tutulunca sürekli olay geliyor.
        if dx != 0 && dy == 0 {
            let simdi = Date().timeIntervalSince1970
            if simdi - sonYanaGecis > 0.3 {
                sonYanaGecis = simdi
                gec(dx > 0 ? -1 : 1)
            }
            return
        }
        tekerBirikim += dy
        while tekerBirikim >= 1 {
            tekerBirikim -= 1
            gec(-1)
        }
        while tekerBirikim <= -1 {
            tekerBirikim += 1
            gec(1)
        }
    }

    // İzleme dörtgeninde kıstırma.
    override func magnify(with e: NSEvent) {
        yakinlas(1 + e.magnification, convert(e.locationInWindow, from: nil))
    }

    override func rightMouseDown(with e: NSEvent) {
        sagTekerKullanildi = false
    }

    // Sağ tuş + tekerlekle zum yapıldıysa menü açılmasın.
    override func rightMouseUp(with e: NSEvent) {
        if !sagTekerKullanildi {
            NSMenu.popUpContextMenu(baglamMenusu(), with: e, for: self)
        }
        sagTekerKullanildi = false
    }

    func baglamMenusu() -> NSMenu {
        let m = NSMenu()
        m.addItem(withTitle: "Aç…", action: #selector(dosyaSecEylem), keyEquivalent: "").target = self
        m.addItem(withTitle: "Varsayılan uygulama yap", action: #selector(varsayilanEylem), keyEquivalent: "").target = self
        m.addItem(.separator())
        m.addItem(withTitle: "Kapat", action: #selector(kapatEylem), keyEquivalent: "").target = self
        return m
    }

    @objc func dosyaSecEylem() { dosyaSec() }
    @objc func varsayilanEylem() { Varsayilan.yap() }
    @objc func kapatEylem() { NSApp.terminate(nil) }

    override func keyDown(with e: NSEvent) {
        let merkez = CGPoint(x: bounds.midX, y: bounds.midY)
        switch e.keyCode {
        case 53: NSApp.terminate(nil)                  // Esc
        case 123, 116: gec(-1)                         // sol ok, Page Up
        case 124, 121: gec(1)                          // sağ ok, Page Down
        case 49: if video { oynatDuraklat() } else { gec(1) }   // boşluk
        default:
            guard e.modifierFlags.contains(.command), let c = e.charactersIgnoringModifiers else {
                super.keyDown(with: e)
                return
            }
            switch c {
            case "0":
                sigdirHesapla()
                yerlestir()
                oranGoster()
            case "1": gercekBoyut()
            case "=", "+": yakinlas(1.2, merkez)
            case "-": yakinlas(1 / 1.2, merkez)
            default: super.keyDown(with: e)
            }
        }
    }

    // MARK: Sürükle bırak

    override func draggingEntered(_ s: NSDraggingInfo) -> NSDragOperation { .copy }

    override func performDragOperation(_ s: NSDraggingInfo) -> Bool {
        let secenek: [NSPasteboard.ReadingOptionKey: Any] = [.urlReadingFileURLsOnly: true]
        guard let url = (s.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: secenek) as? [URL])?.first
        else { return false }
        ac(url)
        return true
    }
}

// MARK: - Önizleme şeridi

// Açık olan hep tam ortada, tam boyda ve çerçeveli; diğerleri %20 küçük ve
// soluk. Kayarken boy ve soluklık ortaya uzaklığa göre yumuşakça değişiyor.
final class SeritGorunum: NSView {
    weak var sahne: Sahne?
    var sigan = 1
    var kutular: [(Int, NSRect)] = []

    override var isFlipped: Bool { true }

    override func draw(_ kirli: NSRect) {
        guard let s = sahne else { return }
        kutular.removeAll()
        let arka = NSBezierPath(roundedRect: bounds, xRadius: 12, yRadius: 12)
        NSColor.black.withAlphaComponent(0.35).setFill()
        arka.fill()
        arka.addClip()

        let adim = s.kucukEn + s.kucukAralik
        let merkezX = bounds.midX, merkezY = bounds.midY
        let n = s.dosyalar.count
        let yarim = sigan / 2
        let kenar = yarim + 1 + Int((abs(s.seritKayma) / adim).rounded(.up))
        // Uzaktakiler önce: ortadaki en son çizilip üstte kalıyor.
        for d in stride(from: kenar, through: 0, by: -1) {
            for yon in [-1, 1] {
                if d == 0 && yon == 1 { continue }
                let i = s.sira + d * yon
                guard i >= 0 && i < n else { continue }
                let cx = merkezX + CGFloat(i - s.sira) * adim + s.seritKayma
                let uzaklik = min(1, abs(cx - merkezX) / adim)
                let o = 1 - 0.2 * uzaklik
                let kutu = NSRect(x: cx - s.kucukEn * o / 2, y: merkezY - s.kucukBoy * o / 2,
                                  width: s.kucukEn * o, height: s.kucukBoy * o)
                if kutu.maxX < 0 || kutu.minX > bounds.width { continue }
                kutular.append((i, kutu))

                let yol = NSBezierPath(roundedRect: kutu, xRadius: 6, yRadius: 6)
                NSGraphicsContext.saveGraphicsState()
                yol.addClip()
                if let k = s.kucukler[s.dosyalar[i]] {
                    // Kareyi doldurup taşanı kırparak.
                    let ks = CGSize(width: k.width, height: k.height)
                    let olc = max(kutu.width / ks.width, kutu.height / ks.height)
                    let hedef = NSRect(x: kutu.midX - ks.width * olc / 2, y: kutu.midY - ks.height * olc / 2,
                                       width: ks.width * olc, height: ks.height * olc)
                    NSImage(cgImage: k, size: ks).draw(in: hedef, from: .zero, operation: .sourceOver,
                                                        fraction: 1, respectFlipped: true, hints: nil)
                } else {
                    NSColor.white.withAlphaComponent(0.15).setFill()
                    yol.fill()
                }
                if videoMu(s.dosyalar[i]) { oynatSimgesi(kutu) }
                if uzaklik > 0 {
                    NSColor.black.withAlphaComponent(0.47 * uzaklik).setFill()
                    yol.fill()
                }
                NSGraphicsContext.restoreGraphicsState()
                if uzaklik < 1 {
                    NSColor.white.withAlphaComponent(1 - uzaklik).setStroke()
                    yol.lineWidth = 2
                    yol.stroke()
                }
            }
        }
    }

    func oynatSimgesi(_ kutu: NSRect) {
        let r = kutu.height / 6
        let ucgen = NSBezierPath()
        ucgen.move(to: NSPoint(x: kutu.midX - r * 0.7, y: kutu.midY - r))
        ucgen.line(to: NSPoint(x: kutu.midX - r * 0.7, y: kutu.midY + r))
        ucgen.line(to: NSPoint(x: kutu.midX + r, y: kutu.midY))
        ucgen.close()
        NSColor.white.withAlphaComponent(0.9).setFill()
        ucgen.fill()
    }

    override func mouseDown(with e: NSEvent) {
        let p = convert(e.locationInWindow, from: nil)
        // Sondan başa: ortadaki en son çizildi, üstte o.
        for (i, k) in kutular.reversed() where k.contains(p) {
            if let s = sahne, i != s.sira { s.siraDegistir(i) }
            return
        }
    }
}

// MARK: - Kapat düğmesi

final class KapatGorunum: NSView {
    var ustunde = false {
        didSet { needsDisplay = true }
    }

    override func draw(_ kirli: NSRect) {
        let daire = NSBezierPath(ovalIn: bounds.insetBy(dx: 1, dy: 1))
        (ustunde ? NSColor(calibratedRed: 0.77, green: 0.17, blue: 0.11, alpha: 0.92)
                 : NSColor.black.withAlphaComponent(0.55)).setFill()
        daire.fill()
        let d: CGFloat = 12
        let x = NSBezierPath()
        x.move(to: NSPoint(x: d, y: d))
        x.line(to: NSPoint(x: bounds.width - d, y: bounds.height - d))
        x.move(to: NSPoint(x: bounds.width - d, y: d))
        x.line(to: NSPoint(x: d, y: bounds.height - d))
        x.lineWidth = 2.5
        x.lineCapStyle = .round
        NSColor.white.setStroke()
        x.stroke()
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways],
                                       owner: self, userInfo: nil))
    }

    override func mouseEntered(with e: NSEvent) { ustunde = true }
    override func mouseExited(with e: NSEvent) { ustunde = false }
    override func mouseDown(with e: NSEvent) { NSApp.terminate(nil) }
}

// MARK: - Zum göstergesi

final class OranGorunum: GecirgenGorunum {
    var yazi = "%100"
    let yaziTipi = NSFont.systemFont(ofSize: 24, weight: .medium)

    var boyut: NSSize {
        let o = (yazi as NSString).size(withAttributes: [.font: yaziTipi])
        return NSSize(width: ceil(o.width) + 36, height: ceil(o.height) + 16)
    }

    override func draw(_ kirli: NSRect) {
        let yol = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 12, yRadius: 12)
        NSColor.black.withAlphaComponent(0.45).setFill()
        yol.fill()
        NSColor.white.withAlphaComponent(0.28).setStroke()
        yol.stroke()
        let orta = NSMutableParagraphStyle()
        orta.alignment = .center
        let o = (yazi as NSString).size(withAttributes: [.font: yaziTipi])
        (yazi as NSString).draw(in: NSRect(x: 0, y: (bounds.height - o.height) / 2, width: bounds.width, height: o.height),
                                withAttributes: [.font: yaziTipi, .foregroundColor: NSColor.white, .paragraphStyle: orta])
    }
}

// MARK: - Tanıtım kartı

// Dosyasız açılınca ortada duran kart: ne olduğu ve neden farklı olduğu.
// Fotoğraf gibi davranıyor: köşesinde çarpı var, dışına tıklamak kapatıyor.
final class TanitimGorunum: GecirgenGorunum {
    static let ozellikler: [(String, String, String)] = [
        ("bolt.fill", "Hafif ve hızlı", "Tek uygulama, kurulum yok, 1 MB'tan küçük. Anında açılır."),
        ("sparkles", "Modern görünüm", "Çerçevesiz pencere, buzlu cam arka plan, yumuşak geçişler."),
        ("computermouse.fill", "Her şey farede", "Tekerlek ya da iki parmak: sonraki, önceki. ⌘ ya da kıstırma: zum."),
        ("play.rectangle.fill", "Fotoğraf ve video", "JPG, PNG, WebP, HEIC, MP4 ve MOV."),
    ]
    static let altYazi = "Mac için sade ve hızlı fotoğraf, video görüntüleyici"
    static let ipucu = "Açmak için bir dosyayı buraya bırak ya da karta çift tıkla"

    let baslikYazi = NSFont.systemFont(ofSize: 30, weight: .semibold)
    let altYaziTipi = NSFont.systemFont(ofSize: 14)
    let kalin = NSFont.systemFont(ofSize: 14, weight: .semibold)
    let aciklama = NSFont.systemFont(ofSize: 13)
    let ipucuYazi = NSFont.systemFont(ofSize: 12)
    let ic: CGFloat = 36, ikonBoy: CGFloat = 96, sol: CGFloat = 48, simgeYeri: CGFloat = 40

    override var isFlipped: Bool { true }

    func olc(_ s: String, _ f: NSFont) -> NSSize { (s as NSString).size(withAttributes: [.font: f]) }

    // Yazıların ölçüsüne göre: hiçbir açıklama taşmasın.
    var boyut: NSSize {
        var en: CGFloat = 560
        en = max(en, olc(Self.altYazi, altYaziTipi).width + 2 * ic)
        for o in Self.ozellikler { en = max(en, olc(o.2, aciklama).width + 2 * sol + simgeYeri) }
        let satir = olc("Ag", kalin).height + olc("Ag", aciklama).height + 12
        let boy = ic + ikonBoy + 16 + olc("A", baslikYazi).height + olc("A", altYaziTipi).height + 24
            + CGFloat(Self.ozellikler.count) * satir + 12 + olc("A", ipucuYazi).height + ic
        return NSSize(width: ceil(en), height: ceil(boy))
    }

    override func draw(_ kirli: NSRect) {
        let kart = bounds
        let yol = NSBezierPath(roundedRect: kart.insetBy(dx: 0.5, dy: 0.5), xRadius: 20, yRadius: 20)
        NSColor(calibratedRed: 0.07, green: 0.07, blue: 0.09, alpha: 0.6).setFill()
        yol.fill()
        NSColor.white.withAlphaComponent(0.2).setStroke()
        yol.stroke()

        let orta = NSMutableParagraphStyle()
        orta.alignment = .center
        var y = ic
        NSApp.applicationIconImage.draw(in: NSRect(x: (kart.width - ikonBoy) / 2, y: y, width: ikonBoy, height: ikonBoy),
                                        from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        y += ikonBoy + 16
        let bh = olc("A", baslikYazi).height
        ("Ahmetview" as NSString).draw(in: NSRect(x: 0, y: y, width: kart.width, height: bh), withAttributes: [
            .font: baslikYazi, .foregroundColor: NSColor.white, .paragraphStyle: orta])
        y += bh
        let ah = olc("A", altYaziTipi).height
        (Self.altYazi as NSString).draw(in: NSRect(x: 0, y: y, width: kart.width, height: ah), withAttributes: [
            .font: altYaziTipi, .foregroundColor: NSColor.white.withAlphaComponent(0.8), .paragraphStyle: orta])
        y += ah + 24

        let vurgu = NSColor(calibratedRed: 0.47, green: 0.67, blue: 1, alpha: 1)
        let kh = olc("Ag", kalin).height, ach = olc("Ag", aciklama).height
        for o in Self.ozellikler {
            let ayar = NSImage.SymbolConfiguration(pointSize: 17, weight: .regular)
                .applying(NSImage.SymbolConfiguration(paletteColors: [vurgu]))
            if let simge = NSImage(systemSymbolName: o.0, accessibilityDescription: nil)?.withSymbolConfiguration(ayar) {
                let sb = simge.size
                simge.draw(in: NSRect(x: sol + (26 - sb.width) / 2, y: y + 2 + (22 - sb.height) / 2, width: sb.width, height: sb.height),
                           from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
            }
            (o.1 as NSString).draw(at: NSPoint(x: sol + simgeYeri, y: y), withAttributes: [
                .font: kalin, .foregroundColor: NSColor.white])
            (o.2 as NSString).draw(at: NSPoint(x: sol + simgeYeri, y: y + kh), withAttributes: [
                .font: aciklama, .foregroundColor: NSColor.white.withAlphaComponent(0.75)])
            y += kh + ach + 12
        }
        y += 12
        (Self.ipucu as NSString).draw(in: NSRect(x: 0, y: y, width: kart.width, height: olc("A", ipucuYazi).height), withAttributes: [
            .font: ipucuYazi, .foregroundColor: NSColor.white.withAlphaComponent(0.55), .paragraphStyle: orta])
    }
}

// MARK: - Başlangıç

let uygulama = NSApplication.shared
let temsilci = Temsilci()
uygulama.delegate = temsilci
uygulama.setActivationPolicy(.regular)
uygulama.run()
