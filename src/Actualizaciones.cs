// Savia - Aviso de versión nueva. Solo consulta el número de la última versión publicada en GitHub;
// no envía datos del usuario ni descarga nada por su cuenta.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;

namespace Savia
{
    public class VersionNueva
    {
        public Version Numero;
        public string Url;
    }

    public static class Actualizaciones
    {
        const string Api = "https://api.github.com/repos/saviapc/Savia/releases/latest";

        public static Version Actual { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        /// <summary>Devuelve la última versión publicada si es más nueva que <paramref name="actual"/>; si no, null.</summary>
        public static VersionNueva Comprobar(Version actual)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(Api);
                req.UserAgent = "Savia/" + actual;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 6000;
                string json;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
                var d = Motor.Json.Deserialize<Dictionary<string, object>>(json);
                Version v;
                if (!Version.TryParse(Convert.ToString(d["tag_name"]).TrimStart('v', 'V'), out v)) return null;
                // Comparar con la misma cantidad de partes (1.2.0 frente a 1.2.0.0).
                var a = new Version(actual.Major, actual.Minor, Math.Max(actual.Build, 0));
                var n = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
                return n > a ? new VersionNueva { Numero = n, Url = Convert.ToString(d["html_url"]) } : null;
            }
            catch { return null; }   // sin internet o GitHub no responde: no se avisa
        }
    }
}
