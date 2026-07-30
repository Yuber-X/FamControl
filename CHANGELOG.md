# Changelog — FAControl

Formato: [Keep a Changelog](https://keepachangelog.com/es/1.0.0/). Fechas en hora de República Dominicana.

## [1.7.1] — 2026-07-30 · Correcciones tras la primera prueba del POS

### Corregido

- **No se podía vender.** Fallaba con "foreign key constraint fails
  `fk_factura_usuario`": la base separada del punto de venta conservaba su
  propia tabla `usuario` vacía, del POS-500 independiente, y la factura apuntaba
  ahí en vez de a los usuarios de la suite. Se resolvió unificando las bases.
- **Panel y Reportes no aparecían en el punto de venta** aunque las pantallas
  estaban: las dos condiciones del sidebar no contemplaban el modo nuevo.

### Cambiado

- **Una sola base de datos.** Las tablas del punto de venta pasan a `facontrol_db`
  con prefijo `pos_` (024). El motivo lo puso el cliente: **dos respaldos
  confunden al usuario** — veía dos `.sql` en la carpeta y no sabía cuál era el
  bueno. Ahora hay un solo archivo y no se puede restaurar media empresa. De paso
  la factura vuelve a tener una clave foránea real contra el usuario que la emitió.
- **El Historial distingue por módulo** (025): cada línea guarda en qué estancia
  se hizo, el historial arranca filtrado por la estancia donde estás parado, y
  desde el filtro se puede abrir a todos los módulos. Las líneas anteriores a
  este cambio se muestran con guion: la auditoría no se reescribe.
- **Detalle del préstamo**: el comprobante fiscal se movió a la derecha y la nota
  usa todo el ancho, así deja de dominar la pantalla. Sin notas, esa sección ni
  aparece.

### Agregado

- **Roles y permisos del POS-500 en la pantalla de Usuarios**, con su rol por
  estancia y sus permisos por pantalla, igual que PrestControl y DealControl.
- La acción **anular** entró al filtro de acciones del Historial, y las tablas
  del punto de venta al de entidades.

## [1.7.0] — 2026-07-30 · POS-500 integrado a la suite

> Decisión con Yuber (2026-07-30): el punto de venta pasa a ser un modo más de
> FAControl —mismo login, mismos usuarios y permisos— con sus DATOS en una base
> aparte (`pos500_db`), porque se vende por separado. Esta entrega cubre los
> cimientos y las capas de datos, negocio e impresión; faltan las pantallas.

### Hecho

- **Modo POS-500** en el launcher, con su color y su clave de base `pos500`. Se
  habilita con el código 5, que ya existía: la clave que guardaba coincide con la
  del modo, así que activar es literalmente digitar el código.
- **Base propia `pos500_db`**, creada sola la primera vez que se entra al modo.
  Lleva SOLO las tablas del punto de venta; usuarios, roles, permisos, sesiones y
  auditoría siguen en `facontrol_db`, compartidos por toda la suite.
- **022**: permisos del punto de venta (vender, productos, almacén, caducidad,
  comprobantes y comprobantes de todos, cuadre y cuadre de todos, anular
  facturas, acceso al modo) y roles **Supervisor / Cajero / Vendedor** propios
  del POS, todo dentro de la base compartida.
- **023**: la auditoría acepta la acción **anular**. Una factura emitida no se
  edita ni se borra nunca, se anula, y eso tiene que poder buscarse solo en el
  Historial.
- **Capas de datos, negocio e impresión portadas**: productos, clientes del
  mostrador, facturación con ITBIS, cuadre de caja, analítica, exportación y los
  tickets de 80mm.
- **La auditoría del POS entra en la MISMA transacción de la venta** aunque viva
  en otra base: se califica el esquema en el INSERT, y como la transacción es de
  la conexión y no de la base, la venta y su línea de historial se guardan o se
  pierden juntas. Verificado con una prueba que fuerza el fallo por falta de
  stock y comprueba que no queda ni factura ni rastro.

### Pantallas

- Las **nueve pantallas del punto de venta dentro del shell de la suite**: Panel,
  Vender, Clientes, Productos, Almacén, Caducidad, Buscar comprobante, Cuadre de
  caja y Reportes — con el mismo sidebar, el mismo tema claro/oscuro y el mismo
  tamaño de texto que el resto. Panel, Clientes y Reportes comparten página con
  los otros modos y se resuelven según la estancia activa, igual que el panel del
  dealer: nunca se mezclan los datos de un módulo con los de otro.
- **Un solo login**: el cajero entra una vez y el POS usa ese usuario. Ya no hay
  usuarios del punto de venta aparte.
- El sidebar del POS **se arma con los permisos** del empleado: un Cajero ve
  vender, clientes, sus comprobantes y su cuadre; un Supervisor ve todo el piso
  de venta, incluidos los comprobantes y la caja de los demás.
- **Tickets y cierres de caja**: la venta imprime directo (con un cliente
  esperando no se pregunta nada) y cae en vista previa si la impresora falla; la
  reimpresión desde Comprobantes siempre muestra la vista previa, y el cierre de
  caja también. La factura ya está guardada antes de imprimir: nada de lo que
  pase con el papel la afecta.
- La base del punto de venta **se crea sola** la primera vez que se entra al modo.
- **El respaldo automático saca las dos bases**: la de la suite y la del punto de
  venta. Si el cliente no tiene el POS, ese respaldo simplemente no se hace y
  queda anotado en el log — no rompe el principal.
- **Instalador 1.7.0**.

## [1.6.0] — 2026-07-30 · Licencia por módulo, préstamo abierto y la cartera real del cliente

### FAControl (la suite)

- **Siete códigos** en lugar de cuatro, con **activación por módulo** (pedido del cliente 2026-07-29): prueba de 14 días, activación total, PrestControl, DealControl, POS-500, respaldar y limpiar todo, eliminar todo. Los códigos por módulo **solo se piden cuando termina la prueba**: durante los 14 días la suite está abierta completa, y lo que se compre durante la prueba sigue valiendo después. En el launcher, un módulo sin código muestra **REQUIERE CÓDIGO** y dice cuál falta.
- **Eliminar todo** (código 7) es nuevo y no respalda nada: borra base, expedientes, ajustes y licencia para retirar la instalación de una PC. Exige escribir `ELIMINAR` y dos confirmaciones. La marca de inicio de prueba del registro **no** se borra: eliminar no sirve para estirar los 14 días.
- **Cuenta de respaldo del desarrollador** sembrada con el esquema (`Yub`, rol Programador). La instalación **sigue pidiendo** las credenciales del primer Admin como si no existiera, y ningún Admin la ve ni la puede tocar. Cada login suyo queda con Warning en el log. Reemplaza al viejo código de recuperación de acceso, que se retiró: dos puertas traseras para lo mismo era una de más.
- **Arranque directo**: casilla en la pantalla de inicio para que la app abra siempre el mismo módulo, sin pasar por el launcher. Se apaga desde Configuración → Apariencia de cualquier módulo. Solo aplica al arranque: al cerrar sesión vuelve a aparecer el launcher, para poder cambiar de estancia.
- **Botón de ayuda** con el teléfono del desarrollador (849-438-0242), en el launcher y en el sidebar de cada módulo: copiar el número, abrir WhatsApp y qué contar al escribir.
- **AutoControl salió de la suite**: DealControl ya hace sus operaciones. Su lugar en el launcher lo ocupa **POS-500** como producto a la venta (código 5 registra la compra). El valor sigue en la base para no migrar los datos históricos, pero ya no se entra ni se asignan roles de esa estancia.

### PrestControl

- **Préstamo abierto (solo interés)** — método de amortización nuevo. El cliente paga **solo el interés** cada período y el capital queda abierto hasta que decida saldarlo: N cuotas de puro interés y el capital completo en la última, que es una proyección y no un vencimiento pactado. **7 de los 10 préstamos reales del cliente son así** y ninguno de los dos métodos anteriores los representaba sin inventar abonos a capital.
- **Comprobante fiscal con la autorización real de la DGII** (constancia del 29/07/2026, autorización 6005407803): B01 Factura de Crédito Fiscal, del B0100000001 al B0100000015, vence 31/12/2027. Probado de punta a punta contra MySQL: los 15 salen en orden y sin repetir, el 16 se bloquea en vez de inventarse un número, un rollback no quema el comprobante y una autorización vencida no emite.
- **La cartera real cargada**: 10 clientes con sus préstamos, cargados con los mismos servicios de la app (códigos, cuotas y auditoría idénticos a haberlos tecleado). Las inconsistencias del listado quedaron escritas en las notas de cada préstamo y en un informe aparte; los pagos ya hechos **no** se cargaron a ojo.

### Verificación

- **Aislamiento entre estancias probado contra MySQL**: los clientes de un módulo no aparecen en el otro, la misma cédula puede existir en los dos, y lo único compartido es lo que pidió el cliente — usuarios, roles por módulo y permisos.
- **Instalador 1.6.0** con prerequisitos opcionales (MySQL, AnyDesk, Google Drive): si los instaladores están en `installer\prerequisitos\`, el asistente ofrece instalarlos antes de abrir FAControl; si no están, compila igual.

## [1.5.0] — 2026-07-27 · Códigos del producto, expediente digital y primer instalador de la suite

### FAControl (los tres modos)

- **Cuatro códigos digitables en el launcher**, con botón propio en el pie:
  1. **prueba de 2 semanas** — 14 días de uso completo; al pasarse, la app se bloquea;
  2. **activación total** — habilita el producto en firme;
  3. **recuperar acceso** — para cuando el cliente perdió todas las contraseñas: le pone contraseña nueva a una cuenta (o la crea) con acceso total, **sin tocar un solo dato del negocio**;
  4. **restablecer todo** — deja el sistema como recién instalado, con **respaldo obligatorio antes** y doble confirmación; si el respaldo falla, no se borra nada.
  El estado de la licencia vive en `licencia.json` **firmado**, con la marca del inicio de prueba también en el registro de Windows: borrar el archivo no reinicia los 14 días. Los códigos solo viajan **hasheados** en el binario (están en un MD privado del desarrollador, fuera del repositorio).
- **Rol Programador**: autoridad total sobre todo el sistema, **invisible e intocable para el Admin** — no aparece en la lista de usuarios, no se puede editar, desactivar ni cambiarle la contraseña. Solo otro Programador puede crearlo o asignarlo, y la única vía para la primera cuenta es el código 3.

### DealControl

- **Grids**: las columnas se miden por su contenido y la tabla se desplaza de lado cuando no caben — se acabaron las columnas que cortaban la información. El alto de fila queda fijo y cada tabla tiene su propio scroll.
- **Panel en modo noche**: "Últimos movimientos" pasó a ser una tabla del tema; antes eran textos negros sobre fondo oscuro.
- **Ficha de cliente propia del dealer**: cambian las métricas de PrestControl por las que sí significan algo acá — **Total transferido** (compras + alquileres), **Total cobrado**, **Saldo pendiente**, **Vehículos comprados/alquilados** y **Plazos vencidos** — y el grid muestra **sus vehículos** (compra o alquiler, con matrícula, chasis, color, estado y pendiente) con botón **Ver ficha** al detalle del vehículo.
- **Gráficos**: en el panel, **ventas vs alquiler de los últimos 6 meses** y **torta del inventario** por estado; en reportes, **de dónde vino el dinero** y **monto vendido por vendedor**.
- **Expediente digital del contrato** (Financiamiento de la venta): subir **varios archivos a la vez**, verlos en **lista** o en **cuadrícula** con su ícono, y **descargar todo en un ZIP** para migrar cuando haga falta. Doble clic sobre un documento abre el "ver automático": **Abrir** con la app de Windows que corresponda (Word, Excel, visor de fotos…), **Guardar** una copia, **Re-ubicar** en otro contrato y **Eliminar** — los dos últimos, **solo Admin**. Se aceptan PDF, Word, Excel, comprimidos e imágenes (incluidas las de iPhone). Los expedientes entran en el **respaldo automático**, en su propio ZIP.
- **Factura firmada**: botón para **reemplazar la factura del sistema por la escaneada y firmada**, que queda guardada en el expediente y se puede volver a abrir desde ahí. La factura generada se sigue pudiendo imprimir.
- La **cantidad de documentos** del expediente de contratos ahora suma los archivos reales, no solo los que emite la app.

### Cambios técnicos
- Migraciones `017_rol_programador.sql` y `018_expediente_documentos.sql`, idempotentes, con espejo en `001` y aplicadas a `facontrol_db`.
- El archivo del expediente vive en **disco** (`<app>\expedientes\<venta>\`) y la base solo guarda su ficha: un BLOB por cada foto de cédula haría lento e inmanejable el respaldo.
- **Lista blanca de extensiones** en el expediente: nada de `.exe`, `.bat` ni `.lnk`, porque los documentos se abren con doble clic.
- `SesionActual.EsAdmin` incluye al Programador; el blindaje real vive en `UsuarioService` (la UI solo oculta lo que el servicio ya rechaza).
- **Primer instalador de la suite completa**: `FAControl_Setup_1.5.0.exe` (Inno Setup 6, español, self-contained win-x64 — no requiere .NET en la PC del cliente). Incluye todas las migraciones para actualizar una base existente y **excluye** el script de rollback y los seeds de prueba. Al desinstalar se conservan la licencia, los ajustes, los expedientes y la base.
- Build sin warnings; **178 tests verdes** (165 unitarios + 13 de integración).

## [1.4.0] — 2026-07-25 · Comprobante fiscal, préstamos antiguos y DealControl completo

### PrestControl

- **Pagaré**: la tasa de interés ahora aparece en el texto principal ("…con una tasa de interés del 10% mensual…"), y la cláusula de datos crediticios usa el **nombre de la empresa** en lugar de "Púrpura Datos".
- **Papelería con la marca**: el recibo 80mm y el estado de préstamo llevan el **logo FA, el nombre del negocio, el RNC y el teléfono**. Nueva sección **Datos del negocio** en Configuración (nombre, RNC, teléfono, dueño, ciudad, correo) — antes solo se podían cambiar editando el archivo de ajustes.
- **Comprobante fiscal (NCF)**: cada préstamo puede llevar su comprobante. Dos caminos, los dos soportados: **registrar** el e-NCF generado en el Facturador Gratuito de la DGII, o **asignar** el siguiente de una secuencia local configurada (prefijo B02/E32, próxima, fin de rango y vencimiento). La reserva es atómica dentro de la transacción del préstamo, así que un error no consume el número, y la app avisa cuando la secuencia está por agotarse o vencida. El comprobante sale impreso en el recibo.
- **Reportes**: dos KPIs nuevos — **Total prestado** (capital colocado en el período) y **Proyección a ganar** (interés que falta por cobrar de los préstamos activos).
- **Permisos por pantalla de vuelta**: el Admin vuelve a tener los **checkboxes por pantalla**, conviviendo con los roles por modo — el rol elegido precarga los permisos y el Admin ajusta fino. Cada estancia muestra solo SUS permisos; nunca se mezclan entre modos.
- **Préstamos antiguos**: al crear un préstamo con fecha atrasada, el wizard lo **detecta y pregunta** si el cliente está al día o cuántas cuotas ya pagó. Las cuotas saldadas nacen pagadas con **recibos históricos fechados en su vencimiento**, así los reportes las ubican en su mes real. Esto reemplaza el flujo que limitaba el abono al cargar un cliente antiguo.

### DealControl

- **Panel principal propio**: inventario disponible, inversión, ventas del mes con su ganancia, alquileres e ingresos, y últimos movimientos. **Cero datos de PrestControl**.
- **Inventario ampliado**: nueva **matrícula** (certificado DGII, distinta de la placa) y columnas de año, chasis, color y matrícula en el grid.
- **Ficha del vehículo**: datos completos + **quién lo compró** (venta al contado o crédito de AutoControl) + **historial de reparaciones** con costo, todo imprimible en carta y exportable a PDF.
- **Vendedor restringido**: no ve costo total ni ganancia, ni en el inventario ni en la ficha — solo marca, modelo, chasis, año, color, precio y nota de condición. Sí puede vender.
- **Facturación**: ver e imprimir la factura de una venta, con la marca del negocio, los datos del cliente y del vehículo, el total, la forma de pago y las firmas de cliente, vendedor y gerencia.
- **Financiamiento por plazos**: la venta se pacta **al contado, por plazos** (inicial + N pagos sin interés, como financia un dealer) **o como separación/apartado**. La pantalla de plazos muestra **total por pagar, lo pendiente, la cantidad de plazos y lo pagado**, con cobro por plazo (recibo propio RV-000001), historial de abonos y semáforo de atrasos.
- **Documentos**: **carta de compromiso** con el calendario pactado y **recibo de separación** con la cláusula de los días de derecho (15 por default). Una separación reserva el vehículo en vez de darlo por vendido, y la app avisa cuando el plazo está por vencer.
- **Contratos del dealer**: expediente por venta con cliente, **quién vendió**, cantidad de documentos, matrícula del auto y estado de los plazos (pagados / atrasados / pendiente), con "ver detalles" al financiamiento completo.
- **Reportes propios**: ganancia del período, monto vendido, ingresos por alquiler, pendiente de cobro, inventario, y **comisiones por vendedor** (el % lo define el negocio en Configuración).

### Cambios técnicos
- Migraciones `012_ncf.sql` (comprobante fiscal), `013_permisos_por_pantalla.sql`, `014_panel_deal.sql`, `015_vehiculo_ficha.sql` y `016_venta_plazos.sql` — todas idempotentes, con espejo en `001` y aplicadas a `facontrol_db`.
- `usuario_modo_permiso` guarda el set marcado por modo; `usuario_permiso` sigue siendo la unión efectiva que lee el login (semántica intacta). El permiso `acceso_<modo>` nunca es un checkbox: se quita eligiendo "Sin acceso".
- Reserva atómica (`SELECT … FOR UPDATE`) para el NCF y para los recibos de plazos, siempre dentro de la transacción de la operación que consume el número.
- `LogoFa` centraliza el monograma vectorial que comparten pagaré, recibo, ficha y factura.
- **FIX**: editar inventario exigía el permiso viejo `vehiculos_editar` y vender también — el Encargado no podía editar y el Vendedor no podía vender. Ahora usan `inventario_editar` y `ventas`.
- Los tests de integración que tocan `SesionActual` (estático global) corren en una colección serializada: el paralelismo de xUnit hacía que una clase le cerrara la sesión a la otra.
- Build Release sin warnings; **146 tests verdes** (133 unitarios + 13 de integración).

## [1.3.5] — 2026-07-19 · Roles por modo (permisos diferenciados por estancia)

### Added
- **Roles por modo**: al dar acceso a un empleado, el Admin ya no marca permisos sueltos — elige **un rol por cada estancia** (PrestControl / DealControl / AutoControl) o lo hace **Administrador** (acceso total a los tres). Cada rol trae sus propios permisos. "Sin acceso" en un modo = ese empleado no entra ahí.
- **Roles propios de Dealer/Auto**, distintos a PrestControl y con **permisos con nombres propios**: `inventario`, `inventario_editar`, `ventas`, `alquileres`, `gastos`. Roles nuevos **Encargado** (gestión completa: puede editar inventario y registrar gastos) y **Vendedor** (opera ventas/alquileres/inventario, sin editar inventario ni gastos) para `dealercontrol` y `autocontrol`.
- **Equivalencia de nivel entre modos**: si Jessi es Cobradora en PrestControl y se le da acceso a DealControl como Vendedora, entra a Dealer con los permisos de Vendedora — el rol y su nivel se respetan por estancia, no se arrastra el de PrestControl.
- El **rol mostrado en la barra** ahora es el del modo en el que se entró (Cobrador en Prest, Encargado en Dealer, etc.), no un rol global.

### Cambios técnicos
- Tabla `usuario_modo_rol (usuario_id, modo, rol_id)` guarda la elección por estancia; `usuario_permiso` sigue siendo la **unión efectiva** (el login no cambia de semántica). `GuardarRolesPorModoAsync` recalcula la unión de forma atómica en la misma transacción.
- `rol.modo` etiqueta cada rol a su estancia; clave única `(nombre, modo)` para permitir "Encargado" en Dealer y en Auto sin chocar. Admin = `usuario.rol_id` = rol Admin global (todos los permisos).
- Migración `011_roles_por_modo.sql` (idempotente) + espejo en `001`: columna `modo`, roles y permisos nuevos, `rol_permiso` de cada rol, y migración de usuarios existentes a `usuario_modo_rol` recalculando su unión.
- Build Release sin warnings; **123 tests verdes**. Migración 011 aplicada a `facontrol_db` y verificada; smoke por SQL + harness del camino de escritura OK (unión correcta, Admin = 22/22 permisos, rol por modo bien mostrado).

## [1.3.4] — 2026-07-18 · Pulido final de grids

### Fixed
- **Préstamos**: columna "Cliente" con ancho fijo → el grid se DESPLAZA horizontalmente (y vertical) mostrando todos los datos completos en vez de comprimir/cortar.
- **Detalle de préstamo**: los botones de acción se movieron a una fila DEBAJO del "← Préstamos" + código/cliente/estado (en todos los tamaños).
- **Reportes**: área de gráfico + desglose con **scroll vertical** (bajar a ver "Ganancia por semana"); "Desglose por semana" con columna fija → **scroll horizontal**.

## [1.3.3] — 2026-07-18 · Ajustes de UX, recordatorios manuales y chrome

### Fixed
- **Scroll global solo en Grande**: en Pequeño/Mediano vuelve el comportamiento original (sin scroll, se sentía como zoom); el scroll solo aparece en Grande, que es cuando el contenido excede la pantalla.
- **Cobros**: dígitos de "Deuda pendiente", "Próxima cuota" y "Liquidar hoy por" reducidos (eran algo grandes en Pequeño).

### Added
- **Guardar PDF** en la vista previa de la intimación de pago y del estado de préstamo (y reportes), con zoom.
- **Recordatorios manuales**: botón "Enviar recordatorios" en Préstamos (masivo a todos los clientes con cuota por vencer/vencida de la estancia) y "Enviar recordatorio" en el detalle del préstamo (individual, al correo de ese cliente).
- **Historial → Ver ficha**: botón por fila que abre el detalle con la DESCRIPCIÓN completa (en el grid nunca entra entera) y los demás campos.
- **Sin botones de Windows** (minimizar/maximizar/cerrar) en todas las ventanas MENOS el login: evita que el cliente cierre con la X por error. Se usa "Cerrar sesión" del sidebar.

## [1.3.2] — 2026-07-18 · Responsive (texto Grande), formato de campos y correo

### Fixed
- **Responsive (texto Grande)**: el shell ahora envuelve el contenido en un ScrollViewer, así el contenido magnificado se puede DESPLAZAR en vez de recortarse en pantallas chicas (laptops 1366). Ajustes de ancho en columnas (Modalidad, Próx. venc.) y `MinWidth` en columnas de dinero (Cobros). Reportes: `MinHeight` en el gráfico/desglose para que no colapse a solo títulos.
- **Ficha de cliente**: columnas Tasa y Cuotas centradas; "Próx. venc." más ancha.

### Added
- **Cédula y teléfono con auto-formato** en Nuevo/Editar cliente: los guiones se insertan solos al escribir (cédula 000-0000000-0, teléfono 000-000-0000). La cédula respeta pasaportes (si hay letras, no se toca).
- **Correo**: indicador "✓ Contraseña guardada" (la PasswordBox se ve vacía al volver pero la clave sigue guardada); guía con los pasos exactos para generar el App Password (Nombre → Crear → 16 letras). Diagnóstico confirmado: la app funciona; solo faltaba generar el App Password (2FA requerido; se confirmó con las 3 cuentas de prueba).

### Datos de prueba
- `seed_alertas_prueba.sql`: correos personales reemplazados por `@example.com`.

## [1.3.1] — 2026-07-18 · Correcciones de UX y pagaré

### Fixed
- **Sidebar**: "Préstamos"/"Nuevo préstamo" ya no necesitan doble click para marcarse (GroupName único por ítem: el gemelo colapsado de AutoControl ya no roba el check).
- **Grid de préstamos**: columnas "Tasa" y "Cuotas" centradas (antes el dato iba a la izquierda con el título centrado).
- **Correo**: se quitan los espacios del app password al pegarlo; mensaje de error accionable cuando Gmail rechaza credenciales (explica que hace falta App Password de 16 caracteres + 2FA); enlace directo a generar el App Password.

### Changed
- **Reportes**: un solo botón "Imprimir" gobernado por los combos (cliente → su reporte; solo usuario → sus cobros generales; sin filtro → global). Botones de acción alineados con los combos/datepickers.
- **Pagaré**: zoom funcional (−/+ 50–300%), botón **Guardar PDF** (multipágina en hoja carta), y **encabezado de marca con el logo FA vectorial** (badge navy + monograma, no imagen pegada) con regla dorada.
- **Clientes**: nota bajo el correo — opcional, pero sin él no se envían recordatorios (el correo del cliente se pone acá, no en Configuración).

### Datos de prueba (Dev)
- `scripts/db/seed_alertas_prueba.sql`: 3 clientes con cuotas por vencer, vencidas y en mora para probar el semáforo, las alertas del panel y los recordatorios (idempotente).

## [1.3.0] — 2026-07-18 · Aislamiento por estancia + acceso por modo

### Apariencia (paletas por modo)
- **El ítem seleccionado del sidebar toma el color del modo activo**: dorado en PrestControl, verde en AutoControl, azul en DealControl (antes era índigo en los tres). Brushes dedicados `Brush.SidebarSel.*` que `MostrarModo` sobreescribe en caliente en `Window.Resources`; no toca botones ni el resto del acento.
- **Modo noche por MODO**: cada estancia recuerda su propio tema y **DealControl arranca en modo noche** por defecto; el resto en claro. Editable en Configuración (persiste por modo, por PC). `AjustesLocales.TemaOscuroDe/FijarTemaOscuro`; el tema se aplica al elegir el modo en el launcher.
- **DealerControl renombrado a "DealControl"** (nombre visible en launcher, shell y título). El identificador de código (`ModoApp.DealerControl`) y los valores de BD (`ambito='dealercontrol'`, permiso `acceso_dealercontrol`) se mantienen para no romper datos.

### Added
- **Acceso por modo (permisos)**: tres permisos nuevos `acceso_prestcontrol` / `acceso_dealercontrol` / `acceso_autocontrol`. El Admin decide desde la pantalla de Usuarios a qué estancias entra cada empleado; el Admin siempre entra a las tres. La **puerta se aplica en el login**: aunque la contraseña sea correcta, si el usuario no tiene el acceso del modo elegido no se abre sesión y ve un mensaje claro (no cuenta como intento fallido). `SesionActual.PuedeAccederModo` / `SesionActual.Modo`.
- **Clientes en los tres modos**, cada estancia con los suyos: Dealer y Auto ahora gestionan sus propios clientes sin depender de PrestControl.

### Cambios técnicos (aislamiento de datos — decisión Yuber 2026-07-18)
- **3 dominios aislados de clientes** (`cliente.ambito` ENUM prestcontrol/dealercontrol/autocontrol). Toda lectura de clientes se scopea al modo activo (`SesionActual.Modo`): un cliente de PrestControl ya NO aparece al vender/alquilar/financiar vehículos, ni viceversa. Cédula **única por ámbito** (`UNIQUE(ambito, cedula)`), no global: la misma persona puede tener ficha independiente en dos estancias.
- **Aislamiento PrestControl ↔ AutoControl** (ambos usan `prestamo`): Cobros, Almacén de contratos, Panel y Reportes se filtran por `vehiculo_id` según el modo (`SesionActual.SoloVehicularesDelModo`) — los créditos vehiculares no se mezclan con los préstamos personales en ninguna lista, KPI ni reporte.
- Migración `010_ambitos.sql` (idempotente) + espejo en `001`: columna `ambito`, índice único compuesto, permisos de acceso, defaults por rol (Admin/Supervisor = 3 modos, Cobrador = solo PrestControl) y backfill de usuarios/clientes existentes a PrestControl.
- Build Release sin warnings; **123 tests verdes** (114 unitarios + 9 integración). Migración 010 aplicada a `facontrol_db` y verificada; smoke de aislamiento OK (misma cédula en dos ámbitos permitida, duplicada en el mismo ámbito rechazada). Corregida aserción obsoleta del conteo de contadores (2 → 5, desde Tier 5).

## [1.2.0] — 2026-07-17 · Tier 5 — DealerControl y AutoControl (suite de 3 modos)

### Added
- **Suite multimodo**: el shell ahora es *mode-aware*. El launcher abre uno de tres modos y el sidebar/landing se adapta. **DealerControl** y **AutoControl** habilitados.
- **Dominio de vehículo** (`vehiculo`, schema 001 + migración 008): el vehículo como ACTIVO que nace en Dealer; código `V-0001`, soft delete, costo vs precio (costo total y ganancia calculados), estados disponible/reservado/vendido/alquilado/baja. Permisos `vehiculos`/`vehiculos_editar`.
- **DealerControl — Inventario**: alta/edición/baja de vehículos con vista previa de costo total y ganancia.
- **DealerControl — Venta al contado** (`venta_vehiculo`, código `VC-0001`): venta atómica (marca el vehículo `vendido` + auditoría en una transacción), con cliente, precio y método de pago.
- **DealerControl — Rent a car** (`alquiler`, código `AL-0001`): alquiler con cálculo automático de días y total; devolución/cancelación que libera el vehículo. Atómico.
- **DealerControl — Gestión de importación** (`vehiculo_gasto`): ledger de gastos (aduana, flete, etc.) cuya suma se refleja en el costo del vehículo.
- **AutoControl — Crédito vehicular**: un crédito vehicular es un `prestamo` con `vehiculo_id` (el vehículo en garantía), reutilizando amortización, cuotas, cobros, pagaré y reportes. Al financiar, el vehículo pasa a `vendido` en la misma transacción; la garantía se autocompleta con el vehículo. La lista y el wizard de préstamos se filtran/adaptan por modo (picker de vehículo disponible).

### Cambios técnicos
- Migración 009: `prestamo.vehiculo_id`, tablas `venta_vehiculo`/`alquiler`/`vehiculo_gasto`, contadores `venta`/`alquiler`. Reordenada `vehiculo` antes de `prestamo` en 001 por la FK. 001 validado contra BD desechable.
- 13 tests nuevos (114 verdes). Flujos atómicos verificados end-to-end contra `facontrol_db`.

## [1.1.0] — 2026-07-17 · Tier 4 — Reportes individuales, contratos, recordatorios Gmail e intimación de pago

### Added
- **Intimación de pago** imprimible por préstamo (`IntimacionDocumentFactory` + `IntimacionImpresa`): requerimiento formal PREVIO a la vía judicial que emite el acreedor para las cuotas vencidas, con encabezado del negocio, datos del deudor, tabla de cuotas vencidas, saldo, plazo configurable (`AjustesLocales.PlazoIntimacionDias`, default 15 días) y firma. Botón en el detalle del préstamo → vista previa imprimible. Se aclara en `docs/INTIMACION-Y-MANDAMIENTO.md` que NO es el "mandamiento de pago" (acto de alguacil): la app genera la intimación, que es lo que el acreedor sí puede emitir por su cuenta.
- **Recordatorios de cobro por Gmail** (`RecordatorioService` + `EmailService`, SMTP `smtp.gmail.com:587` STARTTLS): correo por cliente con cuotas próximas a vencer + resumen al dueño, envío manual o automático una vez al día. Contraseña de aplicación de Gmail cifrada con DPAPI (`Secreto`, por usuario/PC). Destinatario: cliente + dueño. WhatsApp documentado y diferido (`docs/WHATSAPP.md`).
- **Reporte por cliente** (individual y global) imprimible: cobros, capital recuperado y saldo pendiente por cliente, con filtro por usuario cobrador y por cliente.
- **Almacén de contratos**: lista con vista previa lateral del pagaré (`ContratoService` + `ContratosView`).
- Datos del negocio configurables (nombre, prestamista, ciudad, teléfono, email, RNC) para encabezados de pagaré e intimación; doc explicativo de NCF/DGII (`docs/NCF-DGII.md`).
- Respaldo automático cada N días/meses (a carpeta local o sincronizada a la nube).

### Fixed
- **Filtro por usuario en Reportes mostraba todo en 0**: los pagos previos a la columna `pago.created_by` la tenían en `NULL`. Migración `007_pago_created_by.sql` con backfill desde auditoría por `fecha_pago`. Los pagos nuevos ya guardan quién cobró.
- **Responsive**: los textos y dígitos ya no se recortan al cambiar el tamaño de letra en Configuración (columnas de fecha ensanchadas a 185px, tarjetas de métricas envueltas en `Viewbox`, `MinWidth` en columnas estrella).

## [1.0.1] — 2026-07-11 · Arranque robusto sin base de datos + auto-aprovisionamiento

### Fixed
- **La app crasheaba al instante en una PC con MySQL pero sin la base de datos creada** (excepción sin capturar en el `Loaded` del login). Ahora el arranque diagnostica la conexión ANTES de mostrar ventanas y responde con mensajes claros: servicio MySQL detenido, credenciales rechazadas o base de datos inexistente.

### Added
- **Auto-aprovisionamiento del primer arranque**: si el servidor responde pero la BD no existe, la app ofrece crearla con un clic (schema completo embebido en el ensamblado — misma fuente que `scripts/db/001_create_schema.sql`). Verificado end-to-end: crear BD desde cero → wizard de cuenta → sesión operativa. Si el usuario MySQL configurado no tiene permiso CREATE (el dedicado `facontrol`, por diseño), el mensaje dirige al INSTALL.md.
- `VerificadorBaseDatos` (Data) con diagnóstico `Lista/FaltaBaseDatos/CredencialesInvalidas/SinServidor` + 5 tests de integración nuevos (76 tests en total).

### Seguridad
- `App.config` versionado ahora lleva placeholders (`CAMBIAR_USUARIO`/`CAMBIAR_PASSWORD`): las credenciales locales de Dev ya no viven en el repositorio público (protegidas además con `git update-index --skip-worktree`).

## [1.0.0] — 2026-07-10 · Fase 7 (Empaquetado y entrega) — TODAS LAS FASES COMPLETAS

### Added
- **Instalador** `FAControl_Setup_1.0.0.exe` (Inno Setup 6, español, 60 MB): app publicada **self-contained win-x64** (el cliente NO necesita instalar .NET), acceso directo, scripts de BD y documentación incluidos. El App.config no se pisa en actualizaciones; permisos de escritura para logs/ajustes.
- **`scripts/db/003_crear_usuario_dedicado.sql`**: usuario MySQL `facontrol` con permisos mínimos (sin DELETE/DROP — la app usa soft deletes) para no correr como root donde el cliente.
- **`docs/INSTALL.md`**: guía de instalación técnica paso a paso (MySQL, BD, usuario dedicado, config, checklist post-instalación, migración de PC, problemas comunes).
- **`docs/MANUAL.md`**: manual del usuario final pantalla por pantalla, en lenguaje no técnico (pedido de Yuber), con la rutina de respaldo destacada y preguntas frecuentes.
- **Ícono de la aplicación** (`Assets/facontrol.ico`): cuadrado redondeado indigo con la "P" del logo, 7 tamaños (16–256px). Se ve en el Explorador, la barra de tareas, las ventanas y el instalador — adiós al ícono de "app desconocida".

## [0.5.0] — 2026-07-10 · Fase 6 (Reportes, Historial, Configuración) + Notificador de vencimientos

### Added
- **Reporte "Ingresos por período"** (fiel al mockup): rango de fechas con atajos (Este mes / Mes pasado / Trimestre / Año), KPIs — Ganancia (interés cobrado, card indigo), Capital recuperado, Total cobrado, Cuotas cobradas "X de Y programadas" —, gráfico de barras apiladas por semana (interés+capital) y desglose semanal con fila de totales. Botón Exportar Excel.
- **Historial**: visor de solo lectura de la auditoría con filtros por fecha, entidad y acción (límite 300, aviso para afinar filtros).
- **Configuración**:
  - Cambio de contraseña (actual + nueva + confirmación, errores inline).
  - **Tamaño de texto Pequeño/Mediano/Grande** (pedido de Yuber): escala toda la UI (1.0/1.12/1.25) al instante y persiste en `ajustes.json`.
  - **Respaldo/restauración** de la BD con mysqldump/mysql (búsqueda automática del binario, contraseña por MYSQL_PWD, doble confirmación al restaurar).
  - **Exportación a Excel** (ClosedXML 0.105, MIT): libro .xlsx con hojas Clientes/Préstamos/Cuotas/Pagos/Auditoría; manual y **automática** al abrir la app (cada N días configurable, carpeta elegible, activable).
- **Notificador de vencimientos** (pedido del cliente, estilo POS-400): al iniciar sesión —y al cambiar el día de negocio con la app abierta— avisa qué clientes se pasaron de su fecha de pago (lista en rojo con cuotas, monto y fecha). Botón OK + checkbox "No volver a preguntar por este cliente" (persistente, individual). Activable y con "restablecer silenciados" en Configuración.

### Decisiones
- El mockup de Reportes define UN reporte (Ingresos por período); se implementó ese. Los "6 tipos" del plan original quedan abiertos a definirse con el cliente (BLOCKERS.md).
- La migración de PC recomendada es Respaldar/Restaurar (.sql, conserva ids y relaciones); el Excel es de consulta. Importar desde Excel se descartó por riesgo de integridad (BLOCKERS.md).

## [0.4.0] — 2026-07-10 · Fase 5 (Dashboard) + ajustes finos de UI

### Added
- **Panel de control** (pantalla de inicio real): 4 KPIs — Capital colocado (saldo por cobrar de activos), Cobros del mes con **delta vs mes anterior** (↑/↓ %), Clientes activos, Morosidad en RD$ y % del capital.
- **Panel de alertas de cobro** (60%): cuotas vencidas, en mora o que vencen en ≤ 7 días, con pill de semáforo y botón **Cobrar** que navega directo a Cobros con el préstamo preseleccionado.
- **Gráfico de cobros diarios** del mes en curso (LiveChartsCore 2.0.5, barras indigo redondeadas, días sin cobros en 0).
- **Últimos movimientos**: los 10 pagos más recientes.
- `DashboardRepository`/`DashboardService`: agregados en una sola pasada; límites de mes calculados en hora de negocio RD (UTC-4) y convertidos a UTC.

### Changed (ajustes finos pedidos por Yuber)
- Encabezados "Estado", "Nombre" (Clientes) y "Cliente" (Cobros) alineados a la izquierda; el resto sigue centrado.
- Columnas de acciones más anchas: "Ver detalle" y "Ver ficha" ya no se cortan.
- ComboBox: texto centrado verticalmente en toda la app.
- Formularios de Nuevo préstamo y Registrar pago: aire de 12px entre los inputs y el scrollbar.
- App y Views ahora apuntan a `net8.0-windows10.0.19041.0` (asset moderno de SkiaSharp, sin warnings NU1701).

## [0.3.1] — 2026-07-10 · Pulido de UI (10 observaciones de Yuber)

### Added
- **Totales del grid de Préstamos**: 4 cards (capital prestado, por cobrar en activos, total cobrado, préstamos activos) que se recalculan con cada búsqueda/filtro.
- **Filtros rápidos**: por estado en Préstamos (Todos/Activos/Pagados/Cancelados) y por situación en Clientes (con/sin préstamos activos, con saldo pendiente).
- **Iconos en el sidebar** con glifos nativos Segoe MDL2 Assets (sin dependencias).

### Changed
- El **sidebar refleja la navegación interna**: crear un préstamo te lleva al detalle y ahora marca "Préstamos" (antes quedaba en "Nuevo préstamo").
- **ScrollBars delgados (6px)** en toda la app, con thumb redondeado y hover — discretos pero funcionales.
- **Encabezados de tabla centrados** y **scroll horizontal** en las tablas cuando las columnas no caben (el usuario también puede redimensionar columnas).
- La ventana principal abre **maximizada** por defecto.
- Botón de cerrar sesión ahora es **circular** con hover indigo.

### Pendiente registrado
- Documentación final detallada y fácil de entender → Fase 7. Filtros del Historial → Fase 6.

## [0.3.0] — 2026-07-10 · Fase 2 (Clientes) + ajustes de UI

### Added
- **Módulo Clientes completo**: lista con búsqueda (nombre/cédula/teléfono) y agregados por SQL, ficha con 5 métricas (total prestado, cobrado, saldo, préstamos activos, cuotas vencidas) + datos de contacto + sus préstamos, formulario nuevo/editar con validación inline.
- **`ClienteService`**: normalización de cédula dominicana (11 dígitos → `000-0000000-0`; pasaportes se aceptan tal cual), unicidad de cédula amigable, soft delete **bloqueado si hay préstamos activos**, auditoría de crear/modificar/eliminar.
- Flujo ficha → "+ Nuevo préstamo" con el cliente preseleccionado en el wizard.
- Tests: 8 unitarios de normalización de cédula + 2 de integración (CRUD protegido y métricas).

### Fixed
- **LoginWindow**: `SizeToContent="Height"` — en el wizard de primer arranque el botón "Crear cuenta" quedaba cortado por la altura fija (reporte de Yuber).

### Notas
- Pedidos nuevos registrados para Fase 6 (Configuración): tamaño de texto (Pequeño/Mediano/Grande) y exportar/importar datos a Excel con export automático programable. Ver TODO.md.

## [0.2.0] — 2026-07-10 · Fase 3 completa (Préstamos) + Fase 4 (Cobros)

### Added
- **Persistencia de préstamos**: `PrestamoRepository`, `ClienteRepository` (lectura), `PagoRepository`, `ContadorRepository` (correlativos atómicos con `SELECT ... FOR UPDATE`).
- **`PrestamoService`**: creación atómica (contador → prestamo → N cuotas → auditoría en UNA transacción, código P-0001) y cancelación (cuotas impagas → `'cancelada'`, jamás se borran).
- **`PagoService`**: los 4 escenarios de cobro — pago exacto, abono parcial (primero interés, luego capital), adelanto en cascada y liquidación anticipada (cuotas futuras pagan solo capital; el interés futuro se exonera). Todo cobro es una transacción con cuotas bloqueadas (`FOR UPDATE`) y `numero_recibo` R-000001 atómico.
- **UI Préstamos**: lista con búsqueda y agregados en SQL, detalle con métricas + tabla de cuotas con semáforo (indicador rojo lateral en vencidas), wizard "Nuevo préstamo" con vista previa de amortización EN VIVO.
- **UI Cobros**: selector de préstamo activo, cuotas pendientes, atajos (cuota completa / liquidar), preview de cómo se distribuye el monto antes de confirmar, historial de pagos recientes.
- **Recibo 80mm** (patrón imagen del POS-400): el mismo visual se muestra, se imprime (`PrintVisual`) y se exporta a PDF (PdfSharp 6.1.1, rasterizado a 192 DPI).
- **Navegación por páginas**: ContentControl + DataTemplates (ViewModel → View); flujos lista → detalle → cobros cableados por eventos.
- **`IDialogService`** inyectable (confirmaciones/errores testeables; MessageBox solo en la capa App).
- Estilos DESIGN.md: tablas (header uppercase, filas 44px, hover, selección indigo), pills de semáforo y de estado de préstamo, botones Destructivo/Terciario.
- **Tests**: 17 nuevos de distribución de pagos (57 unitarios en total) + 2 de integración contra `facontrol_test` (BD real recreada por corrida: flujo completo crear → abonar → adelantar → liquidar, y cancelación).

### Decisiones
- **Liquidación anticipada** exonera el interés de cuotas futuras (solo se cobra su capital pendiente). Corregible con el cliente — ver BLOCKERS.md.
- **Recibos multi-cuota**: `pago.numero_recibo` es UNIQUE por fila; un cobro que afecta varias cuotas genera un recibo por cuota y el impreso agrupa la operación bajo el primer número.
- PdfSharp 6.1.1 para el PDF del recibo (el mismo visual WPF rasterizado — papel y archivo idénticos).

## [0.1.0] — 2026-07-10 · Fase 1 (Cimientos) + Fase 3 núcleo (Amortización)

### Added
- Solución .NET 8 con arquitectura por capas: App / Views / ViewModels / Models / Services / Data / Common / Printing + 2 proyectos de tests (xUnit + FluentAssertions 7).
- Esquema MySQL `facontrol_db` (8 tablas: usuario, sesion, cliente, prestamo, cuota, pago, auditoria, contador) con scripts de creación, seed Dev y rollback.
- `AmortizacionService`: interés simple dominicano (default) y sistema francés, con redondeo AwayFromZero y ajuste en última cuota. 100% decimal, sin double.
- `CuotaEstadoCalculator`: semáforo de cobros (al día / por vencer / vencida / en mora / pagada / cancelada) con 100% de ramas testeadas.
- 40 tests unitarios verdes (incluye validación exacta contra el mockup: 75,000 al 5% × 12 → cuota 8,461.91).
- Autenticación mono-usuario: wizard de primer arranque, login BCrypt cost 12, rate-limiting (5 intentos → bloqueo 5 min), registro de sesiones, cambio de contraseña.
- `AuditoriaService` con variante transaccional para operaciones multi-paso.
- Shell WPF: LoginWindow + MainWindow con sidebar de 240px (8 secciones) según DESIGN.md (paleta indigo, tipografía, estilos de botones/inputs/cards, converters de moneda RD$ y fecha).
- Serilog a archivo rotativo diario en `logs/`.
- Documentación: `docs/AMORTIZATION.md`, `PTV300-PATTERNS.md`, `TODO.md`, `BLOCKERS.md`.

### Decisiones
- Tasa de interés se ingresa MENSUAL y se convierte por período (÷2 quincenal, ÷4 semanal, ÷30 diaria) — ver AMORTIZATION.md §1.
- ENUM `cuota.estado` amplía la spec con `'cancelada'` (exigido por la regla de cancelación de préstamos).
- FluentAssertions fijado en 7.2.0 (la v8 requiere licencia comercial de pago).
