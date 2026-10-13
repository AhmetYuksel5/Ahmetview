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
        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCLIENT = 1, HTCAPTION = 2;
        const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        // Çerçeve olmadığı için kenardan bu kadar içeride tutunca pencere
        // boyutlanıyor.
        const int Tutamak = 8;

        // Sağ üstteki kapatma düğmesi. Pay tutamaktan büyük ki düğmeye
        // basmak yanlışlıkla boyutlandırmaya dönmesin.
        const int KapatBoyut = 40;
        const int KapatPay = 14;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr p, int ileti, IntPtr w, IntPtr l);

        // Fotoğrafın dışında kalan alanın koyuluğu (0 tam saydam, 255 siyah)
        // ve sığdırırken fotoğrafla ekran kenarı arasında bırakılan pay;
        // pay olmasa gölge görünmüyordu.
        const int ArkaKoyuluk = 110;
        const int Kenar = 32;
        const int GolgeGenislik = 24;

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
        bool sagTekerKullanildi;
        bool kapatUstunde;

        Bitmap tampon;
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        public Pencere(string yol)
        {
            baslangic = yol;
            Text = "Ahmetview";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.None;
            AllowDrop = true;
            MinimumSize = new Size(300, 200);
            // Fare hangi ekrandaysa onun ortasında açılıyor; küçük ekranda
            // ekrandan taşmasın.
            StartPosition = FormStartPosition.Manual;
            Rectangle calisma = Screen.FromPoint(Cursor.Position).WorkingArea;
            int en = Math.Min(1100, calisma.Width), boy = Math.Min(750, calisma.Height);
            Bounds = new Rectangle(calisma.X + (calisma.Width - en) / 2,
                                   calisma.Y + (calisma.Height - boy) / 2, en, boy);

            // ContextMenuStrip özelliğine bağlanmıyor: o zaman sağ tuş
            // bırakılınca her seferinde açılıyor, sağ tuş + tekerlek ile
            // yakınlaştırdıktan sonra da.
            menu.Items.Add("Aç…  (Ctrl+O)", null, delegate { DosyaSec(); });
            menu.Items.Add("Varsayılan uygulama yap", null, delegate { Kayit.AyarlariAc(); });
            menu.Items.Add("Kapat  (Esc)", null, delegate { Close(); });
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
                float s = Math.Min((float)(ClientSize.Width - 2 * Kenar) / resim.Width,
                                   (float)(ClientSize.Height - 2 * Kenar) / resim.Height);
                // Küçük fotoğrafı büyütüp bulanıklaştırmıyoruz.
                olcek = Math.Min(s, 1f);
                Ortala();
            }
            Ciz();
        }

        void GercekBoyut()
        {
            if (resim == null) return;
            sigdir = false;
            olcek = 1f;
            Ortala();
            Baslik();
            Ciz();
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
            Ciz();
        }

        RectangleF FotoAlani()
        {
            if (resim == null) return RectangleF.Empty;
            return new RectangleF(konum.X, konum.Y, resim.Width * olcek, resim.Height * olcek);
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
        // bir tampona çizilip Windows'a tek seferde veriliyor.
        void Ciz()
        {
            if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (tampon == null || tampon.Size != ClientSize)
            {
                if (tampon != null) tampon.Dispose();
                tampon = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            }
            using (Graphics g = Graphics.FromImage(tampon))
            {
                // Arka plan tamamen saydam olsa tıklama pencereye değil
                // arkadaki masaüstüne gider; hafif koyuluk pencereyi tutup
                // taşımayı da mümkün kılıyor.
                g.Clear(Color.FromArgb(ArkaKoyuluk, 0, 0, 0));
                if (resim == null)
                {
                    string yazi = hata ?? "Bir fotoğrafı buraya sürükleyin ya da çift tıklayın\nEsc kapatır";
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
                else
                {
                    RectangleF alan = FotoAlani();
                    Golge(g, alan);
                    // Sürüklerken her karede yüksek kaliteli küçültme takılmaya
                    // yol açıyor; bıraktığında tekrar keskin çiziliyor.
                    if (olcek >= 3f) g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    else if (surukleniyor) g.InterpolationMode = InterpolationMode.Bilinear;
                    else g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(resim, alan);
                }
                KapatCiz(g);
            }
            Katman.Guncelle(this, tampon);
        }

        Rectangle KapatAlani()
        {
            return new Rectangle(ClientSize.Width - KapatPay - KapatBoyut, KapatPay, KapatBoyut, KapatBoyut);
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
                int d = 13;
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
            bool sagBasili = (Control.MouseButtons & MouseButtons.Right) != 0;
            if ((ModifierKeys & Keys.Control) != 0 || sagBasili)
            {
                if (sagBasili) sagTekerKullanildi = true;
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
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    long l = (long)m.LParam;
                    Point p = PointToClient(new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF)));
                    int kenar = KenarBul(p);
                    if (kenar != 0) m.Result = (IntPtr)kenar;
                }
                return;
            }
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

        int KenarBul(Point p)
        {
            bool sol = p.X < Tutamak, sag = p.X >= ClientSize.Width - Tutamak;
            bool ust = p.Y < Tutamak, alt = p.Y >= ClientSize.Height - Tutamak;
            if (ust && sol) return HTTOPLEFT;
            if (ust && sag) return HTTOPRIGHT;
            if (alt && sol) return HTBOTTOMLEFT;
            if (alt && sag) return HTBOTTOMRIGHT;
            if (sol) return HTLEFT;
            if (sag) return HTRIGHT;
            if (ust) return HTTOP;
            if (alt) return HTBOTTOM;
            return 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Farenin yan tuşları (geri/ileri) da aynı işi görsün.
            if (e.Button == MouseButtons.Left && KapatAlani().Contains(e.Location)) { Close(); return; }
            if (e.Button == MouseButtons.XButton1) { Gec(-1); return; }
            if (e.Button == MouseButtons.XButton2) { Gec(1); return; }
            if (e.Button == MouseButtons.Right) { sagTekerKullanildi = false; return; }
            // Fotoğrafın dışındaki koyu alandan tutunca pencere taşınıyor:
            // Windows'a başlık çubuğundan tutulmuş gibi bildiriyoruz.
            if (e.Button == MouseButtons.Left && resim != null && !FotoAlani().Contains(e.Location))
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                return;
            }
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
            if (e.Button != MouseButtons.Left) return;
            // Sığdırılmışsa gerçek boyuta (tıklanan nokta yerinde kalarak),
            // yakınlaştırılmışsa ilk görünüme.
            if (resim == null) DosyaSec();
            else if (sigdir) Yakinlas(1.0 / olcek, e.Location);
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
                case Keys.Escape:
                    Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, tus);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && resim != null) resim.Dispose();
            if (disposing && tampon != null) tampon.Dispose();
            base.Dispose(disposing);
        }
    }

    // Hazır çizilmiş 32 bit bir resmi katmanlı pencerenin görüntüsü yapmak.
    static class Katman
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Nokta { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct Boyut { public int En, Boy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct Karisim { public byte Islem, Bayrak, SabitSaydamlik, AlfaBicimi; }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr pencere);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr pencere, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr nesne);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr nesne);
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr pencere, IntPtr hedefDc, ref Nokta hedef,
            ref Boyut boyut, IntPtr kaynakDc, ref Nokta kaynak, int renkAnahtari,
            ref Karisim karisim, int bayrak);

        const int ULW_ALPHA = 2;
        const byte AC_SRC_ALPHA = 1;

        public static void Guncelle(Form pencere, Bitmap resim)
        {
            IntPtr ekranDc = GetDC(IntPtr.Zero);
            IntPtr bellekDc = CreateCompatibleDC(ekranDc);
            IntPtr hbmp = IntPtr.Zero, eski = IntPtr.Zero;
            try
            {
                // GetHbitmap saydamlığı Windows'un beklediği ön-çarpılmış
                // biçime çeviriyor.
                hbmp = resim.GetHbitmap(Color.FromArgb(0));
                eski = SelectObject(bellekDc, hbmp);
                Nokta hedef = new Nokta { X = pencere.Left, Y = pencere.Top };
                Nokta kaynak = new Nokta();
                Boyut boyut = new Boyut { En = resim.Width, Boy = resim.Height };
                Karisim karisim = new Karisim { SabitSaydamlik = 255, AlfaBicimi = AC_SRC_ALPHA };
                UpdateLayeredWindow(pencere.Handle, ekranDc, ref hedef, ref boyut,
                    bellekDc, ref kaynak, 0, ref karisim, ULW_ALPHA);
            }
            finally
            {
                if (hbmp != IntPtr.Zero)
                {
                    SelectObject(bellekDc, eski);
                    DeleteObject(hbmp);
                }
                DeleteDC(bellekDc);
                ReleaseDC(IntPtr.Zero, ekranDc);
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
