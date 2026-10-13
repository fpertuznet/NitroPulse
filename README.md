# NitroPulse 1.0.16 — monitor portable de recursos y estabilidad de red para Windows

## Descargar y usar

### Descargar desde Releases

Visita la página de [Releases](https://github.com/fpertuznet/NitroPulse/releases) y descarga el ejecutable publicado allí. Guárdalo en una carpeta propia y ábrelo. **No necesitas descargar el código fuente, descomprimir un ZIP ni instalar .NET.** Si otra versión está abierta, ciérrala con **Salir** desde los indicadores junto al reloj.

Si necesitas el paquete con documentación y licencias, también está disponible en [Releases](https://github.com/fpertuznet/NitroPulse/releases). Los archivos «Source code» son para desarrolladores, no para ejecutar la aplicación.

Proyecto de código abierto: https://github.com/fpertuznet/NitroPulse. Código propio bajo licencia MIT; las dependencias mantienen sus propias licencias.

La versión 1.0.16 incluye espera persistente para consultas de actualización y avisos de privacidad/licencias dentro del ejecutable. Consulta [PRIVACIDAD.md](PRIVACIDAD.md) y [PUBLICACION.md](PUBLICACION.md). NitroPulse es un proyecto independiente; Acer, Intel, NVIDIA y GitHub no lo certifican ni patrocinan.

Desde la versión 1.0.15, **Recursos** y **Red** están en pestañas separadas. Guarda etiquetas por identidad de red, incluye botones con iconos Lucide y una gráfica de ping en vivo con pérdidas y cambios de conexión. El informe específico para el operador contiene también la gráfica. Consulta [RED.md](RED.md).

Descarga las versiones portables desde https://github.com/fpertuznet/NitroPulse/releases. Las actualizaciones se descargan en segundo plano y, después de verificarse, aparece **Actualización disponible · Instalar** en el panel y el menú junto al reloj. No se instala hasta pulsar ese botón. Consulta ACTUALIZACIONES.md.

El nuevo icono aparece en el ejecutable y en la ventana principal. Iconos contiene el original PNG y el ICO con tamaños de 16 a 256 píxeles. Los indicadores de temperatura y uso junto al reloj conservan sus números y barras.

La descarga recomendada es NitroPulse.exe. Si eliges el ZIP opcional, extrae todo y abre Aplicacion/NitroPulse.exe.

- Si falta PawnIO, el programa pregunta si deseas instalar la edición oficial firmada 2.2. El instalador está incluido, sin necesidad de conexión a Internet.
- Al aceptar, Windows solicita autorización de administrador. El programa instala PawnIO y abre el monitor con lectura CPU directa.
- Si PawnIO 2.2 o posterior ya está instalado, lo reutiliza y solicita los permisos necesarios para el monitor. No reinstala el controlador.
- Si rechazas la instalación o cancelas la autorización de Windows, el monitor continúa en modo seguro. Si no hay otro proveedor de temperatura disponible, el CPU mostrará —.
- Si detecta una versión antigua de PawnIO, indica que debes actualizarla; no desinstala componentes automáticamente.
- La integridad del instalador incluido se verifica por SHA256 antes de extraerlo. Solo ejecuta la edición oficial, sin opciones sin restricciones.

NitroPulse sigue siendo portable; PawnIO es una instalación en Windows. La temperatura directa depende de la compatibilidad con el hardware. HWiNFO y MSI Afterburner no necesitan permanecer abiertos cuando la lectura directa funciona. Los registros siguen guardándose en AppData/Local/NitroPulse.

Para forzar modo seguro: NitroPulse.exe --safe-mode. El argumento --direct-sensors sigue disponible para diagnósticos; la apertura normal ahora prepara automáticamente las dependencias y los permisos.

Si hay otra versión de NitroPulse abierta, usa Salir en su menú junto al reloj antes de abrir esta versión. No activa el inicio automático de Windows por su cuenta.

## Organización local de versiones

Los ejecutables anteriores están agrupados por número en `Aplicacion/Versiones`. Los ZIP originales disponibles se conservan en `Paquetes` dentro de cada versión; los informes de validación están en `Documentacion/Versiones`. Consulta [VERSIONES.md](VERSIONES.md) para ver el inventario y las rutas.

La versión local más reciente es `Aplicacion/Versiones/1.0.16/NitroPulse.exe`. Las versiones anteriores se conservan en sus carpetas. Si tienes un acceso directo a una versión anterior, apunta a la versión que quieras usar. No se cambió el inicio automático de Windows. Las fuentes y pruebas actuales permanecen en sus rutas originales.

## Compilar

Instala el SDK .NET 8 o posterior. Desde esta carpeta:

    dotnet publish Fuente/NitroPulse.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o Aplicacion/Versiones/1.0.16

El código está en Fuente. Resources/PawnIO_setup.exe es el instalador oficial sin modificaciones.

Pruebas sin acceder a sensores ni instalar controladores (Windows): `dotnet run --project Tests/Tests.csproj -c Release`.

## Alcance de la validación

Compilación y publicación para Windows x64; comprobación del recurso incluido, rechazo de una copia alterada y prioridad del modo seguro. En esta PC, PawnIO ya está instalado: no se desinstaló para simular su ausencia. La instalación real con el mismo instalador y la lectura CPU se validaron en la versión anterior. El recorrido completo en una PC limpia sigue pendiente de validación antes de una publicación general.

## Componentes de terceros

LibreHardwareMonitorLib 0.9.6 (MPL-2.0): https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6

PawnIO 2.2.0 oficial firmado: https://pawnio.eu/ y https://github.com/namazso/PawnIO.Setup/releases

Código fuente de PawnIO: https://github.com/namazso/PawnIO

SHA256 del instalador incluido: 1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032. El instalador indica que puede redistribuirse sin modificaciones. Conserva los avisos y licencias de terceros al publicar. MIT no cambia las licencias de los componentes de terceros; consulta Licencias.
