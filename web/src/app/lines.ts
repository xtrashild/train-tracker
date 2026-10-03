const LINE_COLORS: Record<string, string> = {
  RE1: '#1f5fbf',
  RE50: '#7a3fb3',
  S1: '#0f8b8d',
};

const FALLBACK_COLOR = '#5b6770';

export function lineColor(lineId: string): string {
  return LINE_COLORS[lineId] ?? FALLBACK_COLOR;
}