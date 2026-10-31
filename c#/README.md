# Mindie · Windows nativo

Aplicación de escritorio en **C# y WPF sobre .NET 10**, con **Ollama local** o **Gemma 4 mediante NVIDIA API**. La captura de páginas completas utiliza el runtime Microsoft Edge WebView2.

## Abrir

Ejecuta **`artifacts/win-x64/Mindie.exe`**. La publicación es autocontenida para Windows x64; no necesita Node.js ni instalar .NET. En **Configuración → Inteligencia artificial** elige Ollama local y uno de tus modelos instalados, o NVIDIA API con tu propia clave. NVIDIA no requiere Ollama, descargar Gemma ni una GPU local; requiere Internet. La opción de página completa requiere Microsoft Edge WebView2, ya presente en el equipo donde se verificó esta entrega.

Para desarrollar, abre `Mindie.sln` en Visual Studio con soporte de .NET 10, o ejecuta:

```powershell
dotnet run --project Mindie/Mindie.csproj
```

## El compañero

**Mindie**, la burbujita del logo, es ahora el compañero principal: aparece primero en la galería y se selecciona al actualizar una sola vez. Tiene 24 poses transparentes para parpadeo, movimiento, pensamiento, sueño y comida. Los siete animales anteriores permanecen disponibles; después puedes elegir cualquiera y tu elección se conserva.

El widget muestra **solo el animal**, sin marco, barra de título, nombre, placa ni fondo. Arrástralo a donde quieras y cambia su tamaño en Configuración. Las zonas transparentes dejan pasar los clics. Recuerda su posición y la recupera dentro de una pantalla disponible si desconectas un monitor.

Al acercar el mouse aparece una **cápsula compacta debajo del animal**, con tres iconos pequeños: **Escribir**, **Guardar en mente** y **Mis guardados**. Tiene etiquetas al posar el mouse y admite teclado. Si no hay espacio debajo, se coloca encima.

**Escribir** abre una barra oscura y compacta junto al compañero, sin activar la biblioteca ni el chat completo. Su micrófono permite dictar y revisar el texto antes de enviarlo; Hablar ya no aparece como acción separada en el menú. **+** permite elegir una o varias imágenes, revisar sus miniaturas y quitarlas del borrador. Enter o la flecha envía; mientras llega la respuesta, la barra permite cancelar. Solo cuando la respuesta está lista se abre la conversación existente. Un error conserva texto y adjuntos; Escape o un clic fuera de la mascota y de la barra cierra y cancela la solicitud pendiente. Al usar +, el selector de archivos puede abrirse y utilizarse sin cerrar la barra.

Se admiten hasta **8 imágenes**, de hasta **20 MB por archivo**, con un máximo conjunto de **32 MB tras prepararlas**. PNG, JPG, BMP, GIF y TIFF; de una imagen animada se utiliza la primera pose. Se respeta la orientación de las fotos y se reduce el lado mayor a 1600 píxeles sin ampliar imágenes pequeñas. Los originales permanecen intactos. Las copias se guardan por separado en `%LOCALAPPDATA%/Mindie/chat-images/`, con sus metadatos en el historial. Gemma recibe como máximo las ocho imágenes más recientes de las últimas 16 intervenciones; necesita un modelo con capacidad visual.

El menú permanece abierto mientras el puntero esté en el conjunto del animal y la cápsula, incluido el hueco y un margen lateral para bajar en diagonal hacia cualquiera de los iconos. Al alejarte de ese conjunto espera brevemente antes de cerrarse. Ese margen no bloquea los clics en las aplicaciones que hay debajo. Un clic fuera de la mascota y del menú lo cierra inmediatamente, sin consumir el clic destinado a la otra aplicación.

Al pedir **Guardar en mente desde la mascota**, tanto con el icono como con el clic derecho, el selector aparece sobre la aplicación que estás usando y en el monitor del compañero. La biblioteca permanece detrás, minimizada u oculta tal como estaba. El lector web, Dar contexto y el editor continúan en ventanas independientes; guardar añade la tarjeta sin abrir ni cambiar la sección de la biblioteca. El editor tiene su propia entrada en la barra de tareas y permanece visible si ocultas la mascota. Tras seleccionar un área se recupera el orden de las ventanas sin traer la biblioteca al frente.

