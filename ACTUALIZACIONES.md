# Actualizaciones (1.0.16 local)

Las respuestas válidas se guardan localmente y se reutilizan durante seis horas, también entre reinicios y comprobaciones manuales. Se respeta Retry-After y la cuota/restablecimiento de GitHub. Una denegación de acceso se distingue de un límite y suspende nuevas consultas durante al menos seis horas. Los fallos de conexión esperan al menos un minuto; no hay reintentos inmediatos. El usuario puede desactivar las consultas automáticas. Consulta PRIVACIDAD.md.

En NitroPulse, abre **Actualizaciones** desde el panel o el menú junto al reloj. Escribe tu repositorio público de GitHub como `usuario/NitroPulse` y pulsa **Guardar y comprobar**. Puedes desactivar las consultas automáticas. Las preferencias se guardan en `%LOCALAPPDATA%/NitroPulse/actualizaciones.json`.

El repositorio oficial viene configurado como `fpertuznet/NitroPulse`. La aplicación consulta la última Release estable al abrir y cada seis horas. Si hay una versión mayor, descarga silenciosamente el portable. Solo cuando tamaño, SHA256, identidad y versión del ejecutable coinciden aparece **Actualización disponible · Instalar**, tanto en el panel como en el menú junto al reloj.

Al pulsar Instalar, guarda la sesión, se cierra de forma ordenada y un ayudante reemplaza únicamente el ejecutable. Conserva el anterior en la misma carpeta como `NitroPulse.exe.anterior-FECHA.bak` y abre la nueva versión. No modifica registros, preferencias ni controladores. El resultado se guarda junto a la descarga en `%LOCALAPPDATA%/NitroPulse/Actualizaciones`. Si el reemplazo falla, avisa y conserva la versión anterior. La instalación puede fallar si la carpeta no es escribible o la política del equipo impide ejecutar el ayudante de PowerShell; no se desactivan esas protecciones.

Sin conexión o si la descarga falla, los sensores continúan funcionando. No aparece ningún botón de instalación para descargas parciales. La comprobación manual muestra los errores; la automática permanece silenciosa. Al reiniciar se reutiliza una descarga completa cuyo tamaño y SHA256 coincidan con el adjunto de la Release; se vuelve a validar el ejecutable antes de ofrecer la instalación.

Para publicar: etiqueta estable `v1.0.13` y adjunto `NitroPulse-1.0.13-portable.zip` con un único ejecutable en `Aplicacion/NitroPulse.exe` (o en la raíz), o un adjunto `NitroPulse.exe`. GitHub debe proporcionar su digest SHA256. No se aceptan enlaces fuera de GitHub ni scripts del ZIP. Publica el portable autocontenido de Windows x64, no una compilación dependiente de otros archivos.

Para distribuir el repositorio preconfigurado, establece `GitHubRepository` en `Fuente/NitroPulse.csproj`.

## Validación al compilar

La versión se define únicamente en `Fuente/NitroPulse.csproj`. El título, diagnóstico y registros toman ese mismo valor.

Después de cada publicación local exitosa (`dotnet publish`), se registra una huella del código y recursos en `Fuente/version-publicada.json`. Conserva este archivo en GitHub. Si después cambias código o recursos sin incrementar `<Version>`, la compilación se detiene con un mensaje que solicita subirla. Tampoco permite versiones menores que la última publicada. Recompilar sin cambios sigue permitido.

Primero cambia `<Version>`, luego compila y publica. No borres el registro para evitar la validación. Esta protección actúa sobre el código y recursos; no obliga a subir versión por cambios de documentación.
