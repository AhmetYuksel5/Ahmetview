// WebP okuma.
//
// Program'ın geri kalanının kullandığı GDI+ WebP tanımıyor. Windows'un
// kendi görüntü çözücüsü (WIC) ise "WebP Görüntü Uzantıları" ile tanıyor
// ve bu uzantı Windows 11'de hazır geliyor. WIC'e en kısa yol WPF'in
// BitmapDecoder'ı; sonucu GDI+'nın çizebileceği bir Bitmap'e kopyalıyoruz.
//
// WPF türleri tam adlarıyla yazıldı: System.Windows'u "using" ile açmak
// Point ve Size adlarını System.Drawing'inkilerle çakıştırıyor.

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Ahmetview
{
    static class Webp
    {
        public static Bitmap Oku(byte[] baytlar)
        {
            using (MemoryStream ms = new MemoryStream(baytlar))
            {
                System.Windows.Media.Imaging.BitmapDecoder cozucu =
                    System.Windows.Media.Imaging.BitmapDecoder.Create(ms,
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                // Hangi piksel düzeninde gelirse gelsin GDI+'nın 32 bit
                // ön-çarpılmış biçimine çevriliyor; ikisi bayt bayt aynı.
                System.Windows.Media.Imaging.BitmapSource kaynak =
                    new System.Windows.Media.Imaging.FormatConvertedBitmap(cozucu.Frames[0],
                        System.Windows.Media.PixelFormats.Pbgra32, null, 0);

                int en = kaynak.PixelWidth, boy = kaynak.PixelHeight;
                Bitmap bmp = new Bitmap(en, boy, PixelFormat.Format32bppPArgb);
                BitmapData veri = bmp.LockBits(new Rectangle(0, 0, en, boy),
                    ImageLockMode.WriteOnly, bmp.PixelFormat);
                try
                {
                    kaynak.CopyPixels(System.Windows.Int32Rect.Empty, veri.Scan0,
                        veri.Stride * boy, veri.Stride);
                }
                finally
                {
                    bmp.UnlockBits(veri);
                }
                return bmp;
            }
        }
    }
}
