# CLAUDE.md — FAControl

> Guía de proyecto para Claude Code. Este archivo se lee al inicio de cada sesión y define el alcance, arquitectura y reglas específicas de **FAControl**. Se complementa con el `CLAUDE.md` global de freelance (reglas de seguridad financiera, soft deletes, auditoría, etc.) y con el `DESIGN.md` del proyecto (paleta, tipografía, componentes visuales).

---

## 1. Identidad del proyecto

- **Nombre:** FAControl
- **Tipo:** Aplicación de escritorio para Windows
- **Propósito:** Gestión integral de préstamos para prestamistas independientes en República Dominicana
- **Usuario final:** Una sola persona (el prestamista dueño del negocio)
- **Modelo de operación:** 100% local, sin dependencia de internet
- **Idioma:** Español dominicano en toda la UI

**Diferencia clave con POS-400:** este sistema es **mono-usuario**. NO tiene roles ni permisos granulares. Solo tiene autenticación de login para evitar que terceros abran la aplicación físicamente. Toda la lógica de `rol`, `permiso`, `rol_permiso`, `usuario_permiso` del POS-400 debe omitirse.

---

## 2. Stack técnico

| Capa | Tecnología |
|---|---|
| Runtime | .NET 8 (LTS) |
| UI | WPF (Windows Presentation Foundation) con XAML |
| Patrón | MVVM estricto (Model-View-ViewModel) |
| MVVM helpers | `CommunityToolkit.Mvvm` (ObservableObject, RelayCommand, ObservableProperty) |
| Base de datos | MySQL 8.0+ instalado localmente |
| ADO / ORM | `MySqlConnector` (más rápido y moderno que MySql.Data) |
| Hashing de contraseñas | `BCrypt.Net-Next` |
| Logging | `Serilog` con sink a archivo local rotativo |
| Impresión | `System.Printing` para tickets 80mm y `PdfSharp` para reportes PDF |
| Gráficos del dashboard | `LiveChartsCore.SkiaSharpView.WPF` |
| Testing | `xUnit` + `FluentAssertions` |

**Nullable reference types:** habilitado (`<Nullable>enable</Nullable>`).
**Namespaces:** file-scoped (`namespace FAControl.Views;`).
**Async/await:** obligatorio en toda operación de base de datos y de I/O.

---

## 3. Proyectos de referencia

Antes de escribir código nuevo, consultar los siguientes proyectos para reutilizar patrones probados y evitar reinventar soluciones:

### POS-400 (referencia principal)
Ruta esperada: hermano en el workspace, buscar carpeta `POS-400` o `MiPOSCSharpMySQL`.

Patrones a reutilizar:
- Separación por carpetas: `Modelos/`, `Controladores/`, `Configuracion/`
- Clase `CConexion` para centralizar la cadena de conexión (adaptar a `App.config` externo)
- Patrón de `SesionActual` estático para el usuario autenticado (simplificar: solo `Id`, `Username`, `Nombre`, `LoginAt`)
- Uso consistente de parámetros SQL `@param` en cada query
- Estructura de impresión de ticket (imagen tipo 80mm) del `FormVentas`

**Patrones a NO reutilizar del POS-400:**
- Ningún código relacionado con `rol`, `permiso`, `rol_permiso`, `usuario_permiso`
- Contraseñas en texto plano (cambiar a BCrypt sin excepción)
- Ruta hardcodeada del logo (usar `AppContext.BaseDirectory`)
- Formularios Windows Forms (todo debe ser WPF/XAML)

### PTV-300 (referencia secundaria si está disponible)
Ruta esperada: buscar carpeta `PTV-300` en el workspace.

**Antes de escribir cualquier módulo, Claude Code debe verificar si PTV-300 existe en el workspace. Si existe, leer su estructura y documentar en un archivo `PTV300-PATTERNS.md` los patrones reutilizables. Si no existe, continuar solo con POS-400 como referencia y anotar en `BLOCKERS.md` que PTV-300 no fue encontrado.**

### Referencia SQL del POS-400
El script `BDPOS-400.sql` muestra el patrón de:
- Tabla `usuario` con seguimiento de sesión activa
- Tabla `factura` con `usuario_id` como quién emitió
- Uso de triggers para sincronización automática

En FAControl, replicar la simplicidad de `usuario` y la trazabilidad por `usuario_id`, pero adaptado a las entidades del dominio (cliente, prestamo, cuota, pago).

---

## 4. Arquitectura de solución

