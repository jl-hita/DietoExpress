// ────────────────────────────────────────────────────────────────────────────
// BoolMensaje — respuesta genérica de éxito/error del backend
// Equivale a BoolMensaje.cs en el servidor
// ────────────────────────────────────────────────────────────────────────────

export interface BoolMensaje {
  exito: boolean;
  mensaje: string;
}