El clic derecho ofrece **Guardar en mente**, **Mis guardados**, los estados del animal y las acciones para despertarlo, ocultarlo o salir. La ventana principal conserva sus controles nativos de minimizar, maximizar y cerrar.

El widget es un **20 % más pequeño**: el tamaño inicial pasa de 180 a 144 píxeles lógicos y el intervalo ajustable es de 88 a 224. Al abrir esta versión, el tamaño que ya tenías guardado se reduce al 80 % una sola vez; conserva tu posición y el resto de tus datos.

## Animaciones

Los ocho compañeros —Mindie, pollito, cochinito, ardilla, perrito, gatito, león y cocodrilo— tienen **24 fotogramas cada uno**, repartidos entre seis estados:

| Estado | Comportamiento |
| --- | --- |
| Reposo | Parpadeos espaciados |
| Movimiento | Gestos y pequeños rebotes mientras arrastras la mascota; vuelve a reposo al soltarla |
| Pensando | Mientras Gemma responde o analiza una captura |
| Con sueño | Bostezo y cabeceo tras un rato sin interacción |
| Dormido | Acurrucado, con respiración suave |
| Comiendo / bebiendo | Galleta, manzana, bellota o una taza, según el animal |

Las tarjetas de selección y las vistas de la aplicación son estáticas. Las animaciones se reproducen únicamente en el widget del escritorio. El menú del animal permite probar y mantener sus estados hasta que lo despiertes. **Reducir el movimiento** y la preferencia de animación de Windows mantienen una pose por estado.

Se reproducen fotogramas reales. El cargador identifica la silueta de cada frame y alinea la escala y el apoyo en memoria para evitar patas cortadas o restos de filas vecinas. Los PNG originales permanecen intactos.

## Crear tu propia mascota

En **Compañeros → Crear mascota** encontrarás un prompt completo y fijo, de solo lectura, y los botones **Copiar prompt** y **Abrir ChatGPT**. Pégalo en ChatGPT y cambia allí «pequeño dragón de arcilla verde» por el personaje que quieras. También puedes subir una imagen a ChatGPT y sustituir la descripción del personaje por «inspírate en la imagen». Conserva las instrucciones técnicas y descarga el PNG original que genere.

En la misma sección de Mindie, escribe un nombre y una descripción y utiliza **Elegir hoja PNG** o arrastra el archivo al recuadro. El formato requerido por Mindie es una hoja transparente de **4 columnas × 6 filas**, 24 poses en celdas cuadradas; por ejemplo **1024×1536 px**. Las filas son reposo/parpadeo, movimiento, pensando, con sueño, dormido y comer/beber. El prompt ya incluye todas las especificaciones. Si ChatGPT entrega otra distribución o un fondo pintado, pídele que corrija la imagen antes de importarla.

Mindie valida tamaño, transparencia, márgenes, presencia de los 24 fotogramas y variaciones entre poses. Acepta PNG de hasta 25 MB y entre 256×384 y 2048×3072 px con la proporción requerida. La vista previa es estática y permite revisar cada estado. **Crear y usar mascota** la añade a la galería y la activa en el escritorio. La hoja original queda copiada en `%LOCALAPPDATA%/Mindie/pets/`; el nombre, selección y descripción se conservan al reabrir la aplicación. Se mantienen los ocho compañeros incluidos.

Cada mascota personalizada tiene una **papelera** en su tarjeta de Compañeros. Tras confirmar, se elimina de la galería y se borra su hoja local; si estaba activa, se selecciona Mindie. La ventana permanece abierta y los recuerdos se conservan. Los ocho compañeros incluidos siguen disponibles.

## Configuración automática

Configuración se organiza en **Tu compañero**, **Conversación y capturas**, **Inteligencia artificial**, **Windows y voz** y **Tus datos**. Los interruptores se pueden activar desde toda su fila y con el teclado. Cada opción incluye una explicación; el tamaño muestra su valor en píxeles.