```
FAControl/
├── FAControl.sln
├── src/
│   ├── FAControl.App/           → punto de entrada, App.xaml, MainWindow
│   ├── FAControl.Views/         → todas las Views XAML (UserControls por módulo)
│   ├── FAControl.ViewModels/    → ViewModels correspondientes a cada View
│   ├── FAControl.Models/        → entidades de dominio (records/classes)
│   ├── FAControl.Services/      → lógica de negocio (Amortizacion, Cobro, Auditoria)
│   ├── FAControl.Data/          → repositorios, acceso a MySQL
│   ├── FAControl.Common/        → helpers, converters, constantes, extensions
│   └── FAControl.Printing/      → generación de recibos y reportes
├── tests/
│   ├── FAControl.Services.Tests/
│   └── FAControl.Data.Tests/
├── scripts/
│   └── db/
│       ├── 001_create_schema.sql
│       ├── 002_seed_data.sql
│       └── 999_rollback.sql
├── docs/
│   ├── DESIGN.md                   → guía de diseño visual (ya generada)
│   ├── SCHEMA.md                   → documentación de tablas y relaciones
│   └── AMORTIZATION.md             → explicación matemática de amortización
├── CLAUDE.md                       → este archivo
└── BLOCKERS.md                     → bloqueos actuales (creado por Claude Code cuando aplique)
```

**Regla de dependencias (importante):**

```
Views → ViewModels → Services → Data → Models
Common es utilizado por todas.
Ninguna dependencia inversa está permitida.
```

Views NUNCA acceden directamente a Services o Data. ViewModels NUNCA acceden directamente a Data. Toda operación de negocio pasa por un Service.

---

## 5. Esquema de base de datos

Base de datos: `facontrol_db`. Todas las tablas usan `InnoDB`, `utf8mb4_unicode_ci`.

### Tablas principales

| Tabla | Propósito |
|---|---|
| `usuario` | Cuenta única del prestamista (id, username, password_hash, nombre, activo, created_at, last_login_at) |
| `sesion` | Registro de logins/logouts (id, usuario_id, login_at, logout_at, ip_local) |
| `cliente` | Personas a las que se les presta (id, cedula, nombre, apellido, telefono, direccion, email, notas, created_at, updated_at, deleted_at) |
| `prestamo` | Contrato de préstamo (id, cliente_id, monto_capital, tasa_interes, plazo_cuotas, modalidad, metodo_amortizacion, fecha_inicio, estado, notas, created_at, updated_at) |
| `cuota` | Cada cuota individual del préstamo (id, prestamo_id, numero_cuota, fecha_vencimiento, capital, interes, monto_total, saldo_despues, estado, created_at, updated_at) |
| `pago` | Abono a una cuota (id, cuota_id, numero_recibo, fecha_pago, monto_pagado, metodo_pago, notas, created_at, updated_at, deleted_at) |
| `auditoria` | Log de operaciones (id, usuario_id, entidad, entidad_id, accion, descripcion, ip_local, timestamp) |

### Tipos de datos obligatorios

- **Dinero:** `DECIMAL(15,2)` sin excepción. Nunca `FLOAT`, `DOUBLE` o `REAL`.
- **Fechas:** `DATETIME` almacenado en UTC. Convertir a hora local (`America/Santo_Domingo`) solo al mostrar en UI.
- **IDs:** `BIGINT UNSIGNED AUTO_INCREMENT`.
- **Textos cortos:** `VARCHAR(N)` con N justificado.
- **Notas largas:** `TEXT`.
- **Estados:** `ENUM(...)` explícito, nunca cadenas libres.

### Enumeraciones

```sql
prestamo.modalidad         → ENUM('diaria','semanal','quincenal','mensual')
prestamo.metodo_amortizacion → ENUM('frances','cuota_fija')
prestamo.estado            → ENUM('activo','pagado','cancelado')
cuota.estado               → ENUM('pendiente','pagada','vencida','en_mora')
pago.metodo_pago           → ENUM('efectivo','transferencia','cheque','otro')
auditoria.accion           → ENUM('crear','modificar','eliminar','consultar','login','logout')
```

### Restricciones críticas

- `cliente.cedula` es `UNIQUE` (una persona una sola vez)
- `pago.numero_recibo` es `UNIQUE` (nunca duplicar recibos)
- Todas las columnas `usuario_id` tienen `FOREIGN KEY` con `ON DELETE RESTRICT`
- `deleted_at IS NULL` en todos los queries de lectura (soft delete)

---

