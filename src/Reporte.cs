// Savia - Reporte HTML de todo lo que se hizo, con el porqué y cómo deshacerlo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Savia
{
    public static class Reporte
    {
        static string H(object t) { return WebUtility.HtmlEncode(Convert.ToString(t)); }

        public static void Generar(Sesion s, List<Resultado> res, string punto, string limpieza, Medicion despues, List<Hallazgo> diag, string destino)
        {
            var hw = s.Hw;
            var sb = new StringBuilder();
            int aplicados = res.Count(r => r.Estado == "Aplicado" || r.Estado == "Se aplicaría");
            string titulo = s.Simular ? "Simulación: esto es lo que Savia haría" : "Reporte de optimización";

            sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Savia - ").Append(titulo).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;background:#131311;color:#ede8df;max-width:1100px;margin:30px auto;padding:0 20px}")
              .Append("h1{font-weight:300;margin-bottom:4px} h2{font-weight:400;color:#a9c29b;margin-top:34px} .sub{color:#8c877c}")
              .Append("table{width:100%;border-collapse:collapse;margin-top:12px} td,th{padding:9px 10px;border-bottom:1px solid #2c2b26;vertical-align:top;text-align:left}")
              .Append("th{color:#8c877c;font-weight:400} tr.cat td{color:#cdbb94;font-size:12px;letter-spacing:.08em;text-transform:uppercase;padding-top:22px}")
              .Append(".ok{color:#a9c29b;font-weight:600} .bien{color:#cdbb94} .omit{color:#6a675f} .err{color:#d9786b;font-weight:600} .warn{color:#e0b46c;font-weight:600}")
              .Append(".det{color:#6a675f;font-size:12px;margin-top:4px;word-break:break-all} .det2{color:#8c877c;font-size:13px;margin-top:4px}")
              .Append(".aviso{background:#22201a;border-left:3px solid #d8a27f;padding:10px 14px;border-radius:6px} code{background:#1a1a17;padding:2px 6px;border-radius:4px}")
              .Append("</style></head><body>");
            sb.AppendFormat("<h1>{0}</h1><div class=\"sub\">{1:dd/MM/yyyy HH:mm} · Perfil: <b>{2}</b> · {3} ajustes {4}</div>",
                titulo, DateTime.Now, H(s.Perfil), aplicados, s.Simular ? "por aplicar" : "aplicados");
            if (s.Simular) sb.Append("<p class=\"aviso\">Modo simulación: <b>no se cambió nada</b> en este equipo.</p>");
            else if (s.RequiereReinicio) sb.Append("<p class=\"aviso\">Reinicia el PC para que todos los cambios surtan efecto.</p>");

            sb.Append("<h2>Tu equipo</h2><ul>");
            foreach (string l in new[] {
                string.Format("Procesador: {0} ({1} núcleos / {2} hilos)", hw.CPU, hw.Nucleos, hw.Hilos),
                "Gráfica: " + hw.GPUs, "Memoria RAM: " + hw.RAMGB + " GB", "Disco de Windows: " + hw.Disco,
                "Tipo: " + (hw.Portatil ? "Portátil" : "PC de escritorio"), string.Format("Sistema: {0} (compilación {1})", hw.SO, hw.Build) })
                sb.Append("<li>").Append(H(l)).Append("</li>");
            sb.Append("</ul>");

            if (s.MedicionAntes != null && despues == null)
            {
                // Medir justo al terminar engaña (Windows sigue procesando los cambios): la comparación se hace tras reiniciar.
                sb.Append("<h2>Cómo estaba tu PC antes</h2><table><tr><th>Medida</th><th>Antes</th></tr>");
                foreach (var e in Medicion.Etiquetas)
                {
                    double? a = s.MedicionAntes.Valor(e[0]);
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td></tr>", H(e[1]), a.HasValue ? a + e[2] : "-");
                }
                sb.Append("</table><p class=\"sub\">").Append(s.Simular
                    ? "En simulación no se cambia nada."
                    : "Reinicia el PC y vuelve a abrir Savia: medirá solo y te mostrará cuánto mejoró. También puedes hacerlo desde el historial con \"medir ahora\".").Append("</p>");
            }
            else if (s.MedicionAntes != null)
            {
                sb.Append("<h2>Antes y después</h2><table><tr><th>Medida</th><th>Antes</th><th>Después</th></tr>");
                foreach (var e in Medicion.Etiquetas)
                {
                    double? a = s.MedicionAntes.Valor(e[0]), d = despues.Valor(e[0]);
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td class=\"{2}\">{3}</td></tr>", H(e[1]),
                        a.HasValue ? a + e[2] : "-", a.HasValue && d.HasValue && d < a ? "ok" : "", d.HasValue ? d + e[2] : "-");
                }
                sb.Append("</table>");
            }

            if (diag != null && diag.Count > 0)
            {
                sb.Append("<h2>Revisa esto</h2><p class=\"sub\">Cosas que Savia no puede arreglar solo, pero que afectan al rendimiento.</p><table>");
                foreach (var x in diag)
                    sb.AppendFormat("<tr><td class=\"{0}\">{1}</td><td><b>{2}</b><div class=\"det2\">{3}</div></td></tr>",
                        x.Gravedad == "Alta" ? "err" : x.Gravedad == "Media" ? "warn" : "bien", H(x.Gravedad), H(x.Titulo), H(x.Detalle));
                sb.Append("</table>");
            }

            sb.Append("<h2>Qué se hizo y por qué</h2><table><tr><th style=\"width:28%\">Ajuste</th><th>Por qué</th><th>Riesgo</th><th>Estado</th></tr>");
            foreach (string cat in res.Select(r => r.Categoria).Distinct())
            {
                sb.Append("<tr class=\"cat\"><td colspan=\"4\">").Append(H(cat)).Append("</td></tr>");
                foreach (var r in res.Where(r => r.Categoria == cat))
                {
                    string clase = r.Estado == "Aplicado" || r.Estado == "Se aplicaría" ? "ok" : r.Estado == "Error" ? "err" : r.Estado == "Ya estaba bien" ? "bien" : "omit";
                    sb.AppendFormat("<tr><td><b>{0}</b>{1}</td><td>{2}</td><td>{3}</td><td class=\"{4}\">{5}</td></tr>", H(r.Nombre),
                        string.IsNullOrEmpty(r.Detalle) ? "" : "<div class=\"det\">" + H(r.Detalle) + "</div>", H(r.Porque), H(r.Riesgo), clase, H(r.Estado));
                }
            }
            sb.Append("</table>");

            if (limpieza != null) sb.Append("<h2>Limpieza y memoria</h2><p>").Append(H(limpieza)).Append(" <i>(lo borrado no se recupera; no era necesario)</i></p>");
            if (!s.Simular)
                sb.Append("<h2>¿Quieres deshacerlo?</h2><p>Todo lo marcado como <i>Aplicado</i> se puede deshacer desde <b>historial</b> en Savia. Respaldo de esta sesión:<br><code>")
                  .Append(H(s.ArchivoRespaldo)).Append("</code></p><p>Punto de restauración de Windows: ").Append(H(punto)).Append("</p>");
            sb.Append("<p class=\"sub\">Savia nunca desactiva Windows Defender, Windows Update, el firewall ni los antitrampas.</p></body></html>");
            File.WriteAllText(destino, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
