# INSTALL.md — Guía de instalación de FAControl

> Para quien instala el sistema en la PC del cliente (Yuber o un técnico).
> Tiempo estimado: 20–30 minutos. Windows 10 (2004) u 11, 64 bits.

---

## 1. Requisitos

| Componente | Versión | Nota |
|---|---|---|
| Windows | 10 (build 19041) o 11, x64 | |
| MySQL Community Server | 8.0 o superior | Única dependencia externa |
| .NET | — | NO hace falta: el instalador incluye todo |

---

## 2. Instalar MySQL Server

1. Descargar **MySQL Community Server 8.x** (https://dev.mysql.com/downloads/installer/).
2. En el instalador elegir **Server only**.
3. Configuración:
   - Config Type: **Development Computer** (consume menos RAM)
   - Puerto: **3306** (default)
   - Authentication: **Use Strong Password Encryption**
   - Root password: elegir una y **guardarla en un lugar seguro**
   - Windows Service: dejar **MySQL80**, arranque automático ✔
4. Verificar que el servicio corre: `services.msc` → MySQL80 → *En ejecución*.

## 3. Crear la base de datos

Abrir **MySQL Command Line Client** (o `mysql -uroot -p`) y ejecutar los scripts
de la carpeta `scripts\db\` incluida junto al instalador:

```sql
SOURCE C:/ruta/a/scripts/db/001_create_schema.sql;
```

> `002_seed_data.sql` es SOLO para desarrollo (clientes de prueba) — **no ejecutarlo donde el cliente**.

> 💡 **Alternativa (desde v1.0.1):** si la app arranca y la base de datos no existe,
> ella misma ofrece crearla con un clic. Para eso la cadena de conexión debe tener
> un usuario con permiso CREATE (root funciona; el usuario dedicado del paso 4 no,
> por diseño). En instalaciones donde el cliente, lo recomendado sigue siendo este
> paso manual y dejar configurado el usuario dedicado.

## 4. Crear el usuario dedicado (nunca usar root)

1. Abrir `scripts\db\003_crear_usuario_dedicado.sql` en un editor.
2. Reemplazar `CAMBIAR-ESTA-CLAVE` por una contraseña real.
3. Ejecutarlo como root:

```sql
SOURCE C:/ruta/a/scripts/db/003_crear_usuario_dedicado.sql;
```

## 5. Instalar FAControl

1. Ejecutar **`FAControl_Setup_x.x.x.exe`** y seguir el asistente.
2. Se instala en `C:\Program Files\FAControl` con acceso directo en el escritorio.

## 6. Configurar la conexión

Editar (como administrador) el archivo:

```
C:\Program Files\FAControl\FAControl.App.dll.config
```

y ajustar la cadena de conexión con el usuario dedicado del paso 4:

```xml
<connectionStrings>
  <add name="FAControlDb"
       connectionString="Server=localhost;Port=3306;Database=facontrol_db;Uid=facontrol;Pwd=LA-CLAVE-DEL-PASO-4;" />
</connectionStrings>
```

## 7. Primer arranque

1. Abrir FAControl (acceso directo del escritorio).
2. Aparece el asistente **"Crear cuenta inicial"**: el cliente elige su usuario,
   nombre y una contraseña de mínimo 8 caracteres. **Esa cuenta es única** — anotarla.
3. Entrar y verificar: crear un cliente de prueba, un préstamo pequeño y borrarlos/cancelarlos.

## 8. Después de instalar (checklist)

- [ ] Configuración → **Respaldar ahora** → guardar un primer respaldo y verificar que el .sql se creó
- [ ] Configuración → activar el **export automático a Excel** si el cliente lo quiere (elegir carpeta, ej. Documentos\FAControl)
- [ ] Configuración → elegir el **tamaño de texto** cómodo para el cliente
- [ ] Dejar el **aviso de pagos vencidos** activado (viene activado por defecto)
- [ ] Entregar el **MANUAL.pdf / MANUAL.md** al cliente
- [ ] Acordar rutina de respaldo (recomendado: semanal, a USB o nube)

## 9. Migrar a otra PC (cuando el cliente cambie de equipo)

1. En la PC vieja: Configuración → **Respaldar ahora** → guardar el `.sql` en un USB.
2. En la PC nueva: seguir esta guía completa (pasos 2–6).
3. Configuración → **Restaurar desde archivo…** → elegir el `.sql` del USB.
4. Listo: todos los clientes, préstamos, pagos e historial quedan idénticos.

## 10. Problemas comunes

| Síntoma | Causa probable | Solución |
|---|---|---|
| "No se pudo conectar" al abrir | Servicio MySQL80 detenido | `services.msc` → iniciar MySQL80 |
| "Access denied for user" | Contraseña del App.config no coincide | Revisar paso 6 |
| Respaldo falla: "no se encontró mysqldump" | MySQL instalado sin agregar al PATH | La app lo busca sola en `Program Files\MySQL`; verificar que exista `bin\mysqldump.exe` |
| Ventana de login cortada | Resolución muy baja | Mínimo 1280×720 |
| No aparece el aviso de vencidos | Está apagado o los clientes fueron silenciados | Configuración → Aviso de pagos vencidos |

---
*FAControl · desarrollado por Yuber Santana · soporte según contrato de mantenimiento.*