## 6. Autenticación

**Simplificación respecto a POS-400:** un solo usuario, sin roles, sin permisos, sin recuperación de contraseña por email.

Flujo:
1. Al primer arranque, si la tabla `usuario` está vacía, mostrar wizard de "Crear cuenta inicial" que pide `username`, `nombre` y contraseña (mínimo 8 caracteres). Se guarda el hash BCrypt (cost 12).
2. En arranques posteriores, mostrar login. Validar contra el hash.
3. Al login exitoso, insertar registro en `sesion` con `login_at = UTC_TIMESTAMP()`, actualizar `usuario.last_login_at`, y guardar en `SesionActual` estático (`Id`, `Username`, `Nombre`, `LoginAt`, `SesionId`).
4. Al cerrar sesión o cerrar la app, actualizar `sesion.logout_at`.
5. Toda operación mutable (crear/modificar/eliminar) inserta un registro en `auditoria` con `usuario_id = SesionActual.Id`.

**Cambio de contraseña:** disponible en Configuración. Requiere ingresar contraseña actual + nueva contraseña dos veces.

**No implementar:** "recordar contraseña", recuperación por correo, autenticación biométrica, 2FA. Simplicidad total.

---

## 7. Módulos del sistema

Cada módulo corresponde a un mockup ya diseñado. El orden de esta lista es también el orden sugerido de implementación.

| # | Módulo | View XAML | Mockup |
|---|---|---|---|
| 0 | Login + Crear cuenta inicial | `LoginView.xaml` | Screenshot 1 |
| 1 | Shell principal (MainWindow con sidebar) | `MainWindow.xaml` + `Sidebar.xaml` | Screenshot 2 |
| 2 | Dashboard | `DashboardView.xaml` | Screenshot 2 |
| 3 | Clientes (lista + ficha + formulario) | `ClientesListView.xaml`, `ClienteFichaView.xaml`, `ClienteFormView.xaml` | Screenshot 3 |
| 4 | Préstamos (lista + nuevo + detalle con amortización) | `PrestamosListView.xaml`, `PrestamoNuevoView.xaml`, `PrestamoDetalleView.xaml` | Screenshot 4 |
| 5 | Pagos (lista + registro + recibo) | `PagosListView.xaml`, `PagoRegistroView.xaml` | Screenshot 5 |
| 6 | Reportes (6 tipos según mockup) | `ReportesHomeView.xaml` + una view por reporte | Screenshot 6 |
| 7 | Historial de auditoría | `HistorialView.xaml` | Screenshot 7 |
| 8 | Configuración (usuario, respaldo, DB) | `ConfiguracionView.xaml` | — |

### Notas por módulo

**Dashboard.** Los 4 KPIs son: Capital Colocado (suma de saldos activos), Cobros del Mes (suma de pagos del mes en curso), Clientes Activos (con al menos 1 préstamo activo), Morosidad (suma de cuotas vencidas o en mora, con porcentaje sobre capital total). Los deltas comparan contra el mes anterior. El gráfico de tendencia usa `LiveChartsCore` con datos de cobros diarios del mes en curso.

**Amortización.** Método francés (cuota fija) como default. Fórmula: `Cuota = P × [i(1+i)^n] / [(1+i)^n − 1]`, donde `P` es capital, `i` es tasa por período, `n` es número de cuotas. Documentar la matemática completa en `docs/AMORTIZATION.md` con ejemplos. Toda la lógica va en `AmortizacionService`, que retorna una lista de `CuotaCalculada` (record). El servicio NUNCA persiste directamente — el ViewModel decide cuándo guardar la tabla en la BD.

**Alertas de cobro (semáforo).** Se calculan en tiempo real (no se persisten como estado permanente en la tabla `cuota`). Regla:
- **Al día:** cuota con `fecha_vencimiento` posterior a hoy y sin pagar aún
- **Por vencer:** vence en los próximos 7 días
- **Vencido:** venció hace 1 a 15 días sin pagar
- **En mora:** venció hace más de 15 días sin pagar
- **Pagada:** tiene registro en `pago` que cubre el `monto_total`

**Registro de pago.** Debe permitir: pago exacto de la cuota, abono parcial (aplica primero a interés, luego a capital), adelanto de cuotas futuras, liquidación anticipada. Al registrar cualquier pago, imprimir automáticamente el recibo (o mostrar vista previa según configuración). El `numero_recibo` se genera secuencialmente con transacción atómica (`SELECT ... FOR UPDATE`).

