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
using System.Windows.Forms;

namespace Ahmetview
{
    class VideoPencere : Form
    {
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        readonly System.Windows.Controls.MediaElement oynatici;
        bool oynuyor;

        // Bu pencerenin üstündeki fare hareketleri ana pencereye hiç
        // ulaşmıyor; ana pencerenin işi olanlar böyle iletiliyor.
        public event Action<int, int> Acildi;
        public event Action<string> Hata;
        public event Action<int, Point> Teker;
        public event Action SagBasildi;
        public event Action<Point> SagBirakildi;

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
                if (Acildi != null) Acildi(oynatici.NaturalVideoWidth, oynatici.NaturalVideoHeight);
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
            kap.MouseWheel += delegate (object k, System.Windows.Input.MouseWheelEventArgs e)
            {
                if (Teker != null) Teker(e.Delta, System.Windows.Forms.Cursor.Position);
                e.Handled = true;
            };
            kap.MouseLeftButtonUp += delegate { OynatDuraklat(); };
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
            oynatici.Source = new Uri(yol);
            oynatici.Play();
            oynuyor = true;
        }

        public void Durdur()
        {
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
        }
    }
}
