# TODO.md — FAControl

> Actualizado: 2026-08-20 (ronda de la prueba de Veronica — version 2.0.2)

## Ronda del comprobante fiscal y las impresiones (2026-08-27)

Pedido del cliente en `Freelancer - Claude Active\FamControl\About the FamControl.txt`
y en los screenshots del 26 y 27 de agosto.

- [x] Comprobante fiscal por COBRO, no por prestamo (041). Era el reporte de
      Veronica: las 24 facturas de un prestamo salian con el mismo NCF porque
      `PagoService` copiaba `prestamo.Ncf` al recibo de cada cobro
- [x] NCF manual + switch en "Registrar pago" (PrestControl)
- [x] NCF manual + switch en cobro de alquiler y en abono a plazo (DealControl, 042)
- [x] `Switch.Moderno` en `Controles.xaml` — el "checkbox moderno" pedido
- [x] Columna NCF en las grillas de cobros del dealer (no imprimen recibo suelto)
- [x] `GuardarPdf` deriva el tamano del visual: la factura de venta y la ficha
      de vehiculo se guardaban en 80mm con contenido de hoja carta adentro
- [x] Titulo propio en cada PDF (todos decian "Recibo de pago")
- [x] La hoja del prestamo pasa a FlowDocument: con `PrintVisual` se recortaba
      la tabla de amortizacion, mismo defecto que el BLOCKER del pagare
- [x] 429 tests en verde (306 servicios + 123 base). Se reescribio
      `El_comprobante_sale_impreso_en_el_recibo_del_cobro`, que afirmaba el
      comportamiento VIEJO, y se agregaron 3 casos del reporte del cliente

- [x] Interes recalculado sobre capital rebajado en el prestamo abierto (043).
      Acotado a SoloInteres, solo con abono, y solo sobre cuotas NO vencidas
- [x] `cuota.capital_pagado`: el reparto interes/capital se GUARDA en vez de
      deducirse. La regla vieja "primero interes" se comia una cuota de interes
      de cada abono (820,000 en vez de 800,000), y el error se acumulaba
- [x] Recibo: "Pagado" si la cuota quedo saldada, "Abonado (parcial)" si no
- [x] Regresion propia corregida: `saldoAntes` se leia DESPUES del bucle que
      ahora actualiza las cuotas en memoria, y el recibo restaba el cobro dos
      veces. La cazo `FlujoPrestamoPagoTests`
- [x] 439 tests en verde (311 servicios + 128 base)

### Pendiente de probar a mano
- [ ] Prestamo abierto: abonar a capital y verificar que la cuota del mes que
      viene baja, y que las vencidas NO se mueven
- [ ] Dos abonos seguidos al mismo prestamo abierto: el segundo tiene que
      calcularse sobre el capital ya rebajado
- [ ] Cobrar una cuota completa y confirmar que el recibo dice "Pagado"
- [ ] Cobrar parcial y confirmar que dice "Abonado (parcial)"
- [ ] Cobrar dos cuotas del mismo prestamo con el switch prendido y verificar
      que salen DOS comprobantes distintos en los recibos
- [ ] Cobrar sin comprobante: el recibo no debe mostrar ninguno (antes mostraba
      el del prestamo)
- [ ] Pegar un e-NCF a mano y confirmar que la secuencia local NO se mueve
- [ ] Guardar una factura de venta y una ficha de vehiculo como PDF: tienen que
      salir en hoja carta, no en tira de 8cm
- [ ] Imprimir un prestamo de 24+ cuotas y contar que la tabla llegue completa
- [ ] Cobrar un alquiler y un plazo de venta con NCF, y ver la columna en la grilla

### Pendiente de hacer
- [ ] **Pagare notarial** (plantilla recibida el 2026-08-26). El cliente confirmo
      el 2026-08-27: "Vamos hacer ese solo" — el hipotecario y la oposicion de
      matricula quedan fuera por ahora. Necesita:
      - conversor numero -> letras en espanol (no existe en el proyecto)
      - nacionalidad / estado civil / ocupacion del cliente (no estan en el modelo)
      - notario, representante y 2 testigos configurables
      - las dos formas que pidio el cliente: automatico y editable
