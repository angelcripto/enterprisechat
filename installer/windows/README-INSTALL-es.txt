==============================================================
  EnterpriseChat Server - Antes de instalar
==============================================================

Este instalador va a poner en marcha en tu servidor:

  * El servicio de Windows "EnterpriseChat", que arranca
    automaticamente con el sistema y escucha en el puerto 5080.
  * El binario auto-contenido (.NET 8) en
    C:\Program Files\EnterpriseChat\
  * La base de datos SQLite y los adjuntos en
    C:\Program Files\EnterpriseChat\data\
  * Una contrasena inicial para el usuario "admin", que se
    muestra UNA sola vez al final del asistente.

Requisitos minimos
------------------

  - Windows Server 2019, 2022 o Windows 10/11 (x64).
  - 2 vCPU + 2 GB RAM (mas en empresas de mas de 50 usuarios).
  - Puerto 5080 libre en el equipo. Si esta ocupado por otro
    proceso (por ejemplo, un servidor de desarrollo abierto),
    el servicio no podra arrancar. El instalador detectara el
    conflicto y te avisara.
  - Permisos de Administrador (UAC).

Lo que NO hace este instalador
------------------------------

  - No reconfigura tu firewall. Si quieres exponer el chat fuera
    de la maquina, abre el puerto 5080 en Windows Defender
    Firewall o configura un proxy inverso (IIS, nginx, Apache).
  - No envia datos a internet. EnterpriseChat funciona
    autoalojado; solo necesita una salida HTTPS cada 30 minutos
    para validar la licencia.
  - No instala el cliente de escritorio. El cliente WPF se
    distribuye aparte.

Despues de instalar
-------------------

  1. Abre el panel admin en http://<este-equipo>:5080/
  2. Inicia sesion con usuario "admin" y la contrasena que
     muestra el asistente al final.
  3. Cambia esa contrasena en el primer login.
  4. Reparte el cliente Windows entre tus empleados.

Soporte y documentacion: https://enterprisechat.es

Continua con "Siguiente" para empezar la instalacion.
