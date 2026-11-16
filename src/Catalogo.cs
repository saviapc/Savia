// Savia - Catálogo de ajustes. Cada uno explica en lenguaje sencillo POR QUÉ se hace.
// Nada aquí desactiva Windows Defender, Windows Update, el firewall ni los antitrampas.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Savia
{
    public class Ajuste
    {
        public string Id, Categoria, Riesgo = "Bajo", Nombre, Porque, SiNoAplica;
        public string[] Perfiles = { "Todos" };
        public Func<Sesion, bool> Condicion;
        public Action<Sesion> Aplicar;
        public bool Reinicio;
    }

    public static class Catalogo
    {
        public static readonly string[] Perfiles = { "Competitivo", "Calidad", "Laptop", "Streaming" };

        const string Perfil = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        static void R(Sesion s, string ruta, string nombre, object valor, RegistryValueKind t = RegistryValueKind.DWord) { Motor.SetRegistro(s, ruta, nombre, valor, t); }

        public static readonly List<Ajuste> Ajustes = new List<Ajuste>
        {
            // ===================================================== RENDIMIENTO
            new Ajuste { Id = "plan-energia", Categoria = "Rendimiento", Nombre = "Plan de energía adecuado",
                Porque = "En PC de escritorio (o portátil en perfiles de máximo rendimiento) se usa \"Alto rendimiento\" para que el procesador no baje su velocidad en mitad de la partida. En el perfil Portátil se mantiene \"Equilibrado\" para cuidar batería y temperatura.",
                Aplicar = s => Motor.SetPlanEnergia(s, s.Perfil == "Laptop" ? Motor.PlanEquilibrado : Motor.PlanAlto) },

            new Ajuste { Id = "modo-juego", Categoria = "Rendimiento", Nombre = "Activar el Modo Juego de Windows",
                Porque = "Hace que Windows dé prioridad al juego y no instale actualizaciones ni muestre avisos mientras juegas.",
                Aplicar = s => R(s, @"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1) },

            new Ajuste { Id = "pantalla-hz", Categoria = "Rendimiento", Nombre = "Usar la frecuencia máxima del monitor",
                Porque = "Muchos monitores de 144 Hz o más vienen puestos a 60 Hz de fábrica: ves solo 60 imágenes por segundo aunque el juego genere más. Se pone la frecuencia más alta que admite tu pantalla principal, con la misma resolución (Windows la prueba antes de aplicarla).",
                Condicion = s => { var f = Pantalla.Frecuencias(); return f != null && f[1] > f[0]; },
                SiNoAplica = "Tu pantalla ya usa su frecuencia máxima.",
                Aplicar = s => Pantalla.Aplicar(s, Pantalla.Frecuencias()[1]) },

            new Ajuste { Id = "hags", Categoria = "Rendimiento", Nombre = "Programación de GPU acelerada por hardware (HAGS)",
                Perfiles = new[] { "Competitivo", "Calidad", "Streaming" }, Reinicio = true,
                Porque = "La tarjeta gráfica organiza su propio trabajo en lugar de hacerlo el procesador: reduce algo de latencia y libera CPU. Solo se activa en tarjetas dedicadas modernas.",
                Condicion = s => s.Hw.GPUDedicada && s.Hw.Build >= 19041, SiNoAplica = "Tu equipo no tiene tarjeta gráfica dedicada compatible.",
                Aplicar = s => R(s, @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) },

            new Ajuste { Id = "prioridad-juegos", Categoria = "Rendimiento", Nombre = "Más prioridad de CPU y GPU para los juegos",
                Porque = "Windows reserva por defecto el 20 % del procesador para tareas de fondo. Se baja al 10 % y se marca a los juegos con prioridad alta de CPU y GPU.",
                Aplicar = s => {
                    R(s, Perfil, "SystemResponsiveness", 10);
                    R(s, Perfil + @"\Tasks\Games", "GPU Priority", 8);
                    R(s, Perfil + @"\Tasks\Games", "Priority", 6);
                    R(s, Perfil + @"\Tasks\Games", "Scheduling Category", "High", RegistryValueKind.String);
                    R(s, Perfil + @"\Tasks\Games", "SFIO Priority", "High", RegistryValueKind.String); } },

            new Ajuste { Id = "efectos-visuales", Categoria = "Rendimiento", Nombre = "Quitar animaciones y transparencias de Windows",
                Porque = "En equipos con poca RAM o sin tarjeta gráfica dedicada, las animaciones y transparencias del escritorio consumen recursos que el juego necesita.",
                Condicion = s => s.Hw.GamaBaja || s.QuitarEfectos, SiNoAplica = "Tu equipo tiene recursos de sobra y elegiste conservar los efectos visuales.",
                Aplicar = s => {
                    R(s, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
                    R(s, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0); } },

            new Ajuste { Id = "raton", Categoria = "Rendimiento", Nombre = "Quitar \"Mejorar la precisión del puntero\" (aceleración del ratón)",
                Porque = "Con la aceleración, el mismo movimiento de mano mueve la mira distinto según la velocidad. Sin ella, la puntería es constante (memoria muscular), sobre todo en juegos antiguos y en los menús. Se nota al cerrar sesión o reiniciar.",
                Aplicar = s => {
                    R(s, @"HKCU\Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String);
                    R(s, @"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String);
                    R(s, @"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String); } },

            // ===================================================== RED
            new Ajuste { Id = "red-throttling", Categoria = "Red", Nombre = "Quitar el límite de red multimedia", Perfiles = new[] { "Competitivo", "Streaming" },
                Porque = "Windows limita el tráfico de red mientras se reproduce audio o vídeo; quitar ese límite evita pequeños picos de ping.",
                Aplicar = s => R(s, Perfil, "NetworkThrottlingIndex", unchecked((int)0xffffffff)) },

            new Ajuste { Id = "red-nagle", Categoria = "Red", Nombre = "Enviar los paquetes de red sin esperar (Nagle)", Perfiles = new[] { "Competitivo" },
                Porque = "Windows agrupa paquetes pequeños para ahorrar ancho de banda, lo que añade espera. En juegos que usan TCP esto reduce la latencia.",
                Aplicar = s => {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
                             (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)))
                    {
                        string p = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + ni.Id;
                        using (var k = Motor.Abrir(p, false)) if (k == null) continue;
                        R(s, p, "TcpAckFrequency", 1);
                        R(s, p, "TCPNoDelay", 1);
                    } } },

            // ===================================================== SEGUNDO PLANO
            new Ajuste { Id = "grabacion-fondo", Categoria = "Segundo plano", Nombre = "Desactivar la grabación automática de Xbox Game Bar", Perfiles = new[] { "Competitivo", "Calidad", "Laptop" },
                Porque = "Game Bar puede estar grabando el juego en segundo plano todo el tiempo \"por si acaso\", lo que gasta CPU, GPU y disco. Game Bar sigue disponible con Win+G.",
                Aplicar = s => {
                    R(s, @"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0);
                    R(s, @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0); } },

            new Ajuste { Id = "apps-fondo", Categoria = "Segundo plano", Riesgo = "Medio", Nombre = "Impedir que las apps de la Tienda corran en segundo plano", Perfiles = new[] { "Competitivo", "Laptop" },
                Porque = "Apps como Noticias, Tiempo, Fotos o Xbox se quedan funcionando aunque no las uses. Ojo: las apps de la Tienda (por ejemplo WhatsApp de la Tienda) dejarán de avisar si están cerradas.",
                Aplicar = s => {
                    R(s, @"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
                    R(s, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0); } },

            new Ajuste { Id = "edge-fondo", Categoria = "Segundo plano", Nombre = "Que Microsoft Edge no corra con el navegador cerrado",
                Porque = "Edge se precarga al iniciar Windows y se queda abierto en segundo plano aunque no lo uses. Seguirá funcionando normal al abrirlo.",
                Aplicar = s => {
                    R(s, @"HKLM\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
                    R(s, @"HKLM\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0); } },

            new Ajuste { Id = "widgets", Categoria = "Segundo plano", Nombre = "Desactivar los Widgets (noticias y tiempo)", Perfiles = new[] { "Competitivo", "Laptop" },
                Porque = "El panel de Widgets mantiene procesos descargando noticias todo el tiempo.",
                Aplicar = s => {
                    try { R(s, @"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0); }
                    catch (Exception e) {
                        if (e is UnauthorizedAccessException || e is System.Security.SecurityException || e is System.IO.IOException)
                            throw new Exception("Esta versión de Windows protege la opción y no deja cambiarla desde programas. Puedes quitarlos a mano: Configuración > Personalización > Barra de tareas > Widgets.");
                        throw;
                    } } },

            new Ajuste { Id = "sugerencias", Categoria = "Segundo plano", Nombre = "Quitar sugerencias, anuncios e instalación de apps sin permiso",
                Porque = "Windows descarga en segundo plano \"apps sugeridas\" (juegos de prueba, anuncios en Inicio) sin preguntarte.",
                Aplicar = s => {
                    foreach (string n in new[] { "SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled",
                                                 "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-353698Enabled" })
                        R(s, @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", n, 0); } },

            // ----- Servicios que no aportan nada al usuario
            new Ajuste { Id = "svc-telemetria", Categoria = "Servicios", Nombre = "Telemetría de Microsoft (DiagTrack y dmwappushservice)",
                Porque = "Envían datos de uso a Microsoft de forma continua. No aportan nada al usuario y consumen CPU, disco y red.",
                Condicion = s => Motor.ExisteServicio("DiagTrack"), SiNoAplica = "El servicio no existe en este Windows.",
                Aplicar = s => { Motor.SetServicio(s, "DiagTrack", "disabled"); Motor.SetServicio(s, "dmwappushservice", "disabled"); } },

            new Ajuste { Id = "svc-mapas", Categoria = "Servicios", Nombre = "Administrador de mapas descargados (MapsBroker)",
                Porque = "Solo sirve para la app Mapas sin conexión. Pasa a \"manual\": arranca solo si algún día abres Mapas.",
                Condicion = s => Motor.ExisteServicio("MapsBroker"), SiNoAplica = "El servicio no existe en este Windows.",
                Aplicar = s => Motor.SetServicio(s, "MapsBroker", "demand") },

            new Ajuste { Id = "svc-inutiles", Categoria = "Servicios", Nombre = "Modo demostración de tienda, Fax y Registro remoto",
                Porque = "RetailDemo es para los PC de exhibición en tiendas, Fax casi nadie lo usa y Registro remoto permite modificar tu PC por red (mejor apagado también por seguridad).",
                Aplicar = s => { foreach (string sv in new[] { "RetailDemo", "Fax", "RemoteRegistry" }) Motor.SetServicio(s, sv, "disabled"); } },

            new Ajuste { Id = "svc-enlaces", Categoria = "Servicios", Nombre = "Cliente de seguimiento de vínculos distribuidos (TrkWks)",
                Porque = "Vigila accesos directos a archivos en redes de empresa. En casa no se usa. Pasa a \"manual\".",
                Aplicar = s => Motor.SetServicio(s, "TrkWks", "demand") },

            new Ajuste { Id = "svc-edgeupdate", Categoria = "Servicios", Nombre = "Actualizador de Edge siempre encendido",
                Porque = "Pasa a \"manual\": Edge se sigue actualizando cuando lo abres, pero el servicio no queda vivo todo el tiempo.",
                Aplicar = s => { Motor.SetServicio(s, "edgeupdate", "demand"); Motor.SetServicio(s, "edgeupdatem", "demand"); } },

            new Ajuste { Id = "svc-sysmain", Categoria = "Servicios", Riesgo = "Medio", Nombre = "SysMain (Superfetch) en disco mecánico",
                Porque = "En discos duros mecánicos (HDD) suele dejar el disco al 100 % y provocar tirones. En SSD no molesta y se deja como está.",
                Condicion = s => s.Hw.Disco == "HDD", SiNoAplica = "Tu Windows está en un SSD; SysMain no causa problemas ahí.",
                Aplicar = s => Motor.SetServicio(s, "SysMain", "demand") },

            // ----- Tareas programadas de recopilación de datos
            new Ajuste { Id = "tareas-telemetria", Categoria = "Tareas programadas", Nombre = "Tareas de recopilación de datos y \"experiencia del cliente\"",
                Porque = "Se ejecutan solas (a veces en mitad de una partida) para analizar programas y enviar estadísticas a Microsoft. Pueden causar picos de disco y CPU.",
                Aplicar = s => {
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Application Experience\", "Microsoft Compatibility Appraiser");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Application Experience\", "ProgramDataUpdater");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Customer Experience Improvement Program\", "Consolidator");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Customer Experience Improvement Program\", "UsbCeip");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\DiskDiagnostic\", "Microsoft-Windows-DiskDiagnosticDataCollector");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Feedback\Siuf\", "DmClient");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Feedback\Siuf\", "DmClientOnScenarioDownload");
                    Motor.DesactivarTarea(s, @"\Microsoft\Windows\Autochk\", "Proxy"); } },
        };

        // ===================================================== PROGRAMAS DE INICIO
        // Nunca se recomiendan quitar: seguridad, controladores, antitrampas, periféricos y VPN.
        static readonly Regex Protegido = new Regex(@"\b(?:SecurityHealth|Defender|Realtek|RtkAud|NVIDIA|nvcpl|AMD|Radeon|Intel|igfx|Synaptics|ELAN|Logitech|LGHUB|Razer|Corsair|iCUE|SteelSeries|HyperX|NGENUITY|Vanguard|vgtray|Riot|EasyAntiCheat|BattlEye|FACEIT|ESEA|Bitdefender|Kaspersky|Avast|AVG|ESET|Norton|McAfee|Malwarebytes|NordVPN|ProtonVPN|Wacom|Bluetooth|Dolby|Nahimic|Sonic|Armoury|MSI|Gigabyte|ASUS|Lenovo|Dell|HP)", RegexOptions.IgnoreCase);

        static readonly string[][] Prescindibles = {
            new[] { "Spotify", "Spotify se abre solo al encender. Puedes abrirlo cuando quieras escuchar música." },
            new[] { "Discord", "Discord arranca solo y consume RAM; se abre en segundos cuando lo necesites." },
            new[] { "Steam", "Steam arranca solo y descarga actualizaciones en segundo plano. Se abre igual al lanzar un juego." },
            new[] { "EpicGames", "El lanzador de Epic arranca solo y queda en segundo plano." },
            new[] { "EADesktop|Origin", "La app de EA arranca sola y queda en segundo plano." },
            new[] { "Ubisoft|Uplay", "Ubisoft Connect arranca solo y queda en segundo plano." },
            new[] { @"Battle\.net", "Battle.net arranca solo y queda en segundo plano." },
            new[] { "GalaxyClient", "GOG Galaxy arranca solo y queda en segundo plano." },
            new[] { "Teams|Skype|Zoom", "Programa de videollamadas que arranca solo aunque no tengas reunión." },
            new[] { "Opera|Brave|Chrome|Edge", "El navegador se precarga al encender el PC para \"abrir más rápido\", gastando RAM." },
            new[] { "Adobe|CCXProcess|AcroTray", "Ayudantes y actualizadores de Adobe que no se necesitan al encender." },
            new[] { "CCleaner", "Monitor de CCleaner en segundo plano: no hace falta." },
            new[] { "iTunesHelper", "Ayudante de iTunes: solo sirve si conectas un iPhone por cable." },
            new[] { "Dropbox|GoogleDrive|MEGAsync", "Sincronizador de nube. Si no necesitas tus archivos sincronizados al instante, puede abrirse a mano." },
            new[] { "uTorrent|BitTorrent|qBittorrent", "Cliente torrent que arranca solo y usa red y disco en segundo plano." },
            new[] { "Update|Updater", "Actualizador de un programa: el programa se actualiza igual cuando lo abres." } };

        public static bool EsInicioProtegido(ProgramaInicio p) { return Protegido.IsMatch(p.Nombre + " " + p.Comando); }

        public static string RecomendacionInicio(ProgramaInicio p)
        {
            string t = p.Nombre + " " + p.Comando;
            foreach (var x in Prescindibles) if (Regex.IsMatch(t, x[0], RegexOptions.IgnoreCase)) return x[1];
            return null;
        }
    }
}
