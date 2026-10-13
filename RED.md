# Estabilidad de red — NitroPulse 1.0.16

Descarga NitroPulse desde la página de Releases y selecciona la pestaña **Red** (o el acceso Red / Internet del menú junto al reloj). En este proyecto local el ejecutable está en `Aplicacion/Versiones/1.0.16/NitroPulse.exe`. **Recursos** permanece en su propia pestaña. El monitoreo comienza al abrir NitroPulse, sigue al minimizarlo y guarda datos cada 5 segundos. **Pausar red** detiene estas pruebas sin detener los sensores; **Pausar registro** de Recursos solo afecta los registros de recursos.

## Qué mide

- Wi-Fi: nombre SSID, punto de acceso BSSID y calidad de señal informada por Windows. Si Windows no permite consultarlos, muestra que no están disponibles sin cambiar permisos.
- Interfaces activas, direcciones locales, gateways y DNS. La ruta de Windows hacia cada destino permite identificar Ethernet o una VPN; no se elige simplemente la primera interfaz.
- Ping al siguiente router IPv4 de la ruta y a 1.1.1.1 / 8.8.8.8, con límite de 1,2 segundos.
- Conexión TCP al puerto 443 de ambas IP, con límite de 2 segundos.
- Consulta DNS sintética `example.com` directamente a hasta dos DNS configurados en la interfaz principal, con límite de 2 segundos. No usa el caché DNS de Windows y no equivale al DNS/DoH usado por cada navegador.
- Por destino: intentos, respuestas, fallos, porcentaje de fallos, latencia media/máxima y variación media entre respuestas consecutivas. Episodios orientativos cuando fallan ambos TCP o todos los DNS probados con TCP disponible.

Son comprobaciones ligeras, no una prueba de velocidad ni un análisis de tráfico de otras aplicaciones. No lee contraseñas, documentos, páginas visitadas ni paquetes de otros procesos. No modifica IP, DNS, Wi-Fi, firewall, controlador ni BIOS.

## Si intercambias redes

**Conexiones y cambios** conserva tramos independientes. Cambios de SSID, BSSID, IP, gateway, DNS, interfaz o ruta abren otro tramo. La señal variable no abre un tramo nuevo. Si la red cambia durante una prueba, se descartan sus resultados para no atribuirlos a otra conexión.

Escribe una etiqueta (por ejemplo, «Casa - operador A») y pulsa **Guardar etiqueta**. Se guarda en `%LOCALAPPDATA%/NitroPulse/red_etiquetas.json` y se recupera al reconectar o reiniciar. Se aplica también a otros tramos de esa misma identidad dentro de la sesión; no cambia el nombre de la conexión de Windows ni reescribe los CSV originales.

Se usa el identificador del perfil de Windows cuando está disponible. Si no lo está, se usa SSID para Wi-Fi, o interfaz/gateway para Ethernet. Se informa ese fallback en Datos de la red. Redes diferentes con identificadores de fallback idénticos no pueden distinguirse con certeza; necesitan un perfil de Windows identificable. En la primera apertura se recuperan etiquetas recientes de los registros de la 1.0.14 y se vinculan al perfil activo sin modificar esos registros.

Si la conexión cambia mientras escribes la etiqueta, se pide revisar la red actual antes de guardarla para no vincularla a otra red.

Pausas, suspensión y huecos de observación mayores de 15 segundos no se contabilizan como caídas de Internet. Un cambio muy breve entre muestras puede no identificarse con su SSID exacto.

## Gráfica en vivo

Conserva hasta 30 minutos (360 muestras): ping al router, Cloudflare y Google, más media, máximo, variación y porcentaje de ecos sin respuesta del tramo actual hacia 1.1.1.1. Una cruz roja indica eco sin respuesta; no se dibuja como 0ms ni se une la curva a través de pérdidas. Las líneas tampoco se unen entre tramos ni a través de pausas.

Las marcas ámbar señalan cambios/reconexiones y muestran la etiqueta de la red. El sombreado rojo señala ausencia de enlace o fallo simultáneo de las dos conexiones TCP; si no hay ruta IPv4 se muestra una advertencia ámbar, no una conclusión sobre IPv6. Pasar el puntero muestra el detalle de la muestra más cercana en el tiempo. La interfaz se actualiza cada segundo y las pruebas se toman cada cinco segundos.

## Informe para el operador

1. Deja NitroPulse abierto durante el problema; idealmente registra una franja con fallos y otra sin fallos.
2. En **Red**, pulsa **Medir ruta** si quieres guardar un trazado orientativo de hasta 20 saltos hacia 1.1.1.1. Un salto que no contesta NO demuestra una avería allí.
3. Pulsa **Exportar para operador**. Se crea un ZIP en `%LOCALAPPDATA%/NitroPulse/Exportaciones/Red`.
4. Revisa el ZIP antes de enviarlo. Contiene SSID/BSSID e IP/router/DNS (pueden identificar redes privadas), pero NO registros de procesos ni contraseñas. La aplicación no envía el informe automáticamente.

Incluye `Informe_red.txt`, `red_mediciones.csv`, `red_eventos.csv`, `red_conexiones.jsonl`, `red_nombres.json`, `grafica_ping.png` y, si se midió, `red_ruta.txt`. La gráfica es una instantánea al exportar; el CSV incluye todo el registro de la sesión. Los nombres históricos del CSV conservan lo registrado entonces; el informe y red_nombres.json muestran las etiquetas actuales. Los registros originales quedan junto a los demás archivos de la sesión. Las horas ISO incluyen la zona del reloj de Windows; revisa que el reloj sea correcto.

## Interpretación prudente

Router responde y ambos TCP fallan: posible problema de salida IPv4 o filtrado. TCP funciona pero todos los DNS probados fallan: posible problema de DNS/UDP o su filtrado. ICMP falla pero TCP responde: no se declara caída general. VPN, proxy, portal cautivo y filtros pueden cambiar el resultado.

El diagnóstico no prueba navegación HTTPS, velocidad ni conectividad IPv6 general; tampoco demuestra responsabilidad del operador. La duración de episodios se aproxima con muestras cada 5 segundos. Aporta evidencia temporal para que el operador la contraste con sus equipos.

## Pruebas del proyecto

`dotnet run --project Tests/Network/NetworkTests.csproj -c Release` usa casos simulados de cambio, pérdida y DNS. Añadir `-- --live` ejecuta tres mediciones reales limitadas, sin sensores de hardware. Las pruebas de interfaz están en Tests/NetworkUI.

Implementación basada en las APIs de Windows: [GetBestInterface](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getbestinterface), [WLAN_CONNECTION_ATTRIBUTES](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/ns-wlanapi-wlan_connection_attributes) y [Ping.SendPingAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.ping.sendpingasync?view=net-8.0).