- [ ] Barrido general de errores en los 3 modos
- [ ] Version 2.1.0: actualizador + instalador recompilados

## Ronda de la prueba de Veronica (2026-08-20) — 2.0.2

- [x] Icono propio: monograma FA sobre navy, generado del logo vectorial
      (`scripts/marca/generar_icono.py`), 9 tamanos. Reemplaza la "P" morada
      heredada de la plantilla
- [x] Pagare al crear un prestamo: "no se puede establecer Owner en un Window
      que se ha cerrado". Las vistas se sueltan del ViewModel singleton al salir
      de pantalla, y ninguna ventana se cuelga de otra ya cerrada (`VentanaDuena`)
- [x] Corregir un prestamo diferido conservaba la cuota pactada: la ventana de
      correccion no mandaba `CuotaInicioCapital` y el servicio recalculaba con
      la sugerida, cambiandole la tabla al cliente
- [x] `docs/CORREO-GMAIL.md`: paso a paso del correo automatico, empezando por
      prender la verificacion en 2 pasos (sin eso Google esconde la pagina de
      contrasenas de aplicacion). La ayuda en Configuracion tambien
- [x] Barrido de invariantes de los calculos del POS-500
      (`AuditoriaCalculosPosTests`): totales, ITBIS, redondeo, cambio, comision,
      numero de factura y semaforo de caducidad. No encontro errores
- [x] `scripts/calidad/verificar_recursos_xaml.py` traido de MED-100: 90 claves
      usadas, todas definidas
- [x] Actualizador e instalador 2.0.2 recompilados
- [~] "¿Desde que cuota se cobra el capital?": se dio vuelta la pregunta y se
      REVIRTIO. Era confusion de uso, no un bug — el campo ya permite escribir
      la cuota. Si vuelve a aparecer el reporte, es tema de explicacion, no de
      codigo

### Pendiente de probar a mano
- [ ] Crear un prestamo despues de "Cambiar de usuario": el pagare tiene que
      salir (es el camino exacto que fallaba)
- [ ] Prestamo diferido: crear con "capital desde la 4", corregirlo sin tocar
      nada y verificar que la tabla NO se mueve
- [ ] Correo: seguir `docs/CORREO-GMAIL.md` con la cuenta del negocio y mandar
      la prueba
- [ ] Confirmar que el icono nuevo aparece en la barra de tareas y el escritorio
      (si queda el viejo: `ie4uinit.exe -show`)

### Deuda conocida (no bloquea)
- [ ] Las vistas que NO se suscriben a eventos del ViewModel (listas, formularios)
      siguen sin ciclo de vida explicito. No hacen dano —no abren ventanas— pero
      el dia que una lo haga, hay que engancharla con el mismo patron de
      Reenganchar/Desenganchar

## Ronda de correcciones y cierre de PrestControl (2026-07-30)

- [x] Panel y Reportes visibles en el sidebar del POS
- [x] Una sola base: las tablas del POS pasan a facontrol_db con prefijo pos_ (024)
- [x] Roles y permisos del POS-500 en la pantalla de Usuarios
- [x] Historial acotado por modulo, con filtro y columna de modulo (025)
- [x] Comprobante fiscal a la derecha en el detalle del prestamo
- [x] Seccion "Impresion del ticket" en Configuracion del POS (impresora, vista previa, copias)
- [x] Instalador con los tres prerequisitos adentro
- [x] Expediente digital tambien para prestamos (026)
- [x] Contratos de PrestControl con su expediente
- [x] Pagare e intimacion archivados solos al imprimirlos

### Pendiente de probar a mano
- [ ] Contratos: subir un contrato firmado, abrirlo con doble clic, bajar el ZIP
- [ ] Imprimir un pagare y verificar que aparece solo en el expediente
- [ ] Imprimir una intimacion y lo mismo
- [ ] POS: elegir la impresora del mostrador y cobrar sin que pida guardar PDF

## POS-500 integrado a la suite (2026-07-30)

