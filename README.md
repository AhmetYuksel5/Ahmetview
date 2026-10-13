# Ahmetview

Windows için basit fotoğraf görüntüleyici. JPG, PNG ve WebP açar.

## İndir

[Son sürüm](../../releases/latest) → `Ahmetview.exe`

1. Kalıcı bir klasöre koy, örneğin `C:\Uygulamalar\`. Varsayılan uygulama kaydı
   exe'nin durduğu yeri gösteriyor; İndirilenler'de bırakıp silersen kayıt boşa düşer.
2. Windows "bilgisayarınız korundu" derse: **Ek bilgi → Yine de çalıştır**
   (exe imzasız).
3. İlk açılışta "varsayılan yapılsın mı?" sorusuna Evet de; Ayarlar'da .jpg, .jpeg,
   .png ve .webp için Ahmetview'u seç. Windows bu son tıklamayı uygulamanın
   kendisinin yapmasına izin vermiyor. Sonradan da sağ tık menüsünden yapılabilir.

## Kullanım

Pencerenin çerçevesi yok: fotoğrafın dışında kalan alan yarı saydam, arkası görünüyor.

| Ne | Nasıl |
|---|---|
| Sonraki / önceki fotoğraf | Tekerleği çevir (aynı klasördeki fotoğraflar, Gezgin sırasıyla) |
| | Tekerleği yana yatır, farenin yan tuşları, ← → |
| Yakınlaştır / uzaklaştır | Ctrl + tekerlek ya da sağ tuş basılıyken tekerlek |
| Gerçek boyut / pencereye sığdır | Fotoğrafa çift tık (sırayla), Ctrl+1 / Ctrl+0 |
| Fotoğrafı kaydır | Fotoğrafı sol tuşla sürükle |
| Pencereyi taşı | Fotoğrafın dışındaki alandan sürükle |
| Pencereyi boyutlandır | Kenarlardan çek |
| Dosya aç | Ctrl+O, sürükle bırak ya da boş pencerede çift tık |
| Kapat | Sağ üstteki çarpı ya da Esc |
| Menü | Sağ tık |

WebP için Windows'un "WebP Görüntü Uzantıları" gerekiyor; Windows 11'de hazır geliyor.

## Kendin derlemek istersen

`derle.bat` dosyasına çift tıkla. Windows'ta hazır gelen derleyiciyi kullanıyor.
