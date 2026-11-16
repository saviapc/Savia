// Savia - Punto de entrada.
using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Savia")]
[assembly: AssemblyProduct("Savia")]
[assembly: AssemblyDescription("Optimiza Windows para jugar de forma segura y reversible.")]
[assembly: AssemblyCopyright("Licencia MIT")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Savia
{
    public static class Programa
    {
        static readonly string Log = Path.Combine(Motor.DirDatos, "errores.log");

        public static void Registrar(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Motor.DirDatos);
                File.AppendAllText(Log, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        public static int Main()
        {
            bool nuevo;
            using (new Mutex(true, "Savia.UnaSolaInstancia", out nuevo))
            {
                if (!nuevo) return 0;   // ya está abierto

                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("Savia necesita permisos de administrador para optimizar Windows.", "Savia");
                    return 1;
                }

                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (o, e) =>
                {
                    Registrar(e.Exception);
                    MessageBox.Show("Algo salió mal: " + e.Exception.Message + "\n\nDetalles en " + Log, "Savia");
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Registrar(e.ExceptionObject as Exception);
                var v = new Ventana();
                return app.Run(v.Win);
            }
        }
    }
}
