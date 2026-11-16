# Savia

Optimiza Windows para jugar: quita lo que corre en segundo plano sin aportar nada y ajusta el sistema según tu equipo y para qué lo usas.

- Detecta tu hardware y aplica solo lo que tiene sentido para él.
- Explica cada cambio en un reporte.
- Todo se puede deshacer: crea un punto de restauración y guarda cada valor original.
- No toca Windows Defender, Windows Update, el firewall ni los antitrampas.

## Descarga

Descarga `Savia.exe` desde [Releases](../../releases). Funciona en Windows 10 y 11, sin instalar nada. Pide permisos de administrador porque cambia ajustes del sistema.

Cada versión se compila automáticamente desde este código con GitHub Actions. Para comprobar que el archivo no fue modificado, compara su SHA-256 con el de la release:

```
certutil -hashfile Savia.exe SHA256
```

O verifica su procedencia con GitHub CLI:

```
gh attestation verify Savia.exe --repo saviapc/savia
```

## Compilar

```
build.cmd
```

Usa el compilador de C# incluido en Windows (.NET Framework 4.8). El resultado queda en `dist\Savia.exe`.

## Licencia

MIT