**Historial.** Vista de solo lectura de la tabla `auditoria`, con filtros por fecha, entidad y acción. Ninguna acción del usuario borra registros de auditoría.

**Configuración.** Cambio de contraseña, respaldo manual (`mysqldump` al directorio elegido), restauración desde archivo `.sql`, configuración de la cadena de conexión (para casos donde el cliente cambie de PC).

---

## 8. Reglas específicas del proyecto

### Reutilización del CLAUDE.md global de freelance
Este proyecto hereda las reglas del CLAUDE.md global freelance. En particular respetar sin excepción:
- **Decimal para dinero**, jamás float/double
- **UTC en base de datos**, hora local solo en presentación
- **Soft deletes** en `cliente`, `pago` (no en `auditoria`)
- **BCrypt** con cost mínimo 12 para contraseñas
- **Auditoría** de toda mutación
- **Parametrized queries** siempre — jamás concatenación de strings SQL

### Reglas específicas de FAControl (adicionales)

1. **Cálculos financieros centralizados.** Toda operación matemática de dinero (amortización, interés, saldo, semáforo) va en `FAControl.Services`. Los ViewModels no calculan, delegan.
2. **Redondeo consistente.** Usar `Math.Round(valor, 2, MidpointRounding.AwayFromZero)` en todo cálculo de dinero. Documentar cualquier redondeo intermedio.
3. **Transacciones para operaciones multi-paso.** Crear un préstamo = insertar en `prestamo` + insertar N `cuota` + insertar `auditoria`. Todo dentro de una `MySqlTransaction`. Rollback ante cualquier excepción.
4. **Nunca borrar cuotas de un préstamo activo.** Solo permitir cancelar el préstamo completo (estado `cancelado`), lo que marca las cuotas restantes como `estado = 'cancelado'` sin borrarlas.
5. **Nunca modificar un pago ya registrado.** Si hay un error, se hace un pago compensatorio negativo con notas explicativas. Esta es una regla contable estándar.
6. **Números de recibo no se reutilizan.** Ni siquiera si un pago se anula. Cada `numero_recibo` es único e inmutable.
7. **Formateo de moneda unificado.** Crear un `MoneyConverter` en `FAControl.Common` que se use en todo XAML. Formato: `RD$ 15,000.00`. Nunca hardcodear formato en views.
8. **Formateo de fechas unificado.** `DateConverter` que respete el idioma español y muestre `DD/MM/YYYY` en inputs y `DD-MMM-YY` en tablas compactas.

---

## 9. Fases de desarrollo sugeridas

Cada fase debe cerrar con: código funcional, pruebas mínimas, commit con mensaje descriptivo, y actualización de `TODO.md` con el estado.

### Fase 1 — Cimientos (semana 1)
- Crear solución `.sln` con todos los proyectos
- Configurar `App.config` externo con cadena de conexión
- Script `001_create_schema.sql` completo y probado
- `CConexion` adaptado a WPF con lectura de `App.config`
- Autenticación con BCrypt (wizard inicial + login)
- `SesionActual` estático simplificado
- `AuditoriaService` funcional (usado por todos los módulos siguientes)
- `MainWindow` con sidebar navegable (aunque las páginas estén vacías)
- Aplicación del `DESIGN.md`: paleta, tipografía, converters comunes

### Fase 2 — Datos maestros (semana 2)
- Módulo Clientes completo (lista, búsqueda, ficha, formulario nuevo/editar)
- Repositorio `ClienteRepository` con métodos async
- Pruebas unitarias de `ClienteService`
- Integración de auditoría

### Fase 3 — Préstamos y amortización (semanas 3-4)
- `AmortizacionService` con método francés (con pruebas exhaustivas)
- `docs/AMORTIZATION.md` con la matemática
- Módulo Préstamos: lista, wizard de nuevo préstamo con vista previa de amortización, detalle con tabla de cuotas
- Transacción atómica para crear préstamo + N cuotas + auditoría

### Fase 4 — Cobros (semana 5)
- Lógica de semáforo (`CuotaEstadoCalculator`)
- Módulo Pagos: registro con validación, generación de recibo, impresión 80mm
- Generación atómica de `numero_recibo`
- Recibos como imagen (patrón del POS-400) y también como PDF con `PdfSharp`

### Fase 5 — Analítica (semana 6)
- Dashboard con los 4 KPIs
- Integración de `LiveChartsCore` para gráfico de tendencia
- Panel de alertas de cobro con navegación por click

