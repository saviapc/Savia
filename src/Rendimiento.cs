// Savia - Ajustes de energía para menos input lag, memoria en caché y limpieza de basura acumulada.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Savia
{
    /// <summary>Opciones ocultas del plan de energía (powercfg), con respaldo del valor anterior.</summary>
    public static class Energia
    {
        public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3", UsbSuspension = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        public const string SubPcie = "501a4d13-42af-4429-9fd1-a8218c268e20", PcieAhorro = "ee12f906-d277-404b-b6da-e5fa1a576df5";

        /// <summary>Valor actual con corriente (AC). Null si este equipo no tiene esa opción.</summary>
        public static int? Leer(string plan, string sub, string ajuste)
        {
            string o;
            if (Motor.Ejecutar("powercfg.exe", string.Format("/q {0} {1} {2}", plan, sub, ajuste), out o) != 0) return null;
            var m = Regex.Matches(o, @"0x([0-9a-fA-F]{8})");
            if (m.Count < 2) return null;
            return Convert.ToInt32(m[m.Count - 2].Groups[1].Value, 16);   // penúltimo = AC, último = batería
        }

        public static void Poner(string plan, string sub, string ajuste, int valor)
        {
            string o;
            if (Motor.Ejecutar("powercfg.exe", string.Format("/setacvalueindex {0} {1} {2} {3}", plan, sub, ajuste, valor), out o) != 0)
                throw new Exception("powercfg: " + o.Trim());
            if (Motor.PlanActivo() == plan) Motor.Ejecutar("powercfg.exe", "/setactive " + plan, out o);
        }

        public static void Aplicar(Sesion s, string sub, string ajuste, int valor, string nombre)
        {
            string plan = Motor.PlanActivo();
            if (plan == null) return;
            int? act = Leer(plan, sub, ajuste);
            if (act == null || act == valor) return;
            if (!s.Simular) Poner(plan, sub, ajuste, valor);
            s.Anotar(new Dictionary<string, object> {
                { "Tipo", "Energia" }, { "Nombre", nombre }, { "Plan", plan }, { "Sub", sub }, { "Ajuste", ajuste }, { "Anterior", act.Value }, { "Nuevo", valor } });
        }
    }

    /// <summary>Vacía la memoria en caché (lista "standby"), como hacen ISLC o RAMMap.</summary>
    public static class Memoria
    {
        [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int clase, ref int info, int largo);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr proceso, uint acceso, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sistema, string nombre, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool quitarTodos, ref Privilegios nuevo, int largo, IntPtr anterior, IntPtr largoAnterior);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct Privilegios { public int Cantidad; public long Luid; public int Atributos; }

        static void ActivarPrivilegio(string nombre)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) return;   // ADJUST_PRIVILEGES | QUERY
            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, nombre, out luid)) return;
                var p = new Privilegios { Cantidad = 1, Luid = luid, Atributos = 2 };      // SE_PRIVILEGE_ENABLED
                AdjustTokenPrivileges(token, false, ref p, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(token); }
        }

        static double CacheGB()
        {
            var m = Wmi.Consultar("SELECT StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes, StandbyCacheCoreBytes FROM Win32_PerfFormattedData_PerfOS_Memory").FirstOrDefault();
            if (m == null) return 0;
            return (Convert.ToDouble(m["StandbyCacheNormalPriorityBytes"]) + Convert.ToDouble(m["StandbyCacheReserveBytes"]) + Convert.ToDouble(m["StandbyCacheCoreBytes"])) / (1024.0 * 1024 * 1024);
        }

        public static string LiberarCache()
        {
            double antes = CacheGB();
            ActivarPrivilegio("SeProfileSingleProcessPrivilege");
            int orden = 4;   // MemoryPurgeStandbyList
            int r = NtSetSystemInformation(80, ref orden, 4);   // SystemMemoryListInformation
            if (r != 0) throw new Exception("Windows no permitió vaciar la memoria en caché (código 0x" + r.ToString("X") + ").");
            Thread.Sleep(600);
            return string.Format("Se liberaron {0:0.0} GB de memoria en caché. Windows la irá llenando de nuevo con el uso.", Math.Max(0, antes - CacheGB()));
        }
    }

    /// <summary>Basura acumulada. Lo único de Savia que no se deshace: son archivos que no hacen falta.</summary>
    public static class Limpieza
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string raiz, int flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string raiz, ref InfoPapelera info);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct InfoPapelera { public int Tam; public long Bytes; public long Elementos; }

        static long BorrarViejos(string dir, DateTime limite, ref int n)
        {
            long total = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            IEnumerable<string> archivos;
            try { archivos = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
            catch { try { archivos = Directory.EnumerateFiles(dir).ToList(); } catch { return 0; } }
            foreach (string f in archivos)
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTime >= limite) continue;
                    long tam = fi.Length;
                    fi.Attributes = FileAttributes.Normal;
                    fi.Delete(); total += tam; n++;
                }
                catch { }   // en uso: se deja
            }
            return total;
        }

        public static string Limpiar(bool papelera)
        {
            DateTime limite = DateTime.Now.AddDays(-2);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string datos = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // No se tocan las cachés de sombreadores (DirectX/NVIDIA/AMD): borrarlas provoca tirones mientras se regeneran.
            var grupos = new[] {
                new { Nombre = "temporales", Dirs = new[] { Path.GetTempPath(), Path.Combine(win, "Temp") } },
                new { Nombre = "descargas viejas de Windows Update", Dirs = new[] { Path.Combine(win, @"SoftwareDistribution\Download") } },
                new { Nombre = "informes de errores", Dirs = new[] { Path.Combine(datos, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(datos, @"Microsoft\Windows\WER\ReportQueue"),
                                                                    Path.Combine(win, "Minidump"), Path.Combine(local, "CrashDumps") } } };
            var partes = new List<string>();
            long total = 0;
            foreach (var g in grupos)
            {
                int n = 0; long b = 0;
                foreach (string d in g.Dirs.Distinct(StringComparer.OrdinalIgnoreCase)) b += BorrarViejos(d, limite, ref n);
                if (n > 0) partes.Add(string.Format("{0} ({1} MB)", g.Nombre, b / (1024 * 1024)));
                total += b;
            }
            if (papelera)
            {
                var info = new InfoPapelera { Tam = Marshal.SizeOf(typeof(InfoPapelera)) };
                SHQueryRecycleBin(null, ref info);
                if (info.Elementos > 0 && SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) == 0)
                {
                    partes.Add(string.Format("Papelera ({0} MB)", info.Bytes / (1024 * 1024)));
                    total += info.Bytes;
                }
            }
            if (partes.Count == 0) return "No había basura acumulada que borrar.";
            return string.Format("Se liberaron {0} MB: {1}.", total / (1024 * 1024), string.Join(", ", partes));
        }
    }
}
