# Ahmetview

Windows ve Mac için.

Windows için basit fotoğraf ve video görüntüleyici. JPG, PNG, WebP ile MP4, MOV, M4V, WMV, AVI, MKV ve WebM açar.

## İndir (Windows)

[Son sürüm](../../releases/latest) → `Ahmetview.exe`

1. Kalıcı bir klasöre koy, örneğin `C:\Uygulamalar\`. Varsayılan uygulama kaydı
   exe'nin durduğu yeri gösteriyor; İndirilenler'de bırakıp silersen kayıt boşa düşer.
2. Windows "bilgisayarınız korundu" derse: **Ek bilgi → Yine de çalıştır**
   (exe imzasız).
3. İlk açılışta "varsayılan yapılsın mı?" sorusuna Evet de; Ayarlar'da .jpg, .jpeg,
   .png, .webp ve istediğin video türleri için Ahmetview'u seç. Windows bu son tıklamayı uygulamanın
   kendisinin yapmasına izin vermiyor. Sonradan da sağ tık menüsünden yapılabilir.

## Kullanım

Ekranın tamamını kaplıyor: fotoğraf ortada, çevresi yarı saydam ve dumanlı. Altta
klasördeki diğer fotoğrafların ve videoların önizleme şeridi var.

| Ne | Nasıl |
|---|---|
| Sonraki / önceki | Tekerleği çevir (aynı klasör, Gezgin sırasıyla), ← →, farenin yan tuşları |
| Başka bir dosyaya geç | Alttaki şeritte önizlemesine tıkla |
| Yakınlaştır / uzaklaştır | Ctrl + tekerlek ya da sağ tuş basılıyken tekerlek |
| Gerçek boyut / ilk görünüm | Çift tık (sırayla), Ctrl+1 / Ctrl+0 |
| Kaydır | Sol tuşla sürükle (videoda da) |
| Videoyu duraklat / devam | Videoya tıkla ya da alttaki düğme |
| Videoda ileri / geri sar | Alttaki çubuğa tıkla ya da sürükle |
| Videoyu çevir | Alttaki çevirme düğmesi (telefon videolarında kendiliğinden) |
| Dosya aç | Ctrl+O, sürükle bırak ya da boş pencerede çift tık |
| Kapat | Fotoğrafın sağ üstündeki çarpı, dumanlı alana tıklamak ya da Esc |
| Menü | Sağ tık |

WebP için Windows'un "WebP Görüntü Uzantıları" gerekiyor; Windows 11'de hazır geliyor.
Videoyu Windows'un kendi oynatıcı bileşeni açıyor; WebM için Microsoft Store'dan
"Web Media Extensions" gerekebilir.

## Kendin derlemek istersen

`derle.bat` dosyasına çift tıkla. Windows'ta hazır gelen derleyiciyi kullanıyor.

## Mac

Buzlu cam arka plan, aynı önizleme şeridi; izleme dörtgeninde iki parmakla geçiş,
kıstırarak zum. JPG, PNG, WebP, HEIC ile MP4 ve MOV açar. macOS 12 ve sonrası,
Apple Silicon ve Intel.

**Uyarısız kurulum:** Terminal'i açıp şunu yapıştır:

```
curl -L https://github.com/AhmetYuksel5/Ahmetview/releases/download/mac/Ahmetview-mac.zip -o /tmp/Ahmetview.zip && ditto -x -k /tmp/Ahmetview.zip /Applications && open /Applications/Ahmetview.app
```

Uygulama Apple'a para verilerek imzalatılmadığı için tarayıcıyla indirilince macOS
"bilinmeyen geliştirici" diyor. Uyarı yalnız tarayıcının işaretlediği dosyalara
çıkıyor; Terminal'den indirilen dosyada bu işaret olmadığı için hiç sormuyor.

**Tarayıcıyla:** [Mac sürümü](../../releases/tag/mac) sayfasından `Ahmetview-mac.zip`'i
indir, aç, `Ahmetview.app`'i Uygulamalar'a sürükle. İlk açılışta engellerse:
Sistem Ayarları → Gizlilik ve Güvenlik → aşağıda "Yine de Aç". Bir kez yeterli.

Mac'te kullanım Windows'takiyle aynı; Ctrl yerine ⌘, sağ tık menüsü ve Esc aynı.