### Fase 6 — Reportes y auxiliares (semana 7)
- Los 6 reportes del mockup Screenshot 6
- Historial (viewer de auditoría con filtros)
- Configuración (cambio de contraseña, respaldo, restauración)

### Fase 7 — Empaquetado (semana 8)
- Instalador con WiX Toolset (o Inno Setup como alternativa más simple)
- Script de instalación de MySQL Community como dependencia
- Guía de instalación (`INSTALL.md`)
- Manual básico de usuario en PDF

---

## 10. Testing

Cobertura mínima esperada:

- **AmortizacionService:** 90%+ (es el cerebro financiero, no puede fallar)
- **CuotaEstadoCalculator:** 100% (todas las ramas del semáforo)
- **Servicios de dominio:** 70%+
- **Repositorios:** integración contra BD de prueba (`facontrol_test`)

Regla: **ningún cálculo de dinero se merges a main sin pruebas unitarias**.

Ejemplos de casos de prueba obligatorios para amortización:
- Préstamo de 10,000 DOP, 12 cuotas mensuales, 5% mensual → validar cuota francesa exacta
- Préstamo de 5,000 DOP, 30 cuotas diarias, 1% diario → validar suma de capitales = capital original
- Redondeo: cada cuota redondeada + ajuste final en última cuota
- Amortización cuando la tasa es 0% (préstamo sin interés)

---

## 11. Anti-patrones prohibidos

- ❌ Código de negocio en el `.xaml.cs` (code-behind). Solo lógica de UI (ej: mover foco).
- ❌ Acceso directo a `MySqlConnection` desde ViewModels.
- ❌ Uso de `MessageBox.Show` para lógica; usar un `IDialogService` inyectable para poder testear.
- ❌ `async void` excepto en event handlers.
- ❌ Cadenas mágicas para nombres de tablas o columnas; usar constantes en `FAControl.Common.DbNames`.
- ❌ Cachear `SesionActual` en variables locales que sobrevivan al logout.
- ❌ Silenciar excepciones con `catch { }`. Loguear con Serilog y propagar o mostrar mensaje al usuario.
- ❌ Modificar directamente tablas de dinero desde SQL sin auditoría.
- ❌ Copiar código del POS-400 sin adaptarlo a WPF/MVVM. Los patrones se replican, no se copia el código.
- ❌ Referencias a WinForms (`System.Windows.Forms.*`). Todo es WPF (`System.Windows.*`).

---

## 12. Entregables por fase

Al final de cada fase, Claude Code debe producir:

1. **Commit con mensaje descriptivo** en formato: `[Fase N] Descripción corta` — `Fase 3: Amortización francesa con tests`
2. **Actualización de `TODO.md`** marcando lo completado y listando lo pendiente
3. **Actualización de `CHANGELOG.md`** en formato Keep a Changelog
4. **Screenshots o GIFs** de la funcionalidad nueva guardados en `docs/screenshots/`
5. **Notas de bloqueos** en `BLOCKERS.md` si hay decisiones que requieren consulta al desarrollador

---

## 13. Instrucciones de arranque para Claude Code

Cuando se inicie una nueva sesión de trabajo en este proyecto:

1. Leer este `CLAUDE.md` completo
2. Leer `docs/DESIGN.md` para la parte visual
3. Verificar si existen `TODO.md`, `BLOCKERS.md`, `CHANGELOG.md`. Si no, crearlos vacíos con estructura.
4. Verificar si el proyecto POS-400 está accesible en el workspace; si sí, listarlo mentalmente como referencia
5. Verificar si PTV-300 está accesible; documentar en `BLOCKERS.md` si no está
6. Verificar el estado de la última fase completada mirando `CHANGELOG.md`
7. Continuar con la siguiente tarea pendiente en `TODO.md` respetando el orden de fases

**Frente a ambigüedad:** preferir preguntar al desarrollador antes de asumir. Documentar la duda en `BLOCKERS.md` con la mayor cantidad de contexto posible.

**Frente a decisiones técnicas:** preferir simplicidad sobre elegancia. Este es un sistema de un solo usuario para un negocio pequeño; no sobre-ingenierizar con patrones complejos que no se justifican.

---

## 14. Contacto y ownership

- **Desarrollador:** Yuber Santana
- **Cliente final:** Prestamista independiente (nombre a definir contractualmente)
- **Repositorio:** GitHub (privado)
- **Licencia:** Software propietario, entregado al cliente con contrato de mantenimiento por 1, 2 o 3 años según plan comercial
