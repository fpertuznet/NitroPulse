# Verificación de NitroPulse 1.0.9

- Compilación Release: correcta, cero errores y cero advertencias.
- Publicación autocontenida Windows x64: correcta.
- Versión del ejecutable: 1.0.9.0 / producto 1.0.9.
- El recurso incluido NitroPulse.PawnIO_setup.exe contiene 3.410.960 bytes. Su SHA256 coincide exactamente con el instalador oficial 2.2.0 previamente verificado con firma Authenticode válida.
- El modo seguro se comprueba antes de examinar dependencias, elevar o instalar. Cancelar la autorización de Windows devuelve el modo seguro. Una versión antigua no se elimina automáticamente.
- La instalación de PawnIO y la lectura real del CPU se comprobaron con este mismo instalador en la versión 1.0.8.

La revisión automática rechazó la ejecución del comando --check-dependencies del nuevo ejecutable con el mensaje «blocked by policy», sin más detalle. Se verificó el recurso por lectura de la biblioteca compilada, sin ejecutar el arranque. El recorrido de instalación automática en un equipo sin PawnIO y la cancelación interactiva todavía no se han probado. Esta versión debe tratarse como candidata de prueba antes de publicarla para terceros.