- [x] `ModoApp.Pos500` como cuarto modo, habilitado con el código 5 de la licencia
- [x] Base propia `pos500_db`, creada sola al entrar al modo por primera vez
- [x] 022: permisos y roles del punto de venta dentro de la base compartida
- [x] 023: la auditoría acepta la acción `anular`
- [x] Models, Data, Services e impresión portados a `FAControl.*.Pos`
- [x] La auditoría de una venta entra en la misma transacción aunque viva en otra base
- [x] Las 9 pantallas dentro del shell, con un solo login y los permisos de la suite
- [x] Tickets, reimpresión y cierre de caja enganchados
- [x] Respaldo automático de las dos bases
- [x] Instalador 1.7.0

### Pendiente de probar a mano
- [ ] Recorrer el punto de venta con el guion de pruebas (vender, anular, cuadrar, imprimir)
- [ ] Confirmar que un Cajero ve solo lo suyo y un Supervisor ve todo

## Pedidos del cliente 2026-07-29

### FAControl (la suite)
- [x] Botón de ayuda con el teléfono del desarrollador (849-438-0242) en el launcher y en cada módulo
- [x] Casilla de arranque directo: abrir siempre el mismo módulo, apagable desde Configuración
- [x] Siete códigos con activación POR MÓDULO; los de módulo solo se piden al terminar la prueba
- [x] Código 7 "eliminar todo" (sin respaldo) con palabra de confirmación y doble aviso
- [x] Usuario Programador por defecto (`Yub`) sembrado con el esquema, sin saltarse el wizard inicial
- [x] AutoControl retirado; POS-500 ocupa su lugar en el launcher como producto a la venta
- [x] Instalador con prerequisitos opcionales (MySQL / AnyDesk / Google Drive) + icono
- [x] Documento de cómo interconectar 3 PC contra una 4ta como servidor MySQL
- [x] Guía de pruebas para dar el visto bueno antes del instalador definitivo

### PrestControl
- [x] Comprobante fiscal probado con la autorización real de la DGII (B01, 15 números, vence 31/12/2027)
- [x] Método de amortización **abierto (solo interés)**: lo necesitaban 7 de los 10 préstamos reales
- [x] Cartera real cargada (10 clientes) con informe de inconsistencias del listado
- [x] Aislamiento por estancia verificado contra MySQL (clientes separados; usuarios/roles/permisos compartidos)

### Pendiente del cliente (no del desarrollo)
- [ ] Confirmar con Familia Almonte las 8 dudas del listado de préstamos
      (ver `FAControl_CarteraReal_Informe_v1_2026-07-30.md`, sección 7)
