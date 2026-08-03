-- =============================================================================
--  SEED DATA — Sistema de Intercambios Alimentarios
--  Base de datos: dietexpressdb (PostgreSQL)
--  
--  Este script puebla:
--    1. food_exchange_groups — 9 grupos estándar del sistema español
--    2. foods — Alimentos comunes clasificados con su factor grams_per_exchange
--
--  Los valores de macros por intercambio están basados en las tablas clásicas
--  de intercambio de alimentos utilizadas en la práctica clínica española
--  (método de equivalencias / listas de intercambio).
--
--  IMPORTANTE: Ejecutar DESPUÉS del script de creación de tablas (db_migration).
--  Es idempotente: usa ON CONFLICT para evitar duplicados.
-- =============================================================================

BEGIN;

-- =============================================================================
-- PASO 1: Insertar los 9 Grupos de Intercambio Estándar
-- =============================================================================
-- Cada grupo define los macronutrientes que aporta 1 INTERCAMBIO (1 ración).
-- Ejemplo: 1 intercambio de "Frutas" = 10g HC ≈ 40 kcal.
-- =============================================================================

INSERT INTO food_exchange_groups (id, name, kcal, protein, carbs, fat) VALUES
    (1, 'Lácteos Enteros',                120.00,  6.00, 10.00, 6.00),
    (2, 'Lácteos Desnatados',              60.00,  6.00, 10.00, 0.00),
    (3, 'Verduras Grupo A (bajo HC)',      25.00,  1.00,  5.00, 0.00),
    (4, 'Verduras Grupo B (medio HC)',     50.00,  2.00, 10.00, 0.00),
    (5, 'Frutas',                          40.00,  0.00, 10.00, 0.00),
    (6, 'Féculas y Cereales',              70.00,  2.00, 15.00, 0.00),
    (7, 'Alimentos Proteicos (Magros)',    55.00,  7.00,  0.00, 2.50),
    (8, 'Alimentos Proteicos (Grasos)',    75.00,  7.00,  0.00, 5.00),
    (9, 'Grasas',                          90.00,  0.00,  0.00, 10.00)
ON CONFLICT (id) DO UPDATE SET
    name    = EXCLUDED.name,
    kcal    = EXCLUDED.kcal,
    protein = EXCLUDED.protein,
    carbs   = EXCLUDED.carbs,
    fat     = EXCLUDED.fat;

-- Sincronizar la secuencia del id para que los próximos INSERTs no colisionen
SELECT setval('food_exchange_groups_id_seq', (SELECT MAX(id) FROM food_exchange_groups));


