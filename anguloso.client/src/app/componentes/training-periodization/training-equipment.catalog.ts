/**
 * Vocabulario cerrado de material que entiende el generador (TrainingProgramGenerator.HasRequiredEquipment).
 * El código es el valor que se envía al servidor; la etiqueta es solo para mostrar.
 */
export interface EquipmentOption { code: string; label: string; group: string; hint?: string; }

export const EQUIPMENT_GROUPS = ['Pesos libres', 'Bancos y estructuras', 'Máquinas y poleas', 'Resistencias y accesorios'] as const;

export const EQUIPMENT_OPTIONS: readonly EquipmentOption[] = [
  { code: 'barbell', label: 'Barra', group: 'Pesos libres' },
  { code: 'ez-bar', label: 'Barra Z', group: 'Pesos libres' },
  { code: 'trap-bar', label: 'Barra hexagonal', group: 'Pesos libres' },
  { code: 'dumbbell', label: 'Mancuernas', group: 'Pesos libres' },
  { code: 'kettlebell', label: 'Pesa rusa', group: 'Pesos libres' },
  { code: 'weight-plate', label: 'Discos', group: 'Pesos libres' },
  { code: 'bench', label: 'Banco', group: 'Bancos y estructuras' },
  { code: 'pull-up-bar', label: 'Barra de dominadas', group: 'Bancos y estructuras' },
  { code: 'dip-station', label: 'Paralelas / fondos', group: 'Bancos y estructuras' },
  { code: 'rings', label: 'Anillas', group: 'Bancos y estructuras' },
  { code: 'glute-ham-developer', label: 'Banco glúteo-isquio', group: 'Bancos y estructuras' },
  { code: 'cable', label: 'Poleas', group: 'Máquinas y poleas' },
  { code: 'machine', label: 'Máquinas guiadas (todas)', group: 'Máquinas y poleas', hint: 'Incluye también las máquinas específicas de esta lista.' },
  { code: 'smith-machine', label: 'Multipower (Smith)', group: 'Máquinas y poleas' },
  { code: 'leg-press-machine', label: 'Prensa de piernas', group: 'Máquinas y poleas' },
  { code: 'hack-squat-machine', label: 'Sentadilla hack', group: 'Máquinas y poleas' },
  { code: 'leg-curl-machine', label: 'Curl femoral', group: 'Máquinas y poleas' },
  { code: 'leg-extension-machine', label: 'Extensión de cuádriceps', group: 'Máquinas y poleas' },
  { code: 'resistance-band', label: 'Bandas elásticas', group: 'Resistencias y accesorios' },
  { code: 'suspension-trainer', label: 'Suspensión (TRX)', group: 'Resistencias y accesorios' },
  { code: 'medicine-ball', label: 'Balón medicinal', group: 'Resistencias y accesorios' },
  { code: 'stability-ball', label: 'Fitball', group: 'Resistencias y accesorios' },
  { code: 'ab-wheel', label: 'Rueda abdominal', group: 'Resistencias y accesorios' },
  { code: 'sled', label: 'Trineo', group: 'Resistencias y accesorios' },
  { code: 'sandbag', label: 'Saco de arena', group: 'Resistencias y accesorios' },
  { code: 'battle-rope', label: 'Cuerda de batalla', group: 'Resistencias y accesorios' },
  { code: 'wrist-roller', label: 'Rodillo de muñeca', group: 'Resistencias y accesorios' }
];

/** Mismo criterio que el servidor: minúsculas y guiones (los guiones bajos y los espacios son equivalentes). */
export function normalizeEquipmentCode(value: string): string {
  return value.trim().toLowerCase().replace(/[_\s]+/g, '-');
}

/** Convierte el texto separado por comas (formato histórico y del servidor) en códigos únicos. */
export function parseEquipmentCsv(csv: string | null | undefined): string[] {
  return [...new Set((csv ?? '').split(',').map(normalizeEquipmentCode).filter(Boolean))];
}

export function equipmentLabel(code: string): string {
  return EQUIPMENT_OPTIONS.find(option => option.code === code)?.label ?? code;
}

export function equipmentSummary(csv: string | null | undefined): string {
  const codes = parseEquipmentCsv(csv);
  if (!codes.length) return 'Todo el material (sin restricciones)';
  const labels = codes.map(equipmentLabel);
  return labels.length <= 3 ? labels.join(', ') : `${labels.slice(0, 3).join(', ')} y ${labels.length - 3} más`;
}
