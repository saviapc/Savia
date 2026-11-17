// Savia - Apps preinstaladas que sobran (bloatware).
// Solo se muestran las de esta lista: nunca se tocan la Tienda, Xbox/Gaming, Fotos, Calculadora,
// Bloc de notas, códecs ni componentes del sistema. Se quitan solo para el usuario actual,
// así Windows conserva su copia y se pueden recuperar desde el historial.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Savia
{
    public class AppPreinstalada
    {
        public string Nombre, Porque, Paquete, Familia, Id;
        public bool Recomendada;
    }

    public static class Apps
    {
        // Patrón del nombre del paquete | nombre visible | por qué sobra | ¿se recomienda quitarla?
        static readonly object[][] Catalogo = {
            new object[] { @"^Microsoft\.BingNews$", "Noticias", "Descarga titulares en segundo plano aunque no la abras.", true },
            new object[] { @"^Microsoft\.BingWeather$", "El Tiempo", "Se actualiza sola en segundo plano; el tiempo también sale en el buscador.", true },
            new object[] { @"^Microsoft\.BingSearch$", "Búsqueda de Bing", "Complemento de Bing para la búsqueda de Windows; no hace falta.", true },
            new object[] { @"^Microsoft\.GetHelp$", "Obtener ayuda", "Asistente de soporte de Microsoft que casi nadie usa.", true },
            new object[] { @"^Microsoft\.Getstarted$", "Consejos", "Tutoriales de Windows que muestran avisos de vez en cuando.", true },
            new object[] { @"^Microsoft\.MicrosoftSolitaireCollection$", "Solitario", "Juego preinstalado con anuncios.", true },
            new object[] { @"^Microsoft\.MicrosoftSudoku$", "Sudoku", "Juego preinstalado con anuncios.", true },
            new object[] { @"^Microsoft\.MicrosoftOfficeHub$", "Microsoft 365 (Office)", "Es solo un acceso que invita a suscribirse a Office; tus programas de Office no se tocan.", true },
            new object[] { @"^Microsoft\.People$", "Contactos", "App de contactos antigua que ya no se usa.", true },
            new object[] { @"^Microsoft\.WindowsFeedbackHub$", "Centro de opiniones", "Sirve para enviar comentarios a Microsoft.", true },
            new object[] { @"^Microsoft\.WindowsMaps$", "Mapas", "Mapas sin conexión; se usa más el navegador.", true },
            new object[] { @"^Microsoft\.ZuneVideo$", "Películas y TV", "Tienda de películas de Microsoft que ya no vende contenido.", true },
            new object[] { @"^Microsoft\.549981C3F5F10$", "Cortana", "Asistente retirado por Microsoft.", true },
            new object[] { @"^Microsoft\.SkypeApp$", "Skype", "Microsoft cerró Skype en 2025.", true },
            new object[] { @"^Microsoft\.MixedReality\.Portal$", "Portal de realidad mixta", "Solo sirve con gafas de realidad mixta de Windows.", true },
            new object[] { @"^Microsoft\.Microsoft3DViewer$", "Visor 3D", "Visor de modelos 3D que casi nadie usa.", true },
            new object[] { @"^Microsoft\.PowerAutomateDesktop$", "Power Automate", "Herramienta de automatización para empresas.", true },
            new object[] { @"^Microsoft\.Windows\.DevHome$", "Dev Home", "Panel para programadores, retirado por Microsoft.", true },
            new object[] { @"^Clipchamp\.Clipchamp$", "Clipchamp", "Editor de vídeo preinstalado; muchas funciones son de pago.", true },
            new object[] { @"^king\.com\.", "Juegos de King (Candy Crush…)", "Juegos promocionales que Windows instala solo.", true },
            new object[] { @"^Microsoft\.Todos$", "Microsoft To Do", "Lista de tareas. Quítala solo si no la usas.", false },
            new object[] { @"^Microsoft\.OutlookForWindows$", "Outlook (nuevo)", "Correo de Microsoft. Quítalo solo si no lo usas.", false },
            new object[] { @"^microsoft\.windowscommunicationsapps$", "Correo y Calendario (antiguos)", "Reemplazados por el nuevo Outlook.", false },
            new object[] { @"^(MSTeams|MicrosoftTeams)$", "Microsoft Teams", "Videollamadas. Quítalo solo si no lo usas para clases o trabajo.", false },
            new object[] { @"^Microsoft\.ZuneMusic$", "Reproductor multimedia", "Reproductor de música y vídeo de Windows. Quítalo solo si usas otro.", false },
            new object[] { @"^Microsoft\.YourPhone$", "Enlace Móvil", "Conecta tu móvil con el PC. Quítalo solo si no lo usas.", false },
            new object[] { @"^Microsoft\.Copilot$", "Copilot", "Asistente de IA de Microsoft. Quítalo solo si no lo usas.", false },
            new object[] { @"^Disney\.", "Disney+", "Acceso directo promocional. Quítalo si no tienes Disney+.", false },
            new object[] { @"^Facebook\.", "Facebook / Instagram", "App promocional. Quítala si usas la web o el móvil.", false },
            new object[] { @"TikTok", "TikTok", "App promocional. Quítala si no la usas en el PC.", false },
            new object[] { @"^AmazonVideo\.PrimeVideo$", "Prime Video", "Acceso promocional. Quítalo si no tienes Prime.", false },
            new object[] { @"^SpotifyAB\.SpotifyMusic$", "Spotify", "Quítalo solo si no escuchas música en el PC.", false },
        };

        /// <summary>Ejecuta un comando de AppX. Devuelve la salida estándar; los errores van aparte.</summary>
        static string Ps(string comando, out int codigo, out string error)
        {
            // El comando se pasa por la entrada estándar, en texto plano (sin codificar), para que sea transparente.
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command -") {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8 };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                p.StandardInput.WriteLine("$ProgressPreference='SilentlyContinue'; " + comando);
                p.StandardInput.Close();
                var tErr = p.StandardError.ReadToEndAsync();
                string salida = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                error = Regex.Replace(tErr.Result, "<[^>]+>|#< CLIXML", " ").Trim();
                codigo = p.ExitCode;
                return salida;
            }
        }

        static string Ps(string comando, out int codigo) { string e; string o = Ps(comando, out codigo, out e); return o + " " + e; }

        /// <summary>Apps de la lista que están instaladas para este usuario.</summary>
        public static List<AppPreinstalada> Detectar()
        {
            var res = new List<AppPreinstalada>();
            int code;
            string err;
            string json = Ps("[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | " +
                             "Select-Object Name, PackageFullName, PackageFamilyName | ConvertTo-Json -Compress", out code, out err);
            if (code != 0 || string.IsNullOrWhiteSpace(json)) return res;
            object datos = Motor.Json.DeserializeObject(json.Trim());
            IEnumerable lista = datos as object[] ?? (IEnumerable)new[] { datos };
            foreach (Dictionary<string, object> p in lista.OfType<Dictionary<string, object>>())
            {
                string nombre = Convert.ToString(p["Name"]);
                var c = Catalogo.FirstOrDefault(x => Regex.IsMatch(nombre, (string)x[0], RegexOptions.IgnoreCase));
                if (c == null) continue;
                res.Add(new AppPreinstalada {
                    Id = nombre, Nombre = (string)c[1], Porque = (string)c[2], Recomendada = (bool)c[3],
                    Paquete = Convert.ToString(p["PackageFullName"]), Familia = Convert.ToString(p["PackageFamilyName"]) });
            }
            return res.OrderBy(a => !a.Recomendada).ThenBy(a => a.Nombre).ToList();
        }

        public static List<Resultado> Quitar(Sesion s, IEnumerable<AppPreinstalada> apps, Action<Resultado> avance)
        {
            var res = new List<Resultado>();
            foreach (var a in apps)
            {
                s.ItemActual = "app:" + a.Id;
                var r = new Resultado { Id = s.ItemActual, Nombre = a.Nombre, Categoria = "Apps preinstaladas", Riesgo = "Bajo", Porque = a.Porque,
                                        Detalle = "Se quita solo para tu usuario; se puede recuperar desde el historial." };
                try
                {
                    if (!s.Simular)
                    {
                        int code;
                        string o = Ps("Remove-AppxPackage -Package '" + a.Paquete.Replace("'", "''") + "' -ErrorAction Stop", out code);
                        if (code != 0) throw new Exception(Primera(o));
                    }
                    s.Anotar(new Dictionary<string, object> { { "Tipo", "App" }, { "Nombre", a.Nombre }, { "Paquete", a.Paquete }, { "Familia", a.Familia } });
                    r.Estado = s.Simular ? "Se aplicaría" : "Aplicado";
                }
                catch (Exception e) { r.Estado = "Error"; r.Detalle = e.Message; }
                Motor.GuardarRespaldo(s);
                res.Add(r);
                if (avance != null) avance(r);
            }
            return res;
        }

        /// <summary>
        /// Vuelve a instalar la app con la copia que Windows conserva. Si Windows no la conserva
        /// (apps que se instalaron desde la Tienda), abre su página en la Tienda para reinstalarla.
        /// </summary>
        public static void Recuperar(string familia)
        {
            int code;
            string o = Ps("Add-AppxPackage -RegisterByFamilyName -MainPackage '" + familia.Replace("'", "''") + "' -ErrorAction Stop", out code);
            if (code == 0) return;
            try { System.Diagnostics.Process.Start("ms-windows-store://pdp/?PFN=" + Uri.EscapeDataString(familia)); } catch { }
            throw new Exception("Windows no guardaba una copia; se abrió su página en la Microsoft Store para reinstalarla.");
        }

        static string Primera(string salida)
        {
            string l = (salida ?? "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
            return string.IsNullOrEmpty(l) ? "No se pudo quitar." : l;
        }
    }
}
