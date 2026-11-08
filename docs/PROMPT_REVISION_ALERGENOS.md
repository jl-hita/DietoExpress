> **Nota (obsoleto en parte):** este prompt se escribió cuando los alérgenos curados vivían en `food_culinary_profile.curated_allergens`
> y se promovían con `scripts/promote-curated-allergens.sql`. Ambos se retiraron: la curación ahora está en `food_dietary_profile`
> (`FoodCuration/food-dietary-profiles.json`). El criterio de revisión sigue siendo válido; las referencias a esas piezas no.

# Tarea: revisar y completar los datos de alérgenos del catálogo de alimentos (DietoExpress)

## Contexto
DietoExpress genera dietas automáticas. La tabla `foods` (PostgreSQL, BD `dietexpressdb`) contiene
alimentos importados de BEDCA, OpenFoodFacts y USDA, con `allergen_data_status = 'unknown'` en casi todos.
Por seguridad clínica, un paciente con alergias declaradas solo recibe alimentos con
`allergen_data_status = 'reported'`.

La tabla `food_culinary_profile` (1 fila por alimento, PK `food_id`) guarda una PROPUESTA de alérgenos en
`curated_allergens text[]`, deducida por la identidad del alimento. Tu trabajo es revisarla y corregirla
fila a fila. NO decides la promoción final: eso lo hace un humano.

## Reglas de seguridad (obligatorias)
1. No ejecutes `scripts/promote-curated-allergens.sql` ni cambies `foods.allergen_data_status`,
   `foods.allergen_tags` o `allergens_reviewed`. Eso es solo del responsable humano.
2. Ante la duda, AÑADE el alérgeno (falso positivo = alimento descartado; falso negativo = riesgo para el paciente).
3. Si un alimento es un producto elaborado o de marca, o su composición es incierta, no lo clasifiques:
   márcalo para revisión humana (`needs_human`) en lugar de adivinar.
4. Trabaja en modo solo lectura sobre `foods`. Las únicas escrituras permitidas son en `food_culinary_profile`
   (`curated_allergens`, `origin='manual'`, `excluded_reason`), y solo si el usuario te lo autoriza explícitamente.
   Por defecto produce un CSV/JSON de propuestas y no escribas nada en la BD.
5. No inventes datos nutricionales ni modifiques kcal/macros.

## Vocabulario permitido (UE-14), exactamente estas cadenas
`gluten`, `crustaceos`, `huevo`, `pescado`, `cacahuete`, `soja`, `leche`, `frutos secos`, `apio`,
`mostaza`, `sesamo`, `sulfitos`, `altramuces`, `moluscos`.
Lista vacía `{}` = "revisado, sin alérgenos por identidad del alimento". No uses sinónimos ni inglés.

## Criterios por tipo de alimento
- Cereales con gluten (trigo, cebada, centeno, avena, espelta, sémola, cuscús, seitán, pan, pasta, muesli): `gluten`.
- Pasta al huevo: `gluten,huevo`. Pan: `gluten` (valora si lleva soja, sésamo o leche en su receta habitual).
- Lácteos (leche, yogur, queso, kéfir, cuajada, requesón, nata, mantequilla): `leche`.
- Huevos, tortillas, mayonesa, flan: `huevo`.
- Pescados: `pescado`. Gambas, langostinos, cigalas, cangrejo: `crustaceos`.
  Calamar, sepia, pulpo, mejillón, almeja, berberecho: `moluscos`.
- Tofu, tempeh, edamame, bebida de soja, harina de soja: `soja`.
- Almendra, nuez, avellana, pistacho, anacardo, pecana, macadamia: `frutos secos`.
  Cacahuete: `cacahuete` (es una legumbre, no un fruto seco).
- Apio, apionabo y mezclas que lo incluyan: `apio`. Mostaza y salsas con mostaza: `mostaza`.
- Sésamo, tahini, gomasio: `sesamo`. Vino, vinagre, frutas desecadas, conservas: valora `sulfitos`.
- Altramuz/lupino: `altramuces`.
- Alimentos compuestos (guisos, salsas, platos preparados): lista TODOS los alérgenos de la receta habitual
  y márcalos `needs_human`.
- Carne, verdura y fruta frescas, aceites, arroz, patata, legumbres sin procesar: normalmente `{}`.
  Ojo con trazas en productos envasados.

## Procedimiento
1. Obtén los alimentos a revisar (solo lectura). La consulta devuelve ~300-1000 filas:
   ```sql
   SELECT f.id, f.name, f.category, f.source, f.allergen_data_status, f.allergen_tags,
          p.role, p.family, p.curated_allergens, p.excluded_reason
   FROM foods f LEFT JOIN food_culinary_profile p ON p.food_id = f.id
   WHERE f.source = 'bedca' OR p.food_id IS NOT NULL
   ORDER BY p.role, f.name;
   ```
