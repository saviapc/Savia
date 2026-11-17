// Savia - Lógica de la interfaz. El diseño está en Ventana.xaml.
// El trabajo pesado corre en segundo plano (Task.Run) para que la ventana siempre vaya fluida.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace Savia
{
    public class Ventana
    {
        public readonly Window Win;
        readonly Dictionary<string, object> ui = new Dictionary<string, object>();
        T E<T>(string n) where T : class { return ui[n] as T; }

        Hardware hw;
        List<ProgramaInicio> inicio = new List<ProgramaInicio>();
        List<Hallazgo> diag = new List<Hallazgo>();
        List<AppPreinstalada> apps = new List<AppPreinstalada>();
        string perfilElegido;
        string reporteFinal;
        FrameworkElement vistaActual, vistaSaliente;
        double progresoMeta, progresoActual, total, hechos;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        static readonly CultureInfo Es = new CultureInfo("es-ES");

        static readonly Dictionary<string, string> Titulos = new Dictionary<string, string> {
            { "Competitivo", "Competitivo" }, { "Calidad", "Calidad" }, { "Laptop", "Portátil" }, { "Streaming", "Streaming" } };
        static readonly Dictionary<string, string> Descripciones = new Dictionary<string, string> {
            { "Competitivo", "Máximos FPS y la menor latencia posible." }, { "Calidad", "Juegos grandes con menos tirones." },
            { "Laptop", "Más rendimiento cuidando batería y calor." }, { "Streaming", "Jugar y transmitir a la vez, sin cortes." } };
        static readonly Dictionary<string, string> Iconos = new Dictionary<string, string> {
            { "Competitivo", "M 2,12 L 12,2 L 22,12 L 12,22 Z M 12,8 L 16,12 L 12,16 L 8,12 Z" },
            { "Calidad", "M 12,2 C 18,2 22,7 22,12 C 22,18 17,22 12,22 C 6,22 2,17 2,12 C 2,7 6,2 12,2 Z M 12,7 C 15,7 17,9 17,12 C 17,15 15,17 12,17" },
            { "Laptop", "M 4,5 L 20,5 L 20,16 L 4,16 Z M 1,19 L 23,19" },
            { "Streaming", "M 12,9 C 13.7,9 15,10.3 15,12 C 15,13.7 13.7,15 12,15 C 10.3,15 9,13.7 9,12 C 9,10.3 10.3,9 12,9 Z M 6,6 C 2.7,9.3 2.7,14.7 6,18 M 18,6 C 21.3,9.3 21.3,14.7 18,18" } };

        public Ventana()
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("Savia.Ventana.xaml"))
                Win = (Window)XamlReader.Load(st);
            foreach (string n in new[] { "Arco", "Blob1", "Blob2", "Blob3", "BtnCerrar", "BtnContinuar", "BtnHistorial", "BtnMin", "BtnOptimizar", "BtnReporte",
                "BtnVolver1", "BtnVolver2", "BtnVolver3", "Chips", "ChkEfectos", "ChkLimpieza", "ChkPapelera", "ChkRam", "LineaPapelera", "LineaRam", "TxtRam", "ChkSimular", "ListaHistorial", "ListaInicio", "ListaApps", "Log", "PanelDiag", "PanelTrasReinicio", "TxtTrasReinicio", "ChipsTrasReinicio", "BtnReiniciar",
                "PanelFinal", "PanelMedicion", "Pulso", "Root", "ScrollLog", "Tarjetas", "TitleBar", "TxtEfectos", "TxtEstado", "TxtPorcentaje", "TxtResumenPerfil",
                "TxtSubEstado", "VistaAjustes", "VistaHistorial", "VistaInicio", "VistaProgreso" })
                ui[n] = Win.FindName(n);
            try { Win.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("Savia.icono.ico")); } catch { }

            Directory.CreateDirectory(Motor.DirRespaldos);
            Directory.CreateDirectory(Motor.DirReportes);
            vistaActual = E<FrameworkElement>("VistaInicio");

            int i = 0;
            foreach (string p in Catalogo.Perfiles) { E<Panel>("Tarjetas").Children.Add(NuevaTarjeta(p)); i++; }

            E<UIElement>("TitleBar").MouseLeftButtonDown += (o, e) => Win.DragMove();
            E<Button>("BtnCerrar").Click += (o, e) =>
            {
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
                a.Completed += (o2, e2) => Win.Close();
                E<UIElement>("Root").BeginAnimation(UIElement.OpacityProperty, a);
            };
            E<Button>("BtnMin").Click += (o, e) => Win.WindowState = WindowState.Minimized;
            E<Button>("BtnHistorial").Click += (o, e) => MostrarHistorial();
            E<Button>("BtnContinuar").Click += (o, e) => MostrarAjustes();
            E<Button>("BtnVolver1").Click += (o, e) => MostrarVista(E<FrameworkElement>("VistaInicio"));
            E<Button>("BtnVolver2").Click += (o, e) => MostrarVista(E<FrameworkElement>("VistaInicio"));
            E<Button>("BtnReiniciar").Click += (o, e) =>
            {
                // Primer clic pide confirmación en el propio botón; el segundo reinicia en 5 segundos.
                var b = E<Button>("BtnReiniciar");
                if ((string)b.Content != "¿seguro? pulsa otra vez") { b.Content = "¿seguro? pulsa otra vez"; return; }
                b.Content = "reiniciando…"; b.IsEnabled = false;
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Savia: reiniciando para terminar la optimización\"") { UseShellExecute = false, CreateNoWindow = true });
            };
            E<Button>("BtnVolver3").Click += (o, e) => MostrarVista(E<FrameworkElement>("VistaInicio"));
            E<Button>("BtnOptimizar").Click += (o, e) => Optimizar();
            E<Button>("BtnReporte").Click += (o, e) => { if (reporteFinal != null) AbrirReporte(reporteFinal); };

            timer.Tick += (o, e) =>
            {
                // El anillo persigue su meta con suavidad (sin saltos).
                if (Math.Abs(progresoMeta - progresoActual) > 0.001)
                {
                    progresoActual += (progresoMeta - progresoActual) * 0.12;
                    PonerArco(progresoActual);
                }
            };
            Win.Loaded += (o, e) => AlCargar();
        }

        // ============================================================ Animación
        static IEasingFunction Ease(string tipo = "Cubic")
        {
            EasingFunctionBase e = tipo == "Back" ? (EasingFunctionBase)new BackEase { Amplitude = 0.4 } : new CubicEase();
            e.EasingMode = EasingMode.EaseOut;
            return e;
        }

        static void Anim(Animatable obj, DependencyProperty prop, double hasta, int ms = 450, double? desde = null, int retraso = 0, string ease = "Cubic")
        {
            var a = new DoubleAnimation { To = hasta, Duration = TimeSpan.FromMilliseconds(ms), BeginTime = TimeSpan.FromMilliseconds(retraso), EasingFunction = Ease(ease) };
            if (desde.HasValue) a.From = desde;
            obj.BeginAnimation(prop, a);
        }

        static void Anim(UIElement obj, DependencyProperty prop, double hasta, int ms = 450, double? desde = null, int retraso = 0)
        {
            var a = new DoubleAnimation { To = hasta, Duration = TimeSpan.FromMilliseconds(ms), BeginTime = TimeSpan.FromMilliseconds(retraso), EasingFunction = Ease() };
            if (desde.HasValue) a.From = desde;
            obj.BeginAnimation(prop, a);
        }

        /// <summary>Aparece subiendo suavemente.</summary>
        static void Entrada(UIElement el, int retraso = 0, double desdeY = 14)
        {
            var tt = el.RenderTransform as TranslateTransform;
            if (tt == null) { tt = new TranslateTransform(); el.RenderTransform = tt; }
            el.Opacity = 0;
            Anim(el, UIElement.OpacityProperty, 1, 520, 0, retraso);
            Anim(tt, TranslateTransform.YProperty, 0, 650, desdeY, retraso);
        }

        static void Flotar(UIElement el, double dx, double dy, int seg)
        {
            var tt = (TranslateTransform)el.RenderTransform;
            foreach (var par in new[] { Tuple.Create(TranslateTransform.XProperty, dx), Tuple.Create(TranslateTransform.YProperty, dy) })
            {
                var a = new DoubleAnimation(0, par.Item2, TimeSpan.FromSeconds(seg)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                tt.BeginAnimation(par.Item1, a);
                seg += 4;   // X e Y desfasados: movimiento menos mecánico
            }
        }

        /// <summary>La pantalla actual se desvanece hacia arriba y la nueva entra desde abajo.</summary>
        void MostrarVista(FrameworkElement nueva)
        {
            if (vistaActual == nueva) return;
            vistaSaliente = vistaActual;
            vistaActual = nueva;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            a.Completed += (o, e) =>
            {
                vistaSaliente.Visibility = Visibility.Collapsed;
                vistaActual.Visibility = Visibility.Visible;
                Entrada(vistaActual, 0, 22);
            };
            vistaSaliente.BeginAnimation(UIElement.OpacityProperty, a);
            Anim((TranslateTransform)vistaSaliente.RenderTransform, TranslateTransform.YProperty, -12, 200);
        }

        // ============================================================ Elementos
        static SolidColorBrush Pincel(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        static TextBlock Texto(string t, double tam = 14, string color = "#EDE8DF", bool seminegrita = false)
        {
            return new TextBlock { Text = t, FontSize = tam, TextWrapping = TextWrapping.Wrap, Foreground = Pincel(color), FontWeight = seminegrita ? FontWeights.SemiBold : FontWeights.Normal };
        }

        static Border Chip(string etiqueta, string valor, bool verde = false)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Texto(etiqueta + "  ", 13, "#8C877C"));
            sp.Children.Add(Texto(valor, 13, verde ? "#A9C29B" : "#EDE8DF", true));
            return new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 10, 10), Background = Pincel("#1A1A17"), Child = sp };
        }

        RadioButton NuevaTarjeta(string perfil)
        {
            var rb = new RadioButton { GroupName = "perfil", Tag = perfil, Style = (Style)Win.FindResource("Card") };
            var sp = new StackPanel();
            sp.Children.Add(new Path {
                Data = Geometry.Parse(Iconos[perfil]), Stroke = Pincel("#CDBB94"), StrokeThickness = 1.4,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 26) });
            sp.Children.Add(Texto(Titulos[perfil], 17, "#EDE8DF", true));
            var d = Texto(Descripciones[perfil], 13, "#8C877C"); d.Margin = new Thickness(0, 6, 0, 0); d.LineHeight = 19;
            sp.Children.Add(d);
            rb.Content = sp;
            rb.Checked += (o, e) => { perfilElegido = (string)rb.Tag; E<Button>("BtnContinuar").IsEnabled = hw != null; };
            return rb;
        }

        Border FilaInicio(ProgramaInicio p, int indice)
        {
            string motivo = p.Recomendacion ?? (p.Protegido ? "Necesario (antitrampas, controlador, seguridad o periférico). Mejor no quitarlo."
                                                            : "No es de los que suelen sobrar. Quítalo solo si sabes que no lo usas.");
            return FilaToggle(System.Text.RegularExpressions.Regex.Replace(p.Nombre, @"\.lnk$", ""), motivo, p, p.Recomendacion != null, indice);
        }

        Border FilaToggle(string titulo, string motivo, object tag, bool marcado, int indice)
        {
            var fondo = Pincel("#161614");
            var g = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 14, 20, 14), Margin = new Thickness(0, 0, 0, 8), Background = fondo };
            g.MouseEnter += (o, e) => fondo.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation((Color)ColorConverter.ConvertFromString("#1E1E1A"), TimeSpan.FromMilliseconds(220)));
            g.MouseLeave += (o, e) => fondo.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation((Color)ColorConverter.ConvertFromString("#161614"), TimeSpan.FromMilliseconds(380)));
            var cb = new CheckBox { Style = (Style)Win.FindResource("Toggle"), Tag = tag, IsChecked = marcado };
            var sp = new StackPanel();
            sp.Children.Add(Texto(titulo, 15));
            var sub = Texto(motivo, 12, "#8C877C"); sub.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(sub);
            cb.Content = sp;
            g.Child = cb;
            Entrada(g, 60 * indice, 18);
            return g;
        }

        Grid FilaPunto(string titulo, string detalle, string color, double tamTitulo = 15, Color? colorTitulo = null)
        {
            var fila = new Grid();
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            fila.ColumnDefinitions.Add(new ColumnDefinition());
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Fill = Pincel(color) };
            var sp = new StackPanel();
            Grid.SetColumn(sp, 1);
            var t = Texto(titulo, tamTitulo);
            if (colorTitulo.HasValue) t.Foreground = new SolidColorBrush(colorTitulo.Value);
            sp.Children.Add(t);
            if (!string.IsNullOrEmpty(detalle)) { var d = Texto(detalle, 12, "#8C877C"); d.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(d); }
            fila.Children.Add(dot); fila.Children.Add(sp);
            return fila;
        }

        void AgregarLog(Resultado r)
        {
            string color = r.Estado == "Aplicado" || r.Estado == "Se aplicaría" ? "#A9C29B" : r.Estado == "Error" ? "#D9786B" : r.Estado == "Ya estaba bien" ? "#CDBB94" : "#4A4842";
            string det = r.Estado == "Aplicado" || r.Estado == "Se aplicaría" ? r.Porque : r.Detalle;
            var fila = FilaPunto(r.Nombre, r.Estado.ToLower(Es) + (string.IsNullOrEmpty(det) ? "" : " · " + det), color);
            fila.Margin = new Thickness(0, 0, 0, 16);
            E<Panel>("Log").Children.Add(fila);
            Entrada(fila, 0, 16);
            E<ScrollViewer>("ScrollLog").ScrollToEnd();
        }

        // ============================================================ Anillo de progreso
        void PonerArco(double f)
        {
            f = Math.Max(0.0001, Math.Min(0.9999, f));
            double c = 100, r = 81.5, ang = f * 2 * Math.PI;
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(ang), c - r * Math.Cos(ang)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            E<Path>("Arco").Data = geo;
            E<TextBlock>("TxtPorcentaje").Text = Math.Round(f * 100).ToString();
        }

        // ============================================================ Pantallas
        async void AlCargar()
        {
            var root = E<FrameworkElement>("Root");
            root.Clip = new RectangleGeometry(new Rect(0, 0, Win.ActualWidth, Win.ActualHeight), 26, 26);
            Anim(root, UIElement.OpacityProperty, 1, 500);
            Anim((ScaleTransform)root.RenderTransform, ScaleTransform.ScaleXProperty, 1, 700, null, 0, "Back");
            Anim((ScaleTransform)root.RenderTransform, ScaleTransform.ScaleYProperty, 1, 700, null, 0, "Back");
            Flotar(E<UIElement>("Blob1"), -90, 70, 17);
            Flotar(E<UIElement>("Blob2"), 110, -60, 21);
            Flotar(E<UIElement>("Blob3"), -70, -50, 25);
            E<UIElement>("Pulso").BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            int idx = 0;
            foreach (UIElement el in E<Panel>("VistaInicio").Children) { Entrada(el, 120 + 90 * idx, 18); idx++; }
            PonerArco(0);
            timer.Start();

            try
            {
                Hardware h = null; List<ProgramaInicio> progs = null; List<Hallazgo> dg = null; List<AppPreinstalada> ap = null;
                await Task.Run(() => { h = Hardware.Detectar(); progs = Motor.ProgramasInicio().Where(p => p.Activo).ToList(); dg = Diagnostico.Revisar(h); ap = Apps.Detectar(); });
                hw = h; inicio = progs; diag = dg; apps = ap;
                MostrarHardware();
                ComprobarMedicionPendiente();
            }
            catch (Exception ex) { Error(ex); }
        }

        void MostrarHardware()
        {
            var chips = E<Panel>("Chips");
            chips.Children.Clear();
            string cpu = System.Text.RegularExpressions.Regex.Replace(hw.CPU ?? "", @"\s*\d+-Core Processor|\(R\)|\(TM\)", "").Trim();
            string gpu = System.Text.RegularExpressions.Regex.Replace((hw.GPUs ?? "").Split(new[] { " + " }, StringSplitOptions.None)[0], @"^NVIDIA\s|\(R\)|\(TM\)", "").Trim();
            var datos = new[] { new[] { "procesador", cpu }, new[] { "gráfica", gpu }, new[] { "memoria", hw.RAMGB + " GB" },
                                new[] { "disco", hw.Disco }, new[] { "equipo", hw.Portatil ? "portátil" : "escritorio" } };
            int i = 0;
            foreach (var d in datos) { var c = Chip(d[0], d[1]); chips.Children.Add(c); Entrada(c, 70 * i, 10); i++; }
            string sugerido = hw.Portatil ? "Laptop" : "Competitivo";
            foreach (RadioButton t in E<Panel>("Tarjetas").Children) if ((string)t.Tag == sugerido) t.IsChecked = true;
            E<Button>("BtnContinuar").IsEnabled = true;
        }

        void MostrarAjustes()
        {
            var lista = E<Panel>("ListaInicio");
            lista.Children.Clear();
            int i = 0;
            foreach (var p in inicio.OrderBy(p => p.Recomendacion == null).ThenBy(p => p.Nombre)) { lista.Children.Add(FilaInicio(p, i)); i++; }
            if (i == 0) lista.Children.Add(Texto("No hay programas que se abran solos al encender. Nada que limpiar aquí.", 14, "#8C877C"));
            var la = E<Panel>("ListaApps");
            la.Children.Clear();
            foreach (var a in apps) { la.Children.Add(FilaToggle(a.Nombre, a.Porque, a, a.Recomendada, i)); i++; }
            if (apps.Count == 0) la.Children.Add(Texto("Tu Windows no trae apps de las que sobran. Nada que quitar aquí.", 14, "#8C877C"));

            var ef = E<CheckBox>("ChkEfectos");
            if (hw.GamaBaja) { ef.IsChecked = true; ef.IsEnabled = false; E<TextBlock>("TxtEfectos").Text = "Recomendado en tu equipo: liberan recursos para el juego."; }
            else { ef.IsChecked = false; ef.IsEnabled = true; E<TextBlock>("TxtEfectos").Text = "Tu equipo va sobrado: casi no cuestan FPS. Opcional."; }

            bool sim = E<CheckBox>("ChkSimular").IsChecked == true;
            foreach (string n in new[] { "ChkLimpieza", "ChkPapelera", "ChkRam", "LineaPapelera", "LineaRam" })
                E<UIElement>(n).Visibility = sim ? Visibility.Collapsed : Visibility.Visible;
            E<CheckBox>("ChkRam").IsChecked = hw.RAMGB <= 16;
            E<TextBlock>("TxtRam").Text = hw.RAMGB <= 16
                ? "Recomendado con " + hw.RAMGB + " GB: vacía la caché de memoria para que el juego tenga más libre al empezar."
                : "Con " + hw.RAMGB + " GB casi no se nota. Windows vuelve a llenar la caché con el uso.";
            E<Button>("BtnOptimizar").Content = sim ? "Simular" : "Optimizar ahora";
            E<TextBlock>("TxtResumenPerfil").Text = "Perfil " + Titulos[perfilElegido].ToLower(Es) + ". " +
                (sim ? "Modo simulación: no se toca nada." : "Antes de cambiar nada creo un punto de restauración y guardo cada valor original.");

            var pd = E<Panel>("PanelDiag");
            pd.Children.Clear();
            int j = 0;
            foreach (var x in diag)
            {
                var f = FilaPunto(x.Titulo, x.Detalle, x.Gravedad == "Alta" ? "#D9786B" : x.Gravedad == "Media" ? "#E0B46C" : "#A9C29B", 13);
                f.Margin = new Thickness(0, 0, 0, 12);
                pd.Children.Add(f);
                Entrada(f, 200 + 80 * j, 10); j++;
            }
            MostrarVista(E<FrameworkElement>("VistaAjustes"));
        }

        async void Optimizar()
        {
            bool sim = E<CheckBox>("ChkSimular").IsChecked == true;
            var elegidos = E<Panel>("ListaInicio").Children.OfType<Border>().Select(b => b.Child as CheckBox)
                .Where(c => c != null && c.IsChecked == true).Select(c => (ProgramaInicio)c.Tag).ToList();
            var appsElegidas = E<Panel>("ListaApps").Children.OfType<Border>().Select(b => b.Child as CheckBox)
                .Where(c => c != null && c.IsChecked == true).Select(c => (AppPreinstalada)c.Tag).ToList();
            bool limpiar = E<CheckBox>("ChkLimpieza").IsChecked == true && !sim;
            bool papelera = E<CheckBox>("ChkPapelera").IsChecked == true && !sim;
            bool liberarRam = E<CheckBox>("ChkRam").IsChecked == true && !sim;
            var s = new Sesion { Simular = sim, Perfil = perfilElegido, Hw = hw, QuitarEfectos = E<CheckBox>("ChkEfectos").IsChecked == true };

            E<Panel>("Log").Children.Clear();
            E<Panel>("PanelMedicion").Children.Clear();
            var pf = E<FrameworkElement>("PanelFinal"); pf.Opacity = 0; pf.IsHitTestVisible = false;
            E<TextBlock>("TxtEstado").Text = sim ? "Simulando…" : "Optimizando…";
            Paso("Preparando");
            progresoMeta = progresoActual = hechos = 0; PonerArco(0);
            total = Catalogo.Ajustes.Count + elegidos.Count + appsElegidas.Count + 1;
            E<Button>("BtnHistorial").IsEnabled = false;
            MostrarVista(E<FrameworkElement>("VistaProgreso"));

            Action<Resultado> avance = r => Win.Dispatcher.BeginInvoke(new Action(() =>
            {
                hechos++;
                progresoMeta = Math.Min(0.98, hechos / Math.Max(1, total));
                if (r.Estado != "Omitido") AgregarLog(r);
            }));

            try
            {
                string sello = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string punto = "No aplica (simulación).", limpieza = null, reporte = null;
                Medicion despues = null;
                var res = new List<Resultado>();
                await Task.Run(() =>
                {
                    PasoAsync("Midiendo cómo está tu PC ahora");
                    s.MedicionAntes = Medicion.Tomar();
                    if (!sim)
                    {
                        s.ArchivoRespaldo = System.IO.Path.Combine(Motor.DirRespaldos, "respaldo_" + sello + ".json");
                        PasoAsync("Creando un punto de restauración de Windows");
                        punto = Motor.PuntoRestauracion();
                    }
                    PasoAsync("Aplicando ajustes");
                    res.AddRange(Motor.Optimizar(s, avance));
                    res.AddRange(Motor.DesactivarInicio(s, elegidos, avance));
                    if (appsElegidas.Count > 0) { PasoAsync("Quitando apps preinstaladas"); res.AddRange(Apps.Quitar(s, appsElegidas, avance)); }
                    Motor.GuardarRespaldo(s);
                    var extras = new List<string>();
                    if (limpiar || papelera) { PasoAsync("Borrando basura acumulada"); extras.Add(Limpieza.Limpiar(papelera)); }
                    if (liberarRam)
                    {
                        PasoAsync("Liberando memoria en caché");
                        try { extras.Add(Memoria.LiberarCache()); } catch (Exception ex) { extras.Add(ex.Message); }
                    }
                    if (extras.Count > 0) limpieza = string.Join(" ", extras);
                    reporte = System.IO.Path.Combine(Motor.DirReportes, (sim ? "simulacion_" : "reporte_") + sello + ".html");
                    Reporte.Generar(s, res, punto, limpieza, despues, diag, reporte);
                });
                MostrarFinal(s, res, reporte, despues);
            }
            catch (Exception ex) { Error(ex); }
        }

        /// <summary>
        /// Abre el reporte con el navegador predeterminado. Si Windows no tiene uno válido
        /// (por ejemplo, tras desinstalar el navegador que lo era), usa Edge y, si no, muestra el archivo.
        /// </summary>
        static void AbrirReporte(string ruta)
        {
            try { Process.Start(ruta); return; } catch (System.ComponentModel.Win32Exception) { }
            string edge = null;
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
                if (k != null) edge = k.GetValue(null) as string;
            if (edge != null && File.Exists(edge))
            {
                try { Process.Start(edge, "\"" + ruta + "\""); return; } catch { }
            }
            Process.Start("explorer.exe", "/select,\"" + ruta + "\"");
        }
        /// <summary>
        /// La comparación real se hace tras reiniciar: al abrir Savia, si la última optimización tiene
        /// medición de "antes" y el PC ya se reinició, mide sola (cuando los programas de inicio ya cargaron).
        /// </summary>
        async void ComprobarMedicionPendiente()
        {
            try
            {
                string arch = Directory.GetFiles(Motor.DirRespaldos, "respaldo_*.json").OrderByDescending(f => f).FirstOrDefault();
                if (arch == null) return;
                var d = Motor.LeerRespaldo(arch);
                if ((d.ContainsKey("Revertido") && d["Revertido"] != null) || !d.ContainsKey("MedicionAntes") || d["MedicionAntes"] == null) return;
                var antes = Medicion.DesdeDiccionario(d["MedicionAntes"]);
                var claves = new[] { "Procesos", "Servicios", "RAMUsadaGB", "Inicio" };
                var panel = E<FrameworkElement>("PanelTrasReinicio");
                var txt = E<TextBlock>("TxtTrasReinicio");
                DateTime arranque = Motor.UltimoArranque(), fechaOpt = DateTime.Parse(Convert.ToString(d["Fecha"]));

                if (arranque < fechaOpt)
                {
                    txt.Text = "Reinicia el PC para terminar tu última optimización. Al volver a abrir Savia verás cuánto mejoró.";
                    panel.Visibility = Visibility.Visible; Entrada(panel, 400, 10);
                    return;
                }
                bool yaMedido = d.ContainsKey("MedicionDespues") && d["MedicionDespues"] != null &&
                                d.ContainsKey("MedicionDespuesTrasReinicio") && Convert.ToBoolean(d["MedicionDespuesTrasReinicio"]);
                panel.Visibility = Visibility.Visible; Entrada(panel, 400, 10);
                if (yaMedido)
                {
                    txt.Text = "Desde tu última optimización (medido tras reiniciar):";
                    MostrarComparacion(E<Panel>("ChipsTrasReinicio"), antes, Medicion.DesdeDiccionario(d["MedicionDespues"]), null, claves);
                    return;
                }
                // Esperar a que terminen de cargar los programas de inicio (2 minutos tras encender).
                TimeSpan espera = TimeSpan.FromMinutes(2) - (DateTime.Now - arranque);
                txt.Text = espera > TimeSpan.Zero ? "Esperando a que Windows termine de arrancar para medir la mejora…" : "Midiendo cuánto mejoró tu PC…";
                Medicion m = null;
                await Task.Run(() => { if (espera > TimeSpan.Zero) System.Threading.Thread.Sleep(espera); m = Medicion.Tomar(); });
                d = Motor.LeerRespaldo(arch);
                d["MedicionDespues"] = m.ADiccionario();
                d["MedicionDespuesTrasReinicio"] = true;
                Motor.EscribirRespaldo(arch, d);
                txt.Text = "Desde tu última optimización (medido tras reiniciar):";
                MostrarComparacion(E<Panel>("ChipsTrasReinicio"), antes, m, null, claves);
            }
            catch (Exception ex) { Programa.Registrar(ex); }
        }

        void Paso(string t) { E<TextBlock>("TxtSubEstado").Text = t; }
        void PasoAsync(string t) { Win.Dispatcher.BeginInvoke(new Action(() => Paso(t))); }

        void MostrarFinal(Sesion s, List<Resultado> res, string reporte, Medicion despues)
        {
            int hechosN = res.Count(r => r.Estado == "Aplicado" || r.Estado == "Se aplicaría");
            int bien = res.Count(r => r.Estado == "Ya estaba bien");
            int err = res.Count(r => r.Estado == "Error");
            progresoMeta = 1.0;
            reporteFinal = reporte;
            E<TextBlock>("TxtEstado").Text = s.Simular ? "Esto es lo que haría." : hechosN == 0 ? "Ya estaba en forma." : "Listo. Más ligero.";
            var partes = new List<string> { hechosN + (s.Simular ? " cambios por aplicar" : " cambios aplicados"), bien + " ya estaban bien" };
            if (err > 0) partes.Add(err + " no se pudieron aplicar");
            string sub = string.Join(" · ", partes);
            if (!s.Simular) sub += ". Reinicia el PC y vuelve a abrir Savia: mediré solo cuánto mejoró.";
            E<UIElement>("BtnReiniciar").Visibility = s.Simular ? Visibility.Collapsed : Visibility.Visible;
            E<Button>("BtnReiniciar").Content = "Reiniciar ahora";
            Paso(sub);

            var pf = E<FrameworkElement>("PanelFinal");
            pf.IsHitTestVisible = true;
            Entrada(pf, 250, 12);
            E<Button>("BtnHistorial").IsEnabled = true;
        }

        void MostrarComparacion(Panel panel, Medicion antes, Medicion despues, string cuando, string[] claves)
        {
            panel.Children.Clear();
            if (antes == null || despues == null) return;
            int j = 0;
            foreach (string k in claves)
            {
                var e = Medicion.Etiquetas.First(x => x[0] == k);
                string nom = k == "Procesos" ? "procesos" : k == "Servicios" ? "servicios" : k == "RAMUsadaGB" ? "RAM" : "inicio";
                double? a = antes.Valor(k), d = despues.Valor(k);
                var chip = Chip(nom, string.Format(Es, "{0} → {1}{2}", a, d, e[2]), a.HasValue && d.HasValue && d < a);
                panel.Children.Add(chip);
                Entrada(chip, 120 + 80 * j, 10); j++;
            }
            if (cuando != null)
            {
                var nota = Texto("medido " + cuando, 11, "#6A675F");
                nota.VerticalAlignment = VerticalAlignment.Center; nota.Margin = new Thickness(4, 0, 0, 10);
                panel.Children.Add(nota);
            }
        }

        // ============================================================ Historial
        void MostrarHistorial()
        {
            var lista = E<Panel>("ListaHistorial");
            lista.Children.Clear();
            var archivos = Directory.GetFiles(Motor.DirRespaldos, "respaldo_*.json").OrderByDescending(f => f).ToList();
            if (archivos.Count == 0) lista.Children.Add(Texto("Todavía no has optimizado este equipo.", 14, "#8C877C"));
            int i = 0;
            foreach (string archivo in archivos)
            {
                Dictionary<string, object> d;
                try { d = Motor.LeerRespaldo(archivo); } catch { continue; }
                bool revertido = d.ContainsKey("Revertido") && d["Revertido"] != null;
                int n = d.ContainsKey("Cambios") && d["Cambios"] != null ? ((ICollection)d["Cambios"]).Count : 0;
                string perfil = Convert.ToString(d["Perfil"]);

                var b = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(22, 16, 22, 16), Margin = new Thickness(0, 0, 0, 10), Background = Pincel("#1A1A17") };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(Texto(DateTime.Parse(Convert.ToString(d["Fecha"])).ToString("d 'de' MMMM, HH:mm", Es), 16));
                var s2 = Texto(string.Format("perfil {0} · {1} cambios{2}", (Titulos.ContainsKey(perfil) ? Titulos[perfil] : perfil).ToLower(Es), n, revertido ? " · deshecho" : ""), 12, "#8C877C");
                s2.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(s2);
                var panelMed = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                sp.Children.Add(panelMed);

                var botones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
                Grid.SetColumn(botones, 1);
                var antes = d.ContainsKey("MedicionAntes") ? Medicion.DesdeDiccionario(d["MedicionAntes"]) : null;
                var despues = d.ContainsKey("MedicionDespues") ? Medicion.DesdeDiccionario(d["MedicionDespues"]) : null;
                var claves = new[] { "Procesos", "Servicios", "RAMUsadaGB", "Inicio" };
                if (antes != null && !revertido)
                {
                    if (despues != null) MostrarComparacion(panelMed, antes, despues, DateTime.Parse(despues.Fecha).ToString("d 'de' MMMM, HH:mm", Es), claves);
                    var bm = new Button { Style = (Style)Win.FindResource("Ghost"), Content = despues != null ? "medir otra vez" : "medir ahora",
                        ToolTip = "Compara tu PC ahora con cómo estaba antes de optimizar. Lo ideal es hacerlo después de reiniciar." };
                    string arch = archivo;
                    bm.Click += async (o, e) =>
                    {
                        bm.Content = "midiendo…"; bm.IsEnabled = false;
                        try
                        {
                            Medicion m = null;
                            await Task.Run(() => m = Medicion.Tomar());
                            MostrarComparacion(panelMed, antes, m, "ahora mismo", claves);
                            // Se guarda en el respaldo para verla la próxima vez que abras el historial.
                            var json = Motor.LeerRespaldo(arch);
                            json["MedicionDespues"] = m.ADiccionario();
                            json["MedicionDespuesTrasReinicio"] = Motor.UltimoArranque() > DateTime.Parse(Convert.ToString(json["Fecha"]));
                            Motor.EscribirRespaldo(arch, json);
                        }
                        catch (Exception ex) { Error(ex); }
                        bm.Content = "medir otra vez"; bm.IsEnabled = true;
                    };
                    botones.Children.Add(bm);
                }
                var btn = new Button { Style = (Style)Win.FindResource("Ghost"), Content = revertido ? "deshecho" : "deshacer", IsEnabled = !revertido && n > 0, Margin = new Thickness(6, 0, 0, 0) };
                string archivoR = archivo;
                btn.Click += async (o, e) =>
                {
                    // Primer clic pide confirmación en el propio botón; el segundo deshace.
                    if ((string)btn.Content != "¿seguro? pulsa otra vez") { btn.Content = "¿seguro? pulsa otra vez"; return; }
                    btn.Content = "deshaciendo…"; btn.IsEnabled = false;
                    try
                    {
                        int ok = 0; string[] fallos = null;
                        await Task.Run(() => fallos = Motor.Revertir(archivoR, out ok));
                        string txt = "Se restauraron " + ok + " valores originales. Reinicia el PC para terminar.";
                        if (fallos.Length > 0) txt += "\n\nNo se pudieron restaurar:\n" + string.Join("\n", fallos);
                        MessageBox.Show(Win, txt, "Savia");
                    }
                    catch (Exception ex) { Error(ex); }
                    MostrarHistorial();
                };
                botones.Children.Add(btn);
                g.Children.Add(sp); g.Children.Add(botones);
                b.Child = g;
                lista.Children.Add(b);
                Entrada(b, 60 * i, 14); i++;
            }
            MostrarVista(E<FrameworkElement>("VistaHistorial"));
        }

        void Error(Exception ex)
        {
            Programa.Registrar(ex);
            MessageBox.Show(Win, "Algo salió mal: " + ex.Message, "Savia");
            E<Button>("BtnHistorial").IsEnabled = true;
            MostrarVista(E<FrameworkElement>("VistaInicio"));
        }
    }
}
