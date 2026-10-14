// Video oynatma.
//
// Ana pencere katmanlı çiziliyor (her pikseli kendimiz veriyoruz) ve içine
// başka bir denetim konamıyor; videoyu kare kare kendimiz çizmek de takılır.
// Bu yüzden video, ana pencerenin sahip olduğu ayrı, çerçevesiz bir pencerede
// WPF'in MediaElement'iyle oynuyor; ana pencere onu fotoğrafın duracağı yere
// taşıyor. Çözücü Windows'un kendisi: MP4, MOV, WMV, AVI, MKV hazır geliyor,
// WebM için Microsoft Store'dan uzantı gerekebilir.
//
// WPF türleri tam adlarıyla yazıldı: System.Windows'u "using" ile açmak
// Point, Size ve Brushes adlarını System.Drawing'inkilerle çakıştırıyor.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Ahmetview
{
    class VideoPencere : Form
    {
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int CubukBoy = 36;

        // Segoe MDL2 Assets simgeleri (Windows 10 ve 11'de hazır).
        const string SimgeOynat = "";
        const string SimgeDuraklat = "";
        const string SimgeCevir = "";

        readonly System.Windows.Controls.MediaElement oynatici;
        readonly System.Windows.Controls.TextBlock oynatDugmesi;
        readonly System.Windows.Controls.TextBlock sure;
        readonly System.Windows.Controls.Grid iz;
        readonly System.Windows.Shapes.Rectangle dolu;
        readonly System.Windows.Threading.DispatcherTimer saat;

        bool oynuyor;
        int aci;
        int dogalEn, dogalBoy;

        // Sürükleme: bırakılınca hiç kıpırdamamışsa tık sayılıyor.
        bool basili, suruklendi;
        Point oncekiFare, basmaFare;
        bool izdeSurukle;

        // Bu pencerenin üstündeki fare hareketleri ana pencereye hiç
        // ulaşmıyor; ana pencerenin işi olanlar böyle iletiliyor.
        public event Action<int, int> Acildi;
        public event Action<string> Hata;
        public event Action<int, Point> Teker;
        public event Action SagBasildi;
        public event Action<Point> SagBirakildi;
        public event Action<int, int> Surukle;
        public event Action<Point> CiftTik;

        public VideoPencere()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;

            oynatici = new System.Windows.Controls.MediaElement();
            oynatici.LoadedBehavior = System.Windows.Controls.MediaState.Manual;
            oynatici.UnloadedBehavior = System.Windows.Controls.MediaState.Manual;
            oynatici.Stretch = System.Windows.Media.Stretch.Uniform;
            oynatici.MediaOpened += delegate
            {
                dogalEn = oynatici.NaturalVideoWidth;
                dogalBoy = oynatici.NaturalVideoHeight;
                BoyutBildir();
            };
            oynatici.MediaFailed += delegate (object k, System.Windows.ExceptionRoutedEventArgs e)
            {
                if (Hata != null) Hata(e.ErrorException.Message);
            };
            // Sonuna gelince baştan.
            oynatici.MediaEnded += delegate
            {
                oynatici.Position = TimeSpan.Zero;
                oynatici.Play();
            };

            // Arka planı olan bir kap: MediaElement yalnız görüntünün olduğu
            // yerde tıklama alıyor, kenardaki siyah boşluklar boşa düşerdi.
            System.Windows.Controls.Grid kap = new System.Windows.Controls.Grid();
            kap.Background = System.Windows.Media.Brushes.Black;
            kap.Children.Add(oynatici);
            kap.Children.Add(CubukYap(out oynatDugmesi, out sure, out iz, out dolu));

            kap.MouseWheel += delegate (object k, System.Windows.Input.MouseWheelEventArgs e)
            {
                if (Teker != null) Teker(e.Delta, System.Windows.Forms.Cursor.Position);
                e.Handled = true;
            };
            kap.MouseLeftButtonDown += delegate (object k, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    // İlk tık videoyu duraklatıp açtı; çift tıkta o geri alınıyor.
                    OynatDuraklat();
                    if (CiftTik != null) CiftTik(System.Windows.Forms.Cursor.Position);
                    return;
                }
                basili = true;
                suruklendi = false;
                basmaFare = oncekiFare = System.Windows.Forms.Cursor.Position;
                kap.CaptureMouse();
            };
            kap.MouseMove += delegate
            {
                if (!basili) return;
                Point simdi = System.Windows.Forms.Cursor.Position;
                if (!suruklendi && Math.Abs(simdi.X - basmaFare.X) < 5 && Math.Abs(simdi.Y - basmaFare.Y) < 5)
                    return;
                suruklendi = true;
                if (Surukle != null) Surukle(simdi.X - oncekiFare.X, simdi.Y - oncekiFare.Y);
                oncekiFare = simdi;
            };
            kap.MouseLeftButtonUp += delegate
            {
                if (!basili) return;
                basili = false;
                kap.ReleaseMouseCapture();
                if (!suruklendi) OynatDuraklat();
            };
            kap.MouseRightButtonDown += delegate { if (SagBasildi != null) SagBasildi(); };
            kap.MouseRightButtonUp += delegate (object k, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (SagBirakildi != null) SagBirakildi(System.Windows.Forms.Cursor.Position);
                e.Handled = true;
            };

            System.Windows.Forms.Integration.ElementHost konak = new System.Windows.Forms.Integration.ElementHost();
            konak.Dock = DockStyle.Fill;
            konak.Child = kap;
            Controls.Add(konak);

            saat = new System.Windows.Threading.DispatcherTimer();
            saat.Interval = TimeSpan.FromMilliseconds(250);
            saat.Tick += delegate { IlerlemeGuncelle(); };
        }

        // Alttaki yarı saydam çubuk: oynat/duraklat, ilerleme, süre, çevir.
        System.Windows.UIElement CubukYap(out System.Windows.Controls.TextBlock oynat,
            out System.Windows.Controls.TextBlock sureYazisi, out System.Windows.Controls.Grid izAlani,
            out System.Windows.Shapes.Rectangle doluKisim)
        {
            System.Windows.Controls.Grid cubuk = new System.Windows.Controls.Grid();
            cubuk.Height = CubukBoy;
            cubuk.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            cubuk.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(150, 0, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                System.Windows.Controls.ColumnDefinition sutun = new System.Windows.Controls.ColumnDefinition();
                sutun.Width = i == 1
                    ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star)
                    : System.Windows.GridLength.Auto;
                cubuk.ColumnDefinitions.Add(sutun);
            }
            // Çubuğa yapılan tıklama videoyu duraklatmasın, sürüklemesin.
            cubuk.MouseLeftButtonDown += delegate (object k, System.Windows.Input.MouseButtonEventArgs e) { e.Handled = true; };
            cubuk.MouseLeftButtonUp += delegate (object k, System.Windows.Input.MouseButtonEventArgs e) { e.Handled = true; };

            oynat = Simge(SimgeDuraklat);
            oynat.MouseLeftButtonUp += delegate { OynatDuraklat(); };
            System.Windows.Controls.Grid.SetColumn(oynat, 0);
            cubuk.Children.Add(oynat);

            // İz: ince çizgi ama tıklanabilir alanı çubuk boyunca.
            izAlani = new System.Windows.Controls.Grid();
            izAlani.Background = System.Windows.Media.Brushes.Transparent;
            izAlani.Margin = new System.Windows.Thickness(6, 0, 10, 0);
            System.Windows.Shapes.Rectangle arka = new System.Windows.Shapes.Rectangle();
            arka.Height = 4;
            arka.RadiusX = arka.RadiusY = 2;
            arka.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 255, 255, 255));
            doluKisim = new System.Windows.Shapes.Rectangle();
            doluKisim.Height = 4;
            doluKisim.RadiusX = doluKisim.RadiusY = 2;
            doluKisim.Width = 0;
            doluKisim.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            doluKisim.Fill = System.Windows.Media.Brushes.White;
            izAlani.Children.Add(arka);
            izAlani.Children.Add(doluKisim);
            System.Windows.Controls.Grid izYerel = izAlani;
            izAlani.MouseLeftButtonDown += delegate (object k, System.Windows.Input.MouseButtonEventArgs e)
            {
                izdeSurukle = true;
                izYerel.CaptureMouse();
                Atla(e.GetPosition(izYerel).X);
                e.Handled = true;
            };
            izAlani.MouseMove += delegate (object k, System.Windows.Input.MouseEventArgs e)
            {
                if (izdeSurukle) Atla(e.GetPosition(izYerel).X);
            };
            izAlani.MouseLeftButtonUp += delegate (object k, System.Windows.Input.MouseButtonEventArgs e)
            {
                izdeSurukle = false;
                izYerel.ReleaseMouseCapture();
                e.Handled = true;
            };
            System.Windows.Controls.Grid.SetColumn(izAlani, 1);
            cubuk.Children.Add(izAlani);

            sureYazisi = new System.Windows.Controls.TextBlock();
            sureYazisi.Foreground = System.Windows.Media.Brushes.White;
            sureYazisi.FontSize = 12;
            sureYazisi.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            sureYazisi.Margin = new System.Windows.Thickness(0, 0, 6, 0);
            System.Windows.Controls.Grid.SetColumn(sureYazisi, 2);
            cubuk.Children.Add(sureYazisi);

            System.Windows.Controls.TextBlock cevir = Simge(SimgeCevir);
            cevir.ToolTip = "Videoyu çevir";
            cevir.MouseLeftButtonUp += delegate { Cevir(); };
            System.Windows.Controls.Grid.SetColumn(cevir, 3);
            cubuk.Children.Add(cevir);
            return cubuk;
        }

        static System.Windows.Controls.TextBlock Simge(string harf)
        {
            System.Windows.Controls.TextBlock t = new System.Windows.Controls.TextBlock();
            t.Text = harf;
            t.FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");
            t.FontSize = 16;
            t.Foreground = System.Windows.Media.Brushes.White;
            t.Width = CubukBoy;
            t.TextAlignment = System.Windows.TextAlignment.Center;
            t.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            t.Cursor = System.Windows.Input.Cursors.Hand;
            return t;
        }

        // Odak ana pencerede kalsın: Esc, oklar ve Ctrl oradan okunuyor.
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void Yerlestir(Rectangle ekranda)
        {
            if (Bounds != ekranda) Bounds = ekranda;
            if (!Visible) Show();
        }

        public void Oynat(string yol)
        {
            dogalEn = dogalBoy = 0;
            // Telefonlar dikey videoyu yatay kaydedip dönüşü dosyaya yazıyor;
            // MediaElement bunu okumuyor, biz okuyup çeviriyoruz.
            aci = Mp4.Donus(yol);
            DonusUygula();
            oynatici.Source = new Uri(yol);
            oynatici.Play();
            oynuyor = true;
            oynatDugmesi.Text = SimgeDuraklat;
            saat.Start();
        }

        public void Durdur()
        {
            saat.Stop();
            oynatici.Stop();
            oynatici.Source = null;
            oynuyor = false;
            if (Visible) Hide();
        }

        void OynatDuraklat()
        {
            if (oynuyor) oynatici.Pause();
            else oynatici.Play();
            oynuyor = !oynuyor;
            oynatDugmesi.Text = oynuyor ? SimgeDuraklat : SimgeOynat;
        }

        // Otomatik algılama yanılırsa ya da dosyada bilgi yoksa elle çevirmek.
        void Cevir()
        {
            aci = (aci + 90) % 360;
            DonusUygula();
            BoyutBildir();
        }

        void DonusUygula()
        {
            oynatici.LayoutTransform = new System.Windows.Media.RotateTransform(aci);
        }

        // Ana pencere videonun ekrandaki en-boyunu bilsin: yan dönükse
        // en ile boy yer değiştiriyor.
        void BoyutBildir()
        {
            if (Acildi == null || dogalEn <= 0 || dogalBoy <= 0) return;
            if (aci == 90 || aci == 270) Acildi(dogalBoy, dogalEn);
            else Acildi(dogalEn, dogalBoy);
        }

        void Atla(double x)
        {
            if (!oynatici.NaturalDuration.HasTimeSpan || iz.ActualWidth <= 0) return;
            double oran = Math.Max(0, Math.Min(1, x / iz.ActualWidth));
            oynatici.Position = TimeSpan.FromTicks((long)(oynatici.NaturalDuration.TimeSpan.Ticks * oran));
            IlerlemeGuncelle();
        }

        void IlerlemeGuncelle()
        {
            if (!oynatici.NaturalDuration.HasTimeSpan) return;
            TimeSpan toplam = oynatici.NaturalDuration.TimeSpan, simdi = oynatici.Position;
            double oran = toplam.Ticks > 0 ? (double)simdi.Ticks / toplam.Ticks : 0;
            dolu.Width = Math.Max(0, Math.Min(1, oran)) * iz.ActualWidth;
            sure.Text = SureYaz(simdi) + " / " + SureYaz(toplam);
        }

        static string SureYaz(TimeSpan t)
        {
            if (t.TotalHours >= 1) return (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            return t.Minutes + ":" + t.Seconds.ToString("00");
        }
    }

    // MP4/MOV dosyasındaki görüntü izinin dönüşünü okumak. Dosyanın tamamı
    // okunmuyor; kutuların yalnız başlıklarına bakıp moov → trak → tkhd'ye
    // atlanıyor, oradaki dönüşüm matrisinden açı çıkıyor.
    static class Mp4
    {
        public static int Donus(string yol)
        {
            string uzanti = Path.GetExtension(yol).ToLowerInvariant();
            if (uzanti != ".mp4" && uzanti != ".m4v" && uzanti != ".mov") return 0;
            try
            {
                using (FileStream fs = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    return KutulardaAra(br, 0, fs.Length, 0);
                }
            }
            catch
            {
                return 0;
            }
        }

        // derinlik 0: dosya, 1: moov, 2: trak
        static int KutulardaAra(BinaryReader br, long bas, long son, int derinlik)
        {
            long konum = bas;
            while (konum + 8 <= son)
            {
                br.BaseStream.Position = konum;
                long boyut = BuyukSonlu32(br);
                string tur = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
                long govde = konum + 8;
                if (boyut == 1) { boyut = BuyukSonlu64(br); govde = konum + 16; }
                else if (boyut == 0) boyut = son - konum;
                if (boyut < 8) return 0;

                if (derinlik == 0 && tur == "moov") return KutulardaAra(br, govde, konum + boyut, 1);
                if (derinlik == 1 && tur == "trak")
                {
                    int aci = KutulardaAra(br, govde, konum + boyut, 2);
                    if (aci >= 0) return aci;   // görüntü izi bulundu
                }
                if (derinlik == 2 && tur == "tkhd") return Tkhd(br, govde);
                konum += boyut;
            }
            return derinlik == 2 ? -1 : 0;
        }

        // Ses izinin eni-boyu sıfır; öyleyse -1 dönüp sıradaki ize bakılıyor.
        static int Tkhd(BinaryReader br, long govde)
        {
            br.BaseStream.Position = govde;
            int surum = br.ReadByte();
            br.ReadBytes(3);
            br.ReadBytes(surum == 1 ? 32 : 20);
            br.ReadBytes(16);
            int[] m = new int[9];
            for (int i = 0; i < 9; i++) m[i] = (int)BuyukSonlu32(br);
            long en = BuyukSonlu32(br), boy = BuyukSonlu32(br);
            if (en == 0 || boy == 0) return -1;
            const int Bir = 0x10000;
            if (m[0] == 0 && m[1] == Bir && m[3] == -Bir && m[4] == 0) return 90;
            if (m[0] == -Bir && m[4] == -Bir) return 180;
            if (m[0] == 0 && m[1] == -Bir && m[3] == Bir && m[4] == 0) return 270;
            return 0;
        }

        static long BuyukSonlu32(BinaryReader br)
        {
            byte[] b = br.ReadBytes(4);
            return (int)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]) & 0xFFFFFFFFL;
        }

        static long BuyukSonlu64(BinaryReader br)
        {
            return (BuyukSonlu32(br) << 32) | BuyukSonlu32(br);
        }
    }
}
