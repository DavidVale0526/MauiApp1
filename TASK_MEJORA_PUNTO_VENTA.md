# TASK — Mejora visual del punto de venta

**Estado general:** En progreso — diseño implementado; columnas adaptadas al ancho medido; pendiente de revisión visual final  
**Creado:** 24 de octubre de 2026  
**Referencia:** Imagen de punto de venta compartida por el usuario  
**Vista principal:** `MauiApp1/Presentation/Views/SaleRegistrationPage.xaml`

## Objetivo

Mejorar la presentación del punto de venta de Miscelánea Ari tomando como referencia la distribución visual de un catálogo junto a un carrito. Mantener el estilo existente de la aplicación y conservar sin cambios las funciones y reglas de venta en Windows y Android.

## Reglas de alcance

- [ ] Cambiar únicamente presentación, distribución, espaciado, jerarquía visual y adaptación de pantalla.
- [ ] Conservar el flujo actual: seleccionar un producto, introducir cantidad, agregarlo al carrito, revisar el total y continuar a la revisión de venta.
- [ ] Conservar los datos que ya se muestran: nombre, unidad, precio, existencia, cantidad, importe por línea y total.
- [ ] Conservar las reglas existentes de cantidades fraccionarias, existencias, crédito y selección de cliente.
- [ ] Conservar la acción actual para quitar un producto del carrito.
- [ ] Mantener la paleta de Miscelánea Ari: fondo claro, texto azul oscuro y verde para acciones principales.
- [ ] No añadir imágenes de producto ni soporte nuevo para administrarlas.
- [ ] No añadir búsqueda, filtros por categoría, escáner, favoritos, ventas suspendidas, notas, descuentos, botones de incremento/decremento por línea ni atajos de teclado.
- [ ] No cambiar persistencia, modelos, contratos de aplicación, reglas de negocio ni sincronización.
- [ ] Implementar por etapas; cada etapa debe conservar el comportamiento previo antes de avanzar.

## Etapa 0 — Fijar el estado actual

- [x] Inspeccionar `SaleRegistrationPage.xaml`, su code-behind y `SaleRegistrationViewModel` antes de editar.
- [x] Confirmar los puntos de adaptación por ancho de pantalla y cómo se presenta el carrito en escritorio y Android.
- [x] Registrar el flujo actual de selección, ingreso de cantidad, adición al carrito, eliminación de una línea, venta a crédito y paso a revisión.
- [ ] Capturar la vista actual en Windows maximizado, Windows en ventana y Android.
- [x] Anotar los cambios locales existentes y conservarlos. Antes de editar, el único cambio local era este archivo de tareas.

**Criterio de aceptación:** hay una referencia visual y funcional para comparar; no se ha alterado el comportamiento ni el trabajo previo.

## Etapa 1 — Presentar mejor el catálogo en Windows

- [x] Organizar los productos en tarjetas compactas dentro del panel de catálogo.
- [x] Usar únicamente los datos actuales de cada producto: nombre, unidad, precio y existencia.
- [x] Mantener la selección visual del producto y hacer evidente qué elemento está seleccionado.
- [x] Conservar el campo actual de cantidad, su precisión decimal y el botón «Agregar».
- [x] Distribuir las tarjetas en columnas cuando el ancho disponible lo permita y reducir columnas al estrechar la ventana.
- [x] Permitir que nombres largos se ajusten en varias líneas y evitar solapamientos en las tarjetas.
- [x] Mantener el estado vacío actual cuando no haya productos disponibles.

**Implementación:** el catálogo usa una columna en pantallas angostas y calcula hasta cuatro columnas en escritorio según el ancho medido de la propia lista de productos. Se conservan los bindings de selección, cantidad y agregado. El cálculo anterior usaba el ancho general de la página, que podía producir demasiadas columnas; ahora se usa el espacio real del catálogo. Falta confirmar visualmente la corrección en la nueva ejecución.

**Criterio de aceptación:** en Windows se ven más productos de forma ordenada; seleccionar un producto sigue mostrando sus datos y permite añadir cantidades enteras o decimales como antes.

## Etapa 2 — Mejorar la lectura del carrito en Windows

- [x] Mantener el carrito a la vista junto al catálogo en el diseño de escritorio.
- [x] Ordenar visualmente cada línea con nombre, cantidad y unidad, precio e importe.
- [x] Hacer reconocible la acción existente «Quitar» sin cambiar lo que hace.
- [x] Dar jerarquía al total y al botón existente para revisar/continuar la venta.
- [x] Conservar el contador o resumen actual del carrito y sus estados vacíos.
- [x] Mantener los controles existentes de venta a crédito y selección de cliente.
- [x] Mantener el desplazamiento de las líneas separado del resumen fijo y la acción de continuar.

