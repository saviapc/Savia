// Savia - Detección de hardware, medición de antes/después, diagnóstico y frecuencia del monitor.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace Savia
{
    static class Wmi
    {
        public static List<ManagementObject> Consultar(string consulta, string ns = @"root\cimv2")
        {
            try
            {
                using (var s = new ManagementObjectSearcher(ns, consulta))
                    return s.Get().Cast<ManagementObject>().ToList();
            }
            catch { return new List<ManagementObject>(); }
        }
    }

    public class Hardware
    {
        public string CPU, GPUs, GPUMarca, Disco, SO;
        public int Nucleos, Hilos, RAMGB, Build;
        public bool GPUDedicada, Portatil, GamaBaja;

        public static Hardware Detectar()
        {
            var h = new Hardware();
            var cpu = Wmi.Consultar("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").FirstOrDefault();
            if (cpu != null)
            {
                h.CPU = Convert.ToString(cpu["Name"]).Trim();
                h.Nucleos = Convert.ToInt32(cpu["NumberOfCores"]);
                h.Hilos = Convert.ToInt32(cpu["NumberOfLogicalProcessors"]);
            }
            var gpus = Wmi.Consultar("SELECT Name FROM Win32_VideoController")
                .Select(g => Convert.ToString(g["Name"]))
                .Where(n => !System.Text.RegularExpressions.Regex.IsMatch(n, "Basic Display|Remote|Virtual|Parsec|Mirage")).ToList();
            h.GPUs = string.Join(" + ", gpus);
            string ded = gpus.FirstOrDefault(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"NVIDIA|GeForce|Quadro|Radeon RX|Radeon Pro|Arc\(TM\) [AB]\d"));
            h.GPUDedicada = ded != null;
            h.GPUMarca = ded == null ? "Integrada" : ded.Contains("Radeon") ? "AMD" : ded.Contains("Arc") ? "Intel" : "NVIDIA";

            var cs = Wmi.Consultar("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            h.RAMGB = cs == null ? 0 : (int)Math.Round(Convert.ToDouble(cs["TotalPhysicalMemory"]) / (1024.0 * 1024 * 1024));

            var chasis = Wmi.Consultar("SELECT ChassisTypes FROM Win32_SystemEnclosure").SelectMany(e => (ushort[])e["ChassisTypes"] ?? new ushort[0]);
            int[] portatiles = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
            h.Portatil = Wmi.Consultar("SELECT Name FROM Win32_Battery").Any() || chasis.Any(c => portatiles.Contains(c));

            h.Disco = "Desconocido";
            try
            {
                string letra = Environment.SystemDirectory.Substring(0, 1);
                var part = Wmi.Consultar("SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='" + letra + "'", @"root\Microsoft\Windows\Storage").FirstOrDefault();
                if (part != null)
                {
                    string num = Convert.ToString(part["DiskNumber"]);
                    var pd = Wmi.Consultar("SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId='" + num + "'", @"root\Microsoft\Windows\Storage").FirstOrDefault();
                    if (pd != null)
                    {
                        int media = Convert.ToInt32(pd["MediaType"]), bus = Convert.ToInt32(pd["BusType"]);
                        h.Disco = media == 3 ? "HDD" : (media == 4 || bus == 17) ? "SSD" : "Desconocido";
                    }
                }
            }
            catch { }

            var os = Wmi.Consultar("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
            if (os != null) { h.SO = Convert.ToString(os["Caption"]); h.Build = Convert.ToInt32(os["BuildNumber"]); }
            h.GamaBaja = h.RAMGB <= 8 || !h.GPUDedicada;
            return h;
        }
    }

    // ---------------------------------------------------------------- Medición
    public class Medicion
    {
        public string Fecha;
        public double Procesos, Servicios, RAMUsadaGB, CPUReposo, Inicio;
        public double? PingMs;

        public static readonly string[][] Etiquetas = {
            new[] { "Procesos", "Procesos en ejecución", "" },
            new[] { "Servicios", "Servicios en ejecución", "" },
            new[] { "RAMUsadaGB", "RAM ocupada sin jugar", " GB" },
            new[] { "CPUReposo", "Uso de CPU en reposo", " %" },
            new[] { "Inicio", "Programas que arrancan con Windows", "" },
            new[] { "PingMs", "Ping (1.1.1.1)", " ms" } };

        public static Medicion Tomar()
        {
            var m = new Medicion { Fecha = DateTime.Now.ToString("s") };
            m.Procesos = Process.GetProcesses().Length;
            m.Servicios = ServiceController.GetServices().Count(s => s.Status == ServiceControllerStatus.Running);
            var os = Wmi.Consultar("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
            if (os != null)
                m.RAMUsadaGB = Math.Round((Convert.ToDouble(os["TotalVisibleMemorySize"]) - Convert.ToDouble(os["FreePhysicalMemory"])) / (1024.0 * 1024), 1);
            var muestras = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var c = Wmi.Consultar("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'").FirstOrDefault();
                if (c != null) muestras.Add(Convert.ToDouble(c["PercentProcessorTime"]));
                Thread.Sleep(500);
            }
            m.CPUReposo = muestras.Count == 0 ? 0 : Math.Round(muestras.Average(), 1);
            m.Inicio = Motor.ProgramasInicio().Count(p => p.Activo);
            try
            {
                var tiempos = new List<long>();
                using (var ping = new Ping())
                    for (int i = 0; i < 4; i++)
                    {
                        var r = ping.Send("1.1.1.1", 1500);
                        if (r.Status == IPStatus.Success) tiempos.Add(r.RoundtripTime);
                    }
                if (tiempos.Count > 0) m.PingMs = Math.Round(tiempos.Average());
            }
            catch { }
            return m;
        }

        public double? Valor(string clave)
        {
            switch (clave)
            {
                case "Procesos": return Procesos;
                case "Servicios": return Servicios;
                case "RAMUsadaGB": return RAMUsadaGB;
                case "CPUReposo": return CPUReposo;
                case "Inicio": return Inicio;
                case "PingMs": return PingMs;
            }
            return null;
        }

        public Dictionary<string, object> ADiccionario()
        {
            var d = new Dictionary<string, object> { { "Fecha", Fecha } };
            foreach (var e in Etiquetas) d[e[0]] = Valor(e[0]);
            return d;
        }

        public static Medicion DesdeDiccionario(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            Func<string, double> num = k => d.ContainsKey(k) && d[k] != null ? Convert.ToDouble(d[k]) : 0;
            return new Medicion {
                Fecha = d.ContainsKey("Fecha") ? Convert.ToString(d["Fecha"]) : null,
                Procesos = num("Procesos"), Servicios = num("Servicios"), RAMUsadaGB = num("RAMUsadaGB"),
                CPUReposo = num("CPUReposo"), Inicio = num("Inicio"),
                PingMs = d.ContainsKey("PingMs") && d["PingMs"] != null ? (double?)Convert.ToDouble(d["PingMs"]) : null };
        }
    }

    // ---------------------------------------------------------------- Diagnóstico
    public class Hallazgo
    {
        public string Gravedad, Titulo, Detalle;
        public Hallazgo(string g, string t, string d) { Gravedad = g; Titulo = t; Detalle = d; }
    }

    public static class Diagnostico
    {
        public static List<Hallazgo> Revisar(Hardware hw)
        {
            var h = new List<Hallazgo>();
            string unidad = Environment.SystemDirectory.Substring(0, 2);

            var d = Wmi.Consultar("SELECT FreeSpace, Size FROM Win32_LogicalDisk WHERE DeviceID='" + unidad + "'").FirstOrDefault();
            if (d != null && d["Size"] != null)
            {
                double libre = Convert.ToDouble(d["FreeSpace"]), total = Convert.ToDouble(d["Size"]);
                int gb = (int)Math.Round(libre / (1024.0 * 1024 * 1024));
                if (gb < 20 || libre / total < 0.10)
                    h.Add(new Hallazgo("Alta", "Disco casi lleno", string.Format("Solo quedan {0} GB libres en {1}. Con el disco lleno, Windows y los juegos no tienen sitio para temporales y aparecen tirones. Intenta dejar al menos 30 GB libres.", gb, unidad)));
            }

            var gpus = Wmi.Consultar("SELECT Name, DriverDate FROM Win32_VideoController");
            if (gpus.Any(g => Convert.ToString(g["Name"]).Contains("Microsoft Basic Display")))
                h.Add(new Hallazgo("Alta", "Falta el controlador de la gráfica", "Windows está usando un controlador genérico: los juegos irán muy mal. Instala el controlador desde la web de NVIDIA, AMD o Intel."));
            var principal = gpus.FirstOrDefault(g => System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(g["Name"]), "NVIDIA|GeForce|Radeon|Arc"))
                            ?? gpus.FirstOrDefault(g => !System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(g["Name"]), "Basic|Virtual|Remote"));
            if (principal != null && principal["DriverDate"] != null)
            {
                try
                {
                    int dias = (DateTime.Now - ManagementDateTimeConverter.ToDateTime(Convert.ToString(principal["DriverDate"]))).Days;
                    if (dias > 180)
                    {
                        string n = Convert.ToString(principal["Name"]);
                        string web = n.Contains("NVIDIA") || n.Contains("GeForce") ? "nvidia.com/drivers" : n.Contains("Radeon") ? "amd.com/support" : "intel.com/support";
                        h.Add(new Hallazgo("Media", "Controlador de la gráfica antiguo", string.Format("El controlador de {0} tiene {1} meses. Los nuevos traen mejoras de rendimiento para juegos recientes. Descárgalo en {2}.", n, dias / 30, web)));
                    }
                }
                catch { }
            }

            var mods = Wmi.Consultar("SELECT SMBIOSMemoryType, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            if (mods.Count > 0 && !hw.Portatil)
            {
                int tipo = Convert.ToInt32(mods[0]["SMBIOSMemoryType"]);
                int vel = mods.Min(m => Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0));
                if ((tipo == 26 && vel > 0 && vel <= 2666) || (tipo == 34 && vel > 0 && vel <= 4800))
                    h.Add(new Hallazgo("Media", "La RAM podría ir más lenta de lo que puede", string.Format("Tu memoria funciona a {0} MT/s. Si es RAM de gama gamer, probablemente falta activar el perfil XMP (Intel) o EXPO (AMD) en la BIOS: suele dar entre un 5 y un 15 % más de FPS. Se cambia en la BIOS; Savia no la toca.", vel)));
                if (mods.Count == 1)
                    h.Add(new Hallazgo("Media", "Solo hay un módulo de RAM", "Con un único módulo la memoria trabaja en un solo canal y rinde bastante menos. Dos módulos iguales (dual channel) dan más FPS, sobre todo con gráficos integrados."));
            }

            var av = Wmi.Consultar("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2")
                .Where(a => (Convert.ToInt32(a["productState"]) & 0x1000) != 0).Select(a => Convert.ToString(a["displayName"])).ToList();
            if (av.Count > 1)
                h.Add(new Hallazgo("Media", "Hay más de un antivirus activo", "Activos: " + string.Join(", ", av) + ". Dos antivirus revisando lo mismo duplican el consumo y causan tirones. Deja solo uno."));

            var os = Wmi.Consultar("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
            if (os != null)
            {
                double uso = 1 - Convert.ToDouble(os["FreePhysicalMemory"]) / Convert.ToDouble(os["TotalVisibleMemorySize"]);
                if (uso > 0.75)
                    h.Add(new Hallazgo("Media", "La memoria está muy ocupada sin jugar", string.Format("El {0} % de la RAM ya está en uso antes de abrir ningún juego. Cierra programas que no uses; quitar programas de inicio también ayuda.", Math.Round(uso * 100))));
            }

            if (hw.Portatil)
            {
                var bat = Wmi.Consultar("SELECT BatteryStatus FROM Win32_Battery").FirstOrDefault();
                if (bat != null && Convert.ToInt32(bat["BatteryStatus"]) == 1)
                    h.Add(new Hallazgo("Info", "Estás usando la batería", "Los portátiles bajan mucho el rendimiento sin cargador. Para jugar, conéctalo a la corriente."));
            }

            if (h.Count == 0) h.Add(new Hallazgo("Info", "Todo en orden", "No encontré problemas de disco, controladores, memoria ni antivirus."));
            return h;
        }
    }

    // ---------------------------------------------------------------- Frecuencia del monitor
    public static class Pantalla
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr lParam);

        static DEVMODE Nuevo() { var d = new DEVMODE(); d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); return d; }

        /// <summary>Frecuencia actual y máxima con la misma resolución (pantalla principal). Null si no se puede saber.</summary>
        public static int[] Frecuencias()
        {
            DEVMODE cur = Nuevo();
            if (!EnumDisplaySettings(null, -1, ref cur) || cur.dmDisplayFrequency <= 1) return null;
            int max = cur.dmDisplayFrequency;
            DEVMODE m = Nuevo();
            for (int i = 0; EnumDisplaySettings(null, i, ref m); i++)
                if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmBitsPerPel == cur.dmBitsPerPel &&
                    (m.dmDisplayFlags & 2) == 0 && m.dmDisplayFrequency > max)
                    max = m.dmDisplayFrequency;
            return new[] { cur.dmDisplayFrequency, max };
        }

        /// <summary>0 = correcto. Windows lo prueba primero (CDS_TEST) y solo después lo guarda.</summary>
        public static int Poner(int hz)
        {
            DEVMODE cur = Nuevo();
            EnumDisplaySettings(null, -1, ref cur);
            cur.dmDisplayFrequency = hz;
            cur.dmFields = 0x400000;
            int prueba = ChangeDisplaySettingsEx(null, ref cur, IntPtr.Zero, 2, IntPtr.Zero);
            if (prueba != 0) return prueba;
            return ChangeDisplaySettingsEx(null, ref cur, IntPtr.Zero, 1, IntPtr.Zero);
        }

        public static void Aplicar(Sesion s, int hz)
        {
            int[] f = Frecuencias();
            if (f == null || f[0] == hz) return;
            if (!s.Simular)
            {
                int r = Poner(hz);
                if (r != 0) throw new Exception(string.Format("Windows rechazó {0} Hz para esta pantalla (código {1}).", hz, r));
            }
            s.Anotar(new Dictionary<string, object> { { "Tipo", "Pantalla" }, { "Nombre", "frecuencia" }, { "Anterior", f[0] }, { "Nuevo", hz } });
        }
    }
}
