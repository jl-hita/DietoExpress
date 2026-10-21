export interface AdherenceCheckinRecord {
  id: number;
  week_start: string;
  submitted_at: string;
  weight?: number | null;
  adherence?: number | null;
  hunger?: number | null;
  energy?: number | null;
  sleep_quality?: number | null;
  sleep_hours?: number | null;
  training?: number | null;
  difficulties?: string | null;
  notes?: string | null;
  reviewed_at?: string | null;
}

export type FollowupMetricKey = 'adherence' | 'hunger' | 'energy' | 'sleep_quality' | 'sleep_hours' | 'training' | 'weight';

export interface FollowupPeriodMetric {
  key: FollowupMetricKey;
  label: string;
  unit: string;
  currentAverage: number | null;
  previousAverage: number | null;
  difference: number | null;
  currentCount: number;
  previousCount: number;
}

export interface FollowupSignal {
  level: 'attention' | 'info';
  key: string;
  label: string;
  message: string;
  evidence: string;
}

const METRICS: Array<{ key: FollowupMetricKey; label: string; unit: string; decimals?: number }> = [
  { key: 'adherence', label: 'Adherencia', unit: '%', decimals: 0 },
  { key: 'hunger', label: 'Hambre', unit: '/10' },
  { key: 'energy', label: 'Energía', unit: '/10' },
  { key: 'sleep_quality', label: 'Calidad del sueño', unit: '/10' },
  { key: 'sleep_hours', label: 'Horas de sueño', unit: ' h' },
  { key: 'training', label: 'Actividad/entrenamiento', unit: '/10' },
  { key: 'weight', label: 'Peso', unit: ' kg' }
];

const valueOf = (item: AdherenceCheckinRecord, key: FollowupMetricKey): number | null => {
  const value = item[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
};

const average = (items: AdherenceCheckinRecord[], key: FollowupMetricKey): { value: number | null; count: number } => {
  const values = items.map(item => valueOf(item, key)).filter((value): value is number => value !== null);
  return { value: values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null, count: values.length };
};

/** Compara las últimas N revisiones con las N anteriores. Nunca convierte datos ausentes en ceros. */
export function compareFollowupPeriods(checkins: AdherenceCheckinRecord[], periodSize = 4): FollowupPeriodMetric[] {
  const size = Math.max(2, Math.min(12, Math.floor(periodSize) || 4));
  const ordered = [...checkins].filter(item => !!item.week_start)
    .sort((a, b) => a.week_start.localeCompare(b.week_start) || a.id - b.id);
  const current = ordered.slice(-size);
  const previous = ordered.slice(Math.max(0, ordered.length - size * 2), Math.max(0, ordered.length - size));

  return METRICS.map(metric => {
    const currentResult = average(current, metric.key);
    const previousResult = average(previous, metric.key);
    return {
      key: metric.key,
      label: metric.label,
      unit: metric.unit,
      currentAverage: currentResult.value,
      previousAverage: previousResult.value,
      difference: currentResult.value !== null && previousResult.value !== null
        ? currentResult.value - previousResult.value : null,
      currentCount: currentResult.count,
      previousCount: previousResult.count
    };
  });
}

/** Señales explicables a partir de tres registros válidos consecutivos, sin inferencia probabilística. */
export function detectPersistentFollowupSignals(checkins: AdherenceCheckinRecord[]): FollowupSignal[] {
  const ordered = [...checkins].filter(item => !!item.week_start)
    .sort((a, b) => a.week_start.localeCompare(b.week_start) || a.id - b.id);
  const signals: FollowupSignal[] = [];
  const rules: Array<{ key: FollowupMetricKey; label: string; direction: 'down' | 'up'; threshold: number; unit: string }> = [
    { key: 'adherence', label: 'Adherencia', direction: 'down', threshold: 15, unit: ' puntos porcentuales' },
    { key: 'hunger', label: 'Hambre', direction: 'up', threshold: 2, unit: ' puntos' },
    { key: 'energy', label: 'Energía', direction: 'down', threshold: 2, unit: ' puntos' },
    { key: 'sleep_quality', label: 'Calidad del sueño', direction: 'down', threshold: 2, unit: ' puntos' }
  ];

  for (const rule of rules) {
    const values = ordered.map(item => ({ date: item.week_start, value: valueOf(item, rule.key) }))
      .filter((item): item is { date: string; value: number } => item.value !== null)
      .slice(-3);
    if (values.length < 3) continue;
    const changes = [values[1].value - values[0].value, values[2].value - values[1].value];
    const persistent = rule.direction === 'down'
      ? changes.every(change => change < 0) && values[0].value - values[2].value >= rule.threshold
      : changes.every(change => change > 0) && values[2].value - values[0].value >= rule.threshold;
    if (!persistent) continue;
    const delta = Math.abs(values[2].value - values[0].value);
    signals.push({
      level: 'attention',
      key: rule.key,
      label: rule.label,
      message: rule.direction === 'down' ? 'Descenso persistente' : 'Aumento persistente',
      evidence: `De ${values[0].value} a ${values[2].value}${rule.unit} entre ${values[0].date} y ${values[2].date} (${delta.toFixed(1)}${rule.unit})`
    });
  }
  return signals;
}

/** Devuelve semanas completas sin revisión entre el último check-in y la semana actual. */
export function countMissedCheckinWeeks(checkins: AdherenceCheckinRecord[], today = new Date()): number {
  const dates = checkins.map(item => item.week_start).filter(Boolean).sort();
  if (!dates.length) return 0;
  const latest = new Date(`${dates[dates.length - 1]}T00:00:00Z`);
  if (Number.isNaN(latest.getTime())) return 0;
  const utcToday = new Date(Date.UTC(today.getFullYear(), today.getMonth(), today.getDate()));
  const day = (utcToday.getUTCDay() + 6) % 7;
  utcToday.setUTCDate(utcToday.getUTCDate() - day);
  const elapsedWeeks = Math.floor((utcToday.getTime() - latest.getTime()) / (7 * 24 * 60 * 60 * 1000));
  // La semana en curso sigue abierta; solo las semanas intermedias completas cuentan como omitidas.
  return Math.max(0, elapsedWeeks - 1);
}
