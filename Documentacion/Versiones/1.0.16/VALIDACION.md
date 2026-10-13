# Validación local de NitroPulse 1.0.16

Fecha: 12 de octubre de 2026. Proyecto: C:\Users\info\Documents\NitroPulse.

## Cambios de esta versión

- README local sin enlace directo al ejecutable; conserva la entrada a Releases.
- Preferencias y respuesta de actualización con caché de seis horas. Esperas de API y denegaciones guardadas entre reinicios; sin tokens personales ni reintentos inmediatos.
- Reutilización de descargas completas cuyo tamaño y SHA256 coinciden, con nueva validación del ejecutable.
- Pestaña Privacidad y licencias en el panel y acceso desde el menú de bandeja. Avisos integrados en el ejecutable, disponibles sin red.
- Confirmación antes de exportar informes con datos personales de recursos o redes.
- Exclusiones de archivos locales de diagnóstico y guía de revisión previa a publicar.
- El validador de versión incluye ahora los avisos y licencias integrados en su huella.

## Pruebas ejecutadas

- Tests/Tests.csproj: 14 comprobaciones de versiones y enlaces; descarga, hash incorrecto, truncamiento, redirección ajena, versión incorrecta, ZIP seguro y reutilización sin red; caché persistente, vencimiento, Retry-After, cuota y denegación mediante respuestas simuladas.
- Tests/Notices/Notices.csproj: siete avisos integrados (privacidad, MIT y cinco archivos de Licencias), textos accesibles de solo lectura.
- Tests/Network/NetworkTests.csproj: 39 pruebas de estabilidad, segmentación, DNS, informe y privacidad.
- Tests/Bars/Bars.csproj: 147 comprobaciones de píxeles.
- Tests/NetworkUI/NetworkUI.csproj: renderizado con datos sintéticos a 1100 y 860 píxeles; exportación de gráfica PNG y etiquetas, iconos y redimensionamiento. Capturas en Diagnosticos/Validacion-1.0.16. La captura de 860 píxeles se inspeccionó visualmente.
- Compilación Release correcta, sin advertencias ni errores. Portable autocontenido Windows x64 generado.
- Validador de versión pasó tras generar el portable. README comprobado sin enlaces directos a ejecutables; exclusión Git comprobada para portable, diagnósticos, sesiones y .env. No se encontraron CSV, JSONL, DMP, .env o logs ya rastreados mediante la consulta de nombres usada.

## Artefacto

Ejecutable: Aplicacion/Versiones/1.0.16/NitroPulse.exe

FileVersion: 1.0.16.0

SHA256: 6BD27F13E0E1ECE5A2C4150164A9D01DB937563CC60B287F4DF9C08F82FD5825

El sufijo Git del ProductVersion procede del HEAD local de compilación. Esta versión incluye cambios locales no confirmados: ese hash no identifica por sí solo todo el código de la compilación. La huella de Fuente/version-publicada.json registra las fuentes y recursos incluidos. No es una firma digital.

## Límites y pendientes

No se inició esta versión del monitor sobre los sensores reales ni se instalaron controladores. No se probó un ciclo real entre dos Releases ni la instalación en un equipo limpio. Los diálogos de exportación se compilaron; no se recorrieron manualmente sobre sesiones reales. El test de avisos verifica los textos y recursos del panel, sin afirmar una revisión visual completa del monitor.

La versión conserva los componentes y avisos existentes; no representa una auditoría jurídica completa de todas las dependencias transitivas ni garantiza una resolución de GitHub. Antes de una distribución pública corresponde revisar las condiciones concretas de redistribución y la disponibilidad de las fuentes requeridas.

Todo quedó local. No se subieron commits, Releases ni otros cambios a GitHub. No se modificaron BIOS, protecciones de Windows, inicio automático ni registros de diagnóstico existentes. Las versiones anteriores permanecen en sus carpetas.