- [ ] Dejar los instaladores de AnyDesk, MySQL y Google Drive en `installer\prerequisitos\`
- [ ] Decidir si la licencia se cobra por sistema o por PC (relevante para las 4 PC en red)

## ✅ Pedidos del cliente 2026-07-27 (COMPLETO)

### FAControl (los tres modos)
- [x] 4 códigos digitables en el launcher: prueba de 14 días, activación, recuperar acceso, restablecer todo
- [x] Licencia local firmada (`licencia.json` + marca en el registro); códigos solo hasheados en el binario
- [x] Códigos documentados en `Freelancer - Claude Save\docs\Done\FAControl_CodigosDeActivacion_v1_2026-07-27.md` (NO va al repo)
- [x] Rol **Programador** (017): autoridad total, invisible e intocable para el Admin; solo otro Programador lo crea

### DealControl
- [x] Grids con alto fijo, columnas por contenido y scroll horizontal real
- [x] Panel: textos legibles en modo noche (la tabla de movimientos usaba TextBlocks sin estilo)
- [x] Ficha de cliente propia del dealer (transferido / cobrado / pendiente / vehículos) + grid de sus vehículos con "Ver ficha"
- [x] Gráficos en Panel (6 meses de ventas vs alquiler + torta del inventario) y en Reportes (origen del dinero + por vendedor)
- [x] Expediente digital (018): subida múltiple, vista lista/cuadrícula, abrir/guardar/re-ubicar/eliminar, ZIP y respaldo
- [x] Factura: reemplazar por la firmada y escaneada
- [x] **Instalador 1.5.0** self-contained + todas las migraciones (sin rollback ni seeds)

- Nota: **AutoControl sigue de lado** por pedido del cliente.

## ✅ Pedidos del cliente 2026-07-25 (COMPLETO)

### PrestControl
- [x] Pagaré: tasa en el texto principal; "Púrpura Datos" → nombre de la empresa
- [x] Factura/recibo con logo, empresa, RNC, teléfono y comprobante fiscal + Datos del negocio en Configuración
- [x] Reportes: total prestado y proyección a ganar
- [x] Comprobante fiscal DGII (012): registrar e-NCF del Facturador Gratuito o asignar de secuencia local con reserva atómica
- [x] Permisos por pantalla de vuelta (013): checkboxes por modo, precargados por el rol, sin mezclar estancias
- [x] Préstamo antiguo con fecha atrasada: autodetección + cuotas pagadas con recibos históricos

### DealControl
- [x] Panel principal propio (014), sin datos de PrestControl
- [x] Inventario ampliado (015): matrícula, año/chasis/color en el grid, ficha con comprador y reparaciones, PDF
- [x] Vendedor: no ve costos ni ganancias (solo modelo/marca/chasis/año/color/precio/nota); sí vende
- [x] Facturación: ver/imprimir con marca, cliente, vehículo y firmas
- [x] Financiamiento por plazos (016): inicial + N pagos, separación con 15 días de derecho, cobro con recibo RV-000001
- [x] Carta de compromiso y recibo de separación imprimibles
- [x] Contratos del dealer: expediente con cliente, vendedor, documentos, matrícula y estado de plazos
- [x] Reportes propios con comisiones por vendedor (% configurable)
- Nota: **AutoControl quedó de lado por pedido del cliente** en esta ronda.

## ✅ Roles por modo (COMPLETO — 2026-07-19)
- [x] Esquema: `usuario_modo_rol`, `rol.modo`, clave única `(nombre, modo)`, roles Encargado/Vendedor y permisos propios de Dealer/Auto (`inventario`, `inventario_editar`, `ventas`, `alquileres`, `gastos`) — migración 011 + espejo en 001
- [x] Auth: el login muestra el rol del modo en el que entró (no un rol global); `usuario_permiso` sigue siendo la unión efectiva
- [x] Usuarios UI: check "Administrador" + un ComboBox de rol por cada modo ("Sin acceso" = no entra); `GuardarRolesPorModoAsync` recalcula la unión atómicamente
- [x] Wiring: sidebar y services de Dealer/Auto usan los permisos nuevos (`Inventario/InventarioEditar/Ventas/Alquileres/Gastos`)
- [x] Migración 011 aplicada a `facontrol_db`; build sin warnings; 123 tests verdes; smoke SQL + harness del camino de escritura OK

## ✅ Aislamiento por estancia + acceso por modo (COMPLETO — 2026-07-18)
- [x] 3 dominios aislados de clientes (`cliente.ambito`), cédula única por ámbito; todas las lecturas de clientes scoped al modo activo
- [x] Aislamiento PrestControl ↔ AutoControl en Cobros, Contratos, Panel y Reportes (filtro `vehiculo_id` por modo)
- [x] Permisos de acceso por modo (`acceso_prestcontrol/dealercontrol/autocontrol`); puerta en el login; Admin siempre entra a todo
- [x] Clientes habilitado en los 3 modos; migración 010 + espejo en 001; build limpio; 114 tests unitarios verdes
- [ ] **Pendiente de verificación con MySQL arrancado**: aplicar `010_ambitos.sql` a `facontrol_db`, correr tests de integración (Data.Tests) y smoke (login rechazado sin acceso; clientes no se cruzan entre modos)
- Nota: ficha de cliente en Dealer/Auto muestra la sección de préstamos vacía (es de crédito); afinar si el cliente lo pide. Export a Excel aún no filtra por modo (acción manual de Admin).

## ✅ Tier 5 — DealerControl + AutoControl (COMPLETO)
- [x] 5.1 Dominio `vehiculo` (schema 001+008, modelo/repo/service, código V-0001, tests)
- [x] 5.2 Shell mode-aware + Inventario de vehículos (lista + formulario CRUD)
- [x] 5.3 DealerControl: venta al contado (VC-0001), rent a car (AL-0001, con devolución), gestión de importación (ledger de gastos)
- [x] 5.4 AutoControl: crédito vehicular = `prestamo` con `vehiculo_id` (garantía), reusa amortización/cuotas/cobros/pagaré; al financiar el vehículo pasa a vendido
- [x] Migración 009 + reorden de `vehiculo` en 001; 114 tests verdes; flujos atómicos verificados
- Pendiente de pulido (para revisión de Yuber): paletas por modo (hoy usan el acento global indigo), inicial/enganche en crédito vehicular, panel/dashboard propio de Dealer y Auto.

## ✅ Tier 4 — Cobranza y comunicación (COMPLETO)
- [x] Fix filtro por usuario en Reportes (backfill `pago.created_by` — migración 007)
- [x] Reporte por cliente (individual y global) imprimible
- [x] Fix responsive: textos/dígitos ya no se cortan al escalar en cada pantalla
- [x] Almacén de contratos con vista previa lateral del pagaré
- [x] Datos del negocio configurables + doc NCF/DGII
- [x] Recordatorios de cobro por Gmail (SMTP + DPAPI) — cliente y dueño; WhatsApp diferido y documentado
- [x] Respaldo automático cada N días/meses
- [x] Intimación de pago imprimible para cuotas vencidas (no "mandamiento" — ver doc)

## ✅ Fase 2 — Clientes (COMPLETA)
- [x] Lista con búsqueda por nombre/cédula/teléfono + agregados (préstamos activos, saldo)
- [x] Ficha: 5 métricas + contacto + préstamos del cliente + doble click al detalle
- [x] Formulario nuevo/editar con validación inline (cédula normalizada a 000-0000000-0)
- [x] Soft delete protegido: no se elimina un cliente con préstamos activos
- [x] Auditoría de crear/modificar/eliminar
- [x] "+ Nuevo préstamo" desde la ficha preselecciona el cliente en el wizard
- [x] Fix: LoginWindow crece con el contenido (botón cortado en el wizard inicial)

## ✅ Fase 1 — Cimientos (COMPLETA)
- [x] Solución `.sln` con 8 proyectos src + 2 de tests, regla de dependencias respetada
- [x] `App.config` externo con cadena de conexión (Dev: root local)
- [x] `001_create_schema.sql` ejecutado en MySQL local (8 tablas) + seed + rollback
- [x] `ConexionFactory` (CConexion adaptado: async, config externa, sin UI)
- [x] Autenticación BCrypt cost 12: wizard de cuenta inicial + login + rate-limiting (5 intentos → 5 min)
- [x] `SesionActual` estático simplificado (Id, Username, Nombre, LoginAt, SesionId)
- [x] `AuditoriaService` funcional (con variante transaccional para operaciones multi-paso)
- [x] `MainWindow` con sidebar navegable (8 secciones)
- [x] DESIGN.md aplicado: paleta, tipografía, estilos de botón/input/card/sidebar, MoneyConverter, DateConverter
- [x] `PTV300-PATTERNS.md` documentado

## ✅ Fase 3 — Préstamos y amortización (COMPLETA)
- [x] `AmortizacionService`: interés simple dominicano (default) + sistema francés (40 tests)
- [x] `CuotaEstadoCalculator` (semáforo) con 100% de ramas cubiertas
- [x] `docs/AMORTIZATION.md` con la matemática y la decisión de convención de tasa
- [x] `PrestamoRepository` + `ClienteRepository` + `ContadorRepository` (`SELECT ... FOR UPDATE`)
- [x] `PrestamoService.CrearAsync`: transacción atómica contador → prestamo → N cuotas → auditoría
- [x] Generación de `codigo` P-0001 vía tabla `contador`
- [x] `PrestamoService.CancelarAsync`: cuotas impagas → 'cancelada' (nunca se borran)
- [x] Wizard "Nuevo préstamo" con vista previa de amortización EN VIVO + resumen
- [x] Lista de préstamos (búsqueda, agregados por SQL, pills de estado)
- [x] Detalle de préstamo (métricas, contrato, tabla de cuotas con semáforo e indicador rojo en vencidas)
- [x] Navegación por páginas (ContentControl + DataTemplates): lista → detalle → cobros

## ✅ Fase 4 — Cobros (COMPLETA)
- [x] `PagoService`: pago exacto, abono parcial (interés primero), adelanto en cascada,
      liquidación anticipada (exonera interés futuro) — 17 tests de la lógica pura
- [x] `numero_recibo` atómico R-000001 (contador + FOR UPDATE, dentro de la transacción del cobro)
- [x] Transacción de cobro: cuotas FOR UPDATE → N pagos → update cuotas → estado préstamo → auditoría
- [x] Módulo Cobros: selector de préstamo activo, cuotas pendientes con semáforo,
      atajos (cuota completa / liquidar), preview de distribución, pagos recientes
- [x] Recibo 80mm como visual WPF (patrón imagen POS-400): vista previa + impresión (PrintVisual) + PDF (PdfSharp)
- [x] Tests de integración contra `facontrol_test`: flujo completo crear → abonar → adelantar → liquidar; cancelación

## ✅ Fase 5 — Dashboard (COMPLETA)
- [x] 4 KPIs: capital colocado, cobros del mes (delta vs mes anterior), clientes activos, morosidad RD$ + %
- [x] Panel de alertas de cobro con semáforo y botón "Cobrar" → Cobros preseleccionado
- [x] Gráfico de cobros diarios del mes (LiveChartsCore 2.0.5)
- [x] Últimos movimientos (10 pagos recientes)

## ✅ Fase 6 — Reportes, Historial y Configuración (COMPLETA)
- [x] Reporte "Ingresos por período" fiel al mockup (KPIs, barras apiladas por semana, desglose, export Excel)
- [x] Historial de auditoría con filtros por fecha, entidad y acción
- [x] Configuración: cambio de contraseña
- [x] Tamaño de texto Pequeño/Mediano/Grande (persistente, escala toda la UI)
- [x] Respaldo/restauración de BD (mysqldump/mysql, doble confirmación)
- [x] Export Excel completo (manual + automático cada N días, activable)
- [x] **Notificador de vencimientos** (pedido del cliente): aviso al iniciar + cambio de día,
      silenciado individual por cliente, activable en Configuración
- Nota: los "6 reportes" del plan original quedaron como el único mockup diseñado — más tipos a definir con el cliente (BLOCKERS #7). Importar desde Excel descartado (BLOCKERS #8).

## ✅ Fase 7 — Empaquetado y entrega (COMPLETA)
- [x] `dotnet publish` self-contained win-x64 (no requiere .NET en la PC del cliente)
- [x] Instalador Inno Setup 6 en español: `installer/Output/FAControl_Setup_1.0.0.exe`
- [x] `scripts/db/003_crear_usuario_dedicado.sql` (usuario `facontrol` con permisos mínimos)
- [x] `docs/INSTALL.md` (guía técnica) + `docs/MANUAL.md` (manual no técnico pantalla por pantalla)
- [x] Proyecto copiado a `Freelancer - Claude Save\`

## 📌 Mejoras futuras (post-entrega, si el cliente las pide)
- [ ] Logout que regrese al login sin cerrar la app (BLOCKERS #3)
- [ ] Pago compensatorio negativo con UI propia (hoy: corrección asistida por técnico)
- [ ] Más tipos de reportes (BLOCKERS #7)
- [ ] Empaquetar fuente Inter .ttf (hoy usa fallback Segoe UI)
- [ ] Datos del negocio (RNC, dirección, teléfono) configurables para el encabezado del recibo
- [ ] Logout que regrese al login sin cerrar la app (hoy cierra la app — decisión pendiente)
- [ ] Empaquetar fuente Inter .ttf (hoy usa fallback Segoe UI)
- [ ] Sidebar: marcar el ítem activo cuando la navegación no viene de un click (detalle/nuevo)
- [ ] Pago compensatorio negativo (corrección de errores) con nota obligatoria — regla definida, UI pendiente
