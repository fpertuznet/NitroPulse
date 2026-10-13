# NitroPulse — guía de uso

La guía actual está en README.md. Para localizar cada versión local y sus informes, consulta VERSIONES.md. Para actualizaciones automáticas, consulta ACTUALIZACIONES.md. NitroPulse es código abierto bajo MIT; las dependencias conservan sus licencias.

Versión 1.0.16 solo local: abre Aplicacion/Versiones/1.0.16/NitroPulse.exe. La pestaña «Privacidad y licencias» contiene los avisos integrados. Consulta PRIVACIDAD.md y PUBLICACION.md antes de compartir diagnósticos o publicar una versión.

El nuevo icono aparece en el ejecutable y en la ventana principal. Iconos contiene el original PNG y el ICO con tamaños de 16 a 256 píxeles. Los indicadores de temperatura y uso junto al reloj conservan sus números y barras.

Extrae el ZIP y abre Aplicacion/NitroPulse.exe. No necesita .NET instalado ni un acceso directo con rutas fijas.

- Si falta PawnIO, el programa pregunta si deseas instalar la edición oficial firmada 2.2. El instalador está incluido, sin necesidad de conexión a Internet.
- Al aceptar, Windows solicita autorización de administrador. El programa instala PawnIO y abre el monitor con lectura CPU directa.
- Si PawnIO 2.2 o posterior ya está instalado, lo reutiliza y solicita los permisos necesarios para el monitor. No reinstala el controlador.
- Si rechazas la instalación o cancelas la autorización de Windows, el monitor continúa en modo seguro. Si no hay otro proveedor de temperatura disponible, el CPU mostrará —.
- Si detecta una versión antigua de PawnIO, indica que debes actualizarla; no desinstala componentes automáticamente.
- La integridad del instalador incluido se verifica por SHA256 antes de extraerlo. Solo ejecuta la edición oficial, sin opciones sin restricciones.

NitroPulse sigue siendo portable; PawnIO es una instalación en Windows. La temperatura directa depende de la compatibilidad con el hardware. HWiNFO y MSI Afterburner no necesitan permanecer abiertos cuando la lectura directa funciona. Los registros siguen guardándose en AppData/Local/NitroPulse.

Para forzar modo seguro: NitroPulse.exe --safe-mode. El argumento --direct-sensors sigue disponible para diagnósticos; la apertura normal ahora prepara automáticamente las dependencias y los permisos.

Si hay otra versión de NitroPulse abierta, usa Salir en su menú junto al reloj antes de abrir esta versión. No activa el inicio automático de Windows por su cuenta.

## Compilar

Instala el SDK .NET 8 o posterior. Desde esta carpeta:

    dotnet publish Fuente/NitroPulse.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o Aplicacion

El código está en Fuente. Resources/PawnIO_setup.exe es el instalador oficial sin modificaciones.

## Alcance de la validación

Compilación y publicación para Windows x64; comprobación del recurso incluido, rechazo de una copia alterada y prioridad del modo seguro. En esta PC, PawnIO ya está instalado: no se desinstaló para simular su ausencia. La instalación real con el mismo instalador y la lectura CPU se validaron en la versión anterior. El recorrido completo en una PC limpia sigue pendiente de validación antes de una publicación general.

## Componentes de terceros

LibreHardwareMonitorLib 0.9.6 (MPL-2.0): https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6

PawnIO 2.2.0 oficial firmado: https://pawnio.eu/ y https://github.com/namazso/PawnIO.Setup/releases

Código fuente de PawnIO: https://github.com/namazso/PawnIO

SHA256 del instalador incluido: 1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032. El instalador indica que puede redistribuirse sin modificaciones. Conserva los avisos y licencias de terceros al publicar.