Los cambios se aplican y guardan automáticamente. Los textos se guardan tras una pausa breve al escribir y al salir del campo o de la sección; no hay botón Guardar. Los errores aparecen junto al campo y conservan el último valor válido, sin impedir guardar otras opciones. La comprobación de conexión sigue siendo una acción manual.

## Proveedores de inteligencia artificial

**Ollama local** mantiene el servidor y selector de modelos instalados con sus capacidades. **NVIDIA API** utiliza `google/gemma-4-31b-it`, con texto, imágenes y razonamiento; el dictado y la lectura de voz siguen en Windows. El selector revela los campos del proveedor elegido y guarda la elección automáticamente. No cambia de proveedor por un error, ni elimina conversaciones o recuerdos al cambiar. Las instalaciones existentes conservan Ollama hasta que el usuario elija NVIDIA.

Pega tu clave en el campo enmascarado; se guarda automáticamente tras una pausa y el campo se vacía. **Probar conexión con NVIDIA** realiza una consulta breve. **Eliminar clave** la retira del Administrador de credenciales de Windows, donde se almacena bajo `Mindie/NVIDIA`. No se incrusta en código, ejecutable, biblioteca JSON ni exportaciones; una copia del EXE en otro equipo necesita su propia clave. Tener una clave configurada no equivale a haber verificado una conexión reciente.

En NVIDIA, se envían pregunta, historial pertinente y texto/imágenes necesarios para conversar, generar tarjetas, limpiar artículos o leer PDFs. Los archivos y la biblioteca permanecen en el equipo; la inferencia se realiza en el servicio remoto y está sujeta a sus condiciones, cuotas y disponibilidad. La app indica el proveedor activo. Las preguntas a recuerdos, pantalla y PDFs mantienen las restricciones de fuente; cancelar impide guardar respuestas tardías, sin prometer anular trabajo ya iniciado en el servidor. No se ejecuta inferencia Ollama cuando se selecciona NVIDIA.

## Logo de la aplicación

El logo elegido por el usuario se conserva en **`Mindie/Assets/Logo.png`**. **`LogoMark.png`** adapta los márgenes transparentes para la barra lateral, y **`Mindie.ico`** incluye tamaños de 16 a 256 píxeles para ventanas, bandeja y ejecutable. Se retiraron las cinco propuestas anteriores y su carpeta.

Desde 0.18.1, Windows recibe además un icono explícito de anclado y relanzamiento. Mindie guarda el ICO en `%LOCALAPPDATA%/Mindie/branding/`, con nombre derivado de su contenido, y corrige solo accesos anclados que apunten a su propio ejecutable. Actualiza las entradas de ese icono en el Explorador sin reiniciarlo ni borrar la caché de otros programas. El destino y los argumentos de los accesos se conservan.

## Conversación y recuerdos

El menú principal se ordena como **Conversación**, **Historial**, **Compañeros**, **Mis guardados** y **Cerebro digital**.

### Cerebro digital

