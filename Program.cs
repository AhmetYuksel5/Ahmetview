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
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // Tekerlek yana bastırılı tutulunca sürekli ileti gönderiyor;
        // bu aralık olmasa tek basışta birkaç fotoğraf birden geçiliyordu.
        const int YanaGecisAraligi = 300;

        static readonly string[] Uzantilar = { ".jpg", ".jpeg", ".png", ".webp" };

        // Gezgin'in sıralaması: "foto2" "foto10"dan önce gelsin.
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        readonly string baslangic;
        List<string> dosyalar = new List<string>();
        int sira = -1;
        Image resim;
        string hata;

        float olcek = 1f;
        PointF konum;
        bool sigdir = true;

        bool surukleniyor;
        Point suruklemeFare;
        PointF suruklemeKonum;

        int sonYanaGecis;
        int tekerBirikim;
        bool tamEkran;

        public Pencere(string yol)
        {
            baslangic = yol;
            Text = "Ahmetview";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Color.FromArgb(24, 24, 24);
            ForeColor = Color.FromArgb(150, 150, 150);
            DoubleBuffered = true;
            AllowDrop = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 750);
            MinimumSize = new Size(300, 200);

            ContextMenuStrip = new ContextMenuStrip();
            ContextMenuStrip.Items.Add("Aç…  (Ctrl+O)", null, delegate { DosyaSec(); });
            ContextMenuStrip.Items.Add("Varsayılan uygulama yap", null, delegate { Kayit.AyarlariAc(); });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Kayit.Kaydet();
            if (baslangic != null) Ac(baslangic);
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
            sira = (sira + yon + dosyalar.Count) % dosyalar.Count;
            Yukle();
        }

        void Yukle()
        {
            if (resim != null) { resim.Dispose(); resim = null; }
            if (sira >= 0)
            {
                hata = null;
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
                }
                catch (Exception ex)
                {
                    hata = Path.GetFileName(dosyalar[sira]) + " açılamadı: " + ex.Message;
                }
            }
            Sigdir();
            Baslik();
        }

        // Telefon fotoğrafları yan çekilmiş kaydedilip dönüşü EXIF'e yazılıyor;
        // uygulanmazsa dikey fotoğraf yan yatık görünüyor.
        static void Dondur(Image img)
        {
            if (Array.IndexOf(img.PropertyIdList, ExifYon) < 0) return;
            switch (img.GetPropertyItem(ExifYon).Value[0])
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
            if (resim == null || sira < 0) { Text = "Ahmetview"; return; }
            Text = Path.GetFileName(dosyalar[sira])
                + "  —  " + (sira + 1) + " / " + dosyalar.Count
                + "  —  %" + Math.Round(olcek * 100);
        }

        void DosyaSec()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Fotoğraflar (*.jpg; *.jpeg; *.png; *.webp)|*.jpg;*.jpeg;*.png;*.webp";
                if (d.ShowDialog(this) == DialogResult.OK) Ac(d.FileName);
            }
        }

        // ---- Yakınlaştırma ----

        void Sigdir()
        {
            sigdir = true;
            if (resim != null)
            {
                float s = Math.Min((float)ClientSize.Width / resim.Width,
                                   (float)ClientSize.Height / resim.Height);
                // Küçük fotoğrafı büyütüp bulanıklaştırmıyoruz.
                olcek = Math.Min(s, 1f);
                Ortala();
            }
            Invalidate();
        }

        void GercekBoyut()
        {
            if (resim == null) return;
            sigdir = false;
            olcek = 1f;
            Ortala();
            Baslik();
            Invalidate();
        }

        void Ortala()
        {
            konum = new PointF((ClientSize.Width - resim.Width * olcek) / 2f,
                               (ClientSize.Height - resim.Height * olcek) / 2f);
        }

        // İmlecin altındaki nokta yerinde kalsın diye konum da kaydırılıyor.
        void Yakinlas(double carpan, PointF merkez)
        {
            if (resim == null) return;
            float yeni = (float)Math.Max(0.01, Math.Min(50.0, olcek * carpan));
            float k = yeni / olcek;
            konum = new PointF(merkez.X - (merkez.X - konum.X) * k,
                               merkez.Y - (merkez.Y - konum.Y) * k);
            olcek = yeni;
            sigdir = false;
            Baslik();
            Invalidate();
        }

        PointF PencereOrtasi()
        {
            return new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (sigdir) Sigdir(); else Invalidate();
        }

        // ---- Çizim ----

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            if (resim == null)
            {
                string yazi = hata ?? "Bir fotoğrafı buraya sürükleyin ya da çift tıklayın";
                TextRenderer.DrawText(g, yazi, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            // Sürüklerken her karede yüksek kaliteli küçültme takılmaya yol
            // açıyor; bıraktığında tekrar keskin çiziliyor.
            if (olcek >= 3f) g.InterpolationMode = InterpolationMode.NearestNeighbor;
            else if (surukleniyor) g.InterpolationMode = InterpolationMode.Bilinear;
            else g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(resim, new RectangleF(konum.X, konum.Y,
                resim.Width * olcek, resim.Height * olcek));
        }

        // ---- Fare ----

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            {
                Yakinlas(Math.Pow(1.2, e.Delta / 120.0), e.Location);
                return;
            }
            // Dokunmatik yüzey bir çentiği küçük parçalar hâlinde gönderiyor;
            // birikmeden geçilirse en ufak kaydırma fotoğraf atlatıyordu.
            tekerBirikim += e.Delta;
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
            // Farenin yan tuşları (geri/ileri) da aynı işi görsün.
            if (e.Button == MouseButtons.XButton1) { Gec(-1); return; }
            if (e.Button == MouseButtons.XButton2) { Gec(1); return; }
            if (e.Button == MouseButtons.Left && resim != null)
            {
                surukleniyor = true;
                suruklemeFare = e.Location;
                suruklemeKonum = konum;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!surukleniyor) return;
            konum = new PointF(suruklemeKonum.X + e.X - suruklemeFare.X,
                               suruklemeKonum.Y + e.Y - suruklemeFare.Y);
            sigdir = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!surukleniyor) return;
            surukleniyor = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (resim == null) DosyaSec();
            else Sigdir();
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
                    Sigdir(); Baslik(); return true;
                case Keys.Control | Keys.D1:
                case Keys.Control | Keys.NumPad1:
                    GercekBoyut(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add:
                    Yakinlas(1.2, PencereOrtasi()); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract:
                    Yakinlas(1 / 1.2, PencereOrtasi()); return true;
                case Keys.F11:
                    TamEkran(!tamEkran); return true;
                case Keys.Escape:
                    if (tamEkran) TamEkran(false); else Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, tus);
        }

        void TamEkran(bool ac)
        {
            tamEkran = ac;
            // Önce Normal'e almak gerekiyor; zaten büyütülmüş pencere
            // çerçevesiz olunca görev çubuğunun üstünü kaplamıyor.
            WindowState = FormWindowState.Normal;
            FormBorderStyle = ac ? FormBorderStyle.None : FormBorderStyle.Sizable;
            WindowState = ac ? FormWindowState.Maximized : FormWindowState.Normal;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && resim != null) resim.Dispose();
            base.Dispose(disposing);
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
        static readonly string[] Uzantilar = { ".jpg", ".jpeg", ".png", ".webp" };

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
                Yaz(k, Yetenekler, "ApplicationDescription", "JPG, PNG ve WebP fotoğraflarını açar.");
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
                    "Fotoğraflar çift tıklayınca Ahmetview'da açılsın mı?\n\n"
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
