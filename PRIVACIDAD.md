# Privacidad y permisos de NitroPulse

NitroPulse es un proyecto independiente de monitoreo para Windows. Las mediciones e informes se guardan en este equipo. No envía diagnósticos a GitHub, al desarrollador ni al operador de Internet automáticamente.

## Datos registrados

- Recursos: uso de CPU, GPU y RAM, temperaturas disponibles, disco, memoria comprometida, nombres de procesos y su consumo; fecha y hora, información del equipo y sensores.
- Red: interfaces, perfil de red y etiquetas, SSID/BSSID cuando están disponibles, direcciones IP, gateway y DNS, señal Wi-Fi, cambios de conexión, resultados y horarios de pruebas y rutas solicitadas.
- Actualizaciones: repositorio configurado, preferencias, última respuesta de Release y espera para la siguiente consulta.

La aplicación no registra contraseñas Wi-Fi, contenido de documentos, historial del navegador, contenido de tráfico ni argumentos de procesos. Un diagnóstico puede revelar identidad del equipo o las redes utilizadas: revísalo antes de compartirlo.

## Archivos locales y controles

Los datos están en %LOCALAPPDATA%\NitroPulse: Sesiones, Exportaciones, Actualizaciones y archivos de preferencias/etiquetas. No se eliminan automáticamente por antigüedad. Las copias automáticas y finales también son locales.

«Pausar registro» detiene el registro de recursos; «Pausar red» detiene las pruebas periódicas de red. Son controles independientes. Cerrar con «Salir» detiene el monitor; minimizar mantiene el monitoreo. Puedes abrir la carpeta de registros desde el menú junto al reloj. Para borrar tus informes, cierra la aplicación y elimina únicamente los archivos que ya no necesites. No publiques sesiones reales en repositorios públicos.

## Conexiones de diagnóstico

El monitoreo de red realiza pruebas cada cinco segundos al gateway, 1.1.1.1 y 8.8.8.8; usa ICMP, conexiones TCP 443 y consultas DNS de example.com a los DNS configurados. «Medir ruta» realiza una prueba adicional solicitada por el usuario. Los destinos y el proveedor de red pueden observar esas conexiones y la IP de origen. No es una prueba de velocidad ni una captura de tráfico. Puedes pausar las pruebas en la pestaña Red.

## Actualizaciones

Puedes desactivar la consulta automática desde Actualizaciones. Al consultar, NitroPulse contacta con la API de GitHub y los servidores de archivos de Releases, que reciben tu IP y los metadatos normales de la solicitud, incluida la versión del programa. No se incluye un token personal en el ejecutable. La respuesta válida se reutiliza durante seis horas y los límites o denegaciones conservan una espera entre reinicios.

Una versión nueva puede descargarse en segundo plano. La instalación requiere pulsar «Actualización disponible · Instalar». El tamaño, SHA256 y versión se verifican antes de instalar. El hash verifica integridad respecto a la Release, pero no reemplaza una firma digital ni certifica al autor. Las condiciones de privacidad de GitHub están en https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement.

## Permisos y dependencias

La lectura directa de sensores puede requerir administrador y PawnIO. La instalación asistida solicita aceptación y autorización de Windows; el modo seguro permite continuar sin lectura directa. PawnIO queda instalado en Windows aunque NitroPulse sea portable. No se modifican BIOS ni protecciones de Windows. Las herramientas externas conservan sus condiciones; no se eluden límites de licencia o períodos de prueba.

Las licencias y avisos incluidos pueden consultarse en esta misma pestaña, incluso al distribuir solo el ejecutable. La licencia MIT cubre el código propio; los componentes de terceros mantienen sus licencias.
