# Revisión previa a publicar NitroPulse

Esta lista es un control propio del proyecto. No certifica cumplimiento jurídico ni garantiza una decisión de GitHub. La guía general está fuera del repositorio, en Documents/GUIA-POLITICAS-GITHUB-2026-10-12.md.

El 13 de octubre de 2026 Fabian informó que GitHub retiró la restricción y autorizó publicar la versión 1.0.16. El README debe conservar la entrada a la página de Releases sin reintroducir el enlace directo al ejecutable retirado por indicación de Soporte. Si GitHub vuelve a conceder acceso temporal exclusivamente para corregir un problema, limitar las publicaciones a esa corrección.

- Revisar el diff y los archivos preparados; nunca añadir todo el directorio sin inspección.
- Excluir sesiones, diagnósticos reales, CSV de mediciones, volcados, secretos y copias de seguridad. .gitignore no elimina archivos ya rastreados ni datos del historial.
- Publicar ejecutables como archivos de una Release asociada a código y versión; no almacenar compilaciones en Git.
- Revisar el origen, licencia y permiso de redistribución de dependencias; conservar sus avisos y fuentes correspondientes. La inclusión de textos en el ejecutable no sustituye otras obligaciones de cada licencia.
- Compilar y probar; diferenciar pruebas simuladas de validaciones reales y documentar pendientes.
- Verificar SHA256 del archivo final; no presentar el hash como firma digital o aprobación de GitHub.
- No afirmar asociación con Acer, Intel, NVIDIA o GitHub.
- Revisar permisos, pruebas de red, privacidad y esperas de API; no eludir controles ni cuotas.
- Publicar únicamente después de tener autorización y alcance adecuados de Fabian y acceso autorizado por GitHub.

Políticas: https://docs.github.com/en/site-policy
Releases: https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases
API: https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api