**Implementación:** se compactaron los renglones del carrito, se agrupó la acción «Quitar» con el nombre y se resaltó el total con el verde de la aplicación. No se agregaron controles para modificar la cantidad desde el carrito. La comprobación visual en ejecución queda pendiente.

**Criterio de aceptación:** el contenido y el total del carrito coinciden con los anteriores; cada producto se puede quitar y continuar a revisión con el mismo recorrido.

## Etapa 3 — Adaptar la presentación a Android

- [x] Conservar la distribución vertical apropiada para una pantalla angosta.
- [x] Mantener el acceso actual al carrito y su panel inferior, incluida la navegación para abrirlo y cerrarlo.
- [x] Ajustar tarjetas, márgenes y tipografía para interacción táctil sin reducir áreas de toque.
- [ ] Mantener visible y utilizable el campo de cantidad cuando aparezca el teclado.
- [ ] Evitar que la navegación inferior o el resumen del carrito tapen controles o contenido desplazable.
- [x] Permitir que los nombres largos del catálogo se ajusten; el campo y el formato de cantidades decimales se conservaron.

**Implementación:** Android conserva el catálogo vertical, el resumen inferior y la hoja del carrito. La compilación Android pasó; falta revisar en dispositivo la interacción con teclado y desplazamiento.

**Criterio de aceptación:** el flujo de Android conserva sus acciones actuales y se puede completar una venta sin que teclado, panel del carrito o navegación oculten los controles necesarios.

## Etapa 4 — Revisar estados visuales y tamaños de ventana

**Avance de revisión:** tras recompilar, se abrió la versión de desarrollo y apareció el formulario de inicio de sesión. El intento autorizado no abrió la sesión, así que la revisión visual posterior al ajuste queda pendiente. La revisión visual de Android también queda pendiente.

**Hallazgo y corrección:** en la primera ejecución de desarrollo, incluso maximizada, los productos seguían en una columna. Se ajustó la inicialización de `ItemsLayout` para empezar con cuadrícula en Windows y lista en Android, y se cambió el cálculo para basarse en el ancho medido del catálogo. La cuadrícula debe comprobarse en una sesión iniciada y en varios tamaños de ventana.

- [ ] Revisar el estilo de producto seleccionado usando el color de acento actual.
- [ ] Revisar cómo se lee la existencia disponible usando el texto que ya proporciona el sistema.
- [ ] Comprobar tarjetas con nombre largo, unidad larga y valores de precio o cantidad fraccionaria.
- [ ] Comprobar catálogo vacío, carrito vacío y carrito con varias líneas.
- [ ] Redimensionar la ventana de Windows y revisar que el catálogo, carrito y botones se reacomoden sin cortes.
- [ ] Revisar contraste, foco visible y etiquetas accesibles de los controles existentes.

**Criterio de aceptación:** los estados actuales son comprensibles en los tamaños previstos y ningún control queda oculto, cortado o fuera del área interactiva.

## Etapa 5 — Revisión final de preservación

- [x] Revisar el diff y retirar cambios ajenos al diseño aprobado.
- [ ] Confirmar manualmente que siguen funcionando las acciones enumeradas en las reglas de alcance.
- [x] Compilar los destinos Windows y Android afectados por la presentación.
- [ ] Revisar visualmente Windows maximizado, Windows en ventana y Android.
- [ ] Registrar el resultado, incidencias restantes y evidencia de cada etapa antes de marcarla completa.

**Evidencia de compilación (24/10/2026):** Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron correctamente, ambos con 0 advertencias y 0 errores. La revisión visual posterior al último ajuste no pudo completarse porque el inicio de sesión de desarrollo no abrió la sesión. Android no se revisó en dispositivo.

**Criterio de aceptación:** la vista mantiene las funciones y reglas existentes, la distribución se adapta correctamente a las tres condiciones de revisión y las compilaciones de los destinos afectados terminan correctamente.

## Archivos principales a revisar

- `MauiApp1/Presentation/Views/SaleRegistrationPage.xaml`
- `MauiApp1/Presentation/Views/SaleRegistrationPage.xaml.cs`
- `MauiApp1/Presentation/ViewModels/SaleRegistrationViewModel.cs`

Revisar otros archivos solo si son necesarios para una modificación visual y sin alterar contratos o reglas funcionales.

## Orden de implementación

1. Etapa 0: fijar la referencia actual.
2. Etapa 1: catálogo de Windows.
3. Etapa 2: carrito de Windows.
4. Etapa 3: adaptación de Android.
5. Etapa 4: estados y tamaños de ventana.
6. Etapa 5: revisión de preservación y compilación.