2. Procesa en lotes de 50 filas. Para cada una decide:
   - `confirmed`: la propuesta es correcta tal cual.
   - `corrected`: tu lista corregida de `curated_allergens` (con motivo de una línea).
   - `needs_human`: duda razonable, producto compuesto o dato incoherente.
   - `exclude`: no apto para dietas automáticas (con `excluded_reason`).
3. Devuelve un CSV con columnas:
   `food_id;nombre;propuesta_actual;decision;alergenos_finales;motivo;confianza(alta|media|baja)`
4. Al terminar entrega un resumen: nº confirmados / corregidos / needs_human / excluidos, y la lista de
   los 20 casos de mayor riesgo (los que más cambiarías de "sin alérgenos" a "con alérgenos").

## Criterios de calidad
- Cada fila debe tener una decisión; no omitas ninguna.
- No marques `confianza=alta` si no estás seguro de la receta habitual.
- No supongas contaminación cruzada inexistente, pero señala productos envasados donde "puede contener trazas"
  sea previsible (pan, pasta, muesli, fiambres).
- Si un nombre es ambiguo (p. ej. "Lentejas", "Arroz integral" de marca), pide aclaración en lugar de decidir.

## Persistencia en el bootstrap (obligatorio al terminar cada revisión)

Los datos de alérgenos NO pueden quedarse solo en la BD de quien hace la revisión: una instalación nueva
(o una restauración) debe obtenerlos al arrancar, igual que el catálogo BEDCA se importa si no existe.
Por eso, cada vez que se complete una revisión, el agente debe **añadir sus resultados al código del bootstrap**
y no limitarse a entregar el CSV.

### Cómo hacerlo
1. Guarda los resultados como datos versionados en el repositorio, en un fichero de semilla propio
   (p. ej. `Anguloso.Server/Logica/FoodAllergenCurationSeed.cs`, o un JSON embebido junto a él).
   Una entrada por alimento revisado, con estos campos:
   - `ExternalId` y `Name`: clave estable del alimento. **No uses `foods.id`**, porque cambia entre instalaciones.
     Si un alimento no tiene `external_id`, usa `Source` + nombre normalizado.
   - `Allergens`: lista final con el vocabulario UE-14 de este documento (`[]` = revisado sin alérgenos).
   - `Decision`: `confirmed`, `corrected`, `needs_human` o `exclude`, con `Reason` y `Confidence`.
   - `Approved`: `false` por defecto. Solo un responsable humano lo pone a `true`.
   - `CurationVersion`: número entero que se incrementa en cada revisión.
2. El bootstrap debe aplicarlo de forma **idempotente** tras importar BEDCA y sembrar `food_culinary_profile`
   (el sitio natural es `FoodCulinaryProfileSeeder`, que se llama desde `Program.cs`):
   - Busca cada alimento por su clave estable y actualiza `food_culinary_profile.curated_allergens`,
     `excluded_reason` y `origin = 'manual'`.
   - Si la fila ya tiene `origin = 'manual'` y una `CurationVersion` igual o mayor, no la toca.
   - Solo para entradas con `Approved = true` promueve el dato a `foods` (`allergen_tags`,
     `allergen_data_status = 'reported'`, `allergen_data_source = 'curated-profile-vN'`,
     `allergen_data_checked_at`). Es lo mismo que hace `scripts/promote-curated-allergens.sql`, pero reproducible.
   - Nunca debe degradar un alimento que ya esté en `reported` con datos de otra fuente (p. ej. OpenFoodFacts).
   - Registra en el log cuántos alimentos ha creado, actualizado, saltado y promovido.
3. Añade o actualiza tests que comprueben, como mínimo:
   - que aplicar la semilla dos veces da el mismo resultado;
   - que una entrada `Approved = false` no cambia `foods.allergen_data_status`;
   - que cada alérgeno de la semilla pertenece al vocabulario permitido;
   - que un alimento con alérgeno `X` es excluido para un paciente con alergia a `X`.
4. En tu informe final indica el número de entradas de la semilla, la `CurationVersion` y cuántas quedan
   con `Approved = false`, que son las que esperan revisión humana.

### Lo que el agente nunca debe hacer
- Poner `Approved = true` por su cuenta.
- Sobrescribir datos de alérgenos `reported` de otra fuente.
- Escribir directamente en la BD de producción: la vía de entrega es el código del bootstrap, revisado en un PR.

> Nota: a fecha de este documento, la semilla y su aplicación en el bootstrap aún no existen. Solo existen la tabla
> `food_culinary_profile`, su seeder por clasificación automática y el script `scripts/promote-curated-allergens.sql`.
> La primera revisión debe crear primero ese mecanismo y después rellenarlo.
