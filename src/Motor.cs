// Savia - Motor: cada cambio guarda el valor anterior ANTES de tocar nada, para poder deshacerlo.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Savia
{
    public class Resultado
    {
        public string Id, Nombre, Categoria, Porque, Riesgo, Estado, Detalle;
    }

    public class ProgramaInicio
    {
        public string Nombre, Comando, Origen, Aprob, Recomendacion;
        public bool Activo, Protegido;
    }

    /// <summary>Estado de una optimización (o simulación) en curso.</summary>
    public class Sesion
    {
        public List<Dictionary<string, object>> Cambios = new List<Dictionary<string, object>>();
        public bool Simular;
        public string ItemActual;
        public bool RequiereReinicio;
        public string ArchivoRespaldo;
        public string Perfil;
        public bool QuitarEfectos;
        public Hardware Hw;
        public Medicion MedicionAntes;

        public void Anotar(Dictionary<string, object> c)
        {
            c["Item"] = ItemActual;
            c["Fecha"] = DateTime.Now.ToString("s");
            Cambios.Add(c);
        }
    }

    public static class Motor
    {
        public static readonly string DirDatos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Savia");
        public static string DirRespaldos { get { return Path.Combine(DirDatos, "Respaldos"); } }
        public static string DirReportes { get { return Path.Combine(DirDatos, "Reportes"); } }

        // ------------------------------------------------------------ Registro
        static RegistryKey Raiz(string ruta, out string sub)
        {
            int i = ruta.IndexOf('\\');
            string hive = ruta.Substring(0, i).ToUpperInvariant();
            sub = ruta.Substring(i + 1);
            RegistryHive h = hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            return RegistryKey.OpenBaseKey(h, RegistryView.Registry64);
        }

        public static RegistryKey Abrir(string ruta, bool escritura)
        {
            string sub;
            using (RegistryKey r = Raiz(ruta, out sub)) return r.OpenSubKey(sub, escritura);
        }

        public static bool LeerValor(string ruta, string nombre, out object valor, out RegistryValueKind tipo)
        {
            valor = null; tipo = RegistryValueKind.Unknown;
            using (RegistryKey k = Abrir(ruta, false))
            {
                if (k == null || !k.GetValueNames().Contains(nombre, StringComparer.OrdinalIgnoreCase)) return false;
                valor = k.GetValue(nombre, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                tipo = k.GetValueKind(nombre);
                return true;
            }
        }

        static bool Iguales(object a, object b)
        {
            IEnumerable ea = a as IEnumerable, eb = b as IEnumerable;
            if (ea != null && !(a is string) && eb != null && !(b is string))
                return string.Join(",", ea.Cast<object>()) == string.Join(",", eb.Cast<object>());
            return Convert.ToString(a) == Convert.ToString(b);
        }

        public static void SetRegistro(Sesion s, string ruta, string nombre, object valor, RegistryValueKind tipo = RegistryValueKind.DWord)
        {
            object ant; RegistryValueKind tipoAnt;
            bool existia = LeerValor(ruta, nombre, out ant, out tipoAnt);
            if (existia && Iguales(ant, valor)) return;

            bool claveCreada;
            using (RegistryKey k = Abrir(ruta, false)) claveCreada = k == null;
            if (!s.Simular)
            {
                string sub;
                using (RegistryKey r = Raiz(ruta, out sub))
                using (RegistryKey k = r.CreateSubKey(sub, true))
                {
                    try { k.SetValue(nombre, valor, tipo); }
                    catch
                    {
                        if (claveCreada) { try { r.DeleteSubKey(sub, false); } catch { } }
                        throw;
                    }
                }
            }
            s.Anotar(new Dictionary<string, object> {
                { "Tipo", "Registro" }, { "Ruta", ruta }, { "Nombre", nombre }, { "ClaveCreada", claveCreada },
                { "Existia", existia }, { "ValorAnterior", ant }, { "TipoAnterior", existia ? tipoAnt.ToString() : null },
                { "ValorNuevo", valor }, { "TipoNuevo", tipo.ToString() } });
        }

        // ------------------------------------------------------------ Servicios
        public static string InicioServicio(string nombre)
        {
            using (RegistryKey k = Abrir(@"HKLM\SYSTEM\CurrentControlSet\Services\" + nombre, false))
            {
                if (k == null || k.GetValue("Start") == null) return null;
                switch (Convert.ToInt32(k.GetValue("Start")))
                {
                    case 0: return "boot";
                    case 1: return "system";
                    case 2: return Convert.ToInt32(k.GetValue("DelayedAutostart", 0)) == 1 ? "delayed-auto" : "auto";
                    case 3: return "demand";
                    case 4: return "disabled";
                }
                return null;
            }
        }

        public static bool ExisteServicio(string nombre) { return InicioServicio(nombre) != null; }

        static void Sc(string nombre, string inicio)
        {
            string salida;
            int code = Ejecutar("sc.exe", string.Format("config \"{0}\" start= {1}", nombre, inicio), out salida);
            if (code != 0) throw new Exception(string.Format("No se pudo cambiar el servicio {0}: {1}", nombre, salida.Trim()));
        }

        public static void SetServicio(Sesion s, string nombre, string inicio)
        {
            string act = InicioServicio(nombre);
            if (act == null || act == inicio || (inicio == "demand" && act == "disabled")) return;
            if (!s.Simular)
            {
                Sc(nombre, inicio);
                if (inicio == "disabled")
                {
                    try { using (var sc = new ServiceController(nombre)) if (sc.Status == ServiceControllerStatus.Running) sc.Stop(); } catch { }
                }
            }
            s.Anotar(new Dictionary<string, object> {
                { "Tipo", "Servicio" }, { "Nombre", nombre }, { "InicioAnterior", act }, { "InicioNuevo", inicio } });
        }

        // ------------------------------------------------------------ Tareas programadas
        static dynamic Tarea(string carpeta, string nombre)
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            string c = carpeta.TrimEnd('\\');
            dynamic f = svc.GetFolder(c.Length == 0 ? "\\" : c);
            return f.GetTask(nombre);
        }

        public static void DesactivarTarea(Sesion s, string carpeta, string nombre)
        {
            dynamic t;
            try { t = Tarea(carpeta, nombre); } catch { return; }   // no existe en este Windows
            if (!(bool)t.Enabled) return;
            if (!s.Simular) t.Enabled = false;
            s.Anotar(new Dictionary<string, object> { { "Tipo", "Tarea" }, { "Ruta", carpeta }, { "Nombre", nombre } });
        }

        // ------------------------------------------------------------ Plan de energía
        public const string PlanAlto = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string PlanEquilibrado = "381b4222-f694-41f0-9685-ff5bb260df2e";
        static readonly Regex Guid_ = new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public static string PlanActivo()
        {
            string o; Ejecutar("powercfg.exe", "/getactivescheme", out o);
            Match m = Guid_.Match(o);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        public static void SetPlanEnergia(Sesion s, string guid)
        {
            string act = PlanActivo();
            string lista; Ejecutar("powercfg.exe", "/list", out lista);
            string destino = guid;
            if (lista.IndexOf(guid, StringComparison.OrdinalIgnoreCase) < 0)
            {
                // El plan no existe en este equipo (pasa en algunos portátiles): se crea una copia.
                if (s.Simular) destino = guid + " (se crearía)";
                else
                {
                    string o; Ejecutar("powercfg.exe", "-duplicatescheme " + guid, out o);
                    Match m = Guid_.Match(o);
                    if (!m.Success) throw new Exception("No se pudo crear el plan de energía.");
                    destino = m.Value.ToLowerInvariant();
                }
            }
            if (act == destino) return;
            if (!s.Simular) { string o; Ejecutar("powercfg.exe", "/setactive " + destino, out o); }
            s.Anotar(new Dictionary<string, object> { { "Tipo", "PlanEnergia" }, { "Anterior", act }, { "Nuevo", destino } });
        }

        // ------------------------------------------------------------ Programas de inicio
        static readonly string[][] FuentesInicio = {
            new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Tu usuario" },
            new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Todos los usuarios" },
            new[] { @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "Todos los usuarios (32 bits)" }
        };

        static bool InicioHabilitado(string aprob, string nombre)
        {
            object v; RegistryValueKind t;
            if (!LeerValor(aprob, nombre, out v, out t)) return true;
            byte[] b = v as byte[];
            return b == null || b.Length == 0 || (b[0] & 1) == 0;   // 02/06 = activo, 03/07 = desactivado
        }

        public static List<ProgramaInicio> ProgramasInicio()
        {
            var lista = new List<ProgramaInicio>();
            foreach (string[] f in FuentesInicio)
            {
                using (RegistryKey k = Abrir(f[0], false))
                {
                    if (k == null) continue;
                    foreach (string n in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        lista.Add(new ProgramaInicio { Nombre = n, Comando = Convert.ToString(k.GetValue(n)), Origen = f[2], Aprob = f[1], Activo = InicioHabilitado(f[1], n) });
                    }
                }
            }
            var carpetas = new[] {
                new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "Carpeta Inicio (tu usuario)" },
                new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "Carpeta Inicio (todos)" } };
            foreach (string[] c in carpetas)
            {
                if (string.IsNullOrEmpty(c[0]) || !Directory.Exists(c[0])) continue;
                foreach (string file in Directory.GetFiles(c[0]))
                {
                    string n = Path.GetFileName(file);
                    if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    lista.Add(new ProgramaInicio { Nombre = n, Comando = file, Origen = c[2], Aprob = c[1], Activo = InicioHabilitado(c[1], n) });
                }
            }
            foreach (var p in lista)
            {
                p.Protegido = Catalogo.EsInicioProtegido(p);
                p.Recomendacion = p.Protegido ? null : Catalogo.RecomendacionInicio(p);
            }
            return lista;
        }

        public static IEnumerable<Resultado> DesactivarInicio(Sesion s, IEnumerable<ProgramaInicio> programas, Action<Resultado> avance)
        {
            var res = new List<Resultado>();
            foreach (var p in programas)
            {
                s.ItemActual = "inicio:" + p.Nombre;
                int antes = s.Cambios.Count;
                string estado = "Aplicado", detalle = "Origen: " + p.Origen + ". No se desinstala: solo deja de abrirse al encender.";
                try
                {
                    // Mismo mecanismo que el Administrador de tareas: no borra nada, solo lo marca como desactivado.
                    SetRegistro(s, p.Aprob, p.Nombre, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                    if (s.Simular) estado = "Se aplicaría";
                    else if (s.Cambios.Count == antes) estado = "Ya estaba bien";
                }
                catch (Exception e) { estado = "Error"; detalle = e.Message; }
                var r = new Resultado {
                    Id = s.ItemActual, Nombre = p.Nombre, Categoria = "Programas de inicio", Riesgo = "Bajo", Estado = estado, Detalle = detalle,
                    Porque = p.Recomendacion ?? "Elegido por ti para que no se abra al encender el PC." };
                res.Add(r);
                if (avance != null) avance(r);
            }
            return res;
        }

        // ------------------------------------------------------------ Ejecutar el catálogo
        public static List<Resultado> Optimizar(Sesion s, Action<Resultado> avance)
        {
            var res = new List<Resultado>();
            foreach (Ajuste a in Catalogo.Ajustes)
            {
                var r = new Resultado { Id = a.Id, Nombre = a.Nombre, Categoria = a.Categoria, Porque = a.Porque, Riesgo = a.Riesgo };
                if (!a.Perfiles.Contains("Todos") && !a.Perfiles.Contains(s.Perfil))
                {
                    r.Estado = "Omitido"; r.Detalle = "No corresponde al perfil elegido.";
                }
                else
                {
                    bool aplica;
                    try { aplica = a.Condicion == null || a.Condicion(s); } catch { aplica = false; }
                    if (!aplica) { r.Estado = "Omitido"; r.Detalle = a.SiNoAplica; }
                    else
                    {
                        int antes = s.Cambios.Count;
                        s.ItemActual = a.Id;
                        try
                        {
                            a.Aplicar(s);
                            int n = s.Cambios.Count - antes;
                            if (n > 0)
                            {
                                r.Estado = s.Simular ? "Se aplicaría" : "Aplicado";
                                r.Detalle = string.Join("; ", s.Cambios.Skip(antes).Select(DescribirCambio));
                                if (a.Reinicio) s.RequiereReinicio = true;
                            }
                            else r.Estado = "Ya estaba bien";
                        }
                        catch (Exception e) { r.Estado = "Error"; r.Detalle = e.Message; }
                        GuardarRespaldo(s);
                    }
                }
                res.Add(r);
                if (avance != null) avance(r);
            }
            return res;
        }

        static string Txt(object v)
        {
            IEnumerable e = v as IEnumerable;
            if (e != null && !(v is string)) return string.Join(" ", e.Cast<object>());
            return Convert.ToString(v);
        }

        public static string DescribirCambio(Dictionary<string, object> c)
        {
            switch ((string)c["Tipo"])
            {
                case "Registro": return string.Format("{0}: {1} -> {2}", c["Nombre"], (bool)c["Existia"] ? Txt(c["ValorAnterior"]) : "(no existía)", Txt(c["ValorNuevo"]));
                case "Servicio": return string.Format("servicio {0}: {1} -> {2}", c["Nombre"], c["InicioAnterior"], c["InicioNuevo"]);
                case "Tarea": return string.Format("tarea {0}{1} desactivada", c["Ruta"], c["Nombre"]);
                case "PlanEnergia": return string.Format("plan {0} -> {1}", c["Anterior"], c["Nuevo"]);
                case "Pantalla": return string.Format("pantalla {0} Hz -> {1} Hz", c["Anterior"], c["Nuevo"]);
            }
            return "";
        }

        // ------------------------------------------------------------ Respaldo y restauración
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static void GuardarRespaldo(Sesion s)
        {
            if (s.Simular || s.ArchivoRespaldo == null) return;
            var d = new Dictionary<string, object> {
                { "Version", 1 }, { "Fecha", DateTime.Now.ToString("s") }, { "Perfil", s.Perfil },
                { "Equipo", Environment.MachineName }, { "Revertido", null },
                { "MedicionAntes", s.MedicionAntes == null ? null : s.MedicionAntes.ADiccionario() },
                { "Cambios", s.Cambios } };
            File.WriteAllText(s.ArchivoRespaldo, Json.Serialize(d), new UTF8Encoding(false));
        }

        public static Dictionary<string, object> LeerRespaldo(string archivo)
        {
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(archivo, Encoding.UTF8));
        }

        public static void EscribirRespaldo(string archivo, Dictionary<string, object> d)
        {
            File.WriteAllText(archivo, Json.Serialize(d), new UTF8Encoding(false));
        }

        public static string PuntoRestauracion()
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                var mc = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                var en = mc.GetMethodParameters("Enable");
                en["Drive"] = Path.GetPathRoot(Environment.SystemDirectory);
                mc.InvokeMethod("Enable", en, null);
                int antes = mc.GetInstances().Count;
                var p = mc.GetMethodParameters("CreateRestorePoint");
                p["Description"] = "Savia - antes de optimizar";
                p["RestorePointType"] = 12;   // MODIFY_SETTINGS
                p["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
                var r = mc.InvokeMethod("CreateRestorePoint", p, null);
                if (Convert.ToUInt32(r["ReturnValue"]) != 0) return "No se pudo crear (código " + r["ReturnValue"] + "). El respaldo propio de Savia te sigue protegiendo.";
                if (mc.GetInstances().Count <= antes) return "Windows no creó uno nuevo porque ya existe uno de las últimas 24 h. El respaldo propio de Savia te sigue protegiendo.";
                return "Creado correctamente.";
            }
            catch (Exception e) { return "No se pudo crear (" + e.Message + "). El respaldo propio de Savia te sigue protegiendo."; }
        }

        static object ValorDesdeJson(object v, string tipo)
        {
            if (v == null) return null;
            switch (tipo)
            {
                case "Binary": return ((IEnumerable)v).Cast<object>().Select(Convert.ToByte).ToArray();
                case "MultiString": return ((IEnumerable)v).Cast<object>().Select(Convert.ToString).ToArray();
                case "DWord": return unchecked((int)Convert.ToInt64(v));
                case "QWord": return Convert.ToInt64(v);
                default: return Convert.ToString(v);
            }
        }

        public static string[] Revertir(string archivo, out int restaurados)
        {
            var d = LeerRespaldo(archivo);
            var cambios = ((IEnumerable)d["Cambios"]).Cast<Dictionary<string, object>>().Reverse().ToList();
            var fallos = new List<string>();
            restaurados = 0;
            foreach (var c in cambios)
            {
                try
                {
                    switch ((string)c["Tipo"])
                    {
                        case "Registro":
                        {
                            string ruta = (string)c["Ruta"], nombre = (string)c["Nombre"], sub;
                            using (RegistryKey r = Raiz(ruta, out sub))
                            {
                                if (!(bool)c["Existia"])
                                {
                                    using (RegistryKey k = r.OpenSubKey(sub, true)) if (k != null) k.DeleteValue(nombre, false);
                                }
                                else
                                {
                                    string tipo = (string)c["TipoAnterior"];
                                    using (RegistryKey k = r.CreateSubKey(sub, true))
                                        k.SetValue(nombre, ValorDesdeJson(c["ValorAnterior"], tipo), (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), tipo));
                                }
                                if (c.ContainsKey("ClaveCreada") && (bool)c["ClaveCreada"])
                                {
                                    using (RegistryKey k = r.OpenSubKey(sub, false))
                                        if (k != null && k.ValueCount == 0 && k.SubKeyCount == 0) { k.Close(); r.DeleteSubKey(sub, false); }
                                }
                            }
                            break;
                        }
                        case "Servicio": Sc((string)c["Nombre"], (string)c["InicioAnterior"]); break;
                        case "Tarea": Tarea((string)c["Ruta"], (string)c["Nombre"]).Enabled = true; break;
                        case "PlanEnergia":
                            if (c["Anterior"] != null) { string o; Ejecutar("powercfg.exe", "/setactive " + c["Anterior"], out o); }
                            break;
                        case "Pantalla":
                        {
                            int r2 = Pantalla.Poner(Convert.ToInt32(c["Anterior"]));
                            if (r2 != 0) throw new Exception("código " + r2);
                            break;
                        }
                    }
                    restaurados++;
                }
                catch (Exception e) { fallos.Add(string.Format("{0} {1}: {2}", c["Tipo"], c.ContainsKey("Nombre") ? c["Nombre"] : "", e.Message)); }
            }
            d["Revertido"] = DateTime.Now.ToString("s");
            EscribirRespaldo(archivo, d);
            return fallos.ToArray();
        }

        // ------------------------------------------------------------ Limpieza (no reversible)
        public static string Limpiar()
        {
            long total = 0; int n = 0;
            DateTime limite = DateTime.Now.AddDays(-2);
            var dirs = new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }.Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                IEnumerable<string> archivos;
                try { archivos = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); } catch { archivos = Directory.EnumerateFiles(dir).ToList(); }
                foreach (string f in archivos)
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTime >= limite) continue;
                        long tam = fi.Length;
                        fi.Delete(); total += tam; n++;
                    }
                    catch { }   // en uso: se deja
                }
            }
            return string.Format("Se borraron {0} archivos temporales con más de 2 días ({1} MB liberados).", n, total / (1024 * 1024));
        }

        // ------------------------------------------------------------ Utilidades
        public static int Ejecutar(string exe, string args, out string salida)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }
}