-- =============================================================================
-- PASO 2: Insertar Alimentos de Referencia con su Factor de Intercambio
-- =============================================================================
-- Cada alimento tiene:
--   - Macros por 100g (valores aproximados de referencia BEDCA/USDA)
--   - exchange_group_id → a qué grupo de intercambio pertenece
--   - grams_per_exchange → cuántos gramos del alimento = 1 intercambio
--
-- El paciente puede elegir entre los alimentos de un mismo grupo.
-- Ejemplo: Si la dieta dice "2 intercambios de Frutas", el paciente
-- puede comer 200g de manzana O 100g de plátano O 130g de naranja.
-- =============================================================================

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 1: Lácteos Enteros  (1 int = 200 ml leche entera ≈ 120 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Leche entera',              61.0,   3.2,  4.7,  3.3, 'seed', 1, 200.00),
    ('Yogur natural entero',      62.0,   3.5,  4.0,  3.5, 'seed', 1, 200.00),
    ('Leche de cabra',            69.0,   3.6,  4.5,  4.1, 'seed', 1, 175.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 2: Lácteos Desnatados  (1 int = 200 ml leche desnatada ≈ 60 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Leche desnatada',           34.0,   3.4,  5.0,  0.1, 'seed', 2, 200.00),
    ('Yogur natural desnatado',   40.0,   4.0,  5.5,  0.1, 'seed', 2, 250.00),
    ('Queso fresco 0%',           70.0,  12.0,  3.5,  0.2, 'seed', 2, 85.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 3: Verduras Grupo A (bajo HC)  (1 int ≈ 200-300g ≈ 25 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Lechuga',                   15.0,   1.4,  1.3,  0.2, 'seed', 3, 300.00),
    ('Espinacas',                 23.0,   2.9,  1.4,  0.4, 'seed', 3, 200.00),
    ('Pepino',                    13.0,   0.7,  1.8,  0.2, 'seed', 3, 300.00),
    ('Tomate',                    22.0,   0.9,  3.5,  0.2, 'seed', 3, 150.00),
    ('Calabacín',                 16.0,   1.2,  2.2,  0.2, 'seed', 3, 250.00),
    ('Apio',                      16.0,   0.7,  1.6,  0.2, 'seed', 3, 300.00),
    ('Pimiento verde',            20.0,   0.9,  3.7,  0.2, 'seed', 3, 200.00),
    ('Champiñones',               22.0,   3.1,  0.5,  0.3, 'seed', 3, 200.00),
    ('Berenjena',                 25.0,   1.0,  3.5,  0.2, 'seed', 3, 200.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 4: Verduras Grupo B (medio HC)  (1 int ≈ 100-200g ≈ 50 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Zanahoria',                 41.0,   0.9,  7.6,  0.2, 'seed', 4, 150.00),
    ('Remolacha',                 43.0,   1.6,  7.6,  0.2, 'seed', 4, 130.00),
    ('Cebolla',                   40.0,   1.1,  7.6,  0.1, 'seed', 4, 130.00),
    ('Alcachofa',                 47.0,   3.3,  5.1,  0.2, 'seed', 4, 200.00),
    ('Guisantes frescos',         81.0,   5.4, 11.3,  0.4, 'seed', 4, 100.00),
    ('Judías verdes',             31.0,   1.8,  4.2,  0.1, 'seed', 4, 250.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 5: Frutas  (1 int ≈ 10g HC ≈ 40 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Manzana',                   52.0,   0.3, 11.4,  0.2, 'seed', 5, 100.00),
    ('Pera',                      57.0,   0.4, 12.1,  0.1, 'seed', 5,  90.00),
    ('Naranja',                   47.0,   0.9,  9.4,  0.1, 'seed', 5, 130.00),
    ('Plátano',                   89.0,   1.1, 20.2,  0.3, 'seed', 5,  50.00),
    ('Fresa',                     33.0,   0.7,  5.5,  0.3, 'seed', 5, 200.00),
    ('Melocotón',                 39.0,   0.9,  8.0,  0.3, 'seed', 5, 130.00),
    ('Sandía',                    30.0,   0.6,  7.6,  0.2, 'seed', 5, 150.00),
    ('Melón',                     34.0,   0.8,  7.3,  0.2, 'seed', 5, 150.00),
    ('Uva',                       69.0,   0.7, 17.2,  0.2, 'seed', 5,  60.00),
    ('Kiwi',                      61.0,   1.1, 10.6,  0.5, 'seed', 5, 100.00),
    ('Piña',                      50.0,   0.5, 11.8,  0.1, 'seed', 5, 100.00),
    ('Mandarina',                 53.0,   0.8, 11.5,  0.3, 'seed', 5, 100.00),
    ('Ciruela',                   46.0,   0.7,  9.6,  0.3, 'seed', 5, 110.00),
    ('Cereza',                    63.0,   1.1, 12.2,  0.2, 'seed', 5,  80.00),
    ('Mango',                     60.0,   0.8, 13.1,  0.4, 'seed', 5,  80.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 6: Féculas y Cereales  (1 int ≈ 15g HC ≈ 70 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Arroz blanco (crudo)',     354.0,   6.7, 78.9,  0.6, 'seed', 6,  20.00),
    ('Arroz integral (crudo)',   350.0,   7.5, 74.0,  2.7, 'seed', 6,  20.00),
    ('Pan blanco',               265.0,   9.0, 49.0,  3.2, 'seed', 6,  30.00),
    ('Pan integral',             247.0,  13.0, 41.3,  3.4, 'seed', 6,  35.00),
    ('Pasta (cruda)',            352.0,  12.5, 72.2,  1.5, 'seed', 6,  20.00),
    ('Patata',                    77.0,   2.0, 17.5,  0.1, 'seed', 6,  85.00),
    ('Boniato / Batata',          86.0,   1.6, 20.1,  0.1, 'seed', 6,  75.00),
    ('Avena (copos)',            375.0,  12.5, 60.0,  7.1, 'seed', 6,  25.00),
    ('Maíz (grano cocido)',      96.0,   3.4, 19.0,  1.2, 'seed', 6,  80.00),
    ('Cuscús (crudo)',           376.0,  12.8, 77.4,  0.6, 'seed', 6,  20.00),
    ('Quinoa (cruda)',           368.0,  14.1, 64.2,  6.1, 'seed', 6,  25.00),
    ('Lentejas (crudas)',        352.0,  24.6, 48.8,  1.1, 'seed', 6,  30.00),
    ('Garbanzos (crudos)',       364.0,  19.3, 46.2,  6.0, 'seed', 6,  30.00),
    ('Judías blancas (crudas)',  333.0,  21.4, 47.0,  1.5, 'seed', 6,  30.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 7: Alimentos Proteicos Magros  (1 int ≈ 7g prot ≈ 55 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Pechuga de pollo',        165.0,  31.0,  0.0,  3.6, 'seed', 7,  25.00),
    ('Pechuga de pavo',         157.0,  30.0,  0.0,  3.0, 'seed', 7,  25.00),
    ('Merluza',                  89.0,  17.0,  0.0,  2.0, 'seed', 7,  40.00),
    ('Bacalao fresco',           82.0,  18.0,  0.0,  0.7, 'seed', 7,  40.00),
    ('Lubina',                   97.0,  18.4,  0.0,  2.0, 'seed', 7,  40.00),
    ('Dorada',                   96.0,  20.0,  0.0,  1.2, 'seed', 7,  35.00),
    ('Gambas / Langostinos',     99.0,  24.0,  0.0,  0.3, 'seed', 7,  30.00),
    ('Claras de huevo',          52.0,  11.0,  0.7,  0.2, 'seed', 7,  65.00),
    ('Atún al natural (lata)',  116.0,  26.0,  0.0,  1.0, 'seed', 7,  30.00),
    ('Ternera magra (solomillo)',143.0,  26.0,  0.0,  3.5, 'seed', 7,  30.00),
    ('Conejo',                  136.0,  21.0,  0.0,  5.5, 'seed', 7,  35.00),
    ('Lomo de cerdo',           143.0,  27.0,  0.0,  3.0, 'seed', 7,  25.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 8: Alimentos Proteicos Grasos  (1 int ≈ 7g prot + 5g grasa ≈ 75 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Huevo entero (mediano)',  155.0,  13.0,  1.1, 11.0, 'seed', 8,  50.00),
    ('Salmón fresco',          208.0,  20.0,  0.0, 13.0, 'seed', 8,  35.00),
    ('Sardinas (frescas)',     208.0,  25.0,  0.0, 11.5, 'seed', 8,  30.00),
    ('Queso semicurado',       370.0,  26.0,  0.5, 29.0, 'seed', 8,  25.00),
    ('Queso curado (manchego)',467.0,  32.0,  0.0, 37.0, 'seed', 8,  20.00),
    ('Jamón serrano',          241.0,  31.0,  0.0, 13.0, 'seed', 8,  25.00),
    ('Caballa',                205.0,  19.0,  0.0, 13.9, 'seed', 8,  35.00),
    ('Trucha',                 148.0,  20.8,  0.0,  6.6, 'seed', 8,  40.00)
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- GRUPO 9: Grasas  (1 int = 10g grasa ≈ 90 kcal)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO foods (name, kcal, protein, carbs, fat, source, exchange_group_id, grams_per_exchange) VALUES
    ('Aceite de oliva virgen extra', 884.0, 0.0,  0.0, 100.0, 'seed', 9, 10.00),
    ('Aceite de girasol',            884.0, 0.0,  0.0, 100.0, 'seed', 9, 10.00),
    ('Mantequilla',                  717.0, 0.9,  0.1,  81.0, 'seed', 9, 12.00),
    ('Aguacate',                     160.0, 2.0,  2.0,  15.0, 'seed', 9, 65.00),
    ('Nueces',                       654.0,15.2,  7.0,  65.2, 'seed', 9, 15.00),
    ('Almendras',                    579.0,21.2,  9.1,  49.9, 'seed', 9, 20.00),
    ('Aceitunas',                    115.0, 0.8,  3.8,  11.0, 'seed', 9, 90.00),
    ('Semillas de lino',             534.0,18.3, 28.9,  42.2, 'seed', 9, 25.00),
    ('Crema de cacahuete (natural)', 588.0,25.1, 20.0,  50.4, 'seed', 9, 20.00),
    ('Mayonesa',                     680.0, 1.0,  0.6,  75.0, 'seed', 9, 13.00)
ON CONFLICT DO NOTHING;


COMMIT;

-- =============================================================================
-- VERIFICACIÓN (ejecutar para comprobar que todo está correcto)
-- =============================================================================
-- SELECT g.name AS grupo, COUNT(f.id) AS alimentos
-- FROM food_exchange_groups g
-- LEFT JOIN foods f ON f.exchange_group_id = g.id
-- GROUP BY g.id, g.name
-- ORDER BY g.id;