El cerebro es un **único globo de estrellas sobre fondo negro**, inspirado en la referencia visual [Astral Shell](https://pro.reactbits.dev/docs/components/astral-shell?browse=components). La implementación en C# y WPF combina un campo estelar proyectado con cámara de perspectiva y recuerdos nativos en 3D. Las dos mallas cerebrales anteriores se eliminaron. Los recuerdos aparecen como puntos destacados y sus categorías ocupan zonas de la esfera; las posiciones que ya moviste a mano se conservan. Las conexiones por afinidad se calculan localmente con términos compartidos de títulos, resúmenes e ideas. Son asociaciones para explorar, no afirmaciones de causalidad ni relaciones verificadas por la IA.

Arrastra el espacio libre para **girar** la cámara; usa **Mover**, Shift + arrastrar o el botón derecho para desplazarla. La rueda acerca o aleja y **Centrar** recupera el encuadre. Las flechas del escenario también permiten girar con clics. Arrastra un recuerdo para moverlo en el plano de la vista: sus enlaces se iluminan y las líneas punteadas señalan otros nodos cercanos durante el movimiento. La proximidad no crea una relación permanente ni cambia su categoría. Selecciona un nodo para leer el resumen, abrir el recuerdo o preguntar sobre un PDF. La búsqueda resalta coincidencias.

Las posiciones **X, Y y Z** se guardan automáticamente; las posiciones de versiones anteriores se conservan al abrirlas. **Reorganizar** devuelve los nodos a sus zonas sin borrar contenido. Mueve un nodo con los botones de dirección o Control + flechas, y cambia su profundidad con **Acercar nodo / Alejar nodo**, RePág / AvPág o Alt + rueda. Las flechas recorren los recuerdos, Enter abre el seleccionado y Escape cancela el movimiento o limpia la selección. En ventanas estrechas, **Detalles** abre un panel sobre el mapa.

El globo de ambientación gira lentamente. Puedes detenerlo con el botón de pausa dentro del escenario; seleccionar un recuerdo, arrastrar o buscar también lo pausa. Respeta Reducir movimiento de Mindie y las animaciones de Windows, y se detiene al minimizar, desactivar la ventana o salir de esta sección. Las estrellas y sus filamentos son decorativos: no se guardan como recuerdos, no se pueden seleccionar y no alteran los recuentos ni las relaciones reales.

Al guardar, editar o eliminar recuerdos, el cerebro se actualiza. La disposición nueva respeta las posiciones existentes y una categoría modificada lleva el recuerdo a su nueva zona. Una biblioteca vacía no muestra conocimientos inventados. El cálculo se realiza en segundo plano y no necesita nuevas peticiones a Gemma. La animación está limitada a 24 actualizaciones por segundo y solo afecta al ambiente; los datos permanecen estables al leerlos.

### Conversación y biblioteca

**Conversación** ofrece sugerencias que preparan el mensaje sin enviarlo, un selector **Breve / Detallada**, dictado y un cuadro de escritura con indicación de teclado. Las respuestas conservan Markdown seleccionable y tienen un botón para copiarlas. El ancho de lectura está limitado y las vistas de PDF usan el mismo diseño.

**Mis guardados** muestra como etiquetas las categorías que ya contienen recuerdos, con recuentos calculados. Puedes pulsarlas o buscarlas por nombre y combinarlas con **Favoritos**, en la misma fila. **Limpiar filtros** restaura la colección. Las tarjetas conservan apertura, preguntas al PDF, enlace, favorito y papelera, y los filtros permanecen al actualizarse la app.

### Guardar una página con menos carga

Elige **Guardar en mente → Página web: solo texto**, la opción recomendada para artículos. Pega el enlace, pulsa **Leer página**, revisa el texto y elige **Analizar texto**. Si está activado Dar contexto, escribe tu explicación antes de continuar.

Este modo descarga únicamente el documento HTML y extrae el artículo con AngleSharp, sin abrir WebView2, ejecutar JavaScript ni cargar imágenes, estilos, anuncios visuales o videos. Gemma recibe texto, sin capturas ni procesamiento de visión. Antes de generar la tarjeta, la IA identifica y retira publicidad, menús y recomendaciones de otros artículos. Decide por fragmentos: la aplicación conserva literalmente los párrafos restantes, sin reescribirlos, incluidas cifras, citas, subtítulos, excepciones y conclusiones.

El texto limpio y el enlace se guardan junto con una miniatura de 640×400. Si se retiró contenido, **Ver texto original** permite compararlo con **Ver texto limpio**, incluso después de reabrir la tarjeta. Ambas versiones se incluyen al exportar. Reanalizar un texto ya depurado reutiliza esa versión y evita repetir la limpieza; los recuerdos anteriores se depuran al volver a analizarlos. Los artículos largos se procesan por lotes, sin recortar silenciosamente el final. Si la respuesta de limpieza es inválida, se conserva el original y se informa del error.

Algunas páginas requieren JavaScript, una sesión o bloquean la lectura directa. En ese caso, copia el artículo en tu navegador y utiliza **Pegar texto**. El lector avisa si encuentra muy poco contenido; revisa siempre que esté completo. Para diagramas, fotografías o información visual utiliza Área de pantalla o Página web completa.

La descarga tiene un plazo de 30 segundos, un límite de 5 MB de HTML y hasta 250 000 caracteres de texto; se puede cancelar. No se recorta silenciosamente un artículo que supere el límite. Esta ruta evita el navegador y la inferencia visual, pero Gemma sigue necesitando memoria y tiempo de generación, especialmente con textos largos.

### Guardar un área o una página completa

Además de Solo texto, **Guardar en mente** permite elegir **Área de pantalla** o **Página web completa (full-page screenshot)**. La primera mantiene la selección manual de una región. La segunda abre una vista para pegar una URL, abrir la página y revisar su contenido antes de pulsar **Capturar todo**. Puedes interactuar con la página, cerrar avisos o iniciar sesión allí si necesitas acceder a su contenido; esta vista usa un perfil propio de Mindie y consume más recursos al conservar el diseño y las imágenes.

La captura incluye el contenido inferior y las imágenes que cargan al desplazarse. Se conserva el ancho de la vista previa y se guarda la imagen completa, junto con el título de origen y la URL. Gemma analiza capturas largas por secciones y combina sus notas antes de proponer la tarjeta: la imagen no se reduce entera a una miniatura ilegible. Si está activado **Dar contexto**, ese paso aparece antes de iniciar el análisis. El editor permite desplazarse por la captura larga y revisar el resultado antes de guardarlo en Mis guardados.

Mientras Gemma prepara **Título**, **En pocas palabras** e **Ideas clave**, el editor muestra una esfera de colores suaves en movimiento, el estado del análisis y **Cancelar análisis**. Al terminar aparecen los campos para revisarlos; cancelar o recibir un error recupera el formulario y conserva los datos anteriores. El mismo indicador acompaña la importación de PDF. **Reducir el movimiento** o desactivar las animaciones de Windows muestra la esfera estática; ocultar, minimizar o cerrar la ventana detiene sus animaciones.

La operación se puede cancelar. Las páginas infinitas o que exceden 64 millones de píxeles muestran un aviso para elegir contenido más corto o un área; no se guarda silenciosamente solo el principio. Las páginas que requieren sesión deben abrirse y autenticarse en esta vista antes de capturarse; Mindie no utiliza las sesiones del navegador externo.

### PDFs como tarjetas de conocimiento

Arrastra **un PDF al cuerpo de la mascota**, o utiliza **Mis guardados → Añadir PDF**. Mindie extrae el texto de todas las páginas y utiliza la visión de Gemma para páginas escaneadas y páginas con imágenes. Después crea una tarjeta con título, resumen e ideas y conserva una copia del PDF original en tu biblioteca. Se admiten archivos de hasta 100 MB; los documentos protegidos por contraseña deben desbloquearse antes de importarlos.

La tarjeta ofrece **Preguntar**: abre una conversación exclusiva del PDF, que se guarda para continuar después. Las preguntas recuperan fragmentos pertinentes, también de páginas finales, y Gemma cita páginas en sus respuestas. Puedes editar la tarjeta, abrir el original, marcar favoritos, exportar y eliminar el documento con su conversación. Las lecturas visuales pueden omitir detalles: comprueba cifras y citas en el original. Se utiliza el proveedor y modelo elegidos en Configuración; los pesos de IA no vienen dentro del EXE.

### Dar contexto a las capturas

Activa **Configuración → Conversación y capturas → Dar contexto antes de analizar una captura**; el cambio se guarda automáticamente. Después de seleccionar la región con **Guardar en mente**, aparece un campo para explicar qué estás viendo y qué quieres recordar. **Analizar selección** comienza únicamente después de escribir el contexto. Cancelar no analiza ni guarda la captura.

El campo no tiene límite fijo de caracteres. Si el texto supera el presupuesto de una petición, Gemma lo procesa por partes antes de combinarlo con la imagen. El contexto original se conserva completo, se puede consultar y editar al abrir la tarjeta y también se incluye en las exportaciones. La opción está desactivada inicialmente.

El campo **Enlace de origen (opcional)** permite pegar manualmente la dirección de lo que estás guardando. Está disponible con o sin Dar contexto y se puede modificar después. La tarjeta muestra **Abrir enlace** cuando tiene una dirección guardada; también se conserva al exportar. Las capturas de área no obtienen automáticamente el enlace; las páginas web conservan la URL que abriste o leíste. Reanalizar un texto guardado no vuelve a visitar su origen.

Al acercar un PDF válido a la mascota se reproduce **comer/beber**. Al soltarlo, la animación continúa unos segundos mientras comienza la importación y después pasa a pensando si Gemma sigue trabajando. La opción Reducir movimiento conserva una pose por estado.

- Escribe o dicta una pregunta. Elige respuestas cortas o más específicas.
- **Guardar en mente → Área de pantalla** oculta las ventanas de Mindie, congela cada monitor y permite arrastrar un rectángulo. `Esc` cancela.
- Gemma analiza únicamente la selección y propone título, resumen, ideas y categoría. Revisa y edita todo antes de guardar. Si falla el análisis, puedes completarlo a mano.
- La biblioteca ofrece búsqueda por contenido, categorías, favoritos, edición y eliminación con confirmación.
- Cada tarjeta tiene una **papelera** junto a favoritos. Permite eliminar el recuerdo directamente, sin entrar a editar. Tras confirmar, desaparece únicamente esa tarjeta; la biblioteca conserva la búsqueda y las ventanas abiertas permanecen visibles. Si el recuerdo estaba abierto, el editor o lector avisa del borrado y desactiva las acciones que necesitan sus archivos.
- La exportación JSON incluye metadatos y capturas originales.

Las respuestas de Gemma muestran **negritas**, *cursivas*, listas, encabezados y código con controles nativos de WPF, tanto en conversación como al preguntar sobre PDFs. El formato se aplica también al historial guardado y conserva las citas de página. Puedes seleccionar y copiar el texto; los asteriscos que pertenecen a código o texto literal se respetan.

## Atajos

| Atajo | Acción |
| --- | --- |
| `Ctrl + Shift + M` | Seleccionar un área para guardar, desde cualquier app |
| `Ctrl + Shift + Espacio` | Mostrar al animal |
| `Ctrl + K` | Buscar recuerdos dentro de Mindie |
| `Enter` / `Shift + Enter` | Enviar / nueva línea |
| `Esc` | Cancelar selección de pantalla |

Cerrar la ventana principal la oculta. La mascota y la bandeja siguen disponibles. Usa **Salir** en la bandeja o en el menú del animal para cerrar completamente Mindie.

## Voz local

Se utiliza **System.Speech desde C#**. El dictado necesita un reconocedor en español y un micrófono autorizado para apps de escritorio. Su estado aparece en Configuración. Si falta, añade el paquete en **Configuración de Windows → Hora e idioma → Voz**. El texto dictado se coloca en el campo para revisarlo antes de enviarlo.

La lectura opcional de respuestas utiliza una voz local en español instalada en Windows. No se invocan servicios de voz en la nube.

## Datos

Se guardan en **`%LOCALAPPDATA%/Mindie/`**: `library.json` contiene ajustes, historial, metadatos y mascotas propias; `captures/` conserva los PNG y miniaturas; `documents/<id>/original.pdf` y `knowledge.json` conservan cada PDF, su contenido por páginas y su conversación; `pets/` conserva las hojas importadas y `web-profile/` el perfil de navegación para capturar páginas. Los recuerdos de versiones anteriores se mantienen.

Las escrituras se serializan y reemplazan el archivo de forma atómica. Solo se permiten direcciones de loopback para Ollama. No hay captura en segundo plano. Para capturar un área se ocultan las ventanas de Mindie y la mascota también tiene exclusión nativa de captura; la captura web obtiene directamente el contenido de la página, sin incluir el widget. Se contemplan varios monitores y escalas DPI.

Los datos no están cifrados por Mindie; dependen de la protección de la cuenta y el disco. El texto de una imagen se trata como material para resumir, nunca como instrucciones del sistema. El resumen requiere revisión porque el modelo puede equivocarse.

## Compilar

La versión 0.13.0 incorpora lista virtualizada y miniaturas acotadas, previsualizaciones estáticas preparadas, relojes de animación suspendidos al ocultar y reutilización de Markdown, grafo e índice de PDFs. Consulta las medidas y sus límites en **`PERFORMANCE.md`**.

```powershell
dotnet restore Mindie/Mindie.csproj
dotnet build Mindie/Mindie.csproj -c Release
dotnet publish Mindie/Mindie.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Dependencias directas: `System.Speech`, `PdfPig` (extracción de texto), `PDFtoImage` (PDFium y SkiaSharp para renderizado por CPU), `Markdig` (interpretación de Markdown para texto nativo), `AngleSharp` (extracción de HTML sin navegador) y el SDK oficial `Microsoft.Web.WebView2` para capturas web. La interfaz sigue construida con WPF; el motor web se utiliza exclusivamente en la vista de captura visual de páginas completas. La publicación incluye `THIRD_PARTY_NOTICES.txt` con las licencias correspondientes.

Release genera `Mindie.exe` autocontenido, sin símbolos de depuración externos, acompañado del archivo de licencias. `Mindie/bin/`, `Mindie/obj/` y `test-results/` son salidas regenerables; se retiran de la entrega después de verificar el ejecutable. Los datos de la aplicación están fuera del proyecto y no forman parte de la limpieza.

Los bancos de prueba y herramientas de diagnóstico se compilan por defecto **solo en Debug**. Para validar con optimizaciones Release, publica una copia separada con `-p:IncludeDiagnostics=true -o test-results/validation-release` y ejecútala con `--self-test`. No distribuyas esa copia: el ejecutable de `artifacts/win-x64/` se publica sin `IncludeDiagnostics` y no incluye esos bancos de prueba.

## Verificar

```powershell
dotnet run --project Mindie/Mindie.csproj -- --self-test
dotnet run --project Mindie/Mindie.csproj -- --self-test --with-ollama

dotnet run --project Mindie/Mindie.csproj -- --self-test --with-web-ollama
```

Comprueba destinos locales, recorte al 150% de DPI, persistencia, ocho atlas, seis estados, transparencia, clics sobre la silueta y cambio real de frames. Con `--with-ollama` prueba chat y análisis visual reales con contenido sintético. No lee capturas personales. Los resultados, vistas y la biblioteca aislada se guardan en `test-results/native/`.

`dotnet run --project Mindie/Mindie.csproj -- --performance-probe` repite el escenario sintético de 400 recuerdos y 80 imágenes; sus métricas se guardan en `test-results/performance-after.json`. No utiliza la biblioteca personal.

## Prompt para macOS

**`PROMPT_MACOS_SWIFT_AFM.md`** contiene el prompt completo para crear Mindie en un Mac con Swift, SwiftUI/AppKit y el modelo local de Apple mediante Foundation Models. Incluye las funciones actuales, requisitos reales de Apple Intelligence, diferencias de comprensión visual/OCR según el SDK, arquitectura, rendimiento y criterios de entrega. Para reutilizar estos animales, acompáñalo con `Mindie/Assets/` y `ASSETS.md`.

La ventana inicial se ajusta al área útil y a la escala DPI del monitor para mantener visibles minimizar, maximizar y cerrar. La publicación no tiene una firma comercial; Windows puede mostrar un aviso de editor desconocido.

## Estructura

- `Mindie/Windows/`: widget, conversación rápida y editor de recuerdos.
- `Mindie/Controls/SpritePlayer.cs`: atlas y reproducción de estados.
- `Mindie/Services/`: Ollama/NVIDIA, credenciales de Windows, voz, persistencia y selección de pantalla.
- `Mindie/Views/`: compañeros, guardados, conversación y ajustes.
- `Mindie/Diagnostics/`: pruebas y vistas nativas.
- `ASSETS.md`: origen y prompts de los sprites.

Referencias: [WPF y transparencia](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-10.0), [System.Speech](https://learn.microsoft.com/en-us/dotnet/api/system.speech.recognition.speechrecognitionengine), [Ollama Chat](https://docs.ollama.com/api/chat) y [Ollama Vision](https://docs.ollama.com/capabilities/vision).


## Preguntar por la pantalla y elegir modelo

Desde **Escribir**, el botón **+** muestra **Subir imágenes** y **Qué veo en pantalla?**. La segunda opción captura y consulta el monitor completo de la mascota, ocultando primero las ventanas de Mindie. Si no has escrito una pregunta, utiliza una descripción de lo visible; si hay texto, lo interpreta como una pregunta sobre esa captura. **Escribir «qué veo» u otra frase ya no dispara capturas**. La imagen se conserva en el chat; no crea un recuerdo automáticamente. El modelo debe admitir imágenes. Reintentar reemplaza la captura previa; cerrar o cancelar detiene el envío.

Las consultas de pantalla y sus seguimientos están limitados a la última captura de ese chat. Se envía únicamente esa imagen y el historial correspondiente desde ella, con instrucciones de no completar información con conocimiento externo y respuesta estructurada: si no se puede sustentar, se muestra una explicación de falta de información.

En **Configuración → Inteligencia artificial → Ollama local → Nombre del modelo** se elige entre los modelos locales instalados. El selector muestra las capacidades que devuelve Ollama —Texto, Imágenes, Audio, Razonamiento y otras—, se actualiza al entrar o cambiar el servidor y permite **Actualizar modelos**. La elección se guarda automáticamente. Un modelo solo de embeddings no se habilita para conversar. Mostrar Audio o Razonamiento describe el modelo; el dictado continúa usando Windows y el chat conserva su configuración de respuesta breve.

## Prompt para otra implementación

**[PROMPT_TAURI_RUST_WEB.md](PROMPT_TAURI_RUST_WEB.md)** contiene el prompt completo para replicar la aplicación con Tauri 2, Rust y React/TypeScript para todo el frontend, con NVIDIA API como opción principal, Ollama como alternativa para modelos abiertos y Windows como destino inicial. Incluye mascota, barra, captura desde +, selector, biblioteca, PDFs, lectura web, grafo 3D, voz, creador de mascotas, seguridad, rendimiento y criterios de aceptación. Es una especificación, no un port ya compilado.


## Historial y preguntas sobre recuerdos

La navegación empieza con **Conversación → Historial → Compañeros → Mis guardados → Cerebro digital**. Cada apertura nueva de Escribir desde la mascota crea una conversación independiente. Volver a pulsar Escribir mientras su barra aún está abierta conserva ese borrador. Las preguntas posteriores dentro de una ventana de chat continúan en su sesión, incluso si se ha abierto otra conversación en otra ventana.

Historial permite buscar por título o contenido y **Continuar** un chat guardado. También puedes iniciar uno nuevo. La migración conserva el historial anterior como **Conversación anterior**, sin dividirlo ni borrar mensajes. Se mantienen hasta 100 mensajes por sesión, como límite del chat anterior; el número de conversaciones se conserva y el listado se muestra por bloques.

Las tarjetas de capturas y páginas web añaden **Preguntar al recuerdo**; los PDFs conservan **Preguntar al PDF**. Cada recuerdo tiene su propio chat y solo se aportan su texto, resumen, ideas, contexto y su imagen cuando procede, sin otros recuerdos ni el chat general. Las respuestas sin apoyo en la fuente muestran una explicación en lugar de completar información ajena. Eliminar el recuerdo mantiene abierta su conversación y desactiva nuevas preguntas. Las conversaciones de recuerdos no PDF también aparecen en Historial; las del lector PDF permanecen en el propio documento.


Las ocho acciones del menú lateral tienen iconos vectoriales con el mismo color de la opción activa. Cada conversación del Historial incorpora una papelera con confirmación. El borrado conserva la búsqueda, mantiene la ventana abierta y elimina únicamente los adjuntos que no utilice otro chat; no borra recuerdos de Mis guardados. Si el chat sigue abierto queda en modo de lectura y no puede reaparecer al enviar otro mensaje. Mientras la IA genera, la papelera se desactiva para evitar cambios sobre datos en preparación.
