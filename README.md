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

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Autores y revisores: [miembros de saviapc](https://github.com/orgs/saviapc/people)
- Aprobadores de cada versión firmada: [propietarios de saviapc](https://github.com/orgs/saviapc/people?query=role%3Aowner)

Solo se firman los ejecutables compilados por GitHub Actions desde este repositorio, y cada versión se aprueba a mano antes de firmarse.

## Privacidad

Este programa no transfiere información a otros sistemas de la red, salvo que lo pida expresamente el usuario o la persona que lo instala o lo usa. La única conexión que hace es un ping a `1.1.1.1` al medir el PC, para calcular la latencia. Los respaldos y reportes se guardan solo en `C:\ProgramData\Savia`.

## Licencia

MIT
