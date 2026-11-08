# NitroPulse 1.0.27 — monitor portable de recursos y red para Windows

NitroPulse es un monitor de escritorio para Windows: uso de CPU, GPU y RAM, temperaturas disponibles, estabilidad de red y registros locales. Es un proyecto independiente de código abierto; Acer, Intel, NVIDIA y GitHub no lo certifican ni patrocinan.

Código propio bajo licencia [MIT](LICENSE). Las dependencias conservan sus propias licencias; consulta [PRIVACIDAD.md](PRIVACIDAD.md) y la carpeta `Licencias`.

## Descargar

1. Abre la página de [Releases](https://github.com/fpertuznet/NitroPulse/releases).
2. Descarga el adjunto publicado para la versión que quieras (ejecutable portable o paquete con documentación).
3. Guárdalo en una carpeta propia y ábrelo.

No necesitas instalar .NET ni descomprimir el código fuente. Los archivos «Source code» de GitHub son para desarrollo, no para ejecutar la aplicación.

Si otra copia de NitroPulse ya está abierta, ciérrala con **Salir** desde el menú de los indicadores junto al reloj.

## Qué incluye

- **Recursos:** métricas en vivo, gráficas con historial (barra, arrastre, rueda o flechas; **En vivo** vuelve al final), tabla de procesos y pausa del registro.
- **Red:** pruebas periódicas de conectividad, gráfica de ping, etiquetas por red e informe exportable. Detalle en [RED.md](RED.md).
- **Configuración** (menú lateral): exportar ZIP de sesión, sensores detectados, comprobar actualizaciones, temas (Automático / Claro / Oscuro) y privacidad/licencias.
- **Bandeja:** Abrir panel, pausar/reanudar registro, Configuración, Iniciar con Windows y Salir.
- Indicadores junto al reloj para temperaturas y uso (CPU, GPU, RAM).

Los registros y preferencias se guardan en `%LOCALAPPDATA%\NitroPulse`. No se envían diagnósticos automáticamente.

## Actualizaciones

El repositorio de Releases está fijado en el código (`fpertuznet/NitroPulse`). Desde **Configuración → Actualizaciones** puedes comprobar si hay una versión nueva. Si hay actualización, se descarga en segundo plano tras verificarla; la instalación solo ocurre cuando pulsas **Actualización disponible · Instalar**. Detalle en [ACTUALIZACIONES.md](ACTUALIZACIONES.md).

## Sensores y administrador

- Si falta PawnIO, NitroPulse puede ofrecer instalar la edición oficial firmada incluida en el ejecutable (requiere tu aceptación y autorización de Windows).
- Con PawnIO 2.2 o posterior, puede solicitar permisos de administrador para lectura directa de temperatura CPU.
- Si rechazas la instalación o el UAC, continúa en modo seguro (la temperatura CPU puede mostrar —).
- El instalador incluido se verifica por SHA256 antes de usarlo. Solo se ejecuta la edición oficial, sin opciones sin restricciones.
- NitroPulse es portable; PawnIO, si se instala, queda en Windows. La compatibilidad de sensores depende del hardware.

Forzar modo seguro: `NitroPulse.exe --safe-mode`.

**Iniciar con Windows** (menú de bandeja) puede pedir autorización una sola vez al activarlo; después arranca minimizado sin pedir administrador en cada inicio.

## Compilar

Requiere SDK .NET 8 o posterior. Desde la raíz del repositorio:

```text
dotnet publish Fuente/NitroPulse.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o Aplicacion/Versiones/1.0.27
```

El código está en `Fuente`. `Resources/PawnIO_setup.exe` es el instalador oficial sin modificaciones.

Pruebas sin instalar controladores (Windows):

```text
dotnet run --project Tests/Tests.csproj -c Release
```

## Organización local

Ejecutables de prueba locales: `Aplicacion/Versiones`. Informes de validación: `Documentacion/Versiones`. Inventario en [VERSIONES.md](VERSIONES.md). Antes de publicar, revisa [PUBLICACION.md](PUBLICACION.md): no subir sesiones reales, secretos ni ejecutables al historial de Git; los binarios van solo como adjuntos de Release.

## Validación

Compilación para Windows x64; comprobación de integridad del instalador incluido y prioridad del modo seguro. La temperatura directa y la instalación de PawnIO dependen del equipo. Un recorrido completo en PC limpia puede quedar pendiente según la Release.

## Componentes de terceros

- LibreHardwareMonitorLib 0.9.6 (MPL-2.0): https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6
- PawnIO 2.2.0 oficial firmado: https://pawnio.eu/ · https://github.com/namazso/PawnIO.Setup/releases
- Código fuente de PawnIO: https://github.com/namazso/PawnIO
- Iconos Lucide: ver `Licencias/Lucide.txt`

SHA256 del instalador PawnIO incluido: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`. Ese hash comprueba integridad respecto al instalador embebido; no es una firma digital ni una certificación de GitHub. Conserva los avisos de terceros al redistribuir.
