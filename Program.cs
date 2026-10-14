// Ahmetview: basit fotoğraf görüntüleyici (JPG, PNG, WebP).
//
// Windows'ta hazır gelen .NET Framework derleyicisiyle derlenebilsin diye
// C# 5 sözdizimi kullanılıyor: dize içine değer gömme, "?." ve "=>" ile
// yazılmış üyeler o derleyicide yok.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Ahmetview")]
[assembly: AssemblyProduct("Ahmetview")]

namespace Ahmetview
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // Yüksek çözünürlüklü ekranda Windows pencereyi büyütüp
            // bulanıklaştırmasın; fotoğraf gerçek piksellerle çizilsin.
            try { SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Pencere(args.Length > 0 ? args[0] : null));
        }
    }

    class Pencere : Form
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        const int ExifYon = 0x0112;
        const int WS_EX_LAYERED = 0x00080000;

        // Fotoğrafın sağ üst köşesindeki kapatma düğmesi ve köşeye uzaklığı.
        const int KapatBoyut = 36;
        const int KapatPay = 14;

        // Alttaki önizleme şeridi: 16:9 küçükler, şerit ekranın en fazla
        // bu oranı kadar geniş.
        const int KucukEn = 128;
        const int KucukBoy = 72;
        const int KucukAralik = 10;
        const int SeritAlt = 20;
        const float SeritOrani = 0.6f;
        const int SeritYeri = KucukBoy + SeritAlt + 24;

        // Zum göstergesi bir değişiklikten sonra bu kadar görünüp kayboluyor.
        const int OranSuresi = 1200;

        // Fotoğrafın dışında kalan alanın koyuluğu (0 tam saydam, 255 siyah).
        const int ArkaKoyuluk = 110;

        // Büyük fotoğraf açılırken ekranın bu oranına sığdırılıyor: tamamına
        // sığdırınca ekranı kaplıyor gibi duruyordu, dumanlı alan kalmıyordu.
        const float AcilisOrani = 0.8f;
        const int GolgeGenislik = 24;

        // Tekerlek yana bastırılı tutulunca sürekli ileti gönderiyor;
        // bu aralık olmasa tek basışta birkaç fotoğraf birden geçiliyordu.
        const int YanaGecisAraligi = 300;

        static readonly string[] Uzantilar = {
            ".jpg", ".jpeg", ".png", ".webp",
            ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };
        static readonly string[] VideoUzantilari = {
            ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };

        // Gezgin'in sıralaması: "foto2" "foto10"dan önce gelsin.
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        readonly string baslangic;
        List<string> dosyalar = new List<string>();
        int sira = -1;
        Image resim;
        // Gösterilen şeyin boyutu: fotoğrafta resmin, videoda videonun.
        int icerikEn, icerikBoy;
        bool video;
        VideoPencere oynatici;

        // Gölgeli alana basıp kıpırdatmadan bırakmak pencereyi kapatıyor;
        // sürüklenirse kapatmıyor, fotoğrafı kaydırıyor.
        bool disariBasildi;
        Point basmaNoktasi;
        string hata;

        float olcek = 1f;
        PointF konum;
        bool sigdir = true;

        bool surukleniyor;
        Point suruklemeFare;
        PointF suruklemeKonum;

        int sonYanaGecis;
        int tekerBirikim;
        bool sagTekerKullanildi;
        bool kapatUstunde;

        Katman.Yuzey yuzey;

        // Fotoğrafın ekrandaki boyutuna küçültülmüş hâli, gölgesiyle birlikte.
        // Her çizimde tam çözünürlüklü fotoğrafı yeniden küçültmek sürüklerken
        // takılmaya, önizlemeler yüklenirken de geçişlerin gecikmesine yol
        // açıyordu; ölçek değişmedikçe bu hazır resim kaydırılıyor.
        Bitmap gorunum;
        Image gorunumKaynak;
        float gorunumOlcek;
        bool gorunumKaliteli;
        bool gorunumVideo;
        // Zum sırasında hızlı küçültülüyor, zum bitince kaliteliye geçiliyor.
        readonly System.Windows.Forms.Timer kaliteZamani = new System.Windows.Forms.Timer();

        // Şeridin kayma animasyonu: fotoğraf değişince şerit eski yerinden
        // yenisine yumuşakça kayıyor.
        float seritKayma, kaymaBaslangic;
        int animBaslangic;
        readonly System.Windows.Forms.Timer animZamani = new System.Windows.Forms.Timer();
        const int AnimSure = 320;
        readonly HashSet<string> gorunurKucukler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Önizlemeler arka plandaki bir iş parçacığında hazırlanıyor: büyük
        // bir JPEG'i küçültmek onlarca milisaniye sürüyor, kalabalık bir
        // klasörde ekranı dondururdu. Sözlük ve kuyruk "kilit" ile korunuyor.
        readonly object kilit = new object();
        readonly Dictionary<string, Bitmap> kucukler = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> istenen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Stack<string> istekler = new Stack<string>();
        readonly AutoResetEvent uyari = new AutoResetEvent(false);
        int cizimBekliyor;

        readonly List<KeyValuePair<int, Rectangle>> seritKutulari = new List<KeyValuePair<int, Rectangle>>();
        Rectangle seritAlani;

        bool oranGorunur;
        readonly System.Windows.Forms.Timer oranZamani = new System.Windows.Forms.Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        public Pencere(string yol)
        {
            baslangic = yol;
            Text = "Ahmetview";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.None;
            AllowDrop = true;
            // Fare hangi ekrandaysa onun tamamını kaplıyor: fotoğraf ortada,
            // çevresi yarı saydam ve dumanlı.
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.FromPoint(Cursor.Position).Bounds;

            // ContextMenuStrip özelliğine bağlanmıyor: o zaman sağ tuş
            // bırakılınca her seferinde açılıyor, sağ tuş + tekerlek ile
            // yakınlaştırdıktan sonra da.
            menu.Items.Add("Aç…  (Ctrl+O)", null, delegate { DosyaSec(); });
            menu.Items.Add("Varsayılan uygulama yap", null, delegate { Kayit.AyarlariAc(); });
            menu.Items.Add("Kapat  (Esc)", null, delegate { Close(); });

            kaliteZamani.Interval = 150;
            kaliteZamani.Tick += delegate
            {
                kaliteZamani.Stop();
                Ciz();
            };
            animZamani.Interval = 15;
            animZamani.Tick += delegate
            {
                float t = (Environment.TickCount - animBaslangic) / (float)AnimSure;
                if (t >= 1f)
                {
                    seritKayma = 0;
                    animZamani.Stop();
                }
                else
                {
                    // Yavaşlayarak duran kayma (kübik).
                    float kalan = 1f - t;
                    seritKayma = kaymaBaslangic * kalan * kalan * kalan;
                }
                Ciz();
            };

            oranZamani.Interval = OranSuresi;
            oranZamani.Tick += delegate
            {
                oranZamani.Stop();
                oranGorunur = false;
                Ciz();
            };
        }

        // Katmanlı pencere: her pikselin kendi saydamlığı oluyor. WinForms'un
        // Opacity'si fotoğrafı da soldururdu, TransparencyKey ise ya tam
        // saydam ya hiç; yarı saydam arka plan ve gölge ancak böyle oluyor.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // WebP çözücüsü (WPF) STA iş parçacığı istiyor.
            Thread isci = new Thread(IsciDongusu);
            isci.IsBackground = true;
            isci.Priority = ThreadPriority.BelowNormal;
            isci.SetApartmentState(ApartmentState.STA);
            isci.Start();

            oynatici = new VideoPencere();
            oynatici.Owner = this;
            oynatici.Acildi += VideoAcildi;
            oynatici.Hata += delegate (string ileti)
            {
                hata = Path.GetFileName(dosyalar[sira]) + " oynatılamadı: " + ileti;
                video = false;
                oynatici.Durdur();
                Ciz();
            };
            oynatici.Teker += delegate (int delta, Point ekranda) { TekerIsle(delta, PointToClient(ekranda)); };
            oynatici.SagBasildi += delegate { sagTekerKullanildi = false; };
            oynatici.Surukle += delegate (int dx, int dy)
            {
                konum = new PointF(konum.X + dx, konum.Y + dy);
                sigdir = false;
                Ciz();
            };
            oynatici.CiftTik += delegate (Point ekranda) { CiftTikla(PointToClient(ekranda)); };
            oynatici.SagBirakildi += delegate (Point ekranda)
            {
                if (!sagTekerKullanildi) menu.Show(this, PointToClient(ekranda));
                sagTekerKullanildi = false;
            };
            Kayit.Kaydet();
            if (baslangic != null) Ac(baslangic); else Ciz();
            Kayit.IlkSefer(this);
        }

        // ---- Dosyalar ----

        static bool Desteklenir(string yol)
        {
            string uzanti = Path.GetExtension(yol).ToLowerInvariant();
            return Array.IndexOf(Uzantilar, uzanti) >= 0;
        }

        void Ac(string yol)
        {
            KucukleriTemizle();
            try
            {
                yol = Path.GetFullPath(yol);
                string klasor = Path.GetDirectoryName(yol);
                dosyalar = new List<string>();
                foreach (string f in Directory.GetFiles(klasor))
                    if (Desteklenir(f)) dosyalar.Add(f);
                dosyalar.Sort(StrCmpLogicalW);
                sira = dosyalar.FindIndex(delegate (string f)
                {
                    return string.Equals(f, yol, StringComparison.OrdinalIgnoreCase);
                });
                // Bırakılan dosya desteklenmiyorsa klasörün ilkiyle başla.
                if (sira < 0) sira = dosyalar.Count > 0 ? 0 : -1;
            }
            catch (Exception ex)
            {
                dosyalar = new List<string>();
                sira = -1;
                hata = ex.Message;
            }
            Yukle();
        }

        void Gec(int yon)
        {
            if (dosyalar.Count == 0) return;
            SiraDegistir((sira + yon + dosyalar.Count) % dosyalar.Count);
        }

        void SiraDegistir(int yeni)
        {
            int fark = yeni - sira;
            sira = yeni;
            // Baştan sona sarmada şerit uzun yol kaymasın, olduğu yere atlasın.
            if (fark != 0 && Math.Abs(fark) <= 3)
            {
                seritKayma += fark * (KucukEn + KucukAralik);
                kaymaBaslangic = seritKayma;
                animBaslangic = Environment.TickCount;
                animZamani.Start();
            }
            else
            {
                seritKayma = 0;
                animZamani.Stop();
            }
            Yukle();
        }

        bool IcerikVar
        {
            get { return resim != null || video; }
        }

        static bool VideoMu(string yol)
        {
            return Array.IndexOf(VideoUzantilari, Path.GetExtension(yol).ToLowerInvariant()) >= 0;
        }

        void Yukle()
        {
            if (resim != null) { resim.Dispose(); resim = null; }
            video = false;
            hata = null;
            if (sira >= 0 && VideoMu(dosyalar[sira]))
            {
                video = true;
                // Gerçek boyut video açılınca geliyor; o zamana kadar yaygın oran.
                icerikEn = 1280;
                icerikBoy = 720;
            }
            else
            {
                if (oynatici != null) oynatici.Durdur();
                if (sira >= 0)
                {
                    try
                    {
                        // Dosya baytları belleğe alınıyor: Image.FromFile dosyayı
                        // pencere kapanana kadar kilitliyor, silinemez/taşınamaz oluyor.
                        byte[] baytlar = File.ReadAllBytes(dosyalar[sira]);
                        if (Path.GetExtension(dosyalar[sira]).ToLowerInvariant() == ".webp")
                            resim = Webp.Oku(baytlar);
                        else
                        {
                            using (MemoryStream ms = new MemoryStream(baytlar))
                            using (Image ham = Image.FromStream(ms))
                            {
                                Dondur(ham);
                                resim = new Bitmap(ham);
                            }
                        }
                        icerikEn = resim.Width;
                        icerikBoy = resim.Height;
                    }
                    catch (Exception ex)
                    {
                        hata = Path.GetFileName(dosyalar[sira]) + " açılamadı: " + ex.Message;
                    }
                }
            }
            Sigdir();
            Baslik();
            // Pencere Sigdir'in çizimiyle yerine konduktan sonra; MediaElement
            // görünmeyen pencerede oynatmaya başlamıyor.
            if (video && oynatici != null) oynatici.Oynat(dosyalar[sira]);
        }

        void VideoAcildi(int en, int boy)
        {
            if (!video) return;
            if (en > 0 && boy > 0)
            {
                icerikEn = en;
                icerikBoy = boy;
            }
            Sigdir();
            Baslik();
        }

        // Telefon fotoğrafları yan çekilmiş kaydedilip dönüşü EXIF'e yazılıyor;
        // uygulanmazsa dikey fotoğraf yan yatık görünüyor.
        static void Dondur(Image img)
        {
            YonUygula(img, YonOku(img));
        }

        static int YonOku(Image img)
        {
            if (Array.IndexOf(img.PropertyIdList, ExifYon) < 0) return 1;
            return img.GetPropertyItem(ExifYon).Value[0];
        }

        static void YonUygula(Image img, int yon)
        {
            switch (yon)
            {
                case 2: img.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                case 3: img.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: img.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                case 5: img.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: img.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: img.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: img.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        void Baslik()
        {
            if (!IcerikVar || sira < 0) { Text = "Ahmetview"; return; }
            Text = Path.GetFileName(dosyalar[sira])
                + "  —  " + (sira + 1) + " / " + dosyalar.Count
                + "  —  %" + Math.Round(olcek * 100);
        }

        void DosyaSec()
        {
            // Tam ad: video için eklenen WPF de aynı adda bir pencere getiriyor.
            using (System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog())
            {
                d.Filter = "Fotoğraf ve videolar|*.jpg;*.jpeg;*.png;*.webp;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm";
                if (d.ShowDialog(this) == DialogResult.OK) Ac(d.FileName);
            }
        }

        // ---- Yakınlaştırma ----

        void Sigdir()
        {
            sigdir = true;
            if (IcerikVar)
            {
                // Şerit varken fotoğraf ona binmesin: alttan ve (ortalı
                // dursun diye) üstten şeridin yüksekliği kadar pay.
                float pay = dosyalar.Count > 1 ? 2 * SeritYeri : 0;
                float alanBoy = Math.Min(ClientSize.Height * AcilisOrani, ClientSize.Height - pay);
                float s = Math.Min(ClientSize.Width * AcilisOrani / icerikEn,
                                   alanBoy / icerikBoy);
                // Küçük fotoğrafı büyütüp bulanıklaştırmıyoruz.
                olcek = Math.Min(s, 1f);
                Ortala();
            }
            Ciz();
        }

        void GercekBoyut()
        {
            if (!IcerikVar) return;
            sigdir = false;
            olcek = 1f;
            Ortala();
            Baslik();
            OranGoster();
        }

        void Ortala()
        {
            konum = new PointF((ClientSize.Width - icerikEn * olcek) / 2f,
                               (ClientSize.Height - icerikBoy * olcek) / 2f);
        }

        // İmlecin altındaki nokta yerinde kalsın diye konum da kaydırılıyor.
        void Yakinlas(double carpan, PointF merkez)
        {
            if (!IcerikVar) return;
            float yeni = (float)Math.Max(0.01, Math.Min(50.0, olcek * carpan));
            float k = yeni / olcek;
            konum = new PointF(merkez.X - (merkez.X - konum.X) * k,
                               merkez.Y - (merkez.Y - konum.Y) * k);
            olcek = yeni;
            sigdir = false;
            kaliteZamani.Stop();
            kaliteZamani.Start();
            Baslik();
            OranGoster();
        }

        void OranGoster()
        {
            oranGorunur = true;
            oranZamani.Stop();
            oranZamani.Start();
            Ciz();
        }

        RectangleF FotoAlani()
        {
            if (!IcerikVar) return RectangleF.Empty;
            return new RectangleF(konum.X, konum.Y, icerikEn * olcek, icerikBoy * olcek);
        }

        PointF PencereOrtasi()
        {
            return new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (sigdir) Sigdir(); else Ciz();
        }

        // ---- Çizim ----

        // Katmanlı pencerede WM_PAINT yok: her değişiklikte bütün pencere
        // bir yüzeye çizilip Windows'a tek seferde veriliyor.
        void Ciz()
        {
            if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (yuzey == null || yuzey.En != ClientSize.Width || yuzey.Boy != ClientSize.Height)
            {
                if (yuzey != null) yuzey.Dispose();
                yuzey = new Katman.Yuzey(ClientSize.Width, ClientSize.Height);
            }
            using (Graphics g = Graphics.FromImage(yuzey.Resim))
            {
                // Arka plan tamamen saydam olsa tıklama pencereye değil
                // arkadaki masaüstüne giderdi; hafif koyuluk bunu da önlüyor.
                g.Clear(Color.FromArgb(ArkaKoyuluk, 0, 0, 0));
                // Boşken tanıtım kartı; bir dosya açılamadıysa nedeni yazıyor.
                if (!IcerikVar && hata == null) TanitimCiz(g);
                else if (!IcerikVar)
                {
                    string yazi = hata;
                    // ClearType saydam zeminde renkli kenar bırakıyor.
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Font yaziTipi = new Font("Segoe UI", 14f))
                    using (StringFormat bicim = new StringFormat())
                    {
                        bicim.Alignment = StringAlignment.Center;
                        bicim.LineAlignment = StringAlignment.Center;
                        g.DrawString(yazi, yaziTipi, Brushes.WhiteSmoke, ClientRectangle, bicim);
                    }
                }
                else if (IcerikVar)
                {
                    IcerikCiz(g);
                    SeritCiz(g);
                    OranCiz(g);
                }
                KapatCiz(g);
            }
            yuzey.Guncelle(this);
            if (video && oynatici != null)
                oynatici.Yerlestir(RectangleToScreen(Rectangle.Round(FotoAlani())));
        }

        // Dosyasız açılınca ortada duran kart: ne olduğu ve neden farklı olduğu.
        static readonly string[,] Ozellikler = {
            { "\uE945", "Hafif ve hızlı", "Tek dosya, kurulum yok, 100 KB'tan küçük. Anında açılır." },
            { "\uE771", "Modern görünüm", "Çerçevesiz pencere, dumanlı arka plan, yumuşak geçişler." },
            { "\uE8A3", "Her şey farede", "Tekerlek: sonraki, önceki. Ctrl ya da sağ tuşla tekerlek: zum." },
            { "\uE714", "Fotoğraf ve video", "JPG, PNG, WebP, MP4, MOV, MKV ve daha fazlası." },
        };

        void TanitimCiz(Graphics g)
        {
            const float En = 560, Ic = 36, IkonBoy = 96, SatirBoy = 54;
            int satir = Ozellikler.GetLength(0);
            float boy = Ic + IkonBoy + 18 + 46 + 24 + 22 + satir * SatirBoy + 14 + 20 + Ic;
            RectangleF kart = new RectangleF((ClientSize.Width - En) / 2f, (ClientSize.Height - boy) / 2f, En, boy);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (GraphicsPath yol = Yuvarlak(kart, 20))
            using (SolidBrush zemin = new SolidBrush(Color.FromArgb(150, 18, 18, 24)))
            using (Pen kenar = new Pen(Color.FromArgb(50, 255, 255, 255)))
            {
                g.FillPath(zemin, yol);
                g.DrawPath(kenar, yol);
            }

            float y = kart.Top + Ic;
            IkonCiz(g, new RectangleF(kart.Left + (En - IkonBoy) / 2f, y, IkonBoy, IkonBoy));
            y += IkonBoy + 18;

            using (StringFormat orta = new StringFormat())
            using (Font baslik = new Font("Segoe UI Semibold", 26f))
            using (Font alt = new Font("Segoe UI", 11f))
            using (Font kalin = new Font("Segoe UI Semibold", 11f))
            using (Font aciklama = new Font("Segoe UI", 10f))
            using (Font simge = new Font("Segoe MDL2 Assets", 16f))
            using (Font ipucu = new Font("Segoe UI", 9.5f))
            using (SolidBrush beyaz = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (SolidBrush gri = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            using (SolidBrush soluk = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
            using (SolidBrush vurgu = new SolidBrush(Color.FromArgb(255, 120, 170, 255)))
            {
                orta.Alignment = StringAlignment.Center;
                g.DrawString("Ahmetview", baslik, beyaz, new RectangleF(kart.Left, y, En, 46), orta);
                y += 46;
                g.DrawString("Windows için sade ve hızlı fotoğraf, video görüntüleyici", alt, gri,
                    new RectangleF(kart.Left, y, En, 24), orta);
                y += 24 + 22;

                float sol = kart.Left + 56;
                for (int i = 0; i < satir; i++)
                {
                    g.DrawString(Ozellikler[i, 0], simge, vurgu, sol, y + 2);
                    g.DrawString(Ozellikler[i, 1], kalin, beyaz, sol + 40, y);
                    g.DrawString(Ozellikler[i, 2], aciklama, gri,
                        new RectangleF(sol + 40, y + 22, En - 56 * 2 - 40, SatirBoy - 22));
                    y += SatirBoy;
                }
                y += 14;
                g.DrawString("Açmak için bir dosyayı buraya bırak ya da çift tıkla", ipucu, soluk,
                    new RectangleF(kart.Left, y, En, 20), orta);
            }
        }

        // Uygulamanın simgesi, kodla çizilmiş: ayrı bir resim dosyası taşımamak
        // için. ahmetview.ico ile aynı tasarım.
        static void IkonCiz(Graphics g, RectangleF r)
        {
            Func<float, float, PointF> P = delegate (float x, float y)
            {
                return new PointF(r.Left + x * r.Width, r.Top + y * r.Height);
            };
            using (GraphicsPath kose = Yuvarlak(r, r.Width * 0.22f))
            {
                using (LinearGradientBrush zemin = new LinearGradientBrush(r,
                    Color.FromArgb(56, 140, 255), Color.FromArgb(140, 70, 240), 45f))
                    g.FillPath(zemin, kose);

                Region eskiKirpma = g.Clip;
                g.SetClip(kose, CombineMode.Intersect);
                using (SolidBrush gunes = new SolidBrush(Color.FromArgb(255, 206, 84)))
                    g.FillEllipse(gunes, RectangleF.FromLTRB(P(0.62f, 0.20f).X, P(0.62f, 0.20f).Y,
                        P(0.80f, 0.38f).X, P(0.80f, 0.38f).Y));
                using (SolidBrush tepe = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                    g.FillPolygon(tepe, new[] { P(0.45f, 1f), P(0.72f, 0.52f), P(1f, 0.80f), P(1f, 1f) });
                // A biçimli dağ: dış üçgen, bacak arası çentik ve iç üçgen;
                // Alternate dolguda içteki üçgenler boşluk kalıyor.
                using (GraphicsPath a = new GraphicsPath(FillMode.Alternate))
                {
                    a.AddPolygon(new[] { P(0.10f, 1f), P(0.42f, 0.20f), P(0.74f, 1f) });
                    a.AddPolygon(new[] { P(0.33f, 1f), P(0.42f, 0.80f), P(0.51f, 1f) });
                    a.AddPolygon(new[] { P(0.42f, 0.50f), P(0.36f, 0.68f), P(0.48f, 0.68f) });
                    g.FillPath(Brushes.White, a);
                }
                g.Clip = eskiKirpma;
            }
        }

        void IcerikCiz(Graphics g)
        {
            RectangleF alan = FotoAlani();
            float en = icerikEn * olcek, boy = icerikBoy * olcek;
            // Ekranın birkaç katından büyük bir önbellek belleği bitirir; o
            // kadar yakınlaşınca yalnız ekranda görünen parça çiziliyor.
            bool onbellekOlur = en * boy <= 3f * ClientSize.Width * ClientSize.Height
                && en <= 3 * ClientSize.Width && boy <= 3 * ClientSize.Height;
            if (onbellekOlur)
            {
                GorunumHazirla();
                g.CompositingMode = CompositingMode.SourceOver;
                // Piksel ölçüsüyle: DrawImageUnscaled ekran ölçeklendirmesi
                // %100 değilse resmi DPI'ya göre büyütüyor.
                g.DrawImage(gorunum, (int)Math.Round(alan.X) - GolgeGenislik,
                    (int)Math.Round(alan.Y) - GolgeGenislik,
                    new Rectangle(0, 0, gorunum.Width, gorunum.Height), GraphicsUnit.Pixel);
                return;
            }
            RectangleF gorunen = RectangleF.Intersect(alan, ClientRectangle);
            if (gorunen.Width <= 0 || gorunen.Height <= 0) return;
            if (video)
            {
                g.FillRectangle(Brushes.Black, gorunen);
                return;
            }
            RectangleF kaynak = new RectangleF((gorunen.X - alan.X) / olcek, (gorunen.Y - alan.Y) / olcek,
                gorunen.Width / olcek, gorunen.Height / olcek);
            g.InterpolationMode = olcek >= 3f || surukleniyor || kaliteZamani.Enabled
                ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(resim, gorunen, kaynak, GraphicsUnit.Pixel);
        }

        // Ölçek ya da fotoğraf değiştiyse gölgeli önbelleği yeniden yap. Zum
        // sürerken hızlı yöntemle; zum durunca kaliteZamani kaliteliye çeviriyor.
        void GorunumHazirla()
        {
            bool hizli = kaliteZamani.Enabled;
            if (gorunum != null && gorunumKaynak == resim && gorunumVideo == video
                && gorunumOlcek == olcek && (gorunumKaliteli || hizli))
                return;
            int en = Math.Max(1, (int)Math.Round(icerikEn * olcek));
            int boy = Math.Max(1, (int)Math.Round(icerikBoy * olcek));
            if (gorunum != null) gorunum.Dispose();
            gorunum = new Bitmap(en + 2 * GolgeGenislik, boy + 2 * GolgeGenislik + 6, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(gorunum))
            {
                Golge(g, new RectangleF(GolgeGenislik, GolgeGenislik, en, boy));
                Rectangle hedef = new Rectangle(GolgeGenislik, GolgeGenislik, en, boy);
                if (video) g.FillRectangle(Brushes.Black, hedef);
                else
                {
                    g.InterpolationMode = olcek >= 3f ? InterpolationMode.NearestNeighbor
                        : hizli ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(resim, hedef);
                }
            }
            gorunumKaynak = resim;
            gorunumVideo = video;
            gorunumOlcek = olcek;
            gorunumKaliteli = !hizli;
        }

        static GraphicsPath Yuvarlak(RectangleF r, float yaricap)
        {
            float c = yaricap * 2;
            GraphicsPath yol = new GraphicsPath();
            yol.AddArc(r.Left, r.Top, c, c, 180, 90);
            yol.AddArc(r.Right - c, r.Top, c, c, 270, 90);
            yol.AddArc(r.Right - c, r.Bottom - c, c, c, 0, 90);
            yol.AddArc(r.Left, r.Bottom - c, c, c, 90, 90);
            yol.CloseFigure();
            return yol;
        }

        // Klasördeki fotoğrafların küçükleri. Açık olan hep tam ortada, tam
        // boyda ve çerçeveli; diğerleri %20 küçük ve soluk. Kayarken boy ve
        // soluklık ortaya uzaklığa göre yumuşakça değişiyor.
        void SeritCiz(Graphics g)
        {
            seritKutulari.Clear();
            seritAlani = Rectangle.Empty;
            int n = dosyalar.Count;
            if (n < 2) return;

            int adim = KucukEn + KucukAralik;
            int sigan = (int)((ClientSize.Width * SeritOrani + KucukAralik) / adim);
            if (sigan % 2 == 0) sigan--;          // ortası olsun diye tek
            sigan = Math.Max(1, Math.Min(sigan, 2 * n - 1));
            int yarim = sigan / 2;
            int en = sigan * adim - KucukAralik;
            int x0 = (ClientSize.Width - en) / 2;
            int y0 = ClientSize.Height - SeritAlt - KucukBoy;
            float merkezX = ClientSize.Width / 2f, merkezY = y0 + KucukBoy / 2f;
            seritAlani = new Rectangle(x0 - 8, y0 - 8, en + 16, KucukBoy + 16);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceOver;
            using (GraphicsPath yol = Yuvarlak(seritAlani, 12))
            using (SolidBrush zemin = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillPath(zemin, yol);

            // Yığına uzaktakiler önce giriyor ki açık fotoğrafa en yakın
            // olanlar ilk hazırlansın.
            for (int d = yarim; d >= 0; d--)
            {
                if (sira + d < n) KucukIste(dosyalar[sira + d]);
                if (d > 0 && sira - d >= 0) KucukIste(dosyalar[sira - d]);
            }
            lock (kilit)
            {
                gorunurKucukler.Clear();
                for (int i = Math.Max(0, sira - yarim - 1); i <= Math.Min(n - 1, sira + yarim + 1); i++)
                    gorunurKucukler.Add(dosyalar[i]);
            }

            g.SetClip(seritAlani);
            int kenar = yarim + 1 + (int)Math.Ceiling(Math.Abs(seritKayma) / adim);
            using (SolidBrush bos = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            {
                // Uzaktakiler önce: ortadaki en son çizilip üstte kalıyor.
                for (int d = kenar; d >= 0; d--)
                {
                    for (int yon = -1; yon <= 1; yon += 2)
                    {
                        if (d == 0 && yon == 1) continue;
                        int i = sira + d * yon;
                        if (i < 0 || i >= n) continue;
                        float cx = merkezX + (i - sira) * adim + seritKayma;
                        float uzaklik = Math.Min(1f, Math.Abs(cx - merkezX) / adim);
                        float olc = 1f - 0.2f * uzaklik;
                        RectangleF kutu = new RectangleF(cx - KucukEn * olc / 2f, merkezY - KucukBoy * olc / 2f,
                            KucukEn * olc, KucukBoy * olc);
                        if (kutu.Right < seritAlani.Left || kutu.Left > seritAlani.Right) continue;
                        seritKutulari.Add(new KeyValuePair<int, Rectangle>(i, Rectangle.Round(kutu)));

                        Bitmap k;
                        lock (kilit) kucukler.TryGetValue(dosyalar[i], out k);
                        using (GraphicsPath yol = Yuvarlak(kutu, 6))
                        {
                            if (k != null) g.DrawImage(k, kutu);
                            else g.FillPath(bos, yol);
                            if (VideoMu(dosyalar[i])) OynatSimgesi(g, kutu);
                            if (uzaklik > 0)
                                using (SolidBrush soluk = new SolidBrush(Color.FromArgb((int)(120 * uzaklik), 0, 0, 0)))
                                    g.FillPath(soluk, yol);
                            if (uzaklik < 1)
                                using (Pen cerceve = new Pen(Color.FromArgb((int)(255 * (1 - uzaklik)), 255, 255, 255), 2f))
                                    g.DrawPath(cerceve, yol);
                        }
                    }
                }
            }
            g.ResetClip();
        }

        static void OynatSimgesi(Graphics g, RectangleF kutu)
        {
            float cx = kutu.Left + kutu.Width / 2f, cy = kutu.Top + kutu.Height / 2f, r = kutu.Height / 6f;
            PointF[] ucgen = {
                new PointF(cx - r * 0.7f, cy - r), new PointF(cx - r * 0.7f, cy + r), new PointF(cx + r, cy) };
            using (SolidBrush beyaz = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                g.FillPolygon(beyaz, ucgen);
        }

        // Yakınlaştırma oranı: ekranın tam ortasında, yarı saydam bir kutuda.
        // Yalnız zum değişince bir süre görünüyor.
        void OranCiz(Graphics g)
        {
            if (!oranGorunur) return;
            string yazi = "%" + Math.Round(olcek * 100);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Font yaziTipi = new Font("Segoe UI", 18f))
            {
                SizeF olcu = g.MeasureString(yazi, yaziTipi);
                float en = olcu.Width + 28, boy = olcu.Height + 12;
                RectangleF kutu = new RectangleF((ClientSize.Width - en) / 2f, (ClientSize.Height - boy) / 2f, en, boy);
                using (GraphicsPath yol = Yuvarlak(kutu, 12))
                using (SolidBrush zemin = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                using (Pen kenar = new Pen(Color.FromArgb(70, 255, 255, 255)))
                using (SolidBrush yaziRengi = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                using (StringFormat bicim = new StringFormat())
                {
                    g.FillPath(zemin, yol);
                    g.DrawPath(kenar, yol);
                    bicim.Alignment = StringAlignment.Center;
                    bicim.LineAlignment = StringAlignment.Center;
                    g.DrawString(yazi, yaziTipi, yaziRengi, kutu, bicim);
                }
            }
        }

        // ---- Önizlemeler ----

        void KucukIste(string yol)
        {
            lock (kilit)
            {
                if (istenen.Add(yol))
                {
                    istekler.Push(yol);
                    uyari.Set();
                }
            }
        }

        void KucukleriTemizle()
        {
            lock (kilit)
            {
                foreach (Bitmap b in kucukler.Values) if (b != null) b.Dispose();
                kucukler.Clear();
                istenen.Clear();
                istekler.Clear();
            }
        }

        void IsciDongusu()
        {
            while (true)
            {
                uyari.WaitOne();
                while (true)
                {
                    string yol;
                    lock (kilit)
                    {
                        if (istekler.Count == 0) break;
                        yol = istekler.Pop();
                    }
                    // Açılamayan dosya için de boş kayıt düşülüyor ki her
                    // çizimde yeniden denenmesin.
                    Bitmap k = KucukYap(yol);
                    bool gorunur;
                    lock (kilit)
                    {
                        kucukler[yol] = k;
                        gorunur = gorunurKucukler.Contains(yol);
                    }
                    if (gorunur) CizIste();
                }
            }
        }

        // Peş peşe biten önizlemeler için tek çizim yeter; her biri ayrı
        // çizim isteseydi ekran gereksiz yere onlarca kez yenilenirdi.
        void CizIste()
        {
            if (Interlocked.Exchange(ref cizimBekliyor, 1) != 0) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Interlocked.Exchange(ref cizimBekliyor, 0);
                    Ciz();
                });
            }
            catch (InvalidOperationException) { } // pencere kapanıyor
        }

        // 16:9'a ortadan kırpılmış, köşeleri yuvarlatılmış küçük resim.
        static Bitmap KucukYap(string yol)
        {
            try
            {
                Image ham;
                MemoryStream ms = null;
                int yon = 1;
                if (VideoMu(yol))
                {
                    // Videonun karesini Gezgin'in kendi küçük resminden alıyoruz:
                    // Windows onları zaten üretip saklıyor, dosyayı okumak gerekmiyor.
                    ham = Kabuk.KucukResim(yol);
                    if (ham == null) return null;
                }
                else
                {
                    byte[] baytlar = File.ReadAllBytes(yol);
                    if (Path.GetExtension(yol).ToLowerInvariant() == ".webp")
                        ham = Webp.Oku(baytlar);
                    else
                    {
                        ms = new MemoryStream(baytlar);
                        ham = Image.FromStream(ms, false, false);
                        yon = YonOku(ham);
                    }
                }
                try
                {
                    // Önce uzun kenarı 320'ye küçültüp çeviriyoruz; dikey
                    // fotoğrafta dönüş en-boyu değiştirdiği için kırpmadan önce.
                    float s0 = Math.Min(1f, 320f / Math.Max(ham.Width, ham.Height));
                    using (Bitmap ara = new Bitmap(Math.Max(1, (int)(ham.Width * s0)), Math.Max(1, (int)(ham.Height * s0))))
                    {
                        using (Graphics g = Graphics.FromImage(ara))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(ham, 0, 0, ara.Width, ara.Height);
                        }
                        YonUygula(ara, yon);

                        float s = Math.Max((float)KucukEn / ara.Width, (float)KucukBoy / ara.Height);
                        float en = ara.Width * s, boy = ara.Height * s;
                        Bitmap k = new Bitmap(KucukEn, KucukBoy, PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(k))
                        using (TextureBrush doku = new TextureBrush(ara))
                        using (GraphicsPath kose = Yuvarlak(new RectangleF(0, 0, KucukEn, KucukBoy), 6))
                        {
                            doku.ScaleTransform(s, s);
                            doku.TranslateTransform((KucukEn - en) / 2f / s, (KucukBoy - boy) / 2f / s, MatrixOrder.Prepend);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.FillPath(doku, kose);
                        }
                        return k;
                    }
                }
                finally
                {
                    ham.Dispose();
                    if (ms != null) ms.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }

        // Fotoğrafın sağ köşesinin biraz üstünde, dışarıda. Üstte yer yoksa
        // (fotoğraf büyütülüp ekranın tepesine dayandıysa) fotoğrafın içine,
        // video ise yanına geçiyor; video ayrı pencerede ve üstünü örterdi.
        Rectangle KapatAlani()
        {
            int x, y;
            if (!IcerikVar)
            {
                x = ClientSize.Width - KapatPay - KapatBoyut;
                y = KapatPay;
            }
            else
            {
                RectangleF f = FotoAlani();
                x = (int)f.Right - KapatBoyut;
                y = (int)f.Top - KapatBoyut - 10;
                if (y < KapatPay)
                {
                    if (video)
                    {
                        x = (int)f.Right + 10;
                        y = (int)f.Top;
                    }
                    else
                    {
                        x = (int)f.Right - KapatPay - KapatBoyut;
                        y = (int)f.Top + KapatPay;
                    }
                }
                x = Math.Max(KapatPay, Math.Min(x, ClientSize.Width - KapatPay - KapatBoyut));
                y = Math.Max(KapatPay, Math.Min(y, ClientSize.Height - KapatPay - KapatBoyut));
            }
            return new Rectangle(x, y, KapatBoyut, KapatBoyut);
        }

        void KapatCiz(Graphics g)
        {
            Rectangle k = KapatAlani();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            Color zemin = kapatUstunde ? Color.FromArgb(230, 196, 43, 28) : Color.FromArgb(150, 0, 0, 0);
            using (SolidBrush firca = new SolidBrush(zemin))
                g.FillEllipse(firca, k);
            using (Pen kalem = new Pen(Color.White, 3f))
            {
                kalem.StartCap = LineCap.Round;
                kalem.EndCap = LineCap.Round;
                int d = 12;
                g.DrawLine(kalem, k.Left + d, k.Top + d, k.Right - d, k.Bottom - d);
                g.DrawLine(kalem, k.Right - d, k.Top + d, k.Left + d, k.Bottom - d);
            }
        }

        // Üst üste binen, gittikçe büyüyen soluk dikdörtgenler: fotoğrafa
        // yakın yerde koyu, uzaklaştıkça açılan yumuşak bir gölge. Biraz
        // aşağı kaydırılmış ki ışık yukarıdan geliyormuş gibi dursun.
        static void Golge(Graphics g, RectangleF alan)
        {
            using (SolidBrush firca = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
            {
                for (int i = GolgeGenislik; i > 0; i--)
                {
                    RectangleF k = alan;
                    k.Inflate(i, i);
                    k.Offset(0, 6);
                    g.FillRectangle(firca, k);
                }
            }
        }

        // ---- Fare ----

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            TekerIsle(e.Delta, e.Location);
        }

        // Video penceresinin üstündeki tekerlek de buraya geliyor.
        void TekerIsle(int delta, Point nokta)
        {
            bool sagBasili = (Control.MouseButtons & MouseButtons.Right) != 0;
            if ((ModifierKeys & Keys.Control) != 0 || sagBasili)
            {
                if (sagBasili) sagTekerKullanildi = true;
                Yakinlas(Math.Pow(1.2, delta / 120.0), nokta);
                return;
            }
            // Dokunmatik yüzey bir çentiği küçük parçalar hâlinde gönderiyor;
            // birikmeden geçilirse en ufak kaydırma fotoğraf atlatıyordu.
            tekerBirikim += delta;
            while (tekerBirikim >= 120) { tekerBirikim -= 120; Gec(-1); }
            while (tekerBirikim <= -120) { tekerBirikim += 120; Gec(1); }
        }

        protected override void WndProc(ref Message m)
        {
            // Tekerleği sağa/sola yatırmak WinForms'ta olay olarak gelmiyor.
            if (m.Msg == WM_MOUSEHWHEEL)
            {
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                int simdi = Environment.TickCount;
                if (delta != 0 && simdi - sonYanaGecis > YanaGecisAraligi)
                {
                    sonYanaGecis = simdi;
                    Gec(delta > 0 ? 1 : -1);
                }
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && KapatAlani().Contains(e.Location)) { Close(); return; }
            if (e.Button == MouseButtons.Left && seritAlani.Contains(e.Location))
            {
                foreach (KeyValuePair<int, Rectangle> kutu in seritKutulari)
                {
                    if (kutu.Value.Contains(e.Location) && kutu.Key != sira)
                    {
                        SiraDegistir(kutu.Key);
                        break;
                    }
                }
                return;
            }
            // Farenin yan tuşları (geri/ileri) da aynı işi görsün.
            if (e.Button == MouseButtons.XButton1) { Gec(-1); return; }
            if (e.Button == MouseButtons.XButton2) { Gec(1); return; }
            if (e.Button == MouseButtons.Right) { sagTekerKullanildi = false; return; }
            if (e.Button == MouseButtons.Left && IcerikVar)
            {
                disariBasildi = !FotoAlani().Contains(e.Location);
                basmaNoktasi = e.Location;
                surukleniyor = true;
                suruklemeFare = e.Location;
                suruklemeKonum = konum;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool ustunde = KapatAlani().Contains(e.Location);
            if (ustunde != kapatUstunde) { kapatUstunde = ustunde; Ciz(); }
            if (!surukleniyor) return;
            konum = new PointF(suruklemeKonum.X + e.X - suruklemeFare.X,
                               suruklemeKonum.Y + e.Y - suruklemeFare.Y);
            sigdir = false;
            Ciz();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                if (!sagTekerKullanildi) menu.Show(this, e.Location);
                sagTekerKullanildi = false;
                return;
            }
            if (!surukleniyor) return;
            surukleniyor = false;
            Cursor = Cursors.Default;
            if (disariBasildi && e.Button == MouseButtons.Left
                && Math.Abs(e.X - basmaNoktasi.X) < 5 && Math.Abs(e.Y - basmaNoktasi.Y) < 5)
            {
                Close();
                return;
            }
            Ciz();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (kapatUstunde) { kapatUstunde = false; Ciz(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || seritAlani.Contains(e.Location)) return;
            CiftTikla(e.Location);
        }

        // Sığdırılmışsa gerçek boyuta (tıklanan nokta yerinde kalarak),
        // yakınlaştırılmışsa ilk görünüme.
        void CiftTikla(Point nokta)
        {
            if (!IcerikVar) { DosyaSec(); return; }
            if (sigdir) Yakinlas(1.0 / olcek, nokta);
            else { Sigdir(); OranGoster(); }
            Baslik();
        }

        // ---- Sürükle bırak ----

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] dosya = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dosya != null && dosya.Length > 0) Ac(dosya[0]);
        }

        // ---- Klavye ----

        protected override bool ProcessCmdKey(ref Message msg, Keys tus)
        {
            switch (tus)
            {
                case Keys.Left:
                case Keys.PageUp:
                    Gec(-1); return true;
                case Keys.Right:
                case Keys.PageDown:
                case Keys.Space:
                    Gec(1); return true;
                case Keys.Control | Keys.O:
                    DosyaSec(); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0:
                    Sigdir(); Baslik(); OranGoster(); return true;
                case Keys.Control | Keys.D1:
                case Keys.Control | Keys.NumPad1:
                    GercekBoyut(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add:
                    Yakinlas(1.2, PencereOrtasi()); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract:
                    Yakinlas(1 / 1.2, PencereOrtasi()); return true;
                case Keys.Escape:
                    Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, tus);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && resim != null) resim.Dispose();
            if (disposing && yuzey != null) yuzey.Dispose();
            if (disposing && gorunum != null) gorunum.Dispose();
            if (disposing) { kaliteZamani.Dispose(); animZamani.Dispose(); }
            if (disposing) KucukleriTemizle();
            if (disposing) oranZamani.Dispose();
            base.Dispose(disposing);
        }
    }

    // Gezgin'in küçük resimleri (videolar için).
    static class Kabuk
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(Boyut boyut, int bayrak, out IntPtr hbmp);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Boyut { public int En, Boy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string yol, IntPtr bc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory oge);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr nesne);

        const int SIIGBF_BIGGERSIZEOK = 0x1;
        // Küçük resim yoksa simge vermesin; boş kutu simgeden iyi.
        const int SIIGBF_THUMBNAILONLY = 0x8;

        public static Bitmap KucukResim(string yol)
        {
            IShellItemImageFactory oge;
            SHCreateItemFromParsingName(yol, IntPtr.Zero,
                typeof(IShellItemImageFactory).GUID, out oge);
            try
            {
                IntPtr hbmp;
                Boyut boyut = new Boyut { En = 320, Boy = 320 };
                if (oge.GetImage(boyut, SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY, out hbmp) != 0)
                    return null;
                try { return Image.FromHbitmap(hbmp); }
                finally { DeleteObject(hbmp); }
            }
            finally
            {
                Marshal.ReleaseComObject(oge);
            }
        }
    }

    // Katmanlı pencerenin görüntüsü. Bellekte Windows'un doğrudan
    // okuyabildiği bir resim (DIB) tutuluyor ve GDI+ ona doğrudan çiziyor:
    // her karede resmi kopyalayıp çevirmek sürüklemeyi yavaşlatıyordu.
    static class Katman
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Nokta { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct Boyut { public int En, Boy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct Karisim { public byte Islem, Bayrak, SabitSaydamlik, AlfaBicimi; }

        [StructLayout(LayoutKind.Sequential)]
        struct BilgiBasligi
        {
            public int Boyut, En, Boy;
            public short Duzlem, Bit;
            public int Sikistirma, ResimBoyutu, YatayCozunurluk, DikeyCozunurluk, RenkSayisi, OnemliRenk;
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr pencere);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr pencere, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr nesne);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr nesne);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr dc, ref BilgiBasligi bilgi, int kullanim,
            out IntPtr bitler, IntPtr bolum, int kayma);
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr pencere, IntPtr hedefDc, ref Nokta hedef,
            ref Boyut boyut, IntPtr kaynakDc, ref Nokta kaynak, int renkAnahtari,
            ref Karisim karisim, int bayrak);

        const int ULW_ALPHA = 2;
        const byte AC_SRC_ALPHA = 1;

        public sealed class Yuzey : IDisposable
        {
            public readonly int En, Boy;
            // Ön-çarpılmış 32 bit: Windows'un katmanlı pencereden beklediği biçim.
            public readonly Bitmap Resim;
            readonly IntPtr dc, hbmp, eski;

            public Yuzey(int en, int boy)
            {
                En = en;
                Boy = boy;
                BilgiBasligi bilgi = new BilgiBasligi();
                bilgi.Boyut = Marshal.SizeOf(typeof(BilgiBasligi));
                bilgi.En = en;
                bilgi.Boy = -boy;   // eksi: satırlar yukarıdan aşağı
                bilgi.Duzlem = 1;
                bilgi.Bit = 32;
                IntPtr ekranDc = GetDC(IntPtr.Zero);
                dc = CreateCompatibleDC(ekranDc);
                ReleaseDC(IntPtr.Zero, ekranDc);
                IntPtr bitler;
                hbmp = CreateDIBSection(dc, ref bilgi, 0, out bitler, IntPtr.Zero, 0);
                eski = SelectObject(dc, hbmp);
                Resim = new Bitmap(en, boy, en * 4, PixelFormat.Format32bppPArgb, bitler);
            }

            public void Guncelle(Form pencere)
            {
                Nokta hedef = new Nokta { X = pencere.Left, Y = pencere.Top };
                Nokta kaynak = new Nokta();
                Boyut boyut = new Boyut { En = En, Boy = Boy };
                Karisim karisim = new Karisim { SabitSaydamlik = 255, AlfaBicimi = AC_SRC_ALPHA };
                UpdateLayeredWindow(pencere.Handle, IntPtr.Zero, ref hedef, ref boyut,
                    dc, ref kaynak, 0, ref karisim, ULW_ALPHA);
            }

            public void Dispose()
            {
                Resim.Dispose();
                SelectObject(dc, eski);
                DeleteObject(hbmp);
                DeleteDC(dc);
            }
        }
    }

    // Windows'a "JPG ve PNG açabilen bir uygulama" olarak tanıtma.
    //
    // Windows 10'dan beri bir uygulama kendini sessizce varsayılan yapamıyor:
    // seçim imzalı bir kayıtta tutuluyor ve son tıklamayı kullanıcı yapmak
    // zorunda. Yapabildiğimiz, kendimizi listeye koyup Ayarlar'da doğrudan
    // bu uygulamanın sayfasını açmak.
    //
    // Kayıttaki iç adlar (FotoGoruntuleyici…) uygulamanın eski adından kalma
    // ve bilerek değiştirilmedi: Windows varsayılan seçimini bu adla tutuyor,
    // değişirse önceden yapılmış varsayılan seçimi boşa düşerdi. Görünen ad
    // ApplicationName ve exe'nin kendi açıklaması.
    static class Kayit
    {
        const string Ad = "FotoGoruntuleyici";
        const string ProgId = "FotoGoruntuleyici.Resim";
        const string Yetenekler = @"Software\FotoGoruntuleyici\Capabilities";
        static readonly string[] Uzantilar = {
            ".jpg", ".jpeg", ".png", ".webp",
            ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int olay, int bayrak, IntPtr a, IntPtr b);

        // Her açılışta yazılıyor: exe başka klasöre taşınırsa kayıt da
        // yeni yeri göstersin. Yalnız kullanıcının kendi kaydına (HKCU)
        // yazıldığı için yönetici izni istemiyor.
        public static void Kaydet()
        {
            try
            {
                string exe = Application.ExecutablePath;
                RegistryKey k = Registry.CurrentUser;
                string sinif = @"Software\Classes\" + ProgId;
                Yaz(k, sinif, null, "Fotoğraf");
                Yaz(k, sinif + @"\DefaultIcon", null, "\"" + exe + "\",0");
                Yaz(k, sinif + @"\shell\open\command", null, "\"" + exe + "\" \"%1\"");

                Yaz(k, Yetenekler, "ApplicationName", "Ahmetview");
                Yaz(k, Yetenekler, "ApplicationDescription", "JPG, PNG ve WebP fotoğraflarını, MP4, MOV ve diğer videoları açar.");
                foreach (string u in Uzantilar)
                {
                    Yaz(k, Yetenekler + @"\FileAssociations", u, ProgId);
                    Yaz(k, @"Software\Classes\" + u + @"\OpenWithProgids", ProgId, "");
                }
                Yaz(k, @"Software\RegisteredApplications", Ad, Yetenekler);

                // Gezgin "Birlikte aç" listesini yeniden okusun.
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        static void Yaz(RegistryKey kok, string yol, string ad, string deger)
        {
            using (RegistryKey k = kok.CreateSubKey(yol)) k.SetValue(ad ?? "", deger);
        }

        static bool VarsayilanMi()
        {
            foreach (string u in Uzantilar)
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + u + @"\UserChoice"))
                {
                    if (k == null || (k.GetValue("ProgId") as string) != ProgId) return false;
                }
            }
            return true;
        }

        // Yalnız bir kez soruluyor; "Hayır" denince bir daha rahatsız etmiyor.
        // Sonradan istenirse sağ tık menüsünde duruyor.
        public static void IlkSefer(IWin32Window sahip)
        {
            try
            {
                if (VarsayilanMi()) return;
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\" + Ad))
                {
                    if (k.GetValue("Soruldu") != null) return;
                    k.SetValue("Soruldu", 1);
                }
                DialogResult c = MessageBox.Show(sahip,
                    "Fotoğraflar ve videolar çift tıklayınca Ahmetview'da açılsın mı?\n\n"
                    + "Windows bu seçimi kendisi yapmamıza izin vermiyor. Ayarlar açılacak; "
                    + "orada \"Varsayılan olarak ayarla\" düğmesine ya da .jpg, .jpeg, .png ve .webp "
                    + "satırlarına tıklayıp Ahmetview'u seçmen yeterli.",
                    "Varsayılan uygulama", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (c == DialogResult.Yes) AyarlariAc();
            }
            catch { }
        }

        public static void AyarlariAc()
        {
            Kaydet();
            try { Process.Start("ms-settings:defaultapps?registeredAppUser=" + Ad); }
            catch
            {
                try { Process.Start("ms-settings:defaultapps"); } catch { }
            }
        }
    }
}
